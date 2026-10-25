namespace Steward.Core;

public static class ActionPolicy
{
    public static bool Allows(string? action, Outcome outcome, bool automatic, bool connected, bool faulted)
    {
        if(!automatic || !connected || faulted)return false;
        return action switch
        {
            "Armoire"=>outcome==Outcome.CollectArmoire,
            "Dresser"=>outcome==Outcome.CollectDresser,
            "Retrieve"=>outcome is Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Dispose,
            "Market"=>outcome==Outcome.Market,
            "Vendor" or "Desynth" or "Gc"=>outcome==Outcome.Dispose,
            _=>false
        };
    }
}
