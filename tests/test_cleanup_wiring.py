import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class CleanupWiring(unittest.TestCase):
 def test_dresser_unique_skip_uses_live_equipped_and_bag_ownership(self):
  view=(ROOT/'Services/InventoryView.cs').read_text().split('public static bool? CarriesItem')[1].split('public static bool CanListStock')[0]
  self.assertIn('Append(InventoryType.EquippedItems)',view)
  self.assertIn('if(types.Any(t=>!Loaded((uint)t)))return null;',view)
  self.assertIn('foreach(var type in types)Read(type,carried)',view)
  queue=(ROOT/'Services/MarketQueue.cs').read_text().split('private void CleanupDresserCore')[1].split('public void DepositOpenDresser')[0]
  self.assertLess(queue.index('InventoryView.CarriesItem(id)==true'),queue.index('planned++'))
  live=queue.split('var carried=InventoryView.CarriesItem(id);')[1]
  self.assertIn('if(carried==null)return StepResult.Waiting;',live)
  self.assertLess(live.index('unique copy is already'),live.index('attempted=true'))
  self.assertIn('return StepResult.Completed;',live.split('attempted=true')[0])
 def test_automatic_storage_closes_dresser_only_after_work_finishes(self):
  plugin=(ROOT/'Plugin.cs').read_text()
  self.assertIn('!market.Queue.Running&&!StorageWorkRemaining()&&market.CloseDresserForStorage()',plugin)
  storage=(ROOT/'Services/MarketQueue.Storage.cs').read_text().split('public sealed record CleanupCandidate')[0]
  self.assertIn('ui.Get("MiragePrismPrismBox")!=null||',storage)
  self.assertIn('Queue.Running||',storage)
  self.assertIn('ui.Visible("SelectYesno")',storage)
  self.assertIn('ui.Visible("MiragePrismPrismSetConvertC")',storage)
  self.assertIn('return !DresserOpen;',storage)
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

