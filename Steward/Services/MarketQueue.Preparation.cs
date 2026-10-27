using FFXIVClientStructs.FFXIV.Client.Game;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue
{
    public string? DresserPreparationStatus {get;private set;}
    public bool DresserPreparationReady()
    {
        DresserPreparationStatus=null;
        if(!config.ExecutionEnabled||!config.Features.DresserCollection)return true;
        refreshProtection();
        var candidates=view.Items.ToArray().Where(s=>DresserSource.Category(s.Container)>=0&&
            view.DresserEligible(s.ItemId)&&previewJudge(s).Outcome==Outcome.CollectDresser).ToArray();
        var manager=InventoryManager.Instance();
        var observations=new List<DresserPreparationItem>();
        foreach(var source in candidates)
        {
            var inventory=manager==null?null:manager->GetInventoryContainer((InventoryType)source.Container);
            var item=inventory==null?null:inventory->GetInventorySlot(source.Slot);
            int? condition=InventoryView.ReadSlot(source.Container,source.Slot)==source&&item!=null
                ?(int)item->GetConditionPercentage():null;
            observations.Add(new(source,condition));
        }
        var result=DresserPreparation.Check(observations,manager==null?null:manager->GetInventoryItemCount(21800));
        if(result.Ready)return true;
        var reasons=new List<string>();
        if(result.RepairItems.Length>0)
        {
            var names=result.RepairItems.Take(8).Select(id=>view.Facts(id)?.Name??id.ToString());
            reasons.Add($"Mend {result.RepairItems.Length} item(s) to at least 100%: {string.Join(", ",names)}"+
                (result.RepairItems.Length>8?$" and {result.RepairItems.Length-8} more":""));
        }
        if(result.UnknownItems.Length>0)reasons.Add("Waiting for live inventory condition data");
        if(result.MissingPrisms)reasons.Add("Obtain glamour prisms");
        DresserPreparationStatus="Glamour collection waiting before storage. "+string.Join(". ",reasons)+
            ". Items remain protected; collection resumes automatically when ready.";
        notify(DresserPreparationStatus);
        return false;
    }
}
