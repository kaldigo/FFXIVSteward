namespace Steward.Core;

// Observation only: this record never authorizes an operation or substitutes cached inventory for collection ownership.
public sealed record CollectionCopyEvidence(
    DateTimeOffset ObservedAt,string Origin,bool DresserKnown,bool ArmoireKnown,
    int? DresserCopies,bool? ArmoireOwned,bool ArmoireEligible,int ProposedDresserRemovals,
    int? DresserCopiesAfterProposal,bool? CollectionConfirmedAfterProposal,bool ProposalWithinObservedCopies,
    bool LastCopyOmittedForDecision,bool? NativePlateReference,bool? GlamaholicReference,
    bool GearsetsReady,bool? GearsetReference,bool ReferencesReady,string ProtectionMatches,
    bool NonEquipmentDisposalAllowed,SourceKind AcquisitionSource,byte Rarity,bool Marketable)
{
    public static CollectionCopyEvidence Capture(ItemFacts item,RuleSettings rules,Evidence evidence,
        bool nativeKnown,bool nativeReference,DateTimeOffset observedAt,int removals=0)
    {
        int? copies=null;
        if(evidence.DresserKnown)
            copies=evidence.DresserCopyCounts.TryGetValue(item.Id,out var count)?count:
                evidence.Dresser.Contains(item.Id)?null:0;
        bool? armoire=evidence.ArmoireKnown?evidence.Armoire.Contains(item.Id):null;
        var omitted=evidence.OmittedDresserItem==item.Id;
        var valid=removals>=0 && (removals==0 || copies is int n&&removals<=n);
        int? after=copies is int before&&valid?Math.Max(0,before-removals):null;
        if(omitted&&after==1)after=0;
        bool? confirmed=null;
        if(evidence.DresserKnown&&evidence.ArmoireKnown&&valid)
            confirmed=armoire==true?true:item.ArmoireEligible?false:after is int remaining?remaining>0:null;
        List<string> matches=[];
        if(rules.Ignored.Contains(item.Id))matches.Add("Excluded");
        if(rules.Keep.Contains(item.Id))matches.Add("Explicit keep");
        if(evidence.GearsetsReady&&evidence.Gearsets.Contains(item.Id))matches.Add("Gearset/equipped");
        if(nativeKnown&&nativeReference)matches.Add("Native glamour plate");
        if(evidence.GlamaholicKnown&&evidence.GlamaholicReferences.Contains(item.Id))matches.Add("Glamaholic");
        if(rules.CommonGlamour.Contains(item.Id)||evidence.CuratedGlamour.Contains(item.Id))matches.Add("Common glamour");
        if(evidence.PendingCollection.Contains(item.Id))matches.Add("Pending collection hold");
        if(item.ArmoireEligible)matches.Add("Armoire eligible");
        if(item.Equipment)
        {
            if(item.Rarity>3)matches.Add("Higher rarity");
            if(item.Rarity==3&&!item.Marketable&&item.Source is not (SourceKind.NormalDungeon or SourceKind.NormalRaid))matches.Add("Nonmarketable blue");
            if(item.EquipLevel==1)matches.Add("Equip level 1");
            if(item.DesignedForGlamour)matches.Add("Glamour design");
            if(item.Source is not (SourceKind.Ordinary or SourceKind.NormalDungeon or SourceKind.NormalRaid))matches.Add("Special/unknown acquisition");
        }
        if(!item.Equipment&&!rules.NonEquipmentDisposal.Contains(item.Id))matches.Add("Non-equipment disposal hold");
        return new(observedAt,"Live game collection observation; cached retainer source stacks remain unverified",
            evidence.DresserKnown,evidence.ArmoireKnown,copies,armoire,item.ArmoireEligible,removals,after,confirmed,valid,
            omitted,nativeKnown?nativeReference:null,evidence.GlamaholicKnown?evidence.GlamaholicReferences.Contains(item.Id):null,
            evidence.GearsetsReady,evidence.GearsetsReady?evidence.Gearsets.Contains(item.Id):null,evidence.ReferencesReady,
            string.Join("; ",matches),rules.NonEquipmentDisposal.Contains(item.Id),item.Source,item.Rarity,item.Marketable);
    }
}
