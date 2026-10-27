using Dalamud.Bindings.ImGui;

namespace Steward;

public sealed partial class Plugin
{
    private bool cleanupActive;
    private ulong cleanupOwner;
    private string cleanupStatus="";
    private long nextCleanupStatus;
    private bool CleanupAllowed(ulong character)=>player.IsLoaded&&character!=0&&character==player.ContentId&&
        attached==character&&config.Connected&&config.ExecutionEnabled&&config.Features.Disposal&&!executionFault;
    private bool CleanupRetainerAllowed(ulong character,ulong retainer)=>CleanupAllowed(character)&&
        view.Retainers.Contains(retainer)&&config.Storage.RetainerEnabled(retainer);
    private void StartCleanup()
    {
        if(!CleanupAllowed(player.ContentId)||ownsPostprocess||market.Queue.Running||cleanupActive)
        {Alert("Enable disposal and automation, resolve any stopped run, and wait for current work before starting full cleanup.");return;}
        try
        {
            if(pi.GetIpcSubscriber<int>("AutoRetainer.InventoryPolicy.CleanupVersion").InvokeFunc()<1)
                throw new InvalidOperationException("Update AutoRetainer for batch cleanup");
            if(!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.StartCleanup").InvokeFunc(player.ContentId))
            {cleanupStatus=pi.GetIpcSubscriber<string>("AutoRetainer.InventoryPolicy.CleanupStatus").InvokeFunc();Alert("Full cleanup could not start: "+cleanupStatus);return;}
            cleanupOwner=player.ContentId;cleanupActive=true;storageRun.End();nextCleanupStatus=0;
            Record("Full cleanup started: approved disposal withdrawals, enabled methods, and return batches through AutoRetainer");
        }
        catch(Exception ex){Alert("Full cleanup unavailable: "+ex.Message);}
    }
    private void UpdateCleanup()
    {
        if(!cleanupActive||Environment.TickCount64<nextCleanupStatus)return;
        nextCleanupStatus=Environment.TickCount64+500;
        var active=pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.CleanupActive").InvokeFunc(cleanupOwner);
        var current=pi.GetIpcSubscriber<string>("AutoRetainer.InventoryPolicy.CleanupStatus").InvokeFunc();
        if(current!=cleanupStatus){cleanupStatus=current;Record("Full cleanup: "+current);}
        if(!active){cleanupActive=false;cleanupOwner=0;storageRun.End();Alert("Full cleanup: "+current);}
    }
    private void CancelCleanup()
    {
        if(!cleanupActive)return;
        var owner=cleanupOwner;cleanupActive=false;cleanupOwner=0;
        try{pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.StopCleanup").InvokeFunc(owner);}
        catch(Exception ex){Record("Cleanup cancellation could not be confirmed: "+ex.Message);}
    }
    private void DrawCleanupControls()
    {
        ImGui.BeginDisabled(!config.Features.Disposal||cleanupActive);
        if(ImGui.Button("Run full cleanup"))StartCleanup();
        ImGui.EndDisabled();
        if(cleanupActive){ImGui.SameLine();if(ImGui.Button("Stop full cleanup"))Stop();}
        ImGui.TextWrapped("Visits included retainers, withdraws approved disposal items, and runs enabled methods in order: GC, desynthesis, then selling. Repeats while confirmed progress is made, using AutoRetainer's configured return travel. This pass does not start market listings or glamour removals.");
        if(cleanupStatus.Length>0)ImGui.TextWrapped(cleanupStatus);
    }
}
