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
    public bool CleanDresser { get; set; }
    private Dictionary<ulong,HashSet<uint>> pendingCollection=[];
    private bool pendingCollectionRead;
    public Dictionary<ulong,HashSet<uint>> PendingCollection
    {get=>pendingCollection;set{pendingCollection=value;pendingCollectionRead=true;}}
    public Dictionary<uint,SourceKind> DutySources { get; set; } = [];
    public bool InitialExclusionsApplied { get; set; }
    void IJsonOnDeserialized.OnDeserialized()
    {
        // Preserve legacy single-category rules when opening the multi-category editor.
        foreach(var rule in Rules.Sales)
            if(rule.Category!=0){rule.Categories.Add(rule.Category);rule.Category=0;}
        if(Version!=1 || PendingCollection==null || SafetyRevision>=6&&!pendingCollectionRead ||
            PendingCollection.Any(p=>p.Key==0||p.Value==null||p.Value.Contains(0)))
            throw new System.Text.Json.JsonException("Missing or invalid collection safety state; execution must remain held");
    }
}
