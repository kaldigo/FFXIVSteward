namespace Steward.Core;

public static class SaleTurnover
{
    // Market-wide units/day is a relative ranking, never a promise of this seller's sale time.
    public static double Days(uint quantity,SaleStatistic? sale,DateTimeOffset now,int hours)
    {
        if(quantity==0||sale==null||!sale.RecentSales||sale.HistoryTruncated||sale.DailySales is not >0||
            !double.IsFinite(sale.DailySales.Value)||sale.ValueObserved==null||sale.Fetched>now||sale.ValueObserved>now||
            now-sale.Fetched>TimeSpan.FromHours(Math.Clamp(hours,1,168))||now-sale.ValueObserved.Value>TimeSpan.FromHours(Math.Clamp(hours,1,168)))return double.PositiveInfinity;
        return quantity/sale.DailySales.Value;
    }
    public static bool Improves(double current,double replacement)=>double.IsFinite(current)&&double.IsFinite(replacement)&&replacement<current;
}
