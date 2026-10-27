namespace Steward.Core;

public enum DresserDepositKind { Ordinary, NewOutfit, ExistingOutfit }
public sealed record DresserOutfit(uint SetId, uint[] Pieces);
public sealed record StoredDresserOutfit(uint SetId, bool Hq, uint Index, IReadOnlySet<uint> Pieces);
public sealed record DresserDeposit(DresserDepositKind Kind, uint Target)
{
    // Only the requested source is submitted, even when other pieces of its set are in inventory.
    public static DresserDeposit Choose(Stock source, IEnumerable<DresserOutfit> definitions,
        IEnumerable<StoredDresserOutfit> stored)
    {
        var candidates=definitions.Where(s=>s.Pieces.Contains(source.ItemId)).OrderBy(s=>s.SetId).ToArray();
        var ids=candidates.Select(s=>s.SetId).ToHashSet();
        var existing=stored.Where(s=>ids.Contains(s.SetId)&&s.Hq==source.Hq&&!s.Pieces.Contains(source.ItemId))
            .OrderBy(s=>s.Index).FirstOrDefault();
        if(existing!=null)return new(DresserDepositKind.ExistingOutfit,existing.Index);
        return candidates.Length==0 ? new(DresserDepositKind.Ordinary,0)
            : new(DresserDepositKind.NewOutfit,candidates[0].SetId+(source.Hq?1000000u:0));
    }
}
