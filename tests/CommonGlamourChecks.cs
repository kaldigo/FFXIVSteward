using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Steward.Core;

static class CommonGlamourChecks
{
    public static void Run(Action<bool,string> check,DateTimeOffset now)
    {
        var assembly=typeof(CommonGlamourChecks).Assembly;
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"release-config.json")))root=root.Parent;
        if(root==null)throw new Exception("Cannot locate the source tree for compiled bundle verification");
        var plugin=Path.Combine(root.FullName,"Steward/bin/Release/Steward.dll");
        check(File.Exists(plugin),"Build the plugin before checking its embedded glamour list");
        // Inspect PE resources without loading or executing the plugin.
        using(var file=File.OpenRead(plugin))
        using(var pe=new PEReader(file))
        {
            var metadata=pe.GetMetadataReader();
            var resource=metadata.ManifestResources.Select(metadata.GetManifestResource)
                .Single(r=>metadata.GetString(r.Name)=="Steward.data.common-glamour.json");
            var bytes=pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress).GetReader();
            bytes.Offset=checked((int)resource.Offset);
            var actual=bytes.ReadBytes(bytes.ReadInt32());
            var expected=File.ReadAllBytes(Path.Combine(root.FullName,"Steward/data/common-glamour.json"));
            check(actual.SequenceEqual(expected),"Compiled plugin embeds the exact reviewed common glamour list");
        }
        using var bundle=JsonDocument.Parse(assembly.GetManifestResourceStream("CommonGlamourBundle")??throw new Exception("Bundled list missing from tests"));
        using var evidence=JsonDocument.Parse(assembly.GetManifestResourceStream("CommonGlamourEvidence")??throw new Exception("Curation evidence missing from tests"));
        var candidates=evidence.RootElement.GetProperty("catalogue").GetProperty("rows").EnumerateArray()
            .ToDictionary(c=>c.GetProperty("facts").GetProperty("id").GetUInt32(),c=>c.GetProperty("facts"));
        var ids=new HashSet<uint>();
        foreach(var entry in bundle.RootElement.GetProperty("items").EnumerateArray())
        {
            var id=entry.GetProperty("itemId").GetUInt32();var name=entry.GetProperty("name").GetString()!;
            check(ids.Add(id),$"Unique curated game ID: {id}");
            var facts=candidates[id];
            // Isolate the list's protection even after a future dungeon/source index resolves Ordinary.
            var item=new ItemFacts(id,name,true,facts.GetProperty("rarity").GetByte(),facts.GetProperty("level").GetInt32(),
                facts.GetProperty("marketable").GetBoolean(),facts.GetProperty("armoire").GetBoolean(),SourceKind.Ordinary,
                facts.GetProperty("category").GetUInt32(),DesignedForGlamour:facts.GetProperty("ilvl").GetUInt32()==1);
            var stock=new Stock(id,false,1,0,0);var rules=new RuleSettings();
            var proof=new Evidence{ReferencesReady=true,GearsetsReady=true,DresserKnown=true,ArmoireKnown=true};
            if(item.Marketable)proof.Prices[(id,false)]=new(id,false,1,1000,now,"Test-only fresh competitor");
            Decision Judge()=>Rules.Judge(item,stock,rules,proof,1,now);
            check(Judge().Outcome==Outcome.Dispose,$"Other static rules do not conceal curated protection: {name}");
            proof.CuratedGlamour.Add(id);
            check(Judge().Outcome==Outcome.CollectDresser,$"Missing exact common glamour copy is collected: {name}");
            proof.DresserKnown=false;
            check(Judge().Outcome==Outcome.Wait,$"Unknown ownership cannot authorize an action: {name}");
            proof.DresserKnown=true;proof.Dresser.Add(id);
            check(Judge().Outcome==Outcome.Dispose,$"Confirmed collected common glamour copy permits surplus rules: {name}");
            proof.OmittedDresserItem=id;
            check(Judge().Outcome==Outcome.CollectDresser,$"Removing the last collected common glamour copy preserves its replacement: {name}");
        }
        check(ids.Count>10,"Frequency-based list replaces the ten-piece starter");
    }
}
