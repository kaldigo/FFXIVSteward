using FFXIVClientStructs.FFXIV.Client.Game;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue
{
    public sealed record ReturnProposal(Stock Source,string? Reason);
    private string? ReturnReason(Stock source,ulong retainer,bool live)
    {
        if(config.Silver.Owns(retainer))return null; // Never apply ordinary returns or rotation to the dedicated strategy.
        if(!config.Features.ListingReturns||!config.SalesEnabledFor(retainer)||ReturnRetainerFreeSlots()<1)return null;
        var item=view.Facts(source.ItemId);if(item==null)return null;
        var decision=marketJudge(source,retainer,live);
        var rule=config.Rules.Sales.FirstOrDefault(r=>r.Matches(item,source,retainer));
        var price=(uint)InventoryManager.Instance()->GetRetainerMarketPrice((short)source.Slot);
        return ListingPolicy.ReturnReason(decision,rule,source,price,config.Pricing);
    }
    private void EnqueueReturn(Stock source,ulong retainer,bool coordinated)
    {
        var now=DateTimeOffset.UtcNow;
        EnqueueRotation(new(source,source,now,0,"rule-return",0,0),retainer,coordinated,new ListingPass(),ruleReturn:true);
    }
}
