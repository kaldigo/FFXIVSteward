import json,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
class Distribution(unittest.TestCase):
 def test_default_dry_run(self):
  text=(ROOT/'Steward/Configuration.cs').read_text()
  self.assertIn('public bool Automatic { get; set; }',text)
  self.assertNotIn('Automatic { get; set; } = true',text)
 def test_no_universalis_client(self):
  for source in (ROOT/'Steward').rglob('*.cs'):
   if 'obj' in source.parts:continue
   self.assertNotIn('HttpClient',source.read_text())
 def test_history_does_not_claim_permission_is_execution(self):
  self.assertIn('AutoRetainer owns execution/results',(ROOT/'Steward/Plugin.cs').read_text())
 def test_source_package_is_separate(self):
  cfg=json.loads((ROOT/'release-config.json').read_text())
  self.assertEqual(cfg['required_dlls'],['Steward.dll'])
 def test_stopped_run_survives_plugin_reload(self):
  config=(ROOT/'Steward/Configuration.cs').read_text();plugin=(ROOT/'Steward/Plugin.cs').read_text()
  self.assertIn('public bool ExecutionReviewRequired',config)
  self.assertIn('executionFault=config.ExecutionReviewRequired;',plugin)
  self.assertIn('executionFault=config.ExecutionReviewRequired=true;',plugin)
  self.assertIn('config.Automatic=false;',plugin)
 def test_execution_uses_live_protection_and_owned_handoff(self):
  plugin=(ROOT/'Steward/Plugin.cs').read_text();queue=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
  self.assertIn('new(gui),JudgeLive,Record,RefreshProtection',plugin)
  self.assertIn('AutoRetainer.InventoryPolicy.OwnsHandoff',plugin)
  self.assertIn('MarketSubmission.CanCommit(',queue)
  self.assertIn('if(phase<4 &&',queue)
  self.assertIn('view.Items.ToArray().Where',queue)
 def test_no_silent_fallback_when_expected_market_preferences_fail(self):
  source=(ROOT/'Steward/Services/ExternalImports.cs').read_text()
  self.assertIn('MarketPreferencesReady=!config.ImportMarketBuddy',source)
  self.assertNotIn('local pricing settings apply',source)
 def test_dresser_compatibility_does_not_mean_designed_for_glamour(self):
  source=(ROOT/'Steward/Services/InventoryView.cs').read_text()
  self.assertNotIn('row.IsGlamorous',source)
  self.assertIn('row.LevelItem.RowId==1',source)
 def test_owned_control_and_immediate_planning_faults(self):
  source=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
  self.assertNotIn('AutoRetainer.SetSuppressed',source)
  self.assertIn('AutoRetainer.InventoryPolicy.AcquireControl',source)
  self.assertIn('AutoRetainer.InventoryPolicy.OwnsControl',source)
  self.assertIn('AutoRetainer.InventoryPolicy.ReleaseControl',source)
  self.assertIn('Queue.Advance(now);CheckFault();',source)
 def test_protocol_upgrade_returns_to_dry_run_before_registering_execution(self):
  source=(ROOT/'Steward/Plugin.cs').read_text()
  migration=source.split('if(config.SafetyRevision<3)')[1].split('executionFault=config.ExecutionReviewRequired;')[0]
  self.assertIn('config.Automatic=false;',migration)
  self.assertLess(source.index('if(config.SafetyRevision<3)'),source.index('framework.Update+=Update'))
 def test_packets_and_dialog_values_are_bound_to_live_state(self):
  source=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
  self.assertIn('packet.RequestId',source)
  self.assertIn('proxy->WaitingForListings',source)
  self.assertIn('dialog->AskingPrice->Value!=target',source)
  self.assertIn('agent->SellItemQuantity!=quantity',source)
  self.assertIn('ConfirmationOwnership.Matches(',source)
 def test_backend_uncertainty_is_persisted_and_cleared_in_both_plugins(self):
  source=(ROOT/'Steward/Plugin.cs').read_text()
  self.assertIn('RegisterAction(IntegrationFault)',source)
  self.assertIn('AutoRetainer.InventoryPolicy.HasFault',source)
  self.assertIn('AutoRetainer.InventoryPolicy.ClearFault',source)
  self.assertIn('AutoRetainer.InventoryPolicy.Hold',source)
 def test_pending_owned_dresser_prompt_is_cancelled(self):
  source=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
  self.assertIn('ui.CancelOwnedConfirmation(addon->Id)',source)
  source=(ROOT/'Steward/Services/NativeUi.cs').read_text()
  self.assertIn('ClickButton(&addon->AtkUnitBase,addon->NoButton)',source)
if __name__=='__main__':unittest.main()
