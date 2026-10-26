using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Steward.Core;

namespace Steward;

public sealed partial class Plugin
{
    private readonly Dictionary<string,string> categorySearches=[];
    private void DrawSaleRuleCategories(SaleRule rule)
    {
        // Preserve old rule semantics without making the broad scope selector part of new setup.
        if(rule.Group!=ItemGroup.Any)
        {
            ImGui.TextWrapped(rule.Group==ItemGroup.Gear?"This existing rule still has its previous equipment-only restriction.":"This existing rule still has its previous non-equipment restriction.");
            if(ImGui.SmallButton("Use categories alone instead")){rule.Group=ItemGroup.Any;SavePolicy();}
        }
        DrawCategorySelection("Included categories",rule.Categories,"All categories");
        DrawCategorySelection("Excluded categories for this rule",rule.ExcludedCategories,"None excluded");
    }
    private static string CategorySection(byte major)=>major switch
    {1=>"Weapons",2=>"Crafting and gathering tools",3=>"Armour and shields",4=>"Accessories",5=>"Consumables",6=>"Materials and components",_=>"Other items"};
    private void DrawCategorySelection(string label,HashSet<uint> selected,string emptyLabel)
    {
        var summary=selected.Count==0?emptyLabel:$"{selected.Count} selected";
        if(!ImGui.CollapsingHeader($"{label} — {summary}###{label}"))return;
        ImGui.PushID(label);
        if(ImGui.BeginChild("CategoryDetails",new(0,ImGui.GetTextLineHeightWithSpacing()*19),true))
        {
            var query=categorySearches.GetValueOrDefault(label,"");
            ImGui.SetNextItemWidth(-1);
            if(ImGui.InputTextWithHint("##Search","Find a category...",ref query,100))categorySearches[label]=query;
            ImGui.TextWrapped(selected.Count==0?emptyLabel:"Checked categories are selected. Exclusions take priority.");
            if(ImGui.SmallButton("Select all")){selected.UnionWith(view.Categories.Keys.Where(id=>id!=39));SavePolicy();}
            ImGui.SameLine();if(ImGui.SmallButton("Clear selection")){selected.Clear();SavePolicy();}
            var visible=view.Categories.OrderBy(p=>view.CategoryDetails.GetValueOrDefault(p.Key).Major)
                .ThenBy(p=>view.CategoryDetails.GetValueOrDefault(p.Key).Minor)
                .Where(p=>(p.Key!=39||selected.Contains(p.Key))&&(query.Length==0||p.Value.Contains(query,StringComparison.OrdinalIgnoreCase)||CategorySection(view.CategoryDetails.GetValueOrDefault(p.Key).Major).Contains(query,StringComparison.OrdinalIgnoreCase)||p.Key.ToString().Contains(query))).ToArray();
            if(visible.Length==0)ImGui.TextDisabled("No matching categories.");
            var columns=Math.Clamp((int)(ImGui.GetContentRegionAvail().X/(ImGui.GetFontSize()*24)),1,3);
            foreach(var group in visible.GroupBy(p=>view.CategoryDetails.GetValueOrDefault(p.Key).Major))
            {
                Section(CategorySection(group.Key));
                if(ImGui.BeginTable($"Categories{group.Key}",columns,ImGuiTableFlags.SizingStretchSame|ImGuiTableFlags.NoSavedSettings))
                {
                    foreach(var (id,name) in group)
                    {
                        ImGui.TableNextColumn();ImGui.PushID((int)id);
                        var enabled=selected.Contains(id);var changed=ImGui.Checkbox("##Selected",ref enabled);
                        ImGui.SameLine();var size=ImGui.GetFrameHeight();
                        var icon=view.CategoryDetails.GetValueOrDefault(id).Icon;
                        if(icon!=0&&textures.GetFromGameIcon(new GameIconLookup(icon,hiRes:true)).TryGetWrap(out var wrap,out _))ImGui.Image(wrap.Handle,new(size,size));
                        else ImGui.Dummy(new(size,size));
                        ImGui.SameLine();ImGui.TextWrapped(name);
                        if(ImGui.IsItemClicked()){enabled=!enabled;changed=true;}
                        if(changed){if(enabled)selected.Add(id);else selected.Remove(id);SavePolicy();}
                        ImGui.PopID();
                    }
                    ImGui.EndTable();
                }
            }
            foreach(var id in selected.Where(id=>!view.Categories.ContainsKey(id)).ToArray())
            {var enabled=true;if(ImGui.Checkbox($"Unavailable category {id}",ref enabled)){selected.Remove(id);SavePolicy();}}
        }
        ImGui.EndChild();ImGui.PopID();
    }
}
