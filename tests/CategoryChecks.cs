using System.Text.Json;
using Steward;
using Steward.Core;

static class CategoryChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var gear=new ItemFacts(42,"Gear",true,2,90,true,false,SourceKind.NormalRaid,35);
        var mat=new ItemFacts(43,"Material",false,1,1,true,false,SourceKind.Ordinary,54);
        var food=mat with{Id=44,Name="Food",Category=45};
        Stock Stock(ItemFacts item)=>new(item.Id,false,30,0,0);
        var gearRule=new SaleRule{Name="Gear",Group=ItemGroup.Gear,MaximumStack=1,MinimumUnit=5000,MinimumTotal=5000};
        var matRule=new SaleRule{Name="Materials",Group=ItemGroup.Other,Categories=[54,55],MaximumStack=30,MinimumUnit=100,MinimumTotal=1000};
        check(gearRule.Matches(gear,Stock(gear))&&!gearRule.Matches(mat,Stock(mat)),"Gear rule excludes materials");
        check(matRule.Matches(mat,Stock(mat))&&!matRule.Matches(gear,Stock(gear)),"Materials rule excludes gear");
        check(matRule.Matches(mat with{Category=55},Stock(mat)),"Multiple included material categories use OR");
        check(!matRule.Matches(food,Stock(food)),"Unselected non-gear categories do not match materials rule");
        matRule.ExcludedCategories.Add(54);check(!matRule.Matches(mat,Stock(mat)),"Per-rule exclusion beats an included category");matRule.ExcludedCategories.Clear();
        matRule.ItemIds.Add(99);check(!matRule.Matches(mat,Stock(mat)),"Item ID, group and category restrictions intersect");matRule.ItemIds.Clear();
        matRule.Hq=true;check(!matRule.Matches(mat,Stock(mat))&&matRule.Matches(mat,Stock(mat) with{Hq=true}),"Quality restriction still applies to category rules");matRule.Hq=null;
        matRule.Enabled=false;check(!matRule.Matches(mat,Stock(mat)),"Disabled category rule never matches");matRule.Enabled=true;
        var old=JsonSerializer.Deserialize<SaleRule>("{\"Category\":54}")!;
        check(old.Matches(mat,Stock(mat))&&!old.Matches(food,Stock(food)),"Legacy single-category matching remains exact");
        var migrated=JsonSerializer.Deserialize<Configuration>("{\"Version\":1,\"SafetyRevision\":6,\"PendingCollection\":{},\"Rules\":{\"Sales\":[{\"Category\":54}]}}")!;
        check(migrated.Rules.Sales[0].Categories.SetEquals([54])&&migrated.Rules.Sales[0].Category==0,"Legacy category migrates to multi-select without widening");
        check(migrated.Rules.Sales[0].Matches(mat,Stock(mat))&&!migrated.Rules.Sales[0].Matches(food,Stock(food)),"Migrated category still excludes unrelated types");
        var defaults=JsonSerializer.Deserialize<RuleSettings>("{\"Sales\":[{}]}")!;
        check(defaults.IgnoredCategories.Count==0&&defaults.Sales[0].Group==ItemGroup.Any&&defaults.Sales[0].Categories.Count==0,"Existing all-item rules keep their original scope");
        var rules=new RuleSettings{Sales=[gearRule,matRule]};
        var evidence=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,DresserFreeSlots=800};
        evidence.Prices[(42,false)]=new(42,false,1,10000,now,"Test");evidence.Prices[(43,false)]=new(43,false,1,100,now,"Test");
        Decision Judge(ItemFacts item)=>Rules.Judge(item,Stock(item),rules,evidence,1,now);
        check(Judge(gear).Rule==gearRule&&Judge(mat).Rule==matRule,"Rules select different gear and material price/stack policies");
        var plan=ListingPlanner.Plan([new(gear,Stock(gear),Judge(gear),gearRule,new(1,10000,null,now,"Test")),
            new(mat,Stock(mat),Judge(mat),matRule,new(1,100,null,now,"Test"))],new[]{0,1},new(),1,30,QualityMode.Separate,now);
        check(plan.Listings.All(l=>l.Quantity==1&&l.RuleName=="Gear"),"Gear stack-size rule and matching name reach the shared planner");
        plan=ListingPlanner.Plan([new(mat,Stock(mat),Judge(mat),matRule,new(1,200,null,now,"Test"))],new[]{0},new(),1,30,QualityMode.Separate,now);
        check(plan.Listings.Single().Quantity==30&&plan.Listings.Single().RuleName=="Materials","Material rule keeps its independent stack size and logged name");
        rules.IgnoredCategories.Add(35);
        check(Judge(gear).Outcome==Outcome.Ignore&&rules.IsIgnored(gear),"Global category exclusion overrides all handling and price-query eligibility");
        foreach(var action in new[]{"Vendor","Gc","Desynth","Market","Retrieve","Store","Dresser","Armoire"})
            check(!ActionPolicy.Allows(action,Judge(gear).Outcome,true,true,false),"Excluded category cannot reach "+action);
        var proof=CollectionCopyEvidence.Capture(gear,rules,evidence,true,false,now);
        check(proof.ProtectionMatches.Contains("Excluded category")&&proof.ItemCategory==35,"Global exclusion and category appear in audit evidence");
        rules.IgnoredCategories.Clear();evidence.References.Add(42);
        check(Judge(gear).Outcome==Outcome.CollectDresser,"Matching category sale rule cannot override plate collection");
        evidence.Dresser.Add(42);evidence.PendingCollection.Add(42);
        check(StoragePolicy.WithoutDresserCopy(evidence,42,1,()=>Judge(gear)).Outcome==Outcome.CollectDresser,"Category sale cannot remove a protected last copy");
        evidence.References.Clear();evidence.PendingCollection.Clear();
        rules.Sales=[matRule];evidence.Prices[(43,false)]=new(43,false,1,1,now,"Test");
        check(Judge(mat).Outcome==Outcome.Keep,"Material-category rule never grants disposal permission below sale floors");
        var roundtrip=JsonSerializer.Deserialize<RuleSettings>(JsonSerializer.Serialize(new RuleSettings{IgnoredCategories=[45],Sales=[matRule]}))!;
        check(roundtrip.IgnoredCategories.SetEquals([45])&&roundtrip.Sales[0].Categories.SetEquals([54,55]),"Category inclusions and global exclusions persist");
        rules.Sales=[new(){Name="First",ExcludedCategories=[54]},matRule];
        check(rules.Sales.First(r=>r.Matches(mat,Stock(mat)))==matRule,"Rule exclusion allows a later rule to match; global exclusions remain separate");
        var names=new Dictionary<uint,string>{[6]="One-handed Thaumaturge's Arm",[7]="Two-handed Thaumaturge's Arm",[12]="Carpenter primary",[13]="Carpenter secondary",[32]="Fisher primary",[99]="Fisher secondary",[11]="Shield",[40]="Necklace",[54]="Reagent",[57]="Furnishing"};
        var details=names.Keys.ToDictionary(id=>id,id=>(Icon:id,Major:(byte)(id is 6 or 7 or 11?1:id is 12 or 13 or 32 or 99?2:id==40?4:6),Minor:(byte)id));
        var choices=CategoryDisplay.Build(names,details);
        check(choices.Count==7,"Hand variants are one displayed category each");
        check(choices.Single(c=>c.Id==6).Members.ToHashSet().SetEquals([6,7]),"One/two-handed caster arms share one checkbox");
        check(choices.Single(c=>c.Id==32).Members.ToHashSet().SetEquals([32,99]),"Fisher primary/offhand share one checkbox");
        check(choices.Where(c=>c.Group=="Main Arm/Off Arm").Count()==3,"Tools and weapons share the screenshot's group");
        check(choices.Single(c=>c.Id==11).Group=="Armor"&&choices.Single(c=>c.Id==40).Group=="Armor","Shields and accessories join armor");
        check(choices.Single(c=>c.Id==57).Group=="Housing"&&choices.Single(c=>c.Id==54).Group=="Items","Housing and general items use screenshot groups");
        var chosen=new HashSet<uint>{6,54};
        _=CategoryDisplay.Build(names,details);check(chosen.SetEquals([6,54]),"Displaying a partial saved selection never widens it");
        CategoryDisplay.Set(chosen,choices.Where(c=>c.Group=="Main Arm/Off Arm"),true);
        check(chosen.SetEquals([6,7,12,13,32,99,54]),"Group select includes all underlying hand variants and preserves other groups");
        CategoryDisplay.Set(chosen,choices.Where(c=>c.Group=="Main Arm/Off Arm"),false);
        check(chosen.SetEquals([54]),"Group clear removes only that group's categories");
        CategoryDisplay.Set(chosen,[choices.Single(c=>c.Id==6)],true);
        check(chosen.SetEquals([6,7,54]),"Combined checkbox selects both underlying IDs");
    }
}
