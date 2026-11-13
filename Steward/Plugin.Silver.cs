using Dalamud.Bindings.ImGui;
using Steward.Core;
namespace Steward;
public sealed partial class Plugin
{
    private List<SilverStack>? silverStackEdits;
    private bool silverPricesLoaded;
    private int silverMinimum,silverReset,silverUndercut;
    private void LoadSilverPrices()
    {
        silverMinimum=(int)config.Silver.MinimumPrice;
        silverReset=(int)config.Silver.ResetPrice;
        silverUndercut=(int)config.Silver.Undercut;
        silverPricesLoaded=true;
    }
    private static void SilverPriceInput(string label,ref int value,int step,int fastStep)
    {
        ImGui.TextWrapped(label);
        ImGui.SetNextItemWidth(Math.Min(ImGui.GetContentRegionAvail().X,180*StewardShell.Scale));
        ImGui.InputInt("##"+label,ref value,step,fastStep);
    }
    private void DrawSilverStrategy()
    {
        ImGui.TextWrapped("Metallic Silver Dye: a dedicated retainer strategy. Ordinary sales, rotation, storage and disposal exclusions remain in force elsewhere. Global log-only mode always wins.");
        if(ImGui.BeginCombo("Strategy retainer",view.RetainerNames.GetValueOrDefault(config.Silver.Retainer,"Select a retainer")))
        {
            foreach(var (id,name) in view.RetainerNames)
                if(ImGui.Selectable(name,id==config.Silver.Retainer)){config.Silver.Retainer=id;SavePolicy();}
            ImGui.EndCombo();
        }
        ImGui.BeginDisabled(!config.Silver.Valid);
        Setting("Enable silver-dye strategy",config.Silver.Enabled,v=>config.Silver.Enabled=v,"Adds a narrowly scoped market exception for this item and retainer. Log-only mode and inventory-review holds still apply.");
        ImGui.EndDisabled();
        ImGui.TextWrapped("Refill missing target stacks from your bags or this retainer. Other items are untouched. Existing dye listings are repriced but not resized or withdrawn when you change the split; the new mix fills free slots as items sell.");
        Section("Prices per dye");
        if(!silverPricesLoaded)LoadSilverPrices();
        SilverPriceInput("Minimum selling price (gil)",ref silverMinimum,100,1000);
        ImGui.TextWrapped("Lowest allowed price per dye, including the undercut.");
        SilverPriceInput("Reset price (gil)",ref silverReset,100,1000);
        ImGui.TextWrapped("Used when the market is empty or no competitor can be undercut above the minimum.");
        SilverPriceInput("Undercut by (gil)",ref silverUndercut,1,5);
        ImGui.TextWrapped("Amount below the cheapest qualifying competitor; applies when raising or lowering prices.");
        var priceError=SilverStrategy.PriceInputError(silverMinimum,silverReset,silverUndercut);
        var changed=silverMinimum!=config.Silver.MinimumPrice||silverReset!=config.Silver.ResetPrice||silverUndercut!=config.Silver.Undercut;
        if(priceError!=null)ImGui.TextWrapped(priceError);
        ImGui.BeginDisabled(!changed||priceError!=null);
        if(ImGui.Button("Apply prices"))
        {
            if(config.Silver.TrySetPrices(silverMinimum,silverReset,silverUndercut))SavePolicy();
        }
        ImGui.EndDisabled();ImGui.SameLine();
        if(ImGui.Button("Reset edits"))LoadSilverPrices();
        ImGui.TextWrapped($"Active: minimum {config.Silver.MinimumPrice:N0} gil, reset {config.Silver.ResetPrice:N0} gil, undercut {config.Silver.Undercut:N0} gil per dye. Edits take effect after Apply prices; they do not start an automation run.");
        ImGui.TextWrapped($"Your own retainers never count as competition. Competitors too cheap to undercut above the minimum trigger a manual-buyout alert. If no qualifying competitor remains, reset to {config.Silver.ResetPrice:N0} gil. Missing or incomplete prices always hold. No purchases are automated.");
        Section("Listing stack mix");
        silverStackEdits??=[..config.Silver.Stacks];
        if(ImGui.BeginTable("Silver stack targets",3,ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Dyes per stack");ImGui.TableSetupColumn("Number of stacks");ImGui.TableSetupColumn("Actions",ImGuiTableColumnFlags.WidthFixed,ImGui.CalcTextSize("Remove").X+2*ImGui.GetStyle().FramePadding.X);ImGui.TableHeadersRow();
            for(var i=0;i<silverStackEdits.Count;i++)
            {
                ImGui.PushID(i);ImGui.TableNextRow();ImGui.TableNextColumn();
                var quantity=(int)silverStackEdits[i].Quantity;var count=silverStackEdits[i].Count;
                ImGui.SetNextItemWidth(-1);ImGui.InputInt("##dyes",ref quantity,1,5);
                ImGui.TableNextColumn();ImGui.SetNextItemWidth(-1);ImGui.InputInt("##stacks",ref count,1,5);
                silverStackEdits[i]=new((uint)Math.Max(0,quantity),count);
                ImGui.TableNextColumn();var remove=ImGui.SmallButton("Remove");ImGui.PopID();
                if(remove){silverStackEdits.RemoveAt(i);i--;}
            }
            ImGui.EndTable();
        }
        ImGui.BeginDisabled(silverStackEdits.Count>=20);
        if(ImGui.Button("Add stack size"))silverStackEdits.Add(new(1,1));
        ImGui.EndDisabled();
        var stackError=SilverStrategy.StackInputError(silverStackEdits,view.ItemStackLimit(SilverStrategy.ItemId));
        var stackChanged=!silverStackEdits.SequenceEqual(config.Silver.Stacks);
        if(stackError!=null)ImGui.TextWrapped(stackError);
        else ImGui.TextWrapped($"Proposed mix: {silverStackEdits.Sum(s=>s.Count)}/20 listings, {silverStackEdits.Sum(s=>(long)s.Quantity*s.Count):N0} dyes. Groups are filled in the order shown.");
        ImGui.BeginDisabled(!stackChanged||stackError!=null);
        if(ImGui.Button("Apply stack mix")&&config.Silver.TrySetStacks(silverStackEdits,view.ItemStackLimit(SilverStrategy.ItemId)))SavePolicy();
        ImGui.EndDisabled();ImGui.SameLine();
        if(ImGui.Button("Reset stack edits"))silverStackEdits=[..config.Silver.Stacks];
        ImGui.TextWrapped("Active mix: "+string.Join(", ",config.Silver.Stacks.Select(s=>$"{s.Count} stacks of {s.Quantity}")));
        ImGui.TextWrapped("AutoRetainer checks the dedicated strategy during active bell processing, at most once every five minutes. It does not visit other worlds or monitor while offline. In log-only mode, open this retainer's selling page and Compare Prices for the dye to review proposals.");
    }
}
