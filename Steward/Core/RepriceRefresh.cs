namespace Steward.Core;

// One bounded wait budget per retainer pass, shared by all ordinary listings.
public sealed class RepriceRefresh
{
    private DateTimeOffset? deadline;
    public static bool IsRefreshHold(Decision decision)=>decision.Outcome==Outcome.Wait&&decision.Reason is
        "Average sale value unavailable; sale and disposal held" or "Rule sales velocity is unknown; item held" or
        "Rule requires recent sale evidence" or "Older sale value retained; disposal held without recent sales";
    public bool Wait(Decision decision,bool enabled,IEnumerable<SaleStatistic> scoped,int hours,DateTimeOffset now)
    {
        if(!enabled||!IsRefreshHold(decision))return false;
        var rows=scoped.ToArray();
        // A fresh completed fetch with no usable value is a real hold, not a refresh in progress.
        if(rows.Length>0&&rows.All(s=>s.Fetched<=now&&now-s.Fetched<TimeSpan.FromHours(Math.Clamp(hours,1,168))))return false;
        deadline??=now.AddSeconds(25);
        return now<deadline;
    }
}
