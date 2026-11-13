using Dalamud.Bindings.ImGui;
using Steward.Core;

namespace Steward;

public sealed partial class Plugin
{
    private void DrawProtectionFilters(GlamourFilters f)
    {
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
    }
    private readonly Dictionary<SaleRule,string> ruleUiIds=[];
    private SaleRule? expandSaleRule;

    private void DrawSaleCards()
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
            var enabled=rule.Enabled;
            if(ImGui.Checkbox("Enabled",ref enabled)){rule.Enabled=enabled;SavePolicy();}
            ImGui.SameLine();ImGui.BeginDisabled(index==0);
            if(ImGui.SmallButton("Move up")){(config.Rules.Sales[index-1],config.Rules.Sales[index])=(rule,config.Rules.Sales[index-1]);SavePolicy();}
            ImGui.EndDisabled();ImGui.SameLine();ImGui.BeginDisabled(index==config.Rules.Sales.Count-1);
            if(ImGui.SmallButton("Move down")){(config.Rules.Sales[index+1],config.Rules.Sales[index])=(rule,config.Rules.Sales[index+1]);SavePolicy();}
            ImGui.EndDisabled();ImGui.SameLine();
            var deleted=ImGui.SmallButton("Delete");
            if(deleted){config.Rules.Sales.Remove(rule);ruleUiIds.Remove(rule);SavePolicy();}
            StewardShell.Panel("Rule thresholds",3,()=>{
                StewardShell.Metric("Gil per item",rule.MinimumUnit.ToString("N0"));
                ImGui.TableNextColumn();StewardShell.Metric("Gil per listing",rule.MinimumTotal.ToString("N0"));
                ImGui.TableNextColumn();StewardShell.Metric("Max stack",rule.MaximumStack.ToString("N0"));
            });
            if(rule.MinimumDailySales is {} rate)ImGui.TextWrapped($"Minimum demand: {rate:0.###} sold per day");
            if(rule.AllowLowPriority)ImGui.TextWrapped($"Spare slots: {rule.ListingMinimumUnit:N0} gil each / {rule.ListingMinimumTotal:N0} per listing");
            var scope=rule.Group==ItemGroup.Gear?"Equipment only · ":rule.Group==ItemGroup.Other?"Non-equipment only · ":"";
            ImGui.TextWrapped(scope+(rule.Categories.Count==0?"All categories":string.Join(", ",rule.Categories.Take(4).Select(id=>view.SaleCategories.GetValueOrDefault(id,"Unavailable")))+(rule.Categories.Count>4?" …":""))+$" · {rule.ExcludedCategories.Count} excluded");
            if(expanded&&!deleted){ImGui.Indent();DrawSaleRuleEditor(rule);ImGui.Unindent();}
            ImGui.PopID();
        }
    }
}
