using Dalamud.Bindings.ImGui;
using Steward.Core;

namespace Steward;

public sealed partial class Plugin
{
    private string categorySearch="";
    private void DrawSaleRuleCategories(SaleRule rule)
    {
        var group=rule.Group;
        var label=group switch{ItemGroup.Gear=>"Gear",ItemGroup.Other=>"Other items (materials, consumables, etc.)",_=>"All items"};
        if(ImGui.BeginCombo("Item group",label))
        {
            foreach(var value in Enum.GetValues<ItemGroup>())
                if(ImGui.Selectable(value switch{ItemGroup.Gear=>"Gear",ItemGroup.Other=>"Other items (materials, consumables, etc.)",_=>"All items"},group==value))
                {rule.Group=value;Save();}
            ImGui.EndCombo();
        }
        DrawCategorySelection("Included categories",rule.Categories,"All categories within the group");
        DrawCategorySelection("Excluded categories for this rule",rule.ExcludedCategories,"None excluded");
        ImGui.TextWrapped("Choose material types such as cloth, leather, metal or reagents for a materials rule. Gear/other scope and categories both apply; exclusions win within this rule.");
    }
    private void DrawCategorySelection(string label,HashSet<uint> selected,string emptyLabel)
    {
        if(!ImGui.BeginCombo(label,selected.Count==0?emptyLabel:$"{selected.Count} categories selected",ImGuiComboFlags.HeightLarge))return;
        ImGui.PushID(label);
        ImGui.InputText("Find category",ref categorySearch,100);
        if(ImGui.Button("Select all categories")){selected.UnionWith(view.Categories.Keys);Save();}
        ImGui.SameLine();if(ImGui.Button("Clear selection")){selected.Clear();Save();}
        foreach(var (id,name) in view.Categories.OrderBy(p=>p.Value))
        {
            if(categorySearch.Length>0&&!name.Contains(categorySearch,StringComparison.OrdinalIgnoreCase)&&!id.ToString().Contains(categorySearch))continue;
            var enabled=selected.Contains(id);
            if(ImGui.Checkbox($"{name}##{id}",ref enabled)){if(enabled)selected.Add(id);else selected.Remove(id);Save();}
        }
        foreach(var id in selected.Where(id=>!view.Categories.ContainsKey(id)).ToArray())
        {var enabled=true;if(ImGui.Checkbox($"Unavailable category {id}",ref enabled)){selected.Remove(id);Save();}}
        ImGui.PopID();ImGui.EndCombo();
    }
}
