using System.Text.Json;
using Dalamud.Plugin;
using Steward.Core;

namespace Steward.Services;

public sealed class ExternalImports
{
    private readonly string directory;
    private readonly Configuration config;
    private readonly InventoryView view;
    private readonly HashSet<uint> bundled=[];
    public string Status { get; private set; } = "Waiting for imports";
    public bool MarketPreferencesReady { get; private set; }
    public ExternalImports(IDalamudPluginInterface pi,Configuration config,InventoryView view)
    {
        directory=Directory.GetParent(pi.GetPluginConfigDirectory())!.FullName;this.config=config;this.view=view;
        using var stream=typeof(ExternalImports).Assembly.GetManifestResourceStream("Steward.data.common-glamour.json")??throw new InvalidDataException("Bundled glamour list missing");
        using var doc=JsonDocument.Parse(stream);foreach(var entry in doc.RootElement.GetProperty("items").EnumerateArray())bundled.Add(entry.GetProperty("itemId").GetUInt32());
    }
    public void Refresh()
    {
        MarketPreferencesReady=!config.ImportMarketBuddy;
        var errors=new List<string>();var references=new HashSet<uint>(view.NativeReferences);
        view.Evidence.CuratedGlamour=config.UseBundledGlamourList?bundled:[];
        var glamReady=!config.GlamaholicExpected;
        try
        {
            var path=Path.Combine(directory,"Glamaholic.json");
            if(File.Exists(path)){references.UnionWith(Imports.Glamaholic(File.ReadAllText(path)));glamReady=true;}
            else if(config.GlamaholicExpected)errors.Add("Glamaholic config not found");
        }
        catch(Exception ex){glamReady=false;errors.Add("Glamaholic: "+ex.Message);}
        view.Evidence.References=references;view.Evidence.ReferencesReady=glamReady&&view.NativeReferencesKnown;
        if(config.ImportMarketBuddy)
        {
            try{var path=Path.Combine(directory,"Marketbuddy.json");if(File.Exists(path)){config.Pricing=Imports.MarketBuddy(File.ReadAllText(path));MarketPreferencesReady=true;}else errors.Add("Market Buddy settings not found; market execution held");}
            catch(Exception ex){errors.Add("Market Buddy: "+ex.Message);}
        }
        try
        {
            var sales=Path.Combine(directory,"AllaganMarket","SaleItems.csv");
            if(File.Exists(sales))foreach(var line in File.ReadLines(sales).Skip(1).Where(l=>!string.IsNullOrWhiteSpace(l)))
            {
                var fields=line.Split(',');
                if(fields.Length<9)throw new InvalidDataException("Unexpected Allagan listing schema");
                if(uint.Parse(fields[1])==view.World)config.OwnRetainers.Add(ulong.Parse(fields[0]));
            }
            var path=Path.Combine(directory,"AllaganMarket","MarketPriceCache.csv");
            if(File.Exists(path))foreach(var quote in Imports.MarketCache(File.ReadAllText(path)))
            {
                var key=(quote.ItemId,quote.Hq);
                if(quote.World==view.World && (!view.Evidence.Prices.TryGetValue(key,out var current)||current.Observed<quote.Observed))view.Evidence.Prices[key]=quote;
            }
        }
        catch(Exception ex){errors.Add("Allagan Market cache: "+ex.Message);}
        Status=errors.Count==0?"Read-only imports refreshed":string.Join("; ",errors);
    }
    public static void ImportCommon(string path,RuleSettings settings)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(path));
        var ids=new HashSet<uint>();foreach(var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {var source=item.GetProperty("source").GetString();if(source==null||!Uri.TryCreate(source,UriKind.Absolute,out var uri)||uri.Host!="ffxiv.eorzeacollection.com")throw new InvalidDataException("Each curated item needs an Eorzea Collection source URL");ids.Add(item.GetProperty("itemId").GetUInt32());}
        if(ids.Contains(0))throw new InvalidDataException("Invalid item ID");settings.CommonGlamour=ids;
    }
}
