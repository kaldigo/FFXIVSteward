namespace Steward.Core;

public sealed class SilverStrategy
{
    public const uint ItemId=13116;
    public bool Enabled {get;set;}
    public ulong Retainer {get;set;}
    public uint MinimumPrice {get;set;}=40001;
    public uint ResetPrice {get;set;}=45000;
    public uint Undercut {get;set;}=5;
    public int FloorUpdateRevision {get;set;}
    public void ApplyRequestedFloorUpdate()
    {
        if(FloorUpdateRevision>=1)return;
        // User-requested update for this dedicated strategy only; preserve custom floors.
        if(Retainer==33777197238509254&&MinimumPrice==44001)MinimumPrice=40001;
        FloorUpdateRevision=1;
    }
    public bool Valid=>Retainer!=0&&MinimumPrice>0&&ResetPrice>=MinimumPrice&&ResetPrice<=999999999&&Undercut is >0 and <=1000&&MinimumPrice<=999999999-Undercut;
    public bool Owns(ulong retainer)=>Enabled&&Valid&&retainer==Retainer;
    public bool Matches(ulong retainer,uint item)=>Owns(retainer)&&item==ItemId;
    public SaleRule Rule()=>new(){Name="Experimental Metallic Silver Dye",ItemIds=[ItemId],MinimumUnit=MinimumPrice,MinimumTotal=MinimumPrice,MaximumStack=5};
    public static readonly (uint Quantity,int Count)[] Mix=[(5,10),(2,5),(1,5)];
    public uint NextQuantity(IEnumerable<Stock> listings,uint available)
    {
        var rows=listings.ToArray();if(rows.Length>=20)return 0;
        foreach(var (quantity,count) in Mix)
            if(available>=quantity&&rows.Count(s=>s.ItemId==ItemId&&!s.Hq&&s.Quantity==quantity)<count)return quantity;
        return 0;
    }
    public sealed record Quote(uint Price,Offering[] Buyouts,bool Reset)
    {public ulong BuyoutCost=>Buyouts.Aggregate(0ul,(sum,o)=>checked(sum+(ulong)o.UnitPrice*o.Quantity));}
    public Quote? Price(IEnumerable<Offering> offers,IReadOnlySet<ulong> own,bool complete)
    {
        if(!Valid||!complete)return null;
        var rows=offers.ToArray();
        if(rows.Any(o=>o.ItemId!=ItemId||o.Hq||o.RetainerId==0||o.ListingId==0||o.UnitPrice is 0 or >999999999||o.Quantity is 0 or >9999)||rows.Select(o=>o.ListingId).Distinct().Count()!=rows.Length)return null;
        var other=rows.Where(o=>!own.Contains(o.RetainerId)&&o.RetainerId!=Retainer).ToArray();
        var suitable=other.Where(o=>o.UnitPrice>=MinimumPrice+Undercut).Select(o=>(uint?)o.UnitPrice).Min();
        var target=suitable.HasValue?suitable.Value-Undercut:ResetPrice;
        var alerts=other.Where(o=>o.UnitPrice<(suitable.HasValue?MinimumPrice+Undercut:ResetPrice)).OrderBy(o=>o.UnitPrice).ThenBy(o=>o.ListingId).ToArray();
        return new(target,alerts,!suitable.HasValue);
    }
}
