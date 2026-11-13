namespace Steward.Core;

// Never serialized. Proof is sealed from a complete native read at dresser close.
public sealed class SessionCollection
{
    private ulong owner;
    private Dictionary<uint,int>? copies;
    private int? freeSlots;
    public int? FreeSlots(ulong character)=>owner!=0&&owner==character?freeSlots:null;
    public DateTimeOffset? ObservedAt { get; private set; }
    public void Invalidate(){owner=0;copies=null;freeSlots=null;ObservedAt=null;}
    public void Seal(ulong character,IReadOnlyDictionary<uint,int> observed,bool complete,bool uncertain,int? nativeFreeSlots=null)
    {
        Invalidate();
        if(character==0||!complete||uncertain||observed.Any(x=>x.Key==0||x.Value<=0))return;
        owner=character;freeSlots=nativeFreeSlots is >=0?nativeFreeSlots:null;copies=new(observed);ObservedAt=DateTimeOffset.UtcNow;
    }
    public Dictionary<uint,int>? Read(ulong character)=>character!=0&&character==owner&&copies!=null?new(copies):null;
}
