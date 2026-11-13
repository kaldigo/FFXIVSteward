using Dalamud.Bindings.ImGui;
using Steward.Core;
using Steward.Services;
using Dalamud.Interface.ImGuiNotification;

namespace Steward;

public sealed partial class Plugin
{
    private bool overflowPass;
    private bool collectionPass;
    private int withdrawnAtSweepStart;
    private long nextCollectionAttempt;
    private long nextDresserAttempt;
    private bool postCleanupEntrustPass;
    private long nextPostCleanupEntrust;
    private void TryPostCleanupEntrust()
    {
        if(!storageRun.EntrustPending||postCleanupEntrustPass||!storageRun.Allows(player.ContentId,config.ExecutionEnabled)||
            executionFault||ownsPostprocess||market.Queue.Running||AutoDutyCoordination.OwnsTravel(pi)||Environment.TickCount64<nextPostCleanupEntrust)return;
        if(!market.CloseDresserForStorage())return;
        nextPostCleanupEntrust=Environment.TickCount64+15000;
        if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Entrust"))
        {
            storageRun.EntrustStarted();postCleanupEntrustPass=true;
            Record("Post-dresser native entrust pass requested; AutoRetainer owns plans and transfers, with no disposal, sales or collection withdrawals");
        }
        else Alert("Dresser cleanup finished; follow-up storage is waiting for AutoRetainer and a reachable summoning bell. Items remain safe in inventory.");
    }
    private readonly StorageRun storageRun=new();
    private bool finishingStorageRun;
    private void FinishStorageRun()
    {
        finishingStorageRun=true;
        if(!storageRun.Allows(player.ContentId,config.ExecutionEnabled)||executionFault||market.Queue.Running||ownsPostprocess)return;
        if(pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.FinishStorage").InvokeFunc(player.ContentId))
        {
            if(storageRun.StorageOnly||storageRun.ArmoireOnly)
            {
                void Report(string message)=>StorageAlert(message,false);
                if(!view.Evidence.ReferencesReady||!view.Evidence.GearsetsReady||!InventoryView.PlayerLoaded())
                    Report("Glamour storage held: inventory, gear sets or selected glamour references are unavailable. Check inventory readiness.");
                else if((storageRun.CheckedArmoire||storageRun.AttemptedArmoireItems.Count>0)&&!view.Evidence.ArmoireKnown)
                    Report("Armoire visit finished without verified cabinet contents. Check the armoire before retrying.");
                else if(view.Items.Any(stock=>storageRun.AttemptedArmoireItems.Contains(stock.ItemId)&&!view.Evidence.Armoire.Contains(stock.ItemId)))
                    Report("Some requested armoire pieces are still carried. Check the cabinet and the run log.");
                else if(!storageRun.RequestedStorage&&InventoryView.FreeBagSlots()<2)
                    Report("No collection transfer started. Retainer withdrawals need two empty bag slots.");
                else if(storageRun.RequestedStorage)
                {
                    var cabinet=storageRun.AttemptedArmoireItems.Count(view.Evidence.Armoire.Contains);
                    Report($"Glamour storage finished. Confirmed {storageRun.Withdrawn} retainer withdrawals and {storageRun.Deposited} dresser deposits. {cabinet} requested armoire pieces verified in storage.");
                }
                else if(view.Retainers.Count==0||view.Retainers.Any(id=>config.Storage.RetainerEnabled(id)&&!inventoryMemory.HasRetainer(player.ContentId,id)))
                    Report("No eligible glamour storage found in observed inventory. Some retainer inventories have not been observed.");
                else Report("No eligible glamour items found to store in carried inventory or the saved inventories of included retainers.");
            }
            storageRun.End();finishingStorageRun=false;
            Record("Storage run completed: dresser closed, native entrust finished or unnecessary, AutoRetainer handoff acknowledged");
        }
    }
    private void BeginStorageRun(bool authorized)
    {
        if(AutoDutyCoordination.OwnsTravel(pi)){storageRun.End();return;}
        if(authorized&&!storageRun.Allows(player.ContentId,config.ExecutionEnabled))
        {
            finishingStorageRun=false;
            nextPostCleanupEntrust=0;
            nextCollectionAttempt=nextDresserAttempt=nextOverflowAttempt=nextAutomaticWork=0;
        }
        storageRun.Begin(player.ContentId,authorized);
    }
    private bool CollectionNeeded(ulong character)=>CollectionNeededForRetainer(character,0);
    private bool CollectionNeededForRetainer(ulong character,ulong retainer)
    {
        if(!config.Features.DresserCollection&&!config.Features.ArmoireStorage)return false;
        if(!player.IsLoaded||character==0||character!=player.ContentId||attached!=character||!config.Connected||!config.ExecutionEnabled||executionFault||InventoryView.FreeBagSlots()<2)return false;
        RefreshProtection();
        return CollectionNeededFromView(retainer);
    }
    private bool CollectionNeededFromView(ulong retainer=0)
    {
        var waiting=view.Items.Where(s=>Judge(s).Outcome==Outcome.CollectDresser).Select(s=>s.ItemId).Distinct().Count();
        foreach(var stock in CollectionRetainerStocks(retainer))
        {
            if(view.Items.Any(s=>s.ItemId==stock.ItemId))continue;
            var outcome=Judge(stock).Outcome;
            if((!storageRun.ArmoireOnly||outcome==Outcome.CollectArmoire)&&
                outcome is (Outcome.CollectArmoire or Outcome.CollectDresser)&&config.Features.Enabled(outcome)&&
                StoragePolicy.CanWithdraw(outcome,view.Evidence.DresserFreeSlots,config.Rules.DresserEmptySlots,waiting,0))return true;
        }
        return false;
    }
    private IEnumerable<Stock> CollectionRetainerStocks(ulong retainer=0)
    {
        if(storageRun.RetainerSweepStalled)return [];
        var containers=InventoryView.RetainerTypes().Select(t=>(uint)t).ToHashSet();
        return tracked.Items.Where(r=>(retainer==0||r.Owner==retainer)&&config.Storage.RetainerEnabled(r.Owner)&&view.Retainers.Contains(r.Owner)&&
            (containers.Contains(r.Stock.Container)||AllaganRetainerPreview.IsPreview(r.Stock.Container))).Select(r=>r.Stock);
    }
    // Planning hints only. Unknown snapshots permit an inspection. Every transfer still uses the live policy.
    private uint[]? CachedBagStacks(ulong character,ulong retainer)
    {
        if(!player.IsLoaded||character==0||character!=player.ContentId)return null;
        return inventoryMemory.ObservedBags(character,retainer)?.SelectMany(s=>new[]{s.ItemId,s.Hq?1u:0u,s.Quantity}).ToArray();
    }

    private uint[] EntrustBlockedItems(ulong character,ulong retainer)
    {
        if(character!=view.Character||!player.IsLoaded||!InventoryView.PlayerLoaded())return [];
        return view.Items.GroupBy(s=>s.ItemId)
            .Where(group=>group.All(s=>view.StorageBlock(s,retainer,nativeEntrust:true)!=null||!StorageCandidate(Judge(s))))
            .Select(group=>group.Key).ToArray();
    }
    private bool RetainerVisitNeeded(ulong character,ulong retainer,string operation,int listed)
    {
        if(!player.IsLoaded||character!=player.ContentId||view.Character!=character)return true;
        if(!InventoryView.PlayerLoaded()||!view.Evidence.ReferencesReady||!view.Evidence.GearsetsReady)return true;
        var bags=inventoryMemory.ObservedBags(character,retainer);
        if(operation=="Disposal")
        {
            if(!CleanupRetainerAllowed(character,retainer))return false;
            return bags==null||bags.Any(stock=>Judge(stock).Outcome==Outcome.Dispose&&
                pi.GetIpcSubscriber<uint,uint,bool>("AutoRetainer.InventoryPolicy.DisposalSelected").InvokeFunc(stock.ItemId,stock.Quantity));
        }
        if(operation=="Overflow")
        {
            if(!config.Storage.RetainerEnabled(retainer)||!config.Features.OverflowStorage)return false;
            return (bags==null||bags.Count<175)&&view.Items.Any(s=>s.Quantity==1&&Judge(s).Outcome==Outcome.StoreRetainer&&
                (bags==null||!view.IsUnique(s.ItemId)||!bags.Any(x=>x.ItemId==s.ItemId)));
        }
        if(operation is not ("List" or "Market" or "Review" or "Reprice"))return true;
        if(!config.SalesEnabledFor(retainer))return false;
        if(listed<0||listed>20)return true;
        if(operation=="Review"||operation=="Reprice")return listed>0;
        if(operation=="Market"&&listed>0&&(config.Features.Repricing||config.Features.ListingReturns||config.Features.Rotation))return true;
        if(!config.Features.NewListings||listed>=20)return false;
        return bags==null||view.Items.Where(s=>s.Container<4).Concat(bags).Any(s=>JudgeForRetainer(s,retainer).Outcome==Outcome.Market);
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
    private void Alert(string message)=>StorageAlert(message,true);
    private void StorageAlert(string message,bool throttle)
    {
        var key=player.ContentId+"/"+message;
        if(throttle&&storageAlerts.TryGetValue(key,out var last)&&Environment.TickCount64-last<60000)return;
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
        if(AutoDutyCoordination.OwnsTravel(pi)){storageRun.End();return true;}
        if(!storageRun.Allows(player.ContentId,config.ExecutionEnabled))return false;
        if(!player.IsLoaded||attached!=player.ContentId||!config.Connected||!config.ExecutionEnabled||executionFault||market.Queue.Running||ownsPostprocess||overflowPass||collectionPass)return false;
        if(finishingStorageRun){FinishStorageRun();return true;}
        // Once collection is finished, retry only the requested storage pass; closing
        // the dresser must not turn lost live ownership into another dresser visit.
        if(storageRun.EntrustPending&&nextPostCleanupEntrust>0){TryPostCleanupEntrust();return true;}
        // Existing player/armoury items must not suppress missing-item retainer collection.
        // Deposit a carried batch while the destination is open before closing it for another sweep.
        if(market.DresserOpen&&!storageRun.ArmoireOnly&&!storageRun.CleanupOnly&&config.Features.DresserCollection&&
            view.Items.Any(stock=>NeedsVisit(stock,false)&&Judge(stock).Outcome==Outcome.CollectDresser))return false;
        if(!storageRun.CleanupOnly&&(config.Features.DresserCollection||config.Features.ArmoireStorage)&&InventoryView.FreeBagSlots()>=2&&Environment.TickCount64>=nextCollectionAttempt&&CollectionNeededFromView())
        {
            if(!market.CloseDresserForStorage())return true;
            nextCollectionAttempt=Environment.TickCount64+60000;
            withdrawnAtSweepStart=storageRun.Withdrawn;
            collectionPass=true;
            try
            {
                if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Collect"))
                {storageRun.RequestedStorage=true;Record("Collection retainer pass requested; existing player gear does not block withdrawals");return true;}
                collectionPass=false;nextCollectionAttempt=Environment.TickCount64+5000;Alert("Protected items on retainers are waiting for collection. Move near a summoning bell and keep free inventory space.");
            }
            catch{collectionPass=false;throw;}
        }
        if(storageRun.CheckedArmoire&&!view.Evidence.ArmoireKnown)
        {
            RefreshProtection();
            if(view.Evidence.ArmoireKnown)return true;
            if(!storageRun.StorageOnly&&!storageRun.ArmoireOnly)Alert("Armoire contents could not be verified. Collection is paused for this pass.");
            FinishStorageRun();
            return true;
        }
        if(storageRun.CleanupOnly && market.DresserOpen) return false;
        // Rules need live cabinet ownership before judging a missing dresser copy.
        // A candidate on a retainer can require this visit even with empty player bags.
        bool checkCabinet=config.Features.ArmoireStorage&&!storageRun.CleanupOnly&&!storageRun.CheckedArmoire&&!view.Evidence.ArmoireKnown&&
            (InventoryView.FreeBagSlots()>=2&&CollectionRetainerStocks().Any(s=>NeedsVisit(s,true))||
             !storageRun.ArmoireOnly&&config.Features.DresserCollection&&NeedsCollectionVisit(false));
        if(config.Features.ArmoireStorage&&(checkCabinet||NeedsCollectionVisit(true)))
        {
            if(!market.CloseDresserForStorage())return true;
            var candidates = ArmoireCandidates().Distinct().ToArray();
            if((checkCabinet||candidates.Length>0) && pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Armoire"))
            {
                storageRun.CheckedArmoire=true;
                storageRun.RequestedStorage=true;
                storageRun.AttemptedArmoireItems.UnionWith(candidates);
                if(checkCabinet)Record("Opening armoire to verify collection ownership before withdrawal or deposit");
                Record("Armoire visit requested for carried items: " + string.Join(", ", candidates));
                return true;
            }
            Alert("Armoire route could not start. Check AutoRetainer and the available travel route.");
            nextAutomaticWork=Environment.TickCount64+5000;
            return true;
        }
        if(!storageRun.ArmoireOnly&&!storageRun.CleanupOnly&&config.Features.DresserCollection&&
            !config.Features.ArmoireStorage&&!view.Evidence.ArmoireKnown&&NeedsCollectionVisit(false))
        {
            Alert("Dresser storage needs current armoire contents. Open the armoire once and retry, or enable armoire storage for automatic visits.");
            storageRun.RequestedStorage=true;
            FinishStorageRun();
            return true;
        }
        if(!storageRun.ArmoireOnly&&!storageRun.CleanupOnly&&!market.DresserOpen&&Environment.TickCount64>=nextDresserAttempt&&
            (config.Features.DresserCollection&&NeedsCollectionVisit(false)))
        {
            if(config.Features.DresserCollection&&!market.DresserPreparationReady())
            {status=market.DresserPreparationStatus!;return true;}
            nextDresserAttempt=Environment.TickCount64+60000;
            if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,"Dresser"))
            {storageRun.RequestedStorage=true;Record("Glamour dresser visit requested");return true;}
            nextDresserAttempt=Environment.TickCount64+5000;
            Alert("Glamour collection is waiting for AutoRetainer's dresser route. Check its travel dependencies; protected items remain safe.");
        }
        if(!storageRun.ArmoireOnly&&!storageRun.CleanupOnly&&config.Features.OverflowStorage&&view.Items.Any(s=>Judge(s).Outcome==Outcome.StoreRetainer)&&Environment.TickCount64>=nextOverflowAttempt)
        {StartOverflow();return overflowPass;}
        return false;
    }
    private bool StorageWorkRemaining()=>
        !storageRun.CleanupOnly&&(config.Features.ArmoireStorage||config.Features.DresserCollection)&&InventoryView.FreeBagSlots()>=2&&CollectionNeededFromView()||
        config.Features.ArmoireStorage&&NeedsCollectionVisit(true)||
        !storageRun.ArmoireOnly&&!storageRun.CleanupOnly&&config.Features.DresserCollection&&NeedsCollectionVisit(false)||
        !storageRun.ArmoireOnly&&!storageRun.CleanupOnly&&config.Features.OverflowStorage&&view.Items.Any(s=>Judge(s).Outcome==Outcome.StoreRetainer);
    private IEnumerable<uint> ArmoireCandidates() => view.Items
        .Where(stock => !storageRun.AttemptedArmoireItems.Contains(stock.ItemId) && NeedsVisit(stock,true))
        .Select(stock => stock.ItemId);
    private bool NeedsCollectionVisit(bool armoire)=>armoire ? ArmoireCandidates().Any() : view.Items.Concat(InventoryView.FreeBagSlots()>=2?CollectionRetainerStocks():Enumerable.Empty<Stock>())
        .Any(stock=>NeedsVisit(stock,false));
    private bool NeedsVisit(Stock stock,bool armoire)
    {
        if(view.Facts(stock.ItemId) is not {} item||!armoire&&!view.DresserEligible(item.Id)||
            !StoragePolicy.NeedsCollectionVisit(item,stock,config.Rules,view.Evidence,armoire))return false;
        var outcome=Judge(stock).Outcome;
        return outcome==(armoire?Outcome.CollectArmoire:Outcome.CollectDresser)||
            outcome==Outcome.Wait&&(!view.Evidence.ArmoireKnown||!armoire&&!view.Evidence.DresserKnown);
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
        ImGui.TextWrapped("Set storage destinations, categories, item lists and quantities in AutoRetainer. Steward checks protections before deposits; it does not keep a second assignment list.");
        foreach(var (id,name) in view.RetainerNames.OrderBy(p=>p.Value))
        {
            var enabled=config.Storage.RetainerEnabled(id);
            if(ImGui.Checkbox("Manage stored items: "+name,ref enabled)){if(enabled)config.Storage.ExcludedRetainers.Remove(id);else config.Storage.ExcludedRetainers.Add(id);SavePolicy();}
        }
        DrawCategorySelection("Never store these categories",config.Storage.ExcludedCategories,"None selected","These stay with you even if nonmarketable. Triple Triad cards are always excluded.");
        ImGui.TextWrapped("Triple Triad cards are never stored. Unlearned or unverified unlocks stay available to use. Storage exclusions do not grant disposal permission.");
        var reserve=config.Rules.DresserEmptySlots;
        if(ImGui.SliderInt("Leave empty dresser slots",ref reserve,0,800)){config.Rules.DresserEmptySlots=reserve;SavePolicy();}
        ImGui.TextWrapped("Armoire items do not use this allowance. Protected dresser overflow goes to retainers and remains protected until collection is confirmed.");
        var cleanup=config.CleanDresser;
        if(ImGui.Checkbox("Clean dresser using all protection rules",ref cleanup)){config.CleanDresser=cleanup;SavePolicy();}
        ImGui.TextWrapped("Cleanup runs when visiting for collection or when you request a dresser run; it does not add a trip to venture or entrust runs.");
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
