using Steward.Core;

internal static class CachedRepricingChecks
{
    public static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;
        var pricing=new PricingSettings{UndercutPrice=5};
        Quote q=new(13116,false,83,44790,now,"Allagan Market/0");
        var silver=new SilverStrategy{Enabled=true,Retainer=123};
        CachedPriceDecision Decide(IEnumerable<Quote> rows,uint current=44961,SilverStrategy? strategy=null,QualityMode quality=QualityMode.Separate)=>
            CachedRepricing.Decide(rows,83,13116,false,quality,current,pricing,1000,now,2,strategy);
        check(Decide([q]).Target==44785,"Normal cached undercut is five gil");
        check(!Decide([q],44000).NeedsComparison,"Cheaper ordinary listing skips comparison");
        check(!Decide([q],44790).NeedsComparison,"Equal ordinary listing does not undercut itself");
        check(Decide([q],44961,silver).Target==44785,"Silver lowers from cached competition");
        check(Decide([q with{UnitPrice=50000}],44961,silver).Target==49995,"Silver raises from cached competition");
        check(!Decide([q],44785,silver).NeedsComparison,"Silver already at target skips comparison");
        check(Decide([q with{UnitPrice=50}],44961,silver).Target==45000,"Below-floor silver preview proposes reset");
        check(Decide([q with{UnitPrice=50}],45000,silver).NeedsComparison,"Even at reset, below-floor cache must check higher alternatives");
        foreach(var invalid in new[]{q with{Own=true},q with{World=84},q with{ItemId=2},q with{Observed=now.AddHours(-3)},q with{Observed=now.AddSeconds(1)},q with{UnitPrice=0},q with{Source="Allagan Market/3"}})
            check(!Decide([invalid]).Known&&Decide([invalid]).NeedsComparison,"Invalid/missing/own/manual cache cannot skip checks");
        check(!Decide([]).Known,"Absent cache is unknown, never empty market");
        check(Decide([q,q with{Hq=true,UnitPrice=10000}],quality:QualityMode.Combined).Target==9995,"Combined chooses cheapest across HQ and NQ");
        check(Decide([q,q with{Hq=true,UnitPrice=10000}]).Target==44785,"Separate ignores the other quality");
        check(!Decide([q with{UnitPrice=1000}]).Target.HasValue,"Undercut cannot violate rule floor");
    }
}
