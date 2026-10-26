import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

class ListingWiring(unittest.TestCase):
    def test_dynamic_pass_replaces_upfront_slot_allocation(self):
        source=(ROOT/'Steward/Services/MarketQueue.cs').read_text(encoding='utf-8')
        start=source.split('private void StartCore')[1].split('private void Enqueue(')[0]
        self.assertIn('EnqueueListingPump(retainer,coordinated,new ListingPass())',start)
        self.assertNotIn('.Take(free)',start)
        self.assertLess(start.index('foreach(var listing in listings)'),start.index('EnqueueListingPump'))

    def test_native_and_read_only_use_shared_quantity_and_price_planner(self):
        live=(ROOT/'Steward/Services/MarketQueue.cs').read_text(encoding='utf-8')
        preview=(ROOT/'Steward/Services/MarketQueue.Listing.cs').read_text(encoding='utf-8')
        planner=(ROOT/'Steward/Core/ListingPlanner.cs').read_text(encoding='utf-8')
        self.assertGreaterEqual(live.count('ListingPlanner.Next('),2)
        self.assertIn('ListingPlanner.Plan(',preview)
        self.assertIn('var choice=Next(input,pricing,world,maxAgeMinutes,now)',planner)
        self.assertIn('if(!success)pass.Rejected(source)',preview)
        self.assertIn('pass.Next(candidates,listings.Count)',preview)

    def test_next_split_only_scheduled_after_acknowledgement_and_cleanup(self):
        source=(ROOT/'Steward/Services/MarketQueue.cs').read_text(encoding='utf-8')
        scope=source.split('private void Enqueue(Stock expected')[1].split('public void Advance')[0]
        self.assertLess(scope.index('StockTransition.Removed(expected,live'),scope.index('return Finish(true)'))
        self.assertLess(scope.index('Marketbuddy.Unlock'),scope.index('completed?.Invoke(expected,successful)'))
        self.assertIn('if(finished)completed?.Invoke',scope)
        self.assertIn('view.World!=expectedWorld',scope)
        self.assertIn('occupiedBefore.SetEquals(currentListings.Select',scope)
        self.assertIn('InventoryView.CanListStock(expected)',scope)

    def test_log_only_records_prices_and_never_starts_a_listing(self):
        source=(ROOT/'Steward/Plugin.Review.cs').read_text(encoding='utf-8')
        self.assertIn('market.PreviewMarket()',source)
        self.assertIn('"WouldList"',source)
        self.assertIn('listing.UnitPrice,listing.Total,marketPreview.World',source)
        self.assertIn('listing.Reference.Source,listing.Reference.Observed',source)
        self.assertNotIn('market.Start(',source)
        ui=(ROOT/'Steward/Plugin.cs').read_text(encoding='utf-8')
        self.assertIn('if(config.LogOnly){reviewJournal.Reset();nextReview=0;ReviewTick();}else market.Start();',ui)

    def test_passive_price_observation_uses_current_native_request(self):
        source=(ROOT/'Steward/Services/MarketQueue.Listing.cs').read_text(encoding='utf-8')
        self.assertIn('proxy->CurrentRequestId!=request',source)
        self.assertIn('at<observationWorldSince',source)
        self.assertIn('ComparisonReady(id,snapshot.At,result)',source)
        self.assertIn('snapshot.World==view.World',source)
