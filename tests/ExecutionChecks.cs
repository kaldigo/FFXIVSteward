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
        check(!Comparison(received:21),"Extra or mixed market pages cannot acknowledge a complete request");
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
        var events=new List<string>();string? pending=null;
        OperationQueue JournalQueue(bool failBegin=false,bool failEnd=false)=>new(
            name=>ExecutionJournal.Begin(name,value=>pending=value,()=>{events.Add("saved-start");if(failBegin)throw new IOException("start disk");}),
            ()=>ExecutionJournal.Complete(pending!,value=>pending=value,()=>{events.Add("saved-end");if(failEnd)throw new IOException("end disk");}));
        var durable=JournalQueue();var attempts=0;
        durable.Add(new("Durable item",()=>{events.Add("native");return ++attempts==1?StepResult.Waiting:StepResult.Completed;},()=>events.Add("cleanup")));
        durable.Advance(1);check(pending=="Durable item","Submission retains a durable marker while awaiting acknowledgement");
        durable.Advance(2);check(pending==null&&events.SequenceEqual(new[]{"saved-start","native","native","cleanup","saved-end"}),
            "Journal is saved before native code, once only, and cleared after acknowledgement and cleanup");
        events.Clear();pending=null;durable=JournalQueue(failBegin:true);
        durable.Add(new("Must not submit",()=>{events.Add("native");return StepResult.Completed;}));durable.Advance(3);
        check(pending!=null&&!events.Contains("native")&&durable.Status.StartsWith("Stopped:"),"Failed journal write prevents any native tick");
        events.Clear();pending=null;durable=JournalQueue(failEnd:true);
        durable.Add(new("Acknowledged",()=>StepResult.Completed));durable.Add(new("Dependent",()=>{events.Add("native");return StepResult.Completed;}));
        durable.Advance(4);durable.Advance(5);
        check(pending=="Acknowledged"&&!events.Contains("native")&&!durable.Running,"Failed acknowledgement save restores the marker and cancels dependent work");
        events.Clear();pending=null;durable=JournalQueue();
        durable.Add(new("Cleanup fails",()=>StepResult.Completed,()=>throw new IOException("cleanup")));durable.Advance(6);
        check(pending!=null&&!events.Contains("saved-end"),"Failed cleanup cannot clear the recovery journal");
        events.Clear();pending=null;durable=JournalQueue();
        durable.Add(new("Cancelled",()=>{durable.Stop("Stopped: reentrant fault");return StepResult.Completed;}));durable.Advance(7);
        check(pending=="Cancelled"&&!events.Contains("saved-end")&&durable.Completed==0,"Cancellation inside a native tick cannot acknowledge the cancelled operation");
        events.Clear();pending=null;durable=JournalQueue();
        durable.Add(new("Cancelled cleanup",()=>StepResult.Completed,()=>durable.Stop("Stopped: cleanup cancelled")));durable.Advance(8);
        check(pending=="Cancelled cleanup"&&!events.Contains("saved-end")&&durable.Status.StartsWith("Stopped:")&&durable.Completed==0,
            "Cancellation during cleanup preserves journal and stopped status");
        OperationQueue? starting=null;var startedTicks=0;
        starting=new(_=>starting!.Stop("Stopped: startup cancellation"),()=>throw new Exception("Must not acknowledge"));
        starting.Add(new("Cancelled startup",()=>{startedTicks++;return StepResult.Completed;}));starting.Advance(9);
        check(startedTicks==0&&starting.Status=="Stopped: startup cancellation","Cancellation inside the pre-start callback cannot invoke a native tick");
        automatic=true;var review=false;
        try{ExecutionMode.Persist(()=>throw new IOException("rule save"),()=>{automatic=false;review=true;});}
        catch(IOException){}
        check(!automatic&&review,"Any failed rule/settings save stops execution and requires review");
        for(uint quantity=1;quantity<=5;quantity++)
        for(uint removed=1;removed<=quantity;removed++)
        {
            var expected=stock with{Quantity=quantity};
            Stock? remainder=removed==quantity?null:expected with{Quantity=quantity-removed};
            check(StockTransition.Removed(expected,remainder,removed,true),"Exact source decrement accepted");
            check(!StockTransition.Removed(expected,remainder,removed,false),"Unloaded source cannot acknowledge removal");
            check(!StockTransition.Removed(expected,expected,removed,true),"An unchanged source cannot acknowledge removal");
            check(!StockTransition.Removed(expected,expected with{Hq=!expected.Hq,Quantity=quantity-removed},removed,true),"Changed quality cannot acknowledge removal");
        }
        check(!StockTransition.Removed(stock,null,0,true),"Zero removal rejected");
        check(!StockTransition.Removed(stock,null,stock.Quantity+1,true),"Excess removal rejected");
        var folder=Path.Combine(Path.GetTempPath(),"steward-safety-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"state.json");var migrated=0;
        try
        {
            check(AtomicState.ReadOrMigrate(path,()=>{migrated++;return "legacy";})=="legacy"&&migrated==1,"Only a missing private state uses legacy configuration");
            AtomicState.Write(path,"pending");
            check(AtomicState.ReadOrMigrate(path,()=>"unsafe")=="pending","Real disk reload retains a pending submission");
            AtomicState.Write(path,"reviewed");
            check(AtomicState.ReadOrMigrate(path,()=>"unsafe")=="reviewed","Atomic replacement reloads the latest state");
            File.WriteAllText(path,"{");
            try{AtomicState.ReadOrMigrate(path,()=>{migrated++;return "unsafe";});throw new Exception("Corrupt state accepted");}
            catch(System.Text.Json.JsonException){check(migrated==1,"Corrupt private state cannot silently load defaults");}
            var blocked=Path.Combine(folder,"blocked");Directory.CreateDirectory(blocked);
            try{AtomicState.Write(blocked,"pending");throw new Exception("Write failure swallowed");}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){check(true,"Actual filesystem write/replace failure propagates to the action guard");}
            check(Directory.GetFiles(folder,"*.tmp").Length==0,"Failed replacements clean up only their own temporary file");
        }
        finally{foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(Path.Combine(folder,"blocked"));Directory.Delete(folder);}
    }
}
