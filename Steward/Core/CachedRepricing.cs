namespace Steward.Core;

public sealed record CachedPriceDecision(bool Known,uint? Target,Quote? Quote,string Reason,bool VerifyAlternatives=false)
{
    public bool NeedsComparison=>!Known||Target.HasValue||VerifyAlternatives;
}

public static class CachedRepricing
{
    public static CachedPriceDecision Decide(IEnumerable<Quote> quotes,uint world,uint item,bool hq,QualityMode quality,
        uint current,PricingSettings pricing,uint floor,DateTimeOffset now,int hours,SilverStrategy? silver=null)
    {
        var quote=quotes.Where(q=>q.World==world&&q.ItemId==item&&(quality==QualityMode.Combined||q.Hq==hq)&&
            q.Source is "Allagan Market/0" or "Allagan Market/1" or "Allagan Market/2"&&
            q.UnitPrice is >0 and <=999999999&&q.Observed<=now&&now-q.Observed<=TimeSpan.FromHours(Math.Clamp(hours,1,168)))
            .OrderBy(q=>q.UnitPrice).FirstOrDefault();
        if(quote==null||quote.Own||current==0)return new(false,null,quote,"Allagan Market competitor cache missing, stale or marked as our own price; one comparison needed");
        uint? target;
        if(silver!=null)
        {
            if(!silver.Valid)return new(false,null,quote,"Invalid silver strategy");
            target=quote.UnitPrice>=silver.MinimumPrice+silver.Undercut?quote.UnitPrice-silver.Undercut:silver.ResetPrice;
        }
        else target=current<=quote.UnitPrice?null:Pricing.Undercut(quote.UnitPrice,pricing,floor);
        if(target==current)target=null;
        var alternatives=silver!=null&&quote.UnitPrice<silver.MinimumPrice+silver.Undercut;
        return new(true,target,quote,alternatives?
            "Allagan Market cheapest price is below the silver threshold; compare once for higher qualifying competitors and buyout evidence":target.HasValue?
            "Allagan Market cache indicates a price change; shared comparison verifies before execution":
            "Allagan Market cache indicates no permitted price change; comparison skipped",alternatives);
    }
}
