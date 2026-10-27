using Steward.Core;
using System.Text.Json;

static class StorageChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var routeRules=new RuleSettings();
        var routeEvidence=new Evidence{ReferencesReady=true,GearsetsReady=true};
        var routeItem=new ItemFacts(14799,"Chestwrap",true,2,60,false,false,SourceKind.Special,35);
        var routeStock=new Stock(14799,false,1,0,15);
        check(StoragePolicy.NeedsCollectionVisit(routeItem,routeStock,routeRules,routeEvidence,false),"Missing live dresser observations request a visit");
        check(StoragePolicy.NeedsCollectionVisit(routeItem,routeStock,routeRules,routeEvidence,true),"Unknown armoire ownership requests observation before collection");
        check(Rules.Judge(routeItem,routeStock,routeRules,routeEvidence,1,now).Outcome==Outcome.Wait,"A route request never grants a deposit or disposal without live ownership");
        routeEvidence.Gearsets.Add(14799);
        check(!StoragePolicy.NeedsCollectionVisit(routeItem,routeStock,routeRules,routeEvidence,false),"Gearset items do not trigger storage visits");
        routeEvidence.Gearsets.Clear();routeRules.Ignored.Add(14799);
        check(!StoragePolicy.NeedsCollectionVisit(routeItem,routeStock,routeRules,routeEvidence,false),"Ignored items do not trigger visits");
        routeRules.Ignored.Clear();routeEvidence.DresserKnown=routeEvidence.ArmoireKnown=true;routeEvidence.DresserFreeSlots=20;
        check(!StoragePolicy.NeedsCollectionVisit(routeItem,routeStock,routeRules,routeEvidence,false),"A full reserved dresser does not cause repeated visits");
        routeEvidence.DresserFreeSlots=21;
        check(StoragePolicy.NeedsCollectionVisit(routeItem,routeStock,routeRules,routeEvidence,false),"Available dresser capacity allows collection visit");
        routeEvidence.Dresser.Add(14799);
        check(!StoragePolicy.NeedsCollectionVisit(routeItem,routeStock,routeRules,routeEvidence,false),"A known collected copy does not trigger another visit");
        var planningJudgements=0;var disposalQueries=0;
        var rejected=Enumerable.Range(1,421).Select(i=>new Stock((uint)i,false,1,10000,(ushort)(i%25))).ToArray();
        var planned=StoragePolicy.PlanWithdrawals(rejected,s=>{planningJudgements++;return new(Outcome.Dispose,"test");},_=>true,s=>{disposalQueries++;return false;},false);
        check(planned.Length==0&&planningJudgements==421&&disposalQueries==421,"421 unselected disposal candidates produce no queued withdrawal operations");
        disposalQueries=0;
        planned=StoragePolicy.PlanWithdrawals([routeStock],_=>new(Outcome.CollectDresser,"Missing copy"),_=>true,_=>{disposalQueries++;return false;},true);
        check(planned.Length==1&&disposalQueries==0,"Collection-only planning does not depend on disposal selection");
        check(StoragePolicy.PlanWithdrawals([routeStock],_=>new(Outcome.Dispose,"test"),_=>true,_=>true,true).Length==0,"Collection-only planning never retrieves disposal stock");
        check(StoragePolicy.PlanWithdrawals([routeStock],_=>new(Outcome.Wait,"Unknown"),_=>true,_=>true,false).Length==0,"Unknown evidence remains a hold during batch planning");
        check(StoragePolicy.PlanWithdrawals([routeStock],_=>new(Outcome.CollectDresser,"Missing copy"),_=>false,_=>true,false).Length==0,"Feature switches still block planned withdrawals");

        List<Stock> liveInventory=[new(42,false,1,0,0),new(42,false,1,0,1),new(43,false,1,0,2),new(44,false,1,0,3)];
        var decisions=0;
        Decision RefreshingJudge(Stock candidate)
        {
            // Production JudgeLive refreshes InventoryView, clearing and repopulating this same list.
            var observed=liveInventory.ToArray();liveInventory.Clear();liveInventory.AddRange(observed);
            decisions++;
            return new(candidate.ItemId==44?Outcome.CollectArmoire:Outcome.CollectDresser,"Live evidence");
        }
        check(StoragePolicy.CountWaitingForDresser(liveInventory,RefreshingJudge)==2,"Live refresh during dresser counting preserves distinct missing-item count");
        check(decisions==4,"Each captured candidate is judged despite inventory refreshes");
        check(!StoragePolicy.CanWithdraw(Outcome.CollectDresser,22,20,2,0),"Snapshot counting preserves reserved dresser capacity");
        check(StoragePolicy.CanWithdraw(Outcome.CollectArmoire,0,20,2,0),"Armoire items do not consume dresser capacity");
        check(StoragePolicy.CountWaitingForDresser([], _=>throw new InvalidOperationException())==0,"Empty inventory does not invoke live judgement");
        var failedClosed=false;
        try{StoragePolicy.CountWaitingForDresser(liveInventory,_=>throw new InvalidOperationException("Evidence unavailable"));}
        catch(InvalidOperationException){failedClosed=true;}
        check(failedClosed,"Failed live judgement stops planning rather than fabricating free capacity");

        var settings=new RuleSettings();
        var evidence=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,DresserFreeSlots=100};
        var facts=new ItemFacts(42,"Blue",true,3,90,false,false,SourceKind.NormalRaid,1);
        var stock=new Stock(42,false,1,0,0);
        Decision Judge()=>Rules.Judge(facts,stock,settings,evidence,1,now);
        foreach(var source in new[]{SourceKind.NormalDungeon,SourceKind.NormalRaid})
        {
            facts=facts with{Source=source};
            check(Judge().Outcome==Outcome.Dispose,"Routine blue exception does not collect unreferenced gear: "+source);
            evidence.References.Add(42);check(Judge().Outcome==Outcome.CollectDresser,"Native/Glamaholic references override routine exception");evidence.References.Clear();
            evidence.CuratedGlamour.Add(42);check(Judge().Outcome==Outcome.CollectDresser,"Common glamour still protects routine raid gear");evidence.CuratedGlamour.Clear();
            facts=facts with{EquipLevel=1};check(Judge().Outcome==Outcome.CollectDresser,"Level-one rule overrides routine exception");facts=facts with{EquipLevel=90};
            settings.Keep.Add(42);check(Judge().Outcome==Outcome.Keep,"Explicit keep overrides routine exception");settings.Keep.Clear();
        }
        foreach(var source in new[]{SourceKind.AllianceRaid,SourceKind.SavageRaid,SourceKind.HighEnd,SourceKind.Special,SourceKind.Unknown,SourceKind.Ordinary})
        {
            facts=facts with{Source=source};check(Judge().Outcome==Outcome.CollectDresser,"No routine exception for "+source);
        }
        evidence.References.Add(42);check(StoragePolicy.Priority(facts,settings,evidence)==0,"Plate references receive first storage priority");evidence.References.Clear();
        facts=facts with{Source=SourceKind.SavageRaid};check(StoragePolicy.Priority(facts,settings,evidence)==1,"Difficult-source protection precedes common glamour suggestions");
        facts=facts with{Source=SourceKind.NormalRaid,Rarity=1};check(StoragePolicy.Priority(facts,settings,evidence)==3,"Common-only ordinary items have lower storage priority");
        facts=facts with{Rarity=3};
        var indexed=AcquisitionSources.Load();
        check(indexed.Values.Count(s=>s==SourceKind.NormalRaid)==825,"Audited normal raid index count");
        check(indexed.Values.Count(s=>s==SourceKind.NormalDungeon)==70,"Audited ordinary dungeon blue count");
        check(indexed[1678]==SourceKind.HighEnd,"Coil is not normal raid");
        check(indexed[32276]==SourceKind.SavageRaid,"Edenmorn stays Savage");
        foreach(var pair in indexed)
        {
            facts=facts with{Id=pair.Key,Source=pair.Value};stock=stock with{ItemId=pair.Key};
            var ordinary=pair.Value is SourceKind.NormalDungeon or SourceKind.NormalRaid;
            check(Judge().Outcome==(ordinary?Outcome.Dispose:Outcome.CollectDresser),"Replay exact audited source classification: "+pair.Key);
        }
        facts=facts with{Id=42,Source=SourceKind.SavageRaid};stock=stock with{ItemId=42};
        foreach(var protectedSource in new[]{SourceKind.AllianceRaid,SourceKind.SavageRaid,SourceKind.Special,SourceKind.HighEnd})
            check(AcquisitionSources.Merge(protectedSource,SourceKind.NormalRaid)==protectedSource,"Routine source cannot override a protected alternate source");
        evidence.DresserFreeSlots=20;
        check(Judge().Outcome==Outcome.StoreRetainer,"Reserve boundary sends protected gear to retainer");
        evidence.DresserFreeSlots=21;check(Judge().Outcome==Outcome.CollectDresser,"One slot beyond reserve may be used");
        evidence.DresserFreeSlots=0;
        check(!ActionPolicy.Allows("Vendor",Judge().Outcome,true,true,false),"Overflow cannot be NPC sold");
        check(!ActionPolicy.Allows("Gc",Judge().Outcome,true,true,false),"Overflow cannot be GC delivered");
        check(!ActionPolicy.Allows("Desynth",Judge().Outcome,true,true,false),"Overflow cannot be desynthed");
        check(!ActionPolicy.Allows("Market",Judge().Outcome,true,true,false),"Overflow cannot be listed");
        check(ActionPolicy.Allows("Store",Judge().Outcome,true,true,false,true,true),"Owned handoff may store overflow");
        check(!ActionPolicy.Allows("Store",Judge().Outcome,true,true,false,true,false),"Another plugin reservation cannot store overflow");
        check(!ActionPolicy.Allows("Store",Judge().Outcome,false,true,false,true,true),"Dry run never stores overflow");
        evidence.PendingCollection.Add(42);
        // Persistence round trip; then rules/source changes while the only copy is stored on a retainer.
        var saved=JsonSerializer.Serialize(new Dictionary<ulong,HashSet<uint>>{{9,new(evidence.PendingCollection)}});
        evidence.PendingCollection=JsonSerializer.Deserialize<Dictionary<ulong,HashSet<uint>>>(saved)![9];
        facts=facts with{Source=SourceKind.NormalRaid,Rarity=1};
        foreach(var container in new uint[]{0,1,10000,10001,10006})
        {
            stock=stock with{Container=container};
            check(Judge().Outcome==Outcome.StoreRetainer,"Durable hold prevents stored last-copy disposal after source/rule changes");
            foreach(var action in new[]{"Vendor","Gc","Desynth","Discard","Market"})
                check(!ActionPolicy.Allows(action,Judge().Outcome,true,true,false),"Stored pending copy blocks "+action);
        }
        evidence.DresserKnown=false;check(Judge().Outcome==Outcome.Wait,"Unknown collection ownership cannot release durable hold");evidence.DresserKnown=true;
        evidence.DresserFreeSlots=21;check(Judge().Outcome==Outcome.CollectDresser,"Pending item is eligible to return when space opens");
        check(!StoragePolicy.CanWithdraw(Outcome.CollectDresser,20,20,0,0),"Full dresser does not pull overflow back out of retainers");
        check(!StoragePolicy.CanWithdraw(Outcome.CollectDresser,null,20,0,0),"Unknown capacity cannot withdraw stored gear");
        check(!StoragePolicy.CanWithdraw(Outcome.CollectDresser,21,20,1,0),"Existing player collection items reserve the free slot");
        check(StoragePolicy.CanWithdraw(Outcome.CollectDresser,22,20,1,0),"Additional free slot permits withdrawal");
        check(!StoragePolicy.CanWithdraw(Outcome.CollectDresser,22,20,1,1),"Earlier withdrawal consumes capacity allowance");
        facts=facts with{ArmoireEligible=true};evidence.Dresser.Add(42);evidence.DresserFreeSlots=0;
        check(Judge().Outcome==Outcome.CollectArmoire,"Armoire migration bypasses full dresser and existing dresser ownership");
        check(StoragePolicy.Restore(Judge().Outcome,true,true),"Cleanup can restore last dresser copy for armoire migration");
        check(!StoragePolicy.Restore(Judge().Outcome,false,true),"Cleanup toggle off prevents armoire restoration");
        check(!StoragePolicy.Restore(Outcome.StoreRetainer,true,false),"Cleanup never restores last protected overflow copy");
        foreach(var outcome in new[]{Outcome.Keep,Outcome.Wait,Outcome.Ignore,Outcome.CollectDresser})
            check(!StoragePolicy.Restore(outcome,true,true),"Cleanup respects all protections: "+outcome);
        check(StoragePolicy.Restore(Outcome.Dispose,true,false),"Cleanup restores unneeded gear");
        check(StoragePolicy.TransferConfirmed(2,1,0,1,1),"Transfer requires both source loss and destination gain");
        check(!StoragePolicy.TransferConfirmed(2,1,0,0,1),"Source removal alone is not storage confirmation");
        check(!StoragePolicy.TransferConfirmed(2,2,0,1,1),"Destination gain alone is not storage confirmation");
        check(!StoragePolicy.TransferConfirmed(2,0,0,1,1),"Unexpected extra source loss is not confirmation");
    }
}
