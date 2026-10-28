namespace Steward.Core;

public static class MarketComparison
{
    // A newly completed native search is authoritative. Packet request IDs are not
    // used here: the packet exposes a ushort while the native page counter is a byte.
    public static string? NativeRejection(uint item,int beforeRequest,uint nativeItem,int request,bool waiting,
        uint count,IReadOnlyList<Offering> rows,DateTimeOffset opened,DateTimeOffset now)
    {
        if(item==0||nativeItem%1000000!=item)return "native search is for another item";
        if(beforeRequest<0||request==beforeRequest)return "new native request has not arrived";
        if(waiting)return "native search is still loading";
        if(opened>now||now-opened>TimeSpan.FromSeconds(10))return "search observation expired";
        if(count>100||rows.Count!=count)return "native result count is incomplete or invalid";
        if(rows.Any(r=>r.ItemId!=item||r.ListingId==0||r.RetainerId==0||r.Quantity==0||r.UnitPrice is 0 or >999999999))
            return "native result has invalid item, seller, quantity or price";
        if(rows.Select(r=>r.ListingId).Distinct().Count()!=rows.Count)return "native result has duplicate listing IDs";
        return null;
    }
    public static uint? EmptyMarketAverage(Evidence evidence,uint item,bool hq,QualityMode quality,uint world,DateTimeOffset now,int refreshHours)
    {
        var summary=SaleStatistics.Select(evidence,item,hq,quality,world,now);
        if(world==0||summary==null||summary.Fetched>now||now-summary.Fetched>TimeSpan.FromHours(Math.Clamp(refreshHours,1,168))||
            summary.Average is not (>=1 and <=999999999))return null;
        return (uint)Math.Floor(summary.Average.Value);
    }
    public static bool IsConfirmedEmpty(uint item,uint nativeItem,int request,int nativeRequest,bool waiting,
        int received,uint expected,DateTimeOffset observed,DateTimeOffset opened,DateTimeOffset now)=>
        item!=0&&nativeItem%1000000==item&&request==nativeRequest&&!waiting&&received==0&&expected==0&&
        observed>=opened&&observed<=now&&now-observed<=TimeSpan.FromSeconds(10);
    public static bool IsCurrent(uint item,uint nativeItem,int request,int nativeRequest,bool waiting,
        int received,uint expected,DateTimeOffset observed,DateTimeOffset opened,DateTimeOffset now) =>
        item!=0 && nativeItem%1000000==item && request==nativeRequest && !waiting && expected>0 &&
        received==expected && observed>=opened && observed<=now && now-observed<=TimeSpan.FromSeconds(10);
}
