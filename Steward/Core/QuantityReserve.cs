namespace Steward.Core;

public static class QuantityReserve
{
    // Allocate the reserve deterministically across live source slots, NQ and HQ together.
    // Offline retainer caches never count as a surviving reserve.
    public static uint? Removable(Stock source,uint reserve,IReadOnlyList<Stock> live)
    {
        if(reserve==0)return source.Quantity;
        if(!live.Contains(source)||live.GroupBy(s=>(s.Container,s.Slot)).Any(g=>g.Count()>1))return null;
        ulong remaining=reserve;
        foreach(var stock in live.Where(s=>s.ItemId==source.ItemId).OrderBy(s=>s.Container).ThenBy(s=>s.Slot))
        {
            var kept=Math.Min(remaining,stock.Quantity);remaining-=kept;
            if(stock==source)return stock.Quantity-(uint)kept;
        }
        return null;
    }
}
