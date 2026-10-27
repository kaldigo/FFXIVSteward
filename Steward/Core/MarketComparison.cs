namespace Steward.Core;

public static class MarketComparison
{
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
