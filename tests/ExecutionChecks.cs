using Steward.Core;

static class ExecutionChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var stock=new Stock(42,false,5,0,0);
        var rule=new SaleRule{MinimumUnit=1000,MinimumTotal=5000,MaximumStack=5};
        var decision=new Decision(Outcome.Market,"Sale",rule);
        bool Commit(Stock? live,Decision d,uint price=1000,uint quantity=5,int actualPrice=1000,int actualQuantity=5,int maximum=5,bool reprice=false) =>
            MarketSubmission.CanCommit(stock,live,d,reprice,price,quantity,actualPrice,actualQuantity,maximum);
        check(Commit(stock,decision),"Exact final dialog, source and sale thresholds permit submission");
        check(!Commit(stock,decision,actualPrice:999),"Failed native price update blocks submission");
        check(!Commit(stock,decision,actualQuantity:4),"Failed native quantity update blocks submission");
        check(!Commit(stock with{ItemId=43},decision),"Source replaced before confirmation blocks submission");
        check(!Commit(stock with{Hq=true},decision),"Quality change before confirmation blocks submission");
        check(!Commit(stock with{Quantity=4},decision),"Source quantity change blocks submission");
        check(!Commit(null,decision),"Unloaded source blocks submission");
        check(!Commit(stock,new(Outcome.Keep,"Newly protected")),"New keep decision blocks final confirmation");
        check(!Commit(stock,new(Outcome.Ignore,"New exclusion")),"New exclusion blocks final confirmation");
        check(!Commit(stock,new(Outcome.CollectDresser,"Missing copy")),"Lost collection copy blocks final confirmation");
        check(!Commit(stock,new(Outcome.Wait,"Data unavailable")),"Unavailable protection blocks final confirmation");
        rule.MinimumUnit=1001;check(!Commit(stock,decision),"New minimum unit price is revalidated after UI updates");rule.MinimumUnit=1000;
        rule.MinimumTotal=5001;check(!Commit(stock,decision),"New minimum total is revalidated after UI updates");rule.MinimumTotal=5000;
        check(!Commit(stock,decision,maximum:4),"Reduced maximum stack is revalidated after UI updates");
        check(!Commit(stock,decision,price:0,actualPrice:0),"Zero listing price blocked");
        check(!Commit(stock,decision,price:1000000000,actualPrice:1000000000),"Out-of-range listing price blocked");
        check(!Commit(stock,decision,quantity:6,actualQuantity:6),"Cannot list more than the source owns");
        check(Commit(stock,new(Outcome.Dispose,"Existing listing"),reprice:true),"Permitted existing listing can reprice");
        check(!Commit(stock,new(Outcome.Dispose,"Existing listing"),quantity:4,actualQuantity:4,reprice:true),"Repricing cannot silently reduce the listed stack");

        bool Comparison(uint item=42,uint native=42,int request=7,int nativeRequest=7,bool waiting=false,int received=20,uint expected=20,
            DateTimeOffset? observed=null,DateTimeOffset? opened=null) => MarketComparison.IsCurrent(item,native,request,nativeRequest,waiting,received,expected,observed??now,opened??now.AddSeconds(-1),now);
        check(Comparison(),"Complete current market response accepted");
        check(!Comparison(native:43),"Another item's market response rejected");
        check(!Comparison(nativeRequest:8),"Another market request rejected");
        check(!Comparison(waiting:true),"Still-loading market pages rejected");
        check(!Comparison(received:10),"Missing market pages rejected");
        check(!Comparison(expected:0,received:0),"Empty market result is not a price");
        check(!Comparison(observed:now.AddSeconds(-2)),"Packet older than the comparison rejected");
        check(!Comparison(observed:now.AddSeconds(1)),"Future market timestamp rejected");
        check(!Comparison(observed:now.AddSeconds(-11),opened:now.AddSeconds(-12)),"Expired comparison rejected");
        check(!ConfirmationOwnership.Matches(0,0,0),"Default dialog identifiers cannot establish ownership");
        check(ConfirmationOwnership.Matches(12,12,0),"Matching parent establishes dialog ownership");
        check(ConfirmationOwnership.Matches(12,0,12),"Matching host establishes dialog ownership");
        check(!ConfirmationOwnership.Matches(12,13,14),"Unrelated confirmation cannot be acknowledged");
        check(!ActionPolicy.Allows("Vendor",Outcome.Dispose,true,true,false,true,true),"Owned handoff blocks competing vendor callbacks even after a backend reload");
        check(!ActionPolicy.Allows("Gc",Outcome.Dispose,true,true,false,true,false),"Active standalone queue blocks competing disposal callbacks");
        check(ActionPolicy.Allows("Retrieve",Outcome.CollectDresser,true,true,false,true,true),"Owned postprocess may withdraw its protected collection item");
        check(!ActionPolicy.Allows("Retrieve",Outcome.Dispose,true,true,false,true,false),"Standalone queue cannot authorize a foreign retrieval");
        var proof=new Evidence{GearsetsReady=true,ReferencesReady=true};
        proof.Prices[(42,false)]=new(42,false,0,1000,now,"Unknown world");
        check(Rules.Judge(new(42,"Test",false,1,90,true,false,SourceKind.Ordinary,1),stock,new(),proof,0,now).Outcome==Outcome.Wait,
            "Unknown current world cannot be valued by a zero-world cache row");

        var queue=new OperationQueue();var sent=0;var released=0;
        check(!queue.Plan(()=>{queue.Add(new("Partial plan",()=>{sent++;return StepResult.Completed;},()=>released++));throw new Exception("Import unavailable");}),
            "Planning failure is reported");
        queue.Advance(1);
        check(sent==0&&released==1&&!queue.Running&&queue.Status.StartsWith("Stopped:"),"Partially planned work never executes and releases resources");
        check(!queue.Plan(()=>sent++),"Stopped plan cannot silently clear its own hold");
        queue.Begin();check(queue.Plan(()=>queue.Add(new("Reviewed plan",()=>{sent++;return StepResult.Completed;}))),"Explicit cleared hold permits a new plan");
        check(!queue.Plan(()=>sent++),"A running queue cannot be replaced by another plan");queue.Advance(2);
        check(sent==1,"Only the reviewed plan executed");

        try{Imports.MarketCache("header\n42,X,1,0,2026-10-25,1000,N\n");throw new Exception("Bad quality accepted");}
        catch(InvalidDataException){check(true,"Malformed quality flags rejected");}
        try{Imports.MarketCache("header\n42,N,1,0,2026-10-25,1000,X\n");throw new Exception("Bad owner accepted");}
        catch(InvalidDataException){check(true,"Unknown own-price flags cannot become competitor prices");}
        var automatic=false;var writes=0;
        check(!ExecutionMode.Enable(false,value=>automatic=value,()=>writes++)&&!automatic&&writes==0,
            "An unresolved hold or unavailable connection cannot enable automatic execution");
        try{ExecutionMode.Enable(true,value=>automatic=value,()=>throw new IOException("disk"));throw new Exception("Enable save failure swallowed");}
        catch(IOException){check(!automatic,"Failed settings save leaves automatic execution off");}
        check(ExecutionMode.Enable(true,value=>automatic=value,()=>writes++)&&automatic&&writes==1,
            "Automatic execution requires a successful settings save");
        foreach(var outcome in Enum.GetValues<Outcome>())
        {
            var original=new Decision(outcome,"Original");
            check(UpstreamProtection.Apply(original,true).Outcome==(outcome==Outcome.Ignore?Outcome.Ignore:Outcome.Keep),
                "Explicit AutoRetainer keep overrides market, storage and disposal: "+outcome);
            check(UpstreamProtection.Apply(original,null).Outcome==(outcome==Outcome.Ignore?Outcome.Ignore:Outcome.Wait),
                "Unavailable AutoRetainer protection holds every active path: "+outcome);
            check(UpstreamProtection.Apply(original,false)==original,"Unprotected AutoRetainer item retains Steward decision: "+outcome);
        }
    }
}
