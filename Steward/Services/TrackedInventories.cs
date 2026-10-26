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
            List<(ulong,Stock)> items=[];
            foreach(var owner in owners)
                foreach(var row in pi.GetIpcSubscriber<ulong,HashSet<ulong[]>>("AllaganTools.GetCharacterItems").InvokeFunc(owner))
                {
                    if(row.Length<25)throw new InvalidDataException("Unsupported Allagan Tools numeric inventory schema");
                    if(row[2]==0||row[3]==0)continue;
                    // CriticalCommonLib.InventoryItem.ToNumeric(): container, slot, item, quantity, flags at 6.
                    items.Add((owner,new((uint)row[2]%1000000,(row[6]&1)!=0,checked((uint)row[3]),checked((uint)row[0]),checked((ushort)row[1]))));
                }
            foreach(var owner in owners.Where(retainers.Contains))
                memory.ObserveRetainer(character,owner,items.Where(r=>r.Item1==owner&&r.Item2.Container is >=10000 and <=10006).Select(r=>r.Item2),onlyIfMissing:true);
            Items=memory.Stocks(character).Where(r=>retainers.Contains(r.Owner)).ToList();memory.Flush();Status=$"Allagan Tools: {owners.Count} tracked owners, {items.Count} cached stacks. Preview only; actions use live slots.";
        }
        catch(Exception ex){Status="Using persistent retainer observations for preview/prefetch; Allagan Tools unavailable: "+ex.Message;}
    }
}
