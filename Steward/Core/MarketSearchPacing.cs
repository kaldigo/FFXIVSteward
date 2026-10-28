namespace Steward.Core;

public sealed class MarketSearchPacing
{
    public const int RateLimited=0x70000003;
    public DateTimeOffset NextSearch {get;private set;}=DateTimeOffset.MinValue;
    private long observedSequence;
    public void Sent(DateTimeOffset now)=>DelayUntil(now.AddSeconds(3));
    public void Observe(MarketComparison.SearchResponse? response)
    {
        if(response==null||response.Sequence<=observedSequence)return;
        observedSequence=response.Sequence;
        DelayUntil(response.At.AddSeconds(response.Status==RateLimited?5:3));
    }
    private void DelayUntil(DateTimeOffset until){if(until>NextSearch)NextSearch=until;}
    public bool Ready(DateTimeOffset now)=>now>=NextSearch;
}
