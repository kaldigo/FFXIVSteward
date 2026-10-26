namespace Steward.Core;

public static class StoragePolicy
{
    public static Decision WithoutDresserCopy(Evidence evidence,uint id,int copies,Func<Decision> judge)
    {
        if(copies<=0)return new(Outcome.Wait,"Dresser source copy is not confirmed");
        var previous=evidence.OmittedDresserItem;
        if(copies==1)evidence.OmittedDresserItem=id;
        try{return judge();}finally{evidence.OmittedDresserItem=previous;}
    }
    public static bool CollectionConfirmed(ItemFacts item,Evidence evidence)=>
        evidence.DresserKnown && evidence.ArmoireKnown &&
        (evidence.Armoire.Contains(item.Id)||!item.ArmoireEligible&&evidence.OmittedDresserItem!=item.Id&&evidence.Dresser.Contains(item.Id));
    public static int Priority(ItemFacts item,RuleSettings rules,Evidence evidence)
    {
        if(rules.Keep.Contains(item.Id)||rules.CollectionKeep.Contains(item.Id)||evidence.References.Contains(item.Id))return 0;
        if(item.Rarity>3 || item.Rarity==3&&!item.Marketable&&item.Source is not (SourceKind.NormalDungeon or SourceKind.NormalRaid) ||
            item.Source is SourceKind.Special or SourceKind.AllianceRaid or SourceKind.SavageRaid or SourceKind.HighEnd)return 1;
        if(item.EquipLevel==1||item.DesignedForGlamour||evidence.PendingCollection.Contains(item.Id))return 2;
        return 3;
    }
    public static int Available(int? free,int reserve)=>free is int n?Math.Max(0,n-Math.Clamp(reserve,0,800)):0;
    public static bool CanWithdraw(Outcome outcome,int? free,int reserve,int alreadyWaiting,int withdrawn) =>
        outcome==Outcome.CollectArmoire || outcome==Outcome.Dispose ||
        outcome==Outcome.CollectDresser && Available(free,reserve)>alreadyWaiting+withdrawn;
    public static bool Restore(Outcome withoutCopy,bool cleanupEnabled,bool armoireMissing) =>
        cleanupEnabled && (withoutCopy is Outcome.Dispose or Outcome.Market || armoireMissing && withoutCopy==Outcome.CollectArmoire);
    public static bool TransferConfirmed(long beforeSource,long afterSource,long beforeDestination,long afterDestination,uint quantity) =>
        quantity>0 && beforeSource>=quantity && afterSource==beforeSource-quantity &&
        beforeDestination>=0 && afterDestination==beforeDestination+quantity;
}
