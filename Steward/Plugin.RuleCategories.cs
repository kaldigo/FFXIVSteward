using System.Numerics;
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
        var choices=CategoryDisplay.Build(view.Categories,view.CategoryDetails)
            .Where(c=>c.Id!=62&&(c.Id!=39||selected.Contains(39))).ToArray();
        var selectedNames=choices.Where(c=>c.Members.Any(selected.Contains))
            .Select(c=>c.Name+(c.Members.All(selected.Contains)?"":" (partial)")).ToArray();
        var summary=selected.Count==0?emptyLabel:selectedNames.Length==0?"Saved hidden category selection":$"{selectedNames.Length} selected";
        if(!ImGui.CollapsingHeader($"{label} — {summary}###{label}"))return;
        ImGui.PushID(label);
        if(ImGui.BeginChild("CategoryDetails",new(0,ImGui.GetTextLineHeightWithSpacing()*19),true))
        {
            var query=categorySearches.GetValueOrDefault(ImGui.GetID(label).ToString(),"");
            ImGui.SetNextItemWidth(-1);
            if(ImGui.InputTextWithHint("##Search","Find a category...",ref query,100))categorySearches[ImGui.GetID(label).ToString()]=query;
            ImGui.TextWrapped(description);
            ImGui.TextDisabled("Click an icon to toggle it. Highlighted icons are selected; amber borders indicate a partial selection. Hover for the category name.");
            if(selectedNames.Length>0)ImGui.TextWrapped("Selected: "+string.Join(", ",selectedNames.Take(8))+(selectedNames.Length>8?" …":""));
            if(ImGui.SmallButton("Select all")){CategoryDisplay.Set(selected,choices.Where(c=>c.Id!=39),true);SavePolicy();}
            ImGui.SameLine();if(ImGui.SmallButton("Clear selection")){selected.Clear();SavePolicy();}
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
                var padding=ImGui.GetStyle().FramePadding.Y;
                var width=size+padding*2;
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(padding,padding));
                var first=true;
                foreach(var category in group)
                {
                    var id=category.Id;var name=category.Name;
                    if(!first&&ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+width<=right)ImGui.SameLine();
                    first=false;ImGui.PushID((int)id);
                    var count=category.Members.Count(selected.Contains);var partial=count>0&&count<category.Members.Count;
                    var enabled=count==category.Members.Count;
                    if(enabled)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button,new Vector4(.10f,.42f,.55f,.95f));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered,new Vector4(.16f,.56f,.70f,1));
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive,new Vector4(.08f,.35f,.48f,1));
                    }
                    var icon=category.Icon;
                    var clicked=icon!=0&&textures.GetFromGameIcon(new GameIconLookup(icon,hiRes:true)).TryGetWrap(out var wrap,out _)
                        ?ImGui.ImageButton(wrap.Handle,new(size,size))
                        :ImGui.Button("?##Category",new(width,width));
                    if(enabled)ImGui.PopStyleColor(3);
                    if(enabled||partial)
                        ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(),ImGui.GetItemRectMax(),
                            ImGui.GetColorU32(partial?new Vector4(1,.70f,.20f,1):new Vector4(.35f,.85f,1,1)),3,ImDrawFlags.None,2);
                    if(ImGui.IsItemHovered())ImGui.SetTooltip(name+(partial?" — partially selected; click to select both":enabled?" — selected":" — not selected")+"\n"+string.Join(", ",category.Members.Select(member=>view.Categories[member])));
                    if(clicked){CategoryDisplay.Set(selected,[category],!enabled);SavePolicy();}
                    ImGui.PopID();
                }
                ImGui.PopStyleVar();ImGui.PopID();
            }
            foreach(var id in selected.Where(id=>!view.Categories.ContainsKey(id)).ToArray())
            {var enabled=true;if(ImGui.Checkbox($"Unavailable category {id}",ref enabled)){selected.Remove(id);SavePolicy();}}
        }
        ImGui.EndChild();ImGui.PopID();
    }
}
