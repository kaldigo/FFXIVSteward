using Steward.Core;
using System.Text.Json;

static class DisposalEvidenceChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var evidence=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
        var rules=new RuleSettings();
        check(JsonSerializer.Deserialize<RuleSettings>("{}")!.NonEquipmentDisposal.Count==0,"Existing saved rules migrate to a protected empty disposal allowlist");
        rules.NonEquipmentDisposal.Add(6221);
        check(JsonSerializer.Deserialize<RuleSettings>(JsonSerializer.Serialize(rules))!.NonEquipmentDisposal.SetEquals([6221]),"Only explicitly allowed item IDs survive a rule reload");
        rules.NonEquipmentDisposal.Clear();
        foreach(var (id,name) in new (uint,string)[]{(6221,"Fantasia"),(12994,"Gold Chocobo Feather"),(35936,"Genji Armor Coffer"),(21783,"Sigmascape Datalog v3.0"),(12601,"Enchanted Mythrite Ink"),(10336,"Soul of the Crafter")})
        {
            var item=new ItemFacts(id,name,false,1,1,false,false,SourceKind.Special,1);
            var stock=new Stock(id,false,1,0,0);
            Decision Judge()=>Rules.Judge(item,stock,rules,evidence,1,now);
            var held=Judge();check(held.Outcome==Outcome.Keep,name+" is not implicitly disposable");
            foreach(var action in new[]{"Vendor","Desynth","Gc","Retrieve","Discard"})
                check(!ActionPolicy.Allows(action,held.Outcome,true,true,false),name+" cannot reach "+action);
            rules.NonEquipmentDisposal.Add(id);check(Judge().Outcome==Outcome.Keep,"Item opt-in cannot override special reward protection for "+name);
            item=item with{Source=SourceKind.Ordinary};check(Judge().Outcome==Outcome.Dispose,"Verified ordinary acquisition still respects explicit permission");
            rules.Keep.Add(id);check(Judge().Outcome==Outcome.Keep,"Explicit keep beats disposal opt-in");rules.Keep.Clear();
            rules.Ignored.Add(id);check(Judge().Outcome==Outcome.Ignore,"Exclusion beats disposal opt-in");rules.Ignored.Clear();
            item=item with{Marketable=true};check(Judge().Outcome==Outcome.Wait,"Opt-in never bypasses unavailable valuation");
            evidence.Prices[(id,false)]=new(id,false,1,2000,now,"Test");
            rules.Sales=[new(){MinimumUnit=1000,MinimumTotal=1000}];
            check(Judge().Outcome==Outcome.Market,"Sale rule beats permitted disposal");
            rules.NonEquipmentDisposal.Clear();check(Judge().Outcome==Outcome.Market,"Default disposal hold still allows an eligible listing");
            rules.Sales.Clear();check(Judge().Outcome==Outcome.Keep,"Known price without a matching sale rule never implicitly disposes material");
            rules.Sales=[new(){MinimumUnit=1000,MinimumTotal=5000}];
            check(Judge().Outcome==Outcome.Keep,"Below-floor partial stack remains protected");
            rules.Sales.Clear();evidence.Prices.Clear();
        }
        // Exact logged material example with the user's current quality and stack settings.
        var ink=new ItemFacts(12601,"Enchanted Mythrite Ink",false,1,1,true,false,SourceKind.Ordinary,1);
        var inkStock=new Stock(12601,true,2,0,0);
        rules.Sales=[new(){MinimumUnit=1000,MinimumTotal=5000,MaximumStack=30}];
        evidence.Prices[(12601,true)]=new(12601,true,1,2000,now,"Observed HQ reference");
        var decision=Rules.Judge(ink,inkStock,rules,evidence,1,now);
        check(decision.Outcome==Outcome.Keep,"Two HQ ink worth 4000 stay protected below the 5000 total floor");
        var input=new ListingInput(ink,inkStock,decision,rules.Sales[0],new(1,2000,null,now,"Test"));
        check(!ListingPlanner.Next(input,new(),1,30,now).Eligible,"Below-floor material cannot be listed or disposed by the fill pass");
        evidence=new(){ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,
            Dresser=[42],DresserCopyCounts=new(){{42,2}},GlamaholicKnown=true,GlamaholicReferences=[42]};
        rules=new();
        var gear=new ItemFacts(42,"Referenced gear",true,3,90,false,false,SourceKind.SavageRaid,1);
        CollectionCopyEvidence Capture(int removals=0)=>CollectionCopyEvidence.Capture(gear,rules,evidence,true,true,now,removals);
        var proof=Capture(1);
        check(proof.DresserCopies==2&&proof.DresserCopiesAfterProposal==1&&proof.CollectionConfirmedAfterProposal==true,"Surplus restore records the exact retained dresser copy");
        check(proof.NativePlateReference==true&&proof.GlamaholicReference==true&&proof.ProtectionMatches.Contains("Nonmarketable blue"),"Evidence records overlapping protection reasons");
        evidence.DresserCopyCounts[42]=1;proof=Capture(1);
        check(proof.CollectionConfirmedAfterProposal==false&&proof.DresserCopiesAfterProposal==0,"Last-copy removal cannot be described as retained collection");
        evidence.Armoire.Add(42);check(Capture(1).CollectionConfirmedAfterProposal==true,"Independent exact armoire copy is visible in evidence");
        evidence.Armoire.Clear();gear=gear with{ArmoireEligible=true};check(Capture().CollectionConfirmedAfterProposal==false,"Dresser alone does not confirm preferred armoire storage");
        gear=gear with{ArmoireEligible=false};evidence.OmittedDresserItem=42;
        check(Capture().LastCopyOmittedForDecision&&Capture().CollectionConfirmedAfterProposal==false,"Logging preserves decision's simulated last-copy omission");evidence.OmittedDresserItem=null;
        evidence.DresserKnown=false;proof=Capture();
        check(proof.DresserCopies==null&&proof.DresserCopiesAfterProposal==null&&proof.CollectionConfirmedAfterProposal==null,"Stale dresser counts remain unknown");
        evidence.ArmoireKnown=false;check(Capture().ArmoireOwned==null,"Unknown cabinet ownership is not false/zero");
        evidence.GlamaholicKnown=false;check(Capture().GlamaholicReference==null,"Unavailable saved references are explicitly unknown");
        evidence.DresserKnown=evidence.ArmoireKnown=true;proof=Capture(2);
        check(!proof.ProposalWithinObservedCopies&&proof.CollectionConfirmedAfterProposal==null,"Contradictory removal count cannot claim confirmed ownership");
        evidence.DresserCopyCounts.Clear();check(Capture().DresserCopies==null,"A known ID without count evidence is never invented as one copy");
        evidence.Dresser.Clear();check(Capture().DresserCopies==0,"Confirmed collection absence is distinct from unknown");
        var path=Path.Combine(Path.GetTempPath(),"steward-evidence-"+Guid.NewGuid().ToString("N")+".jsonl");
        try
        {
            var journal=new ReviewJournal();var entry=new ReviewEntry(11,"Cached retainer preview",42,1,false,"WouldDispose","Test",Collection:proof);
            check(journal.Snapshot(path,[entry]).Count==1,"Structured record persists collection evidence");
            var parsed=JsonSerializer.Deserialize<JsonElement>(File.ReadAllLines(path)[0]);
            check(parsed.GetProperty("proposal").GetProperty("Collection").GetProperty("DresserCopies").GetInt32()==1,"Evidence count survives log serialization");
            check(journal.Snapshot(path,[entry with{Collection=proof with{ObservedAt=now.AddSeconds(5)}}]).Count==0,"Observation time alone does not flood logs");
            check(journal.Snapshot(path,[entry with{Collection=proof with{DresserCopies=2}}]).Count==1,"Changed copy evidence writes a new record");
            journal.Reset();check(journal.Snapshot(path,[entry]).Count==1,"Manual snapshot still emits fresh evidence");
        }
        finally{File.Delete(path);}
    }
}
