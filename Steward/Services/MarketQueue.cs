using Dalamud.Game.Network.Structures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe class MarketQueue : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IMarketBoard market;
    private readonly InventoryView view;
    private readonly Configuration config;
    private readonly NativeUi ui;
    private readonly Action<string> history;
    private readonly Func<Stock,Decision> judge;
    private bool manualSuppression;
    private readonly Dictionary<uint,(DateTimeOffset At,List<Offering> Offers)> offers=[];
    public OperationQueue Queue { get; }=new();
    public MarketQueue(IDalamudPluginInterface pi,IMarketBoard market,InventoryView view,Configuration config,NativeUi ui,Func<Stock,Decision> judge,Action<string> history)
    {this.pi=pi;this.market=market;this.view=view;this.config=config;this.ui=ui;this.judge=judge;this.history=history;market.OfferingsReceived+=Received;}
    private void Received(IMarketBoardCurrentOfferings packet)
    {
        var now=DateTimeOffset.UtcNow;
        foreach(var group in packet.ItemListings.GroupBy(x=>x.ItemId%1000000))
        {
            var values=group.Select(x=>new Offering(group.Key,x.IsHq,x.PricePerUnit,x.ItemQuantity,x.RetainerId)).ToList();
            // Accumulate pages from the same request window; stale packets are never used after opening a new comparison.
            if(offers.TryGetValue(group.Key,out var old)&&now-old.At<TimeSpan.FromSeconds(2))values.AddRange(old.Offers);
            offers[group.Key]=(now,values.Distinct().ToList());
            foreach(var quality in values.Where(x=>!config.OwnRetainers.Contains(x.RetainerId)).GroupBy(x=>x.Hq))
                view.Evidence.Prices[(group.Key,quality.Key)]=new(group.Key,quality.Key,view.World,quality.Min(x=>x.UnitPrice),now,"Game");
        }
    }
    public void Start(bool coordinated=false)
    {
        if(Queue.Running||!config.Automatic) return;
        if(!coordinated)
        {
            try{if(pi.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc()){history("AutoRetainer is busy; market work deferred");return;}}
            catch{history("AutoRetainer coordination unavailable");return;}
        }
        Queue.Begin();
        if(ui.Get("RetainerSellList")==null){history("Open the retainer sell list before starting market work.");return;}
        var rm=RetainerManager.Instance();var retainer=rm==null?0:rm->LastSelectedRetainerId;
        if(retainer==0){history("Retainer identity unavailable");return;}
        if(!coordinated)
        {
            if(pi.GetIpcSubscriber<bool>("AutoRetainer.GetSuppressed").InvokeFunc()){history("AutoRetainer is suppressed by another operation");return;}
            pi.GetIpcSubscriber<bool,object>("AutoRetainer.SetSuppressed").InvokeAction(true);manualSuppression=true;
        }
        List<Stock> listings=[];InventoryView.Read(InventoryType.RetainerMarket,listings);
        foreach(var listing in listings)Enqueue(listing,retainer,true);
        if(listings.Count<20)
        {
            var free=20-listings.Count;
            var available=new List<Stock>(view.Items.Where(s=>s.Container<4));
            foreach(var type in InventoryView.RetainerTypes())InventoryView.Read(type,available);
            foreach(var item in available.Where(s=>(judge(s).Outcome==Outcome.Market ||
                judge(s) is {Outcome:Outcome.Wait,Reason:"No fresh competing price; disposal is blocked"} && view.Facts(s.ItemId) is {} f && config.Rules.Sales.Any(r=>r.Matches(f,s)))).Take(free))Enqueue(item,retainer,false);
        }
        if(!Queue.Running)ReleaseManualCoordination();
    }
    private void Enqueue(Stock expected,ulong retainer,bool reprice)
    {
        var phase=0;var opened=DateTimeOffset.MinValue;var sent=DateTimeOffset.MinValue;
        uint? target=null;uint? beforePrice=null;var quantity=0;var locked=false;
        HashSet<ushort> occupiedBefore=[];
        Queue.Add(new((reprice?"Reprice ":"List ")+expected.ItemId,()=>
        {
            var rm=RetainerManager.Instance();
            if(!config.Automatic || rm==null || rm->LastSelectedRetainerId!=retainer)return StepResult.Failed;
            var live=InventoryView.ReadSlot(expected.Container,expected.Slot);
            if(phase<3 && live!=expected)return StepResult.Failed;
            var decision=judge(expected);
            var priceOnlyWait=decision is {Outcome:Outcome.Wait,Reason:"No fresh competing price; disposal is blocked"};
            if(reprice ? decision.Outcome is Outcome.Ignore or Outcome.Keep or Outcome.CollectArmoire or Outcome.CollectDresser || decision.Outcome==Outcome.Wait&&!priceOnlyWait : decision.Outcome!=Outcome.Market&&!priceOnlyWait)return StepResult.Completed;
            if(phase==0)
            {
                if(ui.Get("RetainerSellList")==null || ui.Get("RetainerSell")!=null)return StepResult.Failed;
                try{pi.GetIpcSubscriber<string,bool>("Marketbuddy.Lock").InvokeFunc("Steward");locked=true;}catch{ /* Market Buddy is optional. */ }
                beforePrice=reprice?(uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)expected.Slot):null;
                List<Stock> before=[];InventoryView.Read(InventoryType.RetainerMarket,before);occupiedBefore=before.Select(s=>s.Slot).ToHashSet();
                AgentRetainer.Instance()->OpenRetainerSell((InventoryType)expected.Container,expected.Slot);phase=1;return StepResult.Waiting;
            }
            if(phase==1)
            {
                if(ui.Get("RetainerSell")==null)return StepResult.Waiting;
                offers.Remove(expected.ItemId);opened=DateTimeOffset.UtcNow;
                if(!ui.Click("RetainerSell",4))return StepResult.Failed;phase=2;return StepResult.Waiting;
            }
            if(phase==2)
            {
                if(!offers.TryGetValue(expected.ItemId,out var result)||result.At<opened||DateTimeOffset.UtcNow-result.At<TimeSpan.FromSeconds(1))return StepResult.Waiting;
                decision=judge(expected);
                if(!reprice && decision.Outcome!=Outcome.Market)return StepResult.Completed;
                var floor=decision.Rule?.MinimumUnit??1;
                target=Pricing.Target(expected.ItemId,expected.Hq,beforePrice,result.Offers,config.OwnRetainers,config.Rules.Quality,config.Pricing,floor);
                if(target==null)return StepResult.Completed;
                var maximum=decision.Rule?.MaximumStack??9999;
                if(config.Pricing.UseMaxStackSize)maximum=Math.Min(maximum,config.Pricing.MaximumStackSize);
                quantity=(int)Math.Min(expected.Quantity,(uint)Math.Clamp(maximum,1,9999));
                if(reprice)quantity=(int)expected.Quantity;
                if(decision.Rule!=null&&(ulong)target.Value*(uint)quantity<decision.Rule.MinimumTotal)return StepResult.Completed;
                var agent=AgentRetainer.Instance();
                if(agent==null||agent->SellItemInventoryType!=(InventoryType)expected.Container||agent->SellItemInventorySlot!=expected.Slot)return StepResult.Failed;
                ui.Close("ItemSearchResult");
                if(!ui.Callback("RetainerSell",2,(int)target.Value))return StepResult.Failed;
                if(!reprice && !ui.Callback("RetainerSell",3,quantity))return StepResult.Failed;
                if(InventoryView.ReadSlot(expected.Container,expected.Slot)!=expected)return StepResult.Failed;
                if(!ui.Click("RetainerSell",21))return StepResult.Failed;
                sent=DateTimeOffset.UtcNow;phase=3;return StepResult.Waiting;
            }
            if(reprice)
            {
                if(live!=expected)return StepResult.Failed;
                if((uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)expected.Slot)!=target)return StepResult.Waiting;
            }
            else
            {
                if(live!=null && live.ItemId==expected.ItemId && live.Quantity==expected.Quantity)return StepResult.Waiting;
                List<Stock> listed=[];InventoryView.Read(InventoryType.RetainerMarket,listed);
                if(!listed.Any(s=>!occupiedBefore.Contains(s.Slot)&&s.ItemId==expected.ItemId&&s.Hq==expected.Hq&&s.Quantity==quantity&&
                    (uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)s.Slot)==target))return StepResult.Waiting;
            }
            history($"Confirmed {(reprice?"repriced":"listed")} {expected.ItemId} x{quantity} at {target} gil");return StepResult.Completed;
        },()=>
        {
            ui.Close("ItemSearchResult");ui.Close("RetainerSell");
            if(locked)try{pi.GetIpcSubscriber<string,bool>("Marketbuddy.Unlock").InvokeFunc("Steward");}catch{}
        },20000));
    }
    public void Advance(long now){Queue.Advance(now);if(!Queue.Running)ReleaseManualCoordination();}
    public void Stop(){try{Queue.Stop("Stopped");}finally{ReleaseManualCoordination();}}
    private void ReleaseManualCoordination()
    {
        if(!manualSuppression)return;manualSuppression=false;
        try{pi.GetIpcSubscriber<bool,object>("AutoRetainer.SetSuppressed").InvokeAction(false);}catch(Exception ex){history("AutoRetainer suppression release failed: "+ex.Message);}
    }
    public void Dispose(){try{Stop();}finally{market.OfferingsReceived-=Received;}}
    public void ReturnToRetainerMenu()
    {ui.Close("ItemSearchResult");ui.Close("RetainerSell");ui.Close("RetainerSellList");var agent=AgentRetainer.Instance();if(agent!=null)agent->Hide();}
    public void WithdrawForOtherActions(ulong character)
    {
        Queue.Begin();
        List<Stock> stored=[];foreach(var type in InventoryView.RetainerTypes())InventoryView.Read(type,stored);
        foreach(var stock in stored.Where(s=>judge(s).Outcome is Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Dispose))
        {
            var sent=false;long before=0;
            Queue.Add(new("Withdraw "+stock.ItemId,()=>
            {
                if(!config.Automatic||view.Character!=character)return StepResult.Failed;
                if(!sent)
                {
                    before=view.Items.Where(s=>s.ItemId==stock.ItemId&&s.Hq==stock.Hq).Sum(s=>(long)s.Quantity);
                    if(InventoryView.ReadSlot(stock.Container,stock.Slot)!=stock)return StepResult.Failed;
                    var decision=judge(stock);if(decision.Outcome is not (Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Dispose))return StepResult.Completed;
                    var payload=System.Text.Json.JsonSerializer.Serialize(new{stock.Container,stock.Slot,stock.ItemId,stock.Quantity});
                    if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Retrieve").InvokeFunc(character,payload))return StepResult.Failed;
                    sent=true;return StepResult.Waiting;
                }
                if(InventoryView.ReadSlot(stock.Container,stock.Slot)==stock)return StepResult.Waiting;
                var after=view.Items.Where(s=>s.ItemId==stock.ItemId&&s.Hq==stock.Hq).Sum(s=>(long)s.Quantity);
                if(after<before+stock.Quantity)return StepResult.Waiting;
                history("Confirmed withdrawal of "+stock.ItemId+" x"+stock.Quantity);return StepResult.Completed;
            },TimeoutMs:10000));
        }
    }
    public void CleanupDresser()
    {
        if(Queue.Running||!config.Automatic||ui.Get("MiragePrismPrismBox")==null)return;
        var mm=MirageManager.Instance();if(mm==null||!mm->PrismBoxLoaded)return;
        Queue.Begin();
        for(var index=0;index<mm->PrismBoxItemIds.Length;index++)
        {
            var raw=mm->PrismBoxItemIds[index];if(raw==0)continue;
            var pieces=view.OutfitPieces(raw%1000000);
            if(pieces!=null)
            {for(var part=0;part<pieces.Length;part++)if(pieces[part]!=0&&mm->IsSetSlotUnlocked((uint)index,part))EnqueueRestoration(raw,index,pieces[part],part);}
            else EnqueueRestoration(raw,index,raw%1000000,-1);
        }
        void EnqueueRestoration(uint raw,int slot,uint id,int outfitPart)
        {
            var facts=view.Facts(id);if(facts==null)return;
            Decision WithoutThisCopy()
            {
                var had=view.DresserCopies(id)<=1 && view.Evidence.Dresser.Remove(id);
                try{return judge(new(id,false,1,uint.MaxValue,(ushort)slot));}
                finally{if(had)view.Evidence.Dresser.Add(id);}
            }
            if(WithoutThisCopy().Outcome is not (Outcome.Dispose or Outcome.Market))return;
            var sent=false;long before=0;
            Queue.Add(new("Restore dresser item "+id,()=>
            {
                var live=MirageManager.Instance();
                if(!config.Automatic||live==null||!live->PrismBoxLoaded||ui.Get("MiragePrismPrismBox")==null)return StepResult.Failed;
                if(!sent)
                {
                    if(live->PrismBoxItemIds[slot]!=raw)return StepResult.Failed;
                    if(WithoutThisCopy().Outcome is not (Outcome.Dispose or Outcome.Market))return StepResult.Completed;
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
                if(view.Items.Where(s=>s.ItemId==id).Sum(s=>(long)s.Quantity)<=before)return StepResult.Waiting;
                history("Confirmed dresser restoration of "+id);return StepResult.Completed;
            },TimeoutMs:10000));
        }
    }
    public void DepositOpenDresser()
    {
        if(Queue.Running||!config.Automatic||ui.Get("MiragePrismPrismBoxCrystallize")==null)return;
        Queue.Begin();
        foreach(var source in view.Items.Where(s=>judge(s).Outcome==Outcome.CollectDresser).GroupBy(s=>s.ItemId).Select(g=>g.First()))
        {
            var phase=0;var opened=DateTimeOffset.MinValue;
            Queue.Add(new("Store dresser item "+source.ItemId,()=>
            {
                if(!config.Automatic||view.Character==0||ui.Get("MiragePrismPrismBoxCrystallize")==null)return StepResult.Failed;
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
                    if(selected<0)return StepResult.Completed; // Current native filter may not include this item.
                    var im=InventoryManager.Instance();var inv=im->GetInventoryContainer((InventoryType)source.Container);
                    var item=inv->GetInventorySlot(source.Slot);
                    if(item->GetConditionPercentage()!=100||im->GetInventoryItemCount(21800)<1)return StepResult.Completed;
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
                    var name=view.Facts(source.ItemId)?.Name;
                    if(name==null||confirm->PromptText==null||!confirm->PromptText->NodeText.ToString().Contains(name,StringComparison.Ordinal))return StepResult.Failed;
                    if(!ui.Callback("SelectYesno",0))return StepResult.Failed;phase=2;return StepResult.Waiting;
                }
                if(live==source||!view.Evidence.Dresser.Contains(source.ItemId))return StepResult.Waiting;
                history("Confirmed dresser storage of "+source.ItemId);return StepResult.Completed;
            },TimeoutMs:12000));
        }
    }
}
