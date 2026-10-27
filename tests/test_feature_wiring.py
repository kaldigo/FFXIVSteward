import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'Steward'
class FeatureWiring(unittest.TestCase):
    def test_manual_dresser_trace_is_passive_and_guarded(self):
        trace=(ROOT/'Services/DresserTrace.cs').read_text()
        for forbidden in ['FireCallback(', 'ReceiveEvent(', 'ui.Click(', 'ui.Close(', '->Open(', 'StoreNewOutfit(', 'StoreExistingOutfit(']:
            self.assertNotIn(forbidden,trace)
        self.assertIn('120000',trace)
        self.assertIn('count++>=200',trace)
        self.assertIn('Math.Min(sizeof(AtkEventData),32)',trace)
        self.assertIn('UnregisterListener',trace)
        plugin=(ROOT/'Plugin.cs').read_text()
        gate=plugin.split('private bool CanTraceDresser()')[1].split('private void Save()')[0]
        self.assertIn('config.ExecutionEnabled||market.Queue.Running||ownsPostprocess',gate)
        self.assertIn('AutoRetainer.PluginState.IsBusy',gate)
        self.assertIn('if(dresserTrace.Active) dresserTrace.Tick(CanTraceDresser())',plugin)
        self.assertIn('dresserTrace.Dispose();StopQueue()',plugin)

    def test_withdrawal_capacity_counts_survive_live_refresh(self):
        queue=(ROOT/'Services/MarketQueue.cs').read_text()
        self.assertEqual(queue.count('StoragePolicy.CountWaitingForDresser(view.Items,previewJudge)'),2)
        self.assertNotIn('view.Items.Where(s=>judge(s)',queue)
    def test_planning_does_not_replace_submission_checks(self):
        queue=(ROOT/'Services/MarketQueue.cs').read_text()
        self.assertIn('StoragePolicy.PlanWithdrawals(stored,previewJudge,config.Features.Enabled,DisposalSelected,collectionOnly)',queue)
        self.assertIn('var decision=judge(stock);if(!config.Features.Enabled(decision.Outcome))',queue)
        self.assertIn('if(InventoryView.ReadSlot(stock.Container,stock.Slot)!=stock)',queue)
        self.assertIn('if(!selected){history(',queue)
        plugin=(ROOT/'Plugin.cs').read_text().split('private string Evaluate')[1].split('private void Update')[0]
        self.assertIn('view.Refresh(player.ContentId);',plugin)
        self.assertIn('imports.RefreshReferences();',plugin)
        self.assertNotIn('tracked.Refresh',plugin)
        self.assertNotIn('imports.Refresh();',plugin)
    def test_dresser_selection_does_not_close_windows(self):
        ui=(ROOT/'Services/NativeUi.cs').read_text()
        self.assertIn('list->DispatchItemEvent(selected,AtkEventType.ListItemClick)',ui)
        self.assertIn('entry->UIntValues[1],entry->UIntValues[3],entry->UIntValues[4]',ui)
        self.assertIn('DresserSource.Find(rows,agentIndex,source)',ui)
        self.assertNotIn('StringValues',ui)
        self.assertNotIn('CallbackCore("MiragePrismPrismBoxCrystallize"',ui)
        queue=(ROOT/'Services/MarketQueue.cs').read_text()
        self.assertIn('ui.SelectDresserItem(source,selected,out var treeIndex)',queue)
        self.assertIn('ui.OwnsDresserConfirmation(confirmationOwner,source)',queue)
        self.assertIn('ui.ClickDresserConfirmation(confirmationOwner,source,true)',queue)
        self.assertIn('ui.CancelDresserConfirmation(confirmationOwner,source)',queue)
        deposit=queue.split('Queue.Add(new("Store dresser item "+source.ItemId')[1]
        self.assertLess(deposit.index('if(phase==2)'),deposit.index('ui.Get("MiragePrismPrismBoxCrystallize")'))
        self.assertIn('DresserConfirmation.Acknowledge(source,live',deposit)
        self.assertIn('view.Evidence.Dresser.Contains(source.ItemId)',deposit)
        self.assertIn('phase==1?StepResult.Waiting:RejectDresser',deposit)
        self.assertIn('TimeoutMs:12000',deposit)

    def test_collection_completion_resumes_storage_before_retry_timer(self):
        plugin=(ROOT/'Plugin.cs').read_text()
        transition=plugin.split('if(collectionPass&&!ownsPostprocess')[1].split('if(!ownsPostprocess&&!market.Queue.Running&&Environment.TickCount64>=nextAutomaticWork')[0]
        self.assertIn('!market.Queue.Running',transition)
        self.assertIn('AutoRetainer.PluginState.IsBusy',transition)
        self.assertIn('nextDresserAttempt=0;nextAutomaticWork=0',transition)
        storage=(ROOT/'Plugin.Storage.cs').read_text()
        self.assertNotIn('carriedCollection',storage)
        self.assertNotIn('carriedActions',storage)
        self.assertIn('if(Environment.TickCount64>=nextCollectionAttempt&&CollectionNeeded(player.ContentId))',storage)
        flow=storage.split('private bool AutomaticStorageWork()')[1]
        self.assertLess(flow.index('CollectionNeeded(player.ContentId)'),flow.index('NeedsCollectionVisit(false)'))
        self.assertIn('nextCollectionAttempt=Math.Max(nextCollectionAttempt,Environment.TickCount64+5000)',transition)

    def test_dresser_category_waits_for_native_acknowledgement(self):
        queue=(ROOT/'Services/MarketQueue.cs').read_text().split('private void DepositOpenDresserCore()')[1]
        self.assertIn('DresserSource.Category(source.Container)',queue)
        self.assertIn('if(currentCategory!=requestedCategory)return StepResult.Waiting',queue)
        self.assertLess(queue.index('ui.AdvanceDresserCategory()'),queue.index('ui.SelectDresserItem(source'))
        ui=(ROOT/'Services/NativeUi.cs').read_text()
        self.assertIn('CategoryDropDown->GetSelectedItemIndex()==category',ui)
        self.assertIn('!agent->Data->IsPopulatingList&&agent->Data->IsPopulatingComplete',ui)
        self.assertNotIn('CrystallizeCategory =',ui)
        self.assertIn('candidate.Slot!=source.Slot',ui)

    def test_permissions_and_review_are_gated_without_changing_rules(self):
        plugin=(ROOT/'Plugin.cs').read_text()
        self.assertIn('config.Features.Allows(action,decision.Outcome)&&ActionPolicy.Allows(',plugin)
        review=(ROOT/'Plugin.Review.cs').read_text()
        self.assertIn('if(!config.Features.Enabled(decision.Outcome)&&!(decision.Outcome==Outcome.Dispose&&config.LogDisposal))',review)
        self.assertIn('"FeatureDisabled"',review)
        self.assertNotIn('Features',(ROOT/'Core/Rules.cs').read_text())
    def test_collection_and_sales_are_independent(self):
        queue=(ROOT/'Services/MarketQueue.cs').read_text()
        withdrawal=queue.split('private void WithdrawForOtherActionsCore')[1].split('public void CleanupDresser')[0] if 'private void WithdrawForOtherActionsCore' in queue else queue.split('public void WithdrawForOtherActions')[1].split('public void CleanupDresser')[0]
        self.assertNotIn('SalesEnabledFor',withdrawal)
        self.assertIn('config.Features.Enabled',withdrawal)
        self.assertIn('else if(config.Features.Repricing)Enqueue(',queue)
        self.assertIn('if(config.Features.NewListings)EnqueueListingPump',queue)
        self.assertIn('config.Features.CanRestore',(ROOT/'Services/MarketQueue.Storage.cs').read_text())
    def test_native_rotation_and_return_recheck_toggles(self):
        rotation=(ROOT/'Services/MarketQueue.Rotation.cs').read_text()
        self.assertIn('!config.Features.NewListings||!config.Features.Rotation',rotation)
        self.assertIn('ruleReturn?!config.Features.ListingReturns:!config.Features.Rotation||!config.Features.NewListings',rotation)
        self.assertIn('!config.Features.ListingReturns',(ROOT/'Services/MarketQueue.Returns.cs').read_text())
