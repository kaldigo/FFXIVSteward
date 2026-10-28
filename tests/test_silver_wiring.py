import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class SilverWiring(unittest.TestCase):
 def test_strategy_rechecks_price_and_quota_before_commit(self):
  source=(ROOT/'Services/MarketQueue.cs').read_text()
  self.assertEqual(source.count('SilverChoice(expected,retainer,reprice,result.Offers,true)'),2)
  self.assertIn('choice.Quantity!=quantity',source)
  self.assertIn('MarketSubmission.CanCommit',source)
 def test_no_ordinary_return_or_rotation(self):
  for file in ['MarketQueue.Returns.cs','MarketQueue.Rotation.cs']:
   self.assertIn('if(config.Silver.Owns(retainer))return null;', (ROOT/'Services'/file).read_text())
 def test_strategy_scope_preserves_protections(self):
  source=(ROOT/'Plugin.cs').read_text()
  block=source.split('if(config.Silver.Owns(retainer))')[1].split('var decision=item==null')[0]
  for check in ['config.Silver.Matches(retainer,stock.ItemId)','config.Rules.Keep.Contains','config.Rules.QuantityReserves','config.Rules.IgnoredCategories','view.Evidence.Gearsets.Contains','UpstreamProtection.Apply']:
   self.assertIn(check,block)
  self.assertIn('config.ExecutionEnabled&&!executionFault',source)
 def test_cache_preview_is_provisional_and_live_comparison_is_identified(self):
  source=(ROOT/'Services/MarketQueue.Silver.cs').read_text()
  self.assertIn('observed.Complete',source)
  self.assertIn('CachedRepricing.Decide',source)
  self.assertIn('Allagan Market silver estimate',source)
  self.assertIn('config.Silver.Price(observed.Offers,config.OwnRetainers,true)',source)
  self.assertIn('observed.World==view.World',source)
  self.assertNotIn('Universalis',source)
