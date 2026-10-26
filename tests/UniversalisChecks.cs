using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Steward.Core;
using Steward.Services;

static class UniversalisChecks
{
    static string Listing(uint price,bool hq=false,string? seller="8",bool mannequin=false)=>JsonSerializer.Serialize(new{pricePerUnit=price,quantity=10,hq,retainerID=seller,onMannequin=mannequin});
    static string Item(DateTimeOffset at,string listings,uint world=1,uint id=42)=>
        "{\"itemID\":"+id+",\"worldID\":"+world+",\"lastUploadTime\":"+at.ToUnixTimeMilliseconds()+",\"listings\":["+listings+"]}";
    public static async Task Run(Action<bool,string> check,DateTimeOffset now)
    {
        HashSet<uint> ids=[42];HashSet<ulong> own=[7];
        var raw=Item(now,string.Join(',',Listing(10,seller:"7"),Listing(100),Listing(200,true)));
        var snapshot=UniversalisData.Parse(raw,1,ids).Single();var quotes=snapshot.Quotes(own,now,30);
        check(quotes.Count==2&&quotes.Single(q=>!q.Hq).UnitPrice==100&&quotes.Single(q=>q.Hq).UnitPrice==200,"Universalis keeps NQ/HQ separate and excludes own cheapest listing");
        check(quotes.All(q=>q.Observed==snapshot.Uploaded&&q.Source.StartsWith("Universalis")),"Public quote uses server upload age, never request time");
        check(snapshot.OwnQuotes(own,now,30).Single().UnitPrice==10,"Own cached prices remain separately available for preview matching");
        var publicEvidence=new Evidence{Prices=quotes.ToDictionary(q=>(q.ItemId,q.Hq)),ReferencesReady=false,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
        var saleRules=new RuleSettings{Sales=[new(){MinimumUnit=1,MinimumTotal=1}]};
        var gear=new ItemFacts(42,"Protected gear",true,3,90,true,false,SourceKind.SavageRaid,1);var stock=new Stock(42,false,10,0,0);
        check(Rules.Judge(gear,stock,saleRules,publicEvidence,1,now).Outcome==Outcome.Wait,"Fresh public prices never bypass unavailable protection references");
        publicEvidence.ReferencesReady=true;
        check(Rules.Judge(gear,stock,saleRules,publicEvidence,1,now).Outcome==Outcome.CollectDresser,"Fresh public prices never bypass protected collection rules");
        publicEvidence.Dresser.Add(42);
        check(StoragePolicy.WithoutDresserCopy(publicEvidence,42,1,()=>Rules.Judge(gear,stock,saleRules,publicEvidence,1,now)).Outcome==Outcome.CollectDresser,"Public prices cannot authorize sale of a protected last dresser copy");
        check(snapshot.Quotes(new HashSet<ulong>(),now,30).Count==0,"Unknown ownership never assumes every seller is a competitor");
        check((snapshot with{Uploaded=now.AddMinutes(-31)}).Quotes(own,now,30).Count==0,"New HTTP response cannot freshen stale uploads");
        check((snapshot with{Uploaded=now.AddSeconds(1)}).Quotes(own,now,30).Count==0,"Future public upload time is held");
        check(UniversalisData.Parse(Item(now,Listing(10,seller:"7")),1,ids).Single().Quotes(own,now,30).Count==0,"Own-only public listings produce no competing valuation");
        check(UniversalisData.Parse(Item(now,string.Join(',',Listing(1,seller:null),Listing(100))),1,ids).Single().Quotes(own,now,30).Count==0,"Unknown seller blocks that quality rather than inventing competition");
        check(UniversalisData.Parse(Item(now,Listing(10,seller:"opaque-hash")),1,ids).Single().Quotes(own,now,30).Count==0,"Opaque retainer hashes do not bypass own-price protection");
        check(UniversalisData.Parse(Item(now,string.Join(',',Listing(1,mannequin:true),Listing(100))),1,ids).Single().Quotes(own,now,30).Single().UnitPrice==100,"Mannequin listings are not normal competitors");
        check(UniversalisData.ParseBatch(Item(now,""),1,ids) is {Items.Count:0,Held.Count:1},"Empty listing response is held individually, never valued as zero");
        foreach(var invalid in new[]{Item(now,Listing(100),world:2),Item(now,Listing(100),id:43),Item(now,Listing(0)),"{}","{"})
        {
            var rejected=false;try{UniversalisData.Parse(invalid,1,ids);}catch(Exception ex)when(ex is InvalidDataException or JsonException or KeyNotFoundException){rejected=true;}
            check(rejected,"Malformed, wrong-world/item or zero price response is rejected");
        }
        var multi="{\"worldID\":1,\"items\":{\"42\":"+Item(now,Listing(100))+",\"43\":"+Item(now,Listing(200),id:43)+"},\"unresolvedItems\":[44]}";
        var partial=UniversalisData.ParseBatch(multi,1,new HashSet<uint>{42,43,44});
        check(partial.Items.Count==2&&partial.Held.Single().ItemId==44,"Partial batch responses retain valid values and identify missing items");
        var soiled="{\"worldID\":1,\"items\":{\"42\":"+Item(now,Listing(100))+",\"5433\":"+Item(DateTimeOffset.UnixEpoch,"",id:5433)+"}}";
        partial=UniversalisData.ParseBatch(soiled,1,new HashSet<uint>{42,5433});
        check(partial.Items.Single().ItemId==42&&partial.Held.Single().ItemId==5433,"Soiled Femur's zero upload and empty listings cannot poison the batch");
        foreach(var bad in new[]{Item(now,Listing(0),id:5433),Item(now,Listing(100),world:2,id:5433),"{}"})
        {
            var mixed="{\"worldID\":1,\"items\":{\"42\":"+Item(now,Listing(100))+",\"5433\":"+bad+"}}";
            partial=UniversalisData.ParseBatch(mixed,1,new HashSet<uint>{42,5433});
            check(partial.Items.Single().ItemId==42&&partial.Held.Single().ItemId==5433,"Malformed per-item data holds that ID and retains other valid prices");
        }
        var evidence=new Evidence();var handler=new FakeHandler(_=>new(HttpStatusCode.OK){Content=new StringContent(raw)});
        using(var client=new UniversalisPrices(handler))
        {
            client.Tick(1,[42],own,30,evidence,true,now);
            await Drain(client,evidence,own,now);
            check(handler.Calls==1&&evidence.Prices[(42,false)].UnitPrice==100,"Background HTTP result becomes a cached competitor quote");
            check(handler.Uri!.Host=="universalis.app"&&handler.Uri.Query.Contains("listings=100&entries=0")&&handler.Method==HttpMethod.Get,"Only current listings are requested over read-only HTTPS");
            client.Tick(1,[42],own,30,evidence,true,now.AddSeconds(10));check(handler.Calls==1,"Successful item fetches are cached rather than repeated every scan");
            client.Apply(2,own,30,evidence,true,now);check(evidence.Prices.Count==0&&evidence.PublicOwnPrices.Count==0,"World change cannot reuse public prices from another world");
            client.Apply(1,own,30,evidence,true,now);check(evidence.Prices.Count==2,"World-specific cached data can be reapplied safely");
            evidence.Prices[(42,false)]=new(42,false,1,120,now.AddSeconds(1),"Game");
            client.Apply(1,own,30,evidence,true,now.AddSeconds(2));check(evidence.Prices[(42,false)].Source=="Game","Newer in-game comparison wins over public upload");
            client.Apply(1,own,30,evidence,false,now);check(evidence.Prices.Count==1&&evidence.Prices[(42,false)].Source=="Game","Disabling source removes its quotes without deleting native evidence");
            evidence.Prices.Clear();client.Apply(1,new HashSet<ulong>{7,8},30,evidence,true,now);check(evidence.Prices.Count==0,"Newly identified own retainers invalidate cached competing quotes");
        }
        handler=new(request=>new(HttpStatusCode.OK){Content=new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/44")?Item(now.AddSeconds(6),Listing(200),id:44):soiled)});
        using(var client=new UniversalisPrices(handler))
        {
            evidence=new();client.Tick(1,[42,5433],own,30,evidence,true,now);await Drain(client,evidence,own,now);
            check(evidence.Prices[(42,false)].UnitPrice==100&&!evidence.Prices.Keys.Any(k=>k.Item1==5433),"Client applies usable batch members while leaving unpriced items unknown");
            check(client.HeldItems(1).Single().ItemId==5433&&!client.Status.Contains("unavailable"),"Item-level hold is diagnostic, not an API outage");
            client.Tick(1,[42,5433,44],own,30,evidence,true,now.AddSeconds(6));
            check(handler.Calls==2&&handler.Uri!.AbsolutePath.EndsWith("/44"),"Successful partial batch advances to later items instead of retrying the same unpriced ID");
            await Drain(client,evidence,own,now.AddSeconds(6));
            check(evidence.Prices[(44,false)].UnitPrice==200&&client.HeldItems(1).Single().ItemId==5433,"Later item pricing completes while the original unpriced item stays held");
        }
        var recoveryCalls=0;
        handler=new(_=>new(HttpStatusCode.OK){Content=new StringContent(++recoveryCalls==1?Item(DateTimeOffset.UnixEpoch,"",id:5433):Item(now.AddSeconds(301),Listing(100),id:5433))});
        using(var client=new UniversalisPrices(handler))
        {
            evidence=new();client.Tick(1,[5433],own,30,evidence,true,now);await Drain(client,evidence,own,now);
            check(client.HeldItems(1).Count==1&&evidence.Prices.Count==0,"Entire unpriced batch is a successful item-level hold");
            client.Tick(1,[5433],own,30,evidence,true,now.AddSeconds(301));await Drain(client,evidence,own,now.AddSeconds(301));
            check(client.HeldItems(1).Count==0&&evidence.Prices[(5433,false)].UnitPrice==100,"Fresh later data releases the price hold without a restart");
        }
        handler=new(_=>{var r=new HttpResponseMessage((HttpStatusCode)429);r.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));return r;});
        using(var client=new UniversalisPrices(handler))
        {
            evidence=new();client.Tick(1,[42],own,30,evidence,true,now);await Drain(client,evidence,own,now);
            client.Tick(1,[42],own,30,evidence,true,now.AddSeconds(30));
            check(handler.Calls==1&&evidence.Prices.Count==0&&client.Status.Contains("429"),"Rate-limit response respects Retry-After and holds missing prices");
            client.Tick(1,[42],own,30,evidence,true,now.AddSeconds(61));check(handler.Calls==2,"Rate-limited fetch can retry after the server delay");
        }
        handler=new(_=>new(HttpStatusCode.OK){Content=new StringContent("invalid")});
        using(var client=new UniversalisPrices(handler))
        {
            evidence=new();client.Tick(1,[42],own,30,evidence,true,now);await Drain(client,evidence,own,now);
            check(evidence.Prices.Count==0&&client.Status.Contains("unavailable"),"Malformed HTTP response never becomes a disposal price");
        }
        handler=new(_=>new(HttpStatusCode.OK){Content=new StringContent(new string(' ',4*1024*1024+1))});
        using(var client=new UniversalisPrices(handler))
        {
            evidence=new();client.Tick(1,[42],own,30,evidence,true,now);await Drain(client,evidence,own,now);
            check(evidence.Prices.Count==0&&client.Status.Contains("unavailable"),"Oversized response is rejected before parsing");
        }
        handler=new(_=>new(HttpStatusCode.ServiceUnavailable));
        using(var client=new UniversalisPrices(handler))
        {
            evidence=new();client.Tick(1,[42],own,30,evidence,false,now);check(handler.Calls==0,"Disabled source performs no requests");
            client.Tick(0,[42],own,30,evidence,true,now);check(handler.Calls==0,"Unknown world performs no requests");
            client.Tick(1,Enumerable.Range(1,200).Select(i=>(uint)i),own,30,evidence,true,now);await Drain(client,evidence,own,now);
            check(handler.Uri!.AbsolutePath.Split('/').Last().Split(',').Length==20,"Inventory requests use bounded twenty-item batches");
        }
        handler=new(_=>new(HttpStatusCode.OK){Content=new StringContent(raw)});
        var disposedClient=new UniversalisPrices(handler);disposedClient.Dispose();disposedClient.Tick(1,[42],own,30,new(),true,now);
        check(handler.Calls==0,"Disposed client cannot start another request");
        var delayed=new DelayedHandler();
        using(var client=new UniversalisPrices(delayed))
        {
            evidence=new();client.Tick(1,[42],own,30,evidence,true,now);
            client.Tick(2,[],own,30,evidence,true,now);
            delayed.Reply.SetResult(new(HttpStatusCode.OK){Content=new StringContent(raw)});
            for(var i=0;i<100&&client.Status.Contains("fetching");i++)
            {await Task.Delay(1);client.Tick(2,[],own,30,evidence,true,now);}
            check(!client.Status.Contains("fetching")&&evidence.Prices.Count==0,"Late response from a previously visited world never enters current evidence");
        }
        delayed=new();var cancelling=new UniversalisPrices(delayed);
        cancelling.Tick(1,[42],own,30,new(),true,now);cancelling.Dispose();
        check(delayed.Cancelled,"Plugin unload cancels in-flight HTTP work");
    }
    private static async Task Drain(UniversalisPrices client,Evidence evidence,HashSet<ulong> own,DateTimeOffset now)
    {
        for(var i=0;i<100;i++)
        {
            client.Tick(1,[],own,30,evidence,true,now);
            if(!client.Status.Contains("fetching"))return;
            await Task.Delay(1);
        }
        throw new Exception("Test HTTP response did not complete");
    }
    sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {
        public int Calls;public Uri? Uri;public HttpMethod? Method;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Calls++;Uri=request.RequestUri;Method=request.Method;return Task.FromResult(response(request));}
    }
    sealed class DelayedHandler:HttpMessageHandler
    {
        public readonly TaskCompletionSource<HttpResponseMessage> Reply=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {token.Register(()=>Cancelled=true);return Reply.Task.WaitAsync(token);}
    }
}
