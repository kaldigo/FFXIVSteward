import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class CleanupWiring(unittest.TestCase):
 def test_explicit_start_and_live_withdrawal_checks(self):
  text=(ROOT/'Plugin.Cleanup.cs').read_text()
  self.assertIn('config.ExecutionEnabled&&config.Features.Disposal&&!executionFault',text)
  self.assertIn('config.Storage.RetainerEnabled(retainer)',text)
  self.assertIn('storageRun.End()',text)
  plugin=(ROOT/'Plugin.cs').read_text()
  self.assertIn('CancelCleanup();',plugin.split('private void StopQueue()')[1])
  self.assertIn('!cleanupActive&&storageRun.Allows',plugin)
  self.assertNotIn('SetAutomatic',plugin)
  queue=(ROOT/'Services/MarketQueue.cs').read_text().split('public void WithdrawForOtherActions')[1].split('public void CleanupDresser')[0]
  self.assertIn('if(!disposalOnly)EnqueueOverflow',queue)
  self.assertIn('disposalOnly&&decision.Outcome!=Outcome.Dispose',queue)
  self.assertLess(queue.index('StockTransition.Removed'),queue.index('CleanupWithdrawalConfirmed'))
  self.assertIn('InventoryView.FreeBagSlots()<2',queue)
  self.assertIn('view.IsUnique(stock.ItemId)&&view.Items.Any(s=>s.ItemId==stock.ItemId)',queue)

