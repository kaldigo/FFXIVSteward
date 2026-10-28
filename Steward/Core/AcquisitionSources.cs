using System.Text.Json;

namespace Steward.Core;

public static class AcquisitionSources
{
    // Reviewed permanent city suppliers: Jossy, O'rhoyod, Engerrand and Fridurih.
    // State 100 is the observed ordinary stock state for these exact shops, not a global interpretation.
    public static bool ReviewedGilMaterial(uint shop,bool equipment,uint festival,uint shopQuest,bool itemQuest,
        uint achievement,uint state,uint price) => shop is 262174 or 262182 or 262192 or 262202 &&
        !equipment&&festival==0&&shopQuest==0&&!itemQuest&&achievement==0&&state==100&&price>0;

    public static HashSet<uint> InspectionOutputs(IEnumerable<(uint Input,uint Output,uint Quantity)> rows,IReadOnlySet<uint> gathered) =>
        rows.Where(r=>r.Input!=0&&r.Output!=0&&r.Quantity>0&&gathered.Contains(r.Input)).Select(r=>r.Output).ToHashSet();

    public static HashSet<uint> RepeatableMaterials(IEnumerable<(uint Item,bool LocatedNode)> gathering,
        IEnumerable<(uint Item,bool TargetedTask)> ventures) =>
        gathering.Where(r=>r.Item!=0&&r.LocatedNode).Select(r=>r.Item)
            .Concat(ventures.Where(r=>r.Item!=0&&r.TargetedTask).Select(r=>r.Item)).ToHashSet();

    public static HashSet<uint> Fishable(IEnumerable<(uint Item,bool InLog)> rod,
        IEnumerable<(uint Item,bool Visible,uint Territory)> spear) =>
        rod.Where(r=>r.Item!=0&&r.InLog).Select(r=>r.Item)
            .Concat(spear.Where(r=>r.Item!=0&&r.Visible&&r.Territory!=0).Select(r=>r.Item)).ToHashSet();

    public static IReadOnlyDictionary<uint,SourceKind> Load(string suffix="acquisition-sources.json")
    {
        var assembly=typeof(AcquisitionSources).Assembly;
        var name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith(suffix,StringComparison.Ordinal));
        using var stream=assembly.GetManifestResourceStream(name)??throw new InvalidDataException("Missing acquisition source index");
        using var doc=JsonDocument.Parse(stream);
        var result=new Dictionary<uint,SourceKind>();
        foreach(var entry in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            var id=entry[0].GetUInt32();var kind=(SourceKind)entry[1].GetInt32();
            if(id==0 || !Enum.IsDefined(kind) || kind is SourceKind.Unknown or SourceKind.Ordinary || !result.TryAdd(id,kind))
                throw new InvalidDataException("Invalid acquisition source index");
        }
        return result;
    }
    public static SourceKind Merge(SourceKind first,SourceKind next)
    {
        static int Rank(SourceKind k)=>k switch{SourceKind.Special or SourceKind.AllianceRaid or SourceKind.SavageRaid or SourceKind.HighEnd=>3,
            SourceKind.NormalDungeon or SourceKind.NormalRaid=>2,SourceKind.Ordinary=>1,_=>0};
        return Rank(next)>Rank(first)?next:first;
    }
}
