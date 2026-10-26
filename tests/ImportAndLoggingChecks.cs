using System.Text.Json;
using Steward.Core;

static class ImportAndLoggingChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        ulong[] Row(int bag,int slot,uint id=42,ulong owner=101)
        {var r=new ulong[25];r[0]=(ulong)(10000+bag);r[1]=(ulong)slot;r[2]=id;r[3]=1;r[6]=1;r[23]=owner;return r;}
        var rows=Enumerable.Range(0,175).Select(i=>Row(i/35,i%35,(uint)i+1)).ToArray();
        var imported=AllaganRetainerPreview.Parse(101,rows);
        check(imported.Count==175&&imported.Max(s=>s.Slot)==34,"Allagan five 35-slot bags preserve every item including upper slots");
        check(imported.All(s=>AllaganRetainerPreview.IsPreview(s.Container)&&s.Hq),"Imported sorted positions and HQ are preserved as non-native preview addresses");
        using(var memory=new InventoryMemory())
        {
            memory.ObserveRetainer(100,101,imported);
            check(memory.Stocks(100).Count()==175,"Allagan observations seed persistent inventory without visiting retainers");
            memory.ObserveRetainer(100,101,[new(777,false,1,10000,0)]);
            check(memory.Stocks(100).Single().Stock.ItemId==777,"Live native read replaces imported sorted snapshot");
        }
        foreach(var bad in new[]{new[]{Row(0,35)},new[]{Row(5,0)},new[]{Row(0,0),Row(0,0)},new[]{Row(0,0,42,102)}})
        {var threw=false;try{AllaganRetainerPreview.Parse(101,bad);}catch(InvalidDataException){threw=true;}check(threw,"Invalid layout/duplicate/wrong-owner import rejected");}
        var e=new Evidence{ReserveStocks=[new(42,true,1,10000,0)]};
        check(QuantityReserve.Removable(imported[41],1,e.ReserveStocks)==null,"Sorted preview address cannot impersonate a live quantity-reserve slot");
        var dir=Path.Combine(Path.GetTempPath(),"Steward-background-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var path=Path.Combine(dir,"review.jsonl");var history=Path.Combine(dir,"history.jsonl");
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            var entry=new ReviewEntry(11,"Retainer preview",42,1,false,"WouldDispose","Test");
            var entries=Enumerable.Range(0,2000).Select(i=>entry with{Location="Preview "+i}).ToArray();
            var writes=0;
            var journal=new BackgroundReviewJournal(history,(p,lines)=>{Interlocked.Increment(ref writes);entered.Set();release.Wait();File.AppendAllLines(p,lines);});
            try
            {
                check(journal.Snapshot(path,entries),"Full review accepted without synchronous disk writes");
                check(entered.Wait(5000),"Background writer begins independently");
                check(journal.ReviewPending&&!journal.Snapshot(path,[entry]),"Slow disk cannot queue unlimited snapshots");
                journal.Reset();journal.Record("Manual message",DateTimeOffset.UtcNow);
                release.Set();await Task.Run(journal.Dispose);
                check(File.ReadAllLines(path).Length==2000&&File.ReadAllLines(history).Length==2001,"Snapshot and history drain completely on unload");
                check(writes==3,"Two thousand proposals use one review append and one batched history append");
                check(journal.TryTake(out var completion)&&completion!.Error==null&&completion.History.Length==300,"Only bounded recent history returns to UI thread");
            }
            finally{release.Set();journal.Dispose();}
            var fail=true;
            using(var retry=new BackgroundReviewJournal(history,(p,lines)=>{if(fail)throw new IOException("Test disk failure");File.AppendAllLines(p,lines);}))
            {
                retry.Snapshot(path,[entry]);await Wait(()=>!retry.ReviewPending);
                check(retry.TryTake(out var failure)&&failure!.Error!=null,"Asynchronous write failure is visible");
                fail=false;retry.Snapshot(path,[entry]);await Wait(()=>!retry.ReviewPending);
                check(retry.TryTake(out var recovered)&&recovered!.History.Length==1,"Failed snapshot does not suppress successful retry");
                retry.Proposal(path,entry with{Action="WouldPermit Vendor"},"policy-key");
            }
            check(JsonDocument.Parse(File.ReadAllLines(path).Last()).RootElement.GetProperty("proposal").GetProperty("Action").GetString()=="WouldPermit Vendor","Individual policy proposals retain exact action through the ordered writer");
        }
        finally{Directory.Delete(dir,true);}
    }
    private static async Task Wait(Func<bool> done)
    {for(var i=0;i<5000;i++){if(done())return;await Task.Delay(1);}throw new Exception("Background test timed out");}
}
