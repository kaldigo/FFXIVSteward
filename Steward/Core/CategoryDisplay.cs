namespace Steward.Core;

public sealed record CategoryChoice(uint Id,string Name,uint Icon,string Group,IReadOnlyList<uint> Members);
public static class CategoryDisplay
{
    public static readonly string[] Groups=["Main Arm/Off Arm","Armor","Items","Housing"];
    private static readonly Dictionary<uint,(uint Canonical,string Name)> Paired=new()
    {
        [6]=(6,"Thaumaturge's arms"),[7]=(6,"Thaumaturge's arms"),
        [8]=(8,"Conjurer's arms"),[9]=(8,"Conjurer's arms"),
        [12]=(12,"Carpenter's tools"),[13]=(12,"Carpenter's tools"),
        [14]=(14,"Blacksmith's tools"),[15]=(14,"Blacksmith's tools"),
        [16]=(16,"Armorer's tools"),[17]=(16,"Armorer's tools"),
        [18]=(18,"Goldsmith's tools"),[19]=(18,"Goldsmith's tools"),
        [20]=(20,"Leatherworker's tools"),[21]=(20,"Leatherworker's tools"),
        [22]=(22,"Weaver's tools"),[23]=(22,"Weaver's tools"),
        [24]=(24,"Alchemist's tools"),[25]=(24,"Alchemist's tools"),
        [26]=(26,"Culinarian's tools"),[27]=(26,"Culinarian's tools"),
        [28]=(28,"Miner's tools"),[29]=(28,"Miner's tools"),
        [30]=(30,"Botanist's tools"),[31]=(30,"Botanist's tools"),
        [32]=(32,"Fisher's tools"),[99]=(32,"Fisher's tools")
    };
    public static bool IsHousing(uint id)=>id is 57 or >=64 and <=80 or 82 or 95;
    public static string Group(uint id,byte major)=>IsHousing(id)?"Housing":
        id==11||major is 3 or 4?"Armor":major is 1 or 2?"Main Arm/Off Arm":"Items";
    public static IReadOnlyList<CategoryChoice> Build(IReadOnlyDictionary<uint,string> names,IReadOnlyDictionary<uint,(uint Icon,byte Major,byte Minor)> details)
        =>names.OrderBy(p=>details.GetValueOrDefault(p.Key).Major).ThenBy(p=>details.GetValueOrDefault(p.Key).Minor)
            .GroupBy(p=>Paired.TryGetValue(p.Key,out var pair)?pair.Canonical:p.Key)
            .Select(g=>new CategoryChoice(g.Key,Paired.TryGetValue(g.Key,out var pair)?pair.Name:g.First().Value,
                details.GetValueOrDefault(g.Key).Icon,Group(g.Key,details.GetValueOrDefault(g.Key).Major),g.Select(p=>p.Key).ToArray())).ToArray();
    public static void Set(HashSet<uint> selected,IEnumerable<CategoryChoice> choices,bool enabled)
    {foreach(var id in choices.SelectMany(c=>c.Members)){if(enabled)selected.Add(id);else selected.Remove(id);}}
}
