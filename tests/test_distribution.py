import json,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
class Distribution(unittest.TestCase):
 def test_default_dry_run(self):
  text=(ROOT/'Steward/Configuration.cs').read_text()
  self.assertIn('public bool Automatic { get; set; }',text)
  self.assertNotIn('Automatic { get; set; } = true',text)
  plugin=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text())
  startup=plugin.split('config=AtomicState.ReadOrMigrate')[1].split('if(config.SafetyRevision')[0]
  self.assertIn('config.Automatic=false;',startup)
 def test_universalis_is_read_only_and_native_comparisons_still_gate_submission(self):
  source=(ROOT/'Steward/Services/UniversalisPrices.cs').read_text(encoding='utf-8')
  self.assertIn('HttpMethod.Get',source)
  self.assertIn('https://universalis.app/api/v2/',source)
  self.assertNotIn('HttpMethod.Post',source)
  self.assertNotIn('InventoryPolicy.',source)
  self.assertIn('ComparisonReady(', (ROOT/'Steward/Services/MarketQueue.cs').read_text(encoding='utf-8'))
 def test_universalis_preview_prices_do_not_replace_live_own_prices(self):
  source=(ROOT/'Steward/Services/MarketQueue.Listing.cs').read_text(encoding='utf-8')
  self.assertNotIn('PublicOwnPrices',source.split('private MarketReference? PreviewReference')[0])
  self.assertIn('PublicOwnPrices',source.split('private MarketReference? PreviewReference')[1])
  plugin=((ROOT/'Steward/Plugin.cs').read_text(encoding='utf-8')+(ROOT/'Steward/Plugin.Settings.cs').read_text(encoding='utf-8'))
  self.assertIn('universalis.Tick(',plugin)
  self.assertIn('universalis.Apply(',plugin)
  self.assertIn('universalis.Dispose();',plugin)
  self.assertIn('view.Retainers.Count==0?noPriceOwners:config.OwnRetainers',plugin)
 def test_history_does_not_claim_permission_is_execution(self):
  self.assertIn('AutoRetainer owns execution/results',((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text()))
 def test_source_package_is_separate(self):
  cfg=json.loads((ROOT/'release-config.json').read_text())
  self.assertEqual(cfg['required_dlls'],['Steward.dll'])
 def test_stopped_run_survives_plugin_reload(self):
  config=(ROOT/'Steward/Configuration.cs').read_text();plugin=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text())
  self.assertIn('public bool ExecutionReviewRequired',config)
  self.assertIn('executionFault=config.ExecutionReviewRequired;',plugin)
  self.assertIn('executionFault=config.ExecutionReviewRequired=true;',plugin)
  self.assertIn('config.Automatic=false;',plugin)
 def test_execution_uses_live_protection_and_owned_handoff(self):
  plugin=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text());queue=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
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
  source=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text())
  migration=source.split('if(config.SafetyRevision<6)')[1].split('executionFault=config.ExecutionReviewRequired;')[0]
  self.assertIn('config.Automatic=false;',migration)
  self.assertLess(source.index('if(config.SafetyRevision<6)'),source.index('framework.Update+=Update'))
 def test_packets_and_dialog_values_are_bound_to_live_state(self):
  source=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
  self.assertIn('packet.RequestId',source)
  self.assertIn('proxy->WaitingForListings',source)
  self.assertIn('dialog->AskingPrice->Value!=target',source)
  self.assertIn('agent->SellItemQuantity!=quantity',source)
  self.assertIn('ConfirmationOwnership.Matches(',source)
 def test_backend_uncertainty_is_persisted_and_cleared_in_both_plugins(self):
  source=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text())
  self.assertIn('RegisterAction(IntegrationFault)',source)
  self.assertIn('AutoRetainer.InventoryPolicy.HasFault',source)
  self.assertIn('AutoRetainer.InventoryPolicy.ClearFault',source)
  self.assertIn('AutoRetainer.InventoryPolicy.Hold',source)
 def test_pending_owned_dresser_prompt_is_cancelled(self):
  source=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
  self.assertIn('ui.CancelOwnedConfirmation(addon->Id)',source)
  source=(ROOT/'Steward/Services/NativeUi.cs').read_text()
  self.assertIn('ClickButton(&addon->AtkUnitBase,addon->NoButton)',source)
 def test_failed_save_cannot_enable_or_clear_review_in_memory(self):
  source=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text())
  self.assertIn('ExecutionMode.Enable(',source)
  self.assertIn('catch{config.PendingOperation=pending;config.PendingCharacter=pendingCharacter;executionFault=config.ExecutionReviewRequired=true;throw;}',source)
 def test_visible_unready_confirmation_is_not_absence(self):
  source=(ROOT/'Steward/Services/NativeUi.cs').read_text()
  self.assertIn('if(!addon->IsReady)return false;',source)
 def test_own_execution_and_preview_use_upstream_protection(self):
  source=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text())
  judge=source.split('private Decision Judge(Stock stock)')[1].split('private void RefreshProtection()')[0]
  self.assertIn('AutoRetainer.PluginState.IsItemProtected',judge)
  self.assertIn('catch{protectedItem=null;}',judge)
  self.assertIn('UpstreamProtection.Apply(decision,protectedItem)',judge)
  refresh=source.split('private void RefreshProtection()')[1].split('private Decision JudgeLive')[0]
  self.assertIn('protectionSnapshot.Clear();',refresh)
 def test_all_queue_paths_use_durable_start_and_acknowledgement(self):
  plugin=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text());queue=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
  self.assertIn('HoldExecution,BeginOperation,CompleteOperation,ProtectCollection,Alert,Judge)',plugin)
  self.assertIn('Queue=new(begin,complete)',queue)
  self.assertLess(plugin.index('if(!string.IsNullOrEmpty(config.PendingOperation))'),plugin.index('framework.Update+=Update'))
  self.assertIn('ExecutionMode.Persist(()=>AtomicState.Write(statePath,config)',plugin)
 def test_retainer_and_native_acknowledgements_are_exact(self):
  source=(ROOT/'Steward/Services/MarketQueue.cs').read_text()
  self.assertIn('currentRetainer->LastSelectedRetainerId!=retainer',source)
  self.assertIn('StockTransition.Removed(expected,live',source)
  self.assertIn('!view.Evidence.DresserKnown',source)
  self.assertIn('ui.Visible("RetainerSell")',source)
  self.assertIn('after!=before+1',source)
  source=((ROOT/'Steward/Plugin.cs').read_text()+(ROOT/'Steward/Plugin.Settings.cs').read_text())
  self.assertIn('config.PendingCharacter!=player.ContentId',source)
if __name__=='__main__':unittest.main()
