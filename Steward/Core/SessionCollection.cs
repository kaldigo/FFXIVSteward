namespace Steward.Core;

// Never serialized. Proof is sealed from a complete native read at dresser close.
public sealed class SessionCollection
{
    private ulong owner;
    private Dictionary<uint,int>? copies;
    public DateTimeOffset? ObservedAt { get; private set; }
    public void Invalidate(){owner=0;copies=null;ObservedAt=null;}
    public void Seal(ulong character,IReadOnlyDictionary<uint,int> observed,bool complete,bool uncertain)
    {
        Invalidate();
        if(character==0||!complete||uncertain||observed.Any(x=>x.Key==0||x.Value<=0))return;
        owner=character;copies=new(observed);ObservedAt=DateTimeOffset.UtcNow;
    }
    public Dictionary<uint,int>? Read(ulong character)=>character!=0&&character==owner&&copies!=null?new(copies):null;
}
