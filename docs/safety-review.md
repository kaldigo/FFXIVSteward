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

## Third review

This pass traced permission through the final native target and explicitly reviewed failure recovery:

- **Recovery could re-latch the old GC transaction.** Clearing persisted flags did not clear the previous pending item, acknowledgement timestamps and one-shot gates. Successful explicit recovery now resets that local transaction; it cannot reset an active, unfaulted pending journal.
- **Failed saves could relax guards in memory.** Failed detach/review/acknowledgement writes now restore the previous ownership, review and pending markers. Recovery resets the local GC state only after persistence succeeds. Failed enable writes leave automatic execution off; failed frontend review writes restore the hold.
- **Own market/dresser actions could miss AutoRetainer's explicit keep list.** The companion now queries the existing `AutoRetainer.PluginState.IsItemProtected` API for preview and action decisions. Protection overrides market/storage/disposal; unavailable results hold. Exclusions retain highest priority. AutoRetainer's final slot boundary also rechecks its explicit protection after calling Steward.
- **Desynthesis confirmed an agent selection without binding it to the permitted source.** The final agent snapshot must match item, quantity, container, slot and flags. Source identity is checked again after the policy callback. The implementation compiles against the installed SDK's value-type `DesynthItemSlot`; the older pinned source's pointer shape is not assumed. Actual native population/timing of those fields is unverified, and a mismatch holds.
- **A visible but unready confirmation was treated as absent during cancellation.** It now fails cancellation rather than acknowledging safe cleanup.
- **A pause-manager exception could prevent fault notification.** The review marker remains set and peer notification is attempted even when the pause helper fails.

Injected persistence failures, successful recovery, old commit-gate reset, changed desynthesis identities and upstream-protection decisions have automated regressions. Protocol/safety revision 4 requires both updated plugins and starts in dry-run mode. No installed plugin settings or native inventory actions were changed by this audit. The remaining native acceptance and cross-zone collection-refresh limits above are unchanged.

Validation: both plugin builds passed; Steward passed 317 C# checks and 16 Python tests, and AutoRetainer passed 59 C# checks and 47 Python tests. Read-only saved imports yielded 286 Glamaholic item IDs, 1,098 market observations and parsed Market Buddy preferences. Installed game-sheet replay kept all 286 referenced IDs with no collected copy, and 629 ordinary marketable-blue rows received no blanket blue protection. Replay quotes and ownership were simulated; this is not live inventory verification.

## Repeated review after the third pass

The third pass did not establish the persistence contract of its real dependency. Inspection of the pinned ECommons `EzConfig.SaveConfiguration` showed that its writer catches and logs disk errors. Tests injecting an exception into a substitute save action therefore did not prove that production native submissions would stop on actual disk failure. That was a material gap in the earlier assurance.

The policy now uses its own synchronous `steward-policy.json` ledger. Writes flush a new file before replacing the previous state, propagate failures and do not share a path with background configuration writes. Required ledger fields and schema are validated; corrupt records cannot become empty unmanaged defaults. Runtime ownership is held independently of ordinary config imports. First use migrates the old companion markers. Failed connections retry persistence even when an owner already exists in memory. Startup remains blocked if the safety ledger cannot be loaded. No policy save uses `EzConfig.Save`.

Steward's own synchronous `steward-state.json` is authoritative for settings and pending queue work. The old Dalamud config is only a mirror after migration. Automatic execution is off on every load, covering a failed Stop write followed by restart. A pending operation records its character, remains pending on cancellation or failed acknowledgement writes, and requires review on that character.

| Boundary rechecked after fixes | Result in source and automated coverage |
| --- | --- |
| Queue start, native tick, cleanup, acknowledgement | Journal before the first tick; only acknowledged, cleaned-up work clears it. Cancellation during startup, tick or cleanup cannot turn into completion. Failed writes cancel dependents. |
| Vendor via NPC and retainer, including quick retainer sales | Shared guarded submission saves first, revalidates source/protection/lease after the write, submits once, and requires exact source and total quantity reduction. Original workflows wait for acknowledgement instead of dropping the remaining candidates. |
| Desynthesis | Same guarded submission, one-item reduction, final selected-agent identity check, and native busy/dialog checks. |
| GC | Existing one-shot journal retained, now using the throwing synchronous writer. Exact one-item acknowledgement, pending reward and HQ ownership checks remain. The obsolete waist inventory is excluded from strict loaded-inventory evidence. |
| Armoire | One guarded submission instead of invoking the native store before the retry throttle. Requires source reduction and a positive loaded cabinet ownership result. |
| New listings/repricing | Current retainer, source, form values, rules and current market request remain bound. Listing count must match exactly. Successful listing requires the expected source reduction and matching new listing; repricing requires the expected listing and price. The sell form must no longer be visible. |
| Withdrawal | Character, retainer ID, source, quantity and HQ are checked; destination increase and source removal must match exactly. |
| Dresser restore/deposit | Loaded source/destination evidence and exact quantity changes replace “anything changed” acknowledgement. Deposits require known dresser ownership and a closed confirmation. Last-copy/protection checks remain. |
| Review, detach, restart, character switch | Backlogged AutoRetainer work and active transactions prevent review/detach. Pending work survives restart; a different character cannot clear the companion operation. Ordinary config imports do not detach the policy. |
| Integrations and original behaviour | Other plugin files remain read-only. AutoRetainer retains selectors, travel/storage workflows and identity; unregistered characters bypass the optional policy after successful startup. Bulk conversion and managed discard remain held. |

Additional tests exercise actual temporary-file writes, atomic replacement, failed replacement, damaged/missing ledger fields, reloads, and all one-to-five-item source-removal combinations. Source-wiring assertions supplement these executable tests; they are not native execution tests.

### Remaining acceptance blockers

- No native actions were run. Native callbacks, selected-item field population, UI closure timing, inventory update ordering, cancellation and other plugins' pause/lock behaviour remain unverified in FFXIV. Conservative guards may stop valid operations until these are measured.
- A verified safe collection-data refresh after zoning is still absent. Unloaded collection/reference evidence blocks dependent work. Whole-inventory unattended cleanup is not established.
- Recorded client inventory/UI acknowledgement is not proof of a server-side acknowledgement protocol. Saved-data replays do not establish that an item is currently owned or that a live action is safe.

Repeated source review and passing tests cannot honestly remove these blockers. This remains a development preview, not a certification that unattended cleanup cannot lose items.

Final local validation for this revision: Steward built with zero warnings/errors and passed 395 executable logic checks plus 18 Python contract tests. AutoRetainer built with zero errors and existing compiler warnings, passed 179 executable safety checks plus 53 Python integration/update tests. Real file I/O tests required normal filesystem access; the restricted runner rejected temporary-file renames, and those tests were rerun with that access. Read-only imports found 286 Glamaholic IDs and 1,112 cached quotes. The final source walks covered submission/protection/ownership and persistence/cancellation/recovery separately; they found no further code changes to make within those audited paths. This does not change the native blockers above.
