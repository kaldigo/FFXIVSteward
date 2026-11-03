using Dalamud.Bindings.ImGui;
using Steward.Services;

namespace Steward;

public sealed partial class Plugin
{
    private enum BellAction { GlamourStorage, DresserCleanup, InventoryDisposal }
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
            BellAction.InventoryDisposal => config.Features.Disposal,
            _ => false
        });

    private bool ManualBell(string stage) => pi.GetIpcSubscriber<ulong,string,bool>(
        "AutoRetainer.InventoryPolicy.ManualBell").InvokeFunc(player.ContentId, stage);

    private void DrawManualBellActions()
    {
        ImGui.TextUnformatted("Steward");
        Button("Store glamours", BellAction.GlamourStorage);
        Button("Clean dresser", BellAction.DresserCleanup);
        Button("Dispose inventory", BellAction.InventoryDisposal);

        void Button(string label, BellAction action)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(pendingBellAction.HasValue);
            var clicked = ImGui.Button(label);
            ImGui.EndDisabled();
            if(!clicked) return;
            if(!BellActionAllowed(action))
            {
                Alert("Check the feature is enabled, log-only is off and other work has finished before starting " + label + ".");
                return;
            }
            try
            {
                if(!ManualBell("Available") || !ManualBell("Close"))
                {
                    Alert("Wait for the retainer list to settle and switch off the full AutoRetainer run before starting " + label + ".");
                    return;
                }
                pendingBellAction = action;
                bellCharacter = player.ContentId;
                bellDeadline = Environment.TickCount64 + 10000;
            }
            catch(Exception ex) { Alert("Manual bell controls require the updated AutoRetainer fork: " + ex.Message); }
        }
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
        if(action == BellAction.InventoryDisposal) { StartCleanup(); return; }
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
