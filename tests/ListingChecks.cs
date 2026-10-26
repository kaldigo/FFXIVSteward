using Steward.Core;

static class ListingChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var pricing=new PricingSettings();var rule=new SaleRule{MaximumStack=99,MinimumUnit=1,MinimumTotal=0};
        ListingInput Input(uint id,uint quantity,uint competitor=100,uint? own=null,bool hq=false,ushort slot=0)=>
            new(new(id,"Sale item",false,1,1,true,false,SourceKind.Ordinary,1),new(id,hq,quantity,0,slot),new(Outcome.Market,"Matches sale rule",rule),rule,new(1,competitor,own,now,"Test quote"));
        ListingPlan Plan(IEnumerable<ListingInput> inputs,int slots=20,QualityMode quality=QualityMode.Separate)=>
            ListingPlanner.Plan(inputs,Enumerable.Range(0,slots).ToArray(),pricing,1,30,quality,now);
        var plan=Plan([Input(42,250)]);
        check(plan.Listings.Select(p=>p.Quantity).SequenceEqual(new uint[]{99,99,52}),"A large source stack splits repeatedly within one pass");
        check(plan.Listings.All(p=>p.UnitPrice==99),"Later chunks match own planned price without repeated self-undercutting");
        check(plan.Listings.Select(p=>p.Source.Quantity).SequenceEqual(new uint[]{250,151,52}),"Every split binds the expected remaining source quantity");
        pricing.UseMaxStackSize=true;pricing.MaximumStackSize=50;
        plan=Plan([Input(42,350)],3);
        check(plan.Listings.Count==3&&plan.Listings.All(p=>p.Quantity==50)&&plan.FreeSlotsRemaining==0,"Market Buddy cap and actual free slots both limit splitting");
        check(plan.Listings.Sum(p=>(long)p.Quantity)==150,"Slot limit leaves the unlisted remainder untouched");
        pricing.UseMaxStackSize=false;rule.MinimumTotal=6000;
        plan=Plan([Input(42,250),Input(43,99,100,slot:1)],3);
        check(plan.Listings.Select(p=>p.Source.ItemId).SequenceEqual(new uint[]{42,42,43}),"Low-value remainder is skipped and another item fills the free slot");
        check(plan.Held.Any(s=>s.Contains("total-value floor")),"Held split remainder has an explicit reason");
        rule.MinimumUnit=100;rule.MinimumTotal=0;
        plan=Plan([Input(42,99,100),Input(43,99,1000,slot:1)],1);
        check(plan.Listings.Count==1&&plan.Listings[0].Source.ItemId==43,"A below-floor candidate cannot reserve a slot ahead of an eligible item");
        rule.MinimumUnit=1;
        plan=Plan([Input(42,250,100,90)]);
        check(plan.Listings.Count==3&&plan.Listings.All(p=>p.UnitPrice==90),"Additional stacks match an already cheapest own listing");
        check(Pricing.Target(42,false,90,[new(42,false,90,1,7),new(42,false,100,1,8)],new HashSet<ulong>{7},QualityMode.Separate,pricing,1)==null,
            "Existing-listing repricing still never undercuts own cheapest");
        plan=Plan([Input(42,1),Input(42,1,200,hq:true,slot:1)]);
        check(plan.Listings.Select(p=>p.UnitPrice).SequenceEqual(new uint[]{99,199}),"Separate HQ/NQ keeps independent own-price anchors");
        plan=Plan([Input(42,1),Input(42,1,200,hq:true,slot:1)],20,QualityMode.Combined);
        check(plan.Listings.Select(p=>p.UnitPrice).SequenceEqual(new uint[]{99,99}),"Combined HQ/NQ shares the planned own-price anchor");
        foreach(var outcome in Enum.GetValues<Outcome>().Where(o=>o is not (Outcome.Market or Outcome.Dispose)))
        {
            var input=Input(42,99) with{Decision=new(outcome,"Unavailable or protected")};
            check(!ListingPlanner.Next(input,pricing,1,30,now).Eligible,"Protected/unavailable outcome never lists: "+outcome);
        }
        var missingPrice=Input(42,99) with{Decision=new(Outcome.Wait,"No fresh competing price; disposal is blocked"),Reference=null};
        check(!ListingPlanner.Next(missingPrice,pricing,1,30,now).Eligible,"Missing prices never produce invented targets");
        check(ListingPlanner.Next(missingPrice with{Reference=Input(42,99).Reference},pricing,1,30,now).Eligible,"A validated fresh comparison can resolve price-only waiting");
        var original=Input(42,99);
        foreach(var reference in new[]{original.Reference! with{World=2},original.Reference! with{Observed=now.AddMinutes(-31)},original.Reference! with{Observed=now.AddSeconds(1)},original.Reference! with{Competitor=null}})
            check(!ListingPlanner.Next(original with{Reference=reference},pricing,1,30,now).Eligible,"Wrong-world, stale, future or empty prices hold listing");
        check(!ListingPlanner.Next(original,pricing,0,30,now).Eligible,"Unknown world cannot generate prices");
        check(Plan([original],0).Listings.Count==0,"Full market leaves all stock untouched");
        plan=ListingPlanner.Plan([Input(42,250)],new[]{1,8,19},pricing,1,30,QualityMode.Separate,now);
        check(plan.Listings.Select(p=>p.MarketSlot).SequenceEqual(new[]{1,8,19}),"Preview fills actual holes instead of assuming contiguous free slots");
        try{_=ListingPlanner.Plan([original],new[]{1,1},pricing,1,30,QualityMode.Separate,now);throw new Exception("Duplicate slot accepted");}
        catch(InvalidDataException){check(true,"Conflicting market capacity cannot allocate twice");}
        try{_=Plan([original,original with{Stock=original.Stock with{Quantity=200}}]);throw new Exception("Conflicting source accepted");}
        catch(InvalidDataException){check(true,"Conflicting source snapshots cannot double-spend inventory");}
        pricing.UndercutUsePercent=true;pricing.UndercutPercent=10;pricing.EnablePriceRounding=true;
        var choice=ListingPlanner.Next(Input(42,10,101),pricing,1,30,now);
        check(choice.UnitPrice==90&&choice.Total==900,"Preview uses percentage, rounding and the actual stack total");
        pricing=new();var pass=new ListingPass();var blocked=Input(42,99).Stock;var next=Input(43,99,slot:1).Stock;
        pass.Rejected(blocked);check(pass.Next([blocked,next],19)==next,"A skipped source does not consume the final slot");
        check(pass.Next([next],20)==null,"The live pass rechecks market capacity");
        pass.Rejected(next);check(pass.Next([blocked,next],0)==null,"Exhausted candidates terminate rather than loop forever");
        check(pass.Next([next with{Quantity=98}],0)!=null,"A changed remaining stack has a new identity for the live pass");
        // Conservation and threshold invariants over different stack caps, remainders and slot counts.
        foreach(var quantity in new uint[]{1,49,50,51,99,100,250,9999})foreach(var cap in new[]{1,20,50,99})foreach(var slots in new[]{0,1,3,20})
        {
            rule.MaximumStack=cap;rule.MinimumTotal=0;plan=Plan([Input(42,quantity)],slots);
            check(plan.Listings.Count<=slots&&plan.Listings.Sum(p=>(long)p.Quantity)<=quantity,"Split plan conserves stock and slots");
            check(plan.Listings.All(p=>p.Quantity>0&&p.Quantity<=cap&&p.UnitPrice==99&&p.Total==(ulong)p.Quantity*99),"Every planned split obeys quantity and price rules");
        }
    }
}
