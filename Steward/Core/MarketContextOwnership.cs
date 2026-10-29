namespace Steward.Core;

public static class MarketContextOwnership
{
    // ContextMenu is a shared top-level addon: parent/host can legitimately be zero.
    // Its native context agent carries the owning addon. Never accept a contradictory owner.
    public static bool Matches(ushort saleAddon,uint contextOwner,ushort parent,ushort host,int expectedRow,int actualRow)=>
        saleAddon!=0&&expectedRow is >=0 and <20&&expectedRow==actualRow&&
        (contextOwner==saleAddon||contextOwner==0&&ConfirmationOwnership.Matches(saleAddon,parent,host));
}
