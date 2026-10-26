namespace Steward.Core;

public static class MarketSubmission
{
    public static bool CanCommit(Stock expected, Stock? live, Decision decision, bool reprice,
        uint plannedPrice, uint plannedQuantity, int displayedPrice, int displayedQuantity, int maximumStack)
    {
        if(live!=expected || plannedPrice is 0 or > 999999999 || plannedQuantity==0 || plannedQuantity>expected.Quantity ||
            displayedPrice!=plannedPrice || displayedQuantity!=plannedQuantity)return false;
        if(reprice ? decision.Outcome is not (Outcome.Market or Outcome.Dispose) : decision.Outcome!=Outcome.Market)return false;
        if(reprice ? plannedQuantity!=expected.Quantity : plannedQuantity>(uint)Math.Clamp(maximumStack,1,9999))return false;
        if(!reprice&&decision.MaximumRemovable is uint available&&plannedQuantity>available)return false;
        return decision.Rule==null || plannedPrice>=decision.Rule.MinimumUnit &&
            (ulong)plannedPrice*plannedQuantity>=decision.Rule.MinimumTotal;
    }
}
