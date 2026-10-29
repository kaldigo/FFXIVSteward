import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class MarketReturns(unittest.TestCase):
 def test_menu_dispatch_is_not_treated_as_native_acknowledgement(self):
  s=(ROOT/'Services/MarketQueue.Rotation.cs').read_text(encoding='utf-8')
  self.assertNotIn('if(!ui.Callback(',s)
  self.assertLess(s.index('submitted=true;phase=2'),s.index('var returned=ui.Callback("ContextMenu"'))
  self.assertIn('CanSkip:()=>!submitted',s)
  self.assertIn('Market return rejected:',s)
  self.assertIn('Market return ended without acknowledgement:',s)
 def test_withdrawal_still_requires_exact_source_and_destination_acknowledgement(self):
  s=(ROOT/'Services/MarketQueue.Rotation.cs').read_text(encoding='utf-8')
  ack=s.index('acknowledged=true;')
  self.assertLess(s.index('StockTransition.Removed(source,'),ack)
  self.assertLess(s.index('BagQuantity(source)!=beforeQuantity+source.Quantity'),ack)
  self.assertIn('ConfirmationOwnership.Matches(sale->Id,menu->ParentId,menu->HostId)',s)
  self.assertLess(s.index('QueueReturn(character'),s.index('submitted=true;phase=2'))

 def test_listing_failures_continue_without_restarting_cancelled_work(self):
  s=(ROOT/'Services/MarketQueue.cs').read_text(encoding='utf-8')
  self.assertIn('CanSkip:()=>!confirmAttempted,OnSkipped:()=>completed?.Invoke(expected,false)',s)
  q=(ROOT/'Core/OperationQueue.cs').read_text(encoding='utf-8')
  skip=q.split('private bool TrySkip')[1].split('public void ReplanPending')[0]
  self.assertLess(skip.index('acknowledged?.Invoke()'),skip.index('done.OnSkipped?.Invoke()'))
  self.assertNotIn('OnSkipped',q.split('public void Stop')[1])
