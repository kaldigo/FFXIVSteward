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
  self.assertIn('var finalDecision=judge(expected);',queue)
  self.assertIn('if(phase<3 &&',queue)
  self.assertIn('view.Items.ToArray().Where',queue)
 def test_no_silent_fallback_when_expected_market_preferences_fail(self):
  source=(ROOT/'Steward/Services/ExternalImports.cs').read_text()
  self.assertIn('MarketPreferencesReady=!config.ImportMarketBuddy',source)
  self.assertNotIn('local pricing settings apply',source)
if __name__=='__main__':unittest.main()
