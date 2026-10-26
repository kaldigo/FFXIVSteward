import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

class CategoryWiring(unittest.TestCase):
    def test_categories_use_the_same_sheet_as_autoretainer(self):
        view=(ROOT/'Steward/Services/InventoryView.cs').read_text(encoding='utf-8')
        self.assertIn('GetExcelSheet<Sheets.ItemUICategory>()',view)
        ui=(ROOT/'Steward/Plugin.RuleCategories.cs').read_text(encoding='utf-8')
        self.assertIn('view.Categories.OrderBy',ui)
        self.assertIn('Included categories',ui)
        self.assertIn('Excluded categories for this rule',ui)
        self.assertIn('rule.Group=ItemGroup.Any',ui)

    def test_global_exclusions_gate_prices_and_are_logged(self):
        plugin=((ROOT/'Steward/Plugin.cs').read_text(encoding='utf-8')+(ROOT/'Steward/Plugin.Settings.cs').read_text(encoding='utf-8'))
        self.assertIn('!config.Rules.IsIgnored(item)',plugin)
        self.assertIn('config.Rules.IgnoredCategories',plugin)
        review=(ROOT/'Steward/Plugin.Review.cs').read_text(encoding='utf-8')
        self.assertIn('"ExcludedCategory"',review)
        self.assertIn('RuleName:listing.RuleName',review)

    def test_native_client_uses_partial_parser_and_per_item_diagnostics(self):
        source=(ROOT/'Steward/Services/UniversalisPrices.cs').read_text(encoding='utf-8')
        self.assertIn('UniversalisData.ParseBatch(',source)
        self.assertIn('values.Items,values.Held',source)
        self.assertIn('holds.Remove((result.World,id))',source)
        review=(ROOT/'Steward/Plugin.Review.cs').read_text(encoding='utf-8')
        self.assertIn('universalis.HeldItems(view.World)',review)
        self.assertIn('"PriceHeld"',review)
