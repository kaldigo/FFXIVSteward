using Dalamud.Bindings.ImGui;
using Steward.Core;
namespace Steward;
public sealed partial class Plugin
{
    private bool silverPricesLoaded;
    private int silverMinimum,silverReset,silverUndercut;
    private void LoadSilverPrices()
    {
        silverMinimum=(int)config.Silver.MinimumPrice;
        silverReset=(int)config.Silver.ResetPrice;
        silverUndercut=(int)config.Silver.Undercut;
        silverPricesLoaded=true;
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
        ImGui.TextWrapped("Target: ten stacks of 5, five stacks of 2, five singles (65 dyes). Refill missing quotas from your bags or this retainer. Other items are untouched. Differently sized dye listings are repriced but not resized or withdrawn; shortages are logged.");
        Section("Prices per dye");
        if(!silverPricesLoaded)LoadSilverPrices();
        ImGui.SetNextItemWidth(180);ImGui.InputInt("Minimum selling price (gil)",ref silverMinimum,100,1000);
        ImGui.TextDisabled("Lowest allowed price per dye, including the undercut.");
        ImGui.SetNextItemWidth(180);ImGui.InputInt("Reset price (gil)",ref silverReset,100,1000);
        ImGui.TextDisabled("Used when the market is empty or no competitor can be undercut above the minimum.");
        ImGui.SetNextItemWidth(180);ImGui.InputInt("Undercut by (gil)",ref silverUndercut,1,5);
        ImGui.TextDisabled("Amount below the cheapest qualifying competitor; applies when raising or lowering prices.");
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
        ImGui.TextWrapped("AutoRetainer checks the dedicated strategy during active bell processing, at most once every five minutes. It does not visit other worlds or monitor while offline. In log-only mode, open this retainer's selling page and Compare Prices for the dye to review proposals.");
    }
}
