namespace Steward;
public sealed partial class Plugin
{
    private void OnWorkflowDresserRestored(ulong character)
    {
        if((config.WorkflowMask&4)!=0)storageRun.Restored(character);
        else Record("Dresser restoration completed; entrust is excluded from this workflow, so the item remains in inventory.");
    }
    private int ReadWorkflowMask()
    {
        try
        {
            var api=pi.GetIpcSubscriber<int>("AutoRetainer.Workflow.Mask");
            return api.HasFunction?api.InvokeFunc():255;
        }
        catch{return 0;} // A failing provider must not expand a restricted run.
    }
}
