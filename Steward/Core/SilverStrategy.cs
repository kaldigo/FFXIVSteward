namespace Steward.Core;

public sealed record SilverStack(uint Quantity,int Count);

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
    public static string? PriceInputError(int minimum,int reset,int undercut)
    {
        if(minimum<1||minimum>999999999)return "Minimum price must be between 1 and 999,999,999 gil.";
        if(reset<minimum||reset>999999999)return "Reset price must be at least the minimum and no more than 999,999,999 gil.";
        if(undercut<1||undercut>1000)return "Undercut must be between 1 and 1,000 gil.";
        if((long)minimum+undercut>999999999)return "Minimum plus undercut must fit within the game's price limit.";
        return null;
    }
    public bool TrySetPrices(int minimum,int reset,int undercut)
    {
        if(PriceInputError(minimum,reset,undercut)!=null)return false;
        MinimumPrice=(uint)minimum;ResetPrice=(uint)reset;Undercut=(uint)undercut;
        FloorUpdateRevision=1;
        return true;
    }
    public bool Valid=>StackInputError(Stacks)==null&&Retainer!=0&&MinimumPrice>0&&ResetPrice>=MinimumPrice&&ResetPrice<=999999999&&Undercut is >0 and <=1000&&MinimumPrice<=999999999-Undercut;
    public bool Owns(ulong retainer)=>Enabled&&Valid&&retainer==Retainer;
    public bool Matches(ulong retainer,uint item)=>Owns(retainer)&&item==ItemId;
    public SaleRule Rule()=>new(){Name="Experimental Metallic Silver Dye",ItemIds=[ItemId],MinimumUnit=MinimumPrice,MinimumTotal=MinimumPrice,MaximumStack=StackInputError(Stacks)==null?Stacks.Max(s=>(int)s.Quantity):5};
    public List<SilverStack> Stacks {get;set;}=[new(5,10),new(2,5),new(1,5)];
    public static string? StackInputError(IReadOnlyList<SilverStack>? stacks,uint limit=9999)
    {
        if(stacks==null||stacks.Count is <1 or >20)return "Add between 1 and 20 stack groups.";
        if(stacks.Any(s=>s==null||s.Quantity<1||s.Quantity>limit))return $"Dyes per stack must be between 1 and {limit:N0}.";
        if(stacks.Any(s=>s.Count is <1 or >20)||stacks.Sum(s=>(long)s.Count)>20)return "Use 1 to 20 stacks per group, with no more than 20 listings in total.";
        if(stacks.Select(s=>s.Quantity).Distinct().Count()!=stacks.Count)return "Use one group per stack size; combine groups with the same number of dyes.";
        return null;
    }
    public bool TrySetStacks(IEnumerable<SilverStack> stacks,uint limit=9999)
    {
        var copy=stacks.ToList();if(StackInputError(copy,limit)!=null)return false;
        Stacks=copy;return true;
    }
    public uint NextQuantity(IEnumerable<Stock> listings,uint available)
    {
        if(StackInputError(Stacks)!=null)return 0;
        var rows=listings.ToArray();if(rows.Length>=20)return 0;
        foreach(var (quantity,count) in Stacks)
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
