using Steward.Core;

var total=0;
void Check(bool condition,string name) { total++; if(!condition) throw new Exception(name); }
Check(MarketContextOwnership.Matches(129,129,0,0,0,0),"Native context owner validates observed top-level menu without parent/host");
Check(MarketContextOwnership.Matches(129,129,0,0,14,14),"Native context owner validates later market row");
Check(!MarketContextOwnership.Matches(129,130,129,129,0,0),"Contradictory native owner cannot be overridden by stale addon linkage");
Check(!MarketContextOwnership.Matches(129,129,0,0,1,0),"Correct owner but wrong listing row is rejected");
Check(!MarketContextOwnership.Matches(129,0,0,0,0,0),"No ownership evidence remains a hold");
Check(!MarketContextOwnership.Matches(0,0,0,0,0,0),"Missing selling addon cannot own a menu");
Check(!MarketContextOwnership.Matches(129,129,0,0,-1,-1),"Unselected row cannot own a menu");
{
    var submitted=false;var next=false;var skipped=false;var journal=false;
    var q=new OperationQueue(_=>journal=true,()=>journal=false);
    q.Add(new("Market check failed before submission",()=>StepResult.Failed,CanSkip:()=>!submitted,OnSkipped:()=>
    {skipped=true;q.Add(new("Next eligible listing",()=>{next=true;return StepResult.Completed;}));}));
    q.Advance(0);q.Advance(1);
    Check(skipped&&next&&!journal&&!q.Running,"Recoverable market failure advances listing pass after clearing only its unsubmitted journal");
    q.Begin();skipped=false;
    q.Add(new("Uncertain withdrawal",()=>{submitted=true;return StepResult.Failed;},CanSkip:()=>!submitted,OnSkipped:()=>skipped=true));
    q.Advance(2);Check(!skipped&&journal&&q.Status.StartsWith("Stopped:"),"Possible submitted withdrawal retains hold and never invokes continuation");
    q=new OperationQueue();skipped=false;
    q.Add(new("Queued item",()=>StepResult.Failed,CanSkip:()=>true,OnSkipped:()=>skipped=true));q.Stop("Cancelled");
    Check(!skipped,"Cancellation does not schedule a fresh listing pass");
}
{
    var proof=new SessionCollection();
    Check(proof.Read(12)==null,"Disk caches cannot create same-login collection proof");
    Dictionary<uint,int> native=new(){[18311]=1,[18310]=1,[18283]=1};
    proof.Seal(12,native,true,false);native.Clear();
    Check(proof.Read(12)?.Count==3,"Verified close captures independent exact copy counts");
    Check(proof.Read(13)==null,"Collection proof is character scoped");
    proof.Read(12)!.Clear();Check(proof.Read(12)?.Count==3,"Readers cannot mutate retained proof");
    proof.Invalidate();Check(proof.Read(12)==null,"Opening dresser or logout invalidates proof before edits");
    proof.Seal(12,new Dictionary<uint,int>(),true,false);
    Check(proof.Read(12)?.Count==0,"Removing last copy replaces previous ownership with observed empty contents");
    proof.Seal(12,new Dictionary<uint,int>{{18311,1}},false,false);Check(proof.Read(12)==null,"Unavailable native read never seals old cached contents");
    proof.Seal(12,new Dictionary<uint,int>{{18311,1}},true,true);Check(proof.Read(12)==null,"Uncertain or pending operation cannot authorize surplus disposal");
}
{
    foreach(var reason in new[]{"Cached collection copy is unverified; preserve until collection is observed","Live dresser or armoire ownership is unavailable","Special or difficult-to-replace non-equipment reward; preserve all copies"})
        Check(StoragePolicy.EntrustCandidate(new(Outcome.Keep,reason),true),"Safe storage does not require disposal ownership proof: "+reason);
    foreach(var reason in new[]{"AutoRetainer protection is unavailable","Explicit AutoRetainer protection","Unlearned or unverified unlock item: keep available to use"})
        Check(!StoragePolicy.EntrustCandidate(new(Outcome.Keep,reason),true),"Entrust retains native and use-first holds: "+reason);
    foreach(var outcome in new[]{Outcome.Ignore,Outcome.CollectArmoire,Outcome.CollectDresser})
        Check(!StoragePolicy.EntrustCandidate(new(outcome,"test"),true),"Entrust does not divert known pending collection or ignored items");
    Check(!StoragePolicy.EntrustCandidate(new(Outcome.StoreRetainer,"test"),false),"Disabled overflow remains disabled");
    var run=new StorageRun();run.Restored(12);
    Check(!run.EntrustPending,"No follow-up from an unauthorised dresser action");
    run.Begin(12,true);run.Restored(13);Check(!run.EntrustPending,"No cross-character follow-up");
    run.Restored(12);Check(run.EntrustPending,"Confirmed restoration requests native entrust");
    run.EntrustStarted();Check(!run.EntrustPending,"Accepted pass consumes request so unique skips cannot loop");
    run.Restored(12);run.End();Check(!run.EntrustPending,"Stopping clears follow-up permission");
    run.Begin(12,true);run.Restored(12);run.Begin(13,true);Check(!run.EntrustPending,"Character changes clear pending storage");
}
{
    Stock[] sources=[new(1,false,1,10000,0),new(2,false,1,10000,1),new(3,false,1,10000,2),new(4,false,1,10000,3)];
    Decision JudgeCleanup(Stock s)=>new(s.ItemId switch{1=>Outcome.CollectDresser,2=>Outcome.Dispose,3=>Outcome.Ignore,_=>Outcome.Dispose},"test");
    var selected=StoragePolicy.PlanWithdrawals(sources,JudgeCleanup,_=>true,s=>s.ItemId==2,false,true);
    Check(selected.SequenceEqual([sources[1]]),"Full cleanup withdraws only approved, method-selected disposal items, never protected collection or ignored gear");
    Check(StoragePolicy.PlanWithdrawals(sources,JudgeCleanup,_=>false,_=>true,false,true).Length==0,"Disabled disposal cannot supply a batch");
    Check(StoragePolicy.PlanWithdrawals(sources,JudgeCleanup,_=>true,_=>false,false,true).Length==0,"Demo mode or no selected method cannot withdraw a disposal batch");
}
{
    var run=new StorageRun();
    Check(!run.Allows(12,true),"Enabling automation at startup does not start storage");
    run.Begin(12,false);
    Check(!run.Allows(12,true),"Unowned AutoRetainer handoff does not authorize storage");
    run.Begin(12,true);
    Check(run.Allows(12,true),"Owned retainer handoff permits collection follow-up");
    Check(run.Allows(12,true),"Authorization survives withdrawal and dresser stages");
    Check(!run.Allows(13,true),"Storage run cannot cross characters");
    Check(!run.Allows(12,false),"Log-only or disabled execution blocks the active run");
    run.End();
    Check(!run.Allows(12,true),"Stopping then re-enabling does not revive an old storage run");
    run.Begin(12,true);
    Check(run.Allows(12,true),"Explicit successful Run now can start a fresh run");
    run.End();
    Check(!run.Allows(12,true),"Completed storage waits for another AutoRetainer handoff");
    Check(!new StorageRun().Allows(12,true),"Plugin reload never restores a storage run from enabled settings");
}
// Crash regression: native ordinary left-click callbacks use the agent index, not the tree index.
Check(DresserSource.SelectionArguments(0).SequenceEqual(new uint[]{14,0,0}),"Manual chestwrap callback translation");
Check(DresserSource.SelectionArguments(13).SequenceEqual(new uint[]{14,13,0}),"Crashed source row callback translation uses agent row thirteen");
Check(DresserSource.SelectionArguments(139).SequenceEqual(new uint[]{14,139,0}),"Last valid native candidate index");
foreach(var invalid in new[]{-1,140,int.MaxValue})
{
    var rejected=false;
    try{DresserSource.SelectionArguments(invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
    Check(rejected,"Invalid native index cannot be submitted");
}
// Real native source-category table and tree metadata; no display names or ordinal assumptions.
{
    uint[][] categories=[[0,1,2,3],[3500,3200],[3201,3202,3203],[3205,3206],[3207,3208],[3209,3300]];
    for(var c=0;c<categories.Length;c++)foreach(var container in categories[c])
        Check(DresserSource.Category(container)==c,$"Dresser source category for {container}");
    foreach(var container in new uint[]{10000,2000,3204,3400,uint.MaxValue})
        Check(DresserSource.Category(container)==-1,"Unsupported container is never assigned to inventory category");
    var source=new Stock(47948,false,1,0,3);
    DresserTreeRow[] rows=[new(0,4,2,0,3),new(1,0,0,0,9),new(2,0,2,0,7),new(3,0,2,1,3),new(8,0,2,0,3)];
    Check(DresserSource.Find(rows,2,source)==8,"Header and unrelated duplicates do not match exact candidate");
    Check(DresserSource.Find(rows.Reverse(),2,source)==8,"Tree reordering does not alter exact source mapping");
    Check(DresserSource.Find(rows,1,source)==-1,"Stale agent index rejected");
    Check(DresserSource.Find(rows,2,source with{Slot=4})==-1,"Missing source slot rejected");
    Check(DresserSource.Find(rows.Append(new(9,0,2,0,3)),2,source)==-1,"Ambiguous metadata rejected");
    Check(DresserSource.Find([new(0,4,0,0,0),new(1,0,0,0,0)],0,new(14799,false,1,0,0))==1,"Recorded chestwrap agent row zero maps past header to tree index one");
    Check(DresserSource.Find([new(1,2,0,0,0)],0,new(14799,false,1,0,0))==-1,"Outfit group cannot masquerade as a single source item");
}
// Replay the manual trace's parentless Yes/No ownership, and reject replacement dialogs/sources.
{
    var traced=new Stock(14799,false,1,0,0);
    Check(DresserConfirmation.Acknowledge(traced,traced,true,true,true,false)==StepResult.Waiting,"Trace: confirmation opened, no transfer yet");
    Check(DresserConfirmation.Acknowledge(traced,traced,false,true,true,false)==StepResult.Waiting,"Trace: yes closes dialog before server transfer");
    Check(DresserConfirmation.Acknowledge(traced,null,false,true,true,false)==StepResult.Waiting,"Source removed but destination not observed waits");
    Check(DresserConfirmation.Acknowledge(traced,null,false,true,false,true)==StepResult.Waiting,"Cached ownership does not acknowledge");
    Check(DresserConfirmation.Acknowledge(traced,null,false,false,true,true)==StepResult.Waiting,"Unloaded source does not count as removed");
    Check(DresserConfirmation.Acknowledge(traced,null,false,true,true,true)==StepResult.Completed,"Trace: exact source removed and dresser ownership observed");
    Check(DresserConfirmation.Acknowledge(traced,traced with{ItemId=42},false,true,true,true)==StepResult.Failed,"Unexpected slot replacement holds the run");
    Check(DresserConfirmation.Matches(129,129,131,131,traced,14799,0,0),"Manual dresser trace context owns parentless confirmation");
    Check(!DresserConfirmation.Matches(129,129,131,0,traced,14799,0,0),"No context after confirmation does not authorize another click");
    Check(!DresserConfirmation.Matches(129,129,132,131,traced,14799,0,0),"Unrelated confirmation rejected");
    Check(!DresserConfirmation.Matches(129,130,131,131,traced,14799,0,0),"Recreated source window rejected");
    Check(!DresserConfirmation.Matches(129,129,131,131,traced,14800,0,0),"Wrong item rejected");
    Check(!DresserConfirmation.Matches(129,129,131,131,traced,14799,1,0),"Wrong inventory rejected");
    Check(!DresserConfirmation.Matches(129,129,131,131,traced,14799,0,1),"Wrong slot rejected");
    Check(!DresserConfirmation.Matches(0,0,131,131,traced,14799,0,0),"Unknown source owner rejected");
}
// Outfit routing uses current destination contents and quality, never unrelated inventory pieces.
{
    var piece=new Stock(42,false,1,0,7);
    DresserOutfit[] definitions=[new(500,[42,43,44]),new(501,[42,45])];
    Check(DresserDeposit.Choose(piece with{ItemId=99},definitions,[]).Kind==DresserDepositKind.Ordinary,"Non-outfit gear uses ordinary deposit");
    Check(DresserDeposit.Choose(piece,definitions,[])==new DresserDeposit(DresserDepositKind.NewOutfit,500),"First outfit piece creates deterministic outfit");
    Check(DresserDeposit.Choose(piece with{Hq=true},definitions,[])==new DresserDeposit(DresserDepositKind.NewOutfit,1000500),"HQ outfit encoding matches native quality check");
    StoredDresserOutfit[] stored=[new(501,false,12,new HashSet<uint>{45}),new(500,true,3,new HashSet<uint>{43})];
    Check(DresserDeposit.Choose(piece,definitions,stored)==new DresserDeposit(DresserDepositKind.ExistingOutfit,12),"Existing partial outfit preferred across alternative definitions");
    Check(DresserDeposit.Choose(piece with{Hq=true},definitions,stored)==new DresserDeposit(DresserDepositKind.ExistingOutfit,3),"HQ and NQ destinations are not mixed");
    Check(DresserDeposit.Choose(piece,definitions,[new(500,false,0,new HashSet<uint>{43})]).Target==0,"Dresser slot zero is a valid destination");
    Check(DresserDeposit.Choose(piece,definitions,[new(999,false,4,new HashSet<uint>())]).Kind==DresserDepositKind.NewOutfit,"Unrelated stored set cannot receive a piece");
    Check(DresserDeposit.Choose(piece,definitions,[new(500,false,4,new HashSet<uint>{42})]).Kind==DresserDepositKind.NewOutfit,"Already-unlocked destination slot is never selected for addition; collection judge separately prevents duplicate submission");
    Check(DresserConfirmation.Acknowledge(piece,null,false,true,false,true)==StepResult.Waiting,"Native success cannot rely on cached ownership");
    Check(DresserConfirmation.Acknowledge(piece,piece,false,true,true,true)==StepResult.Waiting,"Owned destination without source removal cannot acknowledge");
    Check(DresserConfirmation.Acknowledge(piece,null,false,true,true,false)==StepResult.Waiting,"Source removal without destination cannot acknowledge");
    Check(DresserConfirmation.Acknowledge(piece,null,false,true,true,true)==StepResult.Completed,"Native ordinary/outfit transfer requires both endpoints");
}
// Dresser preparation is batch-level and accepts self-repairs above 100 percent.
{
    var a=new Stock(42,false,1,0,0);var b=new Stock(43,false,1,0,1);
    Check(!DresserPreparation.Check([new(a,99)],10).Ready,"Damaged collection gear blocks the dresser visit");
    Check(DresserPreparation.Check([new(a,100),new(b,200)],10).Ready,"Both NPC and self-repaired gear are ready");
    var both=DresserPreparation.Check([new(a,50),new(b,100)],0);
    Check(both.RepairItems.SequenceEqual(new uint[]{42})&&both.MissingPrisms,"Repairs and prisms are reported separately");
    Check(!DresserPreparation.Check([new(a,null)],10).Ready,"Unknown condition cannot pass preparation");
    Check(DresserPreparation.Check([],0).Ready,"A data-only dresser visit does not require repair or prisms");
    Check(DresserPreparation.Check([new(a,100)],1).Ready,"Repair completion unblocks on the next observation");
    Check(DresserPreparation.Check([new(a,20),new(a,20)],5).RepairItems.Length==1,"Repair summary does not repeat identical item IDs");
}
var now=DateTimeOffset.UtcNow;
var settings=new RuleSettings();
var e=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
var item=new ItemFacts(42,"Test",true,3,90,false,true,SourceKind.Special,1);
var stock=new Stock(42,false,1,0,0);
Decision Judge() => Rules.Judge(item,stock,settings,e,1,now);
{
    var shisui=new ItemFacts(18311,"Shisui Zori of Scouting",true,2,63,false,false,SourceKind.NormalDungeon,38);
    var surplus=new Stock(18311,false,1,0,1);
    var rules=new RuleSettings();rules.CollectionKeep.Add(18311);
    var proof=new Evidence{ReferencesReady=true,GearsetsReady=true,ArmoireKnown=true,CachedDresserKnown=true,CachedDresser=[18311]};
    Decision Surplus()=>Rules.Judge(shisui,surplus,rules,proof,1,now);
    Check(Surplus().Outcome==Outcome.Keep,"Persisted Shisui ownership alone cannot permit disposal");
    var session=new SessionCollection();session.Seal(12,new Dictionary<uint,int>{{18311,1}},true,false);
    proof.DresserCopyCounts=session.Read(12)!;proof.Dresser=proof.DresserCopyCounts.Keys.ToHashSet();proof.DresserKnown=true;proof.SessionDresserObservedAt=session.ObservedAt;
    Check(Surplus().Outcome==Outcome.Dispose,"Exact same-login retained Shisui ownership releases surplus to normal disposal");
    proof.Gearsets.Add(18311);Check(Surplus().Outcome==Outcome.Keep,"Session proof never overrides gearset protection");proof.Gearsets.Clear();
    Check(StoragePolicy.WithoutDresserCopy(proof,18311,1,Surplus).Outcome==Outcome.CollectDresser,"Required last dresser copy remains protected with session evidence");
    proof.Dresser.Clear();proof.DresserCopyCounts.Clear();Check(Surplus().Outcome==Outcome.CollectDresser,"Observed removal restores collection protection for the remaining copy");
    proof.DresserKnown=false;Check(Surplus().Outcome==Outcome.Keep,"Reopening without a complete replacement read holds surplus again");
}
{
    var blue=item with{Source=SourceKind.Ordinary,ArmoireEligible=false};
    var proof=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
    var rules=new RuleSettings();
    proof.Prices[(42,false)]=new(42,false,1,1000,now,"Test");
    Decision Blue(ItemFacts facts)=>Rules.Judge(facts,stock,rules,proof,1,now);
    Check(Blue(blue).Outcome==Outcome.CollectDresser,"Unmarketable blue protected by rarity alone");
    blue=blue with{Marketable=true};
    Check(Blue(blue).Outcome==Outcome.Dispose,"Marketable blue not protected by rarity alone");
    proof.References.Add(42);Check(Blue(blue).Outcome==Outcome.CollectDresser,"Plate still protects marketable blue");proof.References.Clear();
    proof.CuratedGlamour.Add(42);Check(Blue(blue).Outcome==Outcome.CollectDresser,"Curated list still protects marketable blue");proof.CuratedGlamour.Clear();
    rules.CommonGlamour.Add(42);Check(Blue(blue).Outcome==Outcome.CollectDresser,"Imported glamour list still protects marketable blue");rules.CommonGlamour.Clear();
    Check(Blue(blue with{Source=SourceKind.Special}).Outcome==Outcome.CollectDresser,"Special source still protects marketable blue");
    Check(Blue(blue with{Source=SourceKind.Unknown}).Outcome==Outcome.Keep,"Unknown source holds marketable blue independently of rarity");
    Check(Blue(blue with{EquipLevel=1}).Outcome==Outcome.CollectDresser,"Level-one rule still protects marketable blue");
    proof.Dresser.Add(42);Check(Blue(blue with{Marketable=false}).Outcome==Outcome.Dispose,"Collected unmarketable blue duplicate still disposes");proof.Dresser.Clear();
    proof.Prices.Clear();Check(Blue(blue).Outcome==Outcome.Wait,"Unavailable market price is not evidence that a blue is unmarketable");
}
Check(Judge().Outcome==Outcome.CollectArmoire,"Missing blue goes to armoire");
e.Armoire.Add(42); Check(Judge().Outcome==Outcome.Dispose,"Collected blue duplicate disposes");
e.Armoire.Clear();e.Dresser.Add(42);Check(Judge().Outcome==Outcome.CollectArmoire,"Dresser copy eligible for armoire is migrated to preferred storage");
e.Gearsets.Add(42);Check(Judge().Outcome==Outcome.Keep,"Gearset beats duplicate disposal");
settings.Ignored.Add(42);Check(Judge().Outcome==Outcome.Ignore,"Ignore has highest priority");
settings.Ignored.Clear();e.Gearsets.Clear();e.Dresser.Clear();e.ArmoireKnown=false;
Check(Judge().Outcome==Outcome.Wait,"Unknown armoire is not empty");e.ArmoireKnown=true;
item=item with{Rarity=1,EquipLevel=1,ArmoireEligible=false,Source=SourceKind.Ordinary};
Check(Judge().Outcome==Outcome.CollectDresser,"Level one glamour collected");
item=item with{EquipLevel=90};Check(Judge().Outcome==Outcome.Dispose,"Ordinary unreferenced gear disposes");
e.CuratedGlamour.Add(42);Check(Judge().Outcome==Outcome.CollectDresser,"Curated dungeon glamour protected");e.CuratedGlamour.Clear();
e.References.Add(42);Check(Judge().Outcome==Outcome.CollectDresser,"Plate protects ordinary gear");
e.References.Clear();item=item with{Source=SourceKind.Unknown};Check(Judge().Outcome==Outcome.Keep,"Unknown source held");
item=item with{Equipment=false,Marketable=true,Source=SourceKind.Ordinary};
Check(Judge().Outcome==Outcome.Wait,"Missing price blocks disposal");
e.Prices[(42,false)]=new(42,false,1,1000,now,"Test");settings.Sales.Add(new(){MinimumUnit=1000,MinimumTotal=5000,MaximumStack=5});
stock=stock with{Quantity=5};Check(Judge().Outcome==Outcome.Market,"Sale criteria match");
stock=stock with{Quantity=4};Check(Judge().Outcome==Outcome.Keep,"Below-floor non-equipment stays protected by default");
settings.NonEquipmentDisposal.Add(42);item=item with{Source=SourceKind.Unknown};Check(Judge().Outcome==Outcome.Keep,"Unknown source cannot be released by explicit disposal permission");
item=item with{Source=SourceKind.Ordinary};Check(Judge().Outcome==Outcome.Dispose,"Verified ordinary non-equipment disposal still respects actual sale thresholds");
settings.NonEquipmentDisposal.Clear();
e.Prices[(42,false)]=e.Prices[(42,false)] with{Observed=now.AddHours(-1)};Check(Judge().Outcome==Outcome.Wait,"Stale price held");
e.Prices[(42,false)]=e.Prices[(42,false)] with{Observed=now.AddMinutes(1)};Check(Judge().Outcome==Outcome.Wait,"Future price held");
e.Prices[(42,false)]=e.Prices[(42,false)] with{Observed=now,Own=true};Check(Judge().Outcome==Outcome.Wait,"Own quote is not competition");
var mb=new PricingSettings();Check(Pricing.Undercut(100,mb,1)==99,"Fixed undercut");
mb.UndercutUsePercent=true;mb.UndercutPercent=10;Check(Pricing.Undercut(100,mb,1)==90,"Percent undercut");
mb.UndercutUsePercent=false;mb.EnablePriceRounding=true;Check(Pricing.Undercut(100,mb,1)==95,"Rounded undercut");
Check(Pricing.Undercut(100,mb,96)==null,"Floor blocks rounded price");
var own=new HashSet<ulong>{1};var offers=new[]{new Offering(42,false,100,1,1),new Offering(42,false,110,1,2)};
Check(Pricing.Target(42,false,100,offers,own,QualityMode.Separate,mb,1)==null,"Do not undercut own cheapest");
Check(Pricing.Target(42,true,null,offers,own,QualityMode.Separate,mb,1)==null,"Separate HQ and NQ");
Check(Imports.Glamaholic("""{"Version":3,"Plates":[{"NodeType":"Folder","Children":[{"NodeType":"Plate","Plate":{"Items":{"Head":{"ItemId":42,"Stain1":5,"Stain2":7}}}}]}]}""").SetEquals(new uint[]{42}),"Nested Glamaholic with dyes ignored");
Check(Imports.Glamaholic("""{"Version":1,"Plates":[{"Items":{"Head":{"ItemId":1000042}}}]}""").Contains(42),"Legacy HQ ID normalized");
try{Imports.Glamaholic("""{"Version":4,"Plates":[]}""");throw new Exception("Accepted unsupported schema");}catch(InvalidDataException){total++;}
try{Imports.Glamaholic("""{"Version":3,"Plates":[{"NodeType":"Unexpected"}]}""");throw new Exception("Accepted unknown node");}catch(InvalidDataException){total++;}
Console.WriteLine($"{total} rule/import/market checks passed.");
var queue=new OperationQueue();var released=0;var sent=0;
queue.Add(new("One",()=>{sent++;return StepResult.Waiting;},()=>released++,100));
queue.Add(new("Two",()=>StepResult.Completed,()=>released++));
queue.Advance(1);queue.Advance(102);Check(!queue.Running&&sent==1&&released==2,"Timeout cancels dependants and releases locks");
queue.Stop("Again");Check(released==2,"Cancellation cleanup is idempotent");
queue.Add(new("Throw",()=>throw new InvalidOperationException("broken"),()=>released++));queue.Advance(200);
Check(!queue.Running&&released==3&&queue.Status.Contains("broken"),"Exception releases owned locks");
queue.Add(new("Throwing cleanup",()=>StepResult.Waiting,()=>throw new InvalidOperationException("cleanup failure")));
queue.Add(new("Must still release",()=>StepResult.Completed,()=>released++));queue.Advance(300);queue.Stop("cancel");
Check(released==4&&!queue.Running&&queue.Status.Contains("cleanup failure"),"One failed cleanup cannot strand later locks");
{
    var storage=new RetainerStorageSettings{Enabled=true,NonmarketableRetainer=2,ExcludedRetainers=[3],ExcludedCategories=[46,55],ExcludedItems=[21800],Categories=new(){[1]=[48],[2]=[]}};
    var proof=new Evidence{GearsetsReady=true};
    var material=new ItemFacts(100,"Stone",false,1,0,true,false,SourceKind.Ordinary,48);
    Check(storage.Block(material,proof,1,4)==null,"Assigned marketable material can be stored");
    Check(storage.Block(material with{Category=44},proof,1,4)==null,"AutoRetainer medicine assignment is not vetoed by a stale Steward map");
    foreach(var marketable in new[]{false,true})
    foreach(var category in new uint[]{1,35,44,48,57,64,95,100})
    {
        var candidate=material with{Category=category,Equipment=category is 1 or 35,Marketable=marketable};
        foreach(var destination in new ulong[]{1,2,12})
            Check(storage.Block(candidate,proof,destination,4)==null,"Steward protections do not duplicate AutoRetainer destination selection");
        Check(storage.Block(candidate,proof,3,4)!=null,"Manual retainer exclusion still applies");
        storage.ExcludedItems.Add(candidate.Id);
        Check(storage.Block(candidate,proof,1,4)!=null,"Explicit item exclusion still applies");
        storage.ExcludedItems.Remove(candidate.Id);
        storage.ExcludedCategories.Add(category);
        Check(storage.Block(candidate,proof,1,4)!=null,"Explicit category exclusion still applies");
        storage.ExcludedCategories.Remove(category);
    }
    foreach(var cat in new uint[]{44,46,55,86})
    {
        if(cat!=86)storage.ExcludedCategories.Add(cat);
        Check(storage.Block(material with{Category=cat},proof,1,1)!=null,"Consumption/card exclusions remain holds");
    }
    Check(storage.Block(material with{Id=21800},proof,1,4)!=null,"Prisms excluded");
    proof.Gearsets.Add(100);Check(storage.Block(material,proof,1,4)!=null,"Gearsets excluded even when AutoRetainer selects item");proof.Gearsets.Clear();
    proof.GearsetsReady=false;Check(storage.Block(material,proof,1,4)!=null,"Unknown gearsets hold storage");proof.GearsetsReady=true;
    foreach(long? status in new long?[]{null,0,2,3})Check(storage.Block(material,proof,1,status)!=null,"Unknown/unlearned unlock held");
    Check(storage.Block(material,proof,1,1)==null,"Learned duplicate eligible");
    var writes=new List<string>();var journal=new ReviewJournal((_,lines)=>writes.AddRange(lines));
    journal.Snapshot(Path.Combine(Path.GetTempPath(),"steward-review-mode","log.jsonl"),[new ReviewEntry(1,"test",100,1,false,"WouldDispose","Disabled")],"DisposalReview");
    Check(writes.Count==1&&writes[0].Contains("DisposalReview"),"Disposal-only review has truthful mode label");
    var cfg=new Steward.Configuration{LogOnly=true};
    Check(!cfg.ExecutionEnabled,"Log-only blocks automatic storage and gil");
    cfg.Features.Disposal=false;Check(!cfg.Features.Allows("Vendor",Outcome.Dispose)&&cfg.LogDisposal,"Disposal logging independent of execution permission");
}
{
    var value=new SaleStatistic(1,42,false,50000,1d/14,now,now,50000,true);
    Check(Math.Abs(SaleTurnover.Days(1,value,now,2)-14)<1e-10,"One sale per fortnight ranking");
    Check(Math.Abs(SaleTurnover.Days(30,value with{DailySales=10},now,2)-3)<1e-10,"Stack quantity influences turnover");
    Check(!SaleTurnover.Improves(14,14),"Equally slow listings do not rotate");
    Check(!SaleTurnover.Improves(3,14),"Slower replacement does not rotate");
    Check(SaleTurnover.Improves(14,3),"Faster replacement can rotate");
    foreach(double? rate in new double?[]{null,0,-1,double.NaN,double.PositiveInfinity})
        Check(double.IsPositiveInfinity(SaleTurnover.Days(1,value with{DailySales=rate},now,2)),"Unknown or invalid velocity holds ranking");
    Check(double.IsPositiveInfinity(SaleTurnover.Days(1,value with{Fetched=now.AddHours(-3)},now,2)),"Stale turnover evidence held");
    Check(double.IsPositiveInfinity(SaleTurnover.Days(1,value with{HistoryTruncated=true},now,2)),"Truncated turnover evidence held");
    Check(!SaleTurnover.Improves(double.PositiveInfinity,3)&&!SaleTurnover.Improves(3,double.PositiveInfinity),"Unknown speed cannot justify a withdrawal");
    var storage=new RetainerStorageSettings{ExcludedCategories=[59]};
    Check(storage.Block(new ItemFacts(2,"Crystal",false,1,1,true,false,SourceKind.Ordinary,59),new Evidence{GearsetsReady=true},1,4)!=null,"Crystal category excluded before routing");
}
{
    var strategy=new SilverStrategy{Enabled=true,Retainer=777,MinimumPrice=44001};
    Check(strategy.Matches(777,13116)&&!strategy.Matches(778,13116)&&!strategy.Matches(777,42),"Strategy requires exact retainer and dye");
    var rows=new List<Stock>();uint consumed=0;
    while(strategy.NextQuantity(rows,300-consumed) is uint n&&n>0){rows.Add(new(13116,false,n,12002,(ushort)rows.Count));consumed+=n;}
    Check(consumed==65&&rows.Count==20&&rows.Count(s=>s.Quantity==5)==10&&rows.Count(s=>s.Quantity==2)==5&&rows.Count(s=>s.Quantity==1)==5,"Exact target mix uses 65 dyes and 20 slots");
    rows.RemoveAt(0);Check(strategy.NextQuantity(rows,300)==5,"Sold stack of five refilled as five");
    Check(strategy.NextQuantity(rows,4)==0,"Do not invent a smaller stack when only five-stack quota is missing");
    rows.Add(new(42,false,99,12002,0));Check(strategy.NextQuantity(rows,300)==0,"Other item occupies its slot and is not displaced");
    Offering Offer(uint price,uint qty=1,ulong owner=99,ulong id=1)=>new(13116,false,price,qty,owner,id);
    Check(strategy.Price([Offer(50000)],new HashSet<ulong>{777},true)?.Price==49995,"Five gil under competitor");
    Check(strategy.Price([Offer(2000,5)],new HashSet<ulong>{777},true) is {Price:45000,Reset:true,BuyoutCost:10000},"Below floor resets to 45k with accurate stack cost");
    var mixed=strategy.Price([Offer(2000,5),Offer(50000,1,99,2)],new HashSet<ulong>{777},true);
    Check(mixed is {Price:49995,Reset:false}&&mixed.Buyouts.Length==1,"Ignore below-floor competitor for pricing but retain buyout alert");
    Check(strategy.Price([Offer(44006)],new HashSet<ulong>{777},true)?.Price==44001,"Minimum price remains strictly above 44k");
    Check(strategy.Price([Offer(44005)],new HashSet<ulong>{777},true)?.Price==45000,"Never clip a five-gil undercut below floor");
    Check(strategy.Price([Offer(45000,5,777),Offer(50000,1,888,2)],new HashSet<ulong>{777,888},true) is {Price:45000,Buyouts.Length:0},"All own retainers excluded from competition and buyouts");
    Check(strategy.Price([],new HashSet<ulong>{777},true)?.Price==45000,"Confirmed no competition resets to 45k");
    Check(MarketComparison.IsConfirmedEmpty(13116,13116,17,17,false,0,0,now,now,now),"Explicit correlated empty response supports reset");
    Check(!MarketComparison.IsConfirmedEmpty(13116,13116,16,17,false,0,0,now,now,now),"Old empty request cannot support reset");
    Check(!MarketComparison.IsConfirmedEmpty(13116,13116,17,17,true,0,0,now,now,now),"Waiting is not empty competition");
    Check(!MarketComparison.IsConfirmedEmpty(13116,13116,17,17,false,0,1,now,now,now),"Incomplete listing batch is not empty competition");
    Check(!MarketComparison.IsConfirmedEmpty(13116,42,17,17,false,0,0,now,now,now),"Different item is not an empty silver comparison");
    Check(strategy.Price([],new HashSet<ulong>{777},false)==null,"Missing comparison never resets price");
    Check(strategy.Price([Offer(100,1,0)],new HashSet<ulong>{777},true)==null,"Unidentified seller holds strategy");
    Check(strategy.Price([Offer(100),Offer(100)],new HashSet<ulong>{777},true)==null,"Duplicate listing evidence rejected");
    Check(strategy.Price([Offer(50000) with{Hq=true}],new HashSet<ulong>{777},true)==null,"Impossible HQ dye evidence is held");
    var cfg=new Steward.Configuration{Silver=strategy,ExcludedSaleRetainers=[777],LogOnly=true};
    Check(cfg.SalesEnabledFor(777)&&!cfg.ExecutionEnabled,"Explicit strategy exception does not bypass log-only");
    cfg.Silver.Enabled=false;Check(!cfg.SalesEnabledFor(777),"Disabling strategy restores excluded retainer gate");
    Check(!new SilverStrategy{Enabled=true,Retainer=777,MinimumPrice=46000,ResetPrice=45000}.Valid,"Invalid price configuration rejected");
}
Console.WriteLine($"{total} checks passed including cancellation/error paths.");
SafetyChecks.Run(Check,now);
ExecutionChecks.Run(Check,now);
CommonGlamourChecks.Run(Check,now);
StorageChecks.Run(Check,now);
ReviewSafetyChecks.Run(Check,now);
ListingChecks.Run(Check,now);
DisposalEvidenceChecks.Run(Check,now);
NonEquipmentProtectionChecks.Run(Check,now);
await UniversalisChecks.Run(Check,now);
CategoryChecks.Run(Check,now);
await FeatureAuditChecks.Run(Check,now);
await AggregateRecoveryChecks.Run(Check,now);
PersistentObservationChecks.Run(Check,now);
await ImportAndLoggingChecks.Run(Check);
ListingWindowChecks.Run(Check);
SaleControlChecks.Run(Check);
PriceOutlierChecks.Run(Check);
FeatureToggleChecks.Run(Check);
ReferenceRefreshChecks.Run(Check);
NativePlateCacheChecks.Run(Check);
GlamourFilterChecks.Run(Check,now);
Console.WriteLine($"{total} checks passed including disposal/protection safety regressions.");
if(args.Length==3)
{
    var references=Imports.Glamaholic(File.ReadAllText(args[0]));
    var cache=Imports.MarketCache(File.ReadAllText(args[1]));
    _=Imports.MarketBuddy(File.ReadAllText(args[2]));
    Console.WriteLine($"Read-only local import validation: {references.Count} protected glamour IDs, {cache.Count} cached quotes; Market Buddy settings parsed.");
}

{
    var proof=new Evidence();
    var summary=new SaleStatistic(1,42,false,50000.9,0.1,now,now,null,true);
    proof.SaleStatistics[(42,false)]=summary;
    Check(MarketComparison.EmptyMarketAverage(proof,42,false,QualityMode.Combined,1,now,2)==50000,"Empty market uses whole-gil average");
    Check(MarketComparison.EmptyMarketAverage(proof,42,true,QualityMode.Separate,1,now,2)==null,"Empty-market average respects separate quality");
    Check(MarketComparison.EmptyMarketAverage(proof,42,false,QualityMode.Combined,2,now,2)==null,"Empty-market average respects world");
    Check(MarketComparison.EmptyMarketAverage(proof,42,false,QualityMode.Combined,1,now.AddHours(3),2)==null,"Expired aggregate cannot price empty market");
    var sale=new SaleRule{Name="Empty test",MinimumUnit=50000,MinimumTotal=50000,MaximumStack=1};
    var reference=new MarketReference(1,50000,null,now,"Confirmed empty",EmptyMarketAverage:true);
    var input=new ListingInput(item with{Marketable=true},stock,new(Outcome.Market,"Test"),sale,reference);
    Check(ListingPlanner.Next(input,new PricingSettings(),1,30,now).UnitPrice==50000,"No undercut below empty-market average");
    sale.MinimumTotal=50001;
    Check(!ListingPlanner.Next(input,new PricingSettings(),1,30,now).Eligible,"Empty average still enforces stack value");
    sale.MinimumTotal=0;sale.MinimumUnit=50001;
    Check(!ListingPlanner.Next(input,new PricingSettings(),1,30,now).Eligible,"Empty average still enforces unit floor");
}
Console.WriteLine($"{total} checks passed including confirmed-empty market pricing.");

{
    Check(DresserRestoration.Find([31556u,3093u,777u],3093,1)==1,"Restoration retains a valid live source");
    Check(DresserRestoration.Find([3093u,777u,0u],3093,1)==0,"Next restoration resolves the item after dresser compaction");
    Check(DresserRestoration.Find([3093u,3093u,777u],3093,2)==-2,"Moved duplicate sources are not guessed");
    Check(DresserRestoration.Find([777u],3093,1)==-1,"Missing source is not replaced with another item");
    Check(DresserRestoration.Find([1003093u],3093,0)==-1,"Raw source identity is preserved");
    Check(DresserRestoration.Acknowledge(2,1,0,1)==StepResult.Completed,"Restoration confirms source loss and destination gain even if another copy moves into the old slot");
    Check(DresserRestoration.Acknowledge(2,1,0,0)==StepResult.Waiting,"Wait for delayed player inventory update");
    Check(DresserRestoration.Acknowledge(2,2,0,1)==StepResult.Waiting,"Wait for delayed dresser update");
    Check(DresserRestoration.Acknowledge(2,0,0,1)==StepResult.Failed,"Unexpected dresser loss stops cleanup");
    Check(DresserRestoration.Acknowledge(2,1,0,2)==StepResult.Failed,"Unexpected player gain stops cleanup");
    Check(DresserRestoration.Acknowledge(0,0,0,1)==StepResult.Failed,"Unobserved source cannot acknowledge restoration");
}
Console.WriteLine($"{total} checks passed including dresser restoration refresh and acknowledgement.");

{
    var state=new DresserRestoreState(50000,34,0,0,2,0);
    var retry=new DresserRestoreRetry();
    Check(retry.Ready(1000)&&retry.Unchanged(state),"First restoration needs no retry delay");
    Check(retry.Rejected(state,1000),"First explicit native rejection can defer");
    Check(!retry.Ready(1499)&&retry.Ready(1500),"Native rejection waits half a second");
    foreach(var changed in new[]{state with{Raw=50001},state with{Slot=35},state with{First=1},state with{Second=4},state with{Copies=1},state with{Carried=1}})
        Check(!retry.Unchanged(changed),"Any source or quantity change prevents native retry");
    Check(!retry.Rejected(state with{Copies=1},1500),"Changed state cannot schedule another request");
    Check(retry.Rejected(state,1500),"Second unchanged rejection can defer");
    Check(!retry.Rejected(state,2000),"Third rejection stops instead of looping");
}
Console.WriteLine($"{total} checks passed including bounded rejected dresser submissions.");

{
    var observed=DateTimeOffset.UtcNow;
    var materialItem=new ItemFacts(600,"Material",false,1,0,true,false,SourceKind.Ordinary,48);
    var source=new Stock(600,false,30,0,0);
    var rules=new RuleSettings{Sales=[new SaleRule{Name="Materials",Categories=[48],MinimumUnit=1000,MinimumTotal=5000,MaximumStack=30,MinimumDailySales=1d/14}]};
    var proof=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true,StatisticsRequired=true};
    SaleStatistic value=new(1,600,false,500,1,observed,observed,500,true);
    proof.SaleStatistics[(600,false)]=value;
    Decision JudgeMaterial()=>Rules.Judge(materialItem,source,rules,proof,1,observed);
    Check(JudgeMaterial().Outcome==Outcome.Keep,"Default category permissions preserve materials rejected by sale rule");
    rules.NonEquipmentDisposalCategories.Add(48);
    Check(JudgeMaterial() is {Outcome:Outcome.Dispose,SaleRejected:true},"Explicit category permission allows below-floor material disposal");
    var copyEvidence=CollectionCopyEvidence.Capture(materialItem,rules,proof,true,false,observed);
    Check(copyEvidence.NonEquipmentDisposalAllowed&&copyEvidence.NonEquipmentDisposalPermission=="Category 48","Review evidence identifies category disposal permission");
    proof.SaleStatistics[(600,false)]=value with{Average=1000};
    Check(JudgeMaterial().Outcome==Outcome.Market,"Passing sale criteria wins over category disposal, independently of automation switches");
    proof.SaleStatistics[(600,false)]=value with{Average=1000,DailySales=0.01};
    Check(JudgeMaterial() is {Outcome:Outcome.Dispose,SaleRejected:true},"Known below-threshold demand can permit category disposal");
    proof.SaleStatistics.Clear();Check(JudgeMaterial().Outcome==Outcome.Wait,"Category permission never turns unknown prices into disposable value");
    proof.SaleStatistics[(600,false)]=value with{RecentSales=false,ValueObserved=observed.AddDays(-5)};
    Check(JudgeMaterial().Outcome==Outcome.Wait,"Category permission never bypasses missing recent sale evidence");
    proof.SaleStatistics[(600,false)]=value;
    rules.Keep.Add(600);Check(JudgeMaterial().Outcome==Outcome.Keep,"Keep override wins over category disposal");rules.Keep.Clear();
    rules.IgnoredCategories.Add(48);Check(JudgeMaterial().Outcome==Outcome.Ignore,"Global category exclusion wins over category disposal");rules.IgnoredCategories.Clear();
    proof.Gearsets.Add(600);Check(JudgeMaterial().Outcome==Outcome.Keep,"Gearset protection wins even for material-shaped metadata");proof.Gearsets.Clear();
    proof.ReferencesReady=false;Check(JudgeMaterial().Outcome==Outcome.Wait,"Missing protection evidence still holds material disposal");proof.ReferencesReady=true;
    rules.QuantityReserves[600]=5;proof.ReserveStocks.Add(source);Check(JudgeMaterial().Outcome==Outcome.Keep,"Whole-stack material disposal cannot consume a reserve");rules.QuantityReserves.Clear();
    Check(!rules.AllowsNonEquipmentDisposal(materialItem with{Category=49}),"Permission does not leak to other categories");
    Check(!rules.AllowsNonEquipmentDisposal(materialItem with{Equipment=true}),"Non-equipment permission does not change equipment classification");
    var persisted=System.Text.Json.JsonSerializer.Deserialize<RuleSettings>(System.Text.Json.JsonSerializer.Serialize(rules))!;
    Check(persisted.NonEquipmentDisposalCategories.SetEquals([48]),"Category permission survives a settings restart");
    Check(System.Text.Json.JsonSerializer.Deserialize<RuleSettings>("{}")!.NonEquipmentDisposalCategories.Count==0,"Existing configurations gain no disposal categories automatically");
}
Console.WriteLine($"{total} checks passed including category disposal permissions and storage authority.");

{
    var journal=false;var reports=new List<string>();var followed=false;
    var q=new OperationQueue(_=>journal=true,()=>journal=false,reports.Add);
    q.Add(new("Rejected item",()=>StepResult.Failed,CanSkip:()=>true));
    q.Add(new("Next item",()=>{followed=true;return StepResult.Completed;},CanSkip:()=>true));
    q.Advance(0);
    Check(q.Running&&q.Skipped==1&&!journal&&reports.Count==1,"Known unsubmitted rejection reports a skip and clears only its operation journal");
    q.Advance(1);Check(followed&&q.Completed==1&&q.Skipped==1&&!q.Running&&!journal,"Later items execute after a rejected item without disabling the run");
    var attempted=false;journal=false;followed=false;
    q=new(_=>journal=true,()=>journal=false,reports.Add);
    q.Add(new("Ambiguous native call",()=>{attempted=true;throw new IOException("IPC failed after entering native call");},CanSkip:()=>!attempted));
    q.Add(new("Must not submit",()=>{followed=true;return StepResult.Completed;}));q.Advance(0);
    Check(q.Status.StartsWith("Stopped:")&&journal&&!followed&&q.Skipped==0,"A possibly submitted action still requires review and never continues blindly");
    journal=false;q=new(_=>journal=true,()=>journal=false,reports.Add);
    q.Add(new("Unavailable before submission",()=>StepResult.Waiting,TimeoutMs:1,CanSkip:()=>true));q.Advance(0);q.Advance(2);
    Check(q.Skipped==1&&!journal&&!q.Status.StartsWith("Stopped:"),"Pre-submission timeout is a recoverable item skip");
    journal=false;q=new(_=>journal=true,()=>journal=false,reports.Add);
    q.Add(new("Submitted but unacknowledged",()=>StepResult.Waiting,TimeoutMs:1,CanSkip:()=>false));q.Advance(0);q.Advance(2);
    Check(q.Status.StartsWith("Stopped:")&&journal,"Unacknowledged submission timeout remains a durable hold");
    journal=false;q=new(_=>journal=true,()=>journal=false,reports.Add);
    q.Add(new("Cancelled before submission",()=>StepResult.Waiting,CanSkip:()=>true));q.Advance(0);q.Stop("Cancelled");
    Check(!journal&&!q.Running,"Safe cancellation cannot leave a stale journal that blocks the next login");
    journal=false;var pendingCleaned=false;var activeFinished=false;
    q=new(_=>journal=true,()=>journal=false,reports.Add);
    q.Add(new("Already submitted",()=>activeFinished?StepResult.Completed:StepResult.Waiting,CanSkip:()=>false));
    q.Add(new("Old planned item",()=>throw new Exception("Old decision was executed"),()=>pendingCleaned=true));q.Advance(0);q.ReplanPending();
    Check(q.Running&&journal&&pendingCleaned,"Settings edits retain acknowledgement of the active action and remove old queued decisions");
    activeFinished=true;q.Advance(1);Check(!journal&&!q.Running&&q.Status=="Completed","Active acknowledgement finishes normally after a settings edit");
    journal=false;q=new(_=>journal=true,()=>throw new IOException("Cannot persist acknowledgement"),reports.Add);
    q.Add(new("Rejected item with failed save",()=>StepResult.Failed,CanSkip:()=>true));q.Advance(0);
    Check(q.Status.StartsWith("Stopped:")&&journal,"A failed journal save cannot silently turn a skip into permission to continue");
}
Console.WriteLine($"{total} checks passed including recoverable skips, live control modes and durable uncertainty.");
Check(!AutoDutyTravel.Owned(_=>null),"Absent AutoDuty permits ordinary storage travel");
foreach(var looping in new[]{false,true})
foreach(var navigating in new[]{false,true})
foreach(var stopped in new[]{false,true})
    Check(AutoDutyTravel.Owned(name=>name switch
    {"AutoDuty.IsLooping"=>looping,"AutoDuty.IsNavigating"=>navigating,_=>stopped})==(looping||navigating||!stopped),
        "AutoDuty owns travel during loops, navigation and active stages");
Check(AutoDutyTravel.Owned(_=>throw new InvalidOperationException()),"Unavailable AutoDuty state holds competing travel");
Console.WriteLine("10 AutoDuty travel coordination checks passed.");
CachedRepricingChecks.Run(Check);
Console.WriteLine("Allagan cached repricing checks passed.");

LowPriorityChecks.Run(Check);
Console.WriteLine("Lower-priority sale tier checks passed.");
NativeComparisonChecks.Run(Check);
Console.WriteLine("Native market comparison checks passed.");

var pacing=new MarketSearchPacing();
Check(pacing.Ready(now),"First market search needs no delay");
pacing.Sent(now);
Check(!pacing.Ready(now.AddSeconds(2.9))&&pacing.Ready(now.AddSeconds(3)),"Requests are spaced by at least three seconds");
pacing.Observe(new(1,42,10,0,now.AddSeconds(1)));
Check(!pacing.Ready(now.AddSeconds(3.9))&&pacing.Ready(now.AddSeconds(4)),"Completed response also starts a three-second gap");
pacing.Observe(new(2,43,0,MarketSearchPacing.RateLimited,now.AddSeconds(4)));
Check(!pacing.Ready(now.AddSeconds(8.9))&&pacing.Ready(now.AddSeconds(9)),"Rate limit starts five-second backoff");
pacing.Observe(new(1,42,10,0,now.AddSeconds(1)));
Check(pacing.NextSearch==now.AddSeconds(9),"Old response cannot shorten cooldown");
pacing.Observe(new(2,43,0,MarketSearchPacing.RateLimited,now.AddSeconds(20)));
Check(pacing.NextSearch==now.AddSeconds(9),"Same receipt does not perpetually extend backoff");
Console.WriteLine("Market search pacing checks passed.");

var updatedSilver=new SilverStrategy{Retainer=33777197238509254,MinimumPrice=44001};
updatedSilver.ApplyRequestedFloorUpdate();
Check(updatedSilver.MinimumPrice==40001&&updatedSilver.ResetPrice==45000&&updatedSilver.Undercut==5,"Requested floor update preserves reset and undercut");
updatedSilver.MinimumPrice=43000;updatedSilver.ApplyRequestedFloorUpdate();
Check(updatedSilver.MinimumPrice==43000,"Floor migration never overwrites subsequent user edits");
var otherSilver=new SilverStrategy{Retainer=777,MinimumPrice=44001};otherSilver.ApplyRequestedFloorUpdate();
Check(otherSilver.MinimumPrice==44001,"Other retainer configuration unchanged");
Check(new SilverStrategy().MinimumPrice==40001,"New strategy uses updated floor");

Check(MarketReturnReceipt.Arrived(0,8,8),"Returned stack arrives in empty retainer stock");
Check(MarketReturnReceipt.Arrived(1,31,30),"Return merges with existing retainer stock");
Check(!MarketReturnReceipt.Arrived(1,1,30),"Native acceptance with delayed inventory is not acknowledgement");
Check(!MarketReturnReceipt.Arrived(1,30,30),"Partial arrival is not acknowledged");
Check(!MarketReturnReceipt.Arrived(5,4,1),"Quantity decrease never acknowledges arrival");
Check(!MarketReturnReceipt.Arrived(0,8,0),"Zero quantity cannot acknowledge a return");
Check(!MarketReturnReceipt.Arrived(ulong.MaxValue,0,1),"Overflow cannot fabricate receipt");
