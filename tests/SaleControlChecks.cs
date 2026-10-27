using Steward;
using System.Text.Json;
using Steward.Core;
static class SaleControlChecks
{
    public static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;
        var e=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,StatisticsRequired=true};
        e.SaleStatistics[(42,false)]=new(83,42,false,5000,1,now,now,700,true);
        e.SaleStatistics[(42,true)]=new(83,42,true,1000,2,now,now,900,true);
        check(SaleStatistics.CheapestListing(e,42,true,QualityMode.Combined,83,now)?.MinimumListing==700,"Combined ignores stock quality and average-price ranking");
        check(SaleStatistics.CheapestListing(e,42,true,QualityMode.Separate,83,now)?.MinimumListing==900,"Separate retains quality scope");
        e.SaleStatistics[(42,true)]=e.SaleStatistics[(42,true)] with{Average=null,ValueObserved=null,MinimumListing=600};
        check(SaleStatistics.CheapestListing(e,42,false,QualityMode.Combined,83,now)?.MinimumListing==600,"Listing evidence does not require a sale average");
        e.SaleStatistics[(42,false)]=e.SaleStatistics[(42,false)] with{MinimumListing=0};
        check(SaleStatistics.CheapestListing(e,42,false,QualityMode.Separate,83,now)==null,"Zero is not a listing price");
        check(SaleStatistics.CheapestListing(e,42,false,QualityMode.Combined,84,now)==null,"Other world prices cannot leak");
        check(SaleStatistics.CheapestListing(e,42,false,QualityMode.Combined,83,now.AddSeconds(-1))==null,"Future price timestamps cannot leak");
        var item=new ItemFacts(42,"Test",true,1,50,true,false,SourceKind.Ordinary,35);
        var stock=new Stock(42,false,10,12002,0);
        var first=new SaleRule{Name="First",MinimumUnit=100,MinimumTotal=100,MaximumStack=30,MinimumDailySales=.1,UseRetainerFilter=true,ExcludedRetainers=[2]};
        var second=new SaleRule{Name="Second",MinimumUnit=200,MinimumTotal=200,MaximumStack=30};
        var rules=new RuleSettings{Sales=[first,second]};
        check(Rules.Judge(item,stock,rules,e,83,now,1).Rule==first,"First matching rule on included retainer");
        check(Rules.Judge(item,stock,rules,e,83,now,2).Rule==second,"Excluded first rule falls through to next matching rule");
        first.UseRetainerFilter=false;
        check(Rules.Judge(item,stock,rules,e,83,now,2).Rule==first,"Optional per-rule filter can be disabled");
        first.UseRetainerFilter=true;
        var config=new Configuration{SafetyRevision=8,PendingCollection=[],ListingWindows=new(),ExcludedSaleRetainers=[1],Rules=rules};
        check(!config.SalesEnabledFor(1)&&config.SalesEnabledFor(2)&&!config.SalesEnabledFor(0),"Global exclusions and unknown retainer gate all sale automation");
        var restored=JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
        check(!restored.SalesEnabledFor(1)&&restored.Rules.Sales[0].ExcludedRetainers.Contains(2),"Both retainer scopes survive restart");
        var pricing=new PricingSettings{UseMaxStackSize=true,MaximumStackSize=30};
        var market=new Decision(Outcome.Market,"eligible",first,1000,MaximumRemovable:10);
        check(ListingPolicy.ReturnReason(market,first,stock,1000,pricing)==null,"Valid listing stays up");
        check(ListingPolicy.ReturnReason(market,null,stock,1000,pricing)!=null,"No matching sale rule withdraws existing listing");
        check(ListingPolicy.ReturnReason(market with{SaleRejected=true},first,stock,1000,pricing)!=null,"Known velocity/value rejection withdraws existing listing");
        check(ListingPolicy.ReturnReason(new(Outcome.Wait,"Unknown sales rate"),first,stock,1000,pricing)==null,"Missing evidence holds listing");
        check(ListingPolicy.ReturnReason(new(Outcome.Ignore,"Excluded"),null,stock,1000,pricing)==null,"Item exclusion overrides listing cleanup");
        check(ListingPolicy.ReturnReason(new(Outcome.Keep,"Explicit AutoRetainer protection"),null,stock,1000,pricing)==null,"Upstream protection gates withdrawals");
        check(ListingPolicy.ReturnReason(market,first,stock,99,pricing)!=null,"Current price below unit floor withdraws");
        first.MinimumTotal=20000;
        check(ListingPolicy.ReturnReason(market,first,stock,1000,pricing)!=null,"Current total below floor withdraws");first.MinimumTotal=100;
        check(ListingPolicy.ReturnReason(market,first,stock with{Quantity=31},1000,pricing)!=null,"Oversized existing stacks withdraw for splitting");
        check(ListingPolicy.ReturnReason(new(Outcome.CollectDresser,"Missing collection copy"),first,stock,1000,pricing)!=null,"A protected last copy can safely be returned from market to inventory");
        check(ListingPolicy.ReturnReason(new(Outcome.Wait,"Average sale value unavailable; sale and disposal held"),null,stock,1000,pricing)!=null,"Known no-rule scope failure does not need sale prices to return safely");
        check(ListingPolicy.ReturnReason(new(Outcome.Wait,"Glamour references or gear sets are unavailable"),null,stock,1000,pricing)==null,"No-rule failure never bypasses unknown protections");
        var memory=new ListingWindows();memory.QueueReturn(1,83,2,stock,now,()=>{});
        var saved=JsonSerializer.Deserialize<ListingWindows>(JsonSerializer.Serialize(memory))!;
        check(saved.Holds.Count==0&&saved.Priority(1,83,2,stock)==now,"Returned items retain FIFO order without gaining protection across restart");
        e.RelistingHolds.Add(42);rules.Sales.Clear();
        check(Rules.Judge(item,stock,rules,e,83,now).Outcome==Outcome.Dispose,"Removed sale rules permit ordinary gear disposal despite legacy return flag");
        rules.Keep.Add(42);check(Rules.Judge(item,stock,rules,e,83,now).Outcome==Outcome.Keep,"Explicit keep still protects returned stock");rules.Keep.Clear();
        e.References.Add(42);check(Rules.Judge(item,stock,rules,e,83,now).Outcome==Outcome.CollectDresser,"Glamour protection still preserves returned last copy");e.References.Clear();
        check(Rules.Judge(item with{Equipment=false},stock,rules,e,83,now).Outcome==Outcome.Keep,"Returning non-equipment does not grant disposal permission");
    }
}
