using Dalamud.Plugin;
using Steward.Core;

namespace Steward.Services;

public sealed class TrackedInventories(IDalamudPluginInterface pi)
{
    public List<(ulong Owner,Stock Stock)> Items { get; private set; }=[];
    public string Status { get; private set; }="Not yet queried";
    public void Refresh(ulong character,IReadOnlySet<ulong> retainers,InventoryMemory memory)
    {
        Items=memory.Stocks(character).Where(r=>retainers.Contains(r.Owner)).ToList();
        if(character==0){Items=[];return;}
        try
        {
            if(!pi.GetIpcSubscriber<bool>("AllaganTools.IsInitialized").InvokeFunc())throw new InvalidOperationException("Allagan Tools is not ready");
            var owners=pi.GetIpcSubscriber<bool,HashSet<ulong>>("AllaganTools.GetCharactersOwnedByActive").InvokeFunc(true);
            List<string> errors=[];
            foreach(var owner in owners.Where(retainers.Contains))
            {
                if(memory.HasRetainer(character,owner))continue;
                try
                {
                    var rows=pi.GetIpcSubscriber<ulong,HashSet<ulong[]>>("AllaganTools.GetCharacterItems").InvokeFunc(owner);
                    memory.ObserveRetainer(character,owner,AllaganRetainerPreview.Parse(owner,rows),onlyIfMissing:true);
                }
                catch(Exception ex){errors.Add($"Retainer {owner}: {ex.Message}");}
            }
            Items=memory.Stocks(character).Where(r=>retainers.Contains(r.Owner)).ToList();memory.Flush();
            Status=$"Persistent retainer cache: {Items.Count} stacks. Allagan sorted bags are preview only; live reads replace them.";
            if(errors.Count>0)Status+=" Import holds: "+string.Join("; ",errors);
        }
        catch(Exception ex){Status="Using persistent retainer observations for preview/prefetch; Allagan Tools unavailable: "+ex.Message;}
    }
}
