using System.Net;
using System.Text.Json;
using Steward.Core;
using Steward.Services;

static class FeatureAuditChecks
{
    public static async Task Run(Action<bool,string> check,DateTimeOffset now)
    {
        var facts=new ItemFacts(14799,"Wolf Mark glamour",true,3,60,false,false,SourceKind.Special,1);
        var stock=new Stock(facts.Id,false,1,10002,13);
        var evidence=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,DresserFreeSlots=215};
        evidence.References.Add(facts.Id);
        var rules=new RuleSettings{Sales=[new(){MinimumUnit=1000,MinimumTotal=1000}]};
        Decision Judge()=>Rules.Judge(facts,stock,rules,evidence,83,now);
        check(Judge().Outcome==Outcome.CollectDresser,"Referenced Wolf Mark retainer item takes collection path before sale");
        evidence.DresserFreeSlots=20;check(Judge().Outcome==Outcome.StoreRetainer,"Full dresser routes Wolf Mark gear to protected overflow");
        evidence.PendingCollection.Add(facts.Id);
        evidence.References.Clear();facts=facts with{Rarity=1,Source=SourceKind.NormalDungeon};
        evidence.PendingCollection=JsonSerializer.Deserialize<HashSet<uint>>(JsonSerializer.Serialize(evidence.PendingCollection))!;
        check(Judge().Outcome==Outcome.StoreRetainer,"Persisted overflow remains protected after references and acquisition change");
        evidence.Dresser.Add(facts.Id);evidence.DresserCopyCounts[facts.Id]=1;
        check(StoragePolicy.WithoutDresserCopy(evidence,facts.Id,1,Judge).Outcome==Outcome.StoreRetainer,"Cleanup cannot remove pending last copy when dresser is full");
        check(StoragePolicy.CollectionConfirmed(facts,evidence),"Exact stored collection copy can release overflow hold");
        foreach(var source in new[]{SourceKind.AllianceRaid,SourceKind.SavageRaid,SourceKind.HighEnd})
        {
            evidence.PendingCollection.Clear();evidence.Dresser.Clear();evidence.DresserFreeSlots=100;
            facts=facts with{Source=source,Rarity=3};check(Judge().Outcome==Outcome.CollectDresser,"Hard raid gear remains protected: "+source);
        }
        facts=new(42,"Market material",false,1,0,true,false,SourceKind.Ordinary,1);stock=new(42,false,10,0,1);
        evidence.StatisticsRequired=true;
        check(Judge().Outcome==Outcome.Wait,"Missing summary is unknown, never zero value");
        var stat=new SaleStatistic(83,42,false,5000,0.07,now,now,4500,true);
        evidence.SaleStatistics[(42,false)]=stat;
        check(Judge().Outcome==Outcome.Market,"Valuable fortnightly seller qualifies without velocity filter");
        rules.Sales[0].MinimumDailySales=1;
        var rejected=Judge();
        check(rejected.SaleRejected&&!ListingPlanner.CanConsider(facts,stock,rejected,rules.Sales[0]),"Live listing planner cannot bypass a failed sales-velocity rule");
        rules.Sales[0].MinimumDailySales=null;
        evidence.SaleStatistics[(42,false)]=SaleStatistics.Merge(stat with{Average=null,DailySales=null,Fetched=now,ValueObserved=null,RecentSales=false},stat with{ValueObserved=now.AddDays(-14)});
        check(Judge().Outcome==Outcome.Market,"Older known value survives an empty four-day sales window");
        rules.Sales[0].AllowOlderSaleValue=false;check(Judge().Outcome==Outcome.Wait,"Rule can require recent sales");
        rules.Sales[0].AllowOlderSaleValue=true;rules.Sales[0].MinimumDailySales=0.01;
        check(Judge().Outcome==Outcome.Wait,"Unknown velocity cannot satisfy an enabled threshold");
        rules.Sales[0].MinimumDailySales=null;rules.Sales[0].MinimumUnit=10000;
        rules.NonEquipmentDisposal.Add(42);
        check(Judge().Outcome==Outcome.Wait,"Older low value never authorizes material disposal");
        evidence.SaleStatistics[(42,false)]=stat;check(Judge().Outcome==Outcome.Dispose,"Recent value below explicit sale floor permits configured disposal");
        rules.Sales[0].MinimumUnit=1000;
        var otherWorld=stat with{World=84};evidence.SaleStatistics[(42,false)]=otherWorld;
        check(Judge().Outcome==Outcome.Wait,"World-specific summaries never cross worlds");
        evidence.SaleStatistics[(42,false)]=stat with{Fetched=now.AddHours(1)};
        check(Judge().Outcome==Outcome.Wait,"Future cached summary is held");
        evidence.SaleStatistics[(42,false)]=stat;
        evidence.ReferencesReady=false;check(Judge().Outcome==Outcome.Wait,"Average values cannot bypass unavailable protection");
        evidence.ReferencesReady=true;
        var json="""{"results":[{"itemId":42,"nq":{"averageSalePrice":{"world":{"price":5000}},"dailySaleVelocity":{"world":{"quantity":0.07}},"minListing":{"world":{"price":4500}}},"hq":{"averageSalePrice":null}}],"failedItems":[43]}""";
        var parsed=SaleStatistics.Parse(json,83,new HashSet<uint>{42,43},now);
        check(parsed.Count==4&&parsed.Single(s=>s.ItemId==42&&!s.Hq).Average==5000,"Aggregate parses compact world summaries and isolates empty qualities/items");
        check(parsed.Where(s=>s.ItemId==43).All(s=>s.Average==null),"Failed items remain unknown");
        var old=stat with{ValueObserved=now.AddDays(-14)};
        var merged=SaleStatistics.Merge(parsed.First(s=>s.ItemId==43) with{ItemId=42},old);
        check(merged.ValueObserved==old.ValueObserved&&!merged.RecentSales,"Empty result retains value age rather than freshening it");
        var rejectedScope=false;
        try{SaleStatistics.Parse(json,83,new HashSet<uint>{99},now);}catch(InvalidDataException){rejectedScope=true;}
        check(rejectedScope,"Unrequested aggregate ID rejects response");
        rules.QuantityReserves[42]=3;evidence.ReserveStocks=[stock];evidence.SaleStatistics[(42,false)]=stat;
        var reserved=Judge();
        check(reserved.Outcome==Outcome.Market&&reserved.MaximumRemovable==7,"Quantity reserve caps removable quantity without suppressing surplus sale");
        var plan=ListingPlanner.Plan([new(facts,stock,reserved,rules.Sales[0],new(83,4500,null,now,"native"))],Enumerable.Range(0,20).ToArray(),new PricingSettings{UseMaxStackSize=true,MaximumStackSize=3},83,30,QualityMode.Separate,now);
        check(plan.Listings.Sum(p=>p.Quantity)==7&&plan.Listings.Select(p=>p.Quantity).SequenceEqual(new uint[]{3,3,1}),"Repeated stack splits leave the reserve untouched");
        check(!MarketSubmission.CanCommit(stock,stock,reserved,false,4500,8,4500,8,99),"Final submission cannot consume a reserved quantity");
        rules.Sales[0].MinimumUnit=10000;check(Judge().Outcome==Outcome.Keep,"Whole-stack disposal cannot consume partial reserve");
        evidence.ReserveStocks.Clear();check(Judge().Outcome==Outcome.Wait,"Cached/offline quantity cannot justify consuming reserve");
        for(uint quantity=1;quantity<=30;quantity++)for(uint reserve=0;reserve<=35;reserve++)
        {
            var first=new Stock(42,false,quantity,0,0);var second=new Stock(42,true,10,0,1);
            var allowed=QuantityReserve.Removable(first,reserve,[first,second])!.Value+QuantityReserve.Removable(second,reserve,[first,second])!.Value;
            check(allowed==Math.Max(0,(long)quantity+10-reserve),"Reserve allocation across quality and slots preserves requested quantity");
        }
        rules.QuantityReserves.Clear();rules.Sales[0].MinimumUnit=1000;
        facts=facts with{Equipment=true,Source=SourceKind.NormalDungeon,EquipLevel=90};rules.CollectionKeep.Add(42);
        check(Judge().Outcome==Outcome.CollectDresser,"Supplemental collection list sends ordinary gear to dresser");
        evidence.Dresser.Add(42);check(Judge().Outcome==Outcome.Market,"Collected supplemental copy permits surplus sale");
        check(!StoragePolicy.Restore(StoragePolicy.WithoutDresserCopy(evidence,42,1,Judge).Outcome,true,false),"Supplemental last copy cannot be removed by cleanup");
        rules.CollectionKeep.Clear();evidence.Dresser.Clear();
        var handler=new Handler(json);
        using(var client=new AggregatePrices(handler:handler))
        {
            client.Tick(83,[42,43],new HashSet<ulong>(),6,evidence,true,now);
            for(var i=0;i<100&&!evidence.SaleStatistics.ContainsKey((43,false));i++)
            {await Task.Delay(1);client.Tick(83,[],new HashSet<ulong>(),6,evidence,true,now);}
            check(evidence.SaleStatistics[(42,false)].Average==5000&&handler.Calls==1,"Completed background summary immediately available without all-inventory barrier");
            check(handler.Uri!.AbsolutePath.Contains("/aggregated/83/")&&handler.Uri.Query=="","Compact request contains no individual listings");
            client.Tick(83,[42,43],new HashSet<ulong>(),6,evidence,true,now.AddHours(5));
            check(handler.Calls==1,"Summary not re-fetched inside six-hour cache window");
            client.Apply(84,new HashSet<ulong>(),6,evidence,true,now);check(evidence.SaleStatistics.Count==0,"Switching world clears applied summaries");
            client.Apply(83,new HashSet<ulong>(),6,evidence,false,now);check(!evidence.StatisticsRequired&&evidence.SaleStatistics.Count==0,"Disabling summaries clears their evidence");
        }
        var bulk=new Handler(json);
        using(var client=new AggregatePrices(handler:bulk))
        {
            evidence=new();var ids=Enumerable.Range(1,130).Select(i=>(uint)i).ToArray();
            client.Tick(83,ids,new HashSet<ulong>(),6,evidence,true,now);
            for(var i=0;i<100&&evidence.SaleStatistics.Count==0;i++){await Task.Delay(1);client.Tick(83,[],new HashSet<ulong>(),6,evidence,true,now);}
            check(bulk.Uri!.AbsolutePath.Split('/').Last().Split(',').Length==100,"Cold prefetch batches at most 100 item summaries");
            client.Tick(83,ids,new HashSet<ulong>(),6,evidence,true,now.AddSeconds(1));
            check(bulk.Calls==2&&bulk.Uri!.AbsolutePath.Split('/').Last().Split(',').Length==30,"Empty items do not starve the next prefetch batch");
        }
        var limited=new LimitedHandler();
        using(var client=new AggregatePrices(handler:limited))
        {
            evidence=new();client.Tick(83,[42],new HashSet<ulong>(),6,evidence,true,now);
            for(var i=0;i<100&&!client.Status.Contains("unavailable");i++){await Task.Delay(1);client.Tick(83,[],new HashSet<ulong>(),6,evidence,true,now);}
            client.Tick(83,[42],new HashSet<ulong>(),6,evidence,true,now.AddSeconds(30));
            check(limited.Calls==1,"Aggregate client honors Retry-After");
            client.Tick(83,[42],new HashSet<ulong>(),6,evidence,true,now.AddSeconds(61));check(limited.Calls==2,"Aggregate client retries after rate-limit delay");
        }
        var delayed=new DelayedHandler();var cancelled=new AggregatePrices(handler:delayed);
        cancelled.Tick(83,[42],new HashSet<ulong>(),6,new(),true,now);cancelled.Dispose();
        check(delayed.Cancelled,"Active aggregate request is cancelled on unload");
        var directory=Path.Combine(Path.GetTempPath(),"Steward-summary-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"prices.json");
        try
        {
            using(var writer=new AggregatePrices(path,new Handler(json)))
            {
                writer.Tick(83,[42],new HashSet<ulong>(),6,new(),true,now);
                for(var i=0;i<500&&!File.Exists(path);i++){await Task.Delay(2);writer.Tick(83,[],new HashSet<ulong>(),6,new(),true,now);}
                check(File.Exists(path),"Background fetch persists cache without blocking review");
            }
            using var loaded=new AggregatePrices(path,new Handler(json));
            loaded.Apply(83,new HashSet<ulong>(),6,evidence,true,now);
            check(evidence.SaleStatistics[(42,false)]==stat,"Persistent summary is immediately available after restart, with original value age");
        }
        finally{Directory.Delete(directory,true);}
        check(Imports.MarketCache("42,N,83,0,2026-10-25,1000,N\n").Single().ItemId==42,"Headerless Allagan cache retains first item");
        check(Imports.MarketCache("ItemId,Hq,World,Source,At,Price,Own\n42,N,83,0,2026-10-25,1000,N\n").Count==1,"Headered Allagan cache still supported");
    }
    private sealed class LimitedHandler:HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Calls++;var response=new HttpResponseMessage((HttpStatusCode)429);response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));return Task.FromResult(response);}
    }
    private sealed class DelayedHandler:HttpMessageHandler
    {
        public bool Cancelled;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {token.Register(()=>Cancelled=true);return new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token);}
    }
    private sealed class Handler(string body):HttpMessageHandler
    {
        public int Calls;public Uri? Uri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Calls++;Uri=request.RequestUri;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});}
    }
}
