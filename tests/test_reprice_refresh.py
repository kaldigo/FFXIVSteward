import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class RepriceRefreshWiring(unittest.TestCase):
 def test_refresh_precedes_skip_and_native_window(self):
  s=(ROOT/'Services/MarketQueue.cs').read_text(encoding='utf-8')
  self.assertIn('passComparisons.Clear();repriceRefresh=new()',s)
  gate=s.index('repriceRefresh.Wait(')
  self.assertLess(gate,s.index('return Finish(reason:(saleRule==null'))
  self.assertLess(gate,s.index('selling windows not in expected initial state'))
  self.assertIn('reprice&&phase==0&&saleRule!=null&&!config.Silver.Owns(retainer)',s)
  self.assertIn('Reprice resumed after statistics refresh',s)
  self.assertIn('Pricing.NoRepriceReason',s)
 def test_background_refresh_keeps_running_during_queue(self):
  s=(ROOT/'Plugin.cs').read_text(encoding='utf-8')
  heartbeat=(ROOT/'Plugin.Connection.cs').read_text(encoding='utf-8')
  self.assertIn('var pulseDue=attached==character&&(now>=nextHeartbeat||connectionRequested);',heartbeat)
  self.assertIn('if(!pulseDue&&!connectionRequested&&!storageRoutingRequested)return;',heartbeat)
  pulse=heartbeat.split('if(pulseDue)')[1].split('if(connectionRequested)')[0]
  self.assertNotIn('InventoryPolicy.Version',pulse)
  self.assertNotIn('MigrateStorageRouting',pulse)
  self.assertIn('if(connectionRequested)',heartbeat)
  self.assertIn('.Subscribe(OnConnectionRequested)',s)
  self.assertNotIn('Refresh()',heartbeat)
  self.assertIn('prefilledCharacter!=player.ContentId||prefilledWorld!=world||inventoryRefreshRequested',s)
  self.assertIn('Environment.TickCount64>=nextPricePump)PumpPrices()',s)
  review=(ROOT/'Plugin.Review.cs').read_text(encoding='utf-8')
  self.assertIn('reviewCharacter=0;',review.split('private void CaptureRequestedReview()')[1])
  self.assertNotIn('nextReview',review)
  self.assertIn('universalis.Tick(view.World,priceCandidates',s)
