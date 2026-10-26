using System.Numerics;
using Dalamud.Bindings.ImGui;
using Steward.Core;

namespace Steward;

public sealed partial class Plugin
{
    private int settingsPage;
    private int selectedSaleRule;
    private string navigationSearch="";
    private string itemQuery="";
    private uint selectedItem;
    private string lastItemQuery="";
    private IReadOnlyList<(uint Id,string Name)> itemResults=[];
    private static readonly string[] Pages=["Overview","Sale rules","Glamour filters","Storage","Item overrides","Integrations","Developer tools"];
    private static readonly string[] PageKeywords=["automation pause status inventory","market prices stacks quality undercut sales","collection outfits plates rarity dungeon raid favourites","dresser armoire overflow cleanup space","exclude keep reserve quantities categories disposal","AutoRetainer Market Buddy Universalis connection","logs diagnostics log-only testing"];
    private void SavePolicy()
    {
        config.Automatic=false;StopQueue();
        config.GlamaholicExpected=config.Rules.Glamour.Glamaholic;
        config.UseBundledGlamourList=config.Rules.Glamour.EorzeaCollection;
        config.ControlsRevision=1;Save();nextImports=0;nextReview=0;marketPreview=null;
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
        ImGui.TextDisabled("Editing rules or storage settings pauses automation.");
        DrawRecoveryControls();
        Section("Readiness");
        ImGui.TextUnformatted(attached==player.ContentId&&attached!=0?"AutoRetainer: connected":"AutoRetainer: waiting for connection");
        ImGui.TextUnformatted(view.Evidence.ReferencesReady?"Selected glamour references: ready":"Selected glamour references: waiting for data");
        if(!view.NativeReferencesKnown&&config.Rules.Glamour.NativePlates)ImGui.TextWrapped("Open the in-game glamour plate editor to load your saved plates.");
        if(!view.Evidence.GlamaholicKnown&&config.Rules.Glamour.Glamaholic)ImGui.TextWrapped("Glamaholic is selected but its saved outfits are unavailable. Check that the plugin is installed and configured.");
        ImGui.TextUnformatted(view.Evidence.DresserKnown?"Glamour dresser: ready":"Glamour dresser: open it to verify current contents");
        ImGui.TextUnformatted(view.Evidence.ArmoireKnown?"Armoire: ready":"Armoire: open it to verify current contents");
        Section("Your setup");
        ImGui.TextUnformatted($"{config.Rules.Sales.Count(r=>r.Enabled)} enabled sale rules · {config.Rules.Ignored.Count} item exclusions");
        ImGui.TextUnformatted($"Dresser space reserved: {config.Rules.DresserEmptySlots} slots");
        if(config.PendingCollection.TryGetValue(view.Character,out var waiting)&&waiting.Count>0)ImGui.TextWrapped($"{waiting.Count} protected items are waiting for collection. They remain protected in retainer storage.");
        Section("Run now");
        ImGui.BeginDisabled(!config.ExecutionEnabled||executionFault);
        if(ImGui.Button("List / reprice current retainer"))market.Start();
        if(ImGui.Button("Collect glamour items"))Run("Dresser");
        ImGui.SameLine();if(ImGui.Button("Store armoire items"))Run("Armoire");
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
        ImGui.TextWrapped("Keep one exact collection copy when any enabled filter matches. Disabling one filter does not override another. Extra copies are eligible for your sale and disposal rules only after live collection ownership is confirmed.");
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
        Section("Automatic filter scope");
        DrawCategorySelection("Collect these equipment categories",f.Categories,"All equipment categories");
        DrawCategorySelection("Skip these categories for automatic filters",f.ExcludedCategories,"No categories skipped");
        ImGui.TextWrapped("Category scope applies to automatic filters, not saved outfits, manual collection items or pending collection holds.");
        Section("Always preserved");
        ImGui.TextWrapped("Equipped items, gear sets, explicit keep overrides, quantity reserves and pending collection holds retain their protection. Unknown acquisition is held rather than assumed disposable. Armoire-eligible items use the armoire and do not consume dresser space. Dyes do not require extra copies.");
        if(ImGui.Button("Edit manual collection items"))settingsPage=4;
    }
    private void DrawSalesPage()
    {
        if(!ImGui.BeginTabBar("SalesSections"))return;
        if(ImGui.BeginTabItem("Rules"))
        {
        ImGui.TextWrapped("Rules run from top to bottom. The first enabled rule matching an item supplies its price, activity and stack thresholds. Collection protection and global exclusions take priority.");
        if(ImGui.Button("Add sale rule")){config.Rules.Sales.Add(new());selectedSaleRule=config.Rules.Sales.Count-1;SavePolicy();}
        if(config.Rules.Sales.Count>0)
        {
            selectedSaleRule=Math.Clamp(selectedSaleRule,0,config.Rules.Sales.Count-1);
            if(ImGui.BeginCombo("Edit rule",$"{selectedSaleRule+1}. {config.Rules.Sales[selectedSaleRule].Name}"))
            {for(var i=0;i<config.Rules.Sales.Count;i++)if(ImGui.Selectable($"{i+1}. {config.Rules.Sales[i].Name}##{i}",selectedSaleRule==i))selectedSaleRule=i;ImGui.EndCombo();}
            var rule=config.Rules.Sales[selectedSaleRule];
            var name=rule.Name;if(ImGui.InputText("Name",ref name,100)){rule.Name=name;SavePolicy();}
            Setting("Rule enabled",rule.Enabled,v=>rule.Enabled=v);
            ImGui.BeginDisabled(selectedSaleRule==0);if(ImGui.Button("Move up")){var i=selectedSaleRule;(config.Rules.Sales[i-1],config.Rules.Sales[i])=(rule,config.Rules.Sales[i-1]);selectedSaleRule--;SavePolicy();}ImGui.EndDisabled();
            ImGui.SameLine();ImGui.BeginDisabled(selectedSaleRule==config.Rules.Sales.Count-1);if(ImGui.Button("Move down")){var i=selectedSaleRule;(config.Rules.Sales[i+1],config.Rules.Sales[i])=(rule,config.Rules.Sales[i+1]);selectedSaleRule++;SavePolicy();}ImGui.EndDisabled();
            ImGui.SameLine();if(ImGui.Button("Delete rule")){config.Rules.Sales.RemoveAt(selectedSaleRule);SavePolicy();ImGui.EndTabItem();ImGui.EndTabBar();return;}
            Section("Items matched by this rule");DrawSaleRuleCategories(rule);
            var quality=rule.Hq==null?0:rule.Hq==false?1:2;if(ImGui.Combo("Quality",ref quality,"Any quality\0NQ only\0HQ only\0")){rule.Hq=quality==0?null:quality==2;SavePolicy();}
            if(ImGui.CollapsingHeader("Restrict to specific items"))
            {
                ImGui.TextWrapped("An empty list matches any item in this rule's selected group and categories.");
                DrawItemPicker(id=>{rule.ItemIds.Add(id);SavePolicy();});DrawNamedItems(rule.ItemIds,id=>{rule.ItemIds.Remove(id);SavePolicy();});
            }
            Section("Value and activity");
            var unit=(int)Math.Min(rule.MinimumUnit,999999999);var total=(int)Math.Min(rule.MinimumTotal,999999999);var stack=rule.MaximumStack;
            if(ImGui.InputInt("Minimum gil per item",ref unit)){rule.MinimumUnit=(uint)Math.Clamp(unit,1,999999999);SavePolicy();}
            if(ImGui.InputInt("Minimum gil per listing",ref total)){rule.MinimumTotal=(uint)Math.Clamp(total,1,999999999);SavePolicy();}
            if(ImGui.InputInt("Maximum items per stack",ref stack)){rule.MaximumStack=Math.Clamp(stack,1,9999);SavePolicy();}
            Setting("Require minimum sales activity",rule.MinimumDailySales.HasValue,v=>rule.MinimumDailySales=v ? 0.1 : null);
            if(rule.MinimumDailySales.HasValue)
            {var velocity=(float)rule.MinimumDailySales.Value;if(ImGui.InputFloat("Minimum units sold per day",ref velocity,.01f,.1f,"%.3f")){rule.MinimumDailySales=float.IsFinite(velocity)?Math.Max(0,velocity):0;SavePolicy();}}
            Setting("Allow older known values for slow sellers",rule.AllowOlderSaleValue,v=>rule.AllowOlderSaleValue=v);
            ImGui.TextWrapped("Missing activity data holds an item when a minimum is enabled. Older prices may support a sale proposal, but cannot authorize disposal. Price floors also apply to the final listing price.");
        }
        else ImGui.TextWrapped("No sale rules yet. Add a rule for gear, materials or another group.");
        ImGui.EndTabItem();
        }
        if(ImGui.BeginTabItem("Pricing"))
        {
        Section("Market pricing");
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
    private void DrawItemPicker(Action<uint> choose)
    {
        if(ImGui.InputTextWithHint("##ItemSearch","Find an item by name or ID...",ref itemQuery,120)||lastItemQuery!=itemQuery)
        {lastItemQuery=itemQuery;itemResults=view.SearchItems(itemQuery);}
        if(itemQuery.Length<2){ImGui.TextDisabled("Type at least two characters.");return;}
        if(itemResults.Count==0)ImGui.TextDisabled("No matching items.");
        if(ImGui.BeginChild("ItemMatches",new(0,130),true))
            foreach(var (id,name) in itemResults)if(ImGui.Selectable($"{name}##{id}")){choose(id);itemQuery="";lastItemQuery="";itemResults=[];break;}
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
        DrawCategorySelection("Exclude categories from all handling",config.Rules.IgnoredCategories,"No categories excluded");
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
