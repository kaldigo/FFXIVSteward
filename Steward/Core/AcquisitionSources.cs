using System.Text.Json;

namespace Steward.Core;

public static class AcquisitionSources
{
    public static IReadOnlyDictionary<uint,SourceKind> Load()
    {
        var assembly=typeof(AcquisitionSources).Assembly;
        var name=assembly.GetManifestResourceNames().Single(n=>n.EndsWith("acquisition-sources.json",StringComparison.Ordinal));
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
