using Dalamud.Game.Network.Structures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Steward.Core;
using System.Collections.Concurrent;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IMarketBoard market;
    private readonly InventoryView view;
    private readonly Configuration config;
    private readonly NativeUi ui;
    private readonly Action<string> history;
    private readonly Func<Stock,Decision> judge;
    private readonly Func<Stock,Decision> previewJudge;
    private readonly Action refreshProtection;
    private readonly Func<bool> marketPreferencesReady;
    private readonly Action<string> fault;
    private readonly Action<uint> protectCollection;
    private readonly Action<string> notify;
    private ulong manualControl;
    private long nextControlRelease;
    private readonly ConcurrentQueue<(DateTimeOffset At,int Request,Offering[] Offers)> packets=new();
    private readonly Dictionary<uint,(DateTimeOffset At,int Request,List<Offering> Offers)> offers=[];
    private uint comparisonItem;
    private DateTimeOffset comparisonOpened;
    public OperationQueue Queue { get; }
    public MarketQueue(IDalamudPluginInterface pi,IMarketBoard market,InventoryView view,Configuration config,NativeUi ui,Func<Stock,Decision> judge,Action<string> history,Action refreshProtection,Func<bool> marketPreferencesReady,Action<string> fault,Action<string> begin,Action complete,Action<uint> protectCollection,Action<string> notify,Func<Stock,Decision> previewJudge)
    {this.pi=pi;this.market=market;this.view=view;this.config=config;this.ui=ui;this.judge=judge;this.history=history;this.refreshProtection=refreshProtection;this.marketPreferencesReady=marketPreferencesReady;this.fault=fault;this.protectCollection=protectCollection;this.notify=notify;this.previewJudge=previewJudge;Queue=new(begin,complete);market.OfferingsReceived+=Received;}
    private void CheckFault(){if(Queue.Status.StartsWith("Stopped:",StringComparison.Ordinal))fault(Queue.Status);}
    private bool AcquireManualCoordination()
    {
        try
        {
            if(manualControl!=0 || !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.AcquireControl").InvokeFunc(view.Character))
            {history("AutoRetainer control is unavailable or busy; work deferred");return false;}
            manualControl=view.Character;return true;
        }
        catch(Exception ex){history("AutoRetainer coordination unavailable: "+ex.Message);return false;}
    }
    private void Received(IMarketBoardCurrentOfferings packet)
    {
        packets.Enqueue((DateTimeOffset.UtcNow,packet.RequestId,
            packet.ItemListings.Select(x=>new Offering(x.ItemId%1000000,x.IsHq,x.PricePerUnit,x.ItemQuantity,x.RetainerId,x.ListingId)).ToArray()));
        while(packets.Count>64)packets.TryDequeue(out _);
    }
    private void DrainPackets()
    {
        if(observationWorld!=view.World)
        {observationWorld=view.World;observationWorldSince=DateTimeOffset.UtcNow;observedMarkets.Clear();}
        while(packets.TryDequeue(out var packet))
        foreach(var group in packet.Offers.GroupBy(x=>x.ItemId))
        {
            ObserveMarket(group.Key,packet.At,packet.Request,group.ToList());
            if(group.Key!=comparisonItem || packet.At<comparisonOpened)continue;
            var values=group.ToList();
            if(offers.TryGetValue(group.Key,out var old)&&old.Request==packet.Request&&packet.At-old.At<TimeSpan.FromSeconds(20))values.AddRange(old.Offers);
            offers[group.Key]=(packet.At,packet.Request,values.GroupBy(value=>value.ListingId).Select(g=>g.First()).ToList());
        }
        UpdateObservedMarkets();
    }
    private static bool ComparisonReady(uint item,DateTimeOffset opened,(DateTimeOffset At,int Request,List<Offering> Offers) result)
    {
        var proxy=FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyItemSearch.Instance();
        return proxy!=null && result.Offers.All(o=>o.ListingId!=0&&o.RetainerId!=0&&o.Quantity>0&&o.UnitPrice>0) && MarketComparison.IsCurrent(item,proxy->SearchItemId,result.Request,proxy->CurrentRequestId,
            proxy->WaitingForListings,result.Offers.Count,proxy->ListingCount,result.At,opened,DateTimeOffset.UtcNow);
    }
    private void RecordGameQuotes(uint item,(DateTimeOffset At,int Request,List<Offering> Offers) result)
    {
        foreach(var quality in result.Offers.Where(x=>x.RetainerId!=0&&!config.OwnRetainers.Contains(x.RetainerId)).GroupBy(x=>x.Hq))
            view.Evidence.Prices[(item,quality.Key)]=new(item,quality.Key,view.World,quality.Min(x=>x.UnitPrice),result.At,"Game");
    }
    public void Start(bool coordinated=false)
        =>Plan(()=>StartCore(coordinated));
    private void Plan(Action build)
    {
        try{Queue.Plan(build);}
        finally{try{CheckFault();}finally{if(!Queue.Running)ReleaseManualCoordination();}}
    }
    private void StartCore(bool coordinated)
    {
        if(Queue.Running||!config.ExecutionEnabled||config.ExecutionReviewRequired||!config.Connected) return;
        refreshProtection();
        if(!marketPreferencesReady()){history("Market preferences are unavailable; market execution held");return;}
        Queue.Begin();
        if(ui.Get("RetainerSellList")==null){history("Open the retainer sell list before starting market work.");return;}
        var rm=RetainerManager.Instance();var retainer=rm==null?0:rm->LastSelectedRetainerId;
        if(retainer==0 || !view.Retainers.Contains(retainer)){history("Own retainer identity unavailable");return;}
        if(coordinated ? !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(view.Character) : !AcquireManualCoordination())return;
        List<Stock> listings=[];InventoryView.Read(InventoryType.RetainerMarket,listings);
        foreach(var listing in listings)Enqueue(listing,retainer,true,coordinated);
        EnqueueListingPump(retainer,coordinated,new ListingPass());
        if(!Queue.Running)ReleaseManualCoordination();
    }
    private void Enqueue(Stock expected,ulong retainer,bool reprice,bool coordinated,Action<Stock,bool>? completed=null)
    {
        var phase=0;var expectedWorld=view.World;var finished=false;var successful=false;
        StepResult Finish(bool success=false){finished=true;successful=success;return StepResult.Completed;}
        var opened=DateTimeOffset.MinValue;var sent=DateTimeOffset.MinValue;
        uint? target=null;uint? beforePrice=null;var quantity=0;var locked=false;
        HashSet<ushort> occupiedBefore=[];
        Queue.Add(new((reprice?"Reprice ":"List ")+expected.ItemId,()=>
        {
            var rm=RetainerManager.Instance();
            if(!config.ExecutionEnabled || view.World==0 || view.World!=expectedWorld || rm==null || rm->LastSelectedRetainerId!=retainer)return StepResult.Failed;
            if(coordinated && !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(view.Character))return StepResult.Failed;
            var live=InventoryView.ReadSlot(expected.Container,expected.Slot);
            if(!coordinated && (manualControl==0 || !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(manualControl)))return StepResult.Failed;
            if(phase<4 && live!=expected)return StepResult.Failed;
            var decision=phase<4?judge(expected):new Decision(Outcome.Wait,"Awaiting acknowledgement of an already submitted action");
            if(phase<4 && !marketPreferencesReady())return StepResult.Failed;
            var priceOnlyWait=decision is {Outcome:Outcome.Wait,Reason:"No fresh competing price; disposal is blocked"};
            var facts=phase<4?view.Facts(expected.ItemId):null;
            var saleRule=facts==null?null:config.Rules.Sales.FirstOrDefault(r=>r.Matches(facts,expected));
            if(phase<4 && (reprice ? decision.Outcome is not (Outcome.Market or Outcome.Dispose)&&!priceOnlyWait :
                facts==null||!ListingPlanner.CanConsider(facts,expected,decision,saleRule)))return Finish();
            if(phase==0)
            {
                if(ui.Get("RetainerSellList")==null || ui.Get("RetainerSell")!=null)return StepResult.Failed;
                if(!reprice&&!InventoryView.CanListStock(expected))return Finish();
                if(pi.InstalledPlugins.Any(p=>p.InternalName.Equals("Marketbuddy",StringComparison.OrdinalIgnoreCase)&&p.IsLoaded))
                {
                    if(!pi.GetIpcSubscriber<string,bool>("Marketbuddy.Lock").InvokeFunc("Steward"))return StepResult.Failed;
                    locked=true;
                }
                beforePrice=reprice?(uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)expected.Slot):null;
                if(!MarketSnapshot(out var selected,out var before)||selected!=retainer)return StepResult.Failed;
                if(!reprice&&before.Count>=20)return Finish();
                occupiedBefore=before.Select(s=>s.Slot).ToHashSet();
                opened=DateTimeOffset.UtcNow;
                AgentRetainer.Instance()->OpenRetainerSell((InventoryType)expected.Container,expected.Slot);phase=1;return StepResult.Waiting;
            }
            if(phase==1)
            {
                if(ui.Get("RetainerSell")==null)
                {
                    if(DateTimeOffset.UtcNow-opened>TimeSpan.FromSeconds(5)){history("Held listing "+expected.ItemId+": native selling window unavailable.");return Finish();}
                    return StepResult.Waiting;
                }
                offers.Clear();opened=DateTimeOffset.UtcNow;comparisonItem=expected.ItemId;comparisonOpened=opened;
                if(!ui.Click("RetainerSell",4))return StepResult.Failed;phase=2;return StepResult.Waiting;
            }
            if(phase==2)
            {
                if(!offers.TryGetValue(expected.ItemId,out var result)||!ComparisonReady(expected.ItemId,opened,result))
                {
                    if(DateTimeOffset.UtcNow-opened>TimeSpan.FromSeconds(10))
                    {history("Held market item "+expected.ItemId+": complete fresh competition unavailable; trying other source stacks.");return Finish();}
                    return StepResult.Waiting;
                }
                RecordGameQuotes(expected.ItemId,result);
                decision=judge(expected);
                var reference=ReferenceFromOffers(expected.ItemId,expected.Hq,result.At,result.Offers);
                if(!reprice)
                {
                    facts=view.Facts(expected.ItemId);saleRule=facts==null?null:config.Rules.Sales.FirstOrDefault(r=>r.Matches(facts,expected));
                    if(facts==null)return Finish();
                    var choice=ListingPlanner.Next(new(facts,expected,decision,saleRule,reference),config.Pricing,view.World,config.Rules.MaximumPriceAgeMinutes,DateTimeOffset.UtcNow);
                    if(!choice.Eligible){history("Held listing "+expected.ItemId+": "+choice.Reason);return Finish();}
                    target=choice.UnitPrice;quantity=(int)choice.Quantity;
                    decision=decision with{Outcome=Outcome.Market,Reason="Validated live listing plan",Rule=saleRule,UnitPrice=target};
                }
                else
                {
                    target=Pricing.Target(expected.ItemId,expected.Hq,beforePrice,result.Offers,config.OwnRetainers,config.Rules.Quality,config.Pricing,decision.Rule?.MinimumUnit??1);
                    if(target==null)return Finish();quantity=(int)expected.Quantity;
                    if(decision.Rule!=null&&(ulong)target.Value*(uint)quantity<decision.Rule.MinimumTotal)return Finish();
                }
                var agent=AgentRetainer.Instance();
                if(agent==null||agent->SellItemInventoryType!=(InventoryType)expected.Container||agent->SellItemInventorySlot!=expected.Slot)return StepResult.Failed;
                ui.Close("ItemSearchResult");
                if(!ui.Callback("RetainerSell",2,(int)target.Value))return StepResult.Failed;
                if(!reprice && !ui.Callback("RetainerSell",3,quantity))return StepResult.Failed;
                phase=3;return StepResult.Waiting;
            }
            if(phase==3)
            {
                var agent=AgentRetainer.Instance();
                var dialog=(FFXIVClientStructs.FFXIV.Client.UI.AddonRetainerSell*)ui.Get("RetainerSell");
                if(agent==null || dialog==null || agent->RetainerSellAddonId!=dialog->Id ||
                    agent->SellItemInventoryType!=(InventoryType)expected.Container || agent->SellItemInventorySlot!=expected.Slot ||
                    dialog->AskingPrice==null || dialog->Quantity==null || target==null)return StepResult.Failed;
                if(agent->SellItemUnitPrice!=target || agent->SellItemQuantity!=quantity ||
                    dialog->AskingPrice->Value!=target || dialog->Quantity->Value!=quantity)return StepResult.Waiting;
                if(reprice && (uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)expected.Slot)!=beforePrice)return StepResult.Failed;
                if(!offers.TryGetValue(expected.ItemId,out var result) || !ComparisonReady(expected.ItemId,opened,result))return StepResult.Failed;
                uint? latest;
                if(!reprice)
                {
                    facts=view.Facts(expected.ItemId);saleRule=facts==null?null:config.Rules.Sales.FirstOrDefault(r=>r.Matches(facts,expected));
                    if(facts==null)return StepResult.Failed;
                    var choice=ListingPlanner.Next(new(facts,expected,decision,saleRule,ReferenceFromOffers(expected.ItemId,expected.Hq,result.At,result.Offers)),
                        config.Pricing,view.World,config.Rules.MaximumPriceAgeMinutes,DateTimeOffset.UtcNow);
                    if(!choice.Eligible||choice.Quantity!=quantity)return StepResult.Failed;
                    latest=choice.UnitPrice;decision=decision with{Outcome=Outcome.Market,Reason="Validated live listing plan",Rule=saleRule,UnitPrice=latest};
                    if(!MarketSnapshot(out var selected,out var currentListings,false)||selected!=retainer||!occupiedBefore.SetEquals(currentListings.Select(s=>s.Slot)))return StepResult.Failed;
                }
                else latest=Pricing.Target(expected.ItemId,expected.Hq,beforePrice,result.Offers,config.OwnRetainers,config.Rules.Quality,config.Pricing,decision.Rule?.MinimumUnit??1);
                var maximum=decision.Rule?.MaximumStack??9999;
                if(config.Pricing.UseMaxStackSize)maximum=Math.Min(maximum,config.Pricing.MaximumStackSize);
                if(latest!=target || !MarketSubmission.CanCommit(expected,InventoryView.ReadSlot(expected.Container,expected.Slot),decision,reprice,
                    target.Value,(uint)quantity,dialog->AskingPrice->Value,dialog->Quantity->Value,maximum))return StepResult.Failed;
                if(!reprice&&!InventoryView.CanListStock(expected))return StepResult.Failed;
                if(!ui.Click("RetainerSell",21))return StepResult.Failed;
                sent=DateTimeOffset.UtcNow;phase=4;return StepResult.Waiting;
            }
            if(ui.Visible("RetainerSell") || !InventoryView.Loaded((uint)InventoryType.RetainerMarket))return StepResult.Waiting;
            if(reprice)
            {
                if(live!=expected)return StepResult.Failed;
                if((uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)expected.Slot)!=target)return StepResult.Waiting;
            }
            else
            {
                if(live!=null && live.ItemId==expected.ItemId && live.Quantity==expected.Quantity)return StepResult.Waiting;
                if(!StockTransition.Removed(expected,live,(uint)quantity,InventoryView.Loaded(expected.Container)))return StepResult.Failed;
                List<Stock> listed=[];InventoryView.Read(InventoryType.RetainerMarket,listed);
                if(listed.Count!=occupiedBefore.Count+1||!occupiedBefore.IsSubsetOf(listed.Select(s=>s.Slot)))return StepResult.Waiting;
                if(!listed.Any(s=>!occupiedBefore.Contains(s.Slot)&&s.ItemId==expected.ItemId&&s.Hq==expected.Hq&&s.Quantity==quantity&&
                    (uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)s.Slot)==target))return StepResult.Waiting;
            }
            history($"Confirmed {(reprice?"repriced":"listed")} {expected.ItemId} x{quantity} at {target} gil");return Finish(true);
        },()=>
        {
            try
            {
                var owns=coordinated?pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(view.Character):
                    manualControl!=0&&pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(manualControl);
                if(phase>0 && owns)
                {ui.Close("ItemSearchResult");ui.Close("RetainerSell");}
            }
            finally
            {
                if(phase>0)comparisonItem=0;
                if(locked && !pi.GetIpcSubscriber<string,bool>("Marketbuddy.Unlock").InvokeFunc("Steward"))
                    throw new InvalidOperationException("Market Buddy did not confirm release of Steward's lock");
            }
            if(finished)completed?.Invoke(expected,successful);
        },20000));
    }
    public void Advance(long now)
    {
        DrainPackets();
        if(Queue.Running && manualControl!=0 && !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(manualControl))
            Queue.Stop("Stopped: standalone control ownership was lost");
        if(Queue.Running)view.Refresh(view.Character);
        Queue.Advance(now);CheckFault();if(!Queue.Running)ReleaseManualCoordination();
    }
    public void Stop(){try{Queue.Stop("Stopped");}finally{ReleaseManualCoordination();}}
    private void ReleaseManualCoordination()
    {
        if(manualControl==0 || Environment.TickCount64<nextControlRelease)return;
        try
        {
            if(!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.ReleaseControl").InvokeFunc(manualControl))
                throw new InvalidOperationException("Control release was not acknowledged");
            manualControl=0;
        }
        catch(Exception ex){nextControlRelease=Environment.TickCount64+1000;Queue.Stop("Stopped: AutoRetainer control release failed: "+ex.Message);CheckFault();}
    }
    public void Dispose(){try{Stop();}finally{market.OfferingsReceived-=Received;}}
    public void ReturnToRetainerMenu()
    {ui.Close("ItemSearchResult");ui.Close("RetainerSell");ui.Close("RetainerSellList");var agent=AgentRetainer.Instance();if(agent!=null)agent->Hide();}
    public void WithdrawForOtherActions(ulong character,bool overflowOnly=false,bool collectionOnly=false)
    {
        Queue.Begin();
        var manager=RetainerManager.Instance();
        var retainer=manager==null?0:manager->LastSelectedRetainerId;
        if(retainer==0 || !view.Retainers.Contains(retainer))throw new InvalidOperationException("Withdrawal retainer identity is unavailable");
        EnqueueOverflow(character,retainer);
        if(overflowOnly)return;
        var dresserWithdrawals=0;
        var initialWaiting=view.Items.Where(s=>judge(s).Outcome==Outcome.CollectDresser).Select(s=>s.ItemId).Distinct().Count();
        List<Stock> stored=[];foreach(var type in InventoryView.RetainerTypes())InventoryView.Read(type,stored);
        foreach(var stock in stored.Where(s=>judge(s).Outcome is Outcome.CollectArmoire or Outcome.CollectDresser || !collectionOnly&&judge(s).Outcome==Outcome.Dispose)
            .OrderBy(s=>view.Facts(s.ItemId) is {} f?StoragePolicy.Priority(f,config.Rules,view.Evidence):int.MaxValue).ThenBy(s=>s.ItemId))
        {
            var sent=false;long before=0;
            Queue.Add(new("Withdraw "+stock.ItemId,()=>
            {
                if(!config.ExecutionEnabled||view.Character!=character)return StepResult.Failed;
                var currentRetainer=RetainerManager.Instance();
                if(currentRetainer==null || currentRetainer->LastSelectedRetainerId!=retainer)return StepResult.Failed;
                if(!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(character))return StepResult.Failed;
                if(!sent)
                {
                    if(!InventoryView.PlayerLoaded())return StepResult.Failed;
                    before=view.Items.Where(s=>s.ItemId==stock.ItemId&&s.Hq==stock.Hq).Sum(s=>(long)s.Quantity);
                    if(InventoryView.ReadSlot(stock.Container,stock.Slot)!=stock)return StepResult.Failed;
                    var decision=judge(stock);if(decision.Outcome is not (Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Dispose))return StepResult.Completed;
                    if(decision.Outcome==Outcome.Dispose)
                    {
                        bool selected;
                        try{selected=pi.GetIpcSubscriber<uint,uint,bool>("AutoRetainer.InventoryPolicy.DisposalSelected").InvokeFunc(stock.ItemId,stock.Quantity);}
                        catch{selected=false;}
                        if(!selected){history("Retainer item "+stock.ItemId+" left in storage: no enabled AutoRetainer disposal selection.");return StepResult.Completed;}
                    }
                    if(decision.Outcome is Outcome.CollectArmoire or Outcome.CollectDresser && view.Items.Any(s=>s.ItemId==stock.ItemId))return StepResult.Completed;
                    var waiting=Math.Max(initialWaiting,view.Items.Where(s=>judge(s).Outcome==Outcome.CollectDresser).Select(s=>s.ItemId).Distinct().Count()-dresserWithdrawals);
                    if(!StoragePolicy.CanWithdraw(decision.Outcome,view.Evidence.DresserFreeSlots,config.Rules.DresserEmptySlots,waiting,dresserWithdrawals))return StepResult.Completed;
                    if(InventoryView.FreeBagSlots()<2)return StepResult.Completed;
                    if(decision.Outcome is Outcome.CollectDresser or Outcome.CollectArmoire)protectCollection(stock.ItemId);
                    var payload=System.Text.Json.JsonSerializer.Serialize(new{stock.Container,stock.Slot,stock.ItemId,stock.Quantity,stock.Hq,Retainer=retainer});
                    if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Retrieve").InvokeFunc(character,payload))return StepResult.Failed;
                    sent=true;return StepResult.Waiting;
                }
                if(InventoryView.ReadSlot(stock.Container,stock.Slot)==stock)return StepResult.Waiting;
                if(!InventoryView.PlayerLoaded() || !InventoryView.Loaded(stock.Container))return StepResult.Failed;
                var after=view.Items.Where(s=>s.ItemId==stock.ItemId&&s.Hq==stock.Hq).Sum(s=>(long)s.Quantity);
                if(after<before+stock.Quantity)return StepResult.Waiting;
                if(after!=before+stock.Quantity || !StockTransition.Removed(stock,InventoryView.ReadSlot(stock.Container,stock.Slot),stock.Quantity,true))return StepResult.Failed;
                if(judge(stock).Outcome==Outcome.CollectDresser)dresserWithdrawals++;
                history("Confirmed withdrawal of "+stock.ItemId+" x"+stock.Quantity);return StepResult.Completed;
            },TimeoutMs:10000));
        }
    }
    public void CleanupDresser()
        =>Plan(CleanupDresserCore);
    private void CleanupDresserCore()
    {
        if(Queue.Running||!config.ExecutionEnabled||!config.CleanDresser||ui.Get("MiragePrismPrismBox")==null)return;
        var mm=MirageManager.Instance();if(mm==null||!mm->PrismBoxLoaded)return;
        if(config.ExecutionReviewRequired || !config.Connected || !AcquireManualCoordination())return;
        Queue.Begin();
        for(var index=0;index<mm->PrismBoxItemIds.Length;index++)
        {
            var raw=mm->PrismBoxItemIds[index];if(raw==0)continue;
            var pieces=view.OutfitPieces(raw%1000000);
            if(pieces!=null)
            {for(var part=0;part<pieces.Length;part++)if(pieces[part]!=0&&mm->IsSetSlotUnlocked((uint)index,part))EnqueueRestoration(raw,index,pieces[part],part);}
            else EnqueueRestoration(raw,index,raw%1000000,-1);
        }
        if(!Queue.Running)ReleaseManualCoordination();
        void EnqueueRestoration(uint raw,int slot,uint id,int outfitPart)
        {
            var facts=view.Facts(id);if(facts==null)return;
            Decision WithoutThisCopy(bool live=true)
            {
                if(live)refreshProtection();
                return StoragePolicy.WithoutDresserCopy(view.Evidence,id,view.DresserCopies(id),
                    ()=>(live?judge:previewJudge)(new(id,false,1,uint.MaxValue,(ushort)slot)));
            }
            if(!StoragePolicy.Restore(WithoutThisCopy(false).Outcome,config.CleanDresser,facts.ArmoireEligible && !view.Evidence.Armoire.Contains(id)))return;
            var sent=false;long before=0;
            Queue.Add(new("Restore dresser item "+id,()=>
            {
                var live=MirageManager.Instance();
                if(!sent && !config.CleanDresser)return StepResult.Completed;
                if(!config.ExecutionEnabled||live==null||!live->PrismBoxLoaded||ui.Get("MiragePrismPrismBox")==null)return StepResult.Failed;
                if(!sent)
                {
                    if(live->PrismBoxItemIds[slot]!=raw)return StepResult.Failed;
                    if(!StoragePolicy.Restore(WithoutThisCopy().Outcome,config.CleanDresser,facts.ArmoireEligible && !view.Evidence.Armoire.Contains(id)))return StepResult.Completed;
                    if(InventoryView.FreeBagSlots()<1){notify("Dresser cleanup paused: make one inventory slot available.");return StepResult.Completed;}
                    if(facts.ArmoireEligible && !view.Evidence.Armoire.Contains(id))protectCollection(id);
                    if(!InventoryView.PlayerLoaded())return StepResult.Failed;
                    before=view.Items.Where(s=>s.ItemId==id).Sum(s=>(long)s.Quantity);
                    if(outfitPart>=0)
                    {
                        if(!live->IsSetSlotUnlocked((uint)slot,outfitPart))return StepResult.Completed;
                        var bits=stackalloc byte[2];bits[0]=bits[1]=0;
                        bits[outfitPart/8]=(byte)(1<<(outfitPart%8));
                        if(!live->RestorePrismBoxSetItem((uint)slot,bits))return StepResult.Failed;
                    }
                    else if(!live->RestorePrismBoxItem((uint)slot))return StepResult.Failed;
                    sent=true;return StepResult.Waiting;
                }
                if(outfitPart>=0 ? live->PrismBoxItemIds[slot]==raw&&live->IsSetSlotUnlocked((uint)slot,outfitPart) : live->PrismBoxItemIds[slot]==raw)return StepResult.Waiting;
                if(!InventoryView.PlayerLoaded())return StepResult.Failed;
                var after=view.Items.Where(s=>s.ItemId==id).Sum(s=>(long)s.Quantity);
                if(after==before)return StepResult.Waiting;
                if(after!=before+1)return StepResult.Failed;
                history("Confirmed dresser restoration of "+id);return StepResult.Completed;
            },TimeoutMs:10000));
        }
    }
    public void DepositOpenDresser()
        =>Plan(DepositOpenDresserCore);
    private void DepositOpenDresserCore()
    {
        if(Queue.Running||!config.ExecutionEnabled||ui.Get("MiragePrismPrismBoxCrystallize")==null||view.Evidence.DresserFreeSlots==null)return;
        if(config.ExecutionReviewRequired || !config.Connected || !AcquireManualCoordination())return;
        Queue.Begin();
        foreach(var source in view.Items.ToArray().Where(s=>judge(s).Outcome==Outcome.CollectDresser).GroupBy(s=>s.ItemId).Select(g=>g.First()).OrderBy(s=>view.Facts(s.ItemId) is {} f?StoragePolicy.Priority(f,config.Rules,view.Evidence):int.MaxValue).ThenBy(s=>s.ItemId))
        {
            var phase=0;var opened=DateTimeOffset.MinValue;
            Queue.Add(new("Store dresser item "+source.ItemId,()=>
            {
                if(!config.ExecutionEnabled||view.Character==0||ui.Get("MiragePrismPrismBoxCrystallize")==null)return StepResult.Failed;
                var live=InventoryView.ReadSlot(source.Container,source.Slot);
                if(phase<2 && live!=source)return StepResult.Failed;
                if(phase<2 && judge(source).Outcome!=Outcome.CollectDresser)return StepResult.Completed;
                var agent=AgentMiragePrismPrismBox.Instance();
                if(agent==null||agent->Data==null)return StepResult.Failed;
                if(phase==0)
                {
                    if(ui.Get("SelectYesno")!=null)return StepResult.Failed;
                    var selected=-1;
                    for(var i=0;i<agent->Data->CrystallizeItemCount;i++)
                    {
                        var row=agent->Data->CrystallizeItems[i];
                        if(row.ItemId%1000000==source.ItemId&&(uint)row.Inventory==source.Container&&row.Slot==source.Slot){selected=i;break;}
                    }
                    if(selected<0){notify("Dresser deposit held for "+view.Facts(source.ItemId)?.Name+": the item is not available in the current dresser filter.");return StepResult.Completed;}
                    var im=InventoryManager.Instance();var inv=im->GetInventoryContainer((InventoryType)source.Container);
                    var item=inv->GetInventorySlot(source.Slot);
                    if(item->GetConditionPercentage()!=100||im->GetInventoryItemCount(21800)<1){notify("Dresser deposit needs fully repaired gear and a glamour prism. Item "+source.ItemId+" remains protected.");return StepResult.Completed;}
                    if(!ui.Callback("MiragePrismPrismBoxCrystallize",0,selected))return StepResult.Failed;
                    opened=DateTimeOffset.UtcNow;phase=1;return StepResult.Waiting;
                }
                if(phase==1)
                {
                    // Never acknowledge an unrelated confirmation. The agent must still identify our exact source.
                    var selected=agent->Data->CrystallizeSelectedItem;
                    if(selected.ItemId%1000000!=source.ItemId||(uint)selected.Inventory!=source.Container||selected.Slot!=source.Slot)return StepResult.Failed;
                    if(ui.Get("MiragePrismPrismSetConvert")!=null)return StepResult.Failed; // Hold for review; never progress through an unsupported dialog.
                    var confirm=(FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno*)ui.Get("SelectYesno");
                    if(confirm==null)return StepResult.Waiting;
                    var sourceAddon=ui.Get("MiragePrismPrismBoxCrystallize");
                    if(sourceAddon==null || !ConfirmationOwnership.Matches(sourceAddon->Id,confirm->ParentId,confirm->HostId))return StepResult.Failed;
                    var name=view.Facts(source.ItemId)?.Name;
                    if(name==null||confirm->PromptText==null||!confirm->PromptText->NodeText.ToString().Contains(name,StringComparison.Ordinal))return StepResult.Failed;
                    if(!ui.Callback("SelectYesno",0))return StepResult.Failed;phase=2;return StepResult.Waiting;
                }
                if(live==source||ui.Visible("SelectYesno"))return StepResult.Waiting;
                if(!InventoryView.Loaded(source.Container)||!view.Evidence.DresserKnown)return StepResult.Failed;
                if(!StockTransition.Removed(source,live,1,true))return StepResult.Failed;
                if(!view.Evidence.Dresser.Contains(source.ItemId))return StepResult.Waiting;
                history("Confirmed dresser storage of "+source.ItemId);return StepResult.Completed;
            },()=>
            {
                if(phase!=1)return;
                var addon=ui.Get("MiragePrismPrismBoxCrystallize");
                if(addon==null || !ui.CancelOwnedConfirmation(addon->Id))
                    throw new InvalidOperationException("Pending dresser confirmation could not be safely cancelled");
            },TimeoutMs:12000));
        }
        if(!Queue.Running)ReleaseManualCoordination();
    }
}
