namespace Steward.Core;

public enum Outcome { Ignore, Keep, CollectArmoire, CollectDresser, Market, Dispose, Wait, StoreRetainer }
public enum QualityMode { Separate, Combined }
public enum ItemGroup { Any, Gear, Other }
public enum SourceKind { Unknown, Ordinary, Special, NormalDungeon, NormalRaid, AllianceRaid, SavageRaid, HighEnd }
public sealed record ItemFacts(uint Id, string Name, bool Equipment, byte Rarity, int EquipLevel,
    bool Marketable, bool ArmoireEligible, SourceKind Source, uint Category, uint NpcPrice = 0, bool DesignedForGlamour = false);
public sealed record Stock(uint ItemId, bool Hq, uint Quantity, uint Container, ushort Slot);
public sealed record Quote(uint ItemId, bool Hq, uint World, uint UnitPrice, DateTimeOffset Observed,
    string Source, bool Own = false);
public sealed record Decision(Outcome Outcome, string Reason, SaleRule? Rule = null, uint? UnitPrice = null, bool SaleRejected = false, uint? MaximumRemovable = null);
public sealed class SaleRule
{
    public string Name { get; set; } = "Sale rule";
    public bool Enabled { get; set; } = true;
    public HashSet<uint> ItemIds { get; set; } = [];
    public uint Category { get; set; }
    public ItemGroup Group { get; set; }
    public HashSet<uint> Categories { get; set; } = [];
    public HashSet<uint> ExcludedCategories { get; set; } = [];
    public bool UseRetainerFilter { get; set; }
    public HashSet<ulong> ExcludedRetainers { get; set; } = [];
    public bool? Hq { get; set; }
    public uint MinimumUnit { get; set; } = 1000;
    public uint MinimumTotal { get; set; } = 5000;
    public bool AllowLowPriority { get; set; }
    public uint LowPriorityMinimumUnit { get; set; } = 500;
    public uint LowPriorityMinimumTotal { get; set; } = 2500;
    [System.Text.Json.Serialization.JsonIgnore] public uint ListingMinimumUnit=>AllowLowPriority?Math.Max(1,Math.Min(MinimumUnit,LowPriorityMinimumUnit)):MinimumUnit;
    [System.Text.Json.Serialization.JsonIgnore] public uint ListingMinimumTotal=>AllowLowPriority?Math.Max(1,Math.Min(MinimumTotal,LowPriorityMinimumTotal)):MinimumTotal;
    public int Priority(double? unit,uint quantity)=>AllowLowPriority&&(!unit.HasValue||unit<MinimumUnit||unit*quantity<MinimumTotal)?1:0;
    public int MaximumStack { get; set; } = 99;
    public int? MaximumListingDays { get; set; }
    public double? MinimumDailySales { get; set; }
    public bool AllowOlderSaleValue { get; set; } = true;
    public bool Matches(ItemFacts item, Stock stock, ulong retainer=0) => Enabled &&
        (retainer==0 || !UseRetainerFilter || !ExcludedRetainers.Contains(retainer)) &&
        (ItemIds.Count == 0 || ItemIds.Contains(item.Id)) &&
        (Group==ItemGroup.Any || Group==ItemGroup.Gear&&item.Equipment || Group==ItemGroup.Other&&!item.Equipment) &&
        (Categories.Count==0 ? Category==0||Category==item.Category : Categories.Contains(item.Category)) &&
        !ExcludedCategories.Contains(item.Category) &&
        (Hq == null || Hq == stock.Hq);
}
public sealed class RuleSettings
{
    public GlamourFilters Glamour { get; set; }=new();
    public HashSet<uint> Ignored { get; set; } = [];
    public HashSet<uint> IgnoredCategories { get; set; } = [];
    public bool IsIgnored(ItemFacts item)=>Ignored.Contains(item.Id)||IgnoredCategories.Contains(item.Category);
    public HashSet<uint> Keep { get; set; } = [];
    public HashSet<uint> CollectionKeep { get; set; } = [];
    public Dictionary<uint,uint> QuantityReserves { get; set; } = [];
    public HashSet<uint> NonEquipmentDisposal { get; set; } = [];
    public HashSet<uint> NonEquipmentDisposalCategories { get; set; } = [];
    public bool AllowsNonEquipmentDisposal(ItemFacts item)=>!item.Equipment&&
        (NonEquipmentDisposal.Contains(item.Id)||NonEquipmentDisposalCategories.Contains(item.Category));
    public HashSet<uint> CommonGlamour { get; set; } = [];
    public List<SaleRule> Sales { get; set; } = [];
    public int MaximumPriceAgeMinutes { get; set; } = 30; // Native/imported listing evidence only.
    public int AllaganCacheHours { get; set; } = 24; // Reprice screening only; never execution evidence.
    public int SummaryRefreshHours { get; set; } = 2;
    public int SummaryHistoryDays { get; set; } = 28;
    public QualityMode Quality { get; set; } = QualityMode.Separate;
    public bool PreferFasterSales { get; set; }
    public int DresserEmptySlots { get; set; } = 20;
}
public sealed class Evidence
{
    public IReadOnlyList<Quote> AllaganPrices { get; set; }=[];
    public void InvalidateLive()
    {
        DresserKnown=ArmoireKnown=GearsetsReady=ReferencesReady=false;
    }
    public bool ReferencesReady { get; set; }
    public HashSet<uint> RelistingHolds { get; set; }=[];
    public HashSet<uint> References { get; set; } = [];
    public HashSet<uint> CuratedGlamour { get; set; } = [];
    public HashSet<uint> Gearsets { get; set; } = [];
    public bool GearsetsReady { get; set; }
    public bool DresserKnown { get; set; }
    public DateTimeOffset? SessionDresserObservedAt { get; set; }
    public bool ArmoireKnown { get; set; }
    public bool CachedDresserKnown { get; set; }
    public bool CachedArmoireKnown { get; set; }
    public HashSet<uint> CachedDresser { get; set; } = [];
    public HashSet<uint> CachedArmoire { get; set; } = [];
    public HashSet<uint> Dresser { get; set; } = [];
    public Dictionary<uint,int> DresserCopyCounts { get; set; } = [];
    public HashSet<uint> GlamaholicReferences { get; set; } = [];
    public bool GlamaholicKnown { get; set; }
    public uint? OmittedDresserItem { get; set; }
    public HashSet<uint> Armoire { get; set; } = [];
    public HashSet<uint> PendingCollection { get; set; } = [];
    public int? DresserFreeSlots { get; set; }
    public List<Stock> ReserveStocks { get; set; } = [];
    public bool StatisticsRequired { get; set; }
    public Dictionary<(uint, bool), SaleStatistic> SaleStatistics { get; set; } = [];
    public Dictionary<(uint, bool), Quote> Prices { get; set; } = [];
    public Dictionary<(uint, bool), Quote> PublicOwnPrices { get; set; } = [];
}
public static class Rules
{
    public static Decision Judge(ItemFacts item, Stock stock, RuleSettings settings, Evidence evidence,
        uint world, DateTimeOffset now, ulong retainer=0)
    {
        if(item.Id==0 || stock.ItemId!=item.Id || stock.Quantity==0) return new(Outcome.Wait, "Invalid item or inventory evidence");
        if(settings.Ignored.Contains(item.Id)) return new(Outcome.Ignore, "Excluded from all Steward handling");
        if(settings.IgnoredCategories.Contains(item.Category)) return new(Outcome.Ignore, "Item category excluded from all Steward handling");
        if(settings.Keep.Contains(item.Id)) return new(Outcome.Keep, "Explicit keep override");
        if(!evidence.GearsetsReady || !evidence.ReferencesReady) return new(Outcome.Wait, "Glamour references or gear sets are unavailable");
        if(evidence.Gearsets.Contains(item.Id)) return new(Outcome.Keep, "Equipped or referenced by a gear set");
        var nonEquipmentHold=NonEquipmentProtection.Reason(item);
        var removable=QuantityReserve.Removable(stock,settings.QuantityReserves.GetValueOrDefault(item.Id),evidence.ReserveStocks);
        if(removable==null)return new(Outcome.Wait,"Quantity reserve needs live source inventory");
        if(removable==0)return new(Outcome.Keep,"Quantity reserve protects this stack");
        if(settings.CollectionKeep.Contains(item.Id)&&!item.Equipment&&!item.ArmoireEligible)return new(Outcome.Keep,"Supplemental collection item has no equipment storage destination");
        var protections=GlamourProtection.Match(item,settings,evidence);
        var collect=protections!=GlamourMatch.None;
        if(collect)
        {
            if(!evidence.DresserKnown || !evidence.ArmoireKnown)
                return evidence.CachedDresser.Contains(item.Id)||evidence.CachedArmoire.Contains(item.Id)
                    ?new(Outcome.Keep,"Cached collection copy is unverified; preserve until collection is observed")
                    :new(Outcome.Wait,"Live dresser or armoire ownership is unavailable");
            if(!evidence.Armoire.Contains(item.Id) && (item.ArmoireEligible || evidence.OmittedDresserItem==item.Id || !evidence.Dresser.Contains(item.Id)))
            {
                if(protections==GlamourMatch.Unknown)
                    return new(Outcome.Keep, "Acquisition source is uncertain; preserve for collection");
                if(!item.ArmoireEligible && evidence.DresserFreeSlots is int free && free<=Math.Clamp(settings.DresserEmptySlots,0,800))
                    return new(Outcome.StoreRetainer,"Protected overflow: store in a retainer until dresser space is available");
                return new(item.ArmoireEligible ? Outcome.CollectArmoire : Outcome.CollectDresser, "An exact copy is missing from the collection");
            }
        }
        if(nonEquipmentHold!=null)return new(Outcome.Keep,nonEquipmentHold);
        var rule = settings.Sales.FirstOrDefault(r => r.Matches(item, stock, retainer));
        var saleRejected=false;
        if(item.Marketable)
        {
            if(world==0)return new(Outcome.Wait,"Current world is unavailable; market valuation is blocked");
            if(evidence.StatisticsRequired||rule?.MinimumDailySales!=null)
            {
                var value=SaleStatistics.Select(evidence,item.Id,stock.Hq,settings.Quality,world,now);
                if(value==null)return new(Outcome.Wait,"Average sale value unavailable; sale and disposal held",rule);
                var current=value.RecentSales&&value.ValueObserved.HasValue&&now-value.ValueObserved.Value<=TimeSpan.FromHours(Math.Clamp(settings.SummaryRefreshHours,1,168));
                if(rule!=null)
                {
                    if(!current&&!rule.AllowOlderSaleValue)return new(Outcome.Wait,"Rule requires recent sale evidence",rule);
                    if(rule.MinimumDailySales is double minimum && (value.DailySales==null||!current))
                        return new(Outcome.Wait,"Rule sales velocity is unknown; item held",rule);
                    var quantity=Math.Min(removable.Value,(uint)Math.Clamp(rule.MaximumStack,1,9999));
                    if(value.Average>=rule.ListingMinimumUnit&&value.Average*quantity>=rule.ListingMinimumTotal&&
                        (rule.MinimumDailySales==null||value.DailySales>=rule.MinimumDailySales))
                        return new(Outcome.Market,(rule.Priority(value.Average,quantity)>0?"Low-priority sale; fill spare slots: ":"Matches average-value and sales-velocity rule: ")+rule.Name,rule,(uint)Math.Floor(value.Average!.Value),MaximumRemovable:removable);
                    saleRejected=true;
                }
                // An old value can propose a sale, but a quiet or truncated history window never permits destruction.
                if(!current)return new(Outcome.Wait,"Older sale value retained; disposal held without recent sales",rule,SaleRejected:saleRejected);
            }
            else
            {
            var candidates = evidence.Prices.Values.Where(p => p.ItemId == item.Id && p.World == world && !p.Own &&
                (settings.Quality == QualityMode.Combined || p.Hq == stock.Hq) && p.UnitPrice > 0 &&
                p.Observed <= now && now-p.Observed <= TimeSpan.FromMinutes(settings.MaximumPriceAgeMinutes));
            var quote = candidates.OrderBy(p => p.UnitPrice).FirstOrDefault();
            if(quote == null) return new(Outcome.Wait, "No fresh competing price; disposal is blocked");
            if(rule != null)
            {
                var quantity = Math.Min(removable.Value, (uint)Math.Clamp(rule.MaximumStack, 1, 9999));
                if(quote.UnitPrice >= rule.ListingMinimumUnit && (ulong)quote.UnitPrice*quantity >= rule.ListingMinimumTotal)
                    return new(Outcome.Market, "Matches sale rule: " + rule.Name, rule, quote.UnitPrice,MaximumRemovable:removable);
            }
        }
        }
        if(!item.Equipment && !settings.AllowsNonEquipmentDisposal(item))
            return new(Outcome.Keep, "Non-equipment disposal requires explicit item permission",rule,SaleRejected:saleRejected);
        if(removable<stock.Quantity)return new(Outcome.Keep,"Stack contains a quantity reserve; whole-stack disposal blocked",rule);
        return new(Outcome.Dispose, collect ? "Exact copy already collected; surplus may use AutoRetainer disposal rules" : "Outside collection rules; use AutoRetainer disposal rules",rule,SaleRejected:saleRejected);
    }
}
