using System.Text.Json;

namespace Steward.Core;

// Inventory observations are preview/prefetch only; never supply live ownership or disposal copy counts.
// Native plate configurations are separately observed, per-character references usable across sessions.
public sealed class InventoryMemory : IDisposable
{
    public sealed class CharacterInventory
    {
        public bool NativePlatesObserved { get; set; }
        private HashSet<uint> nativePlateItems=[];
        [System.Text.Json.Serialization.JsonIgnore] public bool NativePlateItemsRead { get; private set; }
        public HashSet<uint> NativePlateItems { get=>nativePlateItems;set{nativePlateItems=value;NativePlateItemsRead=true;} }
        public DateTimeOffset? NativePlatesObservedAt { get; set; }
        public bool DresserObserved { get; set; }
        public bool ArmoireObserved { get; set; }
        public HashSet<uint> Dresser { get; set; }=[];
        public HashSet<uint> Armoire { get; set; }=[];
        public Dictionary<ulong,List<Stock>> Retainers { get; set; }=[];
    }
    private readonly string? path;
    private Dictionary<ulong,CharacterInventory> characters=[];
    private Task? saving;
    private bool dirty;
    public string Status { get; private set; }="Persistent inventory observations available for preview only";
    public InventoryMemory(string? path=null)
    {
        this.path=path;
        if(path==null||!File.Exists(path))return;
        try
        {
            if(new FileInfo(path).Length>8*1024*1024)throw new InvalidDataException("Oversized inventory cache");
            var loaded=JsonSerializer.Deserialize<Dictionary<ulong,CharacterInventory>>(File.ReadAllText(path))??throw new InvalidDataException("Missing inventory cache");
            foreach(var (id,c) in loaded)
                if(id==0||c==null||c.Dresser==null||c.Armoire==null||c.Retainers==null||c.Dresser.Contains(0)||c.Armoire.Contains(0)||c.NativePlateItems==null||c.NativePlateItems.Contains(0)||c.NativePlateItems.Count>4096||
                    c.NativePlatesObserved&&(!c.NativePlateItemsRead||c.NativePlatesObservedAt==null||c.NativePlatesObservedAt==default(DateTimeOffset)||c.NativePlatesObservedAt>DateTimeOffset.UtcNow)||
                    c.Retainers.Any(r=>r.Key==0||r.Value==null||r.Value.Count>175||r.Value.Any(s=>s==null||!Valid(s))))
                    throw new InvalidDataException("Invalid inventory cache");
            characters=loaded;
        }
        catch(Exception ex)when(ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {Status="Inventory cache unavailable; waiting for observations: "+ex.Message;}
    }
    private static bool Valid(Stock s)=>s.ItemId>0&&s.Quantity>0&&(s.Container is >=10000 and <=10006&&s.Slot<25 || AllaganRetainerPreview.IsPreview(s.Container)&&s.Slot<35);
    private CharacterInventory For(ulong id)
    {
        if(id==0)throw new ArgumentException("Character required");
        if(!characters.TryGetValue(id,out var c))characters[id]=c=new();
        return c;
    }
    public void ObserveNativePlates(ulong character,IEnumerable<uint> items,DateTimeOffset observed)
    {
        if(character==0)return;
        var ids=items.ToHashSet();
        if(ids.Contains(0)||ids.Count>4096||observed==default||observed>DateTimeOffset.UtcNow)throw new InvalidDataException("Invalid native plate reference snapshot");
        var c=For(character);
        if(c.NativePlatesObserved&&c.NativePlateItems.SetEquals(ids))return;
        c.NativePlateItems=ids;c.NativePlatesObserved=true;c.NativePlatesObservedAt=observed;dirty=true;
    }
    public bool TryNativePlates(ulong character,out HashSet<uint> items,out DateTimeOffset? observed)
    {
        items=[];observed=null;
        if(character==0||!characters.TryGetValue(character,out var c)||!c.NativePlatesObserved)return false;
        items=[..c.NativePlateItems];observed=c.NativePlatesObservedAt;return true;
    }
    public void ObserveCollection(ulong id,Evidence e)
    {
        if(id==0)return;var c=For(id);
        if(e.DresserKnown&&(!c.DresserObserved||!c.Dresser.SetEquals(e.Dresser))){c.Dresser=[..e.Dresser];c.DresserObserved=true;dirty=true;}
        if(e.ArmoireKnown&&(!c.ArmoireObserved||!c.Armoire.SetEquals(e.Armoire))){c.Armoire=[..e.Armoire];c.ArmoireObserved=true;dirty=true;}
        Apply(id,e);
    }
    public void Apply(ulong id,Evidence e)
    {
        e.CachedDresser=[];e.CachedArmoire=[];e.CachedDresserKnown=e.CachedArmoireKnown=false;
        if(characters.TryGetValue(id,out var c)){e.CachedDresser=[..c.Dresser];e.CachedArmoire=[..c.Armoire];e.CachedDresserKnown=c.DresserObserved;e.CachedArmoireKnown=c.ArmoireObserved;}
    }
    public void ObserveRetainer(ulong character,ulong owner,IEnumerable<Stock> items,bool onlyIfMissing=false)
    {
        if(character==0||owner==0)return;var c=For(character);
        if(onlyIfMissing&&c.Retainers.ContainsKey(owner))return;
        var stocks=items.OrderBy(s=>s.Container).ThenBy(s=>s.Slot).ToList();
        if(stocks.Count>175||stocks.Any(s=>!Valid(s))||stocks.Select(s=>(s.Container,s.Slot)).Distinct().Count()!=stocks.Count)
            throw new InvalidDataException("Incomplete or invalid retainer observation");
        if(!c.Retainers.TryGetValue(owner,out var old)||!old.SequenceEqual(stocks)){c.Retainers[owner]=stocks;dirty=true;}
    }
    public bool HasRetainer(ulong character,ulong owner)=>characters.TryGetValue(character,out var c)&&c.Retainers.ContainsKey(owner);
    public IEnumerable<ulong> Owners(ulong id)=>characters.TryGetValue(id,out var c)?c.Retainers.Keys:[];
    public IEnumerable<(ulong Owner,Stock Stock)> Stocks(ulong id)=>characters.TryGetValue(id,out var c)?
        c.Retainers.SelectMany(r=>r.Value.Select(s=>(r.Key,s))):[];
    public void Flush()
    {
        if(saving?.IsCompleted==true)
        {if(saving.IsFaulted){dirty=true;Status="Inventory cache save failed: "+saving.Exception!.GetBaseException().Message;}saving=null;}
        if(!dirty||saving!=null||path==null)return;
        var snapshot=JsonSerializer.Deserialize<Dictionary<ulong,CharacterInventory>>(JsonSerializer.Serialize(characters))!;
        dirty=false;saving=Task.Run(()=>AtomicState.Write(path,snapshot));
    }
    public void Dispose()
    {
        try{saving?.GetAwaiter().GetResult();if(dirty&&path!=null)AtomicState.Write(path,characters);}
        catch(Exception ex){Status="Inventory cache save failed: "+ex.Message;}
    }
}
