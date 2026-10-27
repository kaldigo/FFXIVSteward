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
        DrawCategorySelection("Included categories",rule.Categories,"All categories","Empty means every category can match this sale rule. Select categories to limit it to those item types.");
        DrawCategorySelection("Excluded categories for this rule",rule.ExcludedCategories,"None excluded","Checked categories cannot match this rule, even if included above. They can still match a later sale rule.");
    }
    private static string CategorySection(uint id,byte major)=>id is >=64 and <=80 or 82 or 95?"Housing":major switch
    {1=>"Weapons",2=>"Crafting and gathering tools",3=>"Armour and shields",4=>"Accessories",5=>"Consumables",6=>"Materials and components",_=>"Other items"};
    private void DrawCategorySelection(string label,HashSet<uint> selected,string emptyLabel,string description)
    {
        var summary=selected.Count==0?emptyLabel:$"{selected.Count} selected";
        if(!ImGui.CollapsingHeader($"{label} — {summary}###{label}"))return;
        ImGui.PushID(label);
        if(ImGui.BeginChild("CategoryDetails",new(0,ImGui.GetTextLineHeightWithSpacing()*19),true))
        {
            var query=categorySearches.GetValueOrDefault(ImGui.GetID(label).ToString(),"");
            ImGui.SetNextItemWidth(-1);
            if(ImGui.InputTextWithHint("##Search","Find a category...",ref query,100))categorySearches[ImGui.GetID(label).ToString()]=query;
            ImGui.TextWrapped(description);
            ImGui.TextDisabled("Check an icon to select it. Hover over an icon for its category name.");
            if(selected.Count>0)ImGui.TextWrapped("Selected: "+string.Join(", ",selected.Take(8).Select(id=>view.Categories.GetValueOrDefault(id,"Unavailable")))+(selected.Count>8?" …":""));
            if(ImGui.SmallButton("Select all")){selected.UnionWith(view.Categories.Keys.Where(id=>id!=39));SavePolicy();}
            ImGui.SameLine();if(ImGui.SmallButton("Clear selection")){selected.Clear();SavePolicy();}
            var visible=view.Categories.OrderBy(p=>view.CategoryDetails.GetValueOrDefault(p.Key).Major)
                .ThenBy(p=>view.CategoryDetails.GetValueOrDefault(p.Key).Minor)
                .Where(p=>(p.Key!=39||selected.Contains(p.Key))&&(query.Length==0||p.Value.Contains(query,StringComparison.OrdinalIgnoreCase)||CategorySection(p.Key,view.CategoryDetails.GetValueOrDefault(p.Key).Major).Contains(query,StringComparison.OrdinalIgnoreCase)||p.Key.ToString().Contains(query))).ToArray();
            if(visible.Length==0)ImGui.TextDisabled("No matching categories.");
            foreach(var group in visible.GroupBy(p=>CategorySection(p.Key,view.CategoryDetails.GetValueOrDefault(p.Key).Major)))
            {
                Section(group.Key);
                var right=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X;
                var size=ImGui.GetFrameHeight();
                var width=size*2+ImGui.GetStyle().ItemSpacing.X;
                var first=true;
                foreach(var (id,name) in group)
                {
                    if(!first&&ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+width<=right)ImGui.SameLine();
                    first=false;ImGui.PushID((int)id);ImGui.BeginGroup();
                    var enabled=selected.Contains(id);var changed=ImGui.Checkbox("##Selected",ref enabled);
                    ImGui.SameLine();
                    var icon=view.CategoryDetails.GetValueOrDefault(id).Icon;
                    if(icon!=0&&textures.GetFromGameIcon(new GameIconLookup(icon,hiRes:true)).TryGetWrap(out var wrap,out _))ImGui.Image(wrap.Handle,new(size,size));
                    else ImGui.Dummy(new(size,size));
                    if(ImGui.IsItemClicked()){enabled=!enabled;changed=true;}
                    ImGui.EndGroup();
                    if(ImGui.IsItemHovered())ImGui.SetTooltip(name+(enabled?" — selected":" — not selected"));
                    if(changed){if(enabled)selected.Add(id);else selected.Remove(id);SavePolicy();}
                    ImGui.PopID();
                }
            }
            foreach(var id in selected.Where(id=>!view.Categories.ContainsKey(id)).ToArray())
            {var enabled=true;if(ImGui.Checkbox($"Unavailable category {id}",ref enabled)){selected.Remove(id);SavePolicy();}}
        }
        ImGui.EndChild();ImGui.PopID();
    }
}
