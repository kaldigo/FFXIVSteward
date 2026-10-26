using Dalamud.Bindings.ImGui;
using Steward.Services;

namespace Steward;

public sealed partial class Plugin
{
    private MarketQueue.MarketPreview? marketPreview;
    private void DrawSalePreview()
    {
        Section("Current retainer preview");
        ImGui.TextWrapped("Open a retainer's selling list to preview free slots, stack splits and prices. This preview does not change any items.");
        if(ImGui.Button("Refresh proposed listings")){RefreshProtection();marketPreview=market.PreviewMarket();}
        if(marketPreview==null)return;
        ImGui.TextWrapped(marketPreview.Status);
        if(marketPreview.Plan is not {} plan)return;
        if(ImGui.BeginTable("ProposedSales",4,ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn("Item");ImGui.TableSetupColumn("Stack");ImGui.TableSetupColumn("Gil each");ImGui.TableSetupColumn("Total gil");ImGui.TableHeadersRow();
            foreach(var listing in plan.Listings)
            {
                ImGui.TableNextRow();ImGui.TableNextColumn();ImGui.TextUnformatted((view.Facts(listing.Source.ItemId)?.Name??"Unknown item")+(listing.Source.Hq?" (HQ)":""));
                ImGui.TableNextColumn();ImGui.TextUnformatted(listing.Quantity.ToString());ImGui.TableNextColumn();ImGui.TextUnformatted(listing.UnitPrice.ToString("N0"));
                ImGui.TableNextColumn();ImGui.TextUnformatted(listing.Total.ToString("N0"));
            }
            ImGui.EndTable();
        }
        foreach(var price in marketPreview.Reprices)ImGui.TextWrapped($"{view.Facts(price.Source.ItemId)?.Name??"Unknown item"}: {price.Before:N0} → {price.After:N0} gil each");
        if(plan.Held.Count>0)ImGui.TextWrapped($"{plan.Held.Count} candidates are waiting for suitable prices or complete evidence.");
        ImGui.TextWrapped("Estimates are rechecked against the market, your own listings and item protections before posting.");
    }
}
