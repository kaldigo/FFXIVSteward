namespace Steward.Core;

public sealed record DresserPreparationItem(Stock Stock,int? ConditionPercent);
public sealed record DresserPreparationResult(uint[] RepairItems,uint[] UnknownItems,bool MissingPrisms)
{
    public bool Ready=>RepairItems.Length==0&&UnknownItems.Length==0&&!MissingPrisms;
}
public static class DresserPreparation
{
    public static DresserPreparationResult Check(IEnumerable<DresserPreparationItem> candidates,int? prisms)
    {
        var items=candidates.ToArray();
        return new(items.Where(x=>x.ConditionPercent is <100).Select(x=>x.Stock.ItemId).Distinct().ToArray(),
            items.Where(x=>x.ConditionPercent==null).Select(x=>x.Stock.ItemId).Distinct().ToArray(),
            items.Length>0&&prisms is not >0);
    }
}
