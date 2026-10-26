using Dalamud.Game;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Steward.Core;

namespace Steward.Services;

public sealed class DungeonSources
{
    private readonly IDalamudPluginInterface pi;
    private readonly Configuration config;
    private readonly Action save;
    private readonly Queue<(uint Id,SourceKind Kind)> pending;
    private readonly Dictionary<uint,SourceKind> staged=[];
    private int complete;
    private long next;
    public string Status { get; private set; }="Dungeon sources not indexed";
    public DungeonSources(IDalamudPluginInterface pi,IDataManager data,Configuration config,Action save)
    {
        this.pi=pi;this.config=config;this.save=save;
        config.DutySources.Clear(); // Ordinary classifications are not published until the full fresh index is available.
        pending=new(data.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>(ClientLanguage.English)
            .Where(row=>row.ContentType.RowId is 2 or 5 or 28)
            .Select(row=>(row.RowId,Kind:row.ContentType.RowId==28 || row.Name.ToString().Contains("Coil of Bahamut",StringComparison.OrdinalIgnoreCase)?SourceKind.HighEnd:
                row.Name.ToString().Contains("Savage",StringComparison.OrdinalIgnoreCase)||row.HighEndDuty?SourceKind.SavageRaid:
                row.AllianceRoulette||row.QueueMaxPlayers==24?SourceKind.AllianceRaid:
                row.ContentType.RowId==2?SourceKind.NormalDungeon:row.NormalRaidRoulette?SourceKind.NormalRaid:SourceKind.HighEnd)));
    }
    public void Tick()
    {
        if(pending.Count==0||Environment.TickCount64<next)return;
        next=Environment.TickCount64+5000;
        try
        {
            var (id,kind)=pending.Peek();
            var items=pi.GetIpcSubscriber<uint,List<uint>>("GlamourLog.GetItemsFromContent").InvokeFunc(id);
            if(items==null)throw new InvalidDataException("Unavailable duty rewards");
            foreach(var item in items)staged[item]=AcquisitionSources.Merge(staged.GetValueOrDefault(item),kind);
            pending.Dequeue();complete++;Status=$"Dungeon acquisition index: {complete} checked, {pending.Count} pending (Glamour Log).";
            if(pending.Count==0){config.DutySources=new(staged);save();}
        }
        catch(Exception ex){Status="Dungeon source index waiting for Glamour Log: "+ex.Message;next=Environment.TickCount64+60000;}
    }
}
