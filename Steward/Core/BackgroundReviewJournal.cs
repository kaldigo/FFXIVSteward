using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json;

namespace Steward.Core;

// A single ordered writer owns both log files. No game objects or mutable evidence leave the game thread.
public sealed class BackgroundReviewJournal : IDisposable
{
    public sealed record Completion(string[] History,string? Error,bool Review,string? Key=null);
    private sealed record Work(DateTimeOffset At,string? Message,string? ReviewPath,ReviewEntry[]? Entries,int Generation,string? PolicyKey=null,string Mode="LogOnly");
    private readonly Channel<Work> queue=Channel.CreateBounded<Work>(new BoundedChannelOptions(128){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
    private readonly ConcurrentQueue<Completion> completed=new();
    private readonly Task worker;
    private readonly string historyPath;
    private readonly Action<string,IEnumerable<string>> append;
    private int generation;
    private int pendingReview;
    public bool ReviewPending=>Volatile.Read(ref pendingReview)!=0;
    public BackgroundReviewJournal(string historyPath,Action<string,IEnumerable<string>>? append=null)
    {
        this.historyPath=historyPath;this.append=append??File.AppendAllLines;
        worker=Task.Run(Run);
    }
    public void Reset()=>generation++;
    public bool TryTake(out Completion? result)=>completed.TryDequeue(out result);
    public void Record(string text,DateTimeOffset at)
    {
        if(!queue.Writer.TryWrite(new(at,text,null,null,generation)))throw new IOException("History writer is busy or closed");
    }
    public bool Snapshot(string path,IEnumerable<ReviewEntry> entries,string mode="LogOnly")
    {
        if(Interlocked.CompareExchange(ref pendingReview,1,0)!=0)return false;
        try
        {
            if(!queue.Writer.TryWrite(new(DateTimeOffset.UtcNow,null,path,entries.ToArray(),generation,Mode:mode)))
                throw new IOException("Review writer is busy or closed");
            return true;
        }
        catch{Volatile.Write(ref pendingReview,0);throw;}
    }
    public void Proposal(string path,ReviewEntry entry,string key,string mode="LogOnly")
    {
        if(!queue.Writer.TryWrite(new(DateTimeOffset.UtcNow,null,path,[entry],generation,key,mode)))throw new IOException("Review writer is busy or closed");
    }
    private async Task Run()
    {
        var journal=new ReviewJournal(append);var appliedGeneration=-1;
        await foreach(var work in queue.Reader.ReadAllAsync())
        {
            var isReview=work.Entries!=null;
            try
            {
                string[] messages;
                if(isReview)
                {
                    IReadOnlyList<ReviewEntry> changed;
                    if(work.PolicyKey!=null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(work.ReviewPath!)!);
                        append(work.ReviewPath!,work.Entries!.Select(proposal=>JsonSerializer.Serialize(new{at=work.At,mode=work.Mode,proposal})));
                        changed=work.Entries!;
                    }
                    else
                    {
                        if(appliedGeneration!=work.Generation){journal.Reset();appliedGeneration=work.Generation;}
                        changed=journal.Snapshot(work.ReviewPath!,work.Entries!,work.Mode);
                    }
                    messages=changed.Select(e=>$"[{work.Mode}] {e.Action}: {e.Location}; item {e.ItemId} x{e.Quantity}. {e.Reason}").ToArray();
                }
                else messages=[work.Message!];
                var history=messages.Select(m=>$"{work.At.LocalDateTime:yyyy-MM-dd HH:mm:ss} {m}").ToArray();
                if(history.Length>0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
                    append(historyPath,history.Select(message=>JsonSerializer.Serialize(new{at=work.At,message})));
                }
                if(isReview)completed.Enqueue(new(history.TakeLast(300).ToArray(),null,true,work.PolicyKey));
            }
            catch(Exception ex)
            {
                if(isReview)journal.Reset(); // Failed writes must never suppress a retry.
                completed.Enqueue(new([],ex.Message,isReview,work.PolicyKey));
            }
            finally{if(isReview&&work.PolicyKey==null)Volatile.Write(ref pendingReview,0);}
        }
    }
    public void Dispose(){queue.Writer.TryComplete();worker.GetAwaiter().GetResult();}
}
