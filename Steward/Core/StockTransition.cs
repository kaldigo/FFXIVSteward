namespace Steward.Core;

public static class StockTransition
{
    public static bool Removed(Stock expected,Stock? current,uint removed,bool loaded)
    {
        if(!loaded || removed==0 || removed>expected.Quantity)return false;
        var remainder=expected.Quantity-removed;
        return remainder==0 ? current==null : current==expected with{Quantity=remainder};
    }
}
