
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
    private void StartCleanup(string scope="RetainersAll")
    {
        if(Steward.Services.AutoDutyCoordination.OwnsTravel(pi))
        {Alert("Stop AutoDuty before starting full cleanup. Its separate GC step still uses Steward protections.");return;}
        if(!CleanupAllowed(player.ContentId)||ownsPostprocess||market.Queue.Running||cleanupActive)
        {Alert("Enable disposal and automation, resolve any stopped run, and wait for current work before starting full cleanup.");return;}
        try
        {
            if(pi.GetIpcSubscriber<int>("AutoRetainer.InventoryPolicy.CleanupVersion").InvokeFunc()<1)
                throw new InvalidOperationException("Update AutoRetainer for batch cleanup");
            var started=scope=="RetainersAll"
                ? pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.StartCleanup").InvokeFunc(player.ContentId)
                : pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.StartScopedCleanup").InvokeFunc(player.ContentId,scope);
            if(!started)
            {cleanupStatus=pi.GetIpcSubscriber<string>("AutoRetainer.InventoryPolicy.CleanupStatus").InvokeFunc();Alert("Full cleanup could not start: "+cleanupStatus);return;}
            cleanupOwner=player.ContentId;cleanupActive=true;storageRun.End();nextCleanupStatus=0;
            Record("Disposal pass started: "+scope);
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
}
