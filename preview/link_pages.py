"""Link production ImGui page bodies into the offline renderer without loading the live plugin."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
parts = ["using Dalamud.Bindings.ImGui;", "using Steward.Core;", "namespace Steward;", "public sealed partial class Plugin {"]
sources = {
    "Settings": ["DrawWorkspace", "DrawReadiness", "DrawGlamourPage", "DrawCollectionFeatureControls", "DrawMarketFeatureControls", "DrawRetainerSelection", "DrawSalesPage", "DrawSaleRuleEditor", "DrawStoragePage", "DrawDisposalPermissions", "DrawItemPicker", "DrawNamedItems", "ToggleMembership", "DrawOverridesPage", "DrawIntegrationsPage", "DrawDeveloperPage"],
    "Storage": ["DrawStorageSettings"],
    "Review": ["DrawReviewSettings"]
}
for file, methods in sources.items():
    source = (root / "Steward" / f"Plugin.{file}.cs").read_text(encoding="utf-8")
    for name in methods:
        start = source.index("    private void " + name + "(")
        end = re.search(r"^    (?:private|public|internal) |^}", source[start+1:], re.M)
        parts.append(source[start:start+1+end.start()])
parts.append("}")
out = root / "preview/obj/Pages.g.cs"
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text("\n".join(parts), encoding="utf-8")
