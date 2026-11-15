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
    private readonly Func<IEnumerable<string>> installedPlugins;
    private readonly Func<uint[]> designItems;
    public bool GlamaholicInstalled { get; private set; }
    public bool AetherloomInstalled { get; private set; }
    public ExternalImports(IDalamudPluginInterface pi,Configuration config,InventoryView view,Func<uint[]> designItems,Func<IEnumerable<string>> installedPlugins)
    {
        this.designItems=designItems;this.installedPlugins=installedPlugins;
        directory=Directory.GetParent(pi.GetPluginConfigDirectory())!.FullName;this.config=config;this.view=view;
        using var stream=typeof(ExternalImports).Assembly.GetManifestResourceStream("Steward.data.common-glamour.json")??throw new InvalidDataException("Bundled glamour list missing");
        using var doc=JsonDocument.Parse(stream);foreach(var entry in doc.RootElement.GetProperty("items").EnumerateArray())bundled.Add(entry.GetProperty("itemId").GetUInt32());
    }
    private readonly Dictionary<string,(DateTime At,long Length,object Value)> parsed=[];
    private T ReadCached<T>(string path,Func<string,T> parse)
    {
        var info=new FileInfo(path);
        if(!info.Exists){parsed.Remove(path);throw new FileNotFoundException("Import file is unavailable",path);}
        if(parsed.TryGetValue(path,out var previous)&&previous.At==info.LastWriteTimeUtc&&previous.Length==info.Length)return (T)previous.Value;
        var text=File.ReadAllText(path);var value=parse(text);
        // A file being replaced is retried instead of accepting a mixed import.
        var after=new FileInfo(path);
        if(after.LastWriteTimeUtc!=info.LastWriteTimeUtc||after.Length!=info.Length)throw new IOException("Import changed while being read");
        parsed[path]=(info.LastWriteTimeUtc,info.Length,value!);return value;
    }
    public List<string> RefreshReferences()
    {
        var filters=config.Rules.Glamour;
        var errors=new List<string>();var references=filters.NativePlates?new HashSet<uint>(view.NativeReferences):[];
        view.Evidence.CuratedGlamour=filters.EorzeaCollection?bundled:[];
        view.Evidence.ReferencesReady=false;
        view.Evidence.References=references;
        view.Evidence.GlamaholicKnown=false;view.Evidence.GlamaholicReferences=[];
        view.Evidence.AetherloomReferences=null;
        try
        {
            var installed=installedPlugins().ToHashSet(StringComparer.OrdinalIgnoreCase);
            GlamaholicInstalled=installed.Contains("Glamaholic");
            AetherloomInstalled=installed.Contains("Aetherloom");
        }
        catch(Exception ex)
        {
            if(filters.Glamaholic||filters.Aetherloom){errors.Add("Installed outfit sources unavailable: "+ex.Message);return errors;}
        }
        if(filters.Glamaholic&&GlamaholicInstalled)
        {
            try
            {
                var imported=ReadCached(Path.Combine(directory,"Glamaholic.json"),Imports.Glamaholic);
                references.UnionWith(imported);
                view.Evidence.GlamaholicReferences=imported;
                view.Evidence.GlamaholicKnown=true;
            }
            catch(Exception ex){errors.Add("Glamaholic: "+ex.Message);}
        }
        if(filters.Aetherloom&&AetherloomInstalled)
        {
            try
            {
                var ids=designItems() ?? throw new InvalidDataException("Design references unavailable");
                view.Evidence.AetherloomReferences=ids.ToHashSet();
                references.UnionWith(ids);
            }
            catch(Exception ex){errors.Add("Aetherloom: "+ex.Message);}
        }
        view.Evidence.References=references;view.Evidence.ReferencesReady=GlamourProtection.ReferencesReady(filters,view.NativeReferencesAvailable,!GlamaholicInstalled||view.Evidence.GlamaholicKnown,!AetherloomInstalled||view.Evidence.AetherloomReferences!=null);
        return errors;
    }
    public void Refresh()
    {
        MarketPreferencesReady=!config.ImportMarketBuddy;
        var errors=RefreshReferences();
        if(config.ImportMarketBuddy)
        {
            try{var path=Path.Combine(directory,"Marketbuddy.json");if(File.Exists(path)){config.Pricing=ReadCached(path,Imports.MarketBuddy);MarketPreferencesReady=true;}else errors.Add("Market Buddy settings not found; market execution held");}
            catch(Exception ex){errors.Add("Market Buddy: "+ex.Message);}
        }
        view.Evidence.AllaganPrices=[];
        try
        {
            var sales=Path.Combine(directory,"AllaganMarket","SaleItems.csv");
            if(File.Exists(sales))foreach(var line in ReadCached(sales,text=>text.Split('\n',StringSplitOptions.RemoveEmptyEntries)).Where(l=>!string.IsNullOrWhiteSpace(l)))
            {
                var fields=line.Split(',');
                if(fields[0].Trim().Equals("RetainerId",StringComparison.OrdinalIgnoreCase))continue;
                if(fields.Length<9)throw new InvalidDataException("Unexpected Allagan listing schema");
                if(uint.Parse(fields[1])==view.World)config.OwnRetainers.Add(ulong.Parse(fields[0]));
            }
            var path=Path.Combine(directory,"AllaganMarket","MarketPriceCache.csv");
            if(File.Exists(path))view.Evidence.AllaganPrices=ReadCached(path,Imports.MarketCache);
            foreach(var quote in view.Evidence.AllaganPrices)
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
