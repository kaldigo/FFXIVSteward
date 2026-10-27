import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class FeatureWiring(unittest.TestCase):
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
