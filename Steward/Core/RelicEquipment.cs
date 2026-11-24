using System.Text.Json;

namespace Steward.Core;

public static class RelicEquipment
{
    private static readonly IReadOnlyDictionary<uint,string> families=Load();

    public static string? Family(uint id)=>families.GetValueOrDefault(id);

    private static IReadOnlyDictionary<uint,string> Load()
    {
        var assembly=typeof(RelicEquipment).Assembly;
        var resource=assembly.GetManifestResourceNames().Single(name=>name.EndsWith("relic-equipment.json",StringComparison.Ordinal));
        using var stream=assembly.GetManifestResourceStream(resource)??throw new InvalidDataException("Missing relic equipment index");
        using var document=JsonDocument.Parse(stream);
        if(document.RootElement.GetProperty("schema").GetInt32()!=1)
            throw new InvalidDataException("Unsupported relic equipment index");

        var result=new Dictionary<uint,string>();
        foreach(var family in document.RootElement.GetProperty("families").EnumerateObject())
        foreach(var row in family.Value.EnumerateArray())
        {
            var id=row.GetUInt32();
            if(id==0||!result.TryAdd(id,family.Name))
                throw new InvalidDataException("Invalid or duplicate relic equipment item");
        }
        return result;
    }
}
