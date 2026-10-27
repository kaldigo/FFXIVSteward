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
    private readonly NativeDresserStore dresserStore;
    private readonly Action<string> history;
    private readonly Action save;
    private readonly Func<Stock,ulong,bool,Decision> marketJudge;
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
    public MarketQueue(IDalamudPluginInterface pi,IMarketBoard market,InventoryView view,Configuration config,NativeUi ui,Func<Stock,Decision> judge,Action<string> history,Action refreshProtection,Func<bool> marketPreferencesReady,Action<string> fault,Action<string> begin,Action complete,Action<uint> protectCollection,Action<string> notify,Func<Stock,Decision> previewJudge,Action save,Func<Stock,ulong,bool,Decision> marketJudge,NativeDresserStore dresserStore)
    {this.dresserStore=dresserStore;this.marketJudge=marketJudge;this.save=save;this.pi=pi;this.market=market;this.view=view;this.config=config;this.ui=ui;this.judge=judge;this.history=history;this.refreshProtection=refreshProtection;this.marketPreferencesReady=marketPreferencesReady;this.fault=fault;this.protectCollection=protectCollection;this.notify=notify;this.previewJudge=previewJudge;Queue=new(begin,complete,reason=>{history("Skipped item; continuing run: "+reason);notify("Skipped item: "+reason);});market.OfferingsReceived+=Received;}
    public void ReplanPending(){Queue.ReplanPending();CheckFault();if(!Queue.Running)ReleaseManualCoordination();}
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
        {
        if(packet.Offers.Length==0)
        {
            var proxy=FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyItemSearch.Instance();
            if(proxy!=null&&proxy->SearchItemId%1000000!=0&&proxy->CurrentRequestId==packet.Request&&proxy->ListingCount==0)
            {
                var emptyItem=proxy->SearchItemId%1000000;
                ObserveMarket(emptyItem,packet.At,packet.Request,[]);
                if(comparisonItem==emptyItem&&packet.At>=comparisonOpened)offers[emptyItem]=(packet.At,packet.Request,[]);
            }
        }
        foreach(var group in packet.Offers.GroupBy(x=>x.ItemId))
        {
            ObserveMarket(group.Key,packet.At,packet.Request,group.ToList());
            if(group.Key!=comparisonItem || packet.At<comparisonOpened)continue;
            var values=group.ToList();
            if(offers.TryGetValue(group.Key,out var old)&&old.Request==packet.Request&&packet.At-old.At<TimeSpan.FromSeconds(20))values.AddRange(old.Offers);
            offers[group.Key]=(packet.At,packet.Request,values.GroupBy(value=>value.ListingId).Select(g=>g.First()).ToList());
        }
        }
        UpdateObservedMarkets();
    }
    private static bool ComparisonReady(uint item,DateTimeOffset opened,(DateTimeOffset At,int Request,List<Offering> Offers) result)
    {
        var proxy=FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyItemSearch.Instance();
        if(proxy!=null&&MarketComparison.IsConfirmedEmpty(item,proxy->SearchItemId,result.Request,proxy->CurrentRequestId,
            proxy->WaitingForListings,result.Offers.Count,proxy->ListingCount,result.At,opened,DateTimeOffset.UtcNow))return true;
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
        if(!config.SalesEnabledFor(retainer) || !view.Retainers.Contains(retainer)){history("Own retainer identity unavailable");return;}
        if(coordinated ? !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(view.Character) : !AcquireManualCoordination())return;
        List<Stock> listings=[];InventoryView.Read(InventoryType.RetainerMarket,listings);
        foreach(var listing in listings)
        {
            if(ReturnReason(listing,retainer,false)!=null)EnqueueReturn(listing,retainer,coordinated);
            else if(config.Features.Repricing)Enqueue(listing,retainer,true,coordinated);
        }
        if(config.Features.NewListings)EnqueueListingPump(retainer,coordinated,new ListingPass());
        if(!Queue.Running)ReleaseManualCoordination();
    }
    private void Enqueue(Stock expected,ulong retainer,bool reprice,bool coordinated,Action<Stock,bool>? completed=null,bool quoteOnly=false)
    {
        var phase=0;var expectedWorld=view.World;var finished=false;var successful=false;
        StepResult Finish(bool success=false){finished=true;successful=success;return StepResult.Completed;}
        var opened=DateTimeOffset.MinValue;var sent=DateTimeOffset.MinValue;
        uint? target=null;uint? beforePrice=null;var quantity=0;var locked=false;
        HashSet<ushort> occupiedBefore=[];
        Queue.Add(new((reprice?"Reprice ":"List ")+expected.ItemId,()=>
        {
            if(reprice?!config.Features.Repricing:!config.Features.NewListings)return StepResult.Failed;
            var rm=RetainerManager.Instance();
            if(!config.ExecutionEnabled || !config.SalesEnabledFor(retainer) || view.World==0 || view.World!=expectedWorld || rm==null || rm->LastSelectedRetainerId!=retainer)return StepResult.Failed;
            if(coordinated && !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(view.Character))return StepResult.Failed;
            var live=InventoryView.ReadSlot(expected.Container,expected.Slot);
            if(!coordinated && (manualControl==0 || !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(manualControl)))return StepResult.Failed;
            if(phase<4 && live!=expected)return StepResult.Failed;
            var decision=phase<4?marketJudge(expected,retainer,true):new Decision(Outcome.Wait,"Awaiting acknowledgement of an already submitted action");
            if(phase<4 && !marketPreferencesReady())return StepResult.Failed;
            var priceOnlyWait=decision is {Outcome:Outcome.Wait,Reason:"No fresh competing price; disposal is blocked"};
            var facts=phase<4?view.Facts(expected.ItemId):null;
            var saleRule=facts==null?null:MarketRule(facts,expected,retainer);
            if(phase<4 && (reprice ? saleRule==null||decision.SaleRejected||decision.Outcome is not (Outcome.Market or Outcome.Dispose)&&!priceOnlyWait :
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
                if(reprice&&config.Silver.Matches(retainer,expected.ItemId)&&beforePrice==0)return Finish();
                if(!MarketSnapshot(out var selected,out var before)||selected!=retainer)return StepResult.Failed;
                if(!reprice&&!quoteOnly&&before.Count>=20)return Finish();
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
                decision=marketJudge(expected,retainer,true);
                var reference=ReferenceFromOffers(expected.ItemId,expected.Hq,result.At,result.Offers);
                if(reference.Source.Contains("ignored ",StringComparison.Ordinal))history($"Market price safeguard for {expected.ItemId}: {reference.Source}; reference {reference.Competitor:N0} gil.");
                if(config.Silver.Matches(retainer,expected.ItemId))
                {
                    var choice=SilverChoice(expected,retainer,reprice,result.Offers,true);
                    if(!choice.Eligible)return Finish();
                    target=choice.UnitPrice;quantity=(int)choice.Quantity;
                    if(reprice&&target==beforePrice)return Finish(true);
                    decision=decision with{Outcome=Outcome.Market,Rule=config.Silver.Rule(),UnitPrice=target};
                }
                else if(!reprice)
                {
                    facts=view.Facts(expected.ItemId);saleRule=facts==null?null:MarketRule(facts,expected,retainer);
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
                if(quoteOnly)return Finish(true);
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
                if(config.Silver.Matches(retainer,expected.ItemId))
                {
                    var choice=SilverChoice(expected,retainer,reprice,result.Offers,true);
                    if(!choice.Eligible||choice.Quantity!=quantity)return StepResult.Failed;
                    latest=choice.UnitPrice;decision=decision with{Outcome=Outcome.Market,Rule=config.Silver.Rule(),UnitPrice=latest};
                    if(!reprice&&(!MarketSnapshot(out var selected,out var currentListings,false)||selected!=retainer||!occupiedBefore.SetEquals(currentListings.Select(s=>s.Slot))))return StepResult.Failed;
                }
                else if(!reprice)
                {
                    facts=view.Facts(expected.ItemId);saleRule=facts==null?null:MarketRule(facts,expected,retainer);
                    if(facts==null)return StepResult.Failed;
                    var choice=ListingPlanner.Next(new(facts,expected,decision,saleRule,ReferenceFromOffers(expected.ItemId,expected.Hq,result.At,result.Offers)),
                        config.Pricing,view.World,config.Rules.MaximumPriceAgeMinutes,DateTimeOffset.UtcNow);
                    if(!choice.Eligible||choice.Quantity!=quantity)return StepResult.Failed;
                    latest=choice.UnitPrice;decision=decision with{Outcome=Outcome.Market,Reason="Validated live listing plan",Rule=saleRule,UnitPrice=latest};
                    if(!MarketSnapshot(out var selected,out var currentListings,false)||selected!=retainer||!occupiedBefore.SetEquals(currentListings.Select(s=>s.Slot)))return StepResult.Failed;
                }
                else latest=Pricing.Target(expected.ItemId,expected.Hq,beforePrice,result.Offers,config.OwnRetainers,config.Rules.Quality,config.Pricing,decision.Rule?.MinimumUnit??1);
                var maximum=decision.Rule?.MaximumStack??9999;
                if(config.Pricing.UseMaxStackSize&&!config.Silver.Matches(retainer,expected.ItemId))maximum=Math.Min(maximum,config.Pricing.MaximumStackSize);
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
            if(!reprice)config.ListingWindows.Posted(view.Character,expected.ItemId,expected.Hq,(uint)quantity,save);
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
            Queue.Stop(Queue.HasUnverifiedAction?"Stopped: standalone control ownership was lost":"Cancelled before submission: standalone control ownership was lost");
        if(Queue.Running)refreshProtection();
        Queue.Advance(now);CheckFault();if(!Queue.Running)ReleaseManualCoordination();
    }
    public void Stop(){try{Queue.Stop("Stopped");}finally{ReleaseManualCoordination();}}
    private void ReleaseManualCoordination()
    {
        if(manualControl==0 || Environment.TickCount64<nextControlRelease)return;
        try
        {
            if(!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(manualControl))
            {manualControl=0;return;} // Never release another owner's control or keep retrying an expired lease.
            if(!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.ReleaseControl").InvokeFunc(manualControl))
                throw new InvalidOperationException("Control release was not acknowledged");
            manualControl=0;
        }
        catch(Exception ex){nextControlRelease=Environment.TickCount64+1000;Queue.Stop("Stopped: AutoRetainer control release failed: "+ex.Message);CheckFault();}
    }
    public void Dispose(){try{Stop();}finally{market.OfferingsReceived-=Received;}}
    public void ReturnToRetainerMenu()
    {ui.Close("ItemSearchResult");ui.Close("RetainerSell");ui.Close("RetainerSellList");var agent=AgentRetainer.Instance();if(agent!=null)agent->Hide();}
    public void WithdrawForOtherActions(ulong character,bool overflowOnly=false,bool collectionOnly=false,bool disposalOnly=false)
    {
        Queue.Begin();
        var manager=RetainerManager.Instance();
        var retainer=manager==null?0:manager->LastSelectedRetainerId;
        if(retainer==0 || !view.Retainers.Contains(retainer))throw new InvalidOperationException("Withdrawal retainer identity is unavailable");
        if(!config.Storage.RetainerEnabled(retainer))return;
        refreshProtection();
        if(!disposalOnly)EnqueueOverflow(character,retainer);
        if(overflowOnly)return;
        var dresserWithdrawals=0;
        var initialWaiting=StoragePolicy.CountWaitingForDresser(view.Items,previewJudge);
        List<Stock> stored=[];foreach(var type in InventoryView.RetainerTypes())InventoryView.Read(type,stored);
        bool DisposalSelected(Stock stock)
        {
            try{return pi.GetIpcSubscriber<uint,uint,bool>("AutoRetainer.InventoryPolicy.DisposalSelected").InvokeFunc(stock.ItemId,stock.Quantity);}
            catch{return false;}
        }
        var candidates=StoragePolicy.PlanWithdrawals(stored,previewJudge,config.Features.Enabled,DisposalSelected,collectionOnly,disposalOnly);
        history($"Retainer withdrawal planning: retainer={retainer}, live stacks={stored.Count}, candidates={candidates.Length}, collection only={collectionOnly}, free bag slots={InventoryView.FreeBagSlots()}, waiting dresser items={initialWaiting}");
        foreach(var stock in candidates.OrderBy(s=>view.Facts(s.ItemId) is {} f?StoragePolicy.Priority(f,config.Rules,view.Evidence):int.MaxValue).ThenBy(s=>s.ItemId))
        {
            var sent=false;var attempted=false;long before=0;
            Queue.Add(new("Withdraw "+stock.ItemId,()=>
            {
                if(!config.ExecutionEnabled||view.Character!=character||!config.Storage.RetainerEnabled(retainer))return StepResult.Failed;
                var currentRetainer=RetainerManager.Instance();
                if(currentRetainer==null || currentRetainer->LastSelectedRetainerId!=retainer)return StepResult.Failed;
                if(!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(character))return StepResult.Failed;
                if(!sent)
                {
                    if(!InventoryView.PlayerLoaded())return StepResult.Failed;
                    before=view.Items.Where(s=>s.ItemId==stock.ItemId&&s.Hq==stock.Hq).Sum(s=>(long)s.Quantity);
                    if(InventoryView.ReadSlot(stock.Container,stock.Slot)!=stock)return StepResult.Failed;
                    var decision=judge(stock);if(!config.Features.Enabled(decision.Outcome))return StepResult.Completed;
                    if(disposalOnly&&decision.Outcome!=Outcome.Dispose)return StepResult.Completed;
                    if(decision.Outcome is not (Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Dispose))return StepResult.Completed;
                    if(decision.Outcome==Outcome.Dispose)
                    {
                        bool selected;
                        try{selected=pi.GetIpcSubscriber<uint,uint,bool>("AutoRetainer.InventoryPolicy.DisposalSelected").InvokeFunc(stock.ItemId,stock.Quantity);}
                        catch{selected=false;}
                        if(!selected){history("Retainer item "+stock.ItemId+" left in storage: no enabled AutoRetainer disposal selection.");return StepResult.Completed;}
                    }
                    if(decision.Outcome is Outcome.CollectArmoire or Outcome.CollectDresser && view.Items.Any(s=>s.ItemId==stock.ItemId))return StepResult.Completed;
                    var waiting=Math.Max(initialWaiting,StoragePolicy.CountWaitingForDresser(view.Items,previewJudge)-dresserWithdrawals);
                    if(!StoragePolicy.CanWithdraw(decision.Outcome,view.Evidence.DresserFreeSlots,config.Rules.DresserEmptySlots,waiting,dresserWithdrawals))return StepResult.Completed;
                    if(InventoryView.FreeBagSlots()<2)return StepResult.Completed;
                    if(view.IsUnique(stock.ItemId)&&view.Items.Any(s=>s.ItemId==stock.ItemId))return StepResult.Completed;
                    if(decision.Outcome is Outcome.CollectDresser or Outcome.CollectArmoire)protectCollection(stock.ItemId);
                    var payload=System.Text.Json.JsonSerializer.Serialize(new{stock.Container,stock.Slot,stock.ItemId,stock.Quantity,stock.Hq,Retainer=retainer});
                    attempted=true;
                    if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Retrieve").InvokeFunc(character,payload)){attempted=false;return StepResult.Failed;}
                    sent=true;return StepResult.Waiting;
                }
                if(InventoryView.ReadSlot(stock.Container,stock.Slot)==stock)return StepResult.Waiting;
                if(!InventoryView.PlayerLoaded() || !InventoryView.Loaded(stock.Container))return StepResult.Failed;
                var after=view.Items.Where(s=>s.ItemId==stock.ItemId&&s.Hq==stock.Hq).Sum(s=>(long)s.Quantity);
                if(after<before+stock.Quantity)return StepResult.Waiting;
                if(after!=before+stock.Quantity || !StockTransition.Removed(stock,InventoryView.ReadSlot(stock.Container,stock.Slot),stock.Quantity,true))return StepResult.Failed;
                if(judge(stock).Outcome==Outcome.CollectDresser)dresserWithdrawals++;
                if(disposalOnly&&!pi.GetIpcSubscriber<ulong,ulong,uint,bool>("AutoRetainer.InventoryPolicy.CleanupWithdrawalConfirmed").InvokeFunc(character,retainer,stock.Quantity))
                    return StepResult.Failed;
                history($"Confirmed withdrawal of {stock.ItemId} x{stock.Quantity}; retainer={retainer}, source={stock.Container}/{stock.Slot}, player count={before}->{after}");return StepResult.Completed;
            },TimeoutMs:10000,CanSkip:()=>!attempted));
        }
    }
    public void CleanupDresser()
        =>Plan(CleanupDresserCore);
    private void CleanupDresserCore()
    {
        if(Queue.Running||!config.ExecutionEnabled||!config.CleanDresser||ui.Get("MiragePrismPrismBox")==null)return;
        var mm=MirageManager.Instance();if(mm==null||!mm->PrismBoxRequested||!mm->PrismBoxLoaded)return;
        if(config.ExecutionReviewRequired || !config.Connected || !AcquireManualCoordination())return;
        Queue.Begin();
        var cleanupCharacter=view.Character;
        List<string> retained=[];var planned=0;
        for(var index=0;index<mm->PrismBoxItemIds.Length;index++)
        {
            var raw=mm->PrismBoxItemIds[index];if(raw==0)continue;
            var pieces=view.OutfitPieces(raw%1000000);
            if(pieces!=null)
            {for(var part=0;part<pieces.Length;part++)if(pieces[part]!=0&&mm->IsSetSlotUnlocked((uint)index,part))EnqueueRestoration(raw,index,pieces[part],part);}
            else EnqueueRestoration(raw,index,raw%1000000,-1);
        }
        history($"Dresser cleanup plan: {planned} restorations queued; retained entries: "+string.Join(" | ",retained));
        if(!Queue.Running)ReleaseManualCoordination();
        void EnqueueRestoration(uint raw,int slot,uint id,int outfitPart)
        {
            var facts=view.Facts(id);if(facts==null){retained.Add($"{id}: item metadata unavailable");return;}
            Decision WithoutThisCopy(bool live=true)
            {
                if(live)refreshProtection();
                var decision=StoragePolicy.WithoutDresserCopy(view.Evidence,id,view.DresserCopies(id),
                    ()=>(live?judge:previewJudge)(new(id,false,1,uint.MaxValue,(ushort)slot)));
                return config.Features.CanRestore(decision.Outcome)?decision:new(Outcome.Keep,"Armoire storage is disabled");
            }
            var proposal=WithoutThisCopy(false);
            if(!StoragePolicy.Restore(proposal.Outcome,config.CleanDresser,facts.ArmoireEligible && !view.Evidence.Armoire.Contains(id)))
            {retained.Add($"{facts.Name} [{id}]: {proposal.Outcome}, {proposal.Reason}");return;}
            planned++;
            var sent=false;var attempted=false;long before=0;var beforeCopies=0;
            var retry=new DresserRestoreRetry();
            Queue.Add(new("Restore dresser item "+id,()=>
            {
                var live=MirageManager.Instance();
                if(!sent && !config.CleanDresser)return StepResult.Completed;
                if(!config.ExecutionEnabled||view.Character!=cleanupCharacter||manualControl!=cleanupCharacter||
                    !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(cleanupCharacter))
                    throw new InvalidOperationException("Dresser restoration lost execution permission or its owned control lease");
                if(live==null||!live->PrismBoxRequested||!live->PrismBoxLoaded||ui.Get("MiragePrismPrismBox")==null)return StepResult.Waiting;
                if(!sent)
                {
                    if(!retry.Ready(Environment.TickCount64))return StepResult.Waiting;
                    if(ui.Visible("SelectYesno")||ui.Visible("ContextMenu"))return StepResult.Waiting;
                    var decision=WithoutThisCopy();
                    if(!StoragePolicy.Restore(decision.Outcome,config.CleanDresser,facts.ArmoireEligible && !view.Evidence.Armoire.Contains(id)))
                    {history($"Dresser cleanup retained {facts.Name} [{id}] after live recheck: {decision.Outcome}, {decision.Reason}");return StepResult.Completed;}
                    slot=DresserRestoration.Find(live->PrismBoxItemIds,raw,slot);
                    if(slot<0)throw new InvalidOperationException($"Dresser source is {(slot==-2?"ambiguous after positions changed":"no longer present")}: item={id}, raw={raw}");
                    if(InventoryView.FreeBagSlots()<1){notify("Dresser cleanup paused: make one inventory slot available.");return StepResult.Completed;}
                    if(view.IsUnique(id)&&view.Items.Any(s=>s.ItemId==id))
                    {notify($"Dresser cleanup held for {facts.Name}: a unique copy is already carried.");return StepResult.Completed;}
                    if(facts.ArmoireEligible && !view.Evidence.Armoire.Contains(id))protectCollection(id);
                    if(!InventoryView.PlayerLoaded())return StepResult.Waiting;
                    before=view.Items.Where(s=>s.ItemId==id).Sum(s=>(long)s.Quantity);
                    beforeCopies=view.DresserCopies(id);
                    var state=new DresserRestoreState(raw,slot,live->PrismBoxStain0Ids[slot],live->PrismBoxStain1Ids[slot],beforeCopies,before);
                    if(!retry.Unchanged(state))throw new InvalidOperationException($"Dresser or inventory changed after a rejected restoration: item={id}; no retry submitted");
                    bool accepted;
                    attempted=true;
                    if(outfitPart>=0)
                    {
                        if(!live->IsSetSlotUnlocked((uint)slot,outfitPart))return StepResult.Completed;
                        var bits=stackalloc byte[2];bits[0]=bits[1]=0;
                        bits[outfitPart/8]=(byte)(1<<(outfitPart%8));
                        accepted=live->RestorePrismBoxSetItem((uint)slot,bits);
                    }
                    else accepted=live->RestorePrismBoxItem((uint)slot);
                    if(!accepted)
                    {
                        attempted=false; // Verified native false return submits no restoration command.
                        var detail=$"item={id}, slot={slot}, part={outfitPart}, raw={raw}, copies={beforeCopies}, carried={before}, freeSlots={InventoryView.FreeBagSlots()}, storedBytes={state.First}/{state.Second}";
                        if(!retry.Rejected(state,Environment.TickCount64))throw new InvalidOperationException("Game repeatedly rejected dresser restoration without submitting it: "+detail);
                        history("Dresser restoration deferred after native rejection (no command submitted): "+detail);
                        return StepResult.Waiting;
                    }
                    sent=true;return StepResult.Waiting;
                }
                if(!InventoryView.PlayerLoaded())return StepResult.Waiting;
                var after=view.Items.Where(s=>s.ItemId==id).Sum(s=>(long)s.Quantity);
                var copies=view.DresserCopies(id);
                var acknowledgement=DresserRestoration.Acknowledge(beforeCopies,copies,before,after);
                if(acknowledgement==StepResult.Failed)throw new InvalidOperationException($"Dresser restoration acknowledgement mismatch: item={id}, dresser={beforeCopies}->{copies}, carried={before}->{after}");
                if(acknowledgement!=StepResult.Completed)return acknowledgement;
                history("Confirmed dresser restoration of "+id);return StepResult.Completed;
            },TimeoutMs:10000,CanSkip:()=>!attempted));
        }
    }
    public void DepositOpenDresser()
        =>Plan(DepositOpenDresserCore);
    private void DepositOpenDresserCore()
    {
        if(Queue.Running||!config.ExecutionEnabled||!config.Features.DresserCollection||ui.Get("MiragePrismPrismBox")==null||view.Evidence.DresserFreeSlots==null)return;
        if(!DresserPreparationReady())return;
        if(config.ExecutionReviewRequired || !config.Connected || !AcquireManualCoordination())return;
        refreshProtection();
        var character=view.Character;
        Queue.Begin();
        foreach(var source in view.Items.ToArray().Where(s=>previewJudge(s).Outcome==Outcome.CollectDresser).GroupBy(s=>s.ItemId).Select(g=>g.First()).OrderBy(s=>view.Facts(s.ItemId) is {} f?StoragePolicy.Priority(f,config.Rules,view.Evidence):int.MaxValue).ThenBy(s=>s.ItemId))
        {
            var submitted=false;
            DresserDeposit? plan=null;
            Queue.Add(new("Store dresser item "+source.ItemId,()=>
            {
                if(!config.ExecutionEnabled||view.Character!=character||character==0||manualControl!=character||
                    !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(character))return StepResult.Failed;
                var live=InventoryView.ReadSlot(source.Container,source.Slot);
                if(submitted)
                {
                    // A true native return means submitted, not completed. Never retry an uncertain submission.
                    var owned=view.LiveDresserContains(source.ItemId);
                    var result=DresserConfirmation.Acknowledge(source,live,false,
                        InventoryView.Loaded(source.Container),owned.HasValue,owned==true);
                    if(result==StepResult.Completed)history($"Confirmed dresser storage: item={source.ItemId}, source={source.Container}/{source.Slot}, route={plan!.Kind}, target={plan.Target}; source consumed and live collection copy present");
                    return result;
                }
                if(live!=source)throw new InvalidOperationException("Dresser source changed before submission: "+source.ItemId);
                refreshProtection();
                if(!config.Features.DresserCollection||judge(source).Outcome!=Outcome.CollectDresser)return StepResult.Completed;
                if(!view.DresserEligible(source.ItemId)||source.Quantity!=1||DresserSource.Category(source.Container)<0)
                {notify("Dresser deposit held: unsupported source or item is not dresser eligible. Item "+source.ItemId+" remains protected.");return StepResult.Completed;}
                var manager=MirageManager.Instance();
                if(manager==null||!manager->PrismBoxLoaded||ui.Get("MiragePrismPrismBox")==null)return StepResult.Waiting;
                if(ui.Visible("SelectYesno")||ui.Visible("ContextMenu")||ui.Visible("MiragePrismPrismSetConvert")||ui.Visible("MiragePrismPrismSetConvertC"))return StepResult.Waiting;
                var im=InventoryManager.Instance();
                var inv=im==null?null:im->GetInventoryContainer((InventoryType)source.Container);
                var item=inv==null?null:inv->GetInventorySlot(source.Slot);
                if(item==null||item->GetConditionPercentage()<100||im->GetInventoryItemCount(21800)<1)
                {notify("Dresser deposit needs fully repaired gear and a glamour prism. Item "+source.ItemId+" remains protected.");return StepResult.Completed;}
                plan=view.DresserDepositFor(source);
                if(plan.Kind==DresserDepositKind.Ordinary&&!dresserStore.OrdinaryAvailable)
                    throw new InvalidOperationException("Ordinary dresser function could not be verified for this game build; no call submitted");
                if(InventoryView.ReadSlot(source.Container,source.Slot)!=source)return StepResult.Failed;
                history($"Dresser native submission: item={source.ItemId}, source={source.Container}/{source.Slot}, HQ={source.Hq}, route={plan.Kind}, target={plan.Target}");
                // Set before entering native code so an exception cannot cause another submission.
                submitted=true;
                if(!dresserStore.Submit(manager,source,plan))throw new InvalidOperationException("Native dresser submission rejected for item "+source.ItemId);
                return StepResult.Waiting;
            },TimeoutMs:30000,CanSkip:()=>!submitted));
        }
        if(!Queue.Running)ReleaseManualCoordination();
    }
}
