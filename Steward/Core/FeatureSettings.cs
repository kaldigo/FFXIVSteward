namespace Steward.Core;

public sealed class FeatureSettings
{
    public bool NewListings { get; set; }=true;
    public bool Repricing { get; set; }=true;
    public bool ListingReturns { get; set; }=true;
    public bool Rotation { get; set; }=true;
    public bool DresserCollection { get; set; }=true;
    public bool ArmoireStorage { get; set; }=true;
    public bool OverflowStorage { get; set; }=true;
    public bool Disposal { get; set; }=true;
    public bool AnyMarket=>NewListings||Repricing||ListingReturns;
    public bool AnyInventoryWork=>DresserCollection||ArmoireStorage||OverflowStorage||Disposal;
    public bool Enabled(Outcome outcome)=>outcome switch
    {
        Outcome.Market=>NewListings,Outcome.CollectDresser=>DresserCollection,
        Outcome.CollectArmoire=>ArmoireStorage,Outcome.StoreRetainer=>OverflowStorage,
        Outcome.Dispose=>Disposal,_=>true
    };
    public bool Allows(string? action,Outcome outcome)=>action switch
    {
        "Market"=>NewListings,"Vendor" or "Desynth" or "Gc"=>Disposal,
        "Dresser"=>DresserCollection,"Armoire"=>ArmoireStorage,"Store"=>OverflowStorage,
        "Retrieve"=>outcome is Outcome.CollectDresser or Outcome.CollectArmoire or Outcome.Dispose&&Enabled(outcome),
        _=>false
    };
    public bool CanRestore(Outcome outcome)=>outcome!=Outcome.CollectArmoire||ArmoireStorage;
}
