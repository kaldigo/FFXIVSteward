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
            var choices=CategoryDisplay.Build(view.Categories,view.CategoryDetails)
                .Where(c=>c.Id!=39||selected.Contains(39)).ToArray();
            var visible=choices.Where(c=>query.Length==0||c.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||
                c.Group.Contains(query,StringComparison.OrdinalIgnoreCase)||c.Members.Any(id=>view.Categories[id].Contains(query,StringComparison.OrdinalIgnoreCase)||id.ToString().Contains(query))).ToArray();
            if(visible.Length==0)ImGui.TextDisabled("No matching categories.");
            foreach(var groupName in CategoryDisplay.Groups)
            {
                var group=visible.Where(c=>c.Group==groupName).ToArray();if(group.Length==0)continue;
                ImGui.PushID(groupName);
                Section(groupName);
                if(ImGui.SmallButton("Select all")){CategoryDisplay.Set(selected,choices.Where(c=>c.Group==groupName),true);SavePolicy();}
                ImGui.SameLine();if(ImGui.SmallButton("Clear all")){CategoryDisplay.Set(selected,choices.Where(c=>c.Group==groupName),false);SavePolicy();}
                if(ImGui.IsItemHovered())ImGui.SetTooltip("Applies to the whole group, including categories hidden by search. An empty overall selection uses the default shown above.");
                var right=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X;
                var size=ImGui.GetFrameHeight();
                var width=size*2+ImGui.GetStyle().ItemSpacing.X;
                var first=true;
                foreach(var category in group)
                {
                    var id=category.Id;var name=category.Name;
                    if(!first&&ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+width<=right)ImGui.SameLine();
                    first=false;ImGui.PushID((int)id);ImGui.BeginGroup();
                    var count=category.Members.Count(selected.Contains);var partial=count>0&&count<category.Members.Count;
                    var enabled=count==category.Members.Count;
                    var changed=ImGui.Checkbox("##Selected",ref enabled);
                    if(partial)
                    {
                        var corner=ImGui.GetItemRectMin();
                        ImGui.GetWindowDrawList().AddLine(new(corner.X+size*.25f,corner.Y+size*.5f),new(corner.X+size*.75f,corner.Y+size*.5f),ImGui.GetColorU32(ImGuiCol.CheckMark),2);
                    }
                    ImGui.SameLine();
                    var icon=category.Icon;
                    if(icon!=0&&textures.GetFromGameIcon(new GameIconLookup(icon,hiRes:true)).TryGetWrap(out var wrap,out _))ImGui.Image(wrap.Handle,new(size,size));
                    else ImGui.Dummy(new(size,size));
                    if(ImGui.IsItemClicked()){enabled=!enabled;changed=true;}
                    ImGui.EndGroup();
                    if(ImGui.IsItemHovered())ImGui.SetTooltip(name+(partial?" — partially selected; click to select both":enabled?" — selected":" — not selected")+"\n"+string.Join(", ",category.Members.Select(member=>view.Categories[member])));
                    if(changed){CategoryDisplay.Set(selected,[category],enabled);SavePolicy();}
                    ImGui.PopID();
                }
                ImGui.PopID();
            }
            foreach(var id in selected.Where(id=>!view.Categories.ContainsKey(id)).ToArray())
            {var enabled=true;if(ImGui.Checkbox($"Unavailable category {id}",ref enabled)){selected.Remove(id);SavePolicy();}}
        }
        ImGui.EndChild();ImGui.PopID();
    }
}
