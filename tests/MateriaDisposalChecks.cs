using System.Text.Json;
using Steward;
using Steward.Core;
static class MateriaDisposalChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        foreach(var family in new uint[]{7,14,15,16,17,18,19,20,21,22,23,24,25})
        {
            check(AcquisitionSources.RepeatableMateria(family,SaleRule.Materia,true,false),"Active lower-grade materia is repeatable: "+family);
            check(!AcquisitionSources.RepeatableMateria(family,SaleRule.Materia,true,true),"Existing protection for the top grades is preserved: "+family);
        }
        foreach(var family in new uint[]{1,2,8,13,26,28,33})
            check(!AcquisitionSources.RepeatableMateria(family,SaleRule.Materia,true,false),"Unverified or retired materia stays protected: "+family);
        check(!AcquisitionSources.RepeatableMateria(15,83,true,false)&&!AcquisitionSources.RepeatableMateria(15,SaleRule.Materia,false,false),
            "Demimateria and untradeable rows are outside the repeatable materia rule");
        var low=new ItemFacts(42,"Materia",false,1,1,true,false,SourceKind.Ordinary,SaleRule.Materia);
        var high=low with{IsTopRankMateria=true};
        var r=new RuleSettings{NonEquipmentDisposalCategories=[SaleRule.OtherMateria]};
        var e=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
        var s=new Stock(42,false,1,0,0);
        e.StatisticsRequired=true;e.SaleStatistics[(42,false)]=new(1,42,false,100,1,now,now,100,true);
        check(r.AllowsNonEquipmentDisposal(low)&&!r.AllowsNonEquipmentDisposal(high),"Lower materia permission excludes top two grades");
        check(Rules.Judge(low,s,r,e,1,now).Outcome==Outcome.Dispose,"Lower materia reaches normal disposal judgement");
        check(Rules.Judge(high,s,r,e,1,now).Outcome==Outcome.Keep,"Unselected top materia remains kept");
        check(CollectionCopyEvidence.Capture(low,r,e,true,false,now).NonEquipmentDisposalPermission=="Category "+SaleRule.OtherMateria,"Review identifies materia tier permission");
        r.Sales.Add(new SaleRule{Categories=[SaleRule.OtherMateria],MinimumUnit=1,MinimumTotal=1});
        check(Rules.Judge(low,s,r,e,1,now).Outcome==Outcome.Market,"Sale eligibility wins over materia disposal permission");r.Sales.Clear();
        r.NonEquipmentDisposalCategories=[SaleRule.TopMateria];
        check(!r.AllowsNonEquipmentDisposal(low)&&r.AllowsNonEquipmentDisposal(high),"Top materia permission excludes lower grades");
        r.NonEquipmentDisposalCategories=[SaleRule.OtherMateria];r.Keep.Add(42);
        check(Rules.Judge(low,s,r,e,1,now).Outcome==Outcome.Keep,"Materia disposal preserves explicit keep");r.Keep.Clear();
        foreach(var source in new[]{SourceKind.Unknown,SourceKind.Special})
            check(Rules.Judge(low with{Source=source},s,r,e,1,now).Outcome==Outcome.Keep,"Materia permission preserves acquisition holds");
        var c=new Configuration();c.Rules.NonEquipmentDisposalCategories=[SaleRule.Materia,94];
        var loaded=JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(c))!;
        check(loaded.Rules.NonEquipmentDisposalCategories.SetEquals([SaleRule.TopMateria,SaleRule.OtherMateria,94]),"Legacy all-materia permission expands to both without altering rolls");
        check(loaded.Rules.AllowsNonEquipmentDisposal(low)&&loaded.Rules.AllowsNonEquipmentDisposal(high),"Migration preserves previous permission scope");
        loaded.Rules.NonEquipmentDisposalCategories.Remove(SaleRule.TopMateria);
        loaded=JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(loaded))!;
        check(loaded.Rules.AllowsNonEquipmentDisposal(low)&&!loaded.Rules.AllowsNonEquipmentDisposal(high),"Tier selection survives restart");
        check(!new RuleSettings().AllowsNonEquipmentDisposal(low),"Materia disposal stays opt-in by default");
    }
}
