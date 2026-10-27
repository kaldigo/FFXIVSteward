using System.Numerics;
using Dalamud.Bindings.ImGui;
using Steward.Core;

namespace Steward;

public sealed partial class Plugin
{
    private int settingsPage;
    private readonly Dictionary<SaleRule,string> ruleUiIds=[];
    private string navigationSearch="";
    private uint selectedItem;
    private SaleRule? expandSaleRule;
    private readonly Dictionary<uint,string> itemQueries=[];
    private readonly Dictionary<uint,IReadOnlyList<(uint Id,string Name)>> itemSearchResults=[];
    private static readonly string[] Pages=["Overview","Sale rules","Glamour filters","Storage","Item overrides","Integrations","Developer tools","Experimental strategies"];
    private static readonly string[] PageKeywords=["automation pause status inventory","market prices stacks quality undercut sales","collection outfits plates rarity dungeon raid favourites","dresser armoire overflow cleanup space","exclude keep reserve quantities categories disposal","AutoRetainer Market Buddy Universalis connection","logs diagnostics log-only testing","silver dye dedicated retainer stacks reset buyout strategy"];
    private void SavePolicy()
    {
        config.Automatic=false;StopQueue();
        config.GlamaholicExpected=config.Rules.Glamour.Glamaholic;
        config.UseBundledGlamourList=config.Rules.Glamour.EorzeaCollection;
        config.ControlsRevision=1;Save();nextImports=0;nextReview=0;marketPreview=null;reviewJournal.Reset();
    }
    private void Setting(string title,bool value,Action<bool> update,string? description=null)
    {
        if(ImGui.Checkbox(title,ref value)){update(value);SavePolicy();}
        if(description!=null)
        {
            ImGui.SameLine();ImGui.TextDisabled("(?)");
            if(ImGui.IsItemHovered()){ImGui.BeginTooltip();ImGui.PushTextWrapPos(ImGui.GetFontSize()*30);ImGui.TextUnformatted(description);ImGui.PopTextWrapPos();ImGui.EndTooltip();}
        }
    }
    private static void Section(string title)
    {ImGui.Spacing();ImGui.Separator();ImGui.Spacing();ImGui.TextUnformatted(title);ImGui.Spacing();}
    private void Draw()
    {
        if(!open)return;
        ImGui.SetNextWindowSize(new(1000,700),ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new(800,500),new(float.MaxValue,float.MaxValue));
        if(!ImGui.Begin("Steward###StewardSettings",ref open)){ImGui.End();return;}
        var width=Math.Clamp(ImGui.GetContentRegionAvail().X*.23f,180,240);
        if(ImGui.BeginChild("Navigation",new(width,0),true))
        {
            var icon=textures.GetFromFile(Path.Combine(pi.AssemblyLocation.DirectoryName!,"images","icon.png")).GetWrapOrDefault();
            if(icon!=null)
            {
                var size=Math.Min(125*ImGui.GetFontSize()/17,ImGui.GetContentRegionAvail().X);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX()+(ImGui.GetContentRegionAvail().X-size)/2);
                ImGui.Image(icon.Handle,new(size,size));ImGui.Spacing();
            }
            ImGui.TextUnformatted("Steward");ImGui.TextDisabled(config.ExecutionEnabled?"Automation enabled":"Automation paused");
            ImGui.Spacing();ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##NavigationSearch","Find a setting...",ref navigationSearch,100);
            for(var i=0;i<Pages.Length;i++)
            {
                if(i==0||i==5||i==6){ImGui.Spacing();ImGui.TextDisabled(i==0?"SETUP":i==5?"CONNECTIONS":"DEVELOPMENT");}
                if(!($"{Pages[i]} {PageKeywords[i]}").Contains(navigationSearch,StringComparison.OrdinalIgnoreCase))continue;
                if(ImGui.Selectable(Pages[i],settingsPage==i))settingsPage=i;
            }
            ImGui.Spacing();if(ImGui.Button("Stop all Steward work",new(-1,0)))Stop();
        }
        ImGui.EndChild();ImGui.SameLine();
        if(ImGui.BeginChild("SettingsPage",Vector2.Zero,false))
        {
            ImGui.TextUnformatted(Pages[settingsPage]);ImGui.Separator();ImGui.Spacing();
            if(executionFault)ImGui.TextWrapped("Automation is stopped because an earlier operation needs an inventory check. Resolve this on Overview.");
            switch(settingsPage)
            {
                case 0:DrawOverviewPage();break;
                case 1:DrawSalesPage();break;
                case 2:DrawGlamourPage();break;
                case 3:DrawStoragePage();break;
                case 4:DrawOverridesPage();break;
                case 5:DrawIntegrationsPage();break;
                case 6:DrawDeveloperPage();break;
                case 7:DrawSilverStrategy();break;
            }
        }
        ImGui.EndChild();ImGui.End();
    }
    private void DrawOverviewPage()
    {
        ImGui.TextWrapped("Choose what to collect and sell. AutoRetainer carries out retainer and disposal work using its own enabled methods.");
        if(config.LogOnly)
        {ImGui.TextWrapped("Developer log-only mode is active. Item actions are disabled.");if(ImGui.SmallButton("Open developer tools"))settingsPage=6;}
        ImGui.BeginDisabled(config.LogOnly||executionFault);
        var automatic=config.Automatic;if(ImGui.Checkbox("Enable automation",ref automatic))SetAutomatic(automatic);
        ImGui.EndDisabled();
        ImGui.TextWrapped("Enabling automation lets Steward join AutoRetainer's retainer runs. It does not start travel or collection by itself. Use Run now to request work manually.");
        ImGui.TextDisabled("Editing rules or storage settings pauses automation.");
        DrawRecoveryControls();
        Section("Readiness");
        ImGui.TextUnformatted(attached==player.ContentId&&attached!=0?"AutoRetainer: connected":"AutoRetainer: waiting for connection");
        ImGui.TextUnformatted(view.Evidence.ReferencesReady?(view.NativeReferencesCached&&config.Rules.Glamour.NativePlates?"Selected glamour references: ready (saved native plates)":"Selected glamour references: ready"):!view.NativeReferencesAvailable&&config.Rules.Glamour.NativePlates?"Waiting for in-game glamour plate data":"Waiting for selected glamour references");
        if(!view.NativeReferencesAvailable&&config.Rules.Glamour.NativePlates)ImGui.TextWrapped("Open the in-game glamour plate editor to load your saved plates.");
        if(view.NativeReferencesCached&&config.Rules.Glamour.NativePlates)ImGui.TextWrapped($"Using saved native plate references from {view.NativeReferencesObservedAt:yyyy-MM-dd HH:mm} UTC. Opening the plate editor refreshes them automatically.");
        if(!view.Evidence.GlamaholicKnown&&config.Rules.Glamour.Glamaholic)ImGui.TextWrapped("Glamaholic is selected but its saved outfits are unavailable. Check that the plugin is installed and configured.");
        ImGui.TextUnformatted(view.Evidence.DresserKnown?"Glamour dresser: ready":"Glamour dresser: open it to verify current contents");
        ImGui.TextUnformatted(view.Evidence.ArmoireKnown?"Armoire: ready":"Armoire: open it to verify current contents");
        Section("Your setup");
        ImGui.TextUnformatted($"{config.Rules.Sales.Count(r=>r.Enabled)} enabled sale rules · {config.Rules.Ignored.Count} item exclusions");
        ImGui.TextUnformatted($"Dresser space reserved: {config.Rules.DresserEmptySlots} slots");
        if(config.PendingCollection.TryGetValue(view.Character,out var waiting)&&waiting.Count>0)ImGui.TextWrapped($"{waiting.Count} protected items are waiting for collection. They remain protected in retainer storage.");
        Section("Features");
        ImGui.TextWrapped("Choose the work Steward should do. Disabling a feature preserves its settings and all item protections. Changes pause automation.");
        DrawMarketFeatureControls();
        Setting("Prefer faster-selling listings",config.Rules.PreferFasterSales,v=>config.Rules.PreferFasterSales=v,"Prioritise listing quantity divided by average daily sales. Rotate only for a strictly faster replacement with known current activity. Market-wide activity is not a guaranteed sale time.");
        if(ImGui.CollapsingHeader("Glamour collection",ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawCollectionFeatureControls();
            Setting("Clean the glamour dresser",config.CleanDresser,v=>config.CleanDresser=v,"Restore unneeded items. Moving dresser pieces to the armoire also requires armoire storage to be enabled.");
        }
        if(ImGui.CollapsingHeader("Storage and disposal",ImGuiTreeNodeFlags.DefaultOpen))
        {
            Setting("Store protected overflow in retainers",config.Features.OverflowStorage,v=>config.Features.OverflowStorage=v,"When disabled, protected overflow stays in inventory and remains protected.");
            Setting("Allow AutoRetainer disposal",config.Features.Disposal,v=>config.Features.Disposal=v,"Allow eligible vendor sales, desynthesis and Grand Company deliveries through AutoRetainer. Turning this off blocks these managed actions while Steward is connected. AutoRetainer demo mode also blocks all three methods independently, so collection can run while disposal stays paused.");
        }
        Section("Run now");
        ImGui.BeginDisabled(!config.ExecutionEnabled||executionFault);
        DrawCleanupControls();
        ImGui.BeginDisabled(cleanupActive);
        ImGui.BeginDisabled(!config.Features.AnyMarket);
        if(ImGui.Button("List / reprice current retainer"))market.Start();
        ImGui.EndDisabled();
        ImGui.BeginDisabled(!config.Features.DresserCollection&&!config.CleanDresser);
        if(ImGui.Button("Collect glamour items"))Run("Dresser");
        ImGui.EndDisabled();ImGui.SameLine();ImGui.BeginDisabled(!config.Features.ArmoireStorage);
        if(ImGui.Button("Store armoire items"))Run("Armoire");
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        Section("Current inventory");
        ImGui.InputTextWithHint("##InventorySearch","Search item names...",ref search,128);
        if(ImGui.BeginTable("InventorySummary",3,ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersInnerH|ImGuiTableFlags.ScrollY,new(0,200)))
        {
            ImGui.TableSetupColumn("Item");ImGui.TableSetupColumn("Quantity");ImGui.TableSetupColumn("Decision");ImGui.TableHeadersRow();
            foreach(var stock in view.Items)
            {
                var item=view.Facts(stock.ItemId);if(item==null||!item.Name.Contains(search,StringComparison.OrdinalIgnoreCase))continue;
                ImGui.TableNextRow();ImGui.TableNextColumn();ImGui.TextUnformatted(item.Name+(stock.Hq?" (HQ)":""));
                ImGui.TableNextColumn();ImGui.TextUnformatted(stock.Quantity.ToString());ImGui.TableNextColumn();
                ImGui.TextUnformatted(Judge(stock).Outcome switch{Outcome.Ignore=>"Excluded",Outcome.Keep=>"Keep",Outcome.CollectArmoire=>"Collect in armoire",Outcome.CollectDresser=>"Collect in dresser",Outcome.StoreRetainer=>"Protected overflow",Outcome.Market=>"Eligible for sale",Outcome.Dispose=>"AutoRetainer disposal candidate",_=>"Waiting for evidence"});
            }
            ImGui.EndTable();
        }
    }
    private void DrawGlamourPage()
    {
        DrawCollectionFeatureControls();
        ImGui.TextWrapped("Collection switches control item movement. The protection filters below still protect items when collection is disabled.");
        ImGui.TextWrapped("These filters decide which items need one copy in the armoire or dresser. A match from ANY enabled filter protects that copy. Extra copies can be sold or disposed of only after live collection ownership is confirmed.");
        var f=config.Rules.Glamour;
        Section("Saved outfits");
        Setting("In-game glamour plates",f.NativePlates,v=>f.NativePlates=v);
        Setting("Glamaholic saved outfits",f.Glamaholic,v=>f.Glamaholic=v,"Includes nested folders. Selected reference sources must be available before handling items.");
        Section("Automatic collection filters");
        Setting("Eorzea Collection favourites",f.EorzeaCollection,v=>f.EorzeaCollection=v,"Steward's internal, curated popularity list. Frequency is the main signal; likes support it. Maintained with major-patch updates. It supplements the filters below and is not a complete catalogue of popular items.");
        Setting("Level 1 equipment",f.LevelOne,v=>f.LevelOne=v);
        Setting("Equipment designed for glamour",f.GlamourDesign,v=>f.GlamourDesign=v);
        Setting("Nonmarketable blue equipment",f.NonmarketableBlue,v=>f.NonmarketableBlue=v,"Normal dungeon and normal raid gear are excluded from this rarity filter. Marketable blues can still match other filters.");
        Setting("Equipment above blue rarity",f.HigherRarity,v=>f.HigherRarity=v);
        Section("Acquisition sources");
        Setting("Special rewards",f.SpecialRewards,v=>f.SpecialRewards=v,"Includes identified PvP, event and other special-source rewards. These share one source category in the current data.");
        Setting("Alliance raids",f.AllianceRaids,v=>f.AllianceRaids=v);
        Setting("Savage raids",f.SavageRaids,v=>f.SavageRaids=v);
        Setting("Other high-end duties",f.HighEndDuties,v=>f.HighEndDuties=v,"Includes Coil and other duties classified as harder to refarm.");
        Setting("Normal raids",f.NormalRaids,v=>f.NormalRaids=v);
        Setting("Normal dungeons",f.NormalDungeons,v=>f.NormalDungeons=v);
        Section("Which gear types should automatic filters collect?");
        DrawCategorySelection("Apply automatic filters to",f.Categories,"All equipment categories","Empty means all gear types. Selecting categories limits the automatic filters above to those gear types; a category alone does not protect an item.");
        DrawCategorySelection("Skip automatic filters for",f.ExcludedCategories,"No categories skipped","Checked gear types will not be protected by the automatic filters above. Saved outfits, manual collection items, pending holds and unknown-source safeguards still apply.");
        ImGui.TextWrapped("Category scope applies to automatic filters, not saved outfits, manual collection items or pending collection holds.");
        Section("Always preserved");
        ImGui.TextWrapped("Equipped items, gear sets, explicit keep overrides, quantity reserves and pending collection holds retain their protection. Unknown acquisition is held rather than assumed disposable. Armoire-eligible items use the armoire and do not consume dresser space. Dyes do not require extra copies.");
        if(ImGui.Button("Edit manual collection items"))settingsPage=4;
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
        DrawMarketFeatureControls();
        Setting("Prefer faster-selling listings",config.Rules.PreferFasterSales,v=>config.Rules.PreferFasterSales=v,"Prioritise listing quantity divided by average daily sales. Rotate only for a strictly faster replacement with known current activity. Market-wide activity is not a guaranteed sale time.");
        if(!ImGui.BeginTabBar("SalesSections"))return;
        if(ImGui.BeginTabItem("Retainers"))
        {
            ImGui.TextWrapped("Ordinary sale automation only runs on checked retainers. Experimental strategies have their own narrowly scoped item exceptions. Each ordinary sale rule can narrow this selection further.");
            DrawRetainerSelection(config.ExcludedSaleRetainers,false);
            ImGui.TextWrapped("Existing listings on enabled retainers are checked against the rules and withdrawn when they no longer qualify. Returned items follow normal collection, sale and disposal rules.");
            ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Rules"))
        {
        ImGui.TextWrapped("Rules run from top to bottom. The first enabled rule matching an item supplies its price, activity and stack thresholds. Collection protection and global exclusions take priority.");
        if(ImGui.Button("Add sale rule")){expandSaleRule=new();config.Rules.Sales.Add(expandSaleRule);SavePolicy();}
        if(config.Rules.Sales.Count==0)ImGui.TextWrapped("Add your first rule, then choose item categories and price thresholds.");
        foreach(var rule in config.Rules.Sales.ToArray())
        {
            if(!ruleUiIds.TryGetValue(rule,out var key))ruleUiIds[rule]=key=Guid.NewGuid().ToString();
            ImGui.PushID(key);
            var index=config.Rules.Sales.IndexOf(rule);
            ImGui.Spacing();ImGui.Separator();ImGui.Spacing();
            if(ReferenceEquals(expandSaleRule,rule)){ImGui.SetNextItemOpen(true);expandSaleRule=null;}
            var expanded=ImGui.CollapsingHeader($"{index+1}. {rule.Name}###RuleDetails");
            Setting("Enabled",rule.Enabled,v=>rule.Enabled=v);
            ImGui.SameLine();ImGui.BeginDisabled(index==0);
            if(ImGui.SmallButton("Move up")){(config.Rules.Sales[index-1],config.Rules.Sales[index])=(rule,config.Rules.Sales[index-1]);SavePolicy();}
            ImGui.EndDisabled();ImGui.SameLine();ImGui.BeginDisabled(index==config.Rules.Sales.Count-1);
            if(ImGui.SmallButton("Move down")){(config.Rules.Sales[index+1],config.Rules.Sales[index])=(rule,config.Rules.Sales[index+1]);SavePolicy();}
            ImGui.EndDisabled();ImGui.SameLine();
            var deleted=ImGui.SmallButton("Delete");
            if(deleted){config.Rules.Sales.Remove(rule);ruleUiIds.Remove(rule);SavePolicy();}
            ImGui.TextWrapped($"At least {rule.MinimumUnit:N0} gil each / {rule.MinimumTotal:N0} gil per listing · Up to {rule.MaximumStack} per stack");
            var scope=rule.Group==ItemGroup.Gear?"Equipment only · ":rule.Group==ItemGroup.Other?"Non-equipment only · ":"";
            ImGui.TextWrapped(scope+(rule.Categories.Count==0?"All categories":string.Join(", ",rule.Categories.Take(4).Select(id=>view.Categories.GetValueOrDefault(id,"Unavailable")))+(rule.Categories.Count>4?" …":""))+$" · {rule.ExcludedCategories.Count} excluded");
            if(expanded&&!deleted){ImGui.Indent();DrawSaleRuleEditor(rule);ImGui.Unindent();}
            ImGui.PopID();
        }
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
        if(ImGui.BeginTabItem("Price data"))
        {
        Section("Price data");
        Setting("Use Universalis sale summaries",config.UseUniversalis,v=>config.UseUniversalis=v);
        var hours=config.Rules.SummaryRefreshHours;if(ImGui.SliderInt("Price refresh interval (hours)",ref hours,1,2)){config.Rules.SummaryRefreshHours=hours;SavePolicy();}
        var days=config.Rules.SummaryHistoryDays;if(ImGui.InputInt("Sales history window (days)",ref days)){config.Rules.SummaryHistoryDays=Math.Clamp(days,7,90);SavePolicy();}
        var listingAge=config.Rules.MaximumPriceAgeMinutes;
        if(ImGui.InputInt("Observed listing price validity (minutes)",ref listingAge)){config.Rules.MaximumPriceAgeMinutes=Math.Clamp(listingAge,1,1440);SavePolicy();}
        ImGui.TextWrapped("The listing-price validity applies to manually observed or imported offers. Prices prefetch on login and remain cached across restarts. Actual posting and repricing still check the game market.");
        ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Listing preview")){DrawSalePreview();ImGui.EndTabItem();}
        ImGui.EndTabBar();
    }
    private void DrawSaleRuleEditor(SaleRule rule)
    {
        Setting("Restrict this rule to selected retainers",rule.UseRetainerFilter,v=>rule.UseRetainerFilter=v,"Optional. Global retainer exclusions always apply.");
        if(rule.UseRetainerFilter)DrawRetainerSelection(rule.ExcludedRetainers,true);
            var name=rule.Name;if(ImGui.InputText("Name",ref name,100)){rule.Name=name;SavePolicy();}
            Section("Items matched by this rule");DrawSaleRuleCategories(rule);
            var quality=rule.Hq==null?0:rule.Hq==false?1:2;if(ImGui.Combo("Quality",ref quality,"Any quality\0NQ only\0HQ only\0")){rule.Hq=quality==0?null:quality==2;SavePolicy();}
            if(ImGui.CollapsingHeader("Restrict to specific items"))
            {
                ImGui.TextWrapped("Optional: limit this rule to named items. Category and quality restrictions still apply.");
                DrawItemPicker(id=>{rule.ItemIds.Add(id);SavePolicy();});DrawNamedItems(rule.ItemIds,id=>{rule.ItemIds.Remove(id);SavePolicy();});
            }
            Section("Value and activity");
            var unit=(int)Math.Min(rule.MinimumUnit,999999999);var total=(int)Math.Min(rule.MinimumTotal,999999999);var stack=rule.MaximumStack;
            if(ImGui.InputInt("Minimum gil per item",ref unit)){rule.MinimumUnit=(uint)Math.Clamp(unit,1,999999999);SavePolicy();}
            if(ImGui.InputInt("Minimum gil per listing",ref total)){rule.MinimumTotal=(uint)Math.Clamp(total,1,999999999);SavePolicy();}
            if(ImGui.InputInt("Maximum items per stack",ref stack)){rule.MaximumStack=Math.Clamp(stack,1,9999);SavePolicy();}
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
    }
    private void DrawStoragePage()
    {
        ImGui.TextWrapped("Armoire first, then glamour dresser. If the dresser has no usable space, protected items go to retainers and remain protected. Steward alerts you when collection cannot finish.");
        DrawStorageSettings();
        Section("Disposal permissions");
        DrawCategorySelection("Allow disposal of these non-equipment categories",config.Rules.NonEquipmentDisposalCategories,"None allowed",
            "Selected categories may be disposed of after protection and sale checks. Items passing a sale rule stay eligible for sale. Items with no matching sale rule may also be disposed of. Missing price data, exclusions, keeps and quantity reserves remain holds. AutoRetainer's enabled methods and stack limits still apply.",nonEquipmentOnly:true);
        ImGui.TextWrapped("Category permission does not enable disposal. Review proposed disposals in log-only mode before running them.");
        ImGui.BeginDisabled(!config.ExecutionEnabled||!config.CleanDresser||executionFault);
        if(ImGui.Button("Clean open dresser now"))market.CleanupDresser();
        ImGui.EndDisabled();
        Section("Inventory memory");
        ImGui.TextWrapped("Retainer, dresser and armoire observations are saved per character without a time limit. Opening storage refreshes its contents. Cached collection copies never authorize disposal or removal of a protected last copy.");
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
        ImGui.TextWrapped("Disconnecting restores AutoRetainer's own rules. It is blocked while protected overflow or an unresolved operation still needs Steward.");
        Section("Glamaholic and native plates");
        ImGui.TextWrapped("Choose which saved outfits protect items on Glamour filters.");if(ImGui.Button("Open glamour filters"))settingsPage=2;
        Section("Market Buddy and Universalis");
        ImGui.TextWrapped("Pricing preferences, undercutting and cache intervals belong on Sale rules.");if(ImGui.Button("Open sale rules"))settingsPage=1;
        Section("Allagan Tools and Allagan Market");
        ImGui.TextWrapped("Optional inventory and market data are imported read-only when available. Saved inventory observations allow prefetching without reopening every retainer. Other plugins' settings are never edited.");
    }
    private void DetachPolicy()
    {
        Stop();
        if(config.PendingCollection.TryGetValue(player.ContentId,out var waiting)&&waiting.Count>0){Alert("Collect protected overflow before disconnecting Steward.");return;}
        try{if(pi.GetIpcSubscriber<ulong,string,bool>("AutoRetainer.InventoryPolicy.Detach").InvokeFunc(player.ContentId,Endpoint)){config.Connected=false;attached=0;Save();}else Alert("Resolve the stopped run and stop AutoRetainer's queue before disconnecting.");}
        catch(Exception ex){Alert("Could not disconnect Steward: "+ex.Message);}
    }
    private void DrawDeveloperPage()
    {
        ImGui.TextWrapped("For testing and troubleshooting. Ordinary configuration is on the setup pages.");
        DrawReviewSettings();
        Section("Diagnostic status");
        ImGui.TextWrapped(status);ImGui.TextWrapped(imports.Status);ImGui.TextWrapped(universalis.Status);ImGui.TextWrapped(universalis.Freshness);ImGui.TextWrapped(tracked.Status);ImGui.TextWrapped(dungeonSources.Status);
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
                    config.Automatic=false;executionFault=config.ExecutionReviewRequired=false;config.PendingOperation=null;config.PendingCharacter=0;
                    try{Save();}catch{config.PendingOperation=pending;config.PendingCharacter=pendingCharacter;executionFault=config.ExecutionReviewRequired=true;throw;}
                    market.Queue.Begin();Record("User cleared both stopped-run holds after inventory review; automatic execution remains off");
                }
            }
            catch(Exception ex){Record("Could not clear the integration hold: "+ex.Message);}
        }

    }
}
