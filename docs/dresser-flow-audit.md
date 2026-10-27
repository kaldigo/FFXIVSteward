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

## Next evidence collection

`/steward trace dresser` observes manual UI receive events and subsequent selection/window state for at most two minutes / 200 records. It performs no UI actions or native writes, requires automation to be idle/off, and retains the existing stopped-run hold. `/steward trace stop` ends it early. A manual ordinary-item storage interaction is needed because the inspected storage references do not cover that branch. Do not call this diagnostic build a collection fix or rerun automatic collection based on it.

No AGPL implementation was copied. No release in this investigation is claimed to have passed in-game storage acceptance.
