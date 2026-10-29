using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue
{
    public sealed record ListingWindowPreview(Stock Source,DateTimeOffset? Since,int? MaximumDays);
    public sealed record RotationProposal(Stock Source,Stock Replacement,DateTimeOffset Since,double DaysListed,string Token,uint ReplacementQuantity,uint ReplacementPrice,double? SourceTurnoverDays=null,double? ReplacementTurnoverDays=null);
    private double TurnoverDays(Stock stock,uint quantity)
    {
        var now=DateTimeOffset.UtcNow;
        return SaleTurnover.Days(quantity,SaleStatistics.Select(view.Evidence,stock.ItemId,stock.Hq,config.Rules.Quality,view.World,now),now,config.Rules.SummaryRefreshHours);
    }
    private double CandidateTurnover(Stock stock)
    {
        var input=SaleInput(stock,false);if(input==null)return double.PositiveInfinity;
        var choice=ListingPlanner.Next(input,config.Pricing,view.World,config.Rules.MaximumPriceAgeMinutes,DateTimeOffset.UtcNow);
        return choice.Eligible?TurnoverDays(stock,choice.Quantity):double.PositiveInfinity;
    }
    private int SalePriority(Stock stock)
    {
        var input=SaleInput(stock,false);if(input==null)return 2;
        return ListingPlanner.Priority(input,ListingPlanner.Next(input,config.Pricing,view.World,config.Rules.MaximumPriceAgeMinutes,DateTimeOffset.UtcNow));
    }
    private IEnumerable<Stock> OrderedSaleStock(ulong retainer)=>AvailableSaleStock()
        .OrderBy(SalePriority)
        .ThenBy(s=>config.Rules.PreferFasterSales?CandidateTurnover(s):0)
        .ThenBy(s=>config.ListingWindows.Priority(view.Character,view.World,retainer,s));
    private void ObserveListingWindows(ulong retainer,List<Stock> listings)
    {
        if(view.Character==0||view.World==0)return;
        if(config.ListingWindows.Observe(view.Character,view.World,retainer,listings,AvailableSaleStock().ToArray(),DateTimeOffset.UtcNow))save();
    }
    private RotationProposal? FindRotation(ulong retainer,List<Stock> listings)
    {
        if(config.Silver.Owns(retainer))return null;
        if(!config.Features.NewListings||!config.Features.Rotation||ReturnRetainerFreeSlots()<1||listings.Count!=20||!view.Evidence.ReferencesReady||!view.Evidence.GearsetsReady||!marketPreferencesReady())return null;
        var now=DateTimeOffset.UtcNow;
        foreach(var source in listings.OrderBy(s=>config.ListingWindows.Age(view.Character,view.World,retainer,s)?.Since??DateTimeOffset.MaxValue))
        {
            var item=view.Facts(source.ItemId);if(item==null)continue;
            var rule=config.Rules.Sales.FirstOrDefault(r=>r.Matches(item,source,retainer));
            var age=config.ListingWindows.Age(view.Character,view.World,retainer,source);
            if(!ListingWindows.Expired(age,rule?.MaximumListingDays,now))continue;
            var decision=marketJudge(source,retainer,false);
            if(decision.Outcome is not (Outcome.Market or Outcome.Dispose))continue;
            foreach(var stock in OrderedSaleStock(retainer))
            {
                if(stock.ItemId==source.ItemId)continue;
                var input=SaleInput(stock,false);if(input==null)continue;
                var choice=ListingPlanner.Next(input,config.Pricing,view.World,config.Rules.MaximumPriceAgeMinutes,now);
                if(!choice.Eligible)continue;
                if(ListingPlanner.Priority(input,choice)>(rule?.Priority(decision.UnitPrice,source.Quantity)??0))continue;
                var sourceDays=TurnoverDays(source,source.Quantity);var replacementDays=TurnoverDays(stock,choice.Quantity);
                if(config.Rules.PreferFasterSales&&!SaleTurnover.Improves(sourceDays,replacementDays))continue;
                var token=ListingWindows.Owner(view.Character,view.World,retainer)+source.Slot+"/"+age!.Since.UtcTicks;
                return new(source,stock,age.Since,(now-age.Since).TotalDays,token,choice.Quantity,choice.UnitPrice,double.IsFinite(sourceDays)?sourceDays:null,double.IsFinite(replacementDays)?replacementDays:null);
            }
        }
        return null;
    }
    private static int ReturnRetainerFreeSlots()
    {
        var manager=InventoryManager.Instance();if(manager==null)return 0;
        var count=0;
        foreach(var type in InventoryView.RetainerTypes())
        {
            var bag=manager->GetInventoryContainer(type);if(bag==null||!bag->IsLoaded)return 0;
            for(var i=0;i<bag->Size;i++)
            {var slot=bag->GetInventorySlot(i);if(slot!=null&&slot->ItemId==0)count++;}
        }
        return count;
    }
    private static ulong RetainerQuantity(Stock stock)
    {
        List<Stock> contents=[];
        foreach(var type in InventoryView.RetainerTypes())InventoryView.Read(type,contents);
        return contents.Where(s=>s.ItemId==stock.ItemId&&s.Hq==stock.Hq).Aggregate(0ul,(sum,s)=>sum+s.Quantity);
    }
    private void EnqueueRotation(RotationProposal proposal,ulong retainer,bool coordinated,ListingPass pass,bool ruleReturn=false)
    {
        var phase=0;var character=view.Character;var world=view.World;var source=proposal.Source;
        var beforeQuantity=0ul;var row=-1;var acknowledged=false;var locked=false;var replan=false;var submitted=false;
        bool Owns()=>coordinated?pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(character):
            manualControl==character&&pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(character);
        StepResult Fail(string reason)
        {
            var native=AgentRetainer.Instance();
            var context=AgentContext.Instance();
            var agentState=native==null?"absent":$"rows={native->SellListEntryCount}, context row={native->ContextMenuIndex}, sell addon={native->RetainerSellListAddonId}, context owner={(context==null?0:context->OwnerAddon)}";
            history($"Market return rejected: item={source.ItemId}, retainer={retainer}, phase={phase}, submitted={submitted}, reason={reason}; expected={source}; live={InventoryView.ReadSlot(source.Container,source.Slot)}; retainer quantity={RetainerQuantity(source)}; agent={agentState}; {ui.Describe("RetainerSellList")}; {ui.Describe("ContextMenu")}");
            return StepResult.Failed;
        }
        Queue.Add(new((ruleReturn?"Return listing outside sale rules ":"Rotate aged listing ")+source.ItemId,()=>
        {
            if(!config.ExecutionEnabled||config.ExecutionReviewRequired||!config.SalesEnabledFor(retainer)||view.Character!=character||view.World!=world||!Owns())return Fail("execution, character, world, retainer scope or control lease changed");
            if(ruleReturn?!config.Features.ListingReturns:!config.Features.Rotation||!config.Features.NewListings)return Fail("listing-return or rotation feature disabled");
            refreshProtection();
            if(!MarketSnapshot(out var selected,out var listings)||selected!=retainer||!InventoryView.PlayerLoaded())return Fail("market snapshot or player inventory not ready");
            if(phase<2)
            {
                if(!ruleReturn&&listings.Count<20){replan=true;return StepResult.Completed;}
                if(InventoryView.ReadSlot(source.Container,source.Slot)!=source)return Fail("listing source changed before submission");
                if(ReturnRetainerFreeSlots()<1)return Fail("retainer inventory unavailable or full");
                if(ruleReturn)
                {
                    if(ReturnReason(source,retainer,true)==null)return StepResult.Completed;
                }
                else
                {
                var decision=marketJudge(source,retainer,true);
                if(decision.Outcome is not (Outcome.Market or Outcome.Dispose))return Fail("rotation protection decision changed");
                var item=view.Facts(source.ItemId);var rule=item==null?null:config.Rules.Sales.FirstOrDefault(r=>r.Matches(item,source,retainer));
                if(!ListingWindows.Expired(config.ListingWindows.Age(character,world,retainer,source),rule?.MaximumListingDays,DateTimeOffset.UtcNow))return Fail("listing age no longer qualifies for rotation");
                if(InventoryView.ReadSlot(proposal.Replacement.Container,proposal.Replacement.Slot)!=proposal.Replacement)return Fail("replacement source changed");
                var proof=observedMarkets.GetValueOrDefault(proposal.Replacement.ItemId);
                var now=DateTimeOffset.UtcNow;
                if(proof==null||!proof.Complete||proof.World!=world||proof.At>now||now-proof.At>TimeSpan.FromSeconds(30))return Fail("fresh replacement comparison unavailable");
                var replacement=SaleInput(proposal.Replacement,true);
                if(replacement==null)return Fail("replacement sale input unavailable");
                var choice=ListingPlanner.Next(replacement,config.Pricing,world,config.Rules.MaximumPriceAgeMinutes,DateTimeOffset.UtcNow);
                if(!choice.Eligible)return Fail("replacement no longer eligible");
                if(ListingPlanner.Priority(replacement,choice)>(rule?.Priority(decision.UnitPrice,source.Quantity)??0))return StepResult.Completed;
                if(config.Rules.PreferFasterSales&&!SaleTurnover.Improves(TurnoverDays(source,source.Quantity),TurnoverDays(proposal.Replacement,choice.Quantity)))return StepResult.Completed;
                }
            }
            var agent=AgentRetainer.Instance();var sale=ui.Get("RetainerSellList");
            if(agent==null||sale==null||agent->RetainerSellListAddonId!=sale->Id)return Fail("retainer selling agent or addon identity not ready");
            if(phase==0)
            {
                if(ui.Visible("ContextMenu")||ui.Visible("SelectYesno")||ui.Visible("InputNumeric"))return Fail("another context or confirmation window is open");
                if(config.ImportMarketBuddy){if(!pi.GetIpcSubscriber<string,bool>("Marketbuddy.Lock").InvokeFunc("Steward"))return Fail("Market Buddy lock unavailable");locked=true;}
                if(agent->SellListEntryCount<1||agent->SellListEntryCount>20)return Fail("native selling-list row count invalid");
                for(var i=0;i<agent->SellListEntryCount;i++)
                {var entry=agent->SellListEntries[i];if(entry.InventorySlot==source.Slot&&entry.ItemId%1000000==source.ItemId&&entry.Quantity==source.Quantity){if(row!=-1)return Fail("multiple native rows match source");row=i;}}
                if(row<0)return Fail("no native row matches source");
                if(InventoryView.ReadSlot(source.Container,source.Slot)!=source||!Owns())return Fail("source or control lease changed before return");
                if(ReturnRetainerFreeSlots()<1)return Fail("retainer inventory unavailable or full before return");
                beforeQuantity=RetainerQuantity(source);
                var finalDecision=marketJudge(source,retainer,true);
                if(ruleReturn&&ReturnReason(source,retainer,true)==null)return StepResult.Completed;
                if(!ruleReturn&&finalDecision.Outcome is not (Outcome.Market or Outcome.Dispose))return Fail("rotation protection changed at submission");
                var manager=InventoryManager.Instance();
                if(source.Container!=(uint)InventoryType.RetainerMarket||manager==null||
                    (nint)InventoryManager.Addresses.MoveFromRetainerMarketToRetainerInventory.Value==0)
                    return Fail("native return-to-retainer binding or market source unavailable");
                config.ListingWindows.QueueReturn(character,world,retainer,source,DateTimeOffset.UtcNow,save);
                submitted=true;phase=2; // Arm before the native call; its return code is not inventory acknowledgement.
                var result=manager->MoveFromRetainerMarketToRetainerInventory(InventoryType.RetainerMarket,source.Slot,source.Quantity);
                history($"Native market return submitted: item={source.ItemId}, retainer={retainer}, slot={source.Slot}, quantity={source.Quantity}, before retainer quantity={beforeQuantity}, result={result}; awaiting source removal and retainer arrival");
                return StepResult.Waiting;
            }
            if(InventoryView.RetainerTypes().Any(t=>!InventoryView.Loaded((uint)t)))return StepResult.Waiting;
            if(!StockTransition.Removed(source,InventoryView.ReadSlot(source.Container,source.Slot),source.Quantity,InventoryView.Loaded(source.Container)))return StepResult.Waiting;
            if(listings.Count>19||!MarketReturnReceipt.Arrived(beforeQuantity,RetainerQuantity(source),source.Quantity))return StepResult.Waiting;
            acknowledged=true;history($"Confirmed return of {source.ItemId} x{source.Quantity} from the market to retainer {retainer} inventory; quantity {beforeQuantity} -> {RetainerQuantity(source)}; normal sale, collection and disposal rules apply.");
            return StepResult.Completed;
        },()=>
        {
            try
            {
                if(phase>0&&!acknowledged)
                    history($"Market return ended without acknowledgement: item={source.ItemId}, phase={phase}, submitted={submitted}, expected={source}, live={InventoryView.ReadSlot(source.Container,source.Slot)}, before retainer quantity={beforeQuantity}, retainer quantity={RetainerQuantity(source)}");
            }
            finally
            {
                if(locked&&!pi.GetIpcSubscriber<string,bool>("Marketbuddy.Unlock").InvokeFunc("Steward"))throw new InvalidOperationException("Market Buddy did not release rotation lock");
            }
            if(acknowledged&&!ruleReturn)Enqueue(proposal.Replacement,retainer,false,coordinated,(stock,success)=>
            {if(!success)pass.Rejected(stock);if(config.ExecutionEnabled&&!config.ExecutionReviewRequired)EnqueueListingPump(retainer,coordinated,pass);});
            else if(replan)EnqueueListingPump(retainer,coordinated,pass);
        },15000,CanSkip:()=>!submitted));
    }
}
