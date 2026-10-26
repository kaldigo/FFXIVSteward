namespace Steward.Core;

public static class CollectionHolds
{
    public static HashSet<uint> ForCharacter(Dictionary<ulong,HashSet<uint>> ledger,ulong character) =>
        character!=0&&ledger.TryGetValue(character,out var held)?new(held):[];
    public static void Release(HashSet<uint> held,IEnumerable<uint> confirmed,Action persist)
    {
        var removed=confirmed.Where(held.Contains).Distinct().ToArray();
        foreach(var id in removed)held.Remove(id);
        try{persist();}catch{held.UnionWith(removed);throw;}
    }
}
