import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class StorageTravel(unittest.TestCase):
 def test_cleanup_enablement_cannot_force_travel_or_keep_a_run_open(self):
  s=(ROOT/'Plugin.Storage.cs').read_text(encoding='utf-8')
  automatic=s.split('private bool AutomaticStorageWork()')[1].split('private void StartOverflow()')[0]
  self.assertNotIn('config.CleanDresser',automatic)
  self.assertIn('NeedsCollectionVisit(false)',automatic)
  self.assertIn('NeedsCollectionVisit(true)',automatic)
 def test_explicit_and_collection_visits_still_run_cleanup(self):
  s=(ROOT/'Plugin.cs').read_text(encoding='utf-8')
  self.assertIn('market.CleanupDresser()',s)
  self.assertIn('operation=="Dresser"&&!config.Features.DresserCollection&&!config.CleanDresser',s)

 def test_manual_bell_actions_wait_for_release_and_stay_scoped(self):
  bell=(ROOT/'Plugin.ManualBell.cs').read_text(encoding='utf-8')
  update=bell.split('private void UpdateManualBellAction()')[1]
  self.assertLess(update.index('ManualBell("Released")'),update.index('StartCleanup()'))
  self.assertIn('player.ContentId != bellCharacter || !BellActionAllowed(action)',update)
  self.assertIn('BellAction.DresserCleanup && !pi.GetIpcSubscriber',update)
  plugin=(ROOT/'Plugin.cs').read_text(encoding='utf-8')
  self.assertIn('!storageRun.StorageOnly)market.CleanupDresser()',plugin)
  self.assertIn('!storageRun.CleanupOnly)market.DepositOpenDresser()',plugin)
  queue=(ROOT/'Services/MarketQueue.cs').read_text(encoding='utf-8')
  self.assertIn('if(collectionOnly&&decision.Outcome==Outcome.Dispose)return StepResult.Completed',queue)
