using Dalamud.Bindings.ImGui;
using Steward.Services;

namespace Steward;

public sealed partial class Plugin
{
    private MarketQueue.MarketPreview? marketPreview;
    private ulong previewOwner;
    private uint previewWorld;
    private long nextMarketPreview;
    private void UpdateAutomaticMarketPreview()
    {
        if(Environment.TickCount64<nextMarketPreview)return;
        nextMarketPreview=Environment.TickCount64+500;
        var owner=market.OpenMarketRetainer();
        var changed=owner!=previewOwner||view.World!=previewWorld;
        previewOwner=owner;previewWorld=view.World;
        if(owner==0){marketPreview=null;return;}
        if(changed&&config.LogOnly){reviewJournal.Reset();nextReview=0;}
        marketPreview=market.PreviewMarket();
    }
    private void DrawSalePreview()
    {
        Section("Current retainer preview");
        ImGui.TextWrapped("Opening a retainer's selling list automatically refreshes these proposals. Log-only mode records them automatically; no button click is needed.");
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
        if(ImGui.CollapsingHeader("Existing listing windows"))
            foreach(var window in marketPreview.Windows??[])
                ImGui.TextWrapped($"{view.Facts(window.Source.ItemId)?.Name}: {(window.Since.HasValue?(int)(DateTimeOffset.UtcNow-window.Since.Value).TotalDays:0)} days tracked · {(window.MaximumDays.HasValue?"rotate after "+window.MaximumDays.Value+" days":"rotation disabled")}");
        foreach(var returned in marketPreview.Returns??[])
            ImGui.TextWrapped($"Would return {view.Facts(returned.Source.ItemId)?.Name}: {returned.Reason}. Normal item rules apply after withdrawal.");
        foreach(var rotation in marketPreview.Rotations??[])
            ImGui.TextWrapped($"Would return {view.Facts(rotation.Source.ItemId)?.Name} to inventory after {rotation.DaysListed:N0} tracked days, making room for {view.Facts(rotation.Replacement.ItemId)?.Name} × {rotation.ReplacementQuantity} at an estimated {rotation.ReplacementPrice:N0} gil each. The returned item follows normal item rules.");
        if(plan.Held.Count>0)ImGui.TextWrapped($"{plan.Held.Count} candidates are waiting for suitable prices or complete evidence.");
        ImGui.TextWrapped("Estimates are rechecked against the market, your own listings and item protections before posting.");
    }
}
