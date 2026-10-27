import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / 'Steward'

class StorageAuthority(unittest.TestCase):
    def test_normal_entrust_uses_native_selection_and_keeps_protections(self):
        plugin = (ROOT / 'Plugin.cs').read_text(encoding='utf-8')
        entrust = plugin.split('if(action=="Entrust")')[1].split('if(action is "Store"')[0]
        self.assertIn('StorageBlock(stock,target,nativeEntrust:true)', entrust)
        self.assertIn('StorageCandidate(decision)', entrust)
        self.assertIn('config.ExecutionEnabled', entrust)
        core = (ROOT / 'Core/RetainerStorageSettings.cs').read_text(encoding='utf-8').split('public string? Block')[1]
        self.assertNotIn('Categories.TryGetValue', core)
        self.assertNotIn('NonmarketableRetainer', core)
        for hold in ['GearsetsReady', 'Gearsets.Contains', 'ExcludedItems.Contains', 'ExcludedCategories.Contains', 'unlockStatus']:
            self.assertIn(hold, core)

    def test_overflow_queries_native_plans_and_unavailable_is_a_hold(self):
        view = (ROOT / 'Services/InventoryView.cs').read_text(encoding='utf-8')
        self.assertIn('StoragePlanAllows?.Invoke(source,retainer)==true', view)
        self.assertIn('AutoRetainer storage plan unavailable', view)
        plugin = (ROOT / 'Plugin.cs').read_text(encoding='utf-8')
        self.assertIn('AutoRetainer.InventoryPolicy.StoragePlanAllows', plugin)
        self.assertIn('StorageVersion").InvokeFunc()<4', plugin)
        ui = (ROOT / 'Plugin.Storage.cs').read_text(encoding='utf-8')
        self.assertNotIn('config.Storage.Categories', ui)
        self.assertNotIn('config.Storage.NonmarketableRetainer', ui)

    def test_category_permissions_reach_prefetch_review_and_native_selection(self):
        plugin = (ROOT / 'Plugin.cs').read_text(encoding='utf-8')
        self.assertIn('config.Rules.AllowsNonEquipmentDisposal(item)', plugin)
        self.assertIn('NonEquipmentDisposalSelected").RegisterFunc', plugin)
        self.assertIn('NonEquipmentDisposalSelected").UnregisterFunc', plugin)
        ui = (ROOT / 'Plugin.Settings.cs').read_text(encoding='utf-8')
        self.assertIn('config.Rules.NonEquipmentDisposalCategories', ui)
        self.assertIn('nonEquipmentOnly:true', ui)
        evidence = (ROOT / 'Core/CollectionCopyEvidence.cs').read_text(encoding='utf-8')
        self.assertIn('NonEquipmentDisposalPermission:', evidence)
