namespace Steward.Core;

public static class MarketComparison
{
    public sealed record SearchResponse(long Sequence,uint Item,byte Expected,int Status,DateTimeOffset At);
    public static string? ResponseRejection(uint item,long before,SearchResponse? response,uint nativeItem,
        uint count,IReadOnlyList<Offering> rows,IReadOnlyList<Offering> packets,DateTimeOffset opened,DateTimeOffset now)
    {
        if(response==null||response.Sequence<=before||response.At<opened)return "new search response has not arrived";
        if(response.Status!=0)return $"server rejected search (status 0x{response.Status:X8})";
        if(item==0||response.Item%1000000!=item||nativeItem%1000000!=item)return "search response is for another item";
        if(opened>now||response.At>now||now-opened>TimeSpan.FromSeconds(10))return "search observation expired";
        if(response.Expected>100||count!=response.Expected||rows.Count!=count||packets.Count!=count)
            return $"incomplete search: expected {response.Expected}, native {count}/{rows.Count}, received {packets.Count}";
        if(rows.Any(r=>r.ItemId!=item||r.ListingId==0||r.RetainerId==0||r.Quantity==0||r.UnitPrice is 0 or >999999999))
            return "native result has invalid item, seller, quantity or price";
        if(rows.Select(r=>r.ListingId).Distinct().Count()!=rows.Count)return "native result has duplicate listing IDs";
        if(!rows.OrderBy(r=>r.ListingId).SequenceEqual(packets.OrderBy(r=>r.ListingId)))return "network listings do not match native results";
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
