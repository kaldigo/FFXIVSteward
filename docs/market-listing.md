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

Non-equipment is protected from disposal by default, including materials, consumables, tokens and coffers. Sale rules can still list it. Below-floor stacks stay protected unless that exact item ID has an explicit non-equipment disposal permission in Rules. Permission remains subject to fresh valuation, keep/exclusion/collection checks and AutoRetainer's existing selection and enabled methods; it does not add an item to AutoRetainer's sale list.

Each item proposal includes `Collection` evidence: observation time, exact dresser copy count (including unlocked outfit components), armoire ownership, armoire eligibility, planned removal quantity, copies remaining after the proposal, nullable confirmation and native/Glamaholic/gearset references, acquisition/rarity and matched protections. Cached retainer source stacks remain labelled unverified. Missing evidence is null, not zero. Armoire migration may have no collection copy remaining immediately after restoration: its durable pending-collection hold protects the item until the armoire transfer is confirmed. Unchanged observation timestamps alone do not generate repeated log records; manual snapshots record fresh evidence. Refresh failures are also written to history while automatic execution is off.

Open **Market preview** and the desired retainer's selling list. The preview reads actual occupied/free slots and live source stacks without opening game menus or requesting in-game searches. Background Universalis requests, fresh Allagan Market cache observations or complete manually obtained game comparisons provide price references. Missing prices or unavailable capacity are shown as held/unknown. Cache-based estimates explicitly note incomplete own-price coverage outside the current retainer. A preview never authorizes execution: native execution compares the live market again.

The shared planner allocates actual slot holes and shows each proposed stack's item, quality, quantity, unit price, total, source and quote origin/time. Log-only writes `WouldList` and `WouldReprice` proposals to `review.jsonl` and History, alongside existing disposal/dresser proposals. Structured listing records include UnitPrice, Total, World, Retainer, PriceSource and PriceObserved. Unchanged snapshots are suppressed; the manual snapshot button starts a fresh review snapshot. Switching out of log-only never enables automatic execution by itself.

## Verification boundary

Automated checks cover repeated splitting, residual thresholds, replacement candidates, own-price matching, combined/separate quality, missing/stale/future/wrong-world quotes, full/holed capacity, conflicting snapshots, stock/slot conservation and the native/preview wiring. Existing last-copy, overflow restart, log-only denial and acknowledgement regressions remain applicable.

No in-game acceptance is claimed. Before unattended execution, verify partial native quantity callbacks, multiple acknowledged chunks from one source, replacing skipped candidates, all 20 slots, Market Buddy coordination, cancellation during a split and observed-versus-executed prices on a small reviewed sample.

## Universalis price source

The user-authorized direct integration uses the [official current-listings API](https://docs.universalis.app/): HTTPS GET requests for the current numeric world ID and up to 20 relevant item IDs per batch, with at most 100 listings per item and no sales history. Requests are spaced by at least five seconds, successful responses are retained for five minutes before refetching, and failures use capped backoff with Retry-After support. Responses are bounded to 4 MiB. Network work never opens or submits game UI. Disabling the source removes its quotes; unloading cancels network requests.

Prices use server lastUploadTime, never the HTTP download time. Wrong-world/item responses, malformed values, zero prices, future or stale upload data, empty listings and unidentified competitors never become disposal valuations. NQ/HQ quotes stay separate until the configured quality policy combines them. Quotes exclude known own retainer IDs, require loaded ownership information, and exclude mannequin listings. Opaque/unavailable retainer identifiers hold that quality rather than assuming another seller. No public average or last sale is treated as a current competing listing.

The preview and review log show Universalis source, upload timestamp and freshness/availability status. Existing maximum cache age applies (30 minutes by default); Price Insight may display older prices or a broader geographic scope. A new download cannot refresh the underlying market upload. Universalis remains provisional: the complete current in-game comparison and all protection, own-price, quantity, slot and price-readback checks still gate actual posting/repricing.

## Category rules and exclusions

Rules use the same ItemUICategory game sheet as AutoRetainer storage. Each sale rule supports Gear, Other items, or All items, with multiple included categories and per-rule excluded categories. Empty included categories mean all categories within the chosen group. Other items includes materials, consumables and other non-gear, so select the actual material categories for a materials-only rule. Item ID and quality restrictions intersect with these filters. Each rule retains independent minimum unit/total values and maximum stack size. The first matching enabled rule wins; move specific gear/material rules above a catch-all rule. Listing proposals log RuleName, and collection evidence includes ItemCategory and Equipment.

Per-rule exclusions skip only that sale rule; a later rule can still match. The separate global category exclusion list leaves those categories untouched by Steward: no listing, disposal or collection/cleanup movement. Global exclusions also skip new public price queries and produce ExcludedCategory review records. They do not clear pending collection holds or grant disposal permission.

Existing all-item rules retain their scope. Legacy single Category values migrate to the multi-select list without broadening their match. Category settings persist in Steward only; no AutoRetainer storage plans are modified. Category matches never override glamour/gearset/last-copy protection, and a materials sale rule never grants non-equipment disposal permission.

Unpriced Universalis items (including empty listings/zero upload timestamps) now receive item-specific PriceHeld records. Malformed per-item responses hold only that ID while preserving valid members of the same batch. Those IDs use the normal successful-request retry interval, allowing later inventory batches to progress. Wrong-world or invalid request-scope envelopes still reject the request. Detailed failure messages replace exception-type-only diagnostics.
