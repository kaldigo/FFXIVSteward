using FFXIVClientStructs.FFXIV.Client.Game;
using Steward.Core;

namespace Steward.Services;

public sealed unsafe partial class MarketQueue
{
    public bool DresserOpen=>ui.Get("MiragePrismPrismBox")!=null||ui.Get("MiragePrismPrismBoxCrystallize")!=null;
    public bool CloseDresserForStorage()
    {
        if(!config.ExecutionEnabled)return false;
        if(Queue.Running||ui.Visible("SelectYesno")||ui.Visible("ContextMenu")||ui.Visible("InputNumeric")||ui.Visible("RetainerSell")||ui.Visible("MiragePrismPrismSetConvert")||ui.Visible("MiragePrismPrismSetConvertC"))return false;
        ui.Close("MiragePrismPrismBoxCrystallize");ui.Close("MiragePrismPrismBox");return !DresserOpen;
    }
    public sealed record CleanupCandidate(uint ItemId,string Name,string Action,string Reason,int Quantity=1);
    public IReadOnlyList<CleanupCandidate> DresserCleanupCandidates()
    {
        var result=new List<CleanupCandidate>();
        var mm=MirageManager.Instance();
        if(mm==null||!mm->PrismBoxLoaded||!view.Evidence.DresserKnown||!view.Evidence.ArmoireKnown)return result;
        foreach(var id in view.Evidence.Dresser.Order().ToArray())
        {
            var facts=view.Facts(id);var count=0;Decision? candidate=null;
            for(var remaining=view.DresserCopies(id);remaining>0;remaining--)
            {
                var decision=StoragePolicy.WithoutDresserCopy(view.Evidence,id,remaining,
                    ()=>previewJudge(new(id,false,1,uint.MaxValue,0)));
                if(!config.Features.CanRestore(decision.Outcome)||!StoragePolicy.Restore(decision.Outcome,true,facts?.ArmoireEligible==true&&!view.Evidence.Armoire.Contains(id)))break;
                count++;candidate=decision;
            }
            if(candidate!=null)result.Add(new(id,facts?.Name??id.ToString(),candidate.Outcome==Outcome.CollectArmoire?"Restore for armoire migration":"Restore for normal inventory rules",candidate.Reason,count));
        }
        return result;
    }
    public IReadOnlyList<string> PreviewDresserCleanup()
    {
        if(!view.Evidence.DresserKnown||!view.Evidence.ArmoireKnown)return ["Open the dresser and armoire to load current collection evidence first."];
        var candidates=DresserCleanupCandidates();
        return candidates.Count==0?["No eligible dresser removals; protected last copies remain in place."]:
            candidates.Select(c=>$"{c.Name} [{c.ItemId}] x{c.Quantity}: {c.Action} — {c.Reason}").ToArray();
    }
    private void EnqueueOverflow(ulong character,ulong retainer)
    {
        if(!config.Features.OverflowStorage||!config.Storage.RetainerEnabled(retainer))return;
        var completed=0;
        var label=retainer.ToString();
        var rm=RetainerManager.Instance();
        if(rm!=null)foreach(var r in rm->Retainers)if(r.RetainerId==retainer)label=r.NameString;
        foreach(var source in view.Items.ToArray().Where(s=>s.Quantity==1&&view.StorageBlock(s,retainer)==null&&previewJudge(s).Outcome==Outcome.StoreRetainer))
        {
            protectCollection(source.ItemId); // Synchronous durable hold precedes any native transfer.
            var sent=false;var attempted=false;long beforePlayer=0,beforeRetainer=0;
            Queue.Add(new("Store protected overflow "+source.ItemId,()=>
            {
                if(!config.ExecutionEnabled||view.Character!=character)return StepResult.Failed;
                var manager=RetainerManager.Instance();
                if(manager==null||manager->LastSelectedRetainerId!=retainer||!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(character))return StepResult.Failed;
                if(!InventoryView.PlayerLoaded()||InventoryView.RetainerTypes().Any(t=>!InventoryView.Loaded((uint)t)))return StepResult.Failed;
                List<Stock> destination=[];foreach(var type in InventoryView.RetainerTypes())InventoryView.Read(type,destination);
                if(!sent)
                {
                    if(InventoryView.ReadSlot(source.Container,source.Slot)!=source)return StepResult.Failed;
                    if(!config.Features.OverflowStorage||view.StorageBlock(source,retainer)!=null||judge(source).Outcome!=Outcome.StoreRetainer)return StepResult.Completed;
                    var im=InventoryManager.Instance();var empty=false;
                    foreach(var type in InventoryView.RetainerTypes())
                    {var bag=im->GetInventoryContainer(type);for(var i=0;i<bag->Size;i++)if(bag->GetInventorySlot(i)->ItemId==0)empty=true;}
                    // Unique conflicts are a normal reason to try a different retainer.
                    if(!empty){notify("Retainer "+label+" is full; protected overflow will be tried with another retainer.");return StepResult.Completed;}
                    beforePlayer=view.Items.Where(s=>s.ItemId==source.ItemId&&s.Hq==source.Hq).Sum(s=>(long)s.Quantity);
                    beforeRetainer=destination.Where(s=>s.ItemId==source.ItemId&&s.Hq==source.Hq).Sum(s=>(long)s.Quantity);
                    var payload=System.Text.Json.JsonSerializer.Serialize(new{source.Container,source.Slot,source.ItemId,source.Quantity,source.Hq,Retainer=retainer});
                    attempted=true;
                    if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Store").InvokeFunc(character,payload))
                    {
                        if(pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.HasFault").InvokeFunc(character))return StepResult.Failed;
                        attempted=false;return StepResult.Failed; // Known rejection without a native transaction; skip this item.
                    }
                    sent=true;return StepResult.Waiting;
                }
                var afterPlayer=view.Items.Where(s=>s.ItemId==source.ItemId&&s.Hq==source.Hq).Sum(s=>(long)s.Quantity);
                var afterRetainer=destination.Where(s=>s.ItemId==source.ItemId&&s.Hq==source.Hq).Sum(s=>(long)s.Quantity);
                if(afterPlayer==beforePlayer&&afterRetainer==beforeRetainer)return StepResult.Waiting;
                if(!StoragePolicy.TransferConfirmed(beforePlayer,afterPlayer,beforeRetainer,afterRetainer,1))return StepResult.Waiting;
                if(!StockTransition.Removed(source,InventoryView.ReadSlot(source.Container,source.Slot),1,true))return StepResult.Failed;
                if(pi.GetIpcSubscriber<bool>("AutoRetainer.InventoryPolicy.SubmissionPending").InvokeFunc())return StepResult.Waiting;
                completed++;history("Confirmed protected overflow storage: "+source.ItemId+" → "+label+". Collection hold retained.");
                return StepResult.Completed;
            },TimeoutMs:12000,CanSkip:()=>!attempted));
        }
        Queue.Add(new("Overflow storage summary",()=>
        {if(completed>0)notify($"Stored {completed} protected glamour item(s) with {label}; they remain protected while waiting for collection.");return StepResult.Completed;},CanSkip:()=>true));
    }
}
