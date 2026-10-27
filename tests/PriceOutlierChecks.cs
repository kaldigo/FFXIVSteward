using Steward.Core;
static class PriceOutlierChecks
{
    public static void Run(Action<bool,string> check)
    {
        Offering[] Offers(params uint[] prices)=>prices.Select((p,i)=>new Offering(42,false,p,1,(ulong)(100+i),(ulong)(1000+i))).ToArray();
        var band=Enumerable.Range(0,10).Select(i=>(uint)(1000+i*100)).ToArray();
        var own=new HashSet<ulong>();var pricing=new PricingSettings{UndercutPrice=5};
        for(var count=1;count<=3;count++)
        {
            var rows=Offers(Enumerable.Repeat(50u,count).Concat(band).ToArray());
            var choice=Pricing.SelectCompetition(rows,own);
            check(choice.Price==1000&&choice.IgnoredCheapListings==count,$"Ignore exactly {count} extreme outliers with ten supporting listings");
            check(Pricing.Target(42,false,3000,rows,own,QualityMode.Combined,pricing,1)==995,"Reprice uses filtered competition");
            check(Pricing.ListingTarget(choice.Price!.Value,null,pricing,1)==995,"New listing uses the same filtered competition");
        }
        check(Pricing.SelectCompetition(Offers(new uint[]{50}.Concat(band.Take(9)).ToArray()),own).Price==50,"Nine supporting listings are insufficient");
        check(Pricing.SelectCompetition(Offers(Enumerable.Repeat(50u,4).Concat(band).ToArray()),own).Price==50,"Four cheap listings are not ignored");
        check(Pricing.SelectCompetition(Offers(new uint[]{251}.Concat(band).ToArray()),own).Price==251,"Modest undercuts remain real competition");
        check(Pricing.SelectCompetition(Offers(new uint[]{250}.Concat(band).ToArray()),own).Price==1000,"Quarter-price boundary qualifies");
        check(Pricing.SelectCompetition(Offers(new uint[]{50}.Concat(band.Take(9)).Append(2001u).ToArray()),own).Price==50,"A wide spread does not establish ten similar listings");
        check(Pricing.SelectCompetition(Offers(new uint[]{50}.Concat(band.Take(9)).Append(2000u).ToArray()),own).Price==1000,"Twice-price band boundary qualifies");
        var weak=Offers(50,1000).Select(p=>p with{Quantity=999}).ToArray();
        check(Pricing.SelectCompetition(weak,own).Price==50,"Stack quantities never count as listing entries");
        var rows2=Offers(new uint[]{50}.Concat(band).ToArray());
        check(Pricing.SelectCompetition(rows2.Select(p=>p with{ListingId=1}),own).Price==50,"Duplicate rows cannot establish support");
        check(Pricing.SelectCompetition(rows2.Select(p=>p with{ListingId=0}),own).Price==50,"Unidentified listing rows cannot establish support");
        check(Pricing.SelectCompetition(rows2.Select(p=>p with{RetainerId=0}),own).Price==null,"Unknown sellers cannot bypass own-listing protection");
        own.Add(rows2[1].RetainerId);
        check(Pricing.SelectCompetition(rows2,own).Price==50,"Own listings do not count toward the ten competitors");own.Clear();
        var ownLow=new Offering(42,false,40,1,9,999);
        check(Pricing.Target(42,false,3000,rows2.Append(ownLow),new HashSet<ulong>{9},QualityMode.Combined,pricing,1)==null,"Cheap own listing is never removed by the outlier filter");
        check(Pricing.ListingTarget(1000,40,pricing,1)==40,"New stacks still match an existing cheapest own listing");
        var mixed=rows2.Select((p,i)=>p with{Hq=i>0}).ToArray();
        check(Pricing.Target(42,false,3000,mixed,own,QualityMode.Combined,pricing,1)==995,"Combined pools NQ/HQ listing support");
        check(Pricing.Target(42,false,3000,mixed,own,QualityMode.Separate,pricing,1)==45,"Separate never borrows HQ support for NQ pricing");
        check(Pricing.Target(42,false,3000,mixed.Select((p,i)=>i==0?p:p with{ItemId=43}),own,QualityMode.Combined,pricing,1)==45,"Other items cannot supply support");
        var e=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,StatisticsRequired=true};
        var now=DateTimeOffset.UtcNow;
        e.SaleStatistics[(42,false)]=new(83,42,false,100,1,now,now,50,true);
        var rules=new RuleSettings{Sales=[new(){MinimumUnit=500,MinimumTotal=500}]};
        var item=new ItemFacts(42,"Test",true,1,50,true,false,SourceKind.Ordinary,35);
        check(Rules.Judge(item,new(42,false,1,0,0),rules,e,83,now).SaleRejected,"Sale qualification still uses the low historical average, not the filtered 1000 listing price");
    }
}

