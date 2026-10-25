# Steward

A collection-aware companion for the maintained AutoRetainer integration fork. Open `/steward`. Defaults to dry run and holds AutoRetainer item actions for connected characters until automatic execution is enabled. Other AutoRetainer functions remain its responsibility.

## Agreed rules

- Both Glamaholic (nested and legacy configurations) and native glamour plates are references.
- Preserve one exact collection copy of referenced/curated items, level-one equipment, blues and special-source equipment. Dyes do not affect protection or duplicates. Equipped and gear-set equipment stays protected.
- An exact item in the dresser or armoire satisfies collection ownership; surplus copies may be disposed of under AutoRetainer's existing settings. Ordinary unreferenced dungeon/crafted equipment can be unwanted even if uncollected. Unknown acquisition sources stay held. Crafted items are recognized from the game Recipe sheet. Dungeon sources are indexed incrementally through Glamour Log’s existing GetItemsFromContent IPC; missing sources stay conservative and can be overridden in Rules.
- Exclusions override all handling, including market actions. Add Metallic Silver's actual item ID to Excluded items before enabling automation. No reserve or speculative market-strategy system is added.
- Ordered sale rules reserve worthwhile surplus before disposal, with minimum unit/stack values and stack size. HQ/NQ comparison is configurable. Known own retainer listings, including Allagan Market listings on the current world, are excluded from competition. If an own listing is already cheapest, do not lower it.
- Missing, future or stale competitor prices hold marketable items rather than valuing them at zero.

## Setup and controls

Build both plugins with `./tools/build.ps1`. Install the fork as a replacement for upstream AutoRetainer, never alongside it; its identity and existing configuration are preserved. Install Steward separately. Repository releases are development previews with immutable archives and checksums. Native testing is still required.

Open the dresser, native plate editor and armoire to establish collection/reference evidence. Collection observations are character-scoped and session-only; storage must be observed again after a plugin restart. Only complete native reads replace collections, including unlocked outfit pieces. Source inventory slots are checked live before permission/submission. Glamaholic, Market Buddy and Allagan Market files are imported read-only and refreshed; failures are visible. The plugin makes no Universalis requests; imported caches may contain Universalis-derived observations.

Preview explains every inventory/armoury decision. Rules allows exclusions, keep overrides, acquisition-source overrides, curated-list imports and ordered sale rules. The JSON editor exposes category and quality filters as well. A rule uses its minimum values against the actual planned stack, and the final undercut price is checked again. Global Automatic starts disabled. Stop cancels queued work and leaves managed actions held. An already submitted native action may complete.

AutoRetainer's retainer postprocess handoff opens inventory, withdraws eligible items through the fork, waits for source and destination evidence, then opens the market list and runs sequential listings/repricing. Current-retainer work can also be requested explicitly. Market Buddy's named lock is released on completion, failure, cancellation or unload. AutoRetainer postprocessing is released only for our own handoff. Retainer identity and source fingerprints are checked throughout. Unknown outcomes stop instead of retrying blindly.

Armoire/Dresser/GC/Desynth/Vendor buttons request AutoRetainer's existing workflows. The companion dresser request leaves the dresser open for the native targeted adapter. While an open dresser is idle, automatic mode processes permitted deposits and cleanup through the same queue. The companion dresser request leaves the dresser open for the native targeted adapter. While an open dresser is idle, automatic mode processes permitted deposits and cleanup through the same queue. Its feature settings and explicit protections still apply. These requests do not rewrite existing disposal lists. Clean open dresser restores eligible loose items to inventory only after evaluating protection with that very dresser copy removed from the ownership evidence. It never disposes of the last required collection copy. Partial-outfit restoration selects only the permitted unlocked piece, preserving protected pieces and checking the destination inventory. Targeted loose-item deposits use the native dresser list and confirm only when both the agent source and item-specific confirmation match. Outfit conversion remains delegated to Glamour Log. These adapters require in-game acceptance.

History distinguishes policy permission from actual execution. AutoRetainer owns its disposal results; Steward records confirmed transfers/listings/restorations and request failures. The session view is bounded; `history.jsonl` remains local to Steward's configuration directory. On unload the character's claim remains, so unavailable policy cannot accidentally resume disposal. Integration > Detach explicitly restores original AutoRetainer rules.

## Curated Eorzea Collection list

The initial bundled list contains the ten main equipment pieces from Eorzea Collection's Skallic Healing and Fending sets, with source URLs and item IDs verified against installed English game data. This is a small curated starting list, not an exhaustive popularity ranking. It can be disabled in Integration. Additions can be imported from a reviewed JSON file with an `items` array. Every entry needs a numeric `itemId` and a `source` URL on `ffxiv.eorzeacollection.com`. See `docs/common-glamour.example.json` and `Steward/data/common-glamour.json`.

## Current verification limits

Local builds and automated rules/import/pricing/cancellation checks pass. No FFXIV in-game tests have been performed. Market callback IDs and packet timing, retainer menu restoration, withdrawals, acknowledgement checks and storage interactions require native acceptance.

Glamour Log’s existing bulk automation is outfit-oriented and has no selected-item IPC. The fork holds batches containing an excluded candidate; loose-item UI conversion is handled by the narrow native adapter, and outfit conversion is left to Glamour Log. Acquisition indexing depends on available source metadata; unknown items are held. Allagan Tools inventories are exposed as cached previews, never execution authority. Automatic travel/storage/disposal schedules remain owned by AutoRetainer, and its configured disposal lists are retained. Unusual outfit conversion dialogs and all native adapters require in-game acceptance; the queue stops on an unsupported dialog or uncertain outcome.
