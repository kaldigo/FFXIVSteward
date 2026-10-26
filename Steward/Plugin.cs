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
    private readonly Configuration config;
    private readonly string statePath;
    private readonly InventoryView view;
    private readonly ExternalImports imports;
    private readonly MarketQueue market;
    private readonly TrackedInventories tracked;
    private readonly DungeonSources dungeonSources;
    private readonly AggregatePrices universalis;
    private static readonly HashSet<ulong> noPriceOwners=[];
    private IReadOnlySet<ulong> PriceOwners=>view.Retainers.Count==0?noPriceOwners:config.OwnRetainers;
    private readonly List<string> history=[];
    private readonly Dictionary<uint,bool?> protectionSnapshot=[];
    private readonly string historyPath;
    private long nextRefresh;
    private long nextImports;
    private long nextAutomaticWork;
    private ulong attached;
    private bool open=true;
    private bool disposed;
    private bool ownsPostprocess;
    private bool executionFault;
    private string status="Starting in dry-run mode";
    private string itemText="";
    private string commonPath="";
    private string search="";
    private string policyJson="";
    private bool openingMarket;
    private bool openingInventory;
    private bool withdrawing;
    private long marketOpenDeadline;
    private long nextPostprocessRelease;
    private readonly Dictionary<string,long> recentDecisions=[];
    public Plugin(IDalamudPluginInterface pi,ICommandManager commands,IFramework framework,IPlayerState player,
        IObjectTable objects,IDataManager data,IGameGui gui,IMarketBoard board,IPluginLog log,IChatGui chat,INotificationManager notifications)
    {
        this.pi=pi;this.commands=commands;this.framework=framework;this.player=player;this.objects=objects;this.log=log;this.chat=chat;this.notifications=notifications;
        statePath=Path.Combine(pi.GetPluginConfigDirectory(),"steward-state.json");
        config=AtomicState.ReadOrMigrate(statePath,()=>Configuration.FromLegacy(pi.GetPluginConfig() as Configuration));
        if(config.Version!=1)throw new InvalidDataException("Unsupported Steward settings; execution cannot start");
        // A failed Stop write must never let a later process restore an old enabled flag.
        config.Automatic=false;
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
        view=new(data,config);imports=new(pi,config,view);
        tracked=new(pi);
        dungeonSources=new(pi,data,config,Save);
        historyPath=Path.Combine(pi.GetPluginConfigDirectory(),"history.jsonl");
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
        try{Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);File.AppendAllText(historyPath,JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,message=text})+Environment.NewLine);}
        catch(Exception ex){log.Error(ex,"Could not write Steward history");}
    }
    private void Refresh()
    {
        view.World=objects.LocalPlayer?.CurrentWorld.RowId??0;
        view.Refresh(player.IsLoaded?player.ContentId:0);
        protectionSnapshot.Clear();
        UpdateCollectionHolds();
        if(Environment.TickCount64>=nextImports)
        {
            imports.Refresh();tracked.Refresh();nextImports=Environment.TickCount64+1000;
            var ids=market.PriceCandidates().Concat(view.Items).Concat(tracked.Items.Where(r=>view.Retainers.Contains(r.Owner)).Select(r=>r.Stock))
                .Where(s=>view.Facts(s.ItemId) is {Marketable:true} item&&!config.Rules.IsIgnored(item)&&
                    (item.Equipment||config.Rules.Sales.Any(r=>r.Matches(item,s))||config.Rules.NonEquipmentDisposal.Contains(item.Id)))
                .Select(s=>s.ItemId);
            universalis.Tick(view.World,ids,PriceOwners,config.Rules.SummaryRefreshHours,view.Evidence,config.UseUniversalis,DateTimeOffset.UtcNow);
        }
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
        universalis.Apply(view.World,PriceOwners,config.Rules.SummaryRefreshHours,view.Evidence,config.UseUniversalis,DateTimeOffset.UtcNow);
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
        if(config.LogOnly){Record("Log only is enabled. Turn it off in Rules before enabling automatic execution.");return;}
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
    private void Draw()
    {
        if(!open)return;
        ImGui.SetNextWindowSize(new(850,640),ImGuiCond.FirstUseEver);
        if(!ImGui.Begin("Steward",ref open)){ImGui.End();return;}
        ImGui.TextWrapped(status);ImGui.TextWrapped(imports.Status);ImGui.TextWrapped(universalis.Status);ImGui.TextWrapped(universalis.Freshness);
        if(config.PendingOperation!=null)ImGui.TextWrapped($"Unverified operation: {config.PendingOperation}; character {config.PendingCharacter}. Review that character's inventory before clearing.");
        var automatic=config.Automatic;
        if(ImGui.Checkbox("Enable automatic execution",ref automatic))SetAutomatic(automatic);
        ImGui.SameLine();if(ImGui.Button("Stop"))Stop();
        if(executionFault && ImGui.Button("I checked inventory — clear stopped run"))
        {
            try
            {
                if(config.PendingCharacter!=0 && config.PendingCharacter!=player.ContentId)
                    Record("Return to the character that owns the unverified operation before clearing it");
                else if(!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.ClearFault").InvokeFunc(player.ContentId))
                    Record("Stop AutoRetainer's queued work, close pending confirmations and release control before clearing the stopped run");
                else
                {
                    var pending=config.PendingOperation;
                    var pendingCharacter=config.PendingCharacter;
                    config.Automatic=false;executionFault=config.ExecutionReviewRequired=false;config.PendingOperation=null;config.PendingCharacter=0;
                    try{Save();}catch{config.PendingOperation=pending;config.PendingCharacter=pendingCharacter;executionFault=config.ExecutionReviewRequired=true;throw;}
                    market.Queue.Begin();Record("User cleared both stopped-run holds after inventory review; automatic execution remains off");
                }
            }
            catch(Exception ex){Record("Could not clear the integration hold: "+ex.Message);}
        }
        ImGui.TextWrapped("Collection: exact item IDs; dyes ignored. Open the dresser, plate editor and armoire to load evidence. Existing AutoRetainer protections and enabled actions still apply.");
        if(ImGui.BeginTabBar("Steward tabs"))
        {
            if(ImGui.BeginTabItem("Preview"))
            {
                ImGui.InputText("Search",ref search,128);
                if(ImGui.Button("Refresh")){nextImports=0;Refresh();}
                ImGui.SameLine();if(ImGui.Button("List / reprice current retainer")){if(config.LogOnly){reviewJournal.Reset();nextReview=0;ReviewTick();}else market.Start();}
                ImGui.SameLine();if(ImGui.Button("Clean open dresser"))market.CleanupDresser();
                ImGui.TextWrapped($"Queue: {market.Queue.Status}; completed {market.Queue.Completed}");
                foreach(var operation in new[]{"Armoire","Dresser","Gc","Desynth","Vendor"})
                {ImGui.SameLine();if(ImGui.Button(operation))Run(operation);}
                if(ImGui.BeginChild("Decisions",new(0,0),true))
                    foreach(var stock in view.Items)
                    {
                        var facts=view.Facts(stock.ItemId);if(facts==null||(!string.IsNullOrWhiteSpace(search)&&!facts.Name.Contains(search,StringComparison.OrdinalIgnoreCase)&&!stock.ItemId.ToString().Contains(search)))continue;
                        var decision=Judge(stock);
                        ImGui.TextWrapped($"{facts.Name} [{stock.ItemId}] {(stock.Hq?"HQ":"NQ")} x{stock.Quantity}: {decision.Outcome} — {decision.Reason}");
                    }
                ImGui.EndChild();ImGui.EndTabItem();
            }
            if(ImGui.BeginTabItem("Rules"))
            {
                DrawStorageSettings();
                DrawCategorySelection("Exclude item categories from all handling",config.Rules.IgnoredCategories,"None excluded");
                ImGui.TextWrapped("Global category exclusions skip listing, disposal and collection moves. Sale-rule exclusions below affect only that rule.");
                var combined=config.Rules.Quality==QualityMode.Combined;
                if(ImGui.Checkbox("Compare HQ and NQ together",ref combined)){config.Rules.Quality=combined?QualityMode.Combined:QualityMode.Separate;Save();}
                var age=config.Rules.SummaryRefreshHours;if(ImGui.InputInt("Refresh sale summaries every (hours)",ref age)){config.Rules.SummaryRefreshHours=Math.Clamp(age,1,168);Save();}
                var publicPrices=config.UseUniversalis;
                if(ImGui.Checkbox("Use Universalis prices",ref publicPrices)){config.UseUniversalis=publicPrices;Save();nextImports=0;}
                ImGui.TextWrapped("Average sale value and daily sales are prefetched and saved across restarts. Missing recent sales never mean zero value. Actual posting and repricing still use an in-game comparison.");
                ImGui.InputText("Item ID",ref itemText,12);
                if(uint.TryParse(itemText,out var id)&&id>0)
                {
                    if(ImGui.Button("Exclude item")){config.Rules.Ignored.Add(id);Save();}ImGui.SameLine();
                    if(ImGui.Button("Remove exclusion")){config.Rules.Ignored.Remove(id);Save();}ImGui.SameLine();
                    if(ImGui.Button("Keep item")){config.Rules.Keep.Add(id);Save();}ImGui.SameLine();
                    if(ImGui.Button("Remove keep")){config.Rules.Keep.Remove(id);Save();}
                    if(ImGui.Button("Collect glamour item")){config.Rules.CollectionKeep.Add(id);Save();}ImGui.SameLine();
                    if(ImGui.Button("Remove supplemental collection")){config.Rules.CollectionKeep.Remove(id);Save();}
                    var reserveQuantity=(int)Math.Min(config.Rules.QuantityReserves.GetValueOrDefault(id),999999);
                    if(ImGui.InputInt("Keep quantity outside collection",ref reserveQuantity)){if(reserveQuantity<=0)config.Rules.QuantityReserves.Remove(id);else config.Rules.QuantityReserves[id]=(uint)Math.Clamp(reserveQuantity,1,999999);Save();}
                    ImGui.TextWrapped("Quantity reserves keep live inventory copies; offline retainer caches never justify consuming the reserve. Whole-stack disposal is held when a stack includes reserved items.");
                    if(ImGui.Button("Allow disposal of this non-equipment item")){config.Rules.NonEquipmentDisposal.Add(id);Save();}ImGui.SameLine();
                    if(ImGui.Button("Remove non-equipment disposal permission")){config.Rules.NonEquipmentDisposal.Remove(id);Save();}
                    if(ImGui.Button("Source: dungeon / crafting")){config.Sources[id]=SourceKind.Ordinary;Save();}ImGui.SameLine();
                    if(ImGui.Button("Source: special")){config.Sources[id]=SourceKind.Special;Save();}
                }
                ImGui.TextWrapped("Excluded IDs: "+string.Join(", ",config.Rules.Ignored));
                ImGui.TextWrapped("Non-equipment stays protected unless explicitly allowed by item ID; sale rules can still list it. AutoRetainer keeps its enabled methods and item-selection rules. Allowed non-equipment disposal IDs: "+string.Join(", ",config.Rules.NonEquipmentDisposal));
                ImGui.InputText("Eorzea Collection list file",ref commonPath,1024);
                if(ImGui.Button("Import curated glamour list"))try{ExternalImports.ImportCommon(commonPath,config.Rules);Save();Record("Imported curated glamour list");}catch(Exception ex){Record(ex.Message);}
                if(ImGui.Button("Add auto-sell rule")){config.Rules.Sales.Add(new());Save();}
                ImGui.TextWrapped("First matching enabled rule wins. Put specific gear/material rules above an all-items rule; each rule can have its own prices and stack size.");
                for(var i=0;i<config.Rules.Sales.Count;i++)
                {
                    var rule=config.Rules.Sales[i];ImGui.PushID(i);
                    var name=rule.Name;if(ImGui.InputText("Rule name",ref name,100)){rule.Name=name;Save();}
                    var ruleEnabled=rule.Enabled;if(ImGui.Checkbox("Rule enabled",ref ruleEnabled)){rule.Enabled=ruleEnabled;Save();}
                    DrawSaleRuleCategories(rule);
                    var ruleQuality=rule.Hq==null?0:rule.Hq==false?1:2;
                    if(ImGui.Combo("Rule quality",ref ruleQuality,"Any quality\0NQ only\0HQ only\0")){rule.Hq=ruleQuality==0?null:ruleQuality==2;Save();}
                    var unit=(int)rule.MinimumUnit;var total=(int)rule.MinimumTotal;var stack=rule.MaximumStack;
                    if(ImGui.InputInt("Minimum unit gil",ref unit)){rule.MinimumUnit=(uint)Math.Clamp(unit,1,999999999);Save();}
                    if(ImGui.InputInt("Minimum stack gil",ref total)){rule.MinimumTotal=(uint)Math.Max(1,total);Save();}
                    if(ImGui.InputInt("Maximum stack",ref stack)){rule.MaximumStack=Math.Clamp(stack,1,9999);Save();}
                    var velocityEnabled=rule.MinimumDailySales.HasValue;
                    if(ImGui.Checkbox("Require minimum daily sales",ref velocityEnabled)){rule.MinimumDailySales=velocityEnabled?0.1:null;Save();}
                    if(velocityEnabled){var velocity=(float)(rule.MinimumDailySales??0.1);if(ImGui.InputFloat("Minimum daily sales",ref velocity,0.01f,0.1f,"%.3f")){rule.MinimumDailySales=float.IsFinite(velocity)?Math.Max(0,velocity):0;Save();}}
                    var older=rule.AllowOlderSaleValue;
                    if(ImGui.Checkbox("Allow older known values for slow sellers",ref older)){rule.AllowOlderSaleValue=older;Save();}
                    ImGui.TextWrapped("Value floors apply to the average sale value and the actual posted price. An enabled sales threshold holds items when sales evidence is missing.");
                    ImGui.TextWrapped("Item IDs: "+(rule.ItemIds.Count==0?"any within the selected group/categories":string.Join(",",rule.ItemIds)));
                    if(uint.TryParse(itemText,out var ruleId)&&ruleId>0&&ImGui.Button("Restrict to entered item")){rule.ItemIds.Add(ruleId);Save();}
                    if(i>0){ImGui.SameLine();if(ImGui.Button("Move up")){(config.Rules.Sales[i-1],config.Rules.Sales[i])=(rule,config.Rules.Sales[i-1]);Save();}}
                    if(i<config.Rules.Sales.Count-1){ImGui.SameLine();if(ImGui.Button("Move down")){(config.Rules.Sales[i+1],config.Rules.Sales[i])=(rule,config.Rules.Sales[i+1]);Save();}}
                    if(ImGui.Button("Remove rule")){config.Rules.Sales.RemoveAt(i);Save();ImGui.PopID();break;}ImGui.PopID();ImGui.Separator();
                }
                if(ImGui.Button("Edit all settings as JSON"))policyJson=JsonSerializer.Serialize(config,new JsonSerializerOptions{WriteIndented=true});
                if(policyJson.Length>0)
                {
                    ImGui.InputTextMultiline("Settings JSON",ref policyJson,65536,new(0,180));
                    if(ImGui.Button("Apply rules and source overrides"))try
                    {var changed=JsonSerializer.Deserialize<Configuration>(policyJson)??throw new InvalidDataException("Empty settings");config.Rules=changed.Rules;config.Sources=changed.Sources;config.Pricing=changed.Pricing;config.ImportMarketBuddy=changed.ImportMarketBuddy;config.UseUniversalis=changed.UseUniversalis;Save();Record("Updated rules");}
                    catch(Exception ex){Record(ex.Message);}
                }
                ImGui.EndTabItem();
            }
            if(ImGui.BeginTabItem("Tracked storage"))
            {
                ImGui.TextWrapped(tracked.Status);
                foreach(var (owner,stock) in tracked.Items)
                {
                    var facts=view.Facts(stock.ItemId);var decision=Judge(stock);
                    ImGui.TextWrapped($"{facts?.Name??stock.ItemId.ToString()} x{stock.Quantity} — owner {owner}, container {stock.Container}: {decision.Outcome} ({decision.Reason}); cached preview");
                }
                ImGui.EndTabItem();
            }
            DrawMarketPreviewTab();
            if(ImGui.BeginTabItem("History")){foreach(var line in history)ImGui.TextWrapped(line);ImGui.EndTabItem();}
            if(ImGui.BeginTabItem("Integration"))
            {
                ImGui.TextWrapped("AutoRetainer keeps its existing workflows and settings. Steward supplies permissions before item actions. Dry run holds managed actions. Unloading Steward leaves registered characters held until Steward reconnects or you explicitly detach.");
                ImGui.TextWrapped(dungeonSources.Status);
                var bundled=config.UseBundledGlamourList;if(ImGui.Checkbox("Use bundled curated glamour items",ref bundled)){config.UseBundledGlamourList=bundled;Save();nextImports=0;}
                var expected=config.GlamaholicExpected;if(ImGui.Checkbox("Require Glamaholic configuration",ref expected)){config.GlamaholicExpected=expected;Save();}
                if(ImGui.Button("Detach current character and restore original AutoRetainer rules"))
                {
                    Stop();if(config.PendingCollection.TryGetValue(player.ContentId,out var waiting)&&waiting.Count>0){Alert("Collect protected overflow before detaching; AutoRetainer cannot preserve these holds without Steward.");}else try{if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Detach").InvokeFunc(player.ContentId,Endpoint)){config.Connected=false;attached=0;Save();}else Record("Resolve the stopped run and stop AutoRetainer's queued work before detaching");}catch(Exception ex){Record(ex.Message);}
                }
                if(!config.Connected&&ImGui.Button("Connect Steward policy")){config.Connected=true;Save();}
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        framework.Update-=Update;
        try{StopQueue();market.Dispose();}
        finally
        {
            universalis.Dispose();
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
