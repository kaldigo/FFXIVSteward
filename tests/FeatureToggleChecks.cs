using Steward;
using Steward.Core;
using System.Text.Json;
static class FeatureToggleChecks
{
    public static void Run(Action<bool,string> check)
    {
        for(var bits=0;bits<256;bits++)
        {
            bool On(int n)=>(bits&(1<<n))!=0;
            var f=new FeatureSettings{NewListings=On(0),Repricing=On(1),ListingReturns=On(2),Rotation=On(3),DresserCollection=On(4),ArmoireStorage=On(5),OverflowStorage=On(6),Disposal=On(7)};
            check(f.AnyMarket==(On(0)||On(1)||On(2)),"Market master reflects independent actions, not collection settings");
            check(f.AnyInventoryWork==(On(4)||On(5)||On(6)||On(7)),"Inventory routes do not depend on sale features");
            check(f.Allows("Vendor",Outcome.Dispose)==On(7)&&f.Allows("Gc",Outcome.Dispose)==On(7)&&f.Allows("Desynth",Outcome.Dispose)==On(7),"Disposal toggle gates all three methods");
            check(f.Allows("Retrieve",Outcome.Dispose)==On(7),"No withdrawal for disabled disposal");
            check(f.Allows("Retrieve",Outcome.CollectDresser)==On(4),"No withdrawal for disabled dresser collection");
            check(f.Allows("Retrieve",Outcome.CollectArmoire)==On(5),"No withdrawal for disabled armoire collection");
            check(!f.Allows("Retrieve",Outcome.Keep)&&!f.Allows("Retrieve",Outcome.Wait),"Feature switches never authorize protected or unknown retrieval");
            check(f.Allows("Dresser",Outcome.CollectDresser)==On(4)&&f.Allows("Armoire",Outcome.CollectArmoire)==On(5),"Collection destinations are independent");
            check(f.Allows("Store",Outcome.StoreRetainer)==On(6),"Overflow toggle gates deposits");
            check(f.CanRestore(Outcome.CollectArmoire)==On(5)&&f.CanRestore(Outcome.Dispose),"Armoire off blocks migration but not ordinary dresser cleanup");
            check(f.Enabled(Outcome.Market)==On(0)&&f.Enabled(Outcome.Dispose)==On(7),"Review actions follow enabled features");
            check(f.Enabled(Outcome.CollectDresser)==On(4)&&f.Enabled(Outcome.CollectArmoire)==On(5)&&f.Enabled(Outcome.StoreRetainer)==On(6),"Review collection destinations follow enabled features");
            var config=new Configuration{SafetyRevision=8,PendingCollection=[],ListingWindows=new(),Features=f};
            check(config.SalesEnabledFor(1)==f.AnyMarket,"No enabled market features means no sale retainer work");
            config.ExcludedSaleRetainers.Add(1);check(!config.SalesEnabledFor(1),"Feature switches cannot override a global retainer exclusion");
            var restored=JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!.Features;
            check(JsonSerializer.Serialize(f)==JsonSerializer.Serialize(restored),"Every feature combination survives restart");
        }
        var defaults=new FeatureSettings();check(defaults.AnyMarket&&defaults.AnyInventoryWork&&defaults.Rotation,"Legacy settings preserve existing enabled features");
        var rules=new RuleSettings();var evidence=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,References=[42]};
        var item=new ItemFacts(42,"Protected",true,1,50,false,false,SourceKind.Ordinary,35);var stock=new Stock(42,false,1,0,0);
        var decision=Rules.Judge(item,stock,rules,evidence,83,DateTimeOffset.UtcNow);
        var disabled=new FeatureSettings{DresserCollection=false,ArmoireStorage=false,OverflowStorage=false};
        check(decision.Outcome==Outcome.CollectDresser&&!disabled.Enabled(decision.Outcome),"Disabling movement keeps the underlying last-copy protection");
        check(!ActionPolicy.Allows("Vendor",decision.Outcome,true,true,false),"Disabled collection never turns a protected copy into disposal");
        var json=JsonSerializer.Serialize(new Configuration{SafetyRevision=8,PendingCollection=[],ListingWindows=new()});
        var doc=System.Text.Json.Nodes.JsonNode.Parse(json)!;doc["Features"]=null;
        try{JsonSerializer.Deserialize<Configuration>(doc.ToJsonString());check(false,"Null features accepted");}catch(JsonException){check(true,"Corrupt feature state fails closed");}
    }
}
