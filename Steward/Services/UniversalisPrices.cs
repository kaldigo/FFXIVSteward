using System.Net.Http;
using System.Text;
using Steward.Core;

namespace Steward.Services;

// Network tasks return immutable data. Only Tick/Apply on the game thread touch inventory evidence.
public sealed class UniversalisPrices : IDisposable
{
    private readonly HttpClient client;
    private readonly CancellationTokenSource shutdown=new();
    private readonly Dictionary<(uint World,uint Item),PublicMarketItem> cache=[];
    private readonly Dictionary<(uint World,uint Item),DateTimeOffset> retry=[];
    private Task<FetchResult>? pending;
    private DateTimeOffset nextRequest;
    private int failures;
    private bool disposed;
    public string Status { get; private set; }="Universalis: waiting for inventory";
    public string Freshness { get; private set; }="";
    private sealed record FetchResult(uint World,uint[] Items,IReadOnlyList<PublicMarketItem> Values,string? Error,TimeSpan? RetryAfter);
    public UniversalisPrices(HttpMessageHandler? handler=null)
    {
        client=handler==null?new HttpClient():new HttpClient(handler);
        client.Timeout=TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Steward/0.1 (+https://github.com/kaldigo/FFXIVSteward)");
    }
    public void Tick(uint world,IEnumerable<uint> items,IReadOnlySet<ulong> own,int maximumAgeMinutes,Evidence evidence,bool enabled,DateTimeOffset now)
    {
        if(disposed)return;
        if(pending?.IsCompleted==true)
        {
            var result=pending.GetAwaiter().GetResult();pending=null;
            if(result.Error==null)
            {
                failures=0;
                foreach(var id in result.Items)cache.Remove((result.World,id));
                foreach(var value in result.Values)cache[(value.World,value.ItemId)]=value;
                foreach(var id in result.Items)retry[(result.World,id)]=now.AddMinutes(5);
                Status=$"Universalis: received {result.Values.Count}/{result.Items.Length} item snapshots; server upload age still applies";
            }
            else
            {
                failures=Math.Min(6,failures+1);
                var wait=result.RetryAfter??TimeSpan.FromSeconds(Math.Min(300,5*(1<<failures)));
                nextRequest=now+wait;
                foreach(var id in result.Items)retry[(result.World,id)]=nextRequest;
                Status=$"Universalis unavailable: {result.Error}; retry after {nextRequest.LocalDateTime:T}. Missing prices remain held.";
            }
            while(cache.Count>2048)cache.Remove(cache.MinBy(p=>p.Value.Uploaded).Key);
            while(retry.Count>4096)retry.Remove(retry.MinBy(p=>p.Value).Key);
        }
        Apply(world,own,maximumAgeMinutes,evidence,enabled,now);
        if(!enabled){Status="Universalis disabled";return;}
        if(world==0||pending!=null||now<nextRequest)return;
        var batch=items.Where(i=>i>0).Distinct().Where(id=>!retry.TryGetValue((world,id),out var at)||now>=at).Take(20).ToArray();
        if(batch.Length==0)return;
        pending=Fetch(world,batch,shutdown.Token);nextRequest=now.AddSeconds(5);
        Status=$"Universalis: fetching {batch.Length} item prices for world {world}; game UI is untouched";
    }
    public void Apply(uint world,IReadOnlySet<ulong> own,int maximumAgeMinutes,Evidence evidence,bool enabled,DateTimeOffset now)
    {
        foreach(var key in evidence.Prices.Where(p=>p.Value.Source.StartsWith("Universalis",StringComparison.Ordinal)).Select(p=>p.Key).ToArray())evidence.Prices.Remove(key);
        evidence.PublicOwnPrices.Clear();
        if(!enabled||world==0){Freshness="";return;}
        var fresh=0;var stale=0;var unpriced=0;
        foreach(var snapshot in cache.Values.Where(s=>s.World==world))
        {
            var quotes=snapshot.Quotes(own,now,maximumAgeMinutes);
            foreach(var quote in snapshot.OwnQuotes(own,now,maximumAgeMinutes))evidence.PublicOwnPrices[(quote.ItemId,quote.Hq)]=quote;
            if(snapshot.Uploaded>now||now-snapshot.Uploaded>TimeSpan.FromMinutes(Math.Clamp(maximumAgeMinutes,1,1440)))stale++;
            else if(quotes.Count==0)unpriced++;else fresh++;
            foreach(var quote in quotes)
            {
                var key=(quote.ItemId,quote.Hq);
                if(!evidence.Prices.TryGetValue(key,out var current)||current.World!=world||current.Observed<quote.Observed)evidence.Prices[key]=quote;
            }
        }
        Freshness=$"World {world}: {fresh} items with usable prices; {stale} stale/future uploads; {unpriced} without identifiable competing prices. Maximum upload age: {maximumAgeMinutes} minutes."+
            (own.Count==0?" Load owned retainers before using public prices.":"");
    }
    private async Task<FetchResult> Fetch(uint world,uint[] items,CancellationToken token)
    {
        try
        {
            // Fetch current listings, never aggregate averages/history. Limit each item to its first 100 listings.
            using var request=new HttpRequestMessage(HttpMethod.Get,$"https://universalis.app/api/v2/{world}/{string.Join(',',items)}?listings=100&entries=0");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode)
            {
                var wait=response.Headers.RetryAfter?.Delta;
                if(wait==null&&response.Headers.RetryAfter?.Date is {} date)wait=date-DateTimeOffset.UtcNow;
                if(wait.HasValue)wait=TimeSpan.FromSeconds(Math.Clamp(wait.Value.TotalSeconds,5,3600));
                return new(world,items,[],"HTTP "+(int)response.StatusCode,wait);
            }
            const int limit=4*1024*1024;
            if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Price response exceeds size limit");
            await using var stream=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer=new MemoryStream();var bytes=new byte[8192];int read;
            while((read=await stream.ReadAsync(bytes,token).ConfigureAwait(false))!=0)
            {if(buffer.Length+read>limit)throw new InvalidDataException("Price response exceeds size limit");buffer.Write(bytes,0,read);}
            var values=UniversalisData.Parse(Encoding.UTF8.GetString(buffer.ToArray()),world,items.ToHashSet());
            return new(world,items,values,null,null);
        }
        catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException or OverflowException or IOException)
        {return new(world,items,[],ex.GetType().Name,null);}
    }
    public void Dispose()
    {if(disposed)return;disposed=true;shutdown.Cancel();client.Dispose();shutdown.Dispose();}
}
