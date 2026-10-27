using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue
{
    public sealed record ListingWindowPreview(Stock Source,DateTimeOffset? Since,int? MaximumDays);
    public sealed record RotationProposal(Stock Source,Stock Replacement,DateTimeOffset Since,double DaysListed,string Token,uint ReplacementQuantity,uint ReplacementPrice);
    private IEnumerable<Stock> OrderedSaleStock(ulong retainer)=>AvailableSaleStock()
        .OrderBy(s=>config.ListingWindows.Priority(view.Character,view.World,retainer,s));
    private void ObserveListingWindows(ulong retainer,List<Stock> listings)
    {
        if(view.Character==0||view.World==0)return;
        if(config.ListingWindows.Observe(view.Character,view.World,retainer,listings,AvailableSaleStock().ToArray(),DateTimeOffset.UtcNow))save();
    }
    private RotationProposal? FindRotation(ulong retainer,List<Stock> listings)
    {
        if(!config.Features.NewListings||!config.Features.Rotation||InventoryView.FreeBagSlots()<2||listings.Count!=20||!view.Evidence.ReferencesReady||!view.Evidence.GearsetsReady||!marketPreferencesReady())return null;
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
                var token=ListingWindows.Owner(view.Character,view.World,retainer)+source.Slot+"/"+age!.Since.UtcTicks;
                return new(source,stock,age.Since,(now-age.Since).TotalDays,token,choice.Quantity,choice.UnitPrice);
            }
        }
        return null;
    }
    private static ulong BagQuantity(Stock stock)
    {
        List<Stock> contents=[];
        foreach(var type in new[]{InventoryType.Inventory1,InventoryType.Inventory2,InventoryType.Inventory3,InventoryType.Inventory4})InventoryView.Read(type,contents);
        return contents.Where(s=>s.ItemId==stock.ItemId&&s.Hq==stock.Hq).Aggregate(0ul,(sum,s)=>sum+s.Quantity);
    }
    private void EnqueueRotation(RotationProposal proposal,ulong retainer,bool coordinated,ListingPass pass,bool ruleReturn=false)
    {
        var phase=0;var character=view.Character;var world=view.World;var source=proposal.Source;
        var beforeQuantity=0ul;var row=-1;ushort menuId=0;var acknowledged=false;var locked=false;var replan=false;
        bool Owns()=>coordinated?pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(character):
            manualControl==character&&pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsControl").InvokeFunc(character);
        Queue.Add(new((ruleReturn?"Return listing outside sale rules ":"Rotate aged listing ")+source.ItemId,()=>
        {
            if(!config.ExecutionEnabled||config.ExecutionReviewRequired||!config.SalesEnabledFor(retainer)||view.Character!=character||view.World!=world||!Owns())return StepResult.Failed;
            if(ruleReturn?!config.Features.ListingReturns:!config.Features.Rotation||!config.Features.NewListings)return StepResult.Failed;
            refreshProtection();
            if(!MarketSnapshot(out var selected,out var listings)||selected!=retainer||!InventoryView.PlayerLoaded())return StepResult.Failed;
            if(phase<2)
            {
                if(!ruleReturn&&listings.Count<20){replan=true;return StepResult.Completed;}
                if(InventoryView.ReadSlot(source.Container,source.Slot)!=source)return StepResult.Failed;
                if(InventoryView.FreeBagSlots()<(ruleReturn?1:2))return StepResult.Completed;
                if(ruleReturn)
                {
                    if(ReturnReason(source,retainer,true)==null)return StepResult.Completed;
                }
                else
                {
                var decision=marketJudge(source,retainer,true);
                if(decision.Outcome is not (Outcome.Market or Outcome.Dispose))return StepResult.Failed;
                var item=view.Facts(source.ItemId);var rule=item==null?null:config.Rules.Sales.FirstOrDefault(r=>r.Matches(item,source,retainer));
                if(!ListingWindows.Expired(config.ListingWindows.Age(character,world,retainer,source),rule?.MaximumListingDays,DateTimeOffset.UtcNow))return StepResult.Failed;
                if(InventoryView.ReadSlot(proposal.Replacement.Container,proposal.Replacement.Slot)!=proposal.Replacement)return StepResult.Failed;
                var proof=observedMarkets.GetValueOrDefault(proposal.Replacement.ItemId);
                var now=DateTimeOffset.UtcNow;
                if(proof==null||!proof.Complete||proof.World!=world||proof.At>now||now-proof.At>TimeSpan.FromSeconds(30))return StepResult.Failed;
                var replacement=SaleInput(proposal.Replacement,true);
                if(replacement==null||!ListingPlanner.Next(replacement,config.Pricing,world,config.Rules.MaximumPriceAgeMinutes,DateTimeOffset.UtcNow).Eligible)return StepResult.Failed;
                }
            }
            var agent=AgentRetainer.Instance();var sale=ui.Get("RetainerSellList");
            if(agent==null||sale==null||agent->RetainerSellListAddonId!=sale->Id)return StepResult.Failed;
            if(phase==0)
            {
                if(ui.Visible("ContextMenu")||ui.Visible("SelectYesno")||ui.Visible("InputNumeric"))return StepResult.Failed;
                if(config.ImportMarketBuddy){if(!pi.GetIpcSubscriber<string,bool>("Marketbuddy.Lock").InvokeFunc("Steward"))return StepResult.Failed;locked=true;}
                if(agent->SellListEntryCount<1||agent->SellListEntryCount>20)return StepResult.Failed;
                for(var i=0;i<agent->SellListEntryCount;i++)
                {var entry=agent->SellListEntries[i];if(entry.InventorySlot==source.Slot&&entry.ItemId%1000000==source.ItemId&&entry.Quantity==source.Quantity){if(row!=-1)return StepResult.Failed;row=i;}}
                if(row<0)return StepResult.Failed;
                if(!ui.Callback("RetainerSellList",0,row,1))return StepResult.Failed;
                phase=1;return StepResult.Waiting;
            }
            if(phase==1)
            {
                var menu=ui.Get("ContextMenu");if(menu==null)return StepResult.Waiting;
                if(agent->ContextMenuIndex!=row||!ConfirmationOwnership.Matches(sale->Id,menu->ParentId,menu->HostId))return StepResult.Failed;
                if(menu->AtkValuesCount<9||menu->AtkValues[0].Type!=AtkValueType.UInt)return StepResult.Failed;
                var count=menu->AtkValues[0].UInt;if(count>32||8+count>menu->AtkValuesCount)return StepResult.Failed;
                var choice=-1;
                for(var i=0;i<count;i++)
                {
                    var value=menu->AtkValues[8+i];
                    if(value.Type is not (AtkValueType.String or AtkValueType.ManagedString)||value.String.Value==null)continue;
                    if(Marshal.PtrToStringUTF8((nint)value.String.Value)==view.ReturnListingLabel){if(choice!=-1)return StepResult.Failed;choice=i;}
                }
                if(choice<0)return StepResult.Failed;
                if(InventoryView.ReadSlot(source.Container,source.Slot)!=source||!Owns())return StepResult.Failed;
                menuId=menu->Id;beforeQuantity=BagQuantity(source);
                var finalDecision=marketJudge(source,retainer,true);
                if(ruleReturn&&ReturnReason(source,retainer,true)==null)return StepResult.Completed;
                if(!ruleReturn&&finalDecision.Outcome is not (Outcome.Market or Outcome.Dispose))return StepResult.Failed;
                config.ListingWindows.QueueReturn(character,world,retainer,source,DateTimeOffset.UtcNow,save);
                if(!ui.Callback("ContextMenu",0,choice,0,0,0))return StepResult.Failed;
                phase=2;return StepResult.Waiting;
            }
            if(!StockTransition.Removed(source,InventoryView.ReadSlot(source.Container,source.Slot),source.Quantity,InventoryView.Loaded(source.Container)))return StepResult.Waiting;
            if(listings.Count>19||BagQuantity(source)!=beforeQuantity+source.Quantity)return StepResult.Waiting;
            acknowledged=true;history($"Confirmed return of {source.ItemId} x{source.Quantity} from the market; normal sale, collection and disposal rules apply.");
            return StepResult.Completed;
        },()=>
        {
            try
            {
                if(phase>0&&Owns())
                {
                    var menu=ui.Get("ContextMenu");var sale=ui.Get("RetainerSellList");var agent=AgentRetainer.Instance();
                    if(menu!=null&&sale!=null&&agent!=null&&agent->ContextMenuIndex==row&&
                        (menuId==menu->Id||ConfirmationOwnership.Matches(sale->Id,menu->ParentId,menu->HostId)))ui.Close("ContextMenu");
                }
            }
            finally
            {
                if(locked&&!pi.GetIpcSubscriber<string,bool>("Marketbuddy.Unlock").InvokeFunc("Steward"))throw new InvalidOperationException("Market Buddy did not release rotation lock");
            }
            if(acknowledged&&!ruleReturn)Enqueue(proposal.Replacement,retainer,false,coordinated,(stock,success)=>
            {if(!success)pass.Rejected(stock);if(config.ExecutionEnabled&&!config.ExecutionReviewRequired)EnqueueListingPump(retainer,coordinated,pass);});
            else if(replan)EnqueueListingPump(retainer,coordinated,pass);
        },15000));
    }
}
