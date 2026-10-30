namespace Steward.Core;

public static class SharedRepricing
{
    public static bool Usable(bool complete,uint observedWorld,uint currentWorld,DateTimeOffset observed,DateTimeOffset now)=>
        complete&&currentWorld!=0&&observedWorld==currentWorld&&observed<=now&&now-observed<=TimeSpan.FromMinutes(2);
    // Called only after live source, rule, protection and retainer checks.
    public static string? NoChange(Stock source,uint current,IEnumerable<Offering> offers,IReadOnlySet<ulong> own,
        QualityMode quality,PricingSettings pricing,uint minimumUnit,ulong minimumTotal)
    {
        if(current==0)return null;
        var rows=offers.ToArray();
        if(rows.Any(o=>o.ItemId!=source.ItemId))return null;
        var target=Pricing.Target(source.ItemId,source.Hq,current,rows,own,quality,pricing,minimumUnit);
        if(target==null)return Pricing.NoRepriceReason(source.ItemId,source.Hq,current,rows,own,quality,pricing,minimumUnit);
        if(target==current)return "Current price already matches target";
        if((ulong)target.Value*source.Quantity<minimumTotal)return $"Target stack value {(ulong)target.Value*source.Quantity} is below {minimumTotal} gil minimum";
        return null;
    }
}
