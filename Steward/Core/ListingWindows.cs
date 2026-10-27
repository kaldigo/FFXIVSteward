namespace Steward.Core;

public sealed record ListingAge(uint ItemId,bool Hq,uint Quantity,DateTimeOffset Since);
public sealed record RelistHold(string Token,ulong Character,uint ItemId,bool Hq,uint Quantity,DateTimeOffset QueuedAt);
public sealed class ListingWindows
{
    public Dictionary<string,ListingAge> Listings { get; set; }=[];
    public Dictionary<string,DateTimeOffset> Waiting { get; set; }=[];
    public List<RelistHold> Holds { get; set; }=[];
    public static string Owner(ulong character,uint world,ulong retainer)=>$"{character}/{world}/{retainer}/";
    public static string ItemKey(Stock stock)=>$"{stock.ItemId}/{stock.Hq}";
    public bool Observe(ulong character,uint world,ulong retainer,IReadOnlyList<Stock> listings,IReadOnlyList<Stock> candidates,DateTimeOffset now)
    {
        var changed=false;var prefix=Owner(character,world,retainer);
        var present=listings.Select(s=>prefix+s.Slot).ToHashSet();
        foreach(var key in Listings.Keys.Where(k=>k.StartsWith(prefix)&&!present.Contains(k)).ToArray()){Listings.Remove(key);changed=true;}
        foreach(var s in listings)
        {
            var key=prefix+s.Slot;
            if(!Listings.TryGetValue(key,out var age)||age.ItemId!=s.ItemId||age.Hq!=s.Hq||age.Quantity!=s.Quantity)
            {Listings[key]=new(s.ItemId,s.Hq,s.Quantity,now);changed=true;}
        }
        var waiting=candidates.Select(s=>prefix+ItemKey(s)).ToHashSet();
        foreach(var key in Waiting.Keys.Where(k=>k.StartsWith(prefix)&&!waiting.Contains(k)).ToArray()){Waiting.Remove(key);changed=true;}
        foreach(var key in waiting)if(Waiting.TryAdd(key,now))changed=true;
        return changed;
    }
    public ListingAge? Age(ulong character,uint world,ulong retainer,Stock stock)
    {
        var age=Listings.GetValueOrDefault(Owner(character,world,retainer)+stock.Slot);
        return age?.ItemId==stock.ItemId&&age.Hq==stock.Hq&&age.Quantity==stock.Quantity?age:null;
    }
    public static bool Expired(ListingAge? age,int? days,DateTimeOffset now)=>age!=null&&days is >0 and <=3650&&age.Since<=now&&now-age.Since>=TimeSpan.FromDays(days.Value);
    public DateTimeOffset Priority(ulong character,uint world,ulong retainer,Stock stock)=>Waiting.GetValueOrDefault(Owner(character,world,retainer)+ItemKey(stock),DateTimeOffset.MaxValue);
    public bool Held(ulong character,uint item)=>Holds.Any(h=>h.Character==character&&h.ItemId==item&&h.Quantity>0);
    public void ReserveReturn(string token,ulong character,uint world,ulong retainer,Stock stock,DateTimeOffset now,Action save)
    {
        if(!Holds.Any(h=>h.Token==token))Holds.Add(new(token,character,stock.ItemId,stock.Hq,stock.Quantity,now));
        Waiting[Owner(character,world,retainer)+ItemKey(stock)]=now;
        save(); // Keep protection in memory even if persistence fails; caller must not submit.
    }
    public void Posted(ulong character,uint item,bool hq,uint quantity,Action save)
    {
        var before=Holds.ToList();var remaining=quantity;
        foreach(var hold in before.Where(h=>h.Character==character&&h.ItemId==item&&h.Hq==hq).OrderBy(h=>h.QueuedAt))
        {
            var used=Math.Min(remaining,hold.Quantity);if(used==0)break;
            Holds.Remove(hold);if(used<hold.Quantity)Holds.Add(hold with{Quantity=hold.Quantity-used});remaining-=used;
        }
        if(remaining==quantity)return;
        try{save();}catch{Holds=before;throw;}
    }
    public void Validate()
    {
        if(Listings==null||Waiting==null||Holds==null||Listings.Any(p=>p.Value==null||p.Value.ItemId==0||p.Value.Quantity==0||p.Value.Since==default)||
            Holds.Any(h=>h==null||h.Character==0||h.ItemId==0||h.Quantity==0||h.QueuedAt==default||string.IsNullOrWhiteSpace(h.Token))||Holds.Select(h=>h.Token).Distinct().Count()!=Holds.Count)
            throw new System.Text.Json.JsonException("Invalid listing-window safety state");
    }
}
