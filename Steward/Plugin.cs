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
    private bool open=true;
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
        IObjectTable objects,IDataManager data,IGameGui gui,IMarketBoard board,IPluginLog log,IChatGui chat,INotificationManager notifications,ITextureProvider textures)
    {
        this.textures=textures;
        this.pi=pi;this.commands=commands;this.framework=framework;this.player=player;this.objects=objects;this.log=log;this.chat=chat;this.notifications=notifications;
        statePath=Path.Combine(pi.GetPluginConfigDirectory(),"steward-state.json");
        config=AtomicState.ReadOrMigrate(statePath,()=>Configuration.FromLegacy(pi.GetPluginConfig() as Configuration));
        if(config.Version!=1)throw new InvalidDataException("Unsupported Steward settings; execution cannot start");
        // A failed Stop write must never let a later process restore an old enabled flag.
        config.Automatic=false;
        if(config.SummaryCacheRevision<1){config.Rules.SummaryRefreshHours=Math.Clamp(config.Rules.SummaryRefreshHours,1,2);config.SummaryCacheRevision=1;}
        if(config.SafetyRevision<6)
        {
            config.Automatic=false;
            config.SafetyRevision=6;
            Save();
        }
        executionFault=config.ExecutionReviewRequired;
        if(!string.IsNullOrEmpty(config.PendingOperation))
        {
            executionFault=config.ExecutionReviewRequired=true;config.Automatic=false;Save();
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
        tracked=new(pi);
        dungeonSources=new(pi,data,config,Save);
        historyPath=Path.Combine(pi.GetPluginConfigDirectory(),"history.jsonl");
        reviewJournal=new(historyPath);
        market=new(pi,board,view,config,new(gui),JudgeLive,Record,RefreshProtection,()=>imports.MarketPreferencesReady,HoldExecution,BeginOperation,CompleteOperation,ProtectCollection,Alert,Judge);
        pi.GetIpcProvider<string,string>(Endpoint).RegisterFunc(Evaluate);
        pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.OverflowNeeded").RegisterFunc(OverflowNeeded);
        pi.GetIpcProvider<ulong,string,object>("Steward.InventoryPolicy.Fault").RegisterAction(IntegrationFault);
        pi.GetIpcSubscriber<string,object>("AutoRetainer.OnRetainerAdditionalTask").Subscribe(RequestPostprocess);
        pi.GetIpcSubscriber<string,string,object>("AutoRetainer.OnRetainerReadyForPostprocess").Subscribe(Postprocess);
        pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.CollectionNeeded").RegisterFunc(CollectionNeeded);
        commands.AddHandler("/steward",new CommandInfo(Command){HelpMessage="Open inventory decisions; /steward stop cancels Steward work."});
        pi.UiBuilder.Draw+=Draw;pi.UiBuilder.OpenMainUi+=Open;pi.UiBuilder.OpenConfigUi+=Open;
        framework.Update+=Update;
    }
    private void Open()=>open=true;
    private void Command(string _,string args){if(args.Trim()=="stop")Stop();else Open();}
    private void Save()
    {
        ExecutionMode.Persist(()=>AtomicState.Write(statePath,config),()=>{config.Automatic=false;executionFault=config.ExecutionReviewRequired=true;});
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
        UpdateCollectionHolds();
        if(Environment.TickCount64>=nextImports)
        {
            imports.Refresh();tracked.Refresh(view.Character,view.Retainers,inventoryMemory);nextImports=Environment.TickCount64+1000;
            priceCandidates=market.PriceCandidates().Concat(view.Items).Concat(tracked.Items.Where(r=>view.Retainers.Contains(r.Owner)).Select(r=>r.Stock))
                .Where(s=>view.Facts(s.ItemId) is {Marketable:true} item&&!config.Rules.IsIgnored(item)&&
                    (item.Equipment||config.Rules.Sales.Any(r=>r.Matches(item,s))||config.Rules.NonEquipmentDisposal.Contains(item.Id)))
                .Select(s=>s.ItemId).Distinct().ToArray();
        }
        universalis.Tick(view.World,priceCandidates,PriceOwners,config.Rules.SummaryRefreshHours,view.Evidence,config.UseUniversalis,DateTimeOffset.UtcNow,config.Rules.SummaryHistoryDays);
    }
    private Decision Judge(Stock stock)
    {
        var item=view.Facts(stock.ItemId);
        var decision=item==null?new Decision(Outcome.Wait,"Unknown game item"):Rules.Judge(item,stock,config.Rules,view.Evidence,view.World,DateTimeOffset.UtcNow);
        if(decision.Outcome==Outcome.Ignore)return decision;
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
    private void HoldExecution(string reason)
    {
        if(executionFault && config.ExecutionReviewRequired && !config.Automatic)return;
        executionFault=config.ExecutionReviewRequired=true;
        config.Automatic=false;
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
    private string Evaluate(string request)
    {
        try
        {
            using var doc=JsonDocument.Parse(request);var root=doc.RootElement;
            if(!player.IsLoaded || attached==0 || attached!=player.ContentId || root.GetProperty("Version").GetInt32()!=6 || root.GetProperty("Character").GetUInt64()!=player.ContentId || !config.Connected)return "Deny";
            Refresh();
            // Protection imports are refreshed again before permission, even between regular refresh intervals.
            imports.Refresh();
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
            LogPolicyProposal(action,stock,decision);
            var allow=ActionPolicy.Allows(action,decision.Outcome,config.ExecutionEnabled,config.Connected,executionFault,
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
            if(!player.IsLoaded){StopQueue();return;}
            if(attached!=0 && attached!=player.ContentId){Stop();attached=0;}
            if(Environment.TickCount64>=nextRefresh)
            {
                nextRefresh=Environment.TickCount64+500;Refresh();
                if(config.Connected && player.ContentId!=0)
                {
                    var version=pi.GetIpcSubscriber<int>("AutoRetainer.InventoryPolicy.Version").InvokeFunc();
                    if(version!=6)throw new InvalidOperationException("Update both plugins: AutoRetainer integration version 6 is required");
                    if(attached==player.ContentId)
                    {if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Heartbeat").InvokeFunc(attached,Endpoint))attached=0;}
                    if(attached==0 && pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Connect").InvokeFunc(player.ContentId,Endpoint))attached=player.ContentId;
                    if(pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.HasFault").InvokeFunc(player.ContentId))HoldExecution("AutoRetainer has an unresolved inventory transaction");
                    status=attached!=0?(config.LogOnly?"Log only: proposed actions recorded; all item actions blocked":config.Automatic?"Automatic decisions enabled":"Dry run: AutoRetainer item actions held"):"Waiting for AutoRetainer integration";
                }
            }
            if(ownsPostprocess && !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(player.ContentId))
            {HoldExecution("AutoRetainer handoff ownership was lost");StopQueue();return;}
            ReviewTick();
            market.Advance(Environment.TickCount64);
            if(market.Queue.Status.StartsWith("Stopped:",StringComparison.Ordinal) && !executionFault)
                HoldExecution(market.Queue.Status);
            if(openingInventory)
            {
                if(Environment.TickCount64>marketOpenDeadline){HoldExecution("Retainer inventory timed out");StopQueue();}
                else if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Menu").InvokeFunc(player.ContentId,"Inventory"))
                {openingInventory=false;Refresh();market.WithdrawForOtherActions(player.ContentId,overflowPass,collectionPass);withdrawing=true;}
            }
            if(withdrawing&&!market.Queue.Running)
            {
                withdrawing=false;market.ReturnToRetainerMenu();
                if(market.Queue.Status.StartsWith("Stopped")||overflowPass||collectionPass){FinishPostprocess();}
                else{openingMarket=true;marketOpenDeadline=Environment.TickCount64+15000;}
            }
            if(openingMarket)
            {
                if(Environment.TickCount64>marketOpenDeadline){HoldExecution("Retainer market menu timed out");StopQueue();}
                else if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Menu").InvokeFunc(player.ContentId,"Market"))
                {openingMarket=false;Refresh();market.Start(true);if(!market.Queue.Running)FinishPostprocess();}
            }
            if(ownsPostprocess && !openingInventory && !withdrawing && !openingMarket && !market.Queue.Running)FinishPostprocess();
            if(!ownsPostprocess&&!market.Queue.Running&&Environment.TickCount64>=nextAutomaticWork&&attached==player.ContentId&&attached!=0)
            {
                nextAutomaticWork=Environment.TickCount64+5000;
                var busy=pi.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc();
                if(!busy)
                {
                    dungeonSources.Tick();
                    var storageStarted=AutomaticStorageWork();
                    if(!storageStarted && config.ExecutionEnabled && !executionFault)
                    {
                        if(!market.Queue.Running)market.DepositOpenDresser();
                        if(!market.Queue.Running)market.CleanupDresser();
                    }
                }
            }
            if(collectionPass&&!ownsPostprocess&&!market.Queue.Running&&!pi.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc())collectionPass=false;
            if(overflowPass && !ownsPostprocess && !market.Queue.Running && !pi.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy").InvokeFunc())
            {overflowPass=false;if(view.Items.Any(s=>Judge(s).Outcome==Outcome.StoreRetainer))Alert("Retainer storage could not fit all protected overflow. Remaining items are still protected in inventory; free retainer space and try again.");}
            if(executionFault)status="Execution held after an uncertain/failed operation. Check inventory and clear the stopped run before continuing.";
        }
        catch(Exception ex)
        {
            status=ex.Message;
            var key="refresh-error/"+ex.Message;
            if(!recentDecisions.TryGetValue(key,out var last)||Environment.TickCount64-last>60000)
            {recentDecisions[key]=Environment.TickCount64;Record("Steward refresh failed; review may be incomplete: "+ex.Message);}
            if(config.Automatic || market.Queue.Running || ownsPostprocess)HoldExecution(ex.Message);StopQueue();
        }
    }
    private void RequestPostprocess(string retainer)
    {
        if(!player.IsLoaded||!config.ExecutionEnabled||executionFault||attached!=player.ContentId||market.Queue.Running)return;
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
        try{Refresh();openingInventory=true;marketOpenDeadline=Environment.TickCount64+15000;}
        catch(Exception ex){HoldExecution("Retainer postprocess planning failed: "+ex.Message);StopQueue();}
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
        if(market.Queue.Running && !executionFault)HoldExecution("An active operation was cancelled; its last game action may still complete");
        openingInventory=withdrawing=openingMarket=false;overflowPass=collectionPass=false;cleanupVisited=false;
        try{market.Stop();}finally{FinishPostprocess();}
    }
    private void Stop(){config.Automatic=false;try{StopQueue();}finally{Save();}Record("Automatic execution stopped; managed items remain held");}
    private void SetAutomatic(bool enabled)
    {
        if(!enabled){Stop();return;}
        if(config.LogOnly){Record("Log only is enabled. Turn it off in Developer tools before enabling automatic execution.");return;}
        try
        {
            var ready=!config.LogOnly && player.IsLoaded && config.Connected && attached!=0 && attached==player.ContentId && !executionFault &&
                !config.ExecutionReviewRequired && !market.Queue.Running && !ownsPostprocess &&
                !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.HasFault").InvokeFunc(player.ContentId);
            if(!ExecutionMode.Enable(ready,value=>config.Automatic=value,Save))
                Record("Automatic execution remains off: connect the current character and resolve the stopped run first");
        }
        catch(Exception ex){HoldExecution("Automatic execution could not be safely enabled: "+ex.Message);StopQueue();}
    }
    private void Run(string operation)
    {
        if(executionFault){Record("Check inventory and clear the stopped run first");return;}
        if(!config.ExecutionEnabled){Record("Dry run: would request AutoRetainer "+operation);return;}
        if(market.Queue.Running){Record("Wait for the current operation to finish");return;}
        try{var result=pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,operation);Record(result?"Requested AutoRetainer "+operation:"AutoRetainer could not start "+operation+"; check its settings, dependencies and current task");}
        catch(Exception ex){HoldExecution("AutoRetainer request failed after a possible partial enqueue: "+ex.Message);StopQueue();}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        framework.Update-=Update;
        try{StopQueue();market.Dispose();}
        finally
        {
            universalis.Dispose();inventoryMemory.Dispose();reviewJournal.Dispose();
            pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.CollectionNeeded").UnregisterFunc();
            pi.GetIpcProvider<string,string>(Endpoint).UnregisterFunc();
            pi.GetIpcProvider<ulong,bool>("Steward.InventoryPolicy.OverflowNeeded").UnregisterFunc();
            pi.GetIpcProvider<ulong,string,object>("Steward.InventoryPolicy.Fault").UnregisterAction();
            pi.GetIpcSubscriber<string,object>("AutoRetainer.OnRetainerAdditionalTask").Unsubscribe(RequestPostprocess);
            pi.GetIpcSubscriber<string,string,object>("AutoRetainer.OnRetainerReadyForPostprocess").Unsubscribe(Postprocess);
            pi.UiBuilder.Draw-=Draw;pi.UiBuilder.OpenMainUi-=Open;pi.UiBuilder.OpenConfigUi-=Open;commands.RemoveHandler("/steward");Save();
        }
    }
}
