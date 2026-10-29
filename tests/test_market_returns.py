import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class MarketReturns(unittest.TestCase):
 def test_native_submission_is_armed_before_call_and_not_treated_as_acknowledgement(self):
  s=(ROOT/'Services/MarketQueue.Rotation.cs').read_text(encoding='utf-8')
  self.assertNotIn('if(!ui.Callback(',s)
  self.assertLess(s.index('submitted=true;phase=2'),s.index('var result=manager->MoveFromRetainerMarketToRetainerInventory'))
  self.assertIn('CanSkip:()=>!submitted',s)
  self.assertIn('Market return rejected:',s)
  self.assertIn('Market return ended without acknowledgement:',s)
 def test_withdrawal_still_requires_exact_source_and_destination_acknowledgement(self):
  s=(ROOT/'Services/MarketQueue.Rotation.cs').read_text(encoding='utf-8')
  ack=s.index('acknowledged=true;')
  self.assertLess(s.index('StockTransition.Removed(source,'),ack)
  self.assertLess(s.index('MarketReturnReceipt.Arrived(beforeQuantity,RetainerQuantity(source),source.Quantity)'),ack)
  self.assertIn('selected!=retainer',s)
  self.assertIn('source or control lease changed before return',s)
  self.assertLess(s.index('QueueReturn(character'),s.index('submitted=true;phase=2'))

 def test_listing_failures_continue_without_restarting_cancelled_work(self):
  s=(ROOT/'Services/MarketQueue.cs').read_text(encoding='utf-8')
  self.assertIn('CanSkip:()=>!confirmAttempted,OnSkipped:()=>completed?.Invoke(expected,false)',s)
  q=(ROOT/'Core/OperationQueue.cs').read_text(encoding='utf-8')
  skip=q.split('private bool TrySkip')[1].split('public void ReplanPending')[0]
  self.assertLess(skip.index('acknowledged?.Invoke()'),skip.index('done.OnSkipped?.Invoke()'))
  self.assertNotIn('OnSkipped',q.split('public void Stop')[1])

 def test_returns_use_native_retainer_destination_without_menu_or_player_bag_fallback(self):
  s=(ROOT/'Services/MarketQueue.Rotation.cs').read_text(encoding='utf-8')
  self.assertNotIn('ui.Callback(',s)
  self.assertNotIn('MoveFromRetainerMarketToPlayerInventory',s)
  self.assertNotIn('FreeBagSlots',s)
  self.assertIn('MoveFromRetainerMarketToRetainerInventory(InventoryType.RetainerMarket,source.Slot,source.Quantity)',s)
  self.assertIn('InventoryManager.Addresses.MoveFromRetainerMarketToRetainerInventory.Value==0',s)
  self.assertIn('foreach(var type in InventoryView.RetainerTypes())InventoryView.Read(type,contents)',s)
  self.assertIn('InventoryView.RetainerTypes().Any(t=>!InventoryView.Loaded((uint)t))',s)
  self.assertLess(s.index('retainer inventory unavailable or full before return'),s.index('submitted=true;phase=2'))
  self.assertNotIn('Thread.Sleep',s)
