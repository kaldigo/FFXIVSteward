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
  capture=text.split('if(rejection==null)')[1]
  self.assertIn('MarketComparison.NativeRejection',text)
  self.assertIn('beforeRequest=proxy->CurrentRequestId;',text)
  self.assertIn('var row=proxy->Listings[i];',text)
  self.assertIn('if(count<=100&&!proxy->WaitingForListings)',text)
  self.assertIn('if(ui.Get("ItemSearchResult")==null)',text)
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
 def test_native_button_payload_is_not_null(self):
  text=(ROOT/'Services/NativeUi.cs').read_text()
  self.assertIn('AtkEventData data=default;',text)
  self.assertIn('addon->ReceiveEvent(evt->State.EventType,(int)evt->Param,evt,&data);',text)
 def test_log_only_uses_cache_without_native_actions(self):
  review=(ROOT/'Plugin.Review.cs').read_text()
  self.assertIn('entries.AddRange(market.CacheReviewEntries)',review)
  silver=(ROOT/'Services/MarketQueue.Silver.cs').read_text().split('private MarketPreview PreviewSilver')[1]
  self.assertIn('CachedRepricing.Decide',silver)
  self.assertIn('higher-priced alternatives require one shared comparison',silver)
  self.assertNotIn('ui.Click',silver)
