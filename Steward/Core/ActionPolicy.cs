namespace Steward.Core;

public static class ActionPolicy
{
    public static bool Allows(string? action, Outcome outcome, bool automatic, bool connected, bool faulted,
        bool reserved=false,bool ownsPostprocess=false)
    {
        if(!automatic || !connected || faulted)return false;
        if(reserved && !((action is "Retrieve" or "Store") && ownsPostprocess))return false;
        return action switch
        {
            "Armoire"=>outcome==Outcome.CollectArmoire,
            "Dresser"=>outcome==Outcome.CollectDresser,
            "Store"=>outcome==Outcome.StoreRetainer,
            "Retrieve"=>outcome is Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Dispose,
            "Market"=>outcome==Outcome.Market,
            "Vendor" or "Desynth" or "Gc"=>outcome==Outcome.Dispose,
            _=>false
        };
    }
}
