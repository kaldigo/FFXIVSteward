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
    private string status="Starting in dry-run mode";
    private string itemText="";
    private string commonPath="";
    private string search="";
    private string policyJson="";
    private bool openingMarket;
    private bool openingInventory;
    private bool withdrawing;
    private long marketOpenDeadline;
    private readonly Dictionary<string,long> recentDecisions=[];
    public Plugin(IDalamudPluginInterface pi,ICommandManager commands,IFramework framework,IPlayerState player,
        IObjectTable objects,IDataManager data,IGameGui gui,IMarketBoard board,IPluginLog log)
    {
        this.pi=pi;this.commands=commands;this.framework=framework;this.player=player;this.objects=objects;this.log=log;
        config=pi.GetPluginConfig() as Configuration??new();
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
        market=new(pi,board,view,config,new(gui),Judge,Record);
        pi.GetIpcProvider<string,string>(Endpoint).RegisterFunc(Evaluate);
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
        view.World=objects.LocalPlayer?.HomeWorld.RowId??0;
        view.Refresh(player.ContentId);
        if(Environment.TickCount64>=nextImports){imports.Refresh();tracked.Refresh();nextImports=Environment.TickCount64+2000;}
    }
    private Decision Judge(Stock stock)
    {var item=view.Facts(stock.ItemId);return item==null?new(Outcome.Wait,"Unknown game item"):Rules.Judge(item,stock,config.Rules,view.Evidence,view.World,DateTimeOffset.UtcNow);}
    private string Evaluate(string request)
    {
        try
        {
            using var doc=JsonDocument.Parse(request);var root=doc.RootElement;
            if(root.GetProperty("Version").GetInt32()!=1 || root.GetProperty("Character").GetUInt64()!=player.ContentId || !config.Connected)return "Deny";
            Refresh();
            // Protection imports are refreshed again before permission, even between regular refresh intervals.
            imports.Refresh();
            var id=root.GetProperty("ItemId").GetUInt32()%1000000;
            var quantity=root.GetProperty("Quantity").GetUInt32();
            var container=root.GetProperty("Container").GetInt32();var slot=root.GetProperty("Slot").GetInt32();
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
            var allow=config.Automatic && action switch
            {
                "Armoire"=>decision.Outcome==Outcome.CollectArmoire,
                "Dresser"=>decision.Outcome==Outcome.CollectDresser,
                "Retrieve"=>decision.Outcome is Outcome.CollectArmoire or Outcome.CollectDresser or Outcome.Dispose,
                "Vendor" or "Desynth" or "Gc"=>decision.Outcome==Outcome.Dispose,
                _=>false // No discard automation was requested.
            };
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
            if(attached!=0 && attached!=player.ContentId){Stop();attached=0;}
            if(Environment.TickCount64>=nextRefresh)
            {
                nextRefresh=Environment.TickCount64+500;Refresh();
                if(config.Connected && player.ContentId!=0)
                {
                    var version=pi.GetIpcSubscriber<int>("AutoRetainer.InventoryPolicy.Version").InvokeFunc();
                    if(version!=1)throw new InvalidOperationException("AutoRetainer integration version mismatch");
                    if(attached==player.ContentId)
                    {if(!pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Heartbeat").InvokeFunc(attached,Endpoint))attached=0;}
                    if(attached==0 && pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Connect").InvokeFunc(player.ContentId,Endpoint))attached=player.ContentId;
                    status=attached!=0?(config.Automatic?"Automatic decisions enabled":"Dry run: AutoRetainer item actions held"):"Waiting for AutoRetainer integration";
                }
            }
            market.Advance(Environment.TickCount64);
            if(openingInventory)
            {
                if(Environment.TickCount64>marketOpenDeadline){openingInventory=false;Record("Retainer inventory timed out");FinishPostprocess();}
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
                if(Environment.TickCount64>marketOpenDeadline){openingMarket=false;Record("Retainer market menu timed out");FinishPostprocess();}
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
                    if(config.Automatic)
                    {
                        market.DepositOpenDresser();
                        if(!market.Queue.Running)market.CleanupDresser();
                    }
                }
            }
        }
        catch(Exception ex){status=ex.Message;StopQueue();}
    }
    private void RequestPostprocess(string retainer)
    {
        if(!config.Automatic||attached!=player.ContentId||market.Queue.Running)return;
        try{pi.GetIpcSubscriber<string,object>("AutoRetainer.RequestPostprocess").InvokeAction("Steward");}
        catch(Exception ex){Record("Postprocess request failed: "+ex.Message);}
    }
    private void Postprocess(string plugin,string retainer)
    {
        if(plugin!="Steward")return;
        ownsPostprocess=true;
        try{Refresh();openingInventory=true;marketOpenDeadline=Environment.TickCount64+15000;}
        catch{FinishPostprocess();throw;}
    }
    private void FinishPostprocess()
    {
        if(!ownsPostprocess)return;ownsPostprocess=false;
        try{market.ReturnToRetainerMenu();}catch(Exception ex){Record("Retainer UI cleanup: "+ex.Message);}
        try{pi.GetIpcSubscriber<object>("AutoRetainer.FinishPostprocessRequest").InvokeAction();}
        catch(Exception ex){Record("AutoRetainer postprocess release failed: "+ex.Message);}
    }
    private void StopQueue(){openingInventory=withdrawing=openingMarket=false;try{market.Stop();}finally{FinishPostprocess();}}
    private void Stop(){config.Automatic=false;Save();StopQueue();Record("Automatic execution stopped; managed items remain held");}
    private void Run(string operation)
    {
        if(!config.Automatic){Record("Dry run: would request AutoRetainer "+operation);return;}
        if(market.Queue.Running){Record("Wait for the current operation to finish");return;}
        try{var result=pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Run").InvokeFunc(player.ContentId,operation);Record(result?"Requested AutoRetainer "+operation:"AutoRetainer could not start "+operation+"; check its settings, dependencies and current task");}
        catch(Exception ex){Record(ex.Message);}
    }
    private void Draw()
    {
        if(!open)return;
        ImGui.SetNextWindowSize(new(850,640),ImGuiCond.FirstUseEver);
        if(!ImGui.Begin("Steward",ref open)){ImGui.End();return;}
        ImGui.TextWrapped(status);ImGui.TextWrapped(imports.Status);
        var automatic=config.Automatic;
        if(ImGui.Checkbox("Enable automatic execution",ref automatic)){config.Automatic=automatic;Save();if(!automatic)StopQueue();}
        ImGui.SameLine();if(ImGui.Button("Stop"))Stop();
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
            pi.GetIpcSubscriber<string,object>("AutoRetainer.OnRetainerAdditionalTask").Unsubscribe(RequestPostprocess);
            pi.GetIpcSubscriber<string,string,object>("AutoRetainer.OnRetainerReadyForPostprocess").Unsubscribe(Postprocess);
            pi.UiBuilder.Draw-=Draw;pi.UiBuilder.OpenMainUi-=Open;pi.UiBuilder.OpenConfigUi-=Open;commands.RemoveHandler("/steward");Save();
        }
    }
}
