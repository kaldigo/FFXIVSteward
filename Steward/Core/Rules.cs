namespace Steward.Core;

public enum Outcome { Ignore, Keep, CollectArmoire, CollectDresser, Market, Dispose, Wait }
public enum QualityMode { Separate, Combined }
public enum SourceKind { Unknown, Ordinary, Special }
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
        var rarityProtected = item.Rarity > 3 || (item.Rarity == 3 && !item.Marketable);
        var collect = item.ArmoireEligible || item.Equipment && (rarityProtected || item.EquipLevel == 1 || item.DesignedForGlamour ||
            item.Source != SourceKind.Ordinary || settings.CommonGlamour.Contains(item.Id) || evidence.CuratedGlamour.Contains(item.Id) || evidence.References.Contains(item.Id));
        if(collect)
        {
            if(!evidence.DresserKnown || !evidence.ArmoireKnown) return new(Outcome.Wait, "Live dresser or armoire ownership is unavailable");
            if((evidence.OmittedDresserItem==item.Id || !evidence.Dresser.Contains(item.Id)) && !evidence.Armoire.Contains(item.Id))
            {
                if(item.Source == SourceKind.Unknown && !item.ArmoireEligible && !evidence.References.Contains(item.Id) && !item.DesignedForGlamour && !rarityProtected && item.EquipLevel != 1 && !settings.CommonGlamour.Contains(item.Id) && !evidence.CuratedGlamour.Contains(item.Id))
                    return new(Outcome.Keep, "Acquisition source is uncertain; preserve for collection");
                return new(item.ArmoireEligible ? Outcome.CollectArmoire : Outcome.CollectDresser, "An exact copy is missing from the collection");
            }
        }
        var rule = settings.Sales.FirstOrDefault(r => r.Matches(item, stock));
        if(item.Marketable)
        {
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
