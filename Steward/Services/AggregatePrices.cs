using System.Net;
using System.Net.Http;
using System.Text.Json;
using Steward.Core;

namespace Steward.Services;

public sealed class AggregatePrices : IDisposable
{
    private readonly HttpClient client;
    private readonly CancellationTokenSource shutdown=new();
    private readonly Dictionary<(uint,uint,bool),SaleStatistic> cache=[];
    private readonly Dictionary<(uint,uint),DateTimeOffset> retry=[];
    private readonly string? path;
    private const int MaximumConcurrentRequests=4;
    private const int MaximumRequestsPerSecond=4;
    private int batchSize=20;
    private sealed record Request(uint World,uint[] Items,Task<Result> Task);
    private readonly List<Request> pending=[];
    private readonly Queue<DateTimeOffset> starts=[];
    private readonly Dictionary<(uint,uint),string> errors=[];
    private Task? saving;
    private bool dirty;
    private DateTimeOffset next;
    private int failures;
    private bool disposed;
    private sealed record Result(uint World,uint[] Items,IReadOnlyList<SaleStatistic> Values,string? Error,TimeSpan? Delay,bool RateLimited=false);
    public string Status { get; private set; }="Universalis: waiting for inventory";
    public string Freshness { get; private set; }="";
    public AggregatePrices(string? path=null,HttpMessageHandler? handler=null)
    {
        this.path=path;
        client=new(handler??new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All});
        client.Timeout=TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Steward/0.1 (+https://github.com/kaldigo/FFXIVSteward)");
        if(path==null||!File.Exists(path))return;
        try
        {
            if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("Oversized statistics cache");
            foreach(var s in JsonSerializer.Deserialize<SaleStatistic[]>(File.ReadAllText(path))??[])
                if(s!=null&&s.World>0&&s.ItemId>0&&(s.Average==null&&s.ValueObserved==null || s.Average is >0 and <=999999999&&s.ValueObserved.HasValue)&&
                    (s.ValueObserved==null||s.ValueObserved<=s.Fetched)&&s.Fetched<=DateTimeOffset.UtcNow&&(s.DailySales==null||double.IsFinite(s.DailySales.Value)&&s.DailySales>=0))
                    cache[(s.World,s.ItemId,s.Hq)]=s.HistoryTruncated?s with{DailySales=null,RecentSales=false}:s;
        }
        catch(Exception ex)when(ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {Status="Price cache unavailable; rebuilding in background: "+ex.Message;}
    }
    public IReadOnlyList<PublicMarketHold> HeldItems(uint world)=>errors.Where(p=>p.Key.Item1==world)
        .Select(p=>new PublicMarketHold(p.Key.Item2,p.Value)).Concat(cache.Values.Where(s=>s.World==world&&s.Average==null)
        .GroupBy(s=>s.ItemId).Where(g=>!errors.ContainsKey((world,g.Key))&&!cache.Values.Any(s=>s.World==world&&s.ItemId==g.Key&&s.Average>0))
        .Select(g=>new PublicMarketHold(g.Key,"No known average sale value; never treated as zero"))).ToArray();
    public void Tick(uint world,IEnumerable<uint> items,IReadOnlySet<ulong> own,int refreshHours,Evidence evidence,bool enabled,DateTimeOffset now,int historyDays=28)
    {
        if(disposed)return;
        foreach(var request in pending.Where(p=>p.Task.IsCompleted).ToArray())
        {
            var result=request.Task.GetAwaiter().GetResult();pending.Remove(request);
            if(result.Error!=null)
            {
                failures=Math.Min(6,failures+1);
                DateTimeOffset retryAt;
                if(result.RateLimited||result.Delay.HasValue)
                {
                    retryAt=now+(result.Delay??TimeSpan.FromSeconds(Math.Min(300,5*(1<<failures))));
                    if(retryAt>next)next=retryAt; // A sibling success never cancels a server-requested pause.
                }
                else
                {
                    batchSize=Math.Max(1,Math.Min(batchSize,result.Items.Length/2));
                    retryAt=now.AddSeconds(result.Items.Length>1?30:120);
                    // Move past failed IDs; only repeated failures across batches pause the whole source.
                    if(failures>=3){var pause=now.AddSeconds(Math.Min(30,1<<failures));if(pause>next)next=pause;}
                }
                foreach(var id in result.Items)
                {retry[(result.World,id)]=retryAt;errors[(result.World,id)]=$"{result.Error}; world {result.World}, batch {result.Items.Length}; retry after {retryAt:O}";}
                Status=$"Universalis unavailable for {result.Items.Length} item(s): {result.Error}. Cached values retained; next batch size {batchSize}. Other eligible items continue unless the server requests a pause.";
            }
            else
            {
                failures=0;
                foreach(var value in result.Values)
                {var key=(value.World,value.ItemId,value.Hq);cache[key]=SaleStatistics.Merge(value,cache.GetValueOrDefault(key));}
                foreach(var id in result.Items)
                {retry.Remove((result.World,id));errors.Remove((result.World,id));}
                dirty=true;Status=$"Universalis: refreshed {result.Values.Where(v=>v.Average.HasValue).Select(v=>v.ItemId).Distinct().Count()}/{result.Items.Length} item values; successful batches are available immediately";
            }
            while(cache.Count>8192)cache.Remove(cache.MinBy(p=>p.Value.Fetched).Key);
            while(retry.Count>8192){var key=retry.MinBy(p=>p.Value).Key;retry.Remove(key);errors.Remove(key);}
        }
        if(saving?.IsCompleted==true)
        {
            if(saving.IsFaulted)Status="Price-cache save failed; session values retained: "+saving.Exception!.GetBaseException().Message;
            saving=null;
        }
        if(dirty&&saving==null&&path!=null)
        {var snapshot=cache.Values.ToArray();dirty=false;saving=Task.Run(()=>AtomicState.Write(path,snapshot));}
        Apply(world,own,refreshHours,evidence,enabled,now,historyDays);
        if(!enabled){Status="Universalis disabled";return;}
        if(world==0||now<next)return;
        while(starts.Count>0&&now-starts.Peek()>=TimeSpan.FromSeconds(1))starts.Dequeue();
        var interval=TimeSpan.FromHours(Math.Clamp(refreshHours,1,168));
        var inFlight=pending.Where(p=>p.World==world).SelectMany(p=>p.Items).ToHashSet();
        var eligible=items.Where(id=>id>0).Distinct().Where(id=>!inFlight.Contains(id)&&
            (!retry.TryGetValue((world,id),out var at)||now>=at)&&
            (!cache.TryGetValue((world,id,false),out var value)||value.Fetched>now||value.HistoryDays!=Math.Clamp(historyDays,7,90)||now-value.Fetched>=interval)).ToArray();
        var offset=0;
        while(pending.Count<MaximumConcurrentRequests&&starts.Count<MaximumRequestsPerSecond&&offset<eligible.Length)
        {
            var batch=eligible.Skip(offset).Take(batchSize).ToArray();offset+=batch.Length;
            starts.Enqueue(now);pending.Add(new(world,batch,Fetch(world,batch,now,Math.Clamp(historyDays,7,90),shutdown.Token)));
        }
    }

    public void Apply(uint world,IReadOnlySet<ulong> own,int refreshHours,Evidence evidence,bool enabled,DateTimeOffset now,int historyDays=28)
    {
        evidence.StatisticsRequired=enabled;evidence.SaleStatistics.Clear();
        if(!enabled||world==0){Freshness="";return;}
        foreach(var s in cache.Values.Where(s=>s.World==world&&s.Fetched<=now))evidence.SaleStatistics[(s.ItemId,s.Hq)]=s.HistoryDays==Math.Clamp(historyDays,7,90)?s:s with{DailySales=null,RecentSales=false};
        Freshness=$"World {world}: {evidence.SaleStatistics.Values.Count(s=>s.Average>0)} quality-specific average values cached. Refresh every {refreshHours} hours; older known values retained for slow sellers. Requests in flight: {pending.Count}/{MaximumConcurrentRequests}; batch size: {batchSize}; deferred item errors: {errors.Count(p=>p.Key.Item1==world)}. Native comparisons set actual listing prices.";
    }
    private async Task<Result> Fetch(uint world,uint[] ids,DateTimeOffset at,int historyDays,CancellationToken token)
    {
        try
        {
            var prefix=ids.Length==1?"":"items.";
            var fields="itemIDs,worldID,unresolvedItems,"+string.Join(',',new[]{"itemID","worldID","lastUploadTime","averagePriceNQ","averagePriceHQ","nqSaleVelocity","hqSaleVelocity","recentHistoryCount","minPriceNQ","minPriceHQ"}.Select(f=>prefix+f));
            using var response=await client.GetAsync($"https://universalis.app/api/v2/{world}/{string.Join(',',ids)}?listings=0&entries=500&statsWithin={(long)historyDays*86400000}&entriesWithin={(long)historyDays*86400}&fields={fields}",HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode)
            {
                var delay=response.Headers.RetryAfter?.Delta;
                if(delay==null&&response.Headers.RetryAfter?.Date is {} date)delay=date-DateTimeOffset.UtcNow;
                if(delay.HasValue)delay=TimeSpan.FromSeconds(Math.Clamp(delay.Value.TotalSeconds,5,3600));
                return new(world,ids,[],"HTTP "+(int)response.StatusCode,delay,response.StatusCode==HttpStatusCode.TooManyRequests);
            }
            const int limit=4*1024*1024;
            if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Oversized sale summary response");
            await using var stream=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer=new MemoryStream();var bytes=new byte[8192];int read;
            while((read=await stream.ReadAsync(bytes,token).ConfigureAwait(false))>0)
            {if(buffer.Length+read>limit)throw new InvalidDataException("Oversized sale summary response");buffer.Write(bytes,0,read);}
            return new(world,ids,SaleStatistics.ParseCurrent(System.Text.Encoding.UTF8.GetString(buffer.ToArray()),world,ids.ToHashSet(),at,historyDays),null,null);
        }
        catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {return new(world,ids,[],ex.Message,null);}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;shutdown.Cancel();client.Dispose();shutdown.Dispose();
        try{saving?.GetAwaiter().GetResult();if(dirty&&path!=null)AtomicState.Write(path,cache.Values.ToArray());}
        catch(Exception ex){Status="Price-cache save failed: "+ex.Message;}
    }
}
