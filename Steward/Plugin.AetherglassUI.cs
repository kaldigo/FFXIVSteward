using System.Numerics;
using Dalamud.Bindings.ImGui;
using AetherglassUI;
using Steward.Core;

namespace Steward;

public sealed partial class Plugin
{
    private readonly WindowShell workspaceWindow=new();
    private static readonly Desk[] workspaces=[Desk.Activity,Desk.Inventory,Desk.Market,Desk.Collection,Desk.Setup];
    private readonly Dictionary<Desk,SettingsPage> workspacePages=workspaces.ToDictionary(d=>d,_=>new SettingsPage());
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<HashSet<uint>,CategoryEditor> categoryEditors=new();
    private readonly Dictionary<(bool Sale,bool NonEquipment),CategoryChoice[]> categoryChoices=[];
    private Destination[]? workspaceRail;
    private Desk? previousDesk;
    private void NavigateWorkspace(Desk target)
    {
        var desk=target switch {
            Desk.Sales or Desk.Strategies=>Desk.Market,Desk.Glamour or Desk.Storage=>Desk.Collection,
            Desk.Disposal or Desk.Items=>Desk.Inventory,Desk.Connections or Desk.Review=>Desk.Setup,
            Desk.Readiness=>Desk.Activity,_=>target
        };
        previousDesk=Enum.TryParse<Desk>(workspaceWindow.Selected,out var old)&&old!=desk?old:null;
        workspaceWindow.Selected=desk.ToString();
        var page=workspacePages[desk];
        page.Query="";
        page.SetCollapsed(target.ToString(),false);
        page.ScrollToGroup(target.ToString());
    }

    private void DrawAetherglassWindow()
    {
        if(workspaceRail==null){
            shell.Navigate=NavigateWorkspace;
            workspaceRail=workspaces.Select(d=>new Destination(d.ToString(),d.ToString(),"",()=>DrawWorkspacePage(d)){
                DrawIcon=(p,size,color)=>StewardShell.Glyph((int)d,p,size/24,color)
            }).ToArray();
            foreach(var id in new[]{Desk.Market,Desk.Strategies})workspacePages[Desk.Market].SetCollapsed(id.ToString(),true);
            workspacePages[Desk.Market].SetCollapsed("pricing",true);
            workspacePages[Desk.Setup].SetCollapsed("Review",true);
            workspacePages[Desk.Collection].SetCollapsed("Storage",true);
            workspacePages[Desk.Inventory].SetCollapsed("Disposal",true);
        }
        workspaceWindow.Close=()=>open=false;
        workspaceWindow.Dock=()=>{
            if(ImGui.Button("Stop run"))Stop();
            ImGui.SameLine();ImGui.TextUnformatted(RunContext);
        };
        workspaceWindow.Draw("Steward",player.IsLoaded?"Inventory management":"Waiting for character",workspaceRail,DrawHeaderMark);
    }
    private void DrawWorkspacePage(Desk desk)
    {
        if(previousDesk is {} back && back!=desk && ImGui.SmallButton("Back to "+back)){
            workspaceWindow.Selected=back.ToString();previousDesk=null;return;
        }
        if(executionFault && desk!=Desk.Activity && ImGui.Button("Inventory check required — review on Activity"))NavigateWorkspace(Desk.Activity);
        if((view.Character,view.World)!=decisionOwner){
            decisions.Clear();selectedItem=0;itemQueries.Clear();itemSearchResults.Clear();decisionOwner=(view.Character,view.World);
        }
        var pages=workspacePages[desk];
        Group[] groups=desk switch{
            Desk.Activity=>[Body(Desk.Activity,"Current activity",DrawOverviewPage),Body(Desk.Readiness,"Readiness",DrawReadiness)],
            Desk.Inventory=>[Body(Desk.Inventory,"Inventory inspection",DrawDecisions),ItemSettings(),
                new("Disposal","Disposal permissions",new Topic("Eligibility",
                    Toggle("Allow AutoRetainer disposal",config.Features.Disposal,v=>config.Features.Disposal=v),
                    Categories("Permitted non-equipment categories",config.Rules.NonEquipmentDisposalCategories,"None allowed","Protection and sale rules take priority.",nonEquipment:true)))],
            Desk.Market=>[Body(Desk.Market,"Selling report",DrawSalePreview),
                Body(Desk.Sales,"Sale rules",()=>{ImGui.TextWrapped("The first enabled matching rule sets the price and stack limits.");if(ImGui.Button("Add sale rule")){var rule=new SaleRule();config.Rules.Sales.Add(rule);SavePolicy();pages.SetCollapsed(RuleKey(rule),false);}}),
                ..config.Rules.Sales.Select(RuleGroup),MarketSettings(),SilverSettings()],
            Desk.Collection=>[new("destinations","Collection destinations",new Topic("Destinations",
                    Toggle("Collect into glamour dresser",config.Features.DresserCollection,v=>config.Features.DresserCollection=v),
                    Toggle("Store eligible items in armoire",config.Features.ArmoireStorage,v=>config.Features.ArmoireStorage=v))),
                GearSettings(),StorageSettings()],
            _=>[ConnectionSettings(),DeveloperSettings(),Body(Desk.Readiness,"Recovery",()=>{
                ImGui.TextWrapped(executionFault||config.ExecutionReviewRequired?"Inventory review required.":"No stopped run awaiting review.");
                DrawRecoveryControls();
            })]
        };
        if(desk==Desk.Collection)ImGui.TextWrapped($"Awaiting collection: {(view.Character==0?"Unknown":(config.PendingCollection.GetValueOrDefault(view.Character)?.Count??0).ToString())}   ·   Dresser slots free: {view.Evidence.DresserFreeSlots?.ToString()??"Unknown"}");
        pages.Draw("steward-"+desk,groups,searchable:desk!=Desk.Activity,inlineTopics:true);
    }
    private static Group Body(Desk id,string label,Action draw)=>new(id.ToString(),label){Body=draw};
    private AetherglassUI.Setting Toggle(string label,bool value,Action<bool> set,string detail="")=>new(label,label,()=>{
        if(Controls.Switch("value",value)!=value){set(!value);SavePolicy();}
    },Detail:detail){Layout=FieldLayout.Switch};
    private AetherglassUI.Setting Number(string label,int value,int min,int max,Action<int> set)=>new(label,label,()=>{
        int edited=value;Controls.Integer(ref edited,min,max);if(edited!=value){set(edited);SavePolicy();}
    }){Layout=FieldLayout.Number};
    private AetherglassUI.Setting Categories(string label,HashSet<uint> values,string empty,string detail,bool sale=false,bool nonEquipment=false)
    {
        // Only resolve game icons and build choices for visible rows.
        AetherglassUI.Setting? active=null;
        return new(label,label,()=>{active=BuildCategoryRow();active.Draw();},Detail:detail){
            ExpandedContent=()=>active?.ExpandedContent?.Invoke()
        };
        AetherglassUI.Setting BuildCategoryRow()
        {
            if(!categoryChoices.TryGetValue((sale,nonEquipment),out var choices)){
                choices=(sale?CategoryDisplay.Build(view.SaleCategories,view.SaleCategoryDetails):CategoryDisplay.Build(view.Categories,view.CategoryDetails))
                    .Where(c=>c.Id!=62&&(!nonEquipment||c.Group is "Items" or "Housing")).ToArray();
                categoryChoices[(sale,nonEquipment)]=choices;
            }
            var editor=categoryEditors.GetOrCreateValue(values);
            var names=sale?view.SaleCategories:view.Categories;
            var options=choices.Select(c=>new CategoryOption(c.Name,c.Group,c.Members.Select(m=>m.ToString()).ToArray(),
                string.Join(" ",c.Members.Select(m=>names[m])),c.Id!=39,
                editor.Expanded?CategoryIcon(c.Icon):null)).ToArray();
            var selection=values.Select(v=>v.ToString()).ToHashSet();
            return editor.Row(label,label,options,selection,empty,detail,()=>{values.Clear();values.UnionWith(selection.Select(uint.Parse));SavePolicy();});
        }
    }
    private string RuleKey(SaleRule rule)
    {
        if(!ruleUiIds.TryGetValue(rule,out var key)){
            ruleUiIds[rule]=key=Guid.NewGuid().ToString();
            workspacePages[Desk.Market].SetCollapsed(key,true);
        }
        return key;
    }
    private Group RuleGroup(SaleRule rule)
    {
        var index=config.Rules.Sales.IndexOf(rule);
        var key=RuleKey(rule);
        return new Group(key,$"{index+1}. {rule.Name}",new Topic("Eligibility",
            new("name","Rule name",()=>{var name=rule.Name;if(Controls.Text(ref name,100)){rule.Name=name;SavePolicy();}}),
            Toggle("Enable rule",rule.Enabled,v=>rule.Enabled=v),
            new("order","Rule priority",()=>{
                var list=config.Rules.Sales;int i=list.IndexOf(rule);
                if(i>0&&ImGui.SmallButton("Up")){(list[i],list[i-1])=(list[i-1],list[i]);SavePolicy();}
                if(i+1<list.Count&&ImGui.SmallButton("Down")){(list[i],list[i+1])=(list[i+1],list[i]);SavePolicy();}
                Controls.Confirm("Delete","Remove this sale rule?","Remove",()=>{list.Remove(rule);ruleUiIds.Remove(rule);SavePolicy();});
            }),
            new("legacy","Legacy item group",()=>{if(ImGui.Button("Use categories alone")){rule.Group=ItemGroup.Any;SavePolicy();}},Detail:rule.Group.ToString(),Visible:()=>rule.Group!=ItemGroup.Any),
            Categories("Include categories",rule.Categories,"All categories","Empty includes all categories.",sale:true),
            Categories("Exclude categories",rule.ExcludedCategories,"None excluded","Excluded items can match later rules.",sale:true),
            new("quality","Quality",()=>{string current=rule.Hq switch{true=>"HQ only",false=>"NQ only",_=>"Any quality"};
                var next=Controls.Choice("##quality",current,"Any quality","NQ only","HQ only");if(next!=current){rule.Hq=next=="Any quality"?null:next=="HQ only";SavePolicy();}}),
            new("items","Restrict to specific items",()=>ImGui.TextUnformatted($"{rule.ItemIds.Count} selected")){
                ExpandedContent=()=>{DrawItemPicker(id=>{rule.ItemIds.Add(id);SavePolicy();});DrawNamedItems(rule.ItemIds,id=>{rule.ItemIds.Remove(id);SavePolicy();});}},
            Toggle("Restrict to selected retainers",rule.UseRetainerFilter,v=>rule.UseRetainerFilter=v),
            new("allowed","Allowed retainers",()=>DrawRetainerSelection(rule.ExcludedRetainers,true),Visible:()=>rule.UseRetainerFilter)),
            new Topic("Price & stacks",
                Number("Minimum gil per item",(int)Math.Min(rule.MinimumUnit,999999999),1,999999999,v=>rule.MinimumUnit=(uint)v),
                Number("Minimum gil per listing",(int)Math.Min(rule.MinimumTotal,999999999),1,999999999,v=>rule.MinimumTotal=(uint)v),
                Number("Maximum items per stack",rule.MaximumStack,1,9999,v=>rule.MaximumStack=v),
                Toggle("Fill spare slots with lower-value items",rule.AllowLowPriority,v=>rule.AllowLowPriority=v),
                Number("Spare-slot minimum gil per item",(int)rule.ListingMinimumUnit,1,(int)Math.Clamp(rule.MinimumUnit,1,999999999),v=>rule.LowPriorityMinimumUnit=(uint)v) with {Visible=()=>rule.AllowLowPriority},
                Number("Spare-slot minimum gil per listing",(int)rule.ListingMinimumTotal,1,(int)Math.Clamp(rule.MinimumTotal,1,999999999),v=>rule.LowPriorityMinimumTotal=(uint)v) with {Visible=()=>rule.AllowLowPriority},
                new("presets","Spare-slot presets",()=>{if(ImGui.SmallButton("General")){rule.LowPriorityMinimumUnit=rule.LowPriorityMinimumTotal=20000;SavePolicy();}
                    if(ImGui.SmallButton("Materials")){rule.LowPriorityMinimumUnit=500;rule.LowPriorityMinimumTotal=2500;SavePolicy();}},Visible:()=>rule.AllowLowPriority)),
            new Topic("Demand & rotation",
                Toggle("Require minimum sales activity",rule.MinimumDailySales.HasValue,v=>rule.MinimumDailySales=v?0.1:null),
                new("rate","Minimum units sold per day",()=>{var old=(float)(rule.MinimumDailySales??0);var value=old;Controls.Decimal(ref value,0,float.MaxValue);if(old!=value){rule.MinimumDailySales=value;SavePolicy();}},Visible:()=>rule.MinimumDailySales.HasValue),
                Toggle("Rotate long-unsold listings",rule.MaximumListingDays.HasValue,v=>rule.MaximumListingDays=v?60:null),
                Number("Maximum listing window (days)",rule.MaximumListingDays??60,1,3650,v=>rule.MaximumListingDays=v) with {Visible=()=>rule.MaximumListingDays.HasValue},
                Toggle("Allow older known values for slow sellers",rule.AllowOlderSaleValue,v=>rule.AllowOlderSaleValue=v)))
            {Status=rule.Enabled?"Enabled":"Disabled",Summary=$"{rule.MinimumUnit:N0} gil each · {rule.MinimumTotal:N0} per listing · {rule.MaximumStack} per stack"};
    }
    private Group MarketSettings()
    {
        var p=config.Pricing;var r=config.Rules;var f=config.Features;
        string? PricingSource()=>config.ImportMarketBuddy?"Read-only · Market Buddy supplies this preference.":null;
        return new("pricing","Market settings",new Topic("Automation",
            new("retainers","Eligible sale retainers",()=>DrawRetainerSelection(config.ExcludedSaleRetainers,false)),
            Toggle("List new items",f.NewListings,v=>f.NewListings=v),
            Toggle("Reprice existing listings",f.Repricing,v=>f.Repricing=v),
            Toggle("Apply sale rules to existing listings",f.ListingReturns,v=>f.ListingReturns=v),
            Toggle("Rotate expired listings",f.Rotation,v=>f.Rotation=v) with {DisabledReason=()=>f.NewListings?null:"Enable new listings first."},
            Toggle("Prefer faster-selling listings",r.PreferFasterSales,v=>r.PreferFasterSales=v)),
            new Topic("Pricing",Toggle("Compare HQ and NQ together",r.Quality==QualityMode.Combined,v=>r.Quality=v?QualityMode.Combined:QualityMode.Separate),
                Toggle("Use Market Buddy pricing preferences",config.ImportMarketBuddy,v=>config.ImportMarketBuddy=v),
                Toggle("Undercut by percentage",p.UndercutUsePercent,v=>p.UndercutUsePercent=v) with {DisabledReason=PricingSource},
                Number("Undercut percentage",p.UndercutPercent,0,99,v=>p.UndercutPercent=v) with {Visible=()=>p.UndercutUsePercent,DisabledReason=PricingSource},
                Number("Undercut gil",p.UndercutPrice,0,999999999,v=>p.UndercutPrice=v) with {Visible=()=>!p.UndercutUsePercent,DisabledReason=PricingSource},
                Toggle("Round listing prices",p.EnablePriceRounding,v=>p.EnablePriceRounding=v) with {DisabledReason=PricingSource},
                Number("Round down to multiples of",p.PriceRoundingMultiple,1,999999999,v=>p.PriceRoundingMultiple=v) with {Visible=()=>p.EnablePriceRounding,DisabledReason=PricingSource},
                Toggle("Apply global stack limit",p.UseMaxStackSize,v=>p.UseMaxStackSize=v) with {DisabledReason=PricingSource},
                Number("Global maximum stack",p.MaximumStackSize,1,9999,v=>p.MaximumStackSize=v) with {Visible=()=>p.UseMaxStackSize,DisabledReason=PricingSource}),
            new Topic("Price data",Toggle("Use Universalis sale summaries",config.UseUniversalis,v=>config.UseUniversalis=v),
                Number("Universalis refresh interval (hours)",r.SummaryRefreshHours,1,2,v=>r.SummaryRefreshHours=v),
                Number("Allagan Market cache validity (hours)",r.AllaganCacheHours,1,168,v=>r.AllaganCacheHours=v),
                Number("Sales history window (days)",r.SummaryHistoryDays,7,90,v=>r.SummaryHistoryDays=v),
                Number("Observed listing price validity (minutes)",r.MaximumPriceAgeMinutes,1,1440,v=>r.MaximumPriceAgeMinutes=v)));
    }

    private Group SilverSettings()
    {
        var silver=config.Silver;
        if(!silverPricesLoaded)LoadSilverPrices();
        silverStackEdits??=[..silver.Stacks];
        bool priceDirty=silverMinimum!=silver.MinimumPrice||silverReset!=silver.ResetPrice||silverUndercut!=silver.Undercut;
        bool stackDirty=!silverStackEdits.SequenceEqual(silver.Stacks);
        var limit=view.ItemStackLimit(SilverStrategy.ItemId);
        AetherglassUI.Setting DraftNumber(string label,Action draw)=>new(label,label,draw){Layout=FieldLayout.Number};
        return new("Strategies","Metallic Silver Dye strategy",new Topic("Assignment",
            new AetherglassUI.Setting("retainer","Strategy retainer",()=>{
                if(!ImGui.BeginCombo("##retainer",view.RetainerNames.GetValueOrDefault(silver.Retainer,"Select retainer")))return;
                foreach(var pair in view.RetainerNames)
                    if(ImGui.Selectable(pair.Value+"###"+pair.Key,pair.Key==silver.Retainer)){silver.Retainer=pair.Key;SavePolicy();}
                ImGui.EndCombo();
            }){DisabledReason=()=>priceDirty||stackDirty?"Apply or discard pending edits first.":null},
            Toggle("Enable silver-dye strategy",silver.Enabled,v=>silver.Enabled=v) with {DisabledReason=()=>silver.Valid?null:"Select a retainer and valid prices and stacks."}),
            new Topic("Prices per dye",
                DraftNumber("Minimum selling price (gil)",()=>Controls.Integer(ref silverMinimum,1,999999999)),
                DraftNumber("Reset price (gil)",()=>Controls.Integer(ref silverReset,1,999999999)),
                DraftNumber("Undercut (gil)",()=>Controls.Integer(ref silverUndercut,1,1000)),
                new("price-draft","Price changes",()=>Editors.DraftActions(SilverStrategy.PriceInputError(silverMinimum,silverReset,silverUndercut)??"",
                    priceDirty,()=>{if(silver.TrySetPrices(silverMinimum,silverReset,silverUndercut))SavePolicy();},LoadSilverPrices),
                    Detail:$"Active: {silver.MinimumPrice:N0} minimum · {silver.ResetPrice:N0} reset · {silver.Undercut:N0} undercut")),
            new Topic("Stack mix",new AetherglassUI.Setting("mix","Listing stack targets",()=>ImGui.TextUnformatted($"{silverStackEdits.Sum(x=>(long)x.Count)}/20 listings"),
                Detail:"Changing the mix fills free slots as items sell."){
                ExpandedContent=()=>{
                    int remove=-1;
                    Editors.Table("mix",["Dyes per stack","Stacks",""],silverStackEdits.Count,(i,column)=>{
                        var row=silverStackEdits[i];
                        if(column==0){int q=(int)row.Quantity;Controls.Integer(ref q,1,(int)Math.Max(1,limit));if(q!=row.Quantity)silverStackEdits[i]=row with {Quantity=(uint)q};}
                        if(column==1){int n=row.Count;Controls.Integer(ref n,1,20);if(n!=row.Count)silverStackEdits[i]=row with {Count=n};}
                        if(column==2&&ImGui.SmallButton("Remove"))remove=i;
                    });
                    if(remove>=0)silverStackEdits.RemoveAt(remove);
                    if(silverStackEdits.Count<20&&ImGui.SmallButton("Add stack size"))silverStackEdits.Add(new(1,1));
                    Editors.DraftActions(SilverStrategy.StackInputError(silverStackEdits,limit)??"",!silverStackEdits.SequenceEqual(silver.Stacks),
                        ()=>{if(silver.TrySetStacks(silverStackEdits,limit))SavePolicy();},()=>silverStackEdits=[..silver.Stacks]);
                }}));
    }

    private Group GearSettings()
    {
        var f=config.Rules.Glamour;
        return new("Glamour","Protected gear",new Topic("Saved outfits",
            Toggle("In-game glamour plates",f.NativePlates,v=>f.NativePlates=v),
            Toggle("Glamaholic saved outfits",f.Glamaholic,v=>f.Glamaholic=v)),
            new Topic("Equipment",Toggle("Eorzea Collection favourites",f.EorzeaCollection,v=>f.EorzeaCollection=v),
                Toggle("Level 1 equipment",f.LevelOne,v=>f.LevelOne=v),Toggle("Equipment designed for glamour",f.GlamourDesign,v=>f.GlamourDesign=v),
                Toggle("Nonmarketable blue equipment",f.NonmarketableBlue,v=>f.NonmarketableBlue=v,"Normal dungeon and normal raid gear are excluded from this filter."),
                Toggle("Equipment above blue rarity",f.HigherRarity,v=>f.HigherRarity=v),
                Categories("Apply automatic filters to",f.Categories,"All equipment categories","Limits automatic filters to these gear types."),
                Categories("Skip automatic filters for",f.ExcludedCategories,"None skipped","Saved outfits, manual collection and pending holds still apply.")),
            new Topic("Acquisition",Toggle("Special rewards",f.SpecialRewards,v=>f.SpecialRewards=v),
                Toggle("Alliance raids",f.AllianceRaids,v=>f.AllianceRaids=v),Toggle("Savage raids",f.SavageRaids,v=>f.SavageRaids=v),
                Toggle("Other high-end duties",f.HighEndDuties,v=>f.HighEndDuties=v),Toggle("Normal raids",f.NormalRaids,v=>f.NormalRaids=v),
                Toggle("Normal dungeons",f.NormalDungeons,v=>f.NormalDungeons=v),Jump("Manual collection items",Desk.Items)));
    }

    private AetherglassUI.Setting Jump(string label,Desk desk)=>new(label,label,()=>{if(ImGui.Button("Open"))NavigateWorkspace(desk);});
    private Group StorageSettings()
    {
        return new("Storage","Storage and cleanup",new Topic("Overflow",[
            Toggle("Store protected overflow in retainers",config.Features.OverflowStorage,v=>config.Features.OverflowStorage=v,"AutoRetainer manages destinations and quantities."),
            ..view.RetainerNames.OrderBy(x=>x.Value).Select(pair=>Toggle("Manage stored items on "+pair.Value,config.Storage.RetainerEnabled(pair.Key),
                v=>{if(v)config.Storage.ExcludedRetainers.Remove(pair.Key);else config.Storage.ExcludedRetainers.Add(pair.Key);}) with {Id="storage"+pair.Key}),
            Categories("Never store these categories",config.Storage.ExcludedCategories,"None excluded","Triple Triad cards always stay with you."),
            new("store","Store protected overflow now",()=>{if(ImGui.Button("Store"))StartOverflow();}){
                DisabledReason=()=>config.ExecutionEnabled&&!executionFault&&config.Features.OverflowStorage?null:"Requires execution and overflow storage."}]),
            new Topic("Dresser",Number("Leave empty dresser slots",config.Rules.DresserEmptySlots,0,800,v=>config.Rules.DresserEmptySlots=v),
                Toggle("Clean dresser using all protection rules",config.CleanDresser,v=>config.CleanDresser=v),
                new("preview","Preview dresser cleanup",()=>{if(ImGui.Button("Preview")){cleanupPreview.Clear();RefreshProtection();cleanupPreview.AddRange(market.PreviewDresserCleanup());}}){
                    ExpandedContent=()=>{foreach(var line in cleanupPreview)ImGui.TextWrapped(line);}},
                new("clean","Clean open dresser now",()=>{if(ImGui.Button("Clean"))market.CleanupDresser();}){
                    DisabledReason=()=>config.ExecutionEnabled&&config.CleanDresser&&!executionFault?null:"Requires execution and dresser cleanup."}));
    }

    private readonly SearchableItemList overrideList = new();

    private ItemListEntry[]? configuredOverrides;

    private Group ItemSettings()
    {
        var rules = config.Rules;
        var list = new AetherglassUI.Setting("items", "Item overrides", () =>
            {
                var ids = rules.Ignored.Concat(rules.Keep).Concat(rules.CollectionKeep)
                    .Concat(rules.NonEquipmentDisposal).Concat(rules.QuantityReserves.Keys).Concat(config.Sources.Keys);
                configuredOverrides ??= ids.Distinct().Select(id => new ItemListEntry(id, view.Facts(id)?.Name ?? "Unavailable item")).ToArray();
                overrideList.Draw(configuredOverrides,
                    query => view.SearchItems(query).Select(i => new ItemListEntry(i.Id, i.Name)).ToArray(), OpenItemSettings, "Configured overrides", "Add an override");
            }, "configured find item by name or ID") { Layout = FieldLayout.FullWidth };
        return new("Items", "Item overrides", new Topic("Inventory policy",
            Categories("Exclude categories from all handling",rules.IgnoredCategories,"No categories excluded","Applies to sales, collection, storage and disposal."), list));
    }

    private void OpenItemSettings(uint id)
    {
        var detail = new SettingsPage();
        overrideList.OpenDetail(workspaceWindow, workspacePages[Desk.Inventory], "override-" + id,
            view.Facts(id)?.Name ?? $"Item {id}", () => detail.Draw("item-policy", [ItemPolicy(id)], searchable: false));
    }

    private Group ItemPolicy(uint id)
    {
        var r = config.Rules;
        var rows = new List<AetherglassUI.Setting>();
        if (view.Facts(id) is {} item) {
            AetherglassUI.Setting Member(string label,HashSet<uint> set)=>Toggle(label,set.Contains(id),v=>{if(v)set.Add(id);else set.Remove(id);},item.Name) with {Id=id+label};
            rows.Add(Member("Exclude from all Steward handling",r.Ignored));
            rows.Add(Member("Keep in inventory",r.Keep));
            rows.Add(Member("Keep one in the glamour collection",r.CollectionKeep));
            rows.Add(Number("Copies to keep outside collection",(int)Math.Min(999999,r.QuantityReserves.GetValueOrDefault(id)),0,999999,
                v=>{if(v==0)r.QuantityReserves.Remove(id);else r.QuantityReserves[id]=(uint)v;}) with {Id="reserve"+id,Detail=item.Name});
            if(!item.Equipment)rows.Add(Member("Allow disposal of this non-equipment item",r.NonEquipmentDisposal));
            rows.Add(new("source"+id,"Acquisition override",()=>{
                string[] sources=["Automatic","Unknown","Ordinary / crafted","Special reward","Normal dungeon","Normal raid","Alliance raid","Savage raid","Other high-end duty"];
                var index=config.Sources.TryGetValue(id,out var source)?(int)source+1:0;
                var current=sources[Math.Clamp(index,0,sources.Length-1)];
                var next=Controls.Choice("##source",current,sources);
                if(next!=current){if(next==sources[0])config.Sources.Remove(id);else config.Sources[id]=(SourceKind)(Array.IndexOf(sources,next)-1);SavePolicy();}
            },Detail:item.Name));
        }
        else rows.Add(new("unavailable", "Item data unavailable", () => {}, Detail: $"Item ID {id}"));
        return new("policy", "Item settings", new Topic("Policy", rows.ToArray()));
    }

    private Group ConnectionSettings()=>new("Connections","Connections",new Topic("Integrations",
        new("retainer","Connect to AutoRetainer",()=>{
            var enabled=config.Connected;
            if(Controls.Switch("connected",enabled)==enabled)return;
            if(enabled)DetachPolicy();else{config.Connected=true;SavePolicy();}
        },Detail:"Applies to this character."){Layout=FieldLayout.Switch},
        Jump("Glamaholic and native plates",Desk.Glamour),
        new("pricing","Market Buddy and Universalis",()=>{if(ImGui.Button("Open")){NavigateWorkspace(Desk.Market);workspacePages[Desk.Market].SetCollapsed("pricing",false);workspacePages[Desk.Market].ScrollToGroup("pricing");}}),
        new("allagan","Allagan Tools and Allagan Market",()=>ImGui.TextUnformatted("Read-only"),Detail:"Optional inventory and market observations.")));

    private Group DeveloperSettings()=>new("Review","Developer review",new Topic("Diagnostics",
        new("log","Log-only review",()=>{bool value=Controls.Switch("value",config.LogOnly);if(value!=config.LogOnly)SetReviewMode(value);},
            Detail:"Record proposals and block managed item actions."){Layout=FieldLayout.Switch},
        Toggle("Log disposal candidates",config.LogDisposal,v=>config.LogDisposal=v),
        new("snapshot","Write review snapshot",()=>{if(ImGui.Button("Record")){reviewJournal.Reset();nextReview=0;}},Detail:ReviewPath){
            DisabledReason=()=>config.LogOnly?null:"Enable log-only review first."},
        new("status","Diagnostic status",()=>ImGui.TextWrapped(status)){ExpandedContent=()=>{foreach(var line in new[]{imports.Status,universalis.Status,universalis.Freshness,tracked.Status,dungeonSources.Status})ImGui.TextWrapped(line);}},
        new("history","Diagnostic history",()=>ImGui.TextUnformatted($"{history.Count} events")){ExpandedContent=()=>{foreach(var line in history)ImGui.TextWrapped(line);}}));
}
