namespace Steward.Core;

public enum StepResult { Waiting, Completed, Failed }
public sealed record Operation(string Name, Func<StepResult> Tick, Action? Finally = null, int TimeoutMs = 15000, Func<bool>? CanSkip=null,Action? OnSkipped=null);
public sealed class OperationQueue(Action<string>? beforeStart=null,Action? acknowledged=null,Action<string>? skipped=null)
{
    private readonly Queue<Operation> queue=[];
    private Operation? current;
    private long started;
    private bool entered;
    private long generation;
    public bool Running => current!=null || queue.Count>0;
    public bool HasUnverifiedAction
    {get{try{return current!=null&&current.CanSkip?.Invoke()!=true;}catch{return true;}}}
    public string Status { get; private set; } = "Idle";
    public int Completed { get; private set; }
    public int Skipped { get; private set; }
    public void Begin(){if(Running)throw new InvalidOperationException("Queue is already running");Status="Idle";Completed=Skipped=0;}
    public void Add(Operation operation)=>queue.Enqueue(operation);
    public bool Plan(Action build)
    {
        if(Running || Status.StartsWith("Stopped:",StringComparison.Ordinal))return false;
        try{build();return true;}
        catch(Exception ex){Stop("Stopped: queue planning failed: "+ex.Message);return false;}
    }
    public void Advance(long now)
    {
        if(current==null)
        {if(!queue.TryDequeue(out current))return;started=now;entered=false;Status=current.Name;}
        try
        {
            var active=current;var epoch=generation;
            if(!entered){beforeStart?.Invoke(active.Name);entered=true;}
            if(current!=active || generation!=epoch)return;
            if(now-started>=current.TimeoutMs){if(!TrySkip(current.Name+" timed out before submission"))Stop("Stopped: "+current.Name+" timed out; reconcile before retrying");return;}
            var result=active.Tick();
            if(current!=active || generation!=epoch)return; // A synchronous fault callback may have cancelled this tick.
            if(result==StepResult.Failed){if(!TrySkip(current.Name+" was not submitted"))Stop("Stopped: "+current.Name+" failed");return;}
            if(result==StepResult.Completed)
            {
                var done=current;current=null;done.Finally?.Invoke();
                if(generation!=epoch)return;
                acknowledged?.Invoke();
                if(generation!=epoch)return;
                Completed++;if(queue.Count==0)Status=Skipped==0?"Completed":$"Completed ({Skipped} skipped)";
            }
        }
        catch(Exception ex)
        {
            try{if(current!=null&&TrySkip(current.Name+": "+ex.Message))return;}
            catch(Exception cleanup){Stop("Stopped: "+ex.Message+"; skip cleanup: "+cleanup.Message);return;}
            Stop("Stopped: "+ex.Message);
        }
    }
    private bool TrySkip(string reason)
    {
        if(current?.CanSkip?.Invoke()!=true)return false;
        var done=current;var epoch=generation;current=null;
        done.Finally?.Invoke();if(generation!=epoch)return true;
        acknowledged?.Invoke();if(generation!=epoch)return true;
        done.OnSkipped?.Invoke();if(generation!=epoch)return true;
        Skipped++;Status=queue.Count==0?"Completed with skipped items":"Skipped: "+reason;
        skipped?.Invoke(reason);return true;
    }
    public void ReplanPending()
    {
        try{while(queue.TryDequeue(out var pending))pending.Finally?.Invoke();if(current==null)Status="Waiting for fresh plan";}
        catch(Exception ex){Stop("Stopped: pending operation cleanup failed: "+ex.Message);}
    }
    public void Stop(string reason)
    {
        var clearUnsubmitted=current!=null&&entered&&!HasUnverifiedAction;
        generation++;
        var stopGeneration=generation;
        var active=current;current=null;
        var failures=new List<string>();
        void Finish(Operation? operation){try{operation?.Finally?.Invoke();}catch(Exception ex){failures.Add(ex.Message);}}
        Finish(active);
        while(queue.TryDequeue(out var pending))Finish(pending);
        if(clearUnsubmitted&&generation==stopGeneration&&failures.Count==0)
            try{acknowledged?.Invoke();}catch(Exception ex){failures.Add(ex.Message);}
        Status=failures.Count==0?reason:"Stopped: "+reason+"; cleanup: "+string.Join("; ",failures);
    }
}
