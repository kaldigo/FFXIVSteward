using Dalamud.Configuration;
using Steward.Core;
using System.Text.Json.Serialization;

namespace Steward;

public sealed class Configuration : IPluginConfiguration, IJsonOnDeserialized
{
    public static Configuration FromLegacy(Configuration? legacy)
    {
        legacy??=new();
        if(legacy.SafetyRevision>=6)throw new InvalidDataException("Authoritative Steward safety state is missing; restore it before executing managed item actions");
        legacy.MigrateControls();
        return legacy;
    }
    [JsonRequired] public int Version { get; set; } = 1;
    public bool Automatic { get; set; }
    public bool LogOnly { get; set; } = true;
    [JsonIgnore] public bool ExecutionEnabled => Automatic && !LogOnly;
    [JsonRequired] public int SafetyRevision { get; set; }
    public bool ExecutionReviewRequired { get; set; }
    public string? PendingOperation { get; set; }
    public ulong PendingCharacter { get; set; }
    public bool Connected { get; set; } = true;
    public RuleSettings Rules { get; set; } = new();
    public PricingSettings Pricing { get; set; } = new();
    public Dictionary<uint,SourceKind> Sources { get; set; } = [];
    public HashSet<ulong> OwnRetainers { get; set; } = [];
    public bool ImportMarketBuddy { get; set; } = true;
    public int SummaryCacheRevision { get; set; }
    public bool UseUniversalis { get; set; } = true;
    public bool GlamaholicExpected { get; set; } = true;
    public bool UseBundledGlamourList { get; set; } = true;
    public int ControlsRevision { get; set; }
    public bool CleanDresser { get; set; }
    public FeatureSettings Features { get; set; } = new();
    public HashSet<ulong> ExcludedSaleRetainers { get; set; } = [];
    public bool SalesEnabledFor(ulong retainer)=>Features.AnyMarket&&retainer!=0&&!ExcludedSaleRetainers.Contains(retainer);
    private ListingWindows listingWindows=new();
    private bool listingWindowsRead;
    public ListingWindows ListingWindows {get=>listingWindows;set{listingWindows=value;listingWindowsRead=true;}}
    private Dictionary<ulong,HashSet<uint>> pendingCollection=[];
    private bool pendingCollectionRead;
    public Dictionary<ulong,HashSet<uint>> PendingCollection
    {get=>pendingCollection;set{pendingCollection=value;pendingCollectionRead=true;}}
    public Dictionary<uint,SourceKind> DutySources { get; set; } = [];
    public bool InitialExclusionsApplied { get; set; }
    private void MigrateControls()
    {
        if(Rules==null||Rules.Glamour==null||Rules.Glamour.Categories==null||Rules.Glamour.ExcludedCategories==null)
            throw new System.Text.Json.JsonException("Invalid glamour filter settings");
        if(ControlsRevision<1)
        {Rules.Glamour.Glamaholic=GlamaholicExpected;Rules.Glamour.EorzeaCollection=UseBundledGlamourList;ControlsRevision=1;}
    }
    void IJsonOnDeserialized.OnDeserialized()
    {
        MigrateControls();
        if(ListingWindows==null||SafetyRevision>=7&&!listingWindowsRead)throw new System.Text.Json.JsonException("Missing listing rotation safety state");
        ListingWindows.Validate();
        if(Features==null||ExcludedSaleRetainers==null||Rules.Sales.Any(r=>r.ExcludedRetainers==null))throw new System.Text.Json.JsonException("Invalid retainer filters");
        // Preserve legacy single-category rules when opening the multi-category editor.
        foreach(var rule in Rules.Sales)
            if(rule.Category!=0){rule.Categories.Add(rule.Category);rule.Category=0;}
        if(Version!=1 || PendingCollection==null || SafetyRevision>=6&&!pendingCollectionRead ||
            PendingCollection.Any(p=>p.Key==0||p.Value==null||p.Value.Contains(0)))
            throw new System.Text.Json.JsonException("Missing or invalid collection safety state; execution must remain held");
    }
}
