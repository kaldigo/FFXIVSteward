using Steward.Core;

var total=0;
void Check(bool condition,string name) { total++; if(!condition) throw new Exception(name); }
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
var now=DateTimeOffset.UtcNow;
var settings=new RuleSettings();
var e=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
var item=new ItemFacts(42,"Test",true,3,90,false,true,SourceKind.Special,1);
var stock=new Stock(42,false,1,0,0);
Decision Judge() => Rules.Judge(item,stock,settings,e,1,now);
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
item=item with{Equipment=false,Marketable=true};
Check(Judge().Outcome==Outcome.Wait,"Missing price blocks disposal");
e.Prices[(42,false)]=new(42,false,1,1000,now,"Test");settings.Sales.Add(new(){MinimumUnit=1000,MinimumTotal=5000,MaximumStack=5});
stock=stock with{Quantity=5};Check(Judge().Outcome==Outcome.Market,"Sale criteria match");
stock=stock with{Quantity=4};Check(Judge().Outcome==Outcome.Keep,"Below-floor non-equipment stays protected by default");
settings.NonEquipmentDisposal.Add(42);Check(Judge().Outcome==Outcome.Dispose,"Explicit non-equipment disposal still respects actual sale thresholds");
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
    var storage=new RetainerStorageSettings{Enabled=true,NonmarketableRetainer=2,ExcludedRetainers=[3],ExcludedCategories=[44,46,55],ExcludedItems=[21800],Categories=new(){[1]=[48],[2]=[]}};
    var proof=new Evidence{GearsetsReady=true};
    var material=new ItemFacts(100,"Stone",false,1,0,true,false,SourceKind.Ordinary,48);
    Check(storage.Block(material,proof,1,4)==null,"Assigned marketable material can be stored");
    Check(storage.Block(material,proof,2,4)!=null,"Wrong category destination denied");
    Check(storage.Block(material with{Marketable=false},proof,2,4)==null,"Nonmarketable override uses currency retainer");
    Check(storage.Block(material with{Marketable=false},proof,1,4)!=null,"Nonmarketable cannot leak to ordinary category destination");
    Check(storage.Block(material,proof,3,4)!=null,"Manual retainer denied");
    foreach(var cat in new uint[]{44,46,55,86})Check(storage.Block(material with{Category=cat,Marketable=false},proof,2,1)!=null,"Consumption/card exclusion beats nonmarketable routing");
    Check(storage.Block(material with{Id=21800,Marketable=false},proof,2,4)!=null,"Prisms excluded");
    proof.Gearsets.Add(100);Check(storage.Block(material,proof,1,4)!=null,"Gearsets excluded even at correct destination");proof.Gearsets.Clear();
    proof.GearsetsReady=false;Check(storage.Block(material,proof,1,4)!=null,"Unknown gearsets hold storage");proof.GearsetsReady=true;
    foreach(long? status in new long?[]{null,0,2,3})Check(storage.Block(material,proof,1,status)!=null,"Unknown/unlearned unlock held");
    Check(storage.Block(material,proof,1,1)==null,"Learned duplicate eligible");
    Check(storage.Block(material with{Category=100},proof,2,4)==null,"Currency category overrides marketability");
    var writes=new List<string>();var journal=new ReviewJournal((_,lines)=>writes.AddRange(lines));
    journal.Snapshot(Path.Combine(Path.GetTempPath(),"steward-review-mode","log.jsonl"),[new ReviewEntry(1,"test",100,1,false,"WouldDispose","Disabled")],"DisposalReview");
    Check(writes.Count==1&&writes[0].Contains("DisposalReview"),"Disposal-only review has truthful mode label");
    var cfg=new Steward.Configuration{LogOnly=true,Automatic=true};
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
    var strategy=new SilverStrategy{Enabled=true,Retainer=777};
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
    var cfg=new Steward.Configuration{Silver=strategy,ExcludedSaleRetainers=[777],LogOnly=true,Automatic=true};
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
