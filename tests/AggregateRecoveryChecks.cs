using System.Net;
using System.Text.Json;
using Steward.Core;
using Steward.Services;

static class AggregateRecoveryChecks
{
    public static async Task Run(Action<bool,string> check,DateTimeOffset now)
    {
        var ids=Enumerable.Range(1,160).Select(i=>(uint)i).ToArray();
        var handler=new ControlledHandler();var evidence=new Evidence();
        using(var client=new AggregatePrices(handler:handler))
        {
            void Tick(double seconds,IEnumerable<uint>? requested=null,uint world=83)=>client.Tick(world,requested??ids,new HashSet<ulong>(),2,evidence,true,now.AddSeconds(seconds));
            Tick(0);Tick(0);
            check(handler.Requests.Count==4&&handler.Requests.All(r=>r.Ids.Length==20),"Four compact 20-item batches start without duplicating in-flight IDs");
            check(handler.Requests.SelectMany(r=>r.Ids).Distinct().Count()==80,"All in-flight IDs are unique");
            handler.Finish(0,HttpStatusCode.GatewayTimeout);
            for(var i=1;i<4;i++)handler.Finish(i,HttpStatusCode.OK);
            await Process(()=>Tick(.5,[]),()=>evidence.SaleStatistics.ContainsKey((80,false))&&client.HeldItems(83).Count==20);
            check(evidence.SaleStatistics[(21,false)].Average==2100&&!evidence.SaleStatistics.ContainsKey((1,false)),"504 preserves successful siblings and holds failed items");
            Tick(.5);check(handler.Requests.Count==4,"Fast completions cannot exceed four starts per second");
            Tick(1);check(handler.Requests.Count==8&&handler.Requests.Skip(4).All(r=>r.Ids.Length==10&&r.Ids.All(id=>id>80)),"504 halves batch and advances to later IDs");
            for(var i=4;i<8;i++)handler.Finish(i,HttpStatusCode.OK);
            await Process(()=>Tick(1,[]),()=>evidence.SaleStatistics.ContainsKey((120,false)));
            Tick(31,[1,2]);handler.Finish(8,HttpStatusCode.OK);
            await Process(()=>Tick(31,[]),()=>evidence.SaleStatistics.ContainsKey((1,false)));
            check(client.HeldItems(83).Count==18,"Recovered IDs clear only their errors");
            Tick(32,[],84);check(evidence.SaleStatistics.Count==0,"Worlds never share statistics");
            Tick(33,[],83);check(evidence.SaleStatistics[(1,false)].Average==100,"Cached recovery survives world switch");
        }
        handler=new();evidence=new();
        using(var client=new AggregatePrices(handler:handler))
        {
            void Tick(double seconds)=>client.Tick(83,ids,new HashSet<ulong>(),2,evidence,true,now.AddSeconds(seconds));
            Tick(0);handler.Finish(0,HttpStatusCode.TooManyRequests,60);
            handler.Finish(1,HttpStatusCode.OK);handler.Finish(2,HttpStatusCode.GatewayTimeout);handler.Finish(3,HttpStatusCode.GatewayTimeout);
            await Process(()=>Tick(1),()=>evidence.SaleStatistics.ContainsKey((21,false))&&client.HeldItems(83).Count==60);
            Tick(40);check(handler.Requests.Count==4,"Sibling success and gateway failures cannot shorten Retry-After");
            Tick(62);check(handler.Requests.Count==8,"Requests resume after mandated delay");
        }
        handler=new();evidence=new();
        using(var client=new AggregatePrices(handler:handler))
        {
            client.Tick(83,[1],new HashSet<ulong>(),2,evidence,true,now);handler.Finish(0,HttpStatusCode.OK);
            await Process(()=>client.Tick(83,[],new HashSet<ulong>(),2,evidence,true,now),()=>evidence.SaleStatistics.Count>0);
            var original=evidence.SaleStatistics[(1,false)];
            client.Tick(83,[1],new HashSet<ulong>(),2,evidence,true,now.AddHours(1));check(handler.Requests.Count==1,"Two-hour cache suppresses duplicate requests");
            client.Tick(83,[1],new HashSet<ulong>(),2,evidence,true,now.AddHours(2));handler.Finish(1,HttpStatusCode.GatewayTimeout);
            await Process(()=>client.Tick(83,[],new HashSet<ulong>(),2,evidence,true,now.AddHours(2)),()=>client.HeldItems(83).Count>0);
            check(evidence.SaleStatistics[(1,false)]==original,"Failure cannot erase or freshen previous value");
            client.Tick(83,[1,2,3],new HashSet<ulong>(),2,evidence,true,now.AddHours(2).AddSeconds(1));
            check(handler.Requests.Skip(2).SelectMany(r=>r.Ids).SequenceEqual(new uint[]{2,3}),"Single failed ID cannot starve unrelated requests");
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
            var json=JsonSerializer.Serialize(new{worldID=83,items=request.Ids.ToDictionary(id=>id.ToString(),id=>new{
                worldID=83,itemID=id,averagePriceNQ=id*100,nqSaleVelocity=1,recentHistoryCount=1})});
            var response=new HttpResponseMessage(code){Content=new StringContent(json)};
            if(retrySeconds.HasValue)response.Headers.RetryAfter=new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retrySeconds.Value));
            request.Reply.SetResult(response);
        }
    }
}
