using Dalamud.Configuration;
using Steward.Core;

namespace Steward;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Automatic { get; set; }
    public bool ExecutionReviewRequired { get; set; }
    public bool Connected { get; set; } = true;
    public RuleSettings Rules { get; set; } = new();
    public PricingSettings Pricing { get; set; } = new();
    public Dictionary<uint,SourceKind> Sources { get; set; } = [];
    public HashSet<ulong> OwnRetainers { get; set; } = [];
    public bool ImportMarketBuddy { get; set; } = true;
    public bool GlamaholicExpected { get; set; } = true;
    public bool UseBundledGlamourList { get; set; } = true;
    public bool InitialExclusionsApplied { get; set; }
}
