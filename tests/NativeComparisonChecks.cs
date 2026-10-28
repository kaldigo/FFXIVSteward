using Steward.Core;

static class NativeComparisonChecks
{
    public static void Run(Action<bool,string> check)
    {
        var start=DateTimeOffset.UtcNow;
        Offering[] rows=[new(42,false,1000,5,10,100),new(42,true,1500,1,20,200)];
        string? Read(int previous=7,int request=8,bool waiting=false,uint item=42,uint count=2,
            Offering[]? data=null,double seconds=1)=>MarketComparison.NativeRejection(42,previous,item,request,waiting,count,data??rows,start,start.AddSeconds(seconds));
        check(Read()==null,"Fresh completed native search includes NQ and HQ without packet request equality");
        check(Read(request:7)!=null,"Old native search cannot authorize listing even if all rows look valid");
        check(Read(waiting:true)!=null,"Partially loaded native search cannot authorize listing");
        check(Read(item:43)!=null,"Another item search cannot authorize listing");
        check(Read(count:3)!=null,"Incomplete native result count is rejected");
        check(Read(count:101)!=null,"Native count outside fixed result capacity is rejected");
        check(Read(previous:255,request:0)==null,"Native byte request wrap still identifies a new search");
        check(Read(previous:-1)!=null,"Uncaptured baseline cannot authorize listing");
        check(Read(seconds:11)!=null&&Read(seconds:-1)!=null,"Expired and future search times are rejected");
        check(Read(count:0,data:[])==null,"Fresh completed zero-result search may use empty-market policy");
        check(Read(request:7,count:0,data:[])!=null&&Read(waiting:true,count:0,data:[])!=null,"Old or pending empty state never authorizes a reset price");
        foreach(var bad in new[]{rows[0] with{ItemId=43},rows[0] with{ListingId=0},rows[0] with{RetainerId=0},
            rows[0] with{Quantity=0},rows[0] with{UnitPrice=0},rows[0] with{UnitPrice=1000000000}})
            check(Read(data:[bad,rows[1]])!=null,"Malformed native row is held");
        check(Read(data:[rows[0],rows[0]])!=null,"Duplicate listing identities cannot fake completion");
        check(Pricing.Target(42,false,null,[rows[0] with{UnitPrice=3000},rows[1]],new HashSet<ulong>{10},QualityMode.Combined,new(){UndercutPrice=5},1)==1495,
            "Native rows retain own-retainer exclusion and combined-quality pricing");
    }
}
