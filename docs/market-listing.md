# Automatic listing and read-only review

Local development implementation, 26 October 2026. Nothing is published by building this project. Protocol 6 AutoRetainer coordination remains required. Log-only defaults on; automatic execution starts off.

## Listing pass

Normal AutoRetainer retainer postprocessing and the existing current-retainer command use the same market queue. Existing eligible listings are repriced first. A dynamic filling step then checks the live selected retainer, loaded market inventory and current number of listings. It selects a rule-matching source from player bags or loaded current-retainer storage. Every source is judged again before submission; protected items, collection overflow, exclusions, unavailable protection and instance-bound items are not listing candidates.

The sale rule and imported Market Buddy cap limit each listing's quantity. Minimum unit value applies to the actual undercut/matching-own price; minimum total applies to that actual price multiplied by the actual split quantity. The native sale quantity control performs the partial transfer; Steward does not manufacture extra stacks or assume the remainder exists before the game acknowledges it.

Only after the expected source reduction and a new matching market slot are confirmed does the next filling step run. It re-reads current inventory and occupancy. A 250-item stack with a cap of 99 can therefore produce 99, 99 and 52 in one visit, if three slots and the price thresholds permit. A small remainder below the minimum total is skipped; another source can fill that slot. Unpriced/skipped stacks do not consume market capacity. All skipped candidates terminate the pass without an infinite retry; later visits may reconsider them.

New listings match the lowest identified own price when it is already cheapest and meets the floor. They do not repeatedly undercut the price established by the preceding split. Repricing retains its existing no-self-undercutting policy. Separate/combined HQ/NQ rules affect both competing and own-price anchors.

## Native safeguards

Each listing retains the owned AutoRetainer handoff or standalone control, Market Buddy lock where applicable, durable operation journal and per-action protection refresh. Source identity/quantity/HQ, current world/retainer, exact native comparison request/item/count/freshness, native agent/dialog fields, current market occupancy, actual price/quantity and thresholds are checked before the commit. Unknown or incomplete price comparisons are held and other candidates may be tried before any commit. An uncertain submitted action stops the queue for review and is never retried as another split.

## Log-only mode

Open **Market preview** and the desired retainer's selling list. The preview reads actual occupied/free slots and live source stacks without opening menus or requesting prices. Fresh Allagan Market cache observations or complete manually obtained game comparisons provide price references. Missing prices or unavailable capacity are shown as held/unknown. Cache-based estimates explicitly note incomplete own-price coverage outside the current retainer. A preview never authorizes execution: native execution compares the live market again.

The shared planner allocates actual slot holes and shows each proposed stack's item, quality, quantity, unit price, total, source and quote origin/time. Log-only writes `WouldList` and `WouldReprice` proposals to `review.jsonl` and History, alongside existing disposal/dresser proposals. Structured listing records include UnitPrice, Total, World, Retainer, PriceSource and PriceObserved. Unchanged snapshots are suppressed; the manual snapshot button starts a fresh review snapshot. Switching out of log-only never enables automatic execution by itself.

## Verification boundary

Automated checks cover repeated splitting, residual thresholds, replacement candidates, own-price matching, combined/separate quality, missing/stale/future/wrong-world quotes, full/holed capacity, conflicting snapshots, stock/slot conservation and the native/preview wiring. Existing last-copy, overflow restart, log-only denial and acknowledgement regressions remain applicable.

No in-game acceptance is claimed. Before unattended execution, verify partial native quantity callbacks, multiple acknowledged chunks from one source, replacing skipped candidates, all 20 slots, Market Buddy coordination, cancellation during a split and observed-versus-executed prices on a small reviewed sample.
