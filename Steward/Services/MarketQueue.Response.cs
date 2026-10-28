using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue
{
    private Hook<InfoProxyItemSearch.Delegates.ProcessRequestResult>? searchResponseHook;
    private MarketComparison.SearchResponse? searchResponse;
    private long responseSequence;
    private void InitializeSearchResponses(IGameInteropProvider interop)
    {
        try
        {
            searchResponseHook=interop.HookFromAddress<InfoProxyItemSearch.Delegates.ProcessRequestResult>(
                InfoProxyItemSearch.Addresses.ProcessRequestResult.Value,SearchResponseDetour);
            searchResponseHook.Enable();
        }
        catch(Exception ex)
        {
            searchResponseHook?.Dispose();searchResponseHook=null;
            history("Market response observer unavailable; live comparisons held: "+ex.Message);
        }
    }
    private void SearchResponseDetour(InfoProxyItemSearch* proxy,byte count,int status)
    {
        // Observe only. Forward the original call exactly once, even if capture fails.
        try
        {
            if(proxy!=null)Volatile.Write(ref searchResponse,new(Interlocked.Increment(ref responseSequence),
                proxy->SearchItemId,count,status,DateTimeOffset.UtcNow));
        }
        catch { /* Missing evidence holds the comparison; never interrupt the game callback. */ }
        finally { searchResponseHook!.Original(proxy,count,status); }
    }
}
