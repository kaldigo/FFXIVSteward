namespace Steward.Core;

public sealed record DresserRestoreState(uint Raw,int Slot,byte First,byte Second,int Copies,long Carried);

// Used only for a native false return (no command submitted). Accepted requests never retry.
public sealed class DresserRestoreRetry
{
    private DresserRestoreState? rejected;
    private int attempts;
    private long next;
    public bool Ready(long now)=>now>=next;
    public bool Unchanged(DresserRestoreState state)=>rejected==null||rejected==state;
    public bool Rejected(DresserRestoreState state,long now)
    {
        if(!Unchanged(state))return false;
        rejected=state;next=now+500;
        return ++attempts<3;
    }
}

public static class DresserRestoration
{
    // Re-resolve moved entries, but never guess between multiple replacement positions.
    public static int Find(ReadOnlySpan<uint> items,uint raw,int preferred)
    {
        if(raw==0)return -1;
        if(preferred>=0&&preferred<items.Length&&items[preferred]==raw)return preferred;
        var found=-1;
        for(var i=0;i<items.Length;i++)if(items[i]==raw)
        {if(found>=0)return -2;found=i;}
        return found;
    }
    public static StepResult Acknowledge(int beforeCopies,int copies,long beforePlayer,long player)
    {
        if(beforeCopies<1||beforePlayer<0||copies<beforeCopies-1||copies>beforeCopies||player<beforePlayer||player>beforePlayer+1)
            return StepResult.Failed;
        return copies==beforeCopies-1&&player==beforePlayer+1?StepResult.Completed:StepResult.Waiting;
    }
}
