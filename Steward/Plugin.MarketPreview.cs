using Dalamud.Bindings.ImGui;
using Steward.Services;

namespace Steward;

public sealed partial class Plugin
{
    private MarketQueue.MarketPreview? marketPreview;
    private void DrawMarketPreviewTab()
    {
        if(!ImGui.BeginTabItem("Market preview"))return;
        ImGui.TextWrapped("Read-only plan for the current retainer. Open its selling list to load actual occupied slots. Universalis and other cached prices are estimates; live execution always checks the game again.");
        ImGui.TextWrapped(universalis.Status);
        ImGui.TextWrapped(universalis.Freshness);
        if(ImGui.Button("Refresh listing preview"))
        {RefreshProtection();marketPreview=market.PreviewMarket();if(config.LogOnly){reviewJournal.Reset();nextReview=0;ReviewTick();}}
        ImGui.TextWrapped(marketPreview?.Status??"Refresh the preview after opening a retainer's selling list.");
        if(marketPreview?.Plan is {} plan)
        {
            if(ImGui.BeginChild("Proposed market work",new(0,0),true))
            {
                foreach(var listing in plan.Listings)
                {
                    ImGui.TextWrapped($"Slot {listing.MarketSlot+1}: {view.Facts(listing.Source.ItemId)?.Name??listing.Source.ItemId.ToString()} {(listing.Source.Hq?"HQ":"NQ")} x{listing.Quantity} — {listing.UnitPrice:N0} gil each; {listing.Total:N0} gil total.");
                    ImGui.TextWrapped($"Source {listing.Source.Container}:{listing.Source.Slot}; price: {listing.Reference.Source}, observed {listing.Reference.Observed.LocalDateTime:g}.");
                    ImGui.TextWrapped("Sale rule: "+listing.RuleName);
                }
                foreach(var listing in marketPreview.Reprices)
                    ImGui.TextWrapped($"Reprice slot {listing.Source.Slot+1}: {view.Facts(listing.Source.ItemId)?.Name} x{listing.Source.Quantity}, {listing.Before:N0} → {listing.After:N0} gil each.");
                foreach(var held in plan.Held)ImGui.TextWrapped("Held: "+held);
            }
            ImGui.EndChild();
        }
        ImGui.EndTabItem();
    }
}
