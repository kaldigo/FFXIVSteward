import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

class ReviewWiring(unittest.TestCase):
    def test_native_queue_paths_use_effective_execution_gate(self):
        for path in (ROOT/'Steward/Services').glob('MarketQueue*.cs'):
            text=path.read_text(encoding='utf-8')
            self.assertNotIn('config.Automatic',text,path.name)
            if 'RestorePrismBox' in text or 'InventoryPolicy.Store' in text:
                self.assertIn('!config.ExecutionEnabled',text,path.name)
        plugin=(ROOT/'Steward/Plugin.cs').read_text(encoding='utf-8')
        self.assertIn('ActionPolicy.Allows(action,decision.Outcome,config.ExecutionEnabled',plugin)
        self.assertIn('if(!config.ExecutionEnabled || !player.IsLoaded',plugin)
        self.assertIn('if(!config.ExecutionEnabled){Record("Dry run:',plugin)

    def test_preview_and_execution_share_last_copy_simulation(self):
        native=(ROOT/'Steward/Services/MarketQueue.cs').read_text(encoding='utf-8')
        preview=(ROOT/'Steward/Services/MarketQueue.Storage.cs').read_text(encoding='utf-8')
        self.assertIn('StoragePolicy.WithoutDresserCopy',native)
        self.assertIn('StoragePolicy.WithoutDresserCopy',preview)
        self.assertLess(native.index('protectCollection(id);'),native.index('live->RestorePrismBoxSetItem'))

    def test_mode_change_cancels_before_relaxation(self):
        review=(ROOT/'Steward/Plugin.Review.cs').read_text(encoding='utf-8')
        self.assertLess(review.index('config.LogOnly=true;Stop();'),review.index('config.LogOnly=enabled;config.Automatic=false;Save();'))
        self.assertNotIn('InvokeFunc(player.ContentId,"',review)
        self.assertIn('cached container',review)
        self.assertIn('ProtectedPendingCollection',review)

    def test_live_hold_release_uses_confirmed_ownership_and_rollback(self):
        text=(ROOT/'Steward/Plugin.Storage.cs').read_text(encoding='utf-8')
        self.assertIn('StoragePolicy.CollectionConfirmed(facts,view.Evidence)',text)
        self.assertIn('CollectionHolds.Release(held,confirmed,Save)',text)
        source=(ROOT/'Steward/Services/InventoryView.cs').read_text(encoding='utf-8')
        self.assertIn('CollectionHolds.ForCharacter(config.PendingCollection,character)',source)
