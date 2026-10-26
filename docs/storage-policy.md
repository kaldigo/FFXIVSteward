# Collection storage policy

Implemented locally, 26 October 2026. Update both plugins: inventory-policy protocol 6. Automatic execution starts off on every load; dresser cleanup defaults off. No in-game acceptance is claimed.

## Protection and sources

Confirmed ordinary dungeon and normal-mode raid sources do not trigger blue-rarity protection. Native plates, Glamaholic, curated lists, level-one/cosmetic rules, explicit keeps, gearsets, exclusions and upstream protection still apply. Alliance, Savage, Coil and other high-end sources never become ordinary through an alternate normal source. Unknown sources stay conservative.

The bundled source index contains the reviewed exact IDs, with item-level evidence in `data/acquisition-sources-evidence.json`. It is derived from Teamcraft revision `f2918cca2b159d05f569e24b5828b5af38444d0a` and cross-checked against local client duty metadata. The reviewed routine-blue entries are 825 normal-raid and 70 ordinary-dungeon IDs. These are exceptions to one rule, not disposal lists. The incremental Glamour Log index uses ContentFinderCondition IDs; its fresh snapshot is published only after all duties have been inspected. Failure or missing IPC never publishes a partial ordinary snapshot.

## Space and overflow

The user chooses an empty-slot reserve, initially 20, with a slider. Armoire storage uses no dresser slots. Before every dresser insertion the current decision is revalidated against live free space. Available dresser placements and retainer retrievals are ordered by explicit/native/Glamaholic references first, then rarity/special-source protection, then level-one/pending protection, then common-only suggestions. No automatic dresser eviction is performed to make room.

Protected collection items beyond that allowance receive a character-scoped hold persisted synchronously before transfer. AutoRetainer visits available retainers at an accessible summoning bell and uses its existing native entrust command through an individually validated bridge. Full retainers and unique-item conflicts are skipped; other retainers are tried. Existing AutoRetainer plans/settings are not edited. When no bell or space is available, the remaining items stay protected and the player receives chat and notification alerts. Successful transfers identify the destination retainer in alerts/history. Repeated identical alerts are throttled for one minute.

Retainer storage never satisfies collection ownership. A stored pending item cannot be sold, desynthesized, GC-delivered, discarded or listed by the managed workflows. Changing its ordinary-source classification or glamour rules does not erase its pending hold. Holds survive restart and are released only after a live exact dresser/armoire copy is confirmed; armoire-eligible pieces require armoire confirmation. Detaching is blocked while collection holds remain. Deliberate manual game actions and unrelated plugins remain outside this boundary.

Stored pieces are not withdrawn while the dresser is at its reserve. Withdrawal budgets include collection pieces already waiting in player inventory, and a second pending copy is not withdrawn unnecessarily. Normal retainer postprocessing can retrieve them when space returns.

## Optional dresser cleanup

Enable **Clean dresser using all protection rules** to allow cleanup. **Preview dresser cleanup** lists the per-item result before execution. Disabling the toggle prevents new restorations. Automatic execution must also be enabled for native actions.

Every candidate is judged as though that specific dresser copy had been removed. Unneeded/surplus pieces are restored to inventory for the normal market/disposal rules. The last required copy stays in place. Unknown sources, gearsets, explicit keeps, exclusions and unavailable protection evidence prevent unsafe restoration. Outfit pieces use their native per-piece restoration path.

Armoire-eligible pieces missing from the armoire are restored under a persisted collection hold, then handed to AutoRetainer's existing armoire workflow. A full dresser does not block this migration. A free player inventory slot is required for restoration; a blocked cleanup alerts the player. Dyes do not affect protection or destination. Existing explicit AutoRetainer protection still wins.

## Automated evidence and native acceptance

Rule tests replay every bundled source classification, assert Alliance/Savage/Coil protection, verify the reserve boundary and all disposal permissions for stored pending copies, and round-trip per-character holds. Capacity tests cover unknown/full space, existing waiting items and repeated withdrawals. Cleanup tests cover toggle-off, last-copy simulation and armoire migration. Both transfer counts are required; source removal alone cannot confirm storage. The bridge tests verify missing/extra destination counts leave transactions pending or faulted.

Before enabling automatic use in FFXIV, native acceptance must verify: a full first retainer and a second with space; all retainers full; unique-item conflicts; cancellation and reload during an entrust; reserve-boundary insertion; protected last copies; multi-piece outfits; and dresser-to-armoire restoration/deposit. Callback delivery, live inventory readiness, bell travel, game-server acknowledgements and notification visibility cannot be verified by local unit tests.

## Additional pre-publication verification

Log-only review defaults on and blocks every companion-managed item action, independently of the automatic flag. Automatic execution cannot be enabled until it is switched off, and switching either way leaves execution off. Disposal proposals, eligible dresser-copy counts, armoire-migration proposals, persistent collection holds and unavailable-evidence summaries are written to `review.jsonl` and History. Cached retainer observations are explicitly marked; log-only never opens storage menus or submits a native action. Enabling dresser cleanup while log-only is on allows removal proposals to be inspected safely.

Preview and execution now share the same last-copy simulation helper, including exception-safe omission restoration and rejection of an unconfirmed source-copy count. Duplicate previews simulate successive removals and stop at the required last copy. Hold release shares a tested rollback helper; simulation of a missing dresser copy cannot clear a pending hold. Armoire migrations retain the hold until exact armoire ownership is confirmed.

Restart tests now write and reload the actual Configuration through AtomicState, then judge stored items after source/rarity and sale-rule changes. They test character isolation, stale/unavailable ownership, invalid or omitted hold fields, truncated JSON, failed hold-release persistence and a missing authoritative state file. Post-overflow safety states cannot silently migrate from the compatibility mirror if the authoritative file is lost. Older pre-overflow settings remain migratable. Local tests do not establish in-game acceptance.
