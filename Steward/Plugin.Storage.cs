using Dalamud.Bindings.ImGui;
using Steward.Core;
using Steward.Services;
using Dalamud.Interface.ImGuiNotification;

namespace Steward;

public sealed partial class Plugin
{
    private bool overflowPass;
    private bool collectionPass;
    private long nextCollectionAttempt;
    private long nextDresserAttempt;
    private bool cleanupVisited;
    private bool CollectionNeeded(ulong character)
    {
        if(!config.Features.DresserCollection&&!config.Features.ArmoireStorage)return false;
        if(!player.IsLoaded||character==0||character!=player.ContentId||attached!=character||!config.Connected||!config.ExecutionEnabled||executionFault||InventoryView.FreeBagSlots()<2)return false;
        RefreshProtection();
        var waiting=view.Items.Where(s=>Judge(s).Outcome==Outcome.CollectDresser).Select(s=>s.ItemId).Distinct().Count();
        var containers=InventoryView.RetainerTypes().Select(t=>(uint)t).ToHashSet();
        return tracked.Items.Any(r=>view.Retainers.Contains(r.Owner)&&(containers.Contains(r.Stock.Container)||AllaganRetainerPreview.IsPreview(r.Stock.Container))&&
            !view.Items.Any(s=>s.ItemId==r.Stock.ItemId)&&(Judge(r.Stock).Outcome is Outcome.CollectArmoire or Outcome.CollectDresser)&&config.Features.Enabled(Judge(r.Stock).Outcome)&&
            StoragePolicy.CanWithdraw(Judge(r.Stock).Outcome,view.Evidence.DresserFreeSlots,config.Rules.DresserEmptySlots,waiting,0));
    }
    private long nextOverflowAttempt;
    private readonly Dictionary<string,long> storageAlerts=[];
    private readonly List<string> cleanupPreview=[];
    private bool OverflowNeeded(ulong character)
    {
        if(!player.IsLoaded||character==0||character!=player.ContentId||attached!=character||!config.Connected||!config.ExecutionEnabled||executionFault)return false;
        RefreshProtection();
        return config.Features.OverflowStorage&&view.Items.Any(s=>Judge(s).Outcome==Outcome.StoreRetainer);
    }
    private void Alert(string message)
    {
        var key=player.ContentId+"/"+message;
        if(storageAlerts.TryGetValue(key,out var last)&&Environment.TickCount64-last<60000)return;
        storageAlerts[key]=Environment.TickCount64;
        try{chat.Print("[Steward] "+message);}catch(Exception ex){log.Error(ex,"Could not show storage alert");}
        try{notifications.AddNotification(new Notification{Title="Steward storage",Content=message,Type=NotificationType.Info,InitialDuration=TimeSpan.FromSeconds(12)});}catch(Exception ex){log.Error(ex,"Could not show storage notification");}
        Record(message);
    }
    private void ProtectCollection(uint id)
    {
        if(!player.IsLoaded||player.ContentId==0||view.Character!=player.ContentId)throw new InvalidOperationException("Collection hold has no live character");
        if(!config.PendingCollection.TryGetValue(player.ContentId,out var held))config.PendingCollection[player.ContentId]=held=[];
        view.Evidence.PendingCollection.Add(id);
        if(held.Add(id))Save(); // A failed write stops execution and leaves the in-memory protection active.
    }
    private void UpdateCollectionHolds()
    {
        if(view.Character==0)return;
        if(config.ExecutionEnabled&&config.Features.OverflowStorage)
            foreach(var id in view.Items.Where(s=>Judge(s).Outcome==Outcome.StoreRetainer).Select(s=>s.ItemId).Distinct().ToArray())ProtectCollection(id);
        if(!config.PendingCollection.TryGetValue(view.Character,out var held)||!view.Evidence.DresserKnown||!view.Evidence.ArmoireKnown)return;
        var confirmed=held.Where(id=>view.Facts(id) is { } facts &&
            StoragePolicy.CollectionConfirmed(facts,view.Evidence)).ToArray();
        if(confirmed.Length==0)return;
        CollectionHolds.Release(held,confirmed,Save);
        foreach(var id in confirmed)view.Evidence.PendingCollection.Remove(id);
    }
    private bool AutomaticStorageWork()
    {
        if(!player.IsLoaded||attached!=player.ContentId||!config.Connected||!config.ExecutionEnabled||executionFault||market.Queue.Running||ownsPostprocess||overflowPass||collectionPass)return false;
        if(Environment.TickCount64>=nextCollectionAttempt&&CollectionNeeded(player.ContentId))
        {
            if(!market.CloseDresserForStorage())return false;
            nextCollectionAttempt=Environment.TickCount64+60000;
            collectionPass=true;
            try
            {
                if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Collect"))return true;
                collectionPass=false;nextCollectionAttempt=Environment.TickCount64+5000;Alert("Protected items on retainers are waiting for collection. Move near a summoning bell and keep free inventory space.");
            }
            catch{collectionPass=false;throw;}
        }
        if(config.Features.ArmoireStorage&&view.Items.Any(s=>Judge(s).Outcome==Outcome.CollectArmoire))
        {
            if(!market.CloseDresserForStorage())return false;
            if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Armoire"))return true;
        }
        if(market.DresserOpen)cleanupVisited=true;
        if(!market.DresserOpen&&Environment.TickCount64>=nextDresserAttempt&&
            (config.Features.DresserCollection&&view.Items.Any(s=>Judge(s).Outcome==Outcome.CollectDresser)||config.CleanDresser&&!cleanupVisited))
        {
            nextDresserAttempt=Environment.TickCount64+60000;
            if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Dresser"))return true;
            Alert("Glamour collection is waiting for AutoRetainer's dresser route. Check its travel dependencies; protected items remain safe.");
        }
        if(config.Features.OverflowStorage&&view.Items.Any(s=>Judge(s).Outcome==Outcome.StoreRetainer)&&Environment.TickCount64>=nextOverflowAttempt)
        {StartOverflow();return overflowPass;}
        return false;
    }
    private void StartOverflow()
    {
        if(!config.Features.OverflowStorage)return;
        if(!player.IsLoaded||attached!=player.ContentId||!config.Connected||!config.ExecutionEnabled||executionFault||market.Queue.Running||ownsPostprocess||overflowPass)return;
        nextOverflowAttempt=Environment.TickCount64+60000;
        if(!market.CloseDresserForStorage())
        {Alert("Storage is waiting for the current confirmation window to close; protected items remain safe.");nextOverflowAttempt=Environment.TickCount64+5000;return;}
        Alert("Dresser space limit reached. Protected overflow will be stored with retainers, without disposal.");
        overflowPass=true;
        try
        {
            if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Overflow"))
            {overflowPass=false;nextOverflowAttempt=Environment.TickCount64+5000;Alert("Overflow storage is waiting for an available summoning bell. Move near a bell; protected items will remain safe until storage can run.");}
        }
        catch{overflowPass=false;throw;}
    }
    private void DrawStorageSettings()
    {
        Setting("Store protected overflow in retainers",config.Features.OverflowStorage,v=>config.Features.OverflowStorage=v,"Disabling storage leaves overflow protected in inventory.");
        var reserve=config.Rules.DresserEmptySlots;
        if(ImGui.SliderInt("Leave empty dresser slots",ref reserve,0,800)){config.Rules.DresserEmptySlots=reserve;SavePolicy();}
        ImGui.TextWrapped("Armoire items do not use this allowance. Protected dresser overflow goes to retainers and remains protected until collection is confirmed.");
        var cleanup=config.CleanDresser;
        if(ImGui.Checkbox("Clean dresser using all protection rules",ref cleanup)){config.CleanDresser=cleanup;cleanupVisited=false;SavePolicy();}
        ImGui.TextWrapped("Cleanup restores items that no rule needs and moves eligible pieces to the armoire. Required last copies, unknown sources, exclusions, gear sets and AutoRetainer protections are preserved.");
        if(ImGui.Button("Preview dresser cleanup"))
        {
            cleanupPreview.Clear();RefreshProtection();
            foreach(var entry in market.PreviewDresserCleanup())cleanupPreview.Add(entry);
        }
        ImGui.SameLine();ImGui.BeginDisabled(!config.ExecutionEnabled||executionFault||!config.Features.OverflowStorage);
        if(ImGui.Button("Store protected overflow now"))StartOverflow();
        ImGui.EndDisabled();
        foreach(var line in cleanupPreview)ImGui.TextWrapped(line);
        if(config.PendingCollection.TryGetValue(view.Character,out var pending))
            ImGui.TextWrapped($"Protected items waiting for collection: {pending.Count}. Dresser free slots: {view.Evidence.DresserFreeSlots?.ToString()??"unknown"}.");
    }
}
