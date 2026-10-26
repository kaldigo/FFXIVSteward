# Read-only game metadata export

This helper reads installed English game sheets through the locally installed Dalamud Lumina references. It does not load a plugin, connect to the game, read player configuration or perform game actions.

From the repository root, with .NET 10 and the Dalamud development references available:

```powershell
dotnet run --project tools/curation-game-data/Export.csproj -- '<game>/sqpack' '<output>/metadata.json'
```

The export records exact Item IDs and names, equipment slots, original equipment-job flags, rarity, equipment/item levels, market eligibility, PvP flags, Cabinet eligibility and exact Recipe output IDs. Market eligibility is `!IsUntradable && ItemSearchCategory != 0`; it is independent of current listings or prices. The game's PvP flag does not enumerate every PvP reward. A missing Recipe/Cabinet/PvP match is not evidence that acquisition is ordinary.

Resolve EC names to exactly one exported English name. Never substitute an EC catalogue ID for an Item ID or choose a fuzzy match. Fashion-accessory names do not become equipment simply because they appear in an EC outfit. The reviewed metadata subset and export hash are preserved in `docs/data/common-glamour-evidence.json`; the full local export is not needed by the plugin or CI.

The offline list builder is `python tools/build_common_glamour.py --check`. See `docs/common-glamour.md` for the sampling, frequency and source-review rules.
