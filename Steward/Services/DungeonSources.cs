using Dalamud.Game;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Steward.Services;

public sealed class DungeonSources
{
    private readonly IDalamudPluginInterface pi;
    private readonly Configuration config;
    private readonly Action save;
    private readonly Queue<uint> pending;
    private int complete;
    private long next;
    public string Status { get; private set; }="Dungeon sources not indexed";
    public DungeonSources(IDalamudPluginInterface pi,IDataManager data,Configuration config,Action save)
    {
        this.pi=pi;this.config=config;this.save=save;
        pending=new(data.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>(ClientLanguage.English)
            .Where(row=>row.ContentType.Value.Name.ToString()=="Dungeons").Select(row=>row.RowId));
    }
    public void Tick()
    {
        if(pending.Count==0||Environment.TickCount64<next)return;
        next=Environment.TickCount64+5000;
        try
        {
            var id=pending.Peek();
            var items=pi.GetIpcSubscriber<uint,List<uint>>("GlamourLog.GetItemsFromContent").InvokeFunc(id);
            foreach(var item in items)config.Sources.TryAdd(item,Core.SourceKind.Ordinary);
            pending.Dequeue();complete++;Status=$"Dungeon acquisition index: {complete} checked, {pending.Count} pending (Glamour Log).";
            if(complete%10==0||pending.Count==0)save();
        }
        catch(Exception ex){Status="Dungeon source index waiting for Glamour Log: "+ex.Message;next=Environment.TickCount64+60000;}
    }
}
