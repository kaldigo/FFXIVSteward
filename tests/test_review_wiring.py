import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

class ReviewWiring(unittest.TestCase):
    def test_disposal_permission_and_live_copy_evidence_are_wired(self):
        rules=(ROOT/'Steward/Core/Rules.cs').read_text(encoding='utf-8')
        self.assertIn('!item.Equipment && !settings.NonEquipmentDisposal.Contains(item.Id)',rules)
        self.assertGreater(rules.index('Non-equipment disposal requires explicit item permission'),rules.index('Matches sale rule:'))
        view=(ROOT/'Steward/Services/InventoryView.cs').read_text(encoding='utf-8')
        self.assertIn('Evidence.DresserCopyCounts=copies;Evidence.DresserKnown=true;',view)
        self.assertIn('mm->IsSetSlotUnlocked((uint)i,p))AddCollected(pieces[p])',view)
        self.assertIn('copies[id]=copies.GetValueOrDefault(id)+1',view)
        review=(ROOT/'Steward/Plugin.Review.cs').read_text(encoding='utf-8')
        self.assertIn('CollectionCopyEvidence.Capture(',review)
        self.assertIn('Collection:CopyEvidence(candidate.ItemId,candidate.Quantity)',review)
        self.assertIn('ProtectedNonEquipment',review)
        self.assertGreaterEqual(review.count('Collection:CopyEvidence('),7)
        imports=(ROOT/'Steward/Services/ExternalImports.cs').read_text(encoding='utf-8')
        self.assertIn('view.Evidence.GlamaholicKnown=false;',imports)
        self.assertIn('view.Evidence.GlamaholicReferences=imported;',imports)

    def test_refresh_failure_is_persistent_even_with_automatic_off(self):
        plugin=((ROOT/'Steward/Plugin.cs').read_text(encoding='utf-8')+(ROOT/'Steward/Plugin.Settings.cs').read_text(encoding='utf-8'))
        self.assertIn('Record("Steward refresh failed; review may be incomplete:',plugin)
        self.assertIn('Allow disposal of this non-equipment item',plugin)

    def test_native_queue_paths_use_effective_execution_gate(self):
        for path in (ROOT/'Steward/Services').glob('MarketQueue*.cs'):
            text=path.read_text(encoding='utf-8')
            self.assertNotIn('config.Automatic',text,path.name)
            if 'RestorePrismBox' in text or 'InventoryPolicy.Store' in text:
                self.assertIn('!config.ExecutionEnabled',text,path.name)
        plugin=((ROOT/'Steward/Plugin.cs').read_text(encoding='utf-8')+(ROOT/'Steward/Plugin.Settings.cs').read_text(encoding='utf-8'))
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
        self.assertIn('cached {AllaganRetainerPreview.Location(stock)}',review)
        self.assertNotIn('File.AppendAll',review)
        self.assertIn('reviewJournal.Proposal(ReviewPath,entry,key,config.LogOnly?"LogOnly":"DisposalReview")',review)
        self.assertIn('ProtectedPendingCollection',review)

    def test_live_hold_release_uses_confirmed_ownership_and_rollback(self):
        text=(ROOT/'Steward/Plugin.Storage.cs').read_text(encoding='utf-8')
        self.assertIn('StoragePolicy.CollectionConfirmed(facts,view.Evidence)',text)
        self.assertIn('CollectionHolds.Release(held,confirmed,Save)',text)
        source=(ROOT/'Steward/Services/InventoryView.cs').read_text(encoding='utf-8')
        self.assertIn('CollectionHolds.ForCharacter(config.PendingCollection,character)',source)
