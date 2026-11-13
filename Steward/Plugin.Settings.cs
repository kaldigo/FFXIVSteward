using System.Numerics;
using Dalamud.Bindings.ImGui;
using Steward.Core;

namespace Steward;

public sealed partial class Plugin
{
    private readonly StewardShell shell = new();
    private uint selectedItem;
    private readonly Dictionary<uint,string> itemQueries=[];
    private readonly Dictionary<uint,IReadOnlyList<(uint Id,string Name)>> itemSearchResults=[];
    private void SavePolicy()
    {
        decisions.Clear();
        market.ReplanPending();
        config.GlamaholicExpected=config.Rules.Glamour.Glamaholic;
        config.UseBundledGlamourList=config.Rules.Glamour.EorzeaCollection;
        config.ControlsRevision=1;Save();nextImports=0;nextReview=0;marketPreview=null;reviewJournal.Reset();
    }
    private void Setting(string title,bool value,Action<bool> update,string? description=null)
    {
        if(StewardShell.SwitchRow(title,value,description)){update(!value);SavePolicy();}
    }
    private static void Section(string title) => StewardShell.Section(title);
    private string RunContext => executionFault || config.ExecutionReviewRequired ? "Inventory review needed"
        : config.LogOnly ? "Log-only review" : !config.Connected ? "Disconnected"
        : cleanupActive ? "Cleanup running" : market.Queue.Running ? "Processing items"
        : attached == 0 || attached != player.ContentId ? "Waiting for AutoRetainer" : "Following AutoRetainer";

    private void Draw()
    {
        if (!open) return;
        float scale = StewardShell.Scale;
        ImGui.SetNextWindowSize(new Vector2(860,650)*scale,ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420,340)*scale,new(float.MaxValue,float.MaxValue));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(14,12)*scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg,new Vector4(.055f,.058f,.074f,1));
        try
        {
            if (ImGui.Begin("Steward###StewardSettings",ref open,ImGuiWindowFlags.NoTitleBar))
                shell.Draw(RunContext,()=>open=false,Stop,DrawWorkspace,DrawHeaderMark);
            ImGui.End();
        }
        finally { ImGui.PopStyleColor(); ImGui.PopStyleVar(); }
    }
    private void DrawHeaderMark(Vector2 topLeft,float size)
    {
        var icon=textures.GetFromFile(Path.Combine(pi.AssemblyLocation.DirectoryName!,"images","icon.png")).GetWrapOrDefault();
        if(icon!=null) ImGui.GetWindowDrawList().AddImage(icon.Handle,topLeft,topLeft+new Vector2(size));
    }
    private void DrawWorkspace(Desk page)
    {
        if(executionFault && page!=Desk.Activity)
        {
            ImGui.TextWrapped("A previous operation needs an inventory check.");
            if(ImGui.SmallButton("Review on Activity"))shell.Open(Desk.Activity);
        }
        switch(page)
        {
            case Desk.Inventory:DrawDecisions();break;
            case Desk.Market:
                if(ImGui.Button("Edit sale rules"))shell.Open(Desk.Sales);
                if(ImGui.Button("Dedicated retainer strategy"))shell.Open(Desk.Strategies);
                DrawSalePreview();break;
            case Desk.Collection:
                DrawCollectionFeatureControls();
                if(config.PendingCollection.TryGetValue(view.Character,out var pending))
                    ImGui.TextWrapped($"{pending.Count} protected items waiting for collection");
                ImGui.TextWrapped($"Dresser free slots: {view.Evidence.DresserFreeSlots?.ToString()??"unknown"}   /   Reserved: {config.Rules.DresserEmptySlots}");
                StewardShell.Link("Choose protected gear","Set which outfits, sources and equipment require a collection copy.",()=>shell.Open(Desk.Glamour));
                StewardShell.Link("Manage storage and dresser cleanup","Set overflow, dresser reserve and cleanup permissions.",()=>shell.Open(Desk.Storage));
                break;
            case Desk.Setup:
                StewardShell.Link("Integrations","Manage the AutoRetainer connection and imported settings.",()=>shell.Open(Desk.Connections));
                StewardShell.Link("Developer tools","Review proposals in log-only mode and inspect diagnostics.",()=>shell.Open(Desk.Review));
                break;
            case Desk.Disposal:DrawDisposalPermissions();break;
            case Desk.Readiness:DrawReadiness();break;
            case Desk.Activity:DrawOverviewPage();break;
            case Desk.Sales:DrawSalesPage();break;
            case Desk.Glamour:DrawGlamourPage();break;
            case Desk.Storage:DrawStoragePage();break;
            case Desk.Items:DrawOverridesPage();break;
            case Desk.Connections:DrawIntegrationsPage();break;
            case Desk.Review:DrawDeveloperPage();break;
            case Desk.Strategies:DrawSilverStrategy();break;
        }
    }
    private void DrawOverviewPage()
    {
        string? hold=executionFault || config.ExecutionReviewRequired ? "Check inventory before resuming."
            : config.LogOnly ? "Log-only mode is recording proposals. Manual passes are paused."
            : !config.ExecutionEnabled ? "Connect Steward to AutoRetainer before running a pass." : null;
        runDesk.Draw(RunContext,config.Rules.Sales.Count(r=>r.Enabled),view.Evidence.DresserFreeSlots,
            view.Character==0?null:config.PendingCollection.GetValueOrDefault(view.Character)?.Count??0,hold,DrawRecoveryControls,
            pass=>pass switch {0=>!cleanupActive&&config.Features.AnyMarket,1=>!cleanupActive&&(config.Features.DresserCollection||config.CleanDresser),
                2=>!cleanupActive&&config.Features.ArmoireStorage,_=>!cleanupActive&&config.Features.Disposal},
            pass=>{if(!config.ExecutionEnabled||executionFault||cleanupActive)return;
                switch(pass){case 0:market.Start();break;case 1:Run("Dresser");break;case 2:Run("Armoire");break;case 3:StartCleanup();break;}},
            ()=>shell.Open(Desk.Inventory),()=>shell.Open(Desk.Readiness));
        if(cleanupActive || cleanupStatus.Length>0)ImGui.TextWrapped(cleanupStatus);
    }
    private readonly RunDesk runDesk=new();
    private readonly DecisionBrowser decisions=new();
    private (ulong Character,uint World) decisionOwner;
    private void DrawDecisions()
    {
        var owner=(view.Character,view.World);
        if(owner!=decisionOwner){decisions.Clear();decisionOwner=owner;}
        decisions.Draw(()=>decisions.Capture(view.Items.Select(stock=>new DecisionRow(stock,view.Facts(stock.ItemId)?.Name??$"Item {stock.ItemId}",Judge(stock)))),
            id=>{selectedItem=id;shell.Open(Desk.Items);},()=>shell.Open(Desk.Disposal));
        if(ImGui.Button("Manage item overrides"))shell.Open(Desk.Items);
    }
    private void DrawReadiness()
    {
        Section("Readiness");
        ImGui.TextUnformatted(attached==player.ContentId&&attached!=0?"AutoRetainer: connected":"AutoRetainer: waiting for connection");
        ImGui.TextWrapped(view.Evidence.ReferencesReady?(view.NativeReferencesCached&&config.Rules.Glamour.NativePlates?"Selected glamour references: ready (saved native plates)":"Selected glamour references: ready"):!view.NativeReferencesAvailable&&config.Rules.Glamour.NativePlates?"Waiting for in-game glamour plate data":"Waiting for selected glamour references");
        if(!view.NativeReferencesAvailable&&config.Rules.Glamour.NativePlates)ImGui.TextWrapped("Open the in-game glamour plate editor to load your saved plates.");
        if(view.NativeReferencesCached&&config.Rules.Glamour.NativePlates)ImGui.TextWrapped($"Using saved native plate references from {view.NativeReferencesObservedAt:yyyy-MM-dd HH:mm} UTC. Opening the plate editor refreshes them automatically.");
        if(!view.Evidence.GlamaholicKnown&&config.Rules.Glamour.Glamaholic)ImGui.TextWrapped("Glamaholic is selected but its saved outfits are unavailable. Check that the plugin is installed and configured.");
        ImGui.TextWrapped(view.Evidence.DresserKnown?"Glamour dresser: ready":"Glamour dresser: open it to verify current contents");
        ImGui.TextWrapped(view.Evidence.ArmoireKnown?"Armoire: ready":"Armoire: open it to verify current contents");
        Section("Your setup");
        ImGui.TextUnformatted($"{config.Rules.Sales.Count(r=>r.Enabled)} enabled sale rules · {config.Rules.Ignored.Count} item exclusions");
        ImGui.TextUnformatted($"Dresser space reserved: {config.Rules.DresserEmptySlots} slots");
        if(config.PendingCollection.TryGetValue(view.Character,out var waiting)&&waiting.Count>0)ImGui.TextWrapped($"{waiting.Count} protected items are waiting for collection. They remain protected in retainer storage.");
    }
    private void DrawGlamourPage()
    {
        ImGui.TextWrapped("Keep one copy when any enabled filter matches. Turning off collection pauses item movement and keeps these protections active.");
        var f=config.Rules.Glamour;
        if(!ImGui.BeginTabBar("ProtectionTopics",ImGuiTabBarFlags.FittingPolicyScroll))return;
        if(ImGui.BeginTabItem("Keep one copy"))
        {
            DrawProtectionFilters(f);
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Gear categories"))
        {
        Section("Which gear types should automatic filters collect?");
        DrawCategorySelection("Apply automatic filters to",f.Categories,"All equipment categories","Empty means all gear types. Selecting categories limits the automatic filters above to those gear types; a category alone does not protect an item.");
        DrawCategorySelection("Skip automatic filters for",f.ExcludedCategories,"No categories skipped","Checked gear types will not be protected by the automatic filters above. Saved outfits, manual collection items, pending holds and unknown-source safeguards still apply.");
        ImGui.TextWrapped("Category scope applies to automatic filters, not saved outfits, manual collection items or pending collection holds.");
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Exceptions"))
        {
        Section("Always preserved");
        ImGui.TextWrapped("Equipped items, gear sets, explicit keep overrides, quantity reserves and pending collection holds retain their protection. Unknown acquisition is held rather than assumed disposable. Armoire-eligible items use the armoire and do not consume dresser space. Dyes do not require extra copies.");
        if(ImGui.Button("Edit manual collection items"))shell.Open(Desk.Items);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
    private void DrawMarketFeatureControls()
    {
        if(!ImGui.CollapsingHeader("Market work",ImGuiTreeNodeFlags.DefaultOpen))return;
        var f=config.Features;
        Setting("List new items",f.NewListings,v=>f.NewListings=v,"Stops new listings without changing sale rules. Items that still qualify for sale remain held rather than becoming disposal candidates.");
        Setting("Reprice existing listings",f.Repricing,v=>f.Repricing=v);
        Setting("Apply sale rules to existing listings",f.ListingReturns,v=>f.ListingReturns=v,"Withdraw listings that no longer qualify under the current sale rules. Turn this off to leave them listed; repricing and sale-window rotation have separate switches.");
        ImGui.BeginDisabled(!f.NewListings);
        Setting("Rotate listings when their sale window expires",f.Rotation,v=>f.Rotation=v,"Requires new listings enabled, a sale window on the matching rule, and another eligible item waiting.");
        ImGui.EndDisabled();
    }
    private void DrawCollectionFeatureControls()
    {
        Setting("Collect items into the glamour dresser",config.Features.DresserCollection,v=>config.Features.DresserCollection=v);
        Setting("Store eligible items in the armoire",config.Features.ArmoireStorage,v=>config.Features.ArmoireStorage=v);
    }
    private void DrawRetainerSelection(HashSet<ulong> excluded,bool perRule)
    {
        if(view.Retainers.Count==0)ImGui.TextWrapped("Open a summoning bell to load your retainers.");
        foreach(var id in view.Retainers.OrderBy(id=>view.RetainerNames.GetValueOrDefault(id,id.ToString())))
        {
            var allowed=!excluded.Contains(id);
            ImGui.BeginDisabled(perRule&&config.ExcludedSaleRetainers.Contains(id));
            if(ImGui.Checkbox($"{view.RetainerNames.GetValueOrDefault(id,"Retainer "+id)}##{id}",ref allowed))
            {if(allowed)excluded.Remove(id);else excluded.Add(id);SavePolicy();}
            ImGui.EndDisabled();
        }
    }
    private void DrawSalesPage()
    {
        if(!ImGui.BeginTabBar("SalesSections",ImGuiTabBarFlags.FittingPolicyScroll))return;
        if(ImGui.BeginTabItem("Rules"))
        {
            DrawSaleCards();
        ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Retainers"))
        {
            ImGui.TextWrapped("Ordinary sale automation only runs on checked retainers. Experimental strategies have their own narrowly scoped item exceptions. Each ordinary sale rule can narrow this selection further.");
            DrawRetainerSelection(config.ExcludedSaleRetainers,false);
            ImGui.TextWrapped("Existing listings on enabled retainers are checked against the rules and withdrawn when they no longer qualify. Returned items follow normal collection, sale and disposal rules.");
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Pricing"))
        {
        Section("Market pricing");
        ImGui.TextWrapped("Live price safeguard: ignore up to three very cheap listings only when they are at most a quarter of the next price and at least ten competitor listings sit within twice that price. Sale decisions still use average values.");
        var combined=config.Rules.Quality==QualityMode.Combined;
        Setting("Compare HQ and NQ together",combined,v=>config.Rules.Quality=v?QualityMode.Combined:QualityMode.Separate);
        Setting("Use Market Buddy pricing preferences",config.ImportMarketBuddy,v=>config.ImportMarketBuddy=v,"Imports undercut, rounding and stack preferences read-only. Turn this off to use the controls below.");
        ImGui.BeginDisabled(config.ImportMarketBuddy);
        var p=config.Pricing;
        Setting("Undercut by percentage",p.UndercutUsePercent,v=>p.UndercutUsePercent=v);
        var cut=p.UndercutUsePercent?p.UndercutPercent:p.UndercutPrice;
        if(ImGui.InputInt(p.UndercutUsePercent?"Undercut percentage":"Undercut gil",ref cut)){if(p.UndercutUsePercent)p.UndercutPercent=Math.Clamp(cut,0,99);else p.UndercutPrice=Math.Clamp(cut,0,999999999);SavePolicy();}
        Setting("Round listing prices",p.EnablePriceRounding,v=>p.EnablePriceRounding=v);
        if(p.EnablePriceRounding){var multiple=p.PriceRoundingMultiple;if(ImGui.InputInt("Round down to multiples of",ref multiple)){p.PriceRoundingMultiple=Math.Clamp(multiple,1,999999999);SavePolicy();}}
        Setting("Apply a global stack limit",p.UseMaxStackSize,v=>p.UseMaxStackSize=v);
        if(p.UseMaxStackSize){var max=p.MaximumStackSize;if(ImGui.InputInt("Global maximum stack",ref max)){p.MaximumStackSize=Math.Clamp(max,1,9999);SavePolicy();}}
        ImGui.EndDisabled();
        ImGui.TextWrapped("Steward never undercuts your own retainers. Stack sizes also respect the item's stack limit and the matching sale rule. Configure disposal methods in AutoRetainer.");
        ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Automation"))
        {
        DrawMarketFeatureControls();
        Setting("Prefer faster-selling listings",config.Rules.PreferFasterSales,v=>config.Rules.PreferFasterSales=v,"Prioritise listing quantity divided by average daily sales. Rotate only for a strictly faster replacement with known current activity. Market-wide activity is not a guaranteed sale time.");
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Price data"))
        {
        Section("Price data");
        Setting("Use Universalis sale summaries",config.UseUniversalis,v=>config.UseUniversalis=v);
        var hours=config.Rules.SummaryRefreshHours;if(ImGui.SliderInt("Universalis refresh interval (hours)",ref hours,1,2)){config.Rules.SummaryRefreshHours=hours;SavePolicy();}
        var allaganHours=config.Rules.AllaganCacheHours;
        if(ImGui.SliderInt("Allagan Market cache validity (hours)",ref allaganHours,1,168)){config.Rules.AllaganCacheHours=allaganHours;SavePolicy();}
        ImGui.TextWrapped("Reuse Allagan Market prices to skip unchanged listings. Older cache data can delay detecting an undercut until the cache updates or expires. Price changes still require a verified in-game comparison.");
        var days=config.Rules.SummaryHistoryDays;if(ImGui.InputInt("Sales history window (days)",ref days)){config.Rules.SummaryHistoryDays=Math.Clamp(days,7,90);SavePolicy();}
        var listingAge=config.Rules.MaximumPriceAgeMinutes;
        if(ImGui.InputInt("Observed listing price validity (minutes)",ref listingAge)){config.Rules.MaximumPriceAgeMinutes=Math.Clamp(listingAge,1,1440);SavePolicy();}
        ImGui.TextWrapped("The listing-price validity applies to manually observed or imported offers. Prices prefetch on login and remain cached across restarts. Actual posting and price changes still require a verified game comparison; unchanged listings can be skipped from the Allagan cache.");
        ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
    private void DrawSaleRuleEditor(SaleRule rule)
    {
            var name=rule.Name;if(ImGui.InputText("Name",ref name,100)){rule.Name=name;SavePolicy();}
        if(!ImGui.BeginTabBar("RuleEditor",ImGuiTabBarFlags.FittingPolicyScroll))return;
        if(ImGui.BeginTabItem("Eligibility"))
        {
        Setting("Restrict this rule to selected retainers",rule.UseRetainerFilter,v=>rule.UseRetainerFilter=v,"Optional. Global retainer exclusions always apply.");
        if(rule.UseRetainerFilter)DrawRetainerSelection(rule.ExcludedRetainers,true);
            Section("Items matched by this rule");DrawSaleRuleCategories(rule);
            var quality=rule.Hq==null?0:rule.Hq==false?1:2;if(ImGui.Combo("Quality",ref quality,"Any quality\0NQ only\0HQ only\0")){rule.Hq=quality==0?null:quality==2;SavePolicy();}
            if(ImGui.CollapsingHeader("Restrict to specific items"))
            {
                ImGui.TextWrapped("Optional: limit this rule to named items. Category and quality restrictions still apply.");
                DrawItemPicker(id=>{rule.ItemIds.Add(id);SavePolicy();});DrawNamedItems(rule.ItemIds,id=>{rule.ItemIds.Remove(id);SavePolicy();});
            }
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Price & stacks"))
        {
            var unit=(int)Math.Min(rule.MinimumUnit,999999999);var total=(int)Math.Min(rule.MinimumTotal,999999999);var stack=rule.MaximumStack;
            if(ImGui.InputInt("Minimum gil per item",ref unit)){rule.MinimumUnit=(uint)Math.Clamp(unit,1,999999999);SavePolicy();}
            if(ImGui.InputInt("Minimum gil per listing",ref total)){rule.MinimumTotal=(uint)Math.Clamp(total,1,999999999);SavePolicy();}
            Setting("Fill spare slots with lower-value items",rule.AllowLowPriority,v=>rule.AllowLowPriority=v,
                "Normal-priority stock is listed first across all rules. Lower-priority items remain protected for sale, use the same activity threshold and cannot replace normal-priority listings.");
            if(rule.AllowLowPriority)
            {
                ImGui.Indent();
                var lowUnit=(int)Math.Min(rule.LowPriorityMinimumUnit,999999999);
                var lowTotal=(int)Math.Min(rule.LowPriorityMinimumTotal,999999999);
                if(ImGui.InputInt("Spare-slot minimum gil per item",ref lowUnit)){rule.LowPriorityMinimumUnit=(uint)Math.Clamp(lowUnit,1,(int)Math.Max(1,rule.MinimumUnit));SavePolicy();}
                if(ImGui.InputInt("Spare-slot minimum gil per listing",ref lowTotal)){rule.LowPriorityMinimumTotal=(uint)Math.Clamp(lowTotal,1,(int)Math.Max(1,rule.MinimumTotal));SavePolicy();}
                if(ImGui.SmallButton("General preset: 20,000 gil")){rule.LowPriorityMinimumUnit=rule.LowPriorityMinimumTotal=20000;SavePolicy();}
                ImGui.SameLine();
                if(ImGui.SmallButton("Materials preset: 500 each / 2,500 total")){rule.LowPriorityMinimumUnit=500;rule.LowPriorityMinimumTotal=2500;SavePolicy();}
                ImGui.TextWrapped("These are lower limits, not target prices. Existing protections, stack sizes and sales-activity requirements still apply.");
                ImGui.Unindent();
            }
            if(ImGui.InputInt("Maximum items per stack",ref stack)){rule.MaximumStack=Math.Clamp(stack,1,9999);SavePolicy();}
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Demand & rotation"))
        {
            Setting("Require minimum sales activity",rule.MinimumDailySales.HasValue,v=>rule.MinimumDailySales=v ? 0.1 : null);
            if(rule.MinimumDailySales.HasValue)
            {var velocity=(float)rule.MinimumDailySales.Value;if(ImGui.InputFloat("Minimum average units sold per day",ref velocity,.01f,.1f,"%.3f")){rule.MinimumDailySales=float.IsFinite(velocity)?Math.Max(0,velocity):0;SavePolicy();}}
            if(rule.MinimumDailySales is >0)ImGui.TextWrapped($"{rule.MinimumDailySales.Value:0.###} units/day means about one unit every {1/rule.MinimumDailySales.Value:0.#} days. This is market-wide demand, not your own retainer's sales.");
            Setting("Rotate long-unsold listings when another item is waiting",rule.MaximumListingDays.HasValue,v=>rule.MaximumListingDays=v?60:null);
            if(rule.MaximumListingDays.HasValue)
            {var days=rule.MaximumListingDays.Value;if(ImGui.InputInt("Maximum listing window (days)",ref days)){rule.MaximumListingDays=Math.Clamp(days,1,3650);SavePolicy();}}
            ImGui.TextWrapped("Rotation only replaces a full market slot when another eligible item is waiting. Returned items join the waiting list and follow the normal collection, sale and disposal rules. Existing listings start tracking when first observed; repricing does not reset their window.");
            Setting("Allow older known values for slow sellers",rule.AllowOlderSaleValue,v=>rule.AllowOlderSaleValue=v);
            ImGui.TextWrapped("Missing activity data holds an item when a minimum is enabled. Older prices may support a sale proposal, but cannot authorize disposal. Price floors also apply to the final listing price.");
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
    private void DrawStoragePage()
    {
        ImGui.TextWrapped("Armoire first, then glamour dresser. If the dresser has no usable space, protected items go to retainers and remain protected. Steward alerts you when collection cannot finish.");
        DrawStorageSettings();
        ImGui.BeginDisabled(!config.ExecutionEnabled||!config.CleanDresser||executionFault);
        if(ImGui.Button("Clean open dresser now"))market.CleanupDresser();
        ImGui.EndDisabled();
        Section("Inventory memory");
        ImGui.TextWrapped("Retainer, dresser and armoire observations are saved per character without a time limit. Opening storage refreshes its contents. Cached collection copies never authorize disposal or removal of a protected last copy.");
    }
    private void DrawDisposalPermissions()
    {
        Section("Disposal permissions");
        Setting("Allow AutoRetainer disposal",config.Features.Disposal,v=>config.Features.Disposal=v,"Allows eligible items to use the disposal methods enabled in AutoRetainer. Demo mode still blocks disposal.");
        DrawCategorySelection("Allow disposal of these non-equipment categories",config.Rules.NonEquipmentDisposalCategories,"None allowed",
            "Selected categories may be disposed of after protection and sale checks. Items passing a sale rule stay eligible for sale. Items with no matching sale rule may also be disposed of. Missing price data, exclusions, keeps and quantity reserves remain holds. AutoRetainer's enabled methods and stack limits still apply.",nonEquipmentOnly:true);
        ImGui.TextWrapped("Category permission does not enable disposal or override special-reward protection. Unknown acquisition sources are held for review. Review proposed disposals in log-only mode before running them.");
    }
    private void DrawItemPicker(Action<uint> choose)
    {
        var key=ImGui.GetID("ItemSearchState");
        var query=itemQueries.GetValueOrDefault(key,"");
        if(ImGui.InputTextWithHint("##ItemSearch","Find an item by name or ID...",ref query,120))
        {itemQueries[key]=query;itemSearchResults[key]=view.SearchItems(query);}
        if(query.Length<2){ImGui.TextDisabled("Type at least two characters.");return;}
        var results=itemSearchResults.GetValueOrDefault(key,[]);
        if(results.Count==0)ImGui.TextDisabled("No matching items.");
        if(ImGui.BeginChild("ItemMatches",new(0,130),true))
            foreach(var (id,name) in results)if(ImGui.Selectable($"{name}##{id}")){choose(id);itemQueries.Remove(key);itemSearchResults.Remove(key);break;}
        ImGui.EndChild();
    }
    private void DrawNamedItems(IEnumerable<uint> ids,Action<uint> remove)
    {
        foreach(var id in ids.ToArray())
        {ImGui.PushID((int)id);ImGui.TextUnformatted(view.Facts(id)?.Name??$"Unavailable item ({id})");ImGui.SameLine();if(ImGui.SmallButton("Remove"))remove(id);ImGui.PopID();}
    }
    private void ToggleMembership(string title,HashSet<uint> set,uint id)
    {Setting(title,set.Contains(id),v=>{if(v)set.Add(id);else set.Remove(id);});}
    private void DrawOverridesPage()
    {
        ImGui.TextWrapped("Exclude items completely, keep physical copies, or add items to your collection. These settings apply across sale and storage rules.");
        DrawCategorySelection("Exclude categories from all handling",config.Rules.IgnoredCategories,"No categories excluded","Checked types are completely ignored by Steward: no listing, disposal, collection or cleanup. Empty means no category exclusions.");
        Section("Find an item");DrawItemPicker(id=>selectedItem=id);
        if(selectedItem!=0&&view.Facts(selectedItem) is {} item)
        {
            Section(item.Name);
            ToggleMembership("Exclude from all Steward handling",config.Rules.Ignored,item.Id);
            ToggleMembership("Keep in inventory",config.Rules.Keep,item.Id);
            ToggleMembership("Keep one in the glamour collection",config.Rules.CollectionKeep,item.Id);
            var qty=(int)Math.Min(config.Rules.QuantityReserves.GetValueOrDefault(item.Id),999999);
            if(ImGui.InputInt("Copies to keep outside the collection",ref qty)){if(qty<=0)config.Rules.QuantityReserves.Remove(item.Id);else config.Rules.QuantityReserves[item.Id]=(uint)Math.Clamp(qty,1,999999);SavePolicy();}
            ImGui.TextWrapped("Exclusions take priority. Quantity reserves require live inventory evidence; cached quantities never justify removing reserved copies.");
            if(!item.Equipment)ToggleMembership("Allow disposal of this non-equipment item",config.Rules.NonEquipmentDisposal,item.Id);
            ImGui.TextWrapped("A disposal permission still requires a safe decision and an enabled AutoRetainer disposal method. Matching sale rules take priority.");
            if(ImGui.CollapsingHeader("Acquisition override"))
            {
                var source=config.Sources.TryGetValue(item.Id,out var value)?(int)value+1:0;
                if(ImGui.Combo("Source classification",ref source,"Automatic\0Unknown\0Ordinary / crafted\0Special reward\0Normal dungeon\0Normal raid\0Alliance raid\0Savage raid\0Other high-end duty\0"))
                {if(source==0)config.Sources.Remove(item.Id);else config.Sources[item.Id]=(SourceKind)(source-1);SavePolicy();}
                ImGui.TextWrapped("An override can add source protection. It cannot downgrade a known harder source from the game data.");
            }
        }
        Section("Configured items");
        var configured=config.Rules.Ignored.Concat(config.Rules.Keep).Concat(config.Rules.CollectionKeep).Concat(config.Rules.NonEquipmentDisposal).Concat(config.Rules.QuantityReserves.Keys).Concat(config.Sources.Keys).Distinct().ToArray();
        if(configured.Length==0)ImGui.TextDisabled("No item overrides.");
        foreach(var id in configured)if(ImGui.Selectable($"{view.Facts(id)?.Name??id.ToString()}##Override{id}",selectedItem==id))selectedItem=id;
    }
    private void DrawIntegrationsPage()
    {
        Section("AutoRetainer");
        ImGui.TextWrapped("AutoRetainer owns retainer visits, storage and disposal methods. Configure those features in AutoRetainer; Steward supplies collection and sale decisions.");
        ImGui.TextUnformatted(config.Connected?"Steward integration enabled":"Steward integration detached");
        if(!config.Connected&&ImGui.Button("Connect Steward")){config.Connected=true;SavePolicy();}
        if(config.Connected&&ImGui.Button("Disconnect Steward from this character"))DetachPolicy();
        ImGui.TextWrapped("Without Steward, AutoRetainer can run native entrust, ventures and gil withdrawal. Disposal, automated sales and glamour work stay paused. Saved collection holds remain available when you reconnect.");
        Section("Glamaholic and native plates");
        ImGui.TextWrapped("Choose which saved outfits protect items on Glamour filters.");if(ImGui.Button("Open glamour filters"))shell.Open(Desk.Glamour);
        Section("Market Buddy and Universalis");
        ImGui.TextWrapped("Pricing preferences, undercutting and cache intervals belong on Sale rules.");if(ImGui.Button("Open sale rules"))shell.Open(Desk.Sales);
        Section("Allagan Tools and Allagan Market");
        ImGui.TextWrapped("Optional inventory and market data are imported read-only when available. Saved inventory observations allow prefetching without reopening every retainer. Other plugins' settings are never edited.");
    }
    private void DetachPolicy()
    {
        Stop();
        try{if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Detach").InvokeFunc(player.ContentId,Endpoint)){config.Connected=false;attached=0;Save();}else Alert("Resolve the stopped run and stop AutoRetainer's queue before disconnecting.");}
        catch(Exception ex){Alert("Could not disconnect Steward: "+ex.Message);}
    }
    private void DrawDeveloperPage()
    {
        ImGui.TextWrapped("For testing and troubleshooting. Ordinary configuration is on the setup pages.");
        DrawReviewSettings();
        StewardShell.Diagnostics(status,imports.Status,universalis.Status,universalis.Freshness,tracked.Status,dungeonSources.Status);
        if(ImGui.CollapsingHeader("Diagnostic history"))foreach(var line in history)ImGui.TextWrapped(line);
    }
    private void DrawRecoveryControls()
    {
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
                    executionFault=config.ExecutionReviewRequired=false;config.PendingOperation=null;config.PendingCharacter=0;
                    try{Save();}catch{config.PendingOperation=pending;config.PendingCharacter=pendingCharacter;executionFault=config.ExecutionReviewRequired=true;throw;}
                    market.Queue.Begin();Record("User cleared both stopped-run holds after inventory review; enabled features resume on the next authorised AutoRetainer run");
                }
            }
            catch(Exception ex){Record("Could not clear the integration hold: "+ex.Message);}
        }

    }
}
