using System.Text.Json;
using System.Text.Json.Nodes;
using Steward;
using Steward.Core;
static class ListingWindowChecks
{
    public static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var old=new Stock(42,false,30,12002,0);var next=new Stock(43,false,5,0,1);
        var memory=new ListingWindows();var start=now.AddDays(-61);
        check(memory.Observe(1,83,2,[old],[next],start),"First observation starts listing and waiting clocks");
        check(!memory.Observe(1,83,2,[old],[next],now),"Unchanged listing/repricing never resets the clock or writes repeatedly");
        check(ListingWindows.Expired(memory.Age(1,83,2,old),60,now),"61 tracked days expires a 60-day window");
        check(!ListingWindows.Expired(null,60,now)&&!ListingWindows.Expired(memory.Age(1,83,2,old),null,now),"Unknown age and disabled window never rotate");
        check(!ListingWindows.Expired(memory.Age(1,83,2,old),60,start.AddDays(59.9)),"Listing window boundary enforced");
        check(memory.Age(1,84,2,old)==null&&memory.Age(2,83,2,old)==null,"Age is isolated by world and character");
        memory.ReserveReturn("return1",1,83,2,old,now,()=>{});
        check(memory.Priority(1,83,2,next)<memory.Priority(1,83,2,old),"Returned listing goes behind items already waiting");
        memory.ReserveReturn("return1",1,83,2,old,now,()=>{});
        check(memory.Holds.Count==1&&memory.Holds[0].Quantity==30,"Retrying a return does not duplicate reserved quantity");
        var config=new Configuration{SafetyRevision=7,PendingCollection=[],ListingWindows=memory};
        var json=JsonSerializer.Serialize(config);var restored=JsonSerializer.Deserialize<Configuration>(json)!;
        check(restored.ListingWindows.Held(1,42)&&restored.ListingWindows.Age(1,83,2,old)?.Since==start,"Return protection and listing age survive restart");
        var missing=JsonNode.Parse(json)!;missing.AsObject().Remove("ListingWindows");
        try{JsonSerializer.Deserialize<Configuration>(missing.ToJsonString());check(false,"Missing rotation safety state accepted");}catch(JsonException){check(true,"Missing rotation safety state fails closed");}
        memory.Posted(1,42,true,30,()=>{});check(memory.Holds[0].Quantity==30,"Other quality cannot release a return reserve");
        memory.Posted(1,42,false,10,()=>{});check(memory.Holds[0].Quantity==20,"Confirmed split posting releases only its quantity");
        try{memory.Posted(1,42,false,20,()=>throw new IOException("test"));}catch(IOException){}
        check(memory.Holds[0].Quantity==20,"Failed reserve release persistence rolls back in memory");
        memory.Posted(1,42,false,20,()=>{});check(!memory.Held(1,42),"Final confirmed posting releases the reserve");
        try{memory.ReserveReturn("failed",1,83,2,old,now,()=>throw new IOException("test"));}catch(IOException){}
        check(memory.Held(1,42),"Failed reserve persistence retains in-memory protection and throws before submission");
        memory.Observe(1,83,2,[old with{Quantity=29}],[next],now);
        check(memory.Age(1,83,2,old)==null&&!ListingWindows.Expired(memory.Age(1,83,2,old with{Quantity=29}),60,now),"Changed quantity invalidates the previous age");
        memory.Observe(1,83,2,[],[next],now);check(memory.Listings.Count==0,"Complete empty list clears old ages");
        var item=new ItemFacts(42,"Test",true,1,50,false,false,SourceKind.Ordinary,35);
        var evidence=new Evidence{DresserKnown=true,ArmoireKnown=true,ReferencesReady=true,GearsetsReady=true,RelistingHolds=[42]};
        var rules=new RuleSettings();
        check(Rules.Judge(item,old,rules,evidence,83,now).Outcome==Outcome.Dispose,"Legacy return flags do not override normal disposal rules");
        item=item with{Marketable=true};rules.Sales.Add(new(){MinimumUnit=1,MinimumTotal=1,MaximumStack=30,MinimumDailySales=.1});
        evidence.SaleStatistics[(42,false)]=new(83,42,false,1000,.1,now,now,1000,true);
        check(Rules.Judge(item,old,rules,evidence,83,now).Outcome==Outcome.Market,"A 0.1 average daily threshold accepts one unit per ten days, including reserved returns");
        evidence.SaleStatistics[(42,false)]=evidence.SaleStatistics[(42,false)] with{DailySales=.099};
        check(Rules.Judge(item,old,rules,evidence,83,now) is {Outcome:Outcome.Dispose,SaleRejected:true},"Below-threshold returned equipment follows normal disposal rules");
        evidence.SaleStatistics.Clear();evidence.Prices[(42,false)]=new(42,false,83,1000,now,"Game");
        check(Rules.Judge(item,old,rules,evidence,83,now).Outcome==Outcome.Wait,"Native listing quotes cannot bypass an unknown sales-rate threshold");
    }
}
