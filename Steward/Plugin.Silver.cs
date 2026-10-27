using Dalamud.Bindings.ImGui;
using Steward.Core;
namespace Steward;
public sealed partial class Plugin
{
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
        Setting("Enable silver-dye strategy",config.Silver.Enabled,v=>config.Silver.Enabled=v,"Adds a narrowly scoped market exception for this item and retainer. It does not enable automatic execution.");
        ImGui.EndDisabled();
        ImGui.TextWrapped("Target: ten stacks of 5, five stacks of 2, five singles (65 dyes). Refill missing quotas from your bags or this retainer. Other items are untouched. Differently sized dye listings are repriced but not resized or withdrawn; shortages are logged.");
        ImGui.TextWrapped($"Minimum: {config.Silver.MinimumPrice:N0} gil per dye. Reset: {config.Silver.ResetPrice:N0}. Undercut: {config.Silver.Undercut} gil. Prices may rise or fall. Your own retainers never count as competition.");
        ImGui.TextWrapped("Competitors too cheap to undercut above the floor trigger a manual-buyout alert. If no qualifying competitor remains, reset to 45,000. Missing or incomplete prices always hold. No purchases are automated.");
        ImGui.TextWrapped("AutoRetainer checks the dedicated strategy during active bell processing, at most once every five minutes. It does not visit other worlds or monitor while offline. In log-only mode, open this retainer's selling page and Compare Prices for the dye to review proposals.");
    }
}
