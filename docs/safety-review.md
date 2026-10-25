# Inventory safety review — 25 October 2026

The earlier build and contract checks were insufficient to establish safe in-game operation. This audit found real gaps and hardened both plugins. These remain development previews; no game actions were exercised and no installed plugin settings were changed.

## Findings and corrections

| Finding | Correction |
| --- | --- |
| Populated collection sets could survive loss of native loaded flags, including zoning. | Every refresh invalidates readiness first. Required equipment cannot be disposed of based on a stale dresser/armoire copy. |
| Queue actions could judge cached protection/import data. | Action judgments refresh native data and Glamaholic immediately. Source identity is checked again before submission. |
| Last-copy dresser simulation could be undone by a fresh read. | The exact omitted collection item survives a refresh for the duration of the judgment. Other verified copies and armoire ownership still count. |
| GC reward and HQ confirmations were not gated at the final step. | Confirmations require an owned pending item, same character, unchanged inventory fingerprint, unexpired window and fresh policy permission. The reward text must identify the pending item. Unknown native layouts hold. |
| A denied discard could still queue a generic confirmation handler. | Managed characters never enter discard scheduling or confirm its dialogs. |
| Quick-sell shortcuts and low-level sale helpers lacked companion checks. | Those automated routes now recheck policy. Desynthesis rechecks before its final native confirmation. |
| A global postprocess lock did not identify its owner. | Protocol 2 records Steward handoff ownership, checks it for menus/withdrawals/market work and releases only that recorded handoff after lease loss. |
| Standalone dresser work could overlap AutoRetainer. | Dresser and market work acquire coordination only while AutoRetainer is idle and release their own suppression afterward. |
| A stopped-run flag was lost on reload. | Uncertain/failing/cancelled active work disables automatic execution and persists an inventory-review hold. Clearing it does not enable automatic execution. |
| Submitted market actions could be treated as complete when their classification changed. | After submission, wait for native acknowledgement; do not skip it because a later protection judgment differs. |
| Missing expected Market Buddy settings could fall back to defaults. | Market execution holds until those settings are readable or importing them is deliberately disabled. A loaded Market Buddy must accept the named lock. |
| Bulk outfit IPC cannot revalidate each item after delegation. | Managed bulk transfers are blocked. AutoRetainer's original unmanaged workflow remains available. Individually checked native transfers remain. |
| Some armoire-eligible items could bypass collection rules. | Armoire eligibility itself requires collection before surplus sale/disposal. |
| Unknown offering owners could be treated as competitors. | Unknown seller identity holds repricing; own retainers remain excluded. Prices use the current world. |
| `Item.IsGlamorous` was mistaken for a dedicated cosmetic-item flag. In installed data it is true for 28,614 of 29,058 equipment rows. | It is not used as purpose evidence. Item-level-one cosmetic gear, equipment-level-one gear, references, curated lists and source/rarity protections determine collection. The 639 marketable blue equipment rows do not receive a blanket dresser-compatibility protection. |

## Automated evidence

- Steward runs the production rules and action policy against missing/stale data, protected references, gearsets, exclusions, blue marketability, duplicates, last-copy removal, armoire eligibility, quality/world mismatches, own listings and failure/cancellation scenarios.
- AutoRetainer runs the production confirmation/lease predicates against changed characters, items, slots, quantities, quality, missing data, revoked permissions, expired leases and other owners. Contract tests check their placement at native call sites; existing upstream-preservation tests remain.
- The actual saved Glamaholic v3 file parsed successfully: 286 distinct protected item IDs. Actual Allagan Market data parsed 1,047 cached quotes, and actual Market Buddy settings parsed. These were read-only checks, not fresh price or game-state validation.
- Market Buddy's upstream IPC was inspected at `PunishXIV/Marketbuddy` commit `7cc07f57df005b6b113564fb7853d6a05837df1b`: `Lock` and `Unlock` register boolean named-lock operations.

Automated checks cannot prove native UI identities, callbacks, memory layouts, packet ordering or server acknowledgement behaviour. A successful build is not an in-game safety certification.

## Before enabling unattended use

1. Update both plugins together and keep automatic execution off. Confirm the current character is connected and Preview/History show destructive actions held. Until connected, AutoRetainer retains its original behaviour.
2. Load native collection/plate data and review Preview against the real inventory. Confirm known Glamaholic/native-plate pieces, nonmarketable blues, special-source gear, gearsets, exclusions and the last collection copy stay protected. Use Keep or Exclude for personal exceptions; the curated starter list contains only ten items.
3. Native acceptance must first use a deliberately chosen disposable item with unrelated AutoRetainer disposal selectors disabled. Verify the exact item, destination/return and history for one route at a time, including Stop and a lost connection. Do not start with a whole-inventory run.
4. If data becomes unavailable after zoning, a confirmation does not match, an outcome is uncertain or a run stops, inspect inventory before clearing the review hold. A submitted game action may still finish after Stop. Unsupported bulk outfit operations remain held.

This audit did not install, enable or run either plugin in FFXIV. Native acceptance remains outstanding.

## Second review

Further review found and corrected additional execution-boundary gaps:

- Final market submission now waits a frame after changing inputs, reads back native agent and dialog price/quantity, verifies the source and existing listing price, and rechecks current rules and pricing. A successful callback alone is insufficient.
- Market snapshots are queued on receipt and consumed on the framework thread. Only the active comparison is retained. Responses must match native item/request identifiers, finished loading state and expected listing count. Listings are keyed by their real listing IDs; a delayed page cannot overwrite a newer request merely because the item matches.
- Standalone work uses protocol-3 owned control instead of a general suppression boolean. The bridge rejects acquisition while busy/suppressed, pauses its main loop while Steward owns control, denies AutoRetainer item actions during that control, and expires/relinquishes only its recorded owner. It never clears another plugin's general suppression flag. Failed release acknowledgements stop work and are retried.
- A failed queue-building step cancels the partial plan and immediately persists the execution hold before releasing control. Direct UI requests cannot leave half a plan running. Menu timeouts and exceptions after a potentially partial AutoRetainer request also hold execution.
- Dresser and HQ GC confirmations now require an expected nonzero parent/host identity as well as the existing source/item/protection checks. Unknown relationships hold; their native layout has not been validated in FFXIV.
- Unloaded player state and unknown current worlds cannot authorize actions or market valuations. Invalid CSV quality/ownership flags are rejected rather than treated as NQ/competitor defaults.
- Upgrading to safety revision/protocol 3 resets automatic execution to off before any execution handlers are registered.
- GC Deliver/HQ confirmations now use one-shot commit gates; waiting for acknowledgement cannot repeat a native commit. An uncertain GC transaction persists a hold in AutoRetainer's own configuration and notifies Steward immediately. Both sides must clear their hold, and pending confirmation windows must be resolved first.
- A closed GC dialog alone is not acknowledgement. The next transaction waits for an exact one-item inventory reduction with no pending confirmation; missing, unexpected or timed-out inventory acknowledgements hold instead of reopening/retrying the same delivery.
- Before a managed GC native commit, a pending-transaction marker must be saved successfully. Exact acknowledgement clears it. Reloading AutoRetainer promotes any uncleared marker to a review hold, and stopping before acknowledgement also holds. This journal is logic-tested/source-checked; an actual game/process interruption has not been exercised.
- Standalone control and review holds reuse AutoRetainer's existing YesAlready/TextAdvance pause managers, including before opening a companion dialog. Pending owned dresser confirmations are declined on cancellation; an unknown or uncloseable prompt holds execution. Competing AutoRetainer disposal callbacks are denied while Steward owns an active queue/handoff, including the interval after a backend reload.

The second read-only validation parsed all 286 saved Glamaholic IDs, 1,098 cached quotes and the saved Market Buddy settings. Cache growth since the first pass reflects changes in the existing files; the audit did not write them.

Native acceptance is still a blocker for declaring unattended whole-inventory cleanup ready. In particular, zoning can clear required native collection data; a safe refresh path at every destination is not verified. New dialog ownership, price readback and request-completeness guards may deliberately hold unsupported native states. Crash recovery and server acknowledgements have not been tested in game. These limits must not be described as completed native verification.
