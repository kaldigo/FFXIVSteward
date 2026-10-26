namespace Steward.Core;

public enum Outcome { Ignore, Keep, CollectArmoire, CollectDresser, Market, Dispose, Wait, StoreRetainer }
public enum QualityMode { Separate, Combined }
public enum SourceKind { Unknown, Ordinary, Special, NormalDungeon, NormalRaid, AllianceRaid, SavageRaid, HighEnd }
public sealed record ItemFacts(uint Id, string Name, bool Equipment, byte Rarity, int EquipLevel,
    bool Marketable, bool ArmoireEligible, SourceKind Source, uint Category, uint NpcPrice = 0, bool DesignedForGlamour = false);
public sealed record Stock(uint ItemId, bool Hq, uint Quantity, uint Container, ushort Slot);
public sealed record Quote(uint ItemId, bool Hq, uint World, uint UnitPrice, DateTimeOffset Observed,
    string Source, bool Own = false);
public sealed record Decision(Outcome Outcome, string Reason, SaleRule? Rule = null, uint? UnitPrice = null);
public sealed class SaleRule
{
    public string Name { get; set; } = "Sale rule";
    public bool Enabled { get; set; } = true;
    public HashSet<uint> ItemIds { get; set; } = [];
    public uint Category { get; set; }
    public bool? Hq { get; set; }
    public uint MinimumUnit { get; set; } = 1000;
    public uint MinimumTotal { get; set; } = 5000;
    public int MaximumStack { get; set; } = 99;
    public bool Matches(ItemFacts item, Stock stock) => Enabled &&
        (ItemIds.Count == 0 || ItemIds.Contains(item.Id)) && (Category == 0 || Category == item.Category) &&
        (Hq == null || Hq == stock.Hq);
}
public sealed class RuleSettings
{
    public HashSet<uint> Ignored { get; set; } = [];
    public HashSet<uint> Keep { get; set; } = [];
    public HashSet<uint> CommonGlamour { get; set; } = [];
    public List<SaleRule> Sales { get; set; } = [];
    public int MaximumPriceAgeMinutes { get; set; } = 30;
    public QualityMode Quality { get; set; } = QualityMode.Separate;
    public int DresserEmptySlots { get; set; } = 20;
}
public sealed class Evidence
{
    public void InvalidateLive()
    {
        DresserKnown=ArmoireKnown=GearsetsReady=ReferencesReady=false;
    }
    public bool ReferencesReady { get; set; }
    public HashSet<uint> References { get; set; } = [];
    public HashSet<uint> CuratedGlamour { get; set; } = [];
    public HashSet<uint> Gearsets { get; set; } = [];
    public bool GearsetsReady { get; set; }
    public bool DresserKnown { get; set; }
    public bool ArmoireKnown { get; set; }
    public HashSet<uint> Dresser { get; set; } = [];
    public uint? OmittedDresserItem { get; set; }
    public HashSet<uint> Armoire { get; set; } = [];
    public HashSet<uint> PendingCollection { get; set; } = [];
    public int? DresserFreeSlots { get; set; }
    public Dictionary<(uint, bool), Quote> Prices { get; set; } = [];
}
public static class Rules
{
    public static Decision Judge(ItemFacts item, Stock stock, RuleSettings settings, Evidence evidence,
        uint world, DateTimeOffset now)
    {
        if(item.Id==0 || stock.ItemId!=item.Id || stock.Quantity==0) return new(Outcome.Wait, "Invalid item or inventory evidence");
        if(settings.Ignored.Contains(item.Id)) return new(Outcome.Ignore, "Excluded from all Steward handling");
        if(settings.Keep.Contains(item.Id)) return new(Outcome.Keep, "Explicit keep override");
        if(!evidence.GearsetsReady || !evidence.ReferencesReady) return new(Outcome.Wait, "Glamour references or gear sets are unavailable");
        if(evidence.Gearsets.Contains(item.Id)) return new(Outcome.Keep, "Equipped or referenced by a gear set");
        var routineDuty = item.Source is SourceKind.NormalDungeon or SourceKind.NormalRaid;
        var ordinary = routineDuty || item.Source == SourceKind.Ordinary;
        var pending = evidence.PendingCollection.Contains(item.Id);
        var rarityProtected = item.Rarity > 3 || (item.Rarity == 3 && !item.Marketable && !routineDuty);
        var collect = item.ArmoireEligible || item.Equipment && (rarityProtected || item.EquipLevel == 1 || item.DesignedForGlamour ||
            !ordinary || pending || settings.CommonGlamour.Contains(item.Id) || evidence.CuratedGlamour.Contains(item.Id) || evidence.References.Contains(item.Id));
        if(collect)
        {
            if(!evidence.DresserKnown || !evidence.ArmoireKnown) return new(Outcome.Wait, "Live dresser or armoire ownership is unavailable");
            if(!evidence.Armoire.Contains(item.Id) && (item.ArmoireEligible || evidence.OmittedDresserItem==item.Id || !evidence.Dresser.Contains(item.Id)))
            {
                if(item.Source == SourceKind.Unknown && !pending && !item.ArmoireEligible && !evidence.References.Contains(item.Id) && !item.DesignedForGlamour && !rarityProtected && item.EquipLevel != 1 && !settings.CommonGlamour.Contains(item.Id) && !evidence.CuratedGlamour.Contains(item.Id))
                    return new(Outcome.Keep, "Acquisition source is uncertain; preserve for collection");
                if(!item.ArmoireEligible && evidence.DresserFreeSlots is int free && free<=Math.Clamp(settings.DresserEmptySlots,0,800))
                    return new(Outcome.StoreRetainer,"Protected overflow: store in a retainer until dresser space is available");
                return new(item.ArmoireEligible ? Outcome.CollectArmoire : Outcome.CollectDresser, "An exact copy is missing from the collection");
            }
        }
        var rule = settings.Sales.FirstOrDefault(r => r.Matches(item, stock));
        if(item.Marketable)
        {
            if(world==0)return new(Outcome.Wait,"Current world is unavailable; market valuation is blocked");
            var candidates = evidence.Prices.Values.Where(p => p.ItemId == item.Id && p.World == world && !p.Own &&
                (settings.Quality == QualityMode.Combined || p.Hq == stock.Hq) && p.UnitPrice > 0 &&
                p.Observed <= now && now-p.Observed <= TimeSpan.FromMinutes(settings.MaximumPriceAgeMinutes));
            var quote = candidates.OrderBy(p => p.UnitPrice).FirstOrDefault();
            if(quote == null) return new(Outcome.Wait, "No fresh competing price; disposal is blocked");
            if(rule != null)
            {
                var quantity = Math.Min(stock.Quantity, (uint)Math.Clamp(rule.MaximumStack, 1, 9999));
                if(quote.UnitPrice >= rule.MinimumUnit && (ulong)quote.UnitPrice*quantity >= rule.MinimumTotal)
                    return new(Outcome.Market, "Matches sale rule: " + rule.Name, rule, quote.UnitPrice);
            }
        }
        return new(Outcome.Dispose, collect ? "Exact copy already collected; surplus may use AutoRetainer disposal rules" : "Outside collection rules; use AutoRetainer disposal rules",rule);
    }
}
