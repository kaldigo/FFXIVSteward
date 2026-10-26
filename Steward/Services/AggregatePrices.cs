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
    private Task<Result>? pending;
    private Task? saving;
    private bool dirty;
    private DateTimeOffset next;
    private int failures;
    private bool disposed;
    private sealed record Result(uint World,uint[] Items,IReadOnlyList<SaleStatistic> Values,string? Error,TimeSpan? Delay);
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
                if(s.World>0&&s.ItemId>0&&s.Average is >0 and <=999999999&&s.ValueObserved.HasValue&&
                    s.ValueObserved<=s.Fetched&&s.Fetched<=DateTimeOffset.UtcNow&&(s.DailySales==null||double.IsFinite(s.DailySales.Value)&&s.DailySales>=0))
                    cache[(s.World,s.ItemId,s.Hq)]=s;
        }
        catch(Exception ex)when(ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {Status="Price cache unavailable; rebuilding in background: "+ex.Message;}
    }
    public IReadOnlyList<PublicMarketHold> HeldItems(uint world)=>cache.Values.Where(s=>s.World==world&&s.Average==null)
        .GroupBy(s=>s.ItemId).Where(g=>!cache.Values.Any(s=>s.World==world&&s.ItemId==g.Key&&s.Average>0)).Select(g=>new PublicMarketHold(g.Key,"No known average sale value; never treated as zero")).ToArray();
    public void Tick(uint world,IEnumerable<uint> items,IReadOnlySet<ulong> own,int refreshHours,Evidence evidence,bool enabled,DateTimeOffset now)
    {
        if(disposed)return;
        if(pending?.IsCompleted==true)
        {
            var result=pending.GetAwaiter().GetResult();pending=null;
            if(result.Error!=null)
            {
                failures=Math.Min(6,failures+1);next=now+(result.Delay??TimeSpan.FromSeconds(Math.Min(300,5*(1<<failures))));
                Status="Universalis unavailable; cached values retained: "+result.Error;
                foreach(var id in result.Items)retry[(result.World,id)]=next;
            }
            else
            {
                failures=0;
                foreach(var s in result.Values)
                {var key=(s.World,s.ItemId,s.Hq);cache[key]=SaleStatistics.Merge(s,cache.GetValueOrDefault(key));}
                foreach(var id in result.Items)retry[(result.World,id)]=now.AddHours(Math.Clamp(refreshHours,1,168));
                while(cache.Count>8192)cache.Remove(cache.MinBy(p=>p.Value.Fetched).Key);
                while(retry.Count>8192)retry.Remove(retry.MinBy(p=>p.Value).Key);
                dirty=true;Status=$"Universalis: refreshed {result.Values.Select(s=>s.ItemId).Distinct().Count()}/{result.Items.Length} item summaries; cached decisions available immediately";
            }
        }
        if(saving?.IsCompleted==true)
        {
            if(saving.IsFaulted)Status="Price-cache save failed; session values retained: "+saving.Exception!.GetBaseException().Message;
            saving=null;
        }
        if(dirty&&saving==null&&path!=null)
        {var snapshot=cache.Values.ToArray();dirty=false;saving=Task.Run(()=>AtomicState.Write(path,snapshot));}
        Apply(world,own,refreshHours,evidence,enabled,now);
        if(!enabled){Status="Universalis disabled";return;}
        if(world==0||pending!=null||now<next)return;
        var interval=TimeSpan.FromHours(Math.Clamp(refreshHours,1,168));
        var batch=items.Where(id=>id>0).Distinct().Where(id=>
            (!retry.TryGetValue((world,id),out var at)||now>=at)&&
            (!cache.TryGetValue((world,id,false),out var s)||s.Fetched>now||now-s.Fetched>=interval)).Take(100).ToArray();
        if(batch.Length==0)return;
        pending=Fetch(world,batch,now,shutdown.Token);next=now.AddMilliseconds(250);
    }
    public void Apply(uint world,IReadOnlySet<ulong> own,int refreshHours,Evidence evidence,bool enabled,DateTimeOffset now)
    {
        evidence.StatisticsRequired=enabled;evidence.SaleStatistics.Clear();
        if(!enabled||world==0){Freshness="";return;}
        foreach(var s in cache.Values.Where(s=>s.World==world&&s.Fetched<=now))evidence.SaleStatistics[(s.ItemId,s.Hq)]=s;
        Freshness=$"World {world}: {evidence.SaleStatistics.Values.Count(s=>s.Average>0)} quality-specific average values cached. Refresh every {refreshHours} hours; older known values retained for slow sellers. Native comparisons set actual listing prices.";
    }
    private async Task<Result> Fetch(uint world,uint[] ids,DateTimeOffset at,CancellationToken token)
    {
        try
        {
            using var response=await client.GetAsync($"https://universalis.app/api/v2/aggregated/{world}/{string.Join(',',ids)}",HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode)
            {
                var delay=response.Headers.RetryAfter?.Delta;
                if(delay==null&&response.Headers.RetryAfter?.Date is {} date)delay=date-DateTimeOffset.UtcNow;
                if(delay.HasValue)delay=TimeSpan.FromSeconds(Math.Clamp(delay.Value.TotalSeconds,5,3600));
                return new(world,ids,[],"HTTP "+(int)response.StatusCode,delay);
            }
            const int limit=4*1024*1024;
            if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Oversized aggregate response");
            await using var stream=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer=new MemoryStream();var bytes=new byte[8192];int read;
            while((read=await stream.ReadAsync(bytes,token).ConfigureAwait(false))>0)
            {if(buffer.Length+read>limit)throw new InvalidDataException("Oversized aggregate response");buffer.Write(bytes,0,read);}
            return new(world,ids,SaleStatistics.Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()),world,ids.ToHashSet(),at),null,null);
        }
        catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {return new(world,ids,[],ex.Message,null);}
    }
    public void Dispose()
    {if(disposed)return;disposed=true;shutdown.Cancel();client.Dispose();shutdown.Dispose();}
}
