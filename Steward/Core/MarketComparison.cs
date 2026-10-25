namespace Steward.Core;

public static class MarketComparison
{
    public static bool IsCurrent(uint item,uint nativeItem,int request,int nativeRequest,bool waiting,
        int received,uint expected,DateTimeOffset observed,DateTimeOffset opened,DateTimeOffset now) =>
        item!=0 && nativeItem%1000000==item && request==nativeRequest && !waiting && expected>0 &&
        received==expected && observed>=opened && observed<=now && now-observed<=TimeSpan.FromSeconds(10);
}
