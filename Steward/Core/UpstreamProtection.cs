namespace Steward.Core;

public static class UpstreamProtection
{
    public static Decision Apply(Decision decision,bool? protectedItem)
    {
        if(decision.Outcome==Outcome.Ignore)return decision;
        if(protectedItem==null)return new(Outcome.Wait,"AutoRetainer protection is unavailable");
        return protectedItem.Value?new(Outcome.Keep,"Explicit AutoRetainer protection"):decision;
    }
}
