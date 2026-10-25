using System.Globalization;
using System.Text.Json;

namespace Steward.Core;

public static class Imports
{
    public static HashSet<uint> Glamaholic(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if(!root.TryGetProperty("Plates", out var plates) || plates.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Missing Glamaholic plates");
        var version = root.TryGetProperty("Version", out var v) ? v.GetInt32() : 1;
        if(version is < 1 or > 3) throw new InvalidDataException("Unsupported Glamaholic version");
        var result = new HashSet<uint>();
        void Plate(JsonElement plate)
        {
            if(!plate.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid plate items");
            foreach(var slot in items.EnumerateObject())
            {
                if(slot.Name == "$type") continue;
                if(!slot.Value.TryGetProperty("ItemId", out var item)) throw new InvalidDataException("Missing plate item ID");
                var id = item.GetUInt32(); if(id != 0) result.Add(id % 1000000);
            }
        }
        void Node(JsonElement node, int depth)
        {
            if(depth > 64) throw new InvalidDataException("Glamaholic folders are too deeply nested");
            if(version < 3) { Plate(node); return; }
            var kind = node.GetProperty("NodeType").GetString();
            if(kind == "Plate") Plate(node.GetProperty("Plate"));
            else if(kind == "Folder") foreach(var child in node.GetProperty("Children").EnumerateArray()) Node(child, depth+1);
            else throw new InvalidDataException("Unknown Glamaholic node type");
        }
        foreach(var node in plates.EnumerateArray()) Node(node, 0);
        return result;
    }

    public static PricingSettings MarketBuddy(string json) => JsonSerializer.Deserialize<PricingSettings>(json) ?? throw new InvalidDataException("Empty Market Buddy settings");

    public static List<Quote> MarketCache(string csv)
    {
        var result = new List<Quote>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach(var raw in lines.Skip(1))
        {
            var fields = raw.TrimEnd('\r').Split(',');
            if(fields.Length != 7) throw new InvalidDataException("Unexpected Allagan Market cache columns");
            var date = DateTime.Parse(fields[4].Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
            result.Add(new(uint.Parse(fields[0]), fields[1] == "Y", uint.Parse(fields[2]), uint.Parse(fields[5]), new DateTimeOffset(date), "Allagan Market/"+fields[3], fields[6] == "Y"));
        }
        return result;
    }
}
