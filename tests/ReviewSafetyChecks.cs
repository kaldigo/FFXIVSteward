using Steward;
using Steward.Core;
using System.Text.Json;

static class ReviewSafetyChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var root=Path.Combine(Path.GetTempPath(),"steward-review-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"state.json");
            var config=new Configuration{SafetyRevision=6,Automatic=true,LogOnly=false,PendingCollection=new(){{11,[42]},{22,[99]}}};
            AtomicState.Write(path,config);
            var loaded=AtomicState.ReadOrMigrate<Configuration>(path,()=>throw new Exception("Unexpected migration"));
            check(loaded.PendingCollection[11].SetEquals([42]),"Actual settings file reload preserves retainer collection hold");
            var e=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,DresserFreeSlots=0,
                PendingCollection=CollectionHolds.ForCharacter(loaded.PendingCollection,11)};
            check(!e.PendingCollection.Contains(99),"Another character's holds cannot bleed into current character");
            check(CollectionHolds.ForCharacter(loaded.PendingCollection,0).Count==0,"Logged-out state cannot inherit holds");
            var item=new ItemFacts(42,"Stored copy",true,1,90,true,false,SourceKind.NormalRaid,1);
            var stock=new Stock(42,false,1,10000,0);var rules=new RuleSettings();
            rules.Sales.Add(new(){MinimumUnit=1,MinimumTotal=1});e.Prices[(42,false)]=new(42,false,1,1000,now,"Fresh price");
            Decision Judge()=>Rules.Judge(item,stock,rules,e,1,now);
            check(Judge().Outcome==Outcome.StoreRetainer,"Restarted hold beats changed rarity/source and a valid sale rule");
            foreach(var action in new[]{"Vendor","Gc","Desynth","Market","Discard","Retrieve"})
                check(!ActionPolicy.Allows(action,Judge().Outcome,true,true,false),"Restarted stored overflow blocks "+action);
            e.DresserKnown=false;check(Judge().Outcome==Outcome.Wait,"Restart with unavailable collections cannot authorize disposal");e.DresserKnown=true;
            check(!StoragePolicy.CollectionConfirmed(item,e),"Retainer holding alone is not confirmed collection");
            e.Dresser.Add(43);check(!StoragePolicy.CollectionConfirmed(item,e),"A different collected ID cannot clear a hold");
            e.Dresser.Add(42);e.OmittedDresserItem=42;check(!StoragePolicy.CollectionConfirmed(item,e),"Simulated removal cannot release collection hold");e.OmittedDresserItem=null;
            check(StoragePolicy.CollectionConfirmed(item,e),"Live exact dresser ownership can confirm collection");
            check(!StoragePolicy.CollectionConfirmed(item with{ArmoireEligible=true},e),"Dresser copy cannot prematurely release armoire-migration hold");
            e.Armoire.Add(42);check(StoragePolicy.CollectionConfirmed(item with{ArmoireEligible=true},e),"Exact armoire ownership completes armoire migration");e.Armoire.Clear();
            try{CollectionHolds.Release(loaded.PendingCollection[11],[42],()=>throw new IOException("disk failed"));throw new Exception("Save failure swallowed");}
            catch(IOException){check(loaded.PendingCollection[11].Contains(42),"Failed hold-release save rolls back in-memory relaxation");}
            check(AtomicState.ReadOrMigrate<Configuration>(path,()=>throw new Exception()).PendingCollection[11].Contains(42),"Failed release leaves original durable hold intact");
            foreach(var bad in new[]{"{}","{\"Version\":1,\"SafetyRevision\":6}","{\"Version\":1,\"SafetyRevision\":6,\"PendingCollection\":null}","{\"Version\":1,\"SafetyRevision\":6,\"PendingCollection\":{\"11\":null}}","{"})
            {
                File.WriteAllText(path,bad);
                try{_ = AtomicState.ReadOrMigrate<Configuration>(path,()=>new());throw new Exception("Invalid safety state accepted");}
                catch(JsonException){check(true,"Damaged/missing hold state cannot silently reload as empty");}
            }
            var legacy=JsonSerializer.Deserialize<Configuration>("{\"Version\":1,\"SafetyRevision\":5}")!;
            check(legacy.LogOnly,"Pre-overflow settings migrate to log-only by default");
            check(Configuration.FromLegacy(legacy).LogOnly,"Safe old configuration migration preserves review default");
            File.Delete(path);
            try{_ = AtomicState.ReadOrMigrate(path,()=>Configuration.FromLegacy(loaded));throw new Exception("Lost authoritative state silently migrated");}
            catch(InvalidDataException){check(true,"Missing authoritative file cannot fall back to an incomplete compatibility mirror after overflow was introduced");}
            foreach(var automatic in new[]{false,true})foreach(var logOnly in new[]{false,true})
            {
                var mode=new Configuration{Automatic=automatic,LogOnly=logOnly};
                check(mode.ExecutionEnabled==(automatic&&!logOnly),"Only explicit automatic plus log-only off can execute");
                if(logOnly)foreach(var outcome in Enum.GetValues<Outcome>())foreach(var action in new[]{"Gc","Vendor","Desynth","Market","Dresser","Armoire","Retrieve","Store","Discard"})
                    check(!ActionPolicy.Allows(action,outcome,mode.ExecutionEnabled,true,false,true,true),"Log-only blocks every managed action/outcome");
            }
            e=new(){ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,DresserFreeSlots=0,Dresser=[42]};
            item=item with{Marketable=false,Source=SourceKind.NormalRaid};rules=new();
            for(var protection=0;protection<9;protection++)
            {
                e.References.Clear();e.CuratedGlamour.Clear();e.PendingCollection.Clear();e.Gearsets.Clear();rules.Keep.Clear();rules.CommonGlamour.Clear();
                item=item with{Rarity=1,EquipLevel=90,DesignedForGlamour=false,Source=SourceKind.NormalRaid};
                if(protection==0)e.References.Add(42);if(protection==1)e.CuratedGlamour.Add(42);if(protection==2)rules.CommonGlamour.Add(42);
                if(protection==3)item=item with{Rarity=3,Source=SourceKind.SavageRaid};if(protection==4)item=item with{EquipLevel=1};
                if(protection==5)item=item with{DesignedForGlamour=true};if(protection==6)e.PendingCollection.Add(42);if(protection==7)rules.Keep.Add(42);if(protection==8)e.Gearsets.Add(42);
                var decision=StoragePolicy.WithoutDresserCopy(e,42,1,()=>{e.Dresser=new(e.Dresser);return Judge();});
                check(!StoragePolicy.Restore(decision.Outcome,true,false),"Protected last copy survives cleanup and live set replacement, protection "+protection);
                check(e.OmittedDresserItem==null,"Simulation restores omission after each protection check");
            }
            e.Gearsets.Clear();e.References.Add(42);
            check(StoragePolicy.WithoutDresserCopy(e,42,2,Judge).Outcome==Outcome.Dispose,"One surplus protected copy may be restored");
            check(!StoragePolicy.Restore(StoragePolicy.WithoutDresserCopy(e,42,1,Judge).Outcome,true,false),"Second queued removal is blocked once it becomes the last copy");
            check(StoragePolicy.WithoutDresserCopy(e,42,0,Judge).Outcome==Outcome.Wait,"Unconfirmed source copy is never restored");
            try{StoragePolicy.WithoutDresserCopy(e,42,1,()=>throw new IOException());}catch(IOException){}
            check(e.OmittedDresserItem==null,"Exception cannot leave simulated ownership state changed");
            var journal=new ReviewJournal();var log=Path.Combine(root,"review.jsonl");
            var entry=new ReviewEntry(11,"Player inventory slot 0",42,1,false,"WouldDispose","Test proposal");
            check(journal.Snapshot(log,[entry]).Count==1,"Review writes a structured proposal");
            check(journal.Snapshot(log,[entry]).Count==0&&File.ReadAllLines(log).Length==1,"Unchanged scans do not flood the review file");
            check(journal.Snapshot(log,[entry with{Quantity=2}]).Count==1,"Quantity changes produce a fresh proposal");
            journal.Snapshot(log,[]);check(journal.Snapshot(log,[entry]).Count==1,"An item returning after absence is logged again");
            journal.Reset();try{journal.Snapshot(root,[entry]);throw new Exception("Log write failure swallowed");}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
            check(journal.Snapshot(log,[entry]).Count==1,"Failed log write does not suppress the next successful record");
        }
        finally{foreach(var file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}
    }
}
