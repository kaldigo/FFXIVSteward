import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]/'Steward'

class AutoDutyWiring(unittest.TestCase):
 def test_travel_permission_expires_and_cannot_be_rearmed(self):
  plugin=(ROOT/'Plugin.cs').read_text()
  storage=(ROOT/'Plugin.Storage.cs').read_text()
  self.assertIn('if(AutoDutyCoordination.OwnsTravel(pi))storageRun.End();',plugin)
  begin=storage.split('private void BeginStorageRun')[1].split('private bool CollectionNeeded')[0]
  self.assertLess(begin.index('AutoDutyCoordination.OwnsTravel(pi)'),begin.index('storageRun.Begin'))
  work=storage.split('private bool AutomaticStorageWork')[1].split('private bool StorageWorkRemaining')[0]
  self.assertLess(work.index('AutoDutyCoordination.OwnsTravel(pi)'),work.index('AutoRetainer.InventoryPolicy.Run'))
 def test_collection_withdrawals_are_deferred_before_submission(self):
  text=(ROOT/'Services/MarketQueue.cs').read_text().split('public void WithdrawForOtherActions')[1].split('public void CleanupDresser')[0]
  self.assertIn('AutoDutyCoordination.OwnsTravel(pi)&&previewJudge(stock).Outcome is Outcome.CollectArmoire or Outcome.CollectDresser',text)
  self.assertIn('AutoDutyCoordination.OwnsTravel(pi)&&decision.Outcome is Outcome.CollectArmoire or Outcome.CollectDresser',text)
  self.assertIn('if(decision.Outcome==Outcome.Dispose)',text)
 def test_absent_ipc_is_not_called_and_explicit_travel_is_guarded(self):
  service=(ROOT/'Services/AutoDutyCoordination.cs').read_text()
  self.assertIn('ipc.HasFunction?ipc.InvokeFunc():null',service)
  run=(ROOT/'Plugin.cs').read_text().split('private void Run(string operation)')[1]
  self.assertLess(run.index('AutoDutyCoordination.OwnsTravel(pi)'),run.index('AutoRetainer.InventoryPolicy.Run'))
  cleanup=(ROOT/'Plugin.Cleanup.cs').read_text().split('private void StartCleanup()')[1]
  self.assertLess(cleanup.index('AutoDutyCoordination.OwnsTravel(pi)'),cleanup.index('AutoRetainer.InventoryPolicy.StartCleanup'))
