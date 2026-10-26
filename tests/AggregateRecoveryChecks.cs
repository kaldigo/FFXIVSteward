using System.Net;
using System.Text.Json;
using Steward.Core;
using Steward.Services;

static class AggregateRecoveryChecks
{
    public static async Task Run(Action<bool,string> check,DateTimeOffset now)
    {
        var ids=Enumerable.Range(1,30).Select(i=>(uint)i).ToArray();
        var handler=new ControlledHandler();var evidence=new Evidence();
        using(var client=new AggregatePrices(handler:handler))
        {
            void Tick(double seconds,IEnumerable<uint>? requested=null,uint world=83)=>client.Tick(world,requested??ids,new HashSet<ulong>(),6,evidence,true,now.AddSeconds(seconds));
            Tick(0);Tick(0);
            check(handler.Requests.Count==2&&handler.Requests.All(r=>r.Ids.Length==5),"Start two small batches, not one oversized request");
            check(handler.Requests.SelectMany(r=>r.Ids).Distinct().Count()==10,"In-flight IDs are not duplicated");
            handler.Finish(0,HttpStatusCode.GatewayTimeout);
            handler.Finish(1,HttpStatusCode.OK);
            await Process(()=>Tick(1,[]),()=>evidence.SaleStatistics.ContainsKey((6,false)));
            check(evidence.SaleStatistics[(6,false)].Average==600&&!evidence.SaleStatistics.ContainsKey((1,false)),"Successful sibling retained when another batch returns 504");
            check(client.HeldItems(83).Count==5,"Each timed-out ID has diagnostic evidence, never zero price");
            Tick(1);
            check(handler.Requests.Count==4&&handler.Requests.Skip(2).All(r=>r.Ids.Length==2&&r.Ids.All(id=>id>10)),"504 halves batch size and advances to later IDs");
            handler.Finish(2,HttpStatusCode.OK);handler.Finish(3,HttpStatusCode.OK);
            await Process(()=>Tick(1,[]),()=>evidence.SaleStatistics.ContainsKey((14,false)));
            Tick(1); // A second pair is allowed in this second, but no third pair.
            check(handler.Requests.Count==6,"At most four requests start in a one-second window");
            handler.Finish(4,HttpStatusCode.OK);handler.Finish(5,HttpStatusCode.OK);
            await Process(()=>Tick(1,[]),()=>evidence.SaleStatistics.ContainsKey((18,false)));
            Tick(1);check(handler.Requests.Count==6,"Rapid completions cannot bypass the request-rate cap");
            Tick(31,[1,2]);check(handler.Requests.Last().Ids.SequenceEqual(new uint[]{1,2}),"Deferred failed IDs become eligible for smaller retry");
            handler.Finish(6,HttpStatusCode.OK);
            await Process(()=>Tick(31,[]),()=>evidence.SaleStatistics.ContainsKey((1,false)));
            check(client.HeldItems(83).Count==3,"Recovered IDs clear their errors without erasing other holds");
            Tick(32,[],84);check(evidence.SaleStatistics.Count==0,"Late old-world results never become current-world evidence");
            Tick(33,[],83);check(evidence.SaleStatistics[(1,false)].Average==100,"World-specific cached recovery survives switching worlds");
        }
        handler=new();evidence=new();
        using(var client=new AggregatePrices(handler:handler))
        {
            void Tick(double seconds)=>client.Tick(83,ids,new HashSet<ulong>(),6,evidence,true,now.AddSeconds(seconds));
            Tick(0);handler.Finish(0,HttpStatusCode.TooManyRequests,60);handler.Finish(1,HttpStatusCode.OK);
            await Process(()=>Tick(1),()=>evidence.SaleStatistics.ContainsKey((6,false)));
            Tick(30);check(handler.Requests.Count==2,"Sibling success cannot undo 429 Retry-After pause");
            Tick(62);check(handler.Requests.Count==4,"Requests resume after server-mandated pause");
        }
        handler=new();evidence=new();
        using(var client=new AggregatePrices(handler:handler))
        {
            void Tick(double seconds)=>client.Tick(83,ids,new HashSet<ulong>(),6,evidence,true,now.AddSeconds(seconds));
            Tick(0);handler.Finish(0,HttpStatusCode.GatewayTimeout);handler.Finish(1,HttpStatusCode.GatewayTimeout);
            await Process(()=>Tick(1),()=>handler.Requests.Count==4);
            handler.Finish(2,HttpStatusCode.TooManyRequests,60);handler.Finish(3,HttpStatusCode.GatewayTimeout);
            await Process(()=>Tick(2),()=>client.HeldItems(83).Count==14);
            Tick(40);check(handler.Requests.Count==4,"Later gateway failures cannot shorten an existing Retry-After pause");
        }
        handler=new();evidence=new();
        using(var client=new AggregatePrices(handler:handler))
        {
            client.Tick(83,[1],new HashSet<ulong>(),6,evidence,true,now);handler.Finish(0,HttpStatusCode.OK);
            await Process(()=>client.Tick(83,[],new HashSet<ulong>(),6,evidence,true,now),()=>evidence.SaleStatistics.Count>0);
            var original=evidence.SaleStatistics[(1,false)];
            client.Tick(83,[1],new HashSet<ulong>(),6,evidence,true,now.AddHours(7));handler.Finish(1,HttpStatusCode.GatewayTimeout);
            await Process(()=>client.Tick(83,[],new HashSet<ulong>(),6,evidence,true,now.AddHours(7)),()=>client.HeldItems(83).Count>0);
            check(evidence.SaleStatistics[(1,false)]==original,"504 preserves persisted-value age rather than erasing or freshening valuation");
            client.Tick(83,[1,2,3],new HashSet<ulong>(),6,evidence,true,now.AddHours(7).AddSeconds(1));
            check(handler.Requests.Skip(2).SelectMany(r=>r.Ids).SequenceEqual(new uint[]{2,3}),"Single failing item is deferred while later items continue");
        }
    }
    private static async Task Process(Action tick,Func<bool> complete)
    {
        for(var i=0;i<200;i++){tick();if(complete())return;await Task.Delay(1);}
        throw new Exception("Controlled aggregate response did not complete");
    }
    private sealed class ControlledHandler:HttpMessageHandler
    {
        public sealed record Pending(uint[] Ids,TaskCompletionSource<HttpResponseMessage> Reply);
        public List<Pending> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var ids=request.RequestUri!.AbsolutePath.Split('/').Last().Split(',').Select(uint.Parse).ToArray();
            var reply=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(new(ids,reply));return reply.Task.WaitAsync(token);
        }
        public void Finish(int index,HttpStatusCode code,int? retrySeconds=null)
        {
            var request=Requests[index];
            var json=JsonSerializer.Serialize(new{results=request.Ids.Select(id=>new{itemId=id,nq=new{
                averageSalePrice=new{world=new{price=id*100}},dailySaleVelocity=new{world=new{quantity=1}}}})});
            var response=new HttpResponseMessage(code){Content=new StringContent(json)};
            if(retrySeconds.HasValue)response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retrySeconds.Value));
            request.Reply.SetResult(response);
        }
    }
}
