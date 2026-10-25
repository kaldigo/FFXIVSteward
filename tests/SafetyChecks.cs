using Steward.Core;

static class SafetyChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var item=new ItemFacts(42,"Required glamour",true,3,90,false,false,SourceKind.Ordinary,1);
        var stock=new Stock(42,false,1,0,0);
        var rules=new RuleSettings();
        Evidence Ready()=>new(){ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
        var evidence=Ready();
        Decision Judge()=>Rules.Judge(item,stock,rules,evidence,1,now);
        evidence.Dresser.Add(42);
        evidence.DresserKnown=false;
        check(Judge().Outcome==Outcome.Wait,"Stale dresser copy cannot authorize loss of a required item");
        evidence=Ready();evidence.Armoire.Add(42);evidence.ArmoireKnown=false;
        check(Judge().Outcome==Outcome.Wait,"Stale armoire copy cannot authorize disposal");
        evidence=Ready();evidence.Dresser.Add(42);evidence.References.Add(42);evidence.InvalidateLive();
        check(!evidence.DresserKnown&&!evidence.ArmoireKnown&&!evidence.GearsetsReady&&!evidence.ReferencesReady,
            "Unavailable/zone-changing native data invalidates every live readiness flag");
        check(Judge().Outcome==Outcome.Wait,"Previously populated protection data remains held after invalidation");
        evidence=Ready();evidence.Dresser.Add(43);
        check(Judge().Outcome==Outcome.CollectDresser,"A different item never satisfies exact collection ownership");
        evidence.Dresser.Add(42);evidence.OmittedDresserItem=42;
        check(Judge().Outcome==Outcome.CollectDresser,"Removing the last required dresser copy is blocked");
        evidence.Dresser=new(evidence.Dresser); // A live refresh can replace the native ownership set.
        check(Judge().Outcome==Outcome.CollectDresser,"Live refresh does not undo last-copy removal simulation");
        evidence.OmittedDresserItem=null;
        check(Judge().Outcome==Outcome.Dispose,"Another verified dresser copy permits surplus restoration");
        evidence.OmittedDresserItem=42;evidence.Armoire.Add(42);
        check(Judge().Outcome==Outcome.Dispose,"Exact armoire copy permits surplus dresser restoration");
        evidence=Ready();stock=stock with{ItemId=43};
        check(Judge().Outcome==Outcome.Wait,"Mismatched inventory item rejected");stock=stock with{ItemId=42,Quantity=0};
        check(Judge().Outcome==Outcome.Wait,"Empty inventory stack rejected");stock=stock with{Quantity=1};
        evidence.Dresser.Add(42);evidence.GearsetsReady=false;
        check(Judge().Outcome==Outcome.Wait,"Unknown equipped/gearset state blocks surplus disposal");
        evidence=Ready();evidence.Dresser.Add(42);rules.Keep.Add(42);
        check(Judge().Outcome==Outcome.Keep,"Explicit keep survives collection duplicates");
        rules.Ignored.Add(42);
        check(Judge().Outcome==Outcome.Ignore,"Exclusion overrides even explicit keep");rules.Keep.Clear();rules.Ignored.Clear();
        item=item with{Marketable=true,Rarity=1,Equipment=false};
        evidence=Ready();evidence.Prices[(42,false)]=new(42,false,2,1000,now,"Wrong world");
        check(Judge().Outcome==Outcome.Wait,"A quote from another world cannot authorize disposal");
        evidence.Prices.Clear();evidence.Prices[(42,true)]=new(42,true,1,1000,now,"HQ only");
        check(Judge().Outcome==Outcome.Wait,"HQ quote cannot value NQ when quality is separate");
        evidence=Ready();item=item with{ArmoireEligible=true};
        check(Judge().Outcome==Outcome.CollectArmoire,"Every armoire-eligible item is collected before sale or disposal");
        item=item with{Source=SourceKind.Unknown};
        check(Judge().Outcome==Outcome.CollectArmoire,"Verified armoire eligibility supplies a collection route even without acquisition metadata");
        var owned=new HashSet<ulong>{10,11};
        check(Pricing.Target(42,false,1000,[new(42,false,1000,1,10),new(42,false,1200,1,11)],owned,
            QualityMode.Separate,new(),1)==null,"Only own listings never produce an undercut target");
        check(Pricing.Target(42,false,null,[new(42,false,900,1,0),new(42,false,1200,1,20)],owned,
            QualityMode.Separate,new(),1)==null,"Unknown seller identity cannot be assumed to be a competing retainer");

        foreach(var outcome in Enum.GetValues<Outcome>())
        {
            foreach(var action in new[]{"Vendor","Desynth","Gc"})
                check(ActionPolicy.Allows(action,outcome,true,true,false)==(outcome==Outcome.Dispose),
                    $"{action} commits only a Dispose decision ({outcome})");
            foreach(var action in new[]{"Vendor","Desynth","Gc","Armoire","Dresser","Retrieve","Market"})
            {
                check(!ActionPolicy.Allows(action,outcome,false,true,false),$"Dry run blocks {action}/{outcome}");
                check(!ActionPolicy.Allows(action,outcome,true,false,false),$"Disconnected policy blocks {action}/{outcome}");
                check(!ActionPolicy.Allows(action,outcome,true,true,true),$"Stopped-run hold blocks {action}/{outcome}");
            }
            check(!ActionPolicy.Allows("Discard",outcome,true,true,false),"Discard always blocked for managed characters");
            check(!ActionPolicy.Allows("Unexpected",outcome,true,true,false),"Unknown action always blocked");
        }
        check(ActionPolicy.Allows("Dresser",Outcome.CollectDresser,true,true,false),"Dresser collection remains enabled");
        check(!ActionPolicy.Allows("Dresser",Outcome.Dispose,true,true,false),"Dresser cannot consume an unapproved item");
        check(ActionPolicy.Allows("Armoire",Outcome.CollectArmoire,true,true,false),"Armoire collection remains enabled");
        check(!ActionPolicy.Allows("Market",Outcome.Keep,true,true,false),"Market shortcut cannot sell needed gear");

        var queue=new OperationQueue();var submissions=0;var released=0;
        queue.Add(new("Uncertain action",()=>{submissions++;return StepResult.Failed;},()=>released++));
        queue.Add(new("Must not execute",()=>{submissions++;return StepResult.Completed;},()=>released++));
        queue.Advance(1);queue.Advance(2);
        check(submissions==1&&released==2&&!queue.Running&&queue.Status.StartsWith("Stopped:"),
            "Failed acknowledgement stops dependent submissions and releases all owned resources");
    }
}
