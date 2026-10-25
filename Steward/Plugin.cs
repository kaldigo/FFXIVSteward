using System.Text.Json;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Steward.Core;
using Steward.Services;

namespace Steward;

public sealed class Plugin : IDalamudPlugin
{
    private const string Endpoint="Steward.InventoryPolicy.Evaluate";
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly IObjectTable objects;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly InventoryView view;
    private readonly ExternalImports imports;
    private readonly MarketQueue market;
    private readonly TrackedInventories tracked;
    private readonly DungeonSources dungeonSources;
    private readonly List<string> history=[];
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
        IObjectTable objects,IDataManager data,IGameGui gui,IMarketBoard board,IPluginLog log)
    {
        this.pi=pi;this.commands=commands;this.framework=framework;this.player=player;this.objects=objects;this.log=log;
        config=pi.GetPluginConfig() as Configuration??new();
        if(config.SafetyRevision<3)
        {
            config.Automatic=false;
            config.SafetyRevision=3;
            pi.SavePluginConfig(config);
        }
        executionFault=config.ExecutionReviewRequired;
        if(!config.InitialExclusionsApplied)
        {
            // Both exact names/IDs verified against installed English Item data.
            config.Rules.Ignored.UnionWith([8743u,13116u]);
            config.InitialExclusionsApplied=true;
            pi.SavePluginConfig(config);
        }
        view=new(data,config);imports=new(pi,config,view);
        tracked=new(pi);
        dungeonSources=new(pi,data,config,Save);
        historyPath=Path.Combine(pi.GetPluginConfigDirectory(),"history.jsonl");
        market=new(pi,board,view,config,new(gui),JudgeLive,Record,RefreshProtection,()=>imports.MarketPreferencesReady,HoldExecution);
        pi.GetIpcProvider<string,string>(Endpoint).RegisterFunc(Evaluate);
        pi.GetIpcProvider<ulong,string,object>("Steward.InventoryPolicy.Fault").RegisterAction(IntegrationFault);
        pi.GetIpcSubscriber<string,object>("AutoRetainer.OnRetainerAdditionalTask").Subscribe(RequestPostprocess);
        pi.GetIpcSubscriber<string,string,object>("AutoRetainer.OnRetainerReadyForPostprocess").Subscribe(Postprocess);
        commands.AddHandler("/steward",new CommandInfo(Command){HelpMessage="Open inventory decisions; /steward stop cancels Steward work."});
        pi.UiBuilder.Draw+=Draw;pi.UiBuilder.OpenMainUi+=Open;pi.UiBuilder.OpenConfigUi+=Open;
        framework.Update+=Update;
    }
    private void Open()=>open=true;
    private void Command(string _,string args){if(args.Trim()=="stop")Stop();else Open();}
    private void Save()=>pi.SavePluginConfig(config);
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
        if(Environment.TickCount64>=nextImports){imports.Refresh();tracked.Refresh();nextImports=Environment.TickCount64+2000;}
    }
    private Decision Judge(Stock stock)
    {var item=view.Facts(stock.ItemId);return item==null?new(Outcome.Wait,"Unknown game item"):Rules.Judge(item,stock,config.Rules,view.Evidence,view.World,DateTimeOffset.UtcNow);}
    private void RefreshProtection()
    {
        view.World=objects.LocalPlayer?.CurrentWorld.RowId??0;
        view.Refresh(player.IsLoaded?player.ContentId:0);
        imports.Refresh();
    }
    private Decision JudgeLive(Stock stock)
    {
        if(!player.IsLoaded || executionFault || !config.Connected || attached==0 || attached!=player.ContentId)
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
            if(!player.IsLoaded || root.GetProperty("Version").GetInt32()!=3 || root.GetProperty("Character").GetUInt64()!=player.ContentId || !config.Connected)return "Deny";
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
            var allow=ActionPolicy.Allows(action,decision.Outcome,config.Automatic,config.Connected,executionFault,
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
                    if(version!=3)throw new InvalidOperationException("Update both plugins: AutoRetainer integration version 3 is required");
                    if(attached==player.ContentId)
                    {if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Heartbeat").InvokeFunc(attached,Endpoint))attached=0;}
                    if(attached==0 && pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Connect").InvokeFunc(player.ContentId,Endpoint))attached=player.ContentId;
                    if(pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.HasFault").InvokeFunc(player.ContentId))HoldExecution("AutoRetainer has an unresolved inventory transaction");
                    status=attached!=0?(config.Automatic?"Automatic decisions enabled":"Dry run: AutoRetainer item actions held"):"Waiting for AutoRetainer integration";
                }
            }
            if(ownsPostprocess && !pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.OwnsHandoff").InvokeFunc(player.ContentId))
            {HoldExecution("AutoRetainer handoff ownership was lost");StopQueue();return;}
            market.Advance(Environment.TickCount64);
            if(market.Queue.Status.StartsWith("Stopped:",StringComparison.Ordinal) && !executionFault)
                HoldExecution(market.Queue.Status);
            if(openingInventory)
            {
                if(Environment.TickCount64>marketOpenDeadline){HoldExecution("Retainer inventory timed out");StopQueue();}
                else if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Menu").InvokeFunc(player.ContentId,"Inventory"))
                {openingInventory=false;Refresh();market.WithdrawForOtherActions(player.ContentId);withdrawing=true;}
            }
            if(withdrawing&&!market.Queue.Running)
            {
                withdrawing=false;market.ReturnToRetainerMenu();
                if(market.Queue.Status.StartsWith("Stopped")){FinishPostprocess();}
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
                    if(config.Automatic && !executionFault)
                    {
                        market.DepositOpenDresser();
                        if(!market.Queue.Running)market.CleanupDresser();
                    }
                }
            }
            if(executionFault)status="Execution held after an uncertain/failed operation. Check inventory and clear the stopped run before continuing.";
        }
        catch(Exception ex){status=ex.Message;if(config.Automatic || market.Queue.Running || ownsPostprocess)HoldExecution(ex.Message);StopQueue();}
    }
    private void RequestPostprocess(string retainer)
    {
        if(!player.IsLoaded||!config.Automatic||executionFault||attached!=player.ContentId||market.Queue.Running)return;
        try{pi.GetIpcSubscriber<string,object>("AutoRetainer.RequestPostprocess").InvokeAction("Steward");}
        catch(Exception ex){Record("Postprocess request failed: "+ex.Message);}
    }
    private void Postprocess(string plugin,string retainer)
    {
        if(plugin!="Steward")return;
        ownsPostprocess=true;
        nextPostprocessRelease=0;
        if(!config.Automatic || executionFault || !config.Connected || attached==0 || attached!=player.ContentId)
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
        openingInventory=withdrawing=openingMarket=false;
        try{market.Stop();}finally{FinishPostprocess();}
    }
    private void Stop(){config.Automatic=false;try{StopQueue();}finally{Save();}Record("Automatic execution stopped; managed items remain held");}
    private void Run(string operation)
    {
        if(executionFault){Record("Check inventory and clear the stopped run first");return;}
        if(!config.Automatic){Record("Dry run: would request AutoRetainer "+operation);return;}
        if(market.Queue.Running){Record("Wait for the current operation to finish");return;}
        try{var result=pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,operation);Record(result?"Requested AutoRetainer "+operation:"AutoRetainer could not start "+operation+"; check its settings, dependencies and current task");}
        catch(Exception ex){HoldExecution("AutoRetainer request failed after a possible partial enqueue: "+ex.Message);StopQueue();}
    }
    private void Draw()
    {
        if(!open)return;
        ImGui.SetNextWindowSize(new(850,640),ImGuiCond.FirstUseEver);
        if(!ImGui.Begin("Steward",ref open)){ImGui.End();return;}
        ImGui.TextWrapped(status);ImGui.TextWrapped(imports.Status);
        var automatic=config.Automatic;
        if(ImGui.Checkbox("Enable automatic execution",ref automatic)){config.Automatic=automatic;if(!automatic)StopQueue();Save();}
        ImGui.SameLine();if(ImGui.Button("Stop"))Stop();
        if(executionFault && ImGui.Button("I checked inventory — clear stopped run"))
        {
            try
            {
                if(!pi.GetIpcSubscriber<ulong,bool>("AutoRetainer.InventoryPolicy.ClearFault").InvokeFunc(player.ContentId))
                    Record("Close pending confirmations and release control before clearing the stopped run");
                else{market.Queue.Begin();config.Automatic=false;executionFault=config.ExecutionReviewRequired=false;Save();Record("User cleared both stopped-run holds after inventory review; automatic execution remains off");}
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
                ImGui.SameLine();if(ImGui.Button("List / reprice current retainer"))market.Start();
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
                var combined=config.Rules.Quality==QualityMode.Combined;
                if(ImGui.Checkbox("Compare HQ and NQ together",ref combined)){config.Rules.Quality=combined?QualityMode.Combined:QualityMode.Separate;Save();}
                var age=config.Rules.MaximumPriceAgeMinutes;if(ImGui.InputInt("Maximum cache age (minutes)",ref age)){config.Rules.MaximumPriceAgeMinutes=Math.Clamp(age,1,1440);Save();}
                ImGui.InputText("Item ID",ref itemText,12);
                if(uint.TryParse(itemText,out var id)&&id>0)
                {
                    if(ImGui.Button("Exclude item")){config.Rules.Ignored.Add(id);Save();}ImGui.SameLine();
                    if(ImGui.Button("Remove exclusion")){config.Rules.Ignored.Remove(id);Save();}ImGui.SameLine();
                    if(ImGui.Button("Keep item")){config.Rules.Keep.Add(id);Save();}ImGui.SameLine();
                    if(ImGui.Button("Remove keep")){config.Rules.Keep.Remove(id);Save();}
                    if(ImGui.Button("Source: dungeon / crafting")){config.Sources[id]=SourceKind.Ordinary;Save();}ImGui.SameLine();
                    if(ImGui.Button("Source: special")){config.Sources[id]=SourceKind.Special;Save();}
                }
                ImGui.TextWrapped("Excluded IDs: "+string.Join(", ",config.Rules.Ignored));
                ImGui.InputText("Eorzea Collection list file",ref commonPath,1024);
                if(ImGui.Button("Import curated glamour list"))try{ExternalImports.ImportCommon(commonPath,config.Rules);Save();Record("Imported curated glamour list");}catch(Exception ex){Record(ex.Message);}
                if(ImGui.Button("Add auto-sell rule")){config.Rules.Sales.Add(new());Save();}
                for(var i=0;i<config.Rules.Sales.Count;i++)
                {
                    var rule=config.Rules.Sales[i];ImGui.PushID(i);
                    var name=rule.Name;if(ImGui.InputText("Rule name",ref name,100)){rule.Name=name;Save();}
                    var unit=(int)rule.MinimumUnit;var total=(int)rule.MinimumTotal;var stack=rule.MaximumStack;
                    if(ImGui.InputInt("Minimum unit gil",ref unit)){rule.MinimumUnit=(uint)Math.Clamp(unit,1,999999999);Save();}
                    if(ImGui.InputInt("Minimum stack gil",ref total)){rule.MinimumTotal=(uint)Math.Max(1,total);Save();}
                    if(ImGui.InputInt("Maximum stack",ref stack)){rule.MaximumStack=Math.Clamp(stack,1,9999);Save();}
                    ImGui.TextWrapped("Item IDs: "+(rule.ItemIds.Count==0?"all marketable items":string.Join(",",rule.ItemIds)));
                    if(uint.TryParse(itemText,out var ruleId)&&ruleId>0&&ImGui.Button("Restrict to entered item")){rule.ItemIds.Add(ruleId);Save();}
                    if(i>0){ImGui.SameLine();if(ImGui.Button("Move up")){(config.Rules.Sales[i-1],config.Rules.Sales[i])=(rule,config.Rules.Sales[i-1]);Save();}}
                    if(ImGui.Button("Remove rule")){config.Rules.Sales.RemoveAt(i);Save();ImGui.PopID();break;}ImGui.PopID();ImGui.Separator();
                }
                if(ImGui.Button("Edit all settings as JSON"))policyJson=JsonSerializer.Serialize(config,new JsonSerializerOptions{WriteIndented=true});
                if(policyJson.Length>0)
                {
                    ImGui.InputTextMultiline("Settings JSON",ref policyJson,65536,new(0,180));
                    if(ImGui.Button("Apply rules and source overrides"))try
                    {var changed=JsonSerializer.Deserialize<Configuration>(policyJson)??throw new InvalidDataException("Empty settings");config.Rules=changed.Rules;config.Sources=changed.Sources;config.Pricing=changed.Pricing;config.ImportMarketBuddy=changed.ImportMarketBuddy;Save();Record("Updated rules");}
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
            if(ImGui.BeginTabItem("History")){foreach(var line in history)ImGui.TextWrapped(line);ImGui.EndTabItem();}
            if(ImGui.BeginTabItem("Integration"))
            {
                ImGui.TextWrapped("AutoRetainer keeps its existing workflows and settings. Steward supplies permissions before item actions. Dry run holds managed actions. Unloading Steward leaves registered characters held until Steward reconnects or you explicitly detach.");
                ImGui.TextWrapped(dungeonSources.Status);
                var bundled=config.UseBundledGlamourList;if(ImGui.Checkbox("Use bundled curated glamour items",ref bundled)){config.UseBundledGlamourList=bundled;Save();nextImports=0;}
                var expected=config.GlamaholicExpected;if(ImGui.Checkbox("Require Glamaholic configuration",ref expected)){config.GlamaholicExpected=expected;Save();}
                if(ImGui.Button("Detach current character and restore original AutoRetainer rules"))
                {
                    Stop();try{if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Detach").InvokeFunc(player.ContentId,Endpoint)){config.Connected=false;attached=0;Save();}}catch(Exception ex){Record(ex.Message);}
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
            pi.GetIpcProvider<string,string>(Endpoint).UnregisterFunc();
            pi.GetIpcProvider<ulong,string,object>("Steward.InventoryPolicy.Fault").UnregisterAction();
            pi.GetIpcSubscriber<string,object>("AutoRetainer.OnRetainerAdditionalTask").Unsubscribe(RequestPostprocess);
            pi.GetIpcSubscriber<string,string,object>("AutoRetainer.OnRetainerReadyForPostprocess").Unsubscribe(Postprocess);
            pi.UiBuilder.Draw-=Draw;pi.UiBuilder.OpenMainUi-=Open;pi.UiBuilder.OpenConfigUi-=Open;commands.RemoveHandler("/steward");Save();
        }
    }
}
