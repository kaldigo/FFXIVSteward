using Dalamud.Bindings.ImGui;
using Steward.Core;
using Dalamud.Interface.ImGuiNotification;

namespace Steward;

public sealed partial class Plugin
{
    private bool overflowPass;
    private long nextOverflowAttempt;
    private readonly Dictionary<string,long> storageAlerts=[];
    private readonly List<string> cleanupPreview=[];
    private bool OverflowNeeded(ulong character)
    {
        if(!player.IsLoaded||character==0||character!=player.ContentId||attached!=character||!config.Connected||!config.ExecutionEnabled||executionFault)return false;
        RefreshProtection();
        return view.Items.Any(s=>Judge(s).Outcome==Outcome.StoreRetainer);
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
        if(config.ExecutionEnabled)
            foreach(var id in view.Items.Where(s=>Judge(s).Outcome==Outcome.StoreRetainer).Select(s=>s.ItemId).Distinct().ToArray())ProtectCollection(id);
        if(!config.PendingCollection.TryGetValue(view.Character,out var held)||!view.Evidence.DresserKnown||!view.Evidence.ArmoireKnown)return;
        var confirmed=held.Where(id=>view.Facts(id) is { } facts &&
            StoragePolicy.CollectionConfirmed(facts,view.Evidence)).ToArray();
        if(confirmed.Length==0)return;
        CollectionHolds.Release(held,confirmed,Save);
        foreach(var id in confirmed)view.Evidence.PendingCollection.Remove(id);
    }
    private void AutomaticStorageWork()
    {
        if(!player.IsLoaded||attached!=player.ContentId||!config.Connected||!config.ExecutionEnabled||executionFault||market.Queue.Running||ownsPostprocess||overflowPass)return;
        if(view.Items.Any(s=>Judge(s).Outcome==Outcome.CollectArmoire))
        {
            if(!market.CloseDresserForStorage())return;
            if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Armoire"))return;
        }
        if(!view.Items.Any(s=>Judge(s).Outcome==Outcome.StoreRetainer)||Environment.TickCount64<nextOverflowAttempt)return;
        StartOverflow();
    }
    private void StartOverflow()
    {
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
        DrawReviewSettings();
        var reserve=config.Rules.DresserEmptySlots;
        if(ImGui.SliderInt("Leave empty dresser slots",ref reserve,0,800)){config.Rules.DresserEmptySlots=reserve;Save();}
        ImGui.TextWrapped("Armoire items do not use this allowance. Protected dresser overflow goes to retainers and remains protected until collection is confirmed.");
        var cleanup=config.CleanDresser;
        if(ImGui.Checkbox("Clean dresser using all protection rules",ref cleanup)){config.CleanDresser=cleanup;Save();}
        ImGui.TextWrapped("Cleanup restores items that no rule needs and moves eligible pieces to the armoire. Required last copies, unknown sources, exclusions, gear sets and AutoRetainer protections are preserved.");
        if(ImGui.Button("Preview dresser cleanup"))
        {
            cleanupPreview.Clear();RefreshProtection();
            foreach(var entry in market.PreviewDresserCleanup())cleanupPreview.Add(entry);
        }
        ImGui.SameLine();if(ImGui.Button("Store protected overflow now"))StartOverflow();
        foreach(var line in cleanupPreview)ImGui.TextWrapped(line);
        if(config.PendingCollection.TryGetValue(view.Character,out var pending))
            ImGui.TextWrapped($"Protected items waiting for collection: {pending.Count}. Dresser free slots: {view.Evidence.DresserFreeSlots?.ToString()??"unknown"}.");
    }
}
