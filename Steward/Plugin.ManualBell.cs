using Steward.Services;

namespace Steward;

public sealed partial class Plugin
{
    private enum BellAction { GlamourStorage, DresserCleanup, InventoryDisposal, Armoire, Overflow, CarriedAll, CarriedGc, CarriedDesynth, CarriedVendor, RetainersAll, RetainersGc, RetainersDesynth, RetainersVendor }
    private BellAction? pendingBellAction;
    private ulong bellCharacter;
    private long bellDeadline;

    private bool BellActionAllowed(BellAction action) =>
        player.IsLoaded && attached != 0 && attached == player.ContentId && config.Connected &&
        config.ExecutionEnabled && !executionFault && !market.Queue.Running && !ownsPostprocess &&
        !cleanupActive && !storageRun.Allows(attached, true) && !AutoDutyCoordination.OwnsTravel(pi) &&
        (action switch
        {
            BellAction.GlamourStorage => config.Features.ArmoireStorage || config.Features.DresserCollection || config.Features.OverflowStorage,
            BellAction.DresserCleanup => config.CleanDresser,
            BellAction.Armoire => config.Features.ArmoireStorage,
            BellAction.Overflow => config.Features.OverflowStorage,
            _ => config.Features.Disposal
        });

    private bool ManualBell(string stage) => pi.GetIpcSubscriber<ulong,string,bool>(
        "AutoRetainer.InventoryPolicy.ManualBell").InvokeFunc(player.ContentId, stage);

    private string? manualMarket;
    private ulong manualMarketOwner;
    private bool previewBellMarket;
    private string BellAvailability(string action,ulong retainer)
    {
        if(!player.IsLoaded || attached==0 || attached!=player.ContentId || !config.Connected) return "Connect Steward on this character.";
        if(executionFault || config.ExecutionReviewRequired) return "Resolve the inventory review hold.";
        if(pendingBellAction.HasValue || cleanupActive || market.Queue.Running || ownsPostprocess || storageRun.Allows(attached,true) ||
            AutoDutyCoordination.OwnsTravel(pi)) return "Wait for the current operation to finish.";
        if(action=="LogOnly")return config.LogOnly ? "" : "Live";
        if(action=="Native")return config.LogOnly ? "Steward is in log-only mode." : "";
        if(action is "Market" or "List" or "Reprice" or "Review")
        {
            if(retainer!=0 && (!view.Retainers.Contains(retainer)||!config.SalesEnabledFor(retainer))) return "Sales are excluded for this retainer.";
            var enabled=action switch { "List"=>config.Features.NewListings, "Reprice"=>config.Features.Repricing,
                "Review"=>config.Features.ListingReturns||config.Features.Rotation, _=>config.Features.AnyMarket };
            return enabled ? "" : "Enable the corresponding sale feature in Steward.";
        }
        if(config.LogOnly) return "Steward is in log-only mode.";
        return Enum.TryParse<BellAction>(action,out var parsed) && BellActionAllowed(parsed) ? "" : "Enable this feature in Steward.";
    }

    private bool StartBellMarket(string action,string name)
    {
        var preview=action.StartsWith("Preview/",StringComparison.Ordinal);
        if(preview)action=action[8..];
        var retainer=view.RetainerNames.FirstOrDefault(p=>p.Value==name).Key;
        if(retainer==0 || action is not ("Market" or "List" or "Reprice" or "Review") || BellAvailability(action,retainer)!="") return false;
        ownsPostprocess=true;
        nextPostprocessRelease=0;
        manualMarket=action;
        manualMarketOwner=retainer;
        previewBellMarket=preview||config.LogOnly;
        openingMarket=true;
        marketOpenDeadline=Environment.TickCount64+15000;
        return true;
    }

    private void LogBellMarketPreview(string action)
    {
        var preview=market.PreviewMarket();
        Record($"Market preview for {view.RetainerNames.GetValueOrDefault(preview.Retainer)}: {preview.Status}");
        if(action is "Review" or "Market")
        {
            foreach(var row in preview.Returns??[])
                Record($"Would withdraw {view.Facts(row.Source.ItemId)?.Name} ({row.Source.ItemId}): {row.Reason}");
            foreach(var row in preview.Rotations??[])
                Record($"Would rotate {row.Source.ItemId} for {row.Replacement.ItemId}");
            if(action=="Review")
                Alert($"Market review: {preview.Returns?.Count??0} proposed withdrawals and {preview.Rotations?.Count??0} rotations on {view.RetainerNames.GetValueOrDefault(preview.Retainer)}.");
        }
        if(action is "Market" or "List" && preview.Plan!=null)
            foreach(var row in preview.Plan.Listings) Record($"Would list {row.Source.ItemId} x{row.Quantity} at {row.UnitPrice}");
        if(action is "Market" or "Reprice")
            foreach(var row in preview.Reprices) Record($"Would reprice {row.Source.ItemId} from {row.Before} to {row.After}");
    }

    private void StartBellAction(string name)
    {
        if(BellAvailability(name,0)!="" || !Enum.TryParse<BellAction>(name,out var action)) return;
        if(!ManualBell("Available") || !ManualBell("Close"))
        {
            Alert("Wait for the retainer list to settle before starting "+name+".");
            return;
        }
        pendingBellAction=action;
        bellCharacter=player.ContentId;
        bellDeadline=Environment.TickCount64+10000;
    }

    private void UpdateManualBellAction()
    {
        if(pendingBellAction is not {} action) return;
        if(player.ContentId != bellCharacter || !BellActionAllowed(action) || Environment.TickCount64 >= bellDeadline)
        {
            pendingBellAction = null;
            Alert("Manual bell action cancelled before starting. Check execution settings and that the retainer list closed.");
            return;
        }
        if(!ManualBell("Released")) return;
        pendingBellAction = null;
        if(action==BellAction.Armoire)
        {
            BeginStorageRun(true);
            storageRun.ArmoireOnly=true;
            return;
        }
        if(action==BellAction.Overflow) { StartOverflow(); return; }
        if(action is not (BellAction.GlamourStorage or BellAction.DresserCleanup))
        {
            StartCleanup(action==BellAction.InventoryDisposal ? "RetainersAll" : action.ToString());
            return;
        }
        if(action == BellAction.DresserCleanup && !pi.GetIpcSubscriber<ulong,string,bool>(
            "AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"InspectDresser"))
        {
            Alert("AutoRetainer could not start the dresser cleanup visit.");
            return;
        }
        BeginStorageRun(true);
        storageRun.StorageOnly = action == BellAction.GlamourStorage;
        storageRun.CleanupOnly = action == BellAction.DresserCleanup;
        Record("Manual bell action started: " + action);
    }
}
