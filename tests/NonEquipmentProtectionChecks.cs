using Steward.Core;

static class NonEquipmentProtectionChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var fishable=AcquisitionSources.Fishable([(4776,true),(4927,true),(0,true),(8791,false)],[(100,true,10),(101,false,10),(102,true,0),(0,true,10)]);
        check(fishable.SetEquals([4776,4927,100]),"Only logged fish and visible spearfishing catches in a territory prove a fishing source");
        check(AcquisitionSources.Merge(SourceKind.Ordinary,SourceKind.Special)==SourceKind.Special,"Fishing never overrides a known special acquisition source");
        var rules=new RuleSettings {NonEquipmentDisposalCategories=[57],NonEquipmentDisposal=[8791]};
        var evidence=new Evidence {ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
        var item=new ItemFacts(8791,"Eternity Cake",false,1,1,false,false,SourceKind.Unknown,57);
        var stock=new Stock(8791,false,2,0,1);
        Decision Judge()=>Rules.Judge(item,stock,rules,evidence,1,now);
        foreach(var source in new[]{SourceKind.Unknown,SourceKind.Special,SourceKind.AllianceRaid,SourceKind.SavageRaid,SourceKind.HighEnd})
        {
            item=item with{Source=source};
            check(Judge().Outcome==Outcome.Keep,"Non-gear source protection beats item and category disposal permission: "+source);
            foreach(var action in new[]{"Vendor","Gc","Desynth","Retrieve","Discard"})
                check(!ActionPolicy.Allows(action,Judge().Outcome,true,true,false),"Protected non-gear cannot reach "+action);
            evidence.Dresser.Add(item.Id);evidence.Armoire.Add(item.Id);
            check(Judge().Outcome==Outcome.Keep,"Collection or duplicate copies cannot release non-gear source hold");
            check(CollectionCopyEvidence.Capture(item,rules,evidence,true,false,now).ProtectionMatches.Contains(NonEquipmentProtection.Reason(item)!),"Review includes non-gear source hold");
        }
        item=item with{Source=SourceKind.Special,Marketable=true};
        rules.Sales=[new(){MinimumUnit=1,MinimumTotal=1}];evidence.Prices[(item.Id,false)]=new(item.Id,false,1,2000,now,"test");
        check(Judge().Outcome==Outcome.Keep,"Special rewards are not automatically sold");
        item=item with{Source=SourceKind.Unknown};
        check(Judge().Outcome==Outcome.Market,"Unknown-source tradeable stock can satisfy an explicit sale rule");
        rules.Sales.Clear();check(Judge().Outcome==Outcome.Keep,"Failure to match a sale rule never releases unknown source for disposal");
        foreach(var source in new[]{SourceKind.Ordinary,SourceKind.NormalDungeon,SourceKind.NormalRaid})
        {
            item=item with{Source=source};check(Judge().Outcome==Outcome.Dispose,"Known repeatable source remains category-disposable: "+source);
        }
        rules.NonEquipmentDisposal.Clear();rules.NonEquipmentDisposalCategories.Clear();
        check(Judge().Outcome==Outcome.Keep,"Repeatable source alone never grants disposal permission");
        item=new(4927,"Striped Goby",false,1,1,true,false,fishable.Contains(4927)?SourceKind.Ordinary:SourceKind.Unknown,47);
        stock=new(item.Id,false,10,0,1);rules.NonEquipmentDisposalCategories.Add(47);
        evidence.Prices[(item.Id,false)]=new(item.Id,false,1,20,now,"test");
        check(Judge().Outcome==Outcome.Dispose,"Known fish below sale rules may be disposed with category permission");
        rules.Sales=[new(){MinimumUnit=1,MinimumTotal=1}];
        check(Judge().Outcome==Outcome.Market,"Fish sale eligibility still protects stock from disposal");
    }
}
