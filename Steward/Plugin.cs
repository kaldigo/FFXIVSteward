using System.Text.Json;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Steward.Core;
using Steward.Services;

namespace Steward;

public sealed partial class Plugin : IDalamudPlugin
{
    private const string Endpoint="Steward.InventoryPolicy.Evaluate";
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly IObjectTable objects;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly INotificationManager notifications;
    private readonly ITextureProvider textures;
    private readonly Configuration config;
    private readonly string statePath;
    private readonly InventoryView view;
    private readonly ExternalImports imports;
    private readonly MarketQueue market;
    private readonly DresserTrace dresserTrace;
    private readonly TrackedInventories tracked;
    private readonly InventoryMemory inventoryMemory;
    private readonly DungeonSources dungeonSources;
    private readonly AggregatePrices universalis;
    private static readonly HashSet<ulong> noPriceOwners=[];
    private IReadOnlySet<ulong> PriceOwners=>view.Retainers.Count==0?noPriceOwners:config.OwnRetainers;
    private readonly List<string> history=[];
    private readonly Dictionary<uint,bool?> protectionSnapshot=[];
    private readonly string historyPath;
    private long nextRefresh;
    private long nextImports;
    private uint[] priceCandidates=[];
    private long nextAutomaticWork;
    private ulong attached;
    private bool open=false;
    private bool disposed;
    private bool ownsPostprocess;
    private bool executionFault;
    private string status="Starting in dry-run mode";
    private string search="";
    private bool openingMarket;
    private bool openingInventory;
    private bool withdrawing;
    private long marketOpenDeadline;
    private long nextPostprocessRelease;
    private readonly Dictionary<string,long> recentDecisions=[];
    public Plugin(IDalamudPluginInterface pi,ICommandManager commands,IFramework framework,IPlayerState player,
        IObjectTable objects,IDataManager data,IGameGui gui,IMarketBoard board,IPluginLog log,IChatGui chat,INotificationManager notifications,ITextureProvider textures,IAddonLifecycle lifecycle,ISigScanner sigScanner,IGameInteropProvider interop)
    {
        this.textures=textures;
        this.pi=pi;this.commands=commands;this.framework=framework;this.player=player;this.objects=objects;this.log=log;this.chat=chat;this.notifications=notifications;
        statePath=Path.Combine(pi.GetPluginConfigDirectory(),"steward-state.json");
        config=AtomicState.ReadOrMigrate(statePath,()=>Configuration.FromLegacy(pi.GetPluginConfig() as Configuration));
        if(config.Version!=1)throw new InvalidDataException("Unsupported Steward settings; execution cannot start");
        if(config.SummaryCacheRevision<1){config.Rules.SummaryRefreshHours=Math.Clamp(config.Rules.SummaryRefreshHours,1,2);config.SummaryCacheRevision=1;}
        if(config.SafetyRevision<7)
        {
            config.SafetyRevision=7;config.LogOnly=true;config.ListingWindows=new();
            Save();
        }
        if(config.SafetyRevision<8)
        {
            config.SafetyRevision=8;config.LogOnly=true;config.ListingWindows.Holds.Clear();Save();
        }
        executionFault=config.ExecutionReviewRequired;
        if(!string.IsNullOrEmpty(config.PendingOperation))
        {
            executionFault=config.ExecutionReviewRequired=true;Save();
        }
        Save();
        if(!config.InitialExclusionsApplied)
        {
            // Both exact names/IDs verified against installed English Item data.
            config.Rules.Ignored.UnionWith([8743u,13116u]);
            config.InitialExclusionsApplied=true;
            Save();
        }
        universalis=new(Path.Combine(pi.GetPluginConfigDirectory(),"sale-statistics.json"));
        inventoryMemory=new(Path.Combine(pi.GetPluginConfigDirectory(),"inventory-observations.json"));
        view=new(data,config,inventoryMemory);imports=new(pi,config,view);
        view.StoragePlanAllows=(source,retainer)=>pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.StoragePlanAllows").InvokeFunc(view.Character,
            JsonSerializer.Serialize(new{source.Container,source.Slot,source.ItemId,source.Quantity,source.Hq,Retainer=retainer}));
        tracked=new(pi);
        dungeonSources=new(pi,data,config,Save);
        historyPath=Path.Combine(pi.GetPluginConfigDirectory(),"history.jsonl");
        reviewJournal=new(historyPath);
        market=new(pi,board,view,config,new(gui),JudgeLive,Record,RefreshProtection,()=>imports.MarketPreferencesReady,HoldExecution,BeginOperation,CompleteOperation,ProtectCollection,Alert,Judge,Save,JudgeMarket,new NativeDresserStore(sigScanner,config),interop);
        dresserTrace=new(lifecycle,gui,Record,id=>$"item={id}, player quantity={view.Items.Where(s=>s.ItemId==id).Sum(s=>(long)s.Quantity)}, live dresser={view.Evidence.DresserKnown}, dresser owned={view.Evidence.Dresser.Contains(id)}, prisms={view.Items.Where(s=>s.ItemId==21800).Sum(s=>(long)s.Quantity)}");
        pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.StrategyRetainer").RegisterFunc(r=>config.ExecutionEnabled&&!executionFault&&config.Connected&&attached==player.ContentId&&player.IsLoaded&&config.Features.AnyMarket&&config.Silver.Owns(r));
        pi.GetIpcProvider<string,string>(Endpoint).RegisterFunc(Evaluate);
        pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.OverflowNeeded").RegisterFunc(OverflowNeeded);
        pi.GetIpcProvider<ulong,string,object>("Steward.InventoryPolicy.Fault").RegisterAction(IntegrationFault);
        pi.GetIpcSubscriber<string,object>("AutoRetainer.OnRetainerAdditionalTask").Subscribe(RequestPostprocess);
        pi.GetIpcSubscriber<string,string,object>("AutoRetainer.OnRetainerReadyForPostprocess").Subscribe(Postprocess);
        pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.CollectionNeeded").RegisterFunc(CollectionNeeded);
        pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.CleanupAllowed").RegisterFunc(CleanupAllowed);
        pi.GetIpcProvider<ulong,ulong,bool>("Steward.InventoryPolicy.CleanupRetainerAllowed").RegisterFunc(CleanupRetainerAllowed);
        pi.GetIpcProvider<ulong,uint,bool>("Steward.InventoryPolicy.NonEquipmentDisposalSelected").RegisterFunc((character,id)=>
            player.IsLoaded&&character==player.ContentId&&attached==character&&config.Connected&&view.Facts(id) is {} item&&
            !config.Rules.IsIgnored(item)&&config.Rules.AllowsNonEquipmentDisposal(item));
        commands.AddHandler("/steward",new CommandInfo(Command){HelpMessage="Open Steward; /steward cleanup starts full disposal batches; /steward stop cancels Steward work."});
        pi.UiBuilder.Draw+=Draw;pi.UiBuilder.OpenMainUi+=Open;pi.UiBuilder.OpenConfigUi+=Open;
        framework.Update+=Update;
    }
    private void Open()=>open=true;
    private void Command(string _,string args)
    {
        if(args.Trim()=="trace dresser")
        {
            if(!CanTraceDresser()){chat.Print("[Steward] Stop Steward and AutoRetainer automation before starting a manual dresser trace.");return;}
            dresserTrace.Start();chat.Print("[Steward] Dresser trace started for two minutes. Manually store one item; use log-only mode and leave any stopped-run hold in place.");
        }
        else if(args.Trim()=="trace stop")dresserTrace.Stop();
        else if(args.Trim()=="stop"){dresserTrace.Stop();Stop();}
        else if(args.Trim()=="cleanup")StartCleanup();
        else Open();
    }
    private bool CanTraceDresser()
    {
        if(!player.IsLoaded||config.ExecutionEnabled||market.Queue.Running||ownsPostprocess)return false;
        try{return !pi.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc();}catch{return false;}
    }
    private void Save()
    {
        ExecutionMode.Persist(()=>AtomicState.Write(statePath,config),()=>{executionFault=config.ExecutionReviewRequired=true;});
        // This is a compatibility mirror; the synchronous private state above is authoritative.
        try{pi.SavePluginConfig(config);}catch(Exception ex){log.Error(ex,"Could not mirror Steward settings; private safety state is saved");}
    }
    private void BeginOperation(string name)
    {
        if(!string.IsNullOrEmpty(config.PendingOperation))throw new InvalidOperationException("A previous operation requires inventory review");
        if(!player.IsLoaded || player.ContentId==0)throw new InvalidOperationException("Operation character is unavailable");
        var character=player.ContentId;
        ExecutionJournal.Begin(name,value=>{config.PendingOperation=value;config.PendingCharacter=character;},Save);
    }
    private void CompleteOperation()
    {
        var pending=config.PendingOperation??throw new InvalidOperationException("Operation journal is missing");
        var character=config.PendingCharacter;
        ExecutionJournal.Complete(pending,value=>{config.PendingOperation=value;config.PendingCharacter=value==null?0:character;},Save);
    }
    private void Record(string text)
    {
        var entry=$"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {text}";
        history.Insert(0,entry);if(history.Count>300)history.RemoveAt(history.Count-1);
        try{reviewJournal.Record(text,DateTimeOffset.UtcNow);}
        catch(Exception ex){log.Error(ex,"Could not write Steward history");}
    }
    private void Refresh()
    {
        var character=player.IsLoaded?player.ContentId:0;
        var world=objects.LocalPlayer?.CurrentWorld.RowId??0;
        // Prefill on the first loaded frame and after character/world changes, independently of automation or UI.
        if(view.Character!=character||view.World!=world){nextImports=0;priceCandidates=[];}
        view.World=world;
        view.Refresh(character);
        protectionSnapshot.Clear();
        if(Environment.TickCount64>=nextImports)
        {
            imports.Refresh();tracked.Refresh(view.Character,view.Retainers,inventoryMemory);nextImports=Environment.TickCount64+1000;
            priceCandidates=market.PriceCandidates().Concat(view.Items).Concat(tracked.Items.Where(r=>view.Retainers.Contains(r.Owner)).Select(r=>r.Stock))
                .Where(s=>view.Facts(s.ItemId) is {Marketable:true} item&&!config.Rules.IsIgnored(item)&&
                    (item.Equipment||config.Rules.Sales.Any(r=>r.Matches(item,s))||config.Rules.AllowsNonEquipmentDisposal(item)))
                .Select(s=>s.ItemId).Distinct().ToArray();
        }
        else imports.RefreshReferences(); // Rebuild readiness after every native refresh, even between slower market imports.
        UpdateCollectionHolds();
        universalis.Tick(view.World,priceCandidates,PriceOwners,config.Rules.SummaryRefreshHours,view.Evidence,config.UseUniversalis,DateTimeOffset.UtcNow,config.Rules.SummaryHistoryDays);
    }
    private Decision Judge(Stock stock)=>JudgeForRetainer(stock,0);
    private Decision JudgeForRetainer(Stock stock,ulong retainer)
    {
        var item=view.Facts(stock.ItemId);
        if(config.Silver.Owns(retainer))
        {
            if(!config.Silver.Matches(retainer,stock.ItemId)||stock.Hq)return new(Outcome.Ignore,"Dedicated strategy retainer: other items untouched");
            if(item==null||!item.Marketable||!view.Evidence.GearsetsReady||!view.Evidence.ReferencesReady)return new(Outcome.Wait,"Strategy protection evidence unavailable");
            if(config.Rules.Keep.Contains(stock.ItemId)||config.Rules.QuantityReserves.GetValueOrDefault(stock.ItemId)>0||config.Rules.IgnoredCategories.Contains(item.Category)||view.Evidence.Gearsets.Contains(stock.ItemId))return new(Outcome.Keep,"Strategy item has a keep, category or gearset protection");
            bool? protectedStrategyItem=null;
            try{protectedStrategyItem=pi.GetIpcSubscriber<uint,bool>("AutoRetainer.PluginState.IsItemProtected").InvokeFunc(stock.ItemId);}catch{}
            return UpstreamProtection.Apply(new(Outcome.Market,"Explicit silver strategy; ordinary item exclusion remains in force elsewhere",config.Silver.Rule()),protectedStrategyItem);
        }
        var decision=item==null?new Decision(Outcome.Wait,"Unknown game item"):Rules.Judge(item,stock,config.Rules,view.Evidence,view.World,DateTimeOffset.UtcNow,retainer);
        if(decision.Outcome==Outcome.Ignore)return decision;
        if(view.UnlockStatus(stock.ItemId) is not (1 or 4))return new(Outcome.Keep,"Unlearned or unverified unlock item: keep available to use");
        if(!protectionSnapshot.TryGetValue(stock.ItemId,out var protectedItem))
        {
            try{protectedItem=pi.GetIpcSubscriber<uint,bool>("AutoRetainer.PluginState.IsItemProtected").InvokeFunc(stock.ItemId);}
            catch{protectedItem=null;}
            protectionSnapshot[stock.ItemId]=protectedItem;
        }
        return UpstreamProtection.Apply(decision,protectedItem);
    }
    private void RefreshProtection()
    {
        view.World=objects.LocalPlayer?.CurrentWorld.RowId??0;
        view.Refresh(player.IsLoaded?player.ContentId:0);
        protectionSnapshot.Clear();
        imports.Refresh();
        universalis.Apply(view.World,PriceOwners,config.Rules.SummaryRefreshHours,view.Evidence,config.UseUniversalis,DateTimeOffset.UtcNow,config.Rules.SummaryHistoryDays);
    }
    private Decision JudgeLive(Stock stock)
    {
        if(!config.ExecutionEnabled || !player.IsLoaded || executionFault || !config.Connected || attached==0 || attached!=player.ContentId)
            return new(Outcome.Wait,"Execution held: policy is disconnected or inventory review is required");
        RefreshProtection();
        return Judge(stock);
    }
    private Decision JudgeMarket(Stock stock,ulong retainer,bool live)
    {
        if(live)
        {
            if(!config.ExecutionEnabled||!player.IsLoaded||executionFault||!config.Connected||attached==0||attached!=player.ContentId)
                return new(Outcome.Wait,"Execution held");
            RefreshProtection();
        }
        return JudgeForRetainer(stock,retainer);
    }
    private void HoldExecution(string reason)
    {
        if(executionFault && config.ExecutionReviewRequired)return;
        executionFault=config.ExecutionReviewRequired=true;

        try{Save();}catch(Exception ex){log.Error(ex,"Could not persist execution hold; this session remains stopped");}
        try{if(attached!=0)pi.GetIpcSubscriber<ulong,string,object>("AutoRetainer.InventoryPolicy.Hold").InvokeAction(attached,reason);}
        catch(Exception ex){log.Debug(ex,"AutoRetainer review hold could not be notified; Steward remains stopped");}
        Alert("Execution stopped for inventory review: "+reason);
        Record("Execution held for inventory review: "+reason);
    }
    private void IntegrationFault(ulong character,string reason)
    {
        if(character!=0 && character==player.ContentId){HoldExecution("AutoRetainer: "+reason);StopQueue();}
    }
    private bool StorageCandidate(Decision decision)=>(decision.Outcome!=Outcome.StoreRetainer||config.Features.OverflowStorage)&&decision.Outcome is not (Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Ignore)&&
        decision.Reason is not ("AutoRetainer protection is unavailable" or "Explicit AutoRetainer protection" or "Unlearned or unverified unlock item: keep available to use" or "Live dresser or armoire ownership is unavailable" or "Cached collection copy is unverified; preserve until collection is observed");
    private string Evaluate(string request)
    {
        try
        {
            using var doc=JsonDocument.Parse(request);var root=doc.RootElement;
            if(!player.IsLoaded || attached==0 || attached!=player.ContentId || root.GetProperty("Version").GetInt32()!=6 || root.GetProperty("Character").GetUInt64()!=player.ContentId || !config.Connected)return "Deny";
            // Submission checks need live protections, not another tracked-inventory scan,
            // price prefetch and full market-cache import for every candidate slot.
            view.World=objects.LocalPlayer?.CurrentWorld.RowId??0;
            view.Refresh(player.ContentId);
            protectionSnapshot.Clear();
            imports.RefreshReferences();
            if(root.GetProperty("Action").GetString()=="Gil")return config.ExecutionEnabled&&!executionFault?"Allow":"Deny";
            var id=root.GetProperty("ItemId").GetUInt32()%1000000;
            var quantity=root.GetProperty("Quantity").GetUInt32();
            var container=root.GetProperty("Container").GetInt32();var slot=root.GetProperty("Slot").GetInt32();
            if(container<-1 || slot<-1 || slot>ushort.MaxValue || (container==-1)!=(slot==-1))return "Deny";
            Stock? stock;
            if(container>=0 && slot>=0)
            {
                stock=InventoryView.ReadSlot((uint)container,(ushort)slot);
                if(stock==null||stock.ItemId!=id||stock.Quantity!=quantity)return "Deny";
            }
            else
            {
                var copies=view.Items.Where(s=>s.ItemId==id).ToArray();
                if(copies.Length==0)return "Deny";
                if(copies.Select(Judge).Select(d=>d.Outcome).Distinct().Count()>1)return "Deny";
                stock=copies[0];
            }
            var decision=Judge(stock);var action=root.GetProperty("Action").GetString();
            var target=root.TryGetProperty("Retainer",out var retainerElement)?retainerElement.GetUInt64():0;
            if(action=="Entrust")
            {
                var block=view.StorageBlock(stock,target,nativeEntrust:true);
                var storageAllow=block==null&&StorageCandidate(decision)&&config.ExecutionEnabled&&!executionFault&&!market.Queue.Running&&!ownsPostprocess;
                if(storageAllow&&decision.Outcome==Outcome.StoreRetainer)ProtectCollection(id);
                var reason=block??(!StorageCandidate(decision)?decision.Reason:!config.ExecutionEnabled?"Execution disabled or log-only":executionFault?"Inventory review required":market.Queue.Running||ownsPostprocess?"Companion already has control":"AutoRetainer selected this transfer; Steward protections passed");
                var storageKey=$"entrust/{target}/{id}/{storageAllow}/{reason}";
                if(!recentDecisions.TryGetValue(storageKey,out var lastEntrust)||Environment.TickCount64-lastEntrust>60000)
                {recentDecisions[storageKey]=Environment.TickCount64;Record($"{(storageAllow?"Permitted":"Held")} Entrust for {id} to retainer {target}: {reason}. AutoRetainer owns destination, quantity and execution.");}
                return storageAllow?"Allow":"Deny";
            }
            if(action is "Store" or "Retrieve" && !config.Storage.RetainerEnabled(target))return "Deny";
            if(action=="Store"&&view.StorageBlock(stock,target)!=null)return "Deny";
            LogPolicyProposal(action,stock,decision);
            var allow=config.Features.Allows(action,decision.Outcome)&&ActionPolicy.Allows(action,decision.Outcome,config.ExecutionEnabled,config.Connected,executionFault,
                market.Queue.Running||ownsPostprocess,ownsPostprocess);
            var key=$"{id}/{action}/{allow}/{decision.Reason}";
            if(!recentDecisions.TryGetValue(key,out var at)||Environment.TickCount64-at>60000)
            {recentDecisions[key]=Environment.TickCount64;Record($"{(allow?"Permitted":"Held")} {action} for {id}: {decision.Reason}. AutoRetainer owns execution/results.");}
            return allow?"Allow":"Deny";
        }
        catch(Exception ex){log.Error(ex,"Inventory policy rejected unavailable or invalid evidence");return "Deny";}
    }
    private void Update(IFramework _)
    {
        if(disposed)return;
        try
        {
            PumpReviewLog();
            if(dresserTrace.Active) dresserTrace.Tick(CanTraceDresser());
            if(!player.IsLoaded){StopQueue();return;}
            if(attached!=0 && attached!=player.ContentId){Stop();attached=0;}
            if(Environment.TickCount64>=nextRefresh)
            {
                nextRefresh=Environment.TickCount64+500;Refresh();
                // Expire follow-up permission rather than resuming an old route after AutoDuty stops.
                if(AutoDutyCoordination.OwnsTravel(pi))storageRun.End();
                if(config.Connected && player.ContentId!=0)
                {
                    var version=pi.GetIpcSubscriber<int>("AutoRetainer.InventoryPolicy.Version").InvokeFunc();
                    if(version!=6)throw new InvalidOperationException("Update both plugins: AutoRetainer integration version 6 is required");
                    if(pi.GetIpcSubscriber<int>("AutoRetainer.InventoryPolicy.StorageVersion").InvokeFunc()<5)throw new InvalidOperationException("Update AutoRetainer: safe standalone entrust and venture mode is required");
                    if(attached==player.ContentId)
                    {if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Heartbeat").InvokeFunc(attached,Endpoint))attached=0;}
                    if(attached==0 && pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Connect").InvokeFunc(player.ContentId,Endpoint))attached=player.ContentId;
                    if(attached!=0&&!pi.GetIpcSubscriber<ulong,ulong,bool>("AutoRetainer.InventoryPolicy.MigrateStorageRouting").InvokeFunc(attached,config.Storage.Enabled?config.Storage.NonmarketableRetainer:0))throw new InvalidOperationException("AutoRetainer storage-role migration is not ready");
                    if(pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.HasFault").InvokeFunc(player.ContentId))HoldExecution("AutoRetainer has an unresolved inventory transaction");
                    status=attached!=0?(config.LogOnly?"Log only: proposed actions recorded; all item actions blocked":config.ExecutionEnabled?"Enabled features follow AutoRetainer":"Item actions held for review"):"Waiting for AutoRetainer integration";
                }
            }
            if(ownsPostprocess && !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(player.ContentId))
            {HoldExecution("AutoRetainer handoff ownership was lost");StopQueue();return;}
            UpdateAutomaticMarketPreview();
            ReviewTick();
            market.Advance(Environment.TickCount64);
            UpdateCleanup();
            if(market.Queue.Status.StartsWith("Stopped:",StringComparison.Ordinal) && !executionFault)
                HoldExecution(market.Queue.Status);
            if(openingInventory)
            {
                if(Environment.TickCount64>marketOpenDeadline){HoldExecution("Retainer inventory timed out");StopQueue();}
                else if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Menu").InvokeFunc(player.ContentId,"Inventory"))
                {openingInventory=false;Refresh();market.WithdrawForOtherActions(player.ContentId,overflowPass,collectionPass,cleanupActive);withdrawing=true;}
            }
            if(withdrawing&&!market.Queue.Running)
            {
                withdrawing=false;market.ReturnToRetainerMenu();
                if(market.Queue.Status.StartsWith("Stopped")||overflowPass||collectionPass||cleanupActive||!config.Features.AnyMarket){FinishPostprocess();}
                else{openingMarket=true;marketOpenDeadline=Environment.TickCount64+15000;}
            }
            if(openingMarket)
            {
                if(Environment.TickCount64>marketOpenDeadline){HoldExecution("Retainer market menu timed out");StopQueue();}
                else if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Menu").InvokeFunc(player.ContentId,"Market"))
                {openingMarket=false;Refresh();market.Start(true);if(!market.Queue.Running)FinishPostprocess();}
            }
            if(ownsPostprocess && !openingInventory && !withdrawing && !openingMarket && !market.Queue.Running)FinishPostprocess();
            if(collectionPass&&!ownsPostprocess&&!market.Queue.Running&&!pi.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc())
            {
                collectionPass=false;
                nextDresserAttempt=0;nextAutomaticWork=0;
                // Start the return route before another sweep, including passes that took over a minute.
                nextCollectionAttempt=Math.Max(nextCollectionAttempt,Environment.TickCount64+5000);
                Record("Collection retainer pass finished; collection storage may resume immediately");
            }
            if(!ownsPostprocess&&!market.Queue.Running&&Environment.TickCount64>=nextAutomaticWork&&attached==player.ContentId&&attached!=0)
            {
                nextAutomaticWork=Environment.TickCount64+5000;
                var busy=pi.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc();
                if(!busy)
                {
                    dungeonSources.Tick();
                    if(!cleanupActive&&storageRun.Allows(player.ContentId,config.ExecutionEnabled)&&!executionFault)
                    {
                        var storageStarted=AutomaticStorageWork();
                        if(!storageStarted)
                        {
                            if(!market.Queue.Running)market.DepositOpenDresser();
                            if(!market.Queue.Running)market.CleanupDresser();
                            if(!market.Queue.Running&&!StorageWorkRemaining()&&market.CloseDresserForStorage())storageRun.End();
                        }
                    }
                }
            }

            if(overflowPass && !ownsPostprocess && !market.Queue.Running && !pi.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc())
            {overflowPass=false;if(view.Items.Any(s=>Judge(s).Outcome==Outcome.StoreRetainer))Alert("Retainer storage could not fit all protected overflow. Remaining items are still protected in inventory; free retainer space and try again.");}
            if(market.DresserPreparationStatus is {} preparation)status=preparation;
            if(executionFault)status="Execution held after an uncertain/failed operation. Check inventory and clear the stopped run before continuing.";
        }
        catch(Exception ex)
        {
            status=ex.Message;
            var key="refresh-error/"+ex.Message;
            if(!recentDecisions.TryGetValue(key,out var last)||Environment.TickCount64-last>60000)
            {recentDecisions[key]=Environment.TickCount64;log.Error(ex,"Steward update failed");Record("Steward refresh failed; review may be incomplete: "+ex.Message);}
            if(market.Queue.Running || ownsPostprocess || config.PendingOperation!=null)HoldExecution(ex.Message);StopQueue();
        }
    }
    private void RequestPostprocess(string retainer)
    {
        if(!player.IsLoaded||!config.ExecutionEnabled||executionFault||attached!=player.ContentId||market.Queue.Running)return;
        if(view.RetainerNames.FirstOrDefault(p=>p.Value==retainer).Key is var retainerId && !config.Storage.RetainerEnabled(retainerId)&&!config.SalesEnabledFor(retainerId))return;
        if(!config.Features.AnyInventoryWork&&!config.Features.AnyMarket)return;
        if(overflowPass && !OverflowNeeded(player.ContentId))return;
        try{pi.GetIpcSubscriber<string,object>("AutoRetainer.RequestPostprocess").InvokeAction("Steward");}
        catch(Exception ex){Record("Postprocess request failed: "+ex.Message);}
    }
    private void Postprocess(string plugin,string retainer)
    {
        if(plugin!="Steward")return;
        ownsPostprocess=true;
        nextPostprocessRelease=0;
        if(!config.ExecutionEnabled || executionFault || !config.Connected || attached==0 || attached!=player.ContentId)
        {FinishPostprocess();return;}
        try{Refresh();
            if(!config.Features.AnyInventoryWork&&!config.Features.AnyMarket){FinishPostprocess();return;}
            if(!cleanupActive)BeginStorageRun(pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(player.ContentId));
            openingInventory=config.Features.AnyInventoryWork;openingMarket=!openingInventory&&config.Features.AnyMarket;marketOpenDeadline=Environment.TickCount64+15000;}
        catch(Exception ex){log.Error(ex,"Retainer postprocess planning failed");HoldExecution("Retainer postprocess planning failed: "+ex.Message);StopQueue();}
    }
    private void FinishPostprocess()
    {
        if(!ownsPostprocess || Environment.TickCount64<nextPostprocessRelease)return;
        try
        {
            if(pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(player.ContentId))
            {
                try{market.ReturnToRetainerMenu();}catch(Exception ex){Record("Retainer UI cleanup: "+ex.Message);}
                pi.GetIpcSubscriber<object>("AutoRetainer.FinishPostprocessRequest").InvokeAction();
            }
            ownsPostprocess=false;
        }
        catch(Exception ex)
        {
            nextPostprocessRelease=Environment.TickCount64+1000;
            HoldExecution("AutoRetainer postprocess release failed: "+ex.Message);
        }
    }
    private void StopQueue()
    {
        CancelCleanup();
        storageRun.End();
        if(market.Queue.HasUnverifiedAction && !executionFault)HoldExecution("An active operation was cancelled; its last game action may still complete");
        openingInventory=withdrawing=openingMarket=false;overflowPass=collectionPass=false;cleanupVisited=false;
        try{market.Stop();}finally{FinishPostprocess();}
    }
    private void Stop(){try{StopQueue();}finally{Save();}Record("Current Steward run stopped; enabled features wait for the next authorised AutoRetainer run");}
    private void Run(string operation)
    {
        if(AutoDutyCoordination.OwnsTravel(pi))
        {Record("AutoDuty owns travel. Stop its run before requesting Steward storage travel.");return;}
        if(operation=="Dresser"&&!config.Features.DresserCollection&&!config.CleanDresser||operation=="Armoire"&&!config.Features.ArmoireStorage)
        {Record(operation+" feature is disabled");return;}
        if(executionFault){Record("Check inventory and clear the stopped run first");return;}
        if(!config.ExecutionEnabled){Record("Dry run: would request AutoRetainer "+operation);return;}
        if(market.Queue.Running){Record("Wait for the current operation to finish");return;}
        if(cleanupActive){Record("Stop full cleanup before starting another operation");return;}
        try{var result=pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,operation);
            if(result)BeginStorageRun(true);
            Record(result?"Requested AutoRetainer "+operation:"AutoRetainer could not start "+operation+"; check its settings, dependencies and current task");}
        catch(Exception ex){HoldExecution("AutoRetainer request failed after a possible partial enqueue: "+ex.Message);StopQueue();}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        framework.Update-=Update;
        try{dresserTrace.Dispose();StopQueue();market.Dispose();}
        finally
        {
            universalis.Dispose();inventoryMemory.Dispose();reviewJournal.Dispose();
            pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.StrategyRetainer").UnregisterFunc();
            pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.CollectionNeeded").UnregisterFunc();
            pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.CleanupAllowed").UnregisterFunc();
            pi.GetIpcProvider<ulong,ulong,bool>("Steward.InventoryPolicy.CleanupRetainerAllowed").UnregisterFunc();
            pi.GetIpcProvider<ulong,uint,bool>("Steward.InventoryPolicy.NonEquipmentDisposalSelected").UnregisterFunc();
            pi.GetIpcProvider<string,string>(Endpoint).UnregisterFunc();
            pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.OverflowNeeded").UnregisterFunc();
            pi.GetIpcProvider<ulong,string,object>("Steward.InventoryPolicy.Fault").UnregisterAction();
            pi.GetIpcSubscriber<string,object>("AutoRetainer.OnRetainerAdditionalTask").Unsubscribe(RequestPostprocess);
            pi.GetIpcSubscriber<string,string,object>("AutoRetainer.OnRetainerReadyForPostprocess").Unsubscribe(Postprocess);
            pi.UiBuilder.Draw-=Draw;pi.UiBuilder.OpenMainUi-=Open;pi.UiBuilder.OpenConfigUi-=Open;commands.RemoveHandler("/steward");Save();
        }
    }
}
