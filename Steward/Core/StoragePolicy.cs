namespace Steward.Core;

public static class StoragePolicy
{
    // Collection ownership is required for disposal, not for a reversible native deposit.
    // Destination selection and inventory-specific exclusions are checked separately.
    public static bool EntrustCandidate(Decision decision,bool overflowEnabled) =>
        (decision.Outcome!=Outcome.StoreRetainer||overflowEnabled)&&
        decision.Outcome is not (Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Ignore)&&
        decision.Reason is not ("AutoRetainer protection is unavailable" or "Explicit AutoRetainer protection" or "Unlearned or unverified unlock item: keep available to use");
    public static Stock[] PlanWithdrawals(IEnumerable<Stock> inventory,Func<Stock,Decision> judge,
        Func<Outcome,bool> enabled,Func<Stock,bool> disposalSelected,bool collectionOnly,bool disposalOnly=false) =>
        inventory.ToArray().Where(stock=>
        {
            var decision=judge(stock);
            return enabled(decision.Outcome)&&(!disposalOnly&&decision.Outcome is Outcome.CollectArmoire or Outcome.CollectDresser ||
                !collectionOnly&&decision.Outcome==Outcome.Dispose&&disposalSelected(stock));
        }).ToArray();

    // Route planning may request live storage observations; it never grants item-action permission.
    public static bool NeedsCollectionVisit(ItemFacts item,Stock stock,RuleSettings rules,Evidence evidence,bool armoire)
    {
        if(rules.IsIgnored(item)||rules.Keep.Contains(item.Id)||!evidence.ReferencesReady||!evidence.GearsetsReady||evidence.Gearsets.Contains(item.Id))return false;
        if(QuantityReserve.Removable(stock,rules.QuantityReserves.GetValueOrDefault(item.Id),evidence.ReserveStocks) is not >0)return false;
        var matches=GlamourProtection.Match(item,rules,evidence);
        if(matches is GlamourMatch.None or GlamourMatch.Unknown)return false;
        if(armoire)
            return item.ArmoireEligible&&!(evidence.ArmoireKnown?evidence.Armoire.Contains(item.Id):
                evidence.CachedArmoireKnown&&evidence.CachedArmoire.Contains(item.Id));
        if(!item.Equipment||item.ArmoireEligible)return false;
        // A saved positive copy can avoid travel, never authorize disposal or removal.
        if(!evidence.DresserKnown&&evidence.CachedDresserKnown&&evidence.CachedDresser.Contains(item.Id))return false;
        if(!evidence.ArmoireKnown&&evidence.CachedArmoireKnown&&evidence.CachedArmoire.Contains(item.Id))return false;
        return !evidence.DresserKnown || !evidence.Dresser.Contains(item.Id)&&!evidence.Armoire.Contains(item.Id)&&
            Available(evidence.DresserFreeSlots,rules.DresserEmptySlots)>0;
    }

    // Live judgement refreshes the source inventory. Capture candidates before invoking it;
    // this snapshot counts capacity only and never authorizes a transfer.
    public static int CountWaitingForDresser(IEnumerable<Stock> inventory,Func<Stock,Decision> judge) =>
        inventory.ToArray().Where(s=>judge(s).Outcome==Outcome.CollectDresser).Select(s=>s.ItemId).Distinct().Count();

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
