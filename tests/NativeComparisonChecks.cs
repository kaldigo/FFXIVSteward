using Steward.Core;

static class NativeComparisonChecks
{
    public static void Run(Action<bool,string> check)
    {
        var start=DateTimeOffset.UtcNow;
        Offering[] rows=[new(42,false,1000,5,10,100),new(42,true,1500,1,20,200)];
        var response=new MarketComparison.SearchResponse(8,42,2,0,start.AddMilliseconds(200));
        string? Read(MarketComparison.SearchResponse? receipt=null,uint item=42,uint count=2,
            Offering[]? native=null,Offering[]? packets=null,double seconds=1)=>
            MarketComparison.ResponseRejection(42,7,receipt??response,item,count,native??rows,packets??rows,start,start.AddSeconds(seconds));
        check(Read()==null,"Successful response plus all matching native/network rows authorizes comparison");
        check(Read(response with{Sequence=7})!=null,"Previous response cannot authorize a new comparison");
        check(MarketComparison.ResponseRejection(42,7,null,42,0,[],[],start,start.AddSeconds(1))!=null,"No server response never means empty market");
        check(Read(response with{Status=unchecked((int)0x70000003)})!=null,"Rate-limited search cannot authorize pricing");
        check(Read(response with{Item=43})!=null&&Read(item:43)!=null,"Response and native search must both identify requested item");
        check(Read(response with{At=start.AddSeconds(-1)})!=null,"Delayed response from before search is rejected");
        check(Read(response with{At=start.AddSeconds(2)})!=null,"Future response is rejected");
        check(Read(seconds:11)!=null,"Expired search is rejected");
        check(Read(count:1,native:[rows[0]],packets:[rows[0]])!=null,"Matching partial native and network pages still wait for advertised count");
        check(Read(packets:[rows[0]])!=null,"Missing network page holds complete-looking native rows");
        check(Read(response with{Expected=101})!=null,"Response beyond native capacity is held");
        check(Read(response with{Expected=0},count:0,native:[],packets:[])==null,"Explicit successful fresh zero count permits empty-market policy");
        check(Read(response with{Expected=0,Status=1},count:0,native:[],packets:[])!=null,"Error with zero count is not an empty market");
        foreach(var bad in new[]{rows[0] with{ItemId=43},rows[0] with{ListingId=0},rows[0] with{RetainerId=0},
            rows[0] with{Quantity=0},rows[0] with{UnitPrice=0},rows[0] with{UnitPrice=1000000000}})
            check(Read(native:[bad,rows[1]],packets:[bad,rows[1]])!=null,"Malformed matching native/network row is held");
        check(Read(native:[rows[0],rows[0]],packets:[rows[0],rows[0]])!=null,"Duplicate listing identities cannot fake completion");
        check(Read(packets:[rows[0] with{UnitPrice=900},rows[1]])!=null,"Stale native price disagrees with fresh network price and is held");
        check(Read(packets:[rows[1],rows[0]])==null,"UI sorting does not invalidate complete matching identities");
        check(Pricing.Target(42,false,null,[rows[0] with{UnitPrice=3000},rows[1]],new HashSet<ulong>{10},QualityMode.Combined,new(){UndercutPrice=5},1)==1495,
            "Validated rows preserve own-retainer exclusion and combined-quality pricing");
    }
}
