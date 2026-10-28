import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'

class CacheRepricing(unittest.TestCase):
 def test_cache_replaced_and_kept_separate_from_game_quotes(self):
  text=(ROOT/'Services/ExternalImports.cs').read_text()
  self.assertIn('view.Evidence.AllaganPrices=[];',text)
  self.assertIn('view.Evidence.AllaganPrices=ReadCached(path,Imports.MarketCache)',text)
 def test_comparison_is_shared_only_after_native_completion(self):
  text=(ROOT/'Services/MarketQueue.cs').read_text()
  self.assertIn('Queue.Begin();passComparisons.Clear();',text)
  capture=text.split('if(offers.TryGetValue(expected.ItemId,out result)&&ComparisonReady')[1]
  self.assertIn('passComparisons[expected.ItemId]=new(expectedWorld',capture)
  self.assertEqual(text.count('!TryComparison(out var result)'),2)
  self.assertIn('shared.World==expectedWorld',text)
  self.assertIn('view.World==expectedWorld&&shared.At<=DateTimeOffset.UtcNow',text)
  self.assertIn('TimeSpan.FromMinutes(2)',text)
  reuse=text.split('if(passComparisons.TryGetValue')[1].split('offers.Clear()')[0]
  self.assertIn('phase=2;return StepResult.Waiting;',reuse)
  self.assertNotIn('ui.Click',reuse)
  self.assertIn('MarketSubmission.CanCommit',text)
  self.assertIn('!config.ExecutionEnabled',text)
 def test_log_only_uses_cache_without_native_actions(self):
  review=(ROOT/'Plugin.Review.cs').read_text()
  self.assertIn('entries.AddRange(market.CacheReviewEntries)',review)
  silver=(ROOT/'Services/MarketQueue.Silver.cs').read_text().split('private MarketPreview PreviewSilver')[1]
  self.assertIn('CachedRepricing.Decide',silver)
  self.assertIn('higher-priced alternatives require one shared comparison',silver)
  self.assertNotIn('ui.Click',silver)
