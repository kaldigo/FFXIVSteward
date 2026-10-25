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
if __name__=='__main__':unittest.main()
