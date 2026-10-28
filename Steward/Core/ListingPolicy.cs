namespace Steward.Core;

public static class ListingPolicy
{
    // Unknown evidence and explicit exclusions never authorize automatic handling.
    public static string? ReturnReason(Decision decision,SaleRule? rule,Stock stock,uint price,PricingSettings pricing)
    {
        if(decision.Outcome==Outcome.Ignore || decision.Reason=="Explicit AutoRetainer protection")return null;
        // A known scope mismatch does not need price evidence, but missing protection still holds.
        var priceOnlyWait=decision.Outcome==Outcome.Wait&&decision.Reason is
            "Average sale value unavailable; sale and disposal held" or
            "No fresh competing price; disposal is blocked" or
            "Older sale value retained; disposal held without recent sales";
        if(decision.Outcome==Outcome.Wait&&!(rule==null&&priceOnlyWait))return null;
        if(rule==null)return "No enabled sale rule matches on this retainer";
        if(decision.SaleRejected)return "No longer meets the sale rule's value or sales-rate thresholds";
        if(decision.Outcome is Outcome.Keep or Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.StoreRetainer)
            return "Needed outside the market: "+decision.Reason;
        if(price==0)return null;
        if(price<rule.ListingMinimumUnit||(ulong)price*stock.Quantity<rule.ListingMinimumTotal)return "Current listing is below the rule's price floor";
        var limit=Math.Clamp(rule.MaximumStack,1,9999);
        if(pricing.UseMaxStackSize)limit=Math.Min(limit,Math.Clamp(pricing.MaximumStackSize,1,9999));
        if(stock.Quantity>limit)return "Current stack exceeds the sale stack limit";
        return null;
    }
}
