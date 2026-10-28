using Dalamud.Plugin;
using Steward.Core;

namespace Steward.Services;

internal static class AutoDutyCoordination
{
    // AutoDuty owns travel between its separate retainer, GC and duty stages.
    // Missing AutoDuty is normal; a failing installed provider must not permit competing travel.
    public static bool OwnsTravel(IDalamudPluginInterface pi)=>AutoDutyTravel.Owned(name=>
    {
        var ipc=pi.GetIpcSubscriber<bool>(name);
        return ipc.HasFunction?ipc.InvokeFunc():null;
    });
}
