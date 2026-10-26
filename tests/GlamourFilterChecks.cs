using System.Text.Json;
using Steward;
using Steward.Core;

static class GlamourFilterChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        GlamourFilters Disabled(){var f=new GlamourFilters();foreach(var p in typeof(GlamourFilters).GetProperties().Where(p=>p.PropertyType==typeof(bool)))p.SetValue(f,false);return f;}
        var ordinary=new ItemFacts(42,"Test equipment",true,1,50,false,false,SourceKind.Ordinary,10);
        var stock=new Stock(42,false,1,0,0);
        var e=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,DresserFreeSlots=100};
        var rules=new RuleSettings();
        var cases=new (string Property,ItemFacts Item)[]
        {
            ("LevelOne",ordinary with{EquipLevel=1}),("GlamourDesign",ordinary with{DesignedForGlamour=true}),
            ("NonmarketableBlue",ordinary with{Rarity=3}),("HigherRarity",ordinary with{Rarity=4}),
            ("SpecialRewards",ordinary with{Source=SourceKind.Special}),("AllianceRaids",ordinary with{Source=SourceKind.AllianceRaid}),
            ("SavageRaids",ordinary with{Source=SourceKind.SavageRaid}),("HighEndDuties",ordinary with{Source=SourceKind.HighEnd}),
            ("NormalRaids",ordinary with{Source=SourceKind.NormalRaid}),("NormalDungeons",ordinary with{Source=SourceKind.NormalDungeon}),
            ("EorzeaCollection",ordinary),("NativePlates",ordinary),("Glamaholic",ordinary)
        };
        foreach(var (property,item) in cases)
        {
            rules.Glamour=Disabled();e.CuratedGlamour=property=="EorzeaCollection"?[42]:[];
            e.References=property is "NativePlates" or "Glamaholic"?[42]:[];
            check(Rules.Judge(item,stock,rules,e,83,now).Outcome==Outcome.Dispose,"Disabled filter does not collect: "+property);
            typeof(GlamourFilters).GetProperty(property)!.SetValue(rules.Glamour,true);
            check(Rules.Judge(item,stock,rules,e,83,now).Outcome==Outcome.CollectDresser,"Enabled filter routes collection: "+property);
            rules.Glamour.ExcludedCategories.Add(10);
            check(Rules.Judge(item,stock,rules,e,83,now).Outcome==(property is "NativePlates" or "Glamaholic"?Outcome.CollectDresser:Outcome.Dispose),"Automatic category scope preserves saved outfits: "+property);
        }
        rules=new();e.References=[];e.CuratedGlamour=[];
        var blue=ordinary with{Rarity=3,Source=SourceKind.AllianceRaid};
        rules.Glamour.AllianceRaids=false;check(GlamourProtection.Match(blue,rules,e).HasFlag(GlamourMatch.Blue),"Disabling source filter cannot override enabled rarity protection");
        rules.Glamour.NonmarketableBlue=false;rules.Glamour.AllianceRaids=true;
        check(Rules.Judge(blue,stock,rules,e,83,now).Outcome==Outcome.CollectDresser,"Alliance filter protects independently of blue filter");
        check(!GlamourProtection.Match(ordinary with{Rarity=3,Marketable=true},rules,e).HasFlag(GlamourMatch.Blue),"Marketable blue excluded from blue filter");
        rules.Glamour.NonmarketableBlue=true;
        foreach(var source in new[]{SourceKind.NormalDungeon,SourceKind.NormalRaid})check(GlamourProtection.Match(ordinary with{Rarity=3,Source=source},rules,e)==GlamourMatch.None,"Normal duties remain off by default");
        rules.Glamour=Disabled();
        check(Rules.Judge(ordinary with{Source=SourceKind.Unknown},stock,rules,e,83,now).Outcome==Outcome.Keep,"Unknown acquisition remains safe even with optional filters off");
        check(Rules.Judge(ordinary with{ArmoireEligible=true},stock,rules,e,83,now).Outcome==Outcome.CollectArmoire,"Armoire storage still preferred");
        e.PendingCollection.Add(42);e.Dresser=[42];e.DresserCopyCounts=new(){[42]=1};
        check(StoragePolicy.WithoutDresserCopy(e,42,1,()=>Rules.Judge(ordinary,stock,rules,e,83,now)).Outcome==Outcome.CollectDresser,"Filter edits cannot remove a pending protected last copy");
        e.InvalidateLive();e.ReferencesReady=e.GearsetsReady=true;e.CachedDresser=[42];
        check(Rules.Judge(ordinary,stock,rules,e,83,now).Outcome==Outcome.Keep,"Optional filters never promote cached ownership to disposal evidence");
        var f=new GlamourFilters{NativePlates=false,Glamaholic=false};
        check(GlamourProtection.ReferencesReady(f,false,false),"Unselected reference integrations are not required");
        f.Glamaholic=true;check(!GlamourProtection.ReferencesReady(f,true,false),"Selected missing Glamaholic still holds all handling");
        check(GlamourProtection.ReferencesReady(f,false,true),"Glamaholic-only setup does not require native plate editor");
        var config=new Configuration{SafetyRevision=6,PendingCollection=[],GlamaholicExpected=false,UseBundledGlamourList=false};
        var migrated=JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;
        check(!migrated.Rules.Glamour.Glamaholic&&!migrated.Rules.Glamour.EorzeaCollection&&migrated.Rules.Glamour.AllianceRaids,"Existing preferences survive controls migration");
        var legacy=Configuration.FromLegacy(new Configuration{GlamaholicExpected=false,UseBundledGlamourList=false});
        check(!legacy.Rules.Glamour.Glamaholic&&!legacy.Rules.Glamour.EorzeaCollection,"Pre-authoritative legacy migration also preserves disabled filters");
        migrated.Rules.Glamour.NormalDungeons=true;
        var reloaded=JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(migrated))!;
        check(reloaded.Rules.Glamour.NormalDungeons&&!reloaded.Rules.Glamour.Glamaholic,"Custom filter settings survive restart");
    }
}
