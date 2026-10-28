import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class CollectionSessionWiring(unittest.TestCase):
 def test_only_complete_idle_native_close_creates_session_proof(self):
  s=(ROOT/'Services/CollectionSessionObserver.cs').read_text()
  for event in ['PreSetup','PreOpen','PreShow','PreClose']:
   self.assertIn('AddonEvent.'+event,s)
  close=s.split('private void Close')[1]
  self.assertLess(close.index('SessionDresser.Invalidate()'),close.index('view.Refresh(cid)'))
  self.assertIn('view.Evidence.DresserKnown,uncertain()',close)
  plugin=(ROOT/'Plugin.cs').read_text()
  self.assertIn('config.PendingOperation!=null||market.Queue.Running||market.Queue.HasUnverifiedAction',plugin)
  self.assertIn('if(!player.IsLoaded){view.SessionDresser.Invalidate()',plugin)
  self.assertIn('view.SessionDresser.Invalidate()',plugin.split('private void HoldExecution')[1].split('private void IntegrationFault')[0])
 def test_native_read_precedes_session_fallback_and_provenance_is_visible(self):
  view=(ROOT/'Services/InventoryView.cs').read_text()
  self.assertIn('else if(SessionDresser.Read(character)',view)
  self.assertIn('SessionDresser.Invalidate();',view.split('if(Character!=character)')[1])
  evidence=(ROOT/'Core/CollectionCopyEvidence.cs').read_text()
  self.assertIn('Dresser verified at close in this login',evidence)
 def test_owned_finish_does_not_clear_other_control(self):
  s=(ROOT/'Plugin.Storage.cs').read_text().split('private void FinishStorageRun()')[1].split('private void BeginStorageRun')[0]
  self.assertLess(s.index('InventoryPolicy.FinishStorage'),s.index('storageRun.End()'))
  self.assertNotIn('SetSuppressed',s)
