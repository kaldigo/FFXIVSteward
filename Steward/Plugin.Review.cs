using Dalamud.Bindings.ImGui;
using Steward.Core;
using Steward.Services;

namespace Steward;

public sealed partial class Plugin
{
    private readonly ReviewJournal reviewJournal=new();
    private long nextReview;
    private string ReviewPath=>Path.Combine(pi.GetPluginConfigDirectory(),"review.jsonl");
    private CollectionCopyEvidence? CopyEvidence(uint id,int removals=0)=>view.Facts(id) is {} item?
        CollectionCopyEvidence.Capture(item,config.Rules,view.Evidence,view.NativeReferencesKnown,
            view.NativeReferences.Contains(id),DateTimeOffset.UtcNow,removals):null;
    private void DrawReviewSettings()
    {
        var enabled=config.LogOnly;
        if(ImGui.Checkbox("Log only — do not change any items",ref enabled))
        {
            // Set the restrictive flag before cancellation/save; changing mode never enables execution.
            config.LogOnly=true;Stop();
            config.LogOnly=enabled;config.Automatic=false;Save();reviewJournal.Reset();nextReview=0;
            Record(enabled?"Log-only review enabled: all managed item actions blocked":"Log-only review disabled; automatic execution remains off");
        }
        ImGui.TextWrapped("Logs collection destinations, overflow, proposed listings and prices, disposal, and enabled dresser cleanup. No managed item actions occur in log-only mode. Open a retainer selling list to load its market capacity; cached prices are labelled estimates.");
        ImGui.TextWrapped("Review log: "+ReviewPath);
        if(ImGui.Button("Write review snapshot now"))
        {if(!config.LogOnly)Record("Enable Log only before recording a review session.");else{reviewJournal.Reset();nextReview=0;ReviewTick();}}
    }
    private void LogPolicyProposal(string? action,Stock stock,Decision decision)
    {
        if(!config.LogOnly||decision.Outcome!=Outcome.Dispose||action is not ("Vendor" or "Desynth" or "Gc"))return;
        var key=$"review/{player.ContentId}/{stock.Container}/{stock.Slot}/{stock.ItemId}/{stock.Quantity}/{action}/{decision.Reason}";
        if(recentDecisions.TryGetValue(key,out var at)&&Environment.TickCount64-at<60000)return;
        var entry=new ReviewEntry(player.ContentId,$"Live policy request: container {stock.Container}, slot {stock.Slot}",stock.ItemId,stock.Quantity,stock.Hq,"WouldPermit "+action,decision.Reason,Collection:CopyEvidence(stock.ItemId),RuleName:decision.Rule?.Name);
        try
        {
            File.AppendAllText(ReviewPath,System.Text.Json.JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,mode="LogOnly",proposal=entry})+Environment.NewLine);
            recentDecisions[key]=Environment.TickCount64;
            Record($"[LOG ONLY] Would permit {action}: item {stock.ItemId} x{stock.Quantity}, container {stock.Container}, slot {stock.Slot}. {decision.Reason}");
        }
        catch(Exception ex){Alert("Review logging failed: "+ex.Message+". The proposed action remains blocked.");}
    }
    private void ReviewTick()
    {
        if(!config.LogOnly||!player.IsLoaded||Environment.TickCount64<nextReview)return;
        nextReview=Environment.TickCount64+5000;
        try
        {
            RefreshProtection();
            List<ReviewEntry> entries=[];var unavailable=0;HashSet<string> holdReasons=[];
            void Inspect(string location,Stock stock)
            {
                var decision=Judge(stock);
                var sale=SaleStatistics.Select(view.Evidence,stock.ItemId,stock.Hq,config.Rules.Quality,view.World,DateTimeOffset.UtcNow);
                if(decision.Outcome==Outcome.Wait){unavailable++;holdReasons.Add(decision.Reason);}
                if(decision.Outcome is Outcome.CollectDresser or Outcome.CollectArmoire or Outcome.StoreRetainer or Outcome.Market or Outcome.Wait or Outcome.Keep or Outcome.Ignore)
                    entries.Add(new(player.ContentId,location,stock.ItemId,stock.Quantity,stock.Hq,
                        decision.Outcome switch{Outcome.CollectDresser=>"WouldCollectDresser",Outcome.CollectArmoire=>"WouldCollectArmoire",Outcome.StoreRetainer=>"ProtectedRetainerOverflow",Outcome.Market=>"SaleEligible",_=>decision.Outcome.ToString()},
                        $"{view.Facts(stock.ItemId)?.Name}: {decision.Reason}. Dresser free: {view.Evidence.DresserFreeSlots?.ToString()??"unknown"}; reserved: {config.Rules.DresserEmptySlots}. Collection proposals select one missing copy per item, subject to live space and readiness.",
                        Collection:CopyEvidence(stock.ItemId),RuleName:decision.Rule?.Name,SaleEvidence:sale));
                if(decision.Outcome==Outcome.Dispose)
                    entries.Add(new(player.ContentId,location,stock.ItemId,stock.Quantity,stock.Hq,"WouldDispose",$"{view.Facts(stock.ItemId)?.Name}: {decision.Reason}. Steward eligibility only; AutoRetainer still applies its enabled methods and item-selection rules.",Collection:CopyEvidence(stock.ItemId),RuleName:decision.Rule?.Name,SaleEvidence:sale));
                if(decision is {Outcome:Outcome.Keep,Reason:"Non-equipment disposal requires explicit item permission"})
                    entries.Add(new(player.ContentId,location,stock.ItemId,stock.Quantity,stock.Hq,"ProtectedNonEquipment",$"{view.Facts(stock.ItemId)?.Name}: {decision.Reason}",Collection:CopyEvidence(stock.ItemId),RuleName:decision.Rule?.Name));
                if(decision is {Outcome:Outcome.Ignore,Reason:"Item category excluded from all Steward handling"})
                    entries.Add(new(player.ContentId,location,stock.ItemId,stock.Quantity,stock.Hq,"ExcludedCategory",$"{view.Facts(stock.ItemId)?.Name}: {decision.Reason}",Collection:CopyEvidence(stock.ItemId)));
            }
            foreach(var stock in view.Items.Distinct())Inspect($"Player container {stock.Container}, slot {stock.Slot}",stock);
            var retainerContainers=InventoryView.RetainerTypes().Select(t=>(uint)t).ToHashSet();
            foreach(var (owner,stock) in tracked.Items.Distinct().Where(r=>view.Retainers.Contains(r.Owner)&&retainerContainers.Contains(r.Stock.Container)))
                Inspect($"Retainer {owner}, cached container {stock.Container}, slot {stock.Slot} (preview only)",stock);
            if(config.CleanDresser)
                foreach(var candidate in market.DresserCleanupCandidates())
                    entries.Add(new(player.ContentId,"Glamour dresser",candidate.ItemId,(uint)candidate.Quantity,false,
                        candidate.Action=="Restore for armoire migration"?"WouldMoveToArmoire":"WouldRestoreDresser",candidate.Name+": "+candidate.Reason,Collection:CopyEvidence(candidate.ItemId,candidate.Quantity)));
            foreach(var id in CollectionHolds.ForCharacter(config.PendingCollection,player.ContentId))
                entries.Add(new(player.ContentId,"Pending collection hold",id,1,false,"ProtectedPendingCollection",
                    $"{view.Facts(id)?.Name}: protected while waiting for confirmed collection; retainer storage never satisfies ownership.",Collection:CopyEvidence(id)));
            marketPreview=market.PreviewMarket();
            if(marketPreview.Plan is {} plan)
            {
                foreach(var listing in plan.Listings)
                    entries.Add(new(player.ContentId,$"Retainer {marketPreview.Retainer}, proposed market slot {listing.MarketSlot+1}; source {listing.Source.Container}:{listing.Source.Slot}",
                        listing.Source.ItemId,listing.Quantity,listing.Source.Hq,"WouldList",
                        $"{view.Facts(listing.Source.ItemId)?.Name}: {listing.Quantity} at {listing.UnitPrice:N0} gil each; total {listing.Total:N0} gil. Estimate; live comparison and protection rechecked before execution.",
                        listing.UnitPrice,listing.Total,marketPreview.World,marketPreview.Retainer,listing.Reference.Source,listing.Reference.Observed,Collection:CopyEvidence(listing.Source.ItemId),RuleName:listing.RuleName));
                foreach(var repricing in marketPreview.Reprices)
                    entries.Add(new(player.ContentId,$"Retainer {marketPreview.Retainer}, listing slot {repricing.Source.Slot}",repricing.Source.ItemId,repricing.Source.Quantity,repricing.Source.Hq,
                        "WouldReprice",$"{view.Facts(repricing.Source.ItemId)?.Name}: {repricing.Before:N0} to {repricing.After:N0} gil each; estimate rechecked before execution.",
                        repricing.After,(ulong)repricing.After*repricing.Source.Quantity,marketPreview.World,marketPreview.Retainer,repricing.Reference.Source,repricing.Reference.Observed,Collection:CopyEvidence(repricing.Source.ItemId),RuleName:repricing.RuleName));
                for(var i=0;i<plan.Held.Count;i++)
                    entries.Add(new(player.ContentId,$"Market planning hold {i+1}",0,0,false,"MarketHeld",plan.Held[i],World:marketPreview.World,Retainer:marketPreview.Retainer));
            }
            entries.Add(new(player.ContentId,"Market preview summary",0,0,false,"MarketSnapshot",marketPreview.Status,World:marketPreview.World,Retainer:marketPreview.Retainer));
            entries.Add(new(player.ContentId,"Market price source",0,0,false,"PriceSourceStatus",universalis.Status+" "+universalis.Freshness,World:view.World));
            if(config.UseUniversalis)
                foreach(var held in universalis.HeldItems(view.World).Where(h=>view.Facts(h.ItemId) is {} facts&&!config.Rules.IsIgnored(facts)))
                    entries.Add(new(player.ContentId,"Universalis price hold",held.ItemId,0,false,"PriceHeld",$"{view.Facts(held.ItemId)?.Name}: {held.Reason}",World:view.World));
            var disposals=entries.Count(e=>e.Action=="WouldDispose");var removals=entries.Where(e=>e.Location=="Glamour dresser").Sum(e=>e.Quantity);
            entries.Add(new(player.ContentId,"Review summary",0,0,false,"Snapshot",
                $"{disposals} disposal candidates (cached retainer observations labelled); {removals} dresser removals. Protected non-equipment stacks: {entries.Count(e=>e.Action=="ProtectedNonEquipment")}. Enabled sale rules: {config.Rules.Sales.Count(r=>r.Enabled)}; quality: {config.Rules.Quality}. Held/unverified stacks: {unavailable} ({string.Join("; ",holdReasons.Order())}). Cleanup enabled: {config.CleanDresser}. References ready: {view.Evidence.ReferencesReady}; native plates known: {view.NativeReferencesKnown}; Glamaholic known: {view.Evidence.GlamaholicKnown}; gearsets ready: {view.Evidence.GearsetsReady}; dresser known: {view.Evidence.DresserKnown}; armoire known: {view.Evidence.ArmoireKnown}. {tracked.Status} {inventoryMemory.Status}"));
            foreach(var entry in reviewJournal.Snapshot(ReviewPath,entries))
                Record($"[LOG ONLY] {entry.Action}: {entry.Location}; item {entry.ItemId} x{entry.Quantity}. {entry.Reason}");
        }
        catch(Exception ex){Alert("Review logging could not complete: "+ex.Message+". Log-only mode still blocks all managed item actions.");}
    }
}
