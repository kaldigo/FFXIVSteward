namespace Steward.Core;

public sealed class RetainerStorageSettings
{
    public bool Enabled { get; set; }
    public HashSet<ulong> ExcludedRetainers { get; set; } = [];
    public HashSet<uint> ExcludedItems { get; set; } = [];
    public HashSet<uint> ExcludedCategories { get; set; } = [];
    public ulong NonmarketableRetainer { get; set; }
    public Dictionary<ulong,HashSet<uint>> Categories { get; set; } = [];
    public bool RetainerEnabled(ulong retainer)=>retainer!=0&&!ExcludedRetainers.Contains(retainer);
    public string? Block(ItemFacts? item,Evidence evidence,ulong retainer,long? unlockStatus)
    {
        if(!RetainerEnabled(retainer))return "Retainer excluded from item handling";
        if(item==null)return "Item metadata unavailable";
        if(!evidence.GearsetsReady)return "Gearset references unavailable";
        if(evidence.Gearsets.Contains(item.Id))return "Equipped or referenced by a gear set";
        if(ExcludedItems.Contains(item.Id)||ExcludedCategories.Contains(item.Category))return "Excluded from retainer storage";
        // Cards always remain available for use or exchange, including learned duplicates.
        if(item.Category==86)return "Triple Triad cards are never stored";
        // 1=learned, 2=unlearned, 3=unknown, 4=not an unlock. Null is unavailable.
        if(unlockStatus is not (1 or 4))return "Unlearned or unverified unlock item: keep available to use";
        if(!Enabled)return null;
        if(!item.Marketable||item.Category==100)return retainer==NonmarketableRetainer?null:"Nonmarketable items belong on the currency retainer";
        return Categories.TryGetValue(retainer,out var categories)&&categories.Contains(item.Category)?null:"Item belongs to another storage category";
    }
}
