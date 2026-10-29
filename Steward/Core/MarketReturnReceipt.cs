namespace Steward.Core;

public static class MarketReturnReceipt
{
    public static bool Arrived(ulong before,ulong after,uint quantity)=>
        quantity>0&&after>=before&&after-before==quantity;
}
