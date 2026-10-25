namespace Steward.Core;

public enum StepResult { Waiting, Completed, Failed }
public sealed record Operation(string Name, Func<StepResult> Tick, Action? Finally = null, int TimeoutMs = 15000);
public sealed class OperationQueue(Action<string>? beforeStart=null,Action? acknowledged=null)
{
    private readonly Queue<Operation> queue=[];
    private Operation? current;
    private long started;
    private bool entered;
    private long generation;
    public bool Running => current!=null || queue.Count>0;
    public string Status { get; private set; } = "Idle";
    public int Completed { get; private set; }
    public void Begin(){if(Running)throw new InvalidOperationException("Queue is already running");Status="Idle";Completed=0;}
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
            if(now-started>=current.TimeoutMs){Stop("Stopped: "+current.Name+" timed out; reconcile before retrying");return;}
            var result=active.Tick();
            if(current!=active || generation!=epoch)return; // A synchronous fault callback may have cancelled this tick.
            if(result==StepResult.Failed){Stop("Stopped: "+current.Name+" failed");return;}
            if(result==StepResult.Completed)
            {
                var done=current;current=null;done.Finally?.Invoke();
                if(generation!=epoch)return;
                acknowledged?.Invoke();
                if(generation!=epoch)return;
                Completed++;if(queue.Count==0)Status="Completed";
            }
        }
        catch(Exception ex){Stop("Stopped: "+ex.Message);}
    }
    public void Stop(string reason)
    {
        generation++;
        var active=current;current=null;
        var failures=new List<string>();
        void Finish(Operation? operation){try{operation?.Finally?.Invoke();}catch(Exception ex){failures.Add(ex.Message);}}
        Finish(active);
        while(queue.TryDequeue(out var pending))Finish(pending);
        Status=reason+(failures.Count==0?"":"; cleanup: "+string.Join("; ",failures));
    }
}
