import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class FeatureWiring(unittest.TestCase):
    def test_withdrawal_capacity_counts_survive_live_refresh(self):
        queue=(ROOT/'Services/MarketQueue.cs').read_text()
        self.assertEqual(queue.count('StoragePolicy.CountWaitingForDresser(view.Items,previewJudge)'),2)
        self.assertNotIn('view.Items.Where(s=>judge(s)',queue)
    def test_planning_does_not_replace_submission_checks(self):
        queue=(ROOT/'Services/MarketQueue.cs').read_text()
        self.assertIn('StoragePolicy.PlanWithdrawals(stored,previewJudge,config.Features.Enabled,DisposalSelected,collectionOnly)',queue)
        self.assertIn('var decision=judge(stock);if(!config.Features.Enabled(decision.Outcome))',queue)
        self.assertIn('if(InventoryView.ReadSlot(stock.Container,stock.Slot)!=stock)',queue)
        self.assertIn('if(!selected){history(',queue)
        plugin=(ROOT/'Plugin.cs').read_text().split('private string Evaluate')[1].split('private void Update')[0]
        self.assertIn('view.Refresh(player.ContentId);',plugin)
        self.assertIn('imports.RefreshReferences();',plugin)
        self.assertNotIn('tracked.Refresh',plugin)
        self.assertNotIn('imports.Refresh();',plugin)
    def test_dresser_selection_does_not_close_windows(self):
        ui=(ROOT/'Services/NativeUi.cs').read_text()
        self.assertIn('SelectDresserItem(int row)=>CallbackCore("MiragePrismPrismBoxCrystallize",false,[0,row])',ui)
        queue=(ROOT/'Services/MarketQueue.cs').read_text()
        self.assertIn('ui.SelectDresserItem(selected)',queue)
        self.assertIn('ConfirmationOwnership.Matches(confirmationOwner,confirm->ParentId,confirm->HostId)',queue)
        deposit=queue.split('Queue.Add(new("Store dresser item "+source.ItemId')[1]
        self.assertLess(deposit.index('if(phase==2)'),deposit.index('ui.Get("MiragePrismPrismBoxCrystallize")'))
        self.assertIn('StockTransition.Removed(source,live,1,true)',deposit)
        self.assertIn('view.Evidence.Dresser.Contains(source.ItemId)',deposit)
        self.assertIn('phase==1?StepResult.Waiting:RejectDresser',deposit)
        self.assertIn('TimeoutMs:12000',deposit)

    def test_permissions_and_review_are_gated_without_changing_rules(self):
        plugin=(ROOT/'Plugin.cs').read_text()
        self.assertIn('config.Features.Allows(action,decision.Outcome)&&ActionPolicy.Allows(',plugin)
        review=(ROOT/'Plugin.Review.cs').read_text()
        self.assertIn('if(!config.Features.Enabled(decision.Outcome)&&!(decision.Outcome==Outcome.Dispose&&config.LogDisposal))',review)
        self.assertIn('"FeatureDisabled"',review)
        self.assertNotIn('Features',(ROOT/'Core/Rules.cs').read_text())
    def test_collection_and_sales_are_independent(self):
        queue=(ROOT/'Services/MarketQueue.cs').read_text()
        withdrawal=queue.split('private void WithdrawForOtherActionsCore')[1].split('public void CleanupDresser')[0] if 'private void WithdrawForOtherActionsCore' in queue else queue.split('public void WithdrawForOtherActions')[1].split('public void CleanupDresser')[0]
        self.assertNotIn('SalesEnabledFor',withdrawal)
        self.assertIn('config.Features.Enabled',withdrawal)
        self.assertIn('else if(config.Features.Repricing)Enqueue(',queue)
        self.assertIn('if(config.Features.NewListings)EnqueueListingPump',queue)
        self.assertIn('config.Features.CanRestore',(ROOT/'Services/MarketQueue.Storage.cs').read_text())
    def test_native_rotation_and_return_recheck_toggles(self):
        rotation=(ROOT/'Services/MarketQueue.Rotation.cs').read_text()
        self.assertIn('!config.Features.NewListings||!config.Features.Rotation',rotation)
        self.assertIn('ruleReturn?!config.Features.ListingReturns:!config.Features.Rotation||!config.Features.NewListings',rotation)
        self.assertIn('!config.Features.ListingReturns',(ROOT/'Services/MarketQueue.Returns.cs').read_text())
