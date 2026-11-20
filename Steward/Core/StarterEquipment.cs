namespace Steward.Core;

// Exact reviewed starter IDs and shops, not a name/level/price heuristic.
// Evidence: docs/data/starter-equipment-evidence.json. Runtime stock checks fail closed.
public static class StarterEquipment
{
    public static readonly IReadOnlySet<uint> Items = new HashSet<uint> { 1601, 1680, 1749, 1819, 1889, 1995, 2055, 2142, 2219, 2314, 2340, 2366, 2391, 2416, 2442, 2467, 2493, 2519, 2545, 2571, 2629, 2630, 2631, 2958, 2959, 2960, 2961, 2983, 2984, 2985, 2986, 2987, 2988, 2989, 2990, 2991, 2992, 3274, 3275, 3276, 3296, 3297, 3298, 3299, 3300, 3301, 3302, 3303, 3304, 3305, 3515, 3516, 3520, 3521, 3522, 3523, 3524, 3525, 3526, 3527, 3528, 3529, 3740, 3741, 3742, 3750, 3751, 3752, 3753, 3754, 3755, 3756, 3757, 3758, 3759, 7952, 9645, 9646, 9647, 9648, 9649, 9650, 9651, 9652, 25208, 25209, 25210, 25211, 25212, 25213, 25214, 25215, 33943, 33944, 33945, 33946, 41790, 41791, 41792, 41793 };
    public static bool ReviewedStock(uint id,uint shop,uint festival,uint quest,bool itemQuest,uint achievement,uint state,uint price) =>
        Items.Contains(id) && shop is 262671 or 262672 or 262673 or 262674 or 262675 or 262676 or 262700 or 262705 or 262715 or 262720 or 262725 or 262730 or 263007 or 263008 or 263263 or 263268 &&
        festival==0&&quest==0&&!itemQuest&&achievement==0&&state==100&&price>0;
    public static bool ExcludeLevelFilters(ItemFacts item) =>
        item.Equipment&&item.Rarity==1&&item.EquipLevel==1&&item.RepeatableStarter&&item.Source==SourceKind.Ordinary;
}
