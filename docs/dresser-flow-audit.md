# Full collection-flow audit, 2026-10-27

## Observed run

- 06:07:28 UTC: AutoRetainer starts the armoire visit.
- 06:07:32: first dresser visit starts to obtain live collection contents.
- 06:07:39–06:08:04: retainer selection and confirmed withdrawals occur.
- 06:08:34: the return dresser route starts.
- 06:08:40: item 14799, inventory 0/0, list row 13 is submitted to the existing selection callback. Within 3 ms both dresser windows are hidden, selection data is unavailable, and no confirmation is visible. Steward labels the callback's false return as rejection. This is Steward's interpretation, not a game-provided explanation.

The return delay matches `nextDresserAttempt`, set to first visit time plus 60 seconds and not reset when the withdrawal pass completes. A successful phase transition must not inherit a failed-visit retry cooldown.

## Reference coverage correction

The previous comparison was insufficient. A matching callback in another repository is not proof of the correct ordinary-item storage flow.

The installed English `MirageStoreSetItem` sheet has 1,170 rows and 5,931 distinct component IDs. Item 14799 is **not** among them. Current [GlamourLog storage](https://github.com/Jaksuhn/ffxiv_glamourlog/blob/74e736f7b67be859bf4b975818fa593751ac55df/GlamourLog/Tweaks/PrismBox/StoreAllDresserTask.cs) iterates outfit sets and submits `StoreNewOutfit` / `StoreExistingOutfit`. Its full flow validates ownership, gearsets, repair condition and prisms, and waits for consumption. It is not a general ordinary-item storage implementation for 14799.

The [earlier GlamourLog UI implementation](https://github.com/Jaksuhn/ffxiv_glamourlog/blob/117a1ad03559917006044159fc2750d7df77700a/GlamourLog/Tweaks/PrismBox/StoreAllDresserTask.cs) opens the set-conversion agent, waits for the conversion UI, selects its storage action, handles a distinct confirmation, and resynchronizes the list. It also explicitly selects items belonging to a set. Steward's simple selection → Yes/No sequence and rejection of conversion windows cannot cover this outfit flow. Its broad acceptance of Yes/No in the reference must not be copied into Steward; exact ownership and approved sources remain required.

## Complete flow that needs validation

1. Follow AutoRetainer's activation and acquire only owned coordination. The separate idle-activation gap remains tracked in `autoretainer-activation.md`.
2. Obtain live armoire/dresser evidence when necessary; keep persistent observations distinct from execution permission.
3. Select missing approved copies; reserve inventory and dresser space, exclude gearsets/manual retainers, save pending collection holds before withdrawal.
4. Use AutoRetainer's existing retainer executor and acknowledge each withdrawal. Release postprocessing and close the bell before travel.
5. Return immediately after successful withdrawals, with retry delays only for failed route attempts.
6. Distinguish ordinary items from outfit pieces. Check exact source, repair/prism prerequisites, existing loose/outfit copies and destination capacity. Never submit unrelated auto-selected pieces.
7. Use a verified selection/conversion path for that branch, wait for its actual UI transitions, and bind each confirmation to the exact source and owner.
8. Require source consumption plus exact live collection ownership before clearing the operation. Refresh the list before selecting another item; handle partial inventory-capacity batches without duplicate submission.
9. On cancellation, timeout, unload or changed protection, keep uncertain-operation holds, cancel only owned dialogs, and release only owned coordination.

## Diagnostic build (0.1.0.40)

`/steward trace dresser` observes manual UI receive events and subsequent selection/window state for at most two minutes / 200 records. It performs no UI actions or native writes, requires automation to be idle/off, and retains the existing stopped-run hold. `/steward trace stop` ends it early. A manual ordinary-item storage interaction is needed because the inspected storage references do not cover that branch. Do not call this diagnostic build a collection fix or rerun automatic collection based on it.

No AGPL implementation was copied. No release in this investigation is claimed to have passed in-game storage acceptance.

## Manual trace and ordinary-storage correction (0.1.0.41)

The user's 06:37:02-06:37:04 UTC manual storage provides the ordinary flow:

1. MiragePrismPrismBoxCrystallize receives ListItemClick, parameter 0. The payload's tree index is 1, while the agent source row is 0 (headers occupy tree positions).
2. The agent selects item 14799, Inventory1/0 and sets ContextAddonId=131. SelectYesno 131 is initially not ready, then ready. ParentId and HostId are both zero. Neither set-conversion window opens.
3. Yes receives ButtonClick, parameter 0. ContextAddonId clears and the dialog hides before inventory changes.
4. About 150 ms later player quantity changes 1 to 0, live dresser ownership false to true, and prisms 230 to 229. Both dresser windows remain open.

The executor now uses the native tree's DispatchItemEvent(ListItemClick) on a unique matching leaf, excluding headers and disabled/hidden rows. Tree entries are resolved fresh each time, rather than treating the agent's index as a UI index. Exact item/container/slot checks remain mandatory before any confirmation: names locate a candidate but never authorize storage. Ambiguous names stop the run rather than choosing a potentially protected duplicate.

Confirmation ownership requires the unchanged source window, exact selected source, and the agent's ContextAddonId matching the current dialog ID. General parent/host ownership rules elsewhere are unchanged. The same identity checks guard Yes and cancellation with No. Protection changes cancel only this owned confirmation. Completion waits for both source removal and exact live dresser ownership, even after selection UI changes; missing live evidence cannot acknowledge success.

The completed retainer collection pass resets the dresser retry deadline and automatic-work timer before the next storage decision. A carried collection batch is handled before another retainer sweep; if the dresser is already open with eligible carried gear, collection storage runs before travel. Failed route attempts retain their retry backoff. AutoRetainer still owns retainer selection and travel.

Validation covers the observed parentless dialog, changed window/context/source rejection, delayed source/destination acknowledgement and collection-pass timer wiring. Build and automated checks do not constitute in-game acceptance. The next live run must verify dispatch across multiple rows/items and return travel. Outfit conversion remains a separate unsupported path that stops safely; this ordinary-item trace does not establish its flow. AutoRetainer activation parity remains separately tracked in autoretainer-activation.md.

## Source category, exact row matching and withdrawals (0.1.0.42)

The 06:51 test did not confirm any new withdrawal. Version .41's carried-item priority suppressed the entire retainer sweep when player or armoury gear was eligible. This guard has been removed: eligible missing retainer copies can be collected even with existing carried gear. Space reservations and the existing-copy checks remain in CollectionNeeded and final submission. On pass completion the dresser deadline is reset, and another sweep is deferred for at least the next automatic-work interval so a long pass cannot immediately restart ahead of return travel.

Read-only inspection of the installed executable established the native metadata layout, rather than assuming display text or tree order:

- PopulateCrystallizeAndFireRefresh (RVA 0xDB4860) writes six uint fields per tree entry. Ordinary items contain type, agent row index, icon, source container, source slot, flags. The name is separately formatted display data. The source row's container/slot and the agent index are used directly for matching, with an additional exact item-ID check against the agent candidate. Header/group types and ambiguous metadata are rejected.
- Source-category dispatch (RVA 0xDB52EC) enumerates categories 0: inventory 0-3; 1: containers 3500/3200; 2: 3201/3202/3203; 3: 3205/3206; 4: 3207/3208; 5: 3209/3300. Unsupported containers are not treated as bag inventory.
- Category switching uses the normal next-category button. Each advance waits for the dropdown and agent to agree, native population completion, and settled tree layout. It does not write category indices or bypass gearset filters. The source fingerprint and protection decision are checked throughout and immediately before confirmation.

Executable SHA256: 46a52ab1dbe80b9a8d6c4d4531bf66693904200c5135facacd053ef28a5de14a. No game process was modified or attached for this inspection. No AGPL implementation was copied.

The full path was reviewed through CompanionPolicy.Run/EnqueueOverflow/EnqueueRetainers, Steward's postprocessing handoff, WithdrawForOtherActions, AutoRetainer's Retrieve, source/destination acknowledgement, postprocessing release, return route and dresser storage. Native retainer commands remain in AutoRetainer. Manual retainer exclusions, live space, item identity, enabled features, AutoRetainer protection and gearsets remain gates. Planning and confirmed-withdrawal logs now identify the retainer, source slot, player counts and capacity so empty/blocked passes can be distinguished from successful transfers.

Tests cover every supported/unsupported source category; headers, reordered rows and duplicate item locations; ambiguous/stale metadata; parentless confirmation ownership; source/destination acknowledgement; category-rebuild waits; retainer scheduling with existing player gear; and existing withdrawal protections. They do not simulate the FFXIV client. In-game acceptance of the complete multi-retainer, multi-category cycle is still pending. Outfit conversion remains separately held.
