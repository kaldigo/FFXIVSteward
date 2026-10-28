import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

class ListingWiring(unittest.TestCase):
    def test_sale_value_dispatch_requires_readback_not_callback_return(self):
        ui=(ROOT/'Steward/Services/NativeUi.cs').read_text(encoding='utf-8')
        dispatch=ui.split('public bool SetRetainerSaleValue')[1].split('public int DresserCategory')[0]
        self.assertIn('callbackResult=addon->FireCallback(2,data,true);',dispatch)
        self.assertIn('return true;',dispatch)
        self.assertNotIn('return callbackResult',dispatch)
        live=(ROOT/'Steward/Services/MarketQueue.cs').read_text(encoding='utf-8')
        self.assertIn('ui.SetRetainerSaleValue(2',live)
        self.assertIn('ui.SetRetainerSaleValue(3',live)
        self.assertIn('agent->SellItemUnitPrice!=target',live)
        self.assertIn('dialog->AskingPrice->Value!=target',live)
        self.assertIn('price/quantity readback did not match dispatched values',live)
        self.assertLess(live.index('MarketSubmission.CanCommit'),live.index('confirmAttempted=true;'))
        self.assertLess(live.index('confirmAttempted=true;'),live.index('ui.Click("RetainerSell",21)'))
        self.assertIn('Market action rejected:',live)

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
        self.assertIn('Choice:Next(p.Input,pricing,world,maxAgeMinutes,now)',planner)
        self.assertIn('if(!success)pass.Rejected(source)',preview)
        self.assertIn('pass.Next(candidates,listings.Count)',preview)

    def test_next_split_only_scheduled_after_acknowledgement_and_cleanup(self):
        source=(ROOT/'Steward/Services/MarketQueue.cs').read_text(encoding='utf-8')
        scope=source.split('private void Enqueue(Stock expected')[1].split('public void Advance')[0]
        self.assertLess(scope.index('StockTransition.Removed(expected,live'),scope.rindex('return Finish(true)'))
        self.assertLess(scope.index('Marketbuddy.Unlock'),scope.index('completed?.Invoke(expected,successful)'))
        self.assertIn('if(finished)completed?.Invoke',scope)
        self.assertIn('view.World!=expectedWorld',scope)
        self.assertIn('occupiedBefore.SetEquals(currentListings.Select',scope)
        self.assertIn('InventoryView.CanListStock(expected)',scope)

    def test_log_only_records_prices_and_never_starts_a_listing(self):
        source=(ROOT/'Steward/Plugin.Review.cs').read_text(encoding='utf-8')
        self.assertIn('market.PreviewMarket()',source)
        preview=(ROOT/'Steward/Services/MarketQueue.Listing.cs').read_text(encoding='utf-8')
        self.assertIn('if(!view.Evidence.ReferencesReady||!view.Evidence.GearsetsReady)',preview)
        self.assertIn('Protection references unavailable; listing proposals held.',preview)
        self.assertIn('"WouldList"',source)
        self.assertIn('listing.UnitPrice,listing.Total,marketPreview.World',source)
        self.assertIn('listing.Reference.Source,listing.Reference.Observed',source)
        self.assertNotIn('market.Start(',source)
        ui=(ROOT/'Steward/Plugin.Settings.cs').read_text(encoding='utf-8')
        self.assertIn('ImGui.BeginDisabled(!config.ExecutionEnabled||executionFault);',ui)
        self.assertIn('if(ImGui.Button("List / reprice current retainer"))market.Start();',ui)
        self.assertIn('if(config.LogOnly)',ui)

    def test_passive_price_observation_uses_current_native_request(self):
        source=(ROOT/'Steward/Services/MarketQueue.Listing.cs').read_text(encoding='utf-8')
        self.assertIn('proxy->CurrentRequestId!=request',source)
        self.assertIn('at<observationWorldSince',source)
        self.assertIn('ComparisonReady(id,snapshot.At,result)',source)
        self.assertIn('snapshot.World==view.World',source)

    def test_automatic_preview_and_rotation_submission_gates(self):
        plugin=(ROOT/'Steward/Plugin.cs').read_text(encoding='utf-8')
        self.assertIn('UpdateAutomaticMarketPreview();',plugin)
        preview=(ROOT/'Steward/Plugin.MarketPreview.cs').read_text(encoding='utf-8')
        self.assertIn('if(changed&&config.LogOnly){reviewJournal.Reset();nextReview=0;}',preview)
        self.assertNotIn('ImGui.Button("Refresh proposed listings")',preview)
        rotation=(ROOT/'Steward/Services/MarketQueue.Rotation.cs').read_text(encoding='utf-8')
        self.assertLess(rotation.index('QueueReturn('),rotation.index('ui.Callback("ContextMenu",0,choice'))
        self.assertIn('!config.ExecutionEnabled||config.ExecutionReviewRequired',rotation)
        self.assertIn('BagQuantity(source)!=beforeQuantity+source.Quantity',rotation)
        self.assertIn('ConfirmationOwnership.Matches',rotation)
        self.assertIn('if(acknowledged&&!ruleReturn)Enqueue(proposal.Replacement',rotation)
        self.assertIn('else if(replan)EnqueueListingPump',rotation)
        queue=(ROOT/'Steward/Services/MarketQueue.cs').read_text(encoding='utf-8')
        scope=queue.split('if(quoteOnly)return Finish(true);')[0]
        self.assertIn('if(!choice.Eligible)',scope)
        self.assertNotIn('ui.Click("RetainerSell",21)',scope.split('private void Enqueue(Stock expected')[1])

    def test_retainer_scope_gates_preview_and_every_native_market_path(self):
        root=ROOT/'Steward'
        for name in ['MarketQueue.cs','MarketQueue.Listing.cs','MarketQueue.Rotation.cs','MarketQueue.Returns.cs']:
            self.assertIn('config.SalesEnabledFor(retainer)',(root/'Services'/name).read_text())
        queue=(root/'Services/MarketQueue.cs').read_text()
        self.assertIn('decision=marketJudge(expected,retainer,true)',queue)
        self.assertIn('else if(config.Features.Repricing)',queue)
        self.assertIn('if(cached.NeedsComparison)Enqueue(listing,retainer,true,coordinated)',queue)
        self.assertIn('EnqueueReturn(listing,retainer,coordinated)',queue)
        review=(root/'Plugin.Review.cs').read_text()
        self.assertIn('"WouldReturnListing"',review)
        self.assertNotIn('"ProtectedForRelisting"',review)
        rotation=(root/'Services/MarketQueue.Rotation.cs').read_text()
        self.assertNotIn('ReserveReturn(',rotation)
        self.assertIn('if(ReturnReason(source,retainer,true)==null)',rotation)
        self.assertIn('if(ruleReturn&&ReturnReason(source,retainer,true)==null)',rotation)
