namespace Steward.Core;

// Transient permission for the follow-up to a real retainer handoff or explicit Run now.
// Enabling automation and loading saved settings deliberately do not create a run.
public sealed class StorageRun
{
    private ulong owner;
    public bool EntrustPending { get; private set; }
    public void Restored(ulong character) { if(owner!=0&&owner==character)EntrustPending=true; }
    public void EntrustStarted() => EntrustPending=false;
    public void Begin(ulong character,bool authorized)
    {
        if(!authorized||owner!=character)End();
        owner=authorized?character:0;
    }
    public bool Allows(ulong character,bool executionEnabled) => executionEnabled&&owner!=0&&owner==character;
    public void End() { owner=0;EntrustPending=false; }
}
