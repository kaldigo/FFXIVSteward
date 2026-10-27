## 0.1.0.48

Resolve queued dresser restoration sources against current positions after each removal. Verify exact dresser-copy loss and player-copy gain rather than assuming positions remain stable, wait for transient loading, hold already-carried unique items, and report precise preflight/native/acknowledgement failures. Preserve per-action last-copy protection and verify the owned control lease. The reported Eternal Shade stop has insufficient diagnostics to prove its cause; in-game acceptance remains pending.

## 0.1.0.47

- Add explicit Run full cleanup / `/steward cleanup`: AutoRetainer visits included retainers, Steward withdraws approved disposal candidates, and AutoRetainer runs enabled GC, desynthesis and sale actions before returning for another batch. No venture timing requirement, market listing or glamour removal is introduced by this pass.
- Keep GC-first priority and per-item live checks. Count only confirmed withdrawals and disposal acknowledgements. Stop on no progress, outstanding GC candidates, method failures, capacity, lost permission, cancellation, or the bounded batch limit. Unique duplicates wait for a later batch.
- Require the companion cleanup capability and AutoRetainer's return-travel configuration for GC batches. Start is explicit, blocked in log-only/demo mode, and never resumed on plugin load. Retainer exclusions include the manual/gil-only retainer. Stop full cleanup also disables Steward automation.
- Core, integration and build checks cover the new cycle; the full live retainer-to-disposal-to-return loop still needs in-game acceptance.

## 0.1.0.46

- Enabling automation now waits for an actual AutoRetainer retainer handoff. It no longer starts a dresser, armoire, cleanup or overflow run from the idle timer. Explicit Run now actions still work, and authorized collection can finish its withdrawal/storage stages. Run permission is not saved and is cleared when stopped, completed, or automation is re-enabled.
- Check carried glamour gear for required repairs and missing glamour prisms before requesting the dresser route. Report what needs attention and resume the authorized pass when it is ready; self-repaired gear above 100% condition remains eligible.
- Regression coverage includes idle enable/reload, owned handoffs, character changes, stop/re-enable, manual runs, and repair/prism preparation. In-game acceptance remains pending.

## 0.1.0.45

- Fix ordinary dresser binding resolution: Dalamud's single-match scanner follows leading call instructions. Use literal multi-match scanning and require exactly one body and caller match before verifying their agreement.
- Add production-binding regression checks reproducing the scanner behaviour and holding missing, ambiguous or disagreeing signatures.
- The 0.1.0.44 live test confirmed two new-outfit deposits. Ordinary deposits stopped before submission; existing-outfit additions remain unverified in game.

## 0.1.0.44

- Replace ordinary dresser row selection and confirmation clicks with a verified MirageManager member-function binding. Both function and caller signatures must agree; unavailable bindings hold storage.
- Use existing native outfit calls to create outfits and fill missing pieces in matching-quality outfits. Submit one approved inventory source at a time.
- Preserve live protection checks, gearset exclusions, owned AutoRetainer coordination, repair/prism requirements and reserved dresser space. Require source removal plus live collection ownership before acknowledging each deposit; uncertain submissions are never retried automatically.
- Log the ordinary, new-outfit or existing-outfit route and confirmed transfer for the upcoming in-game test. Automated and static verification are complete; live acceptance of these three routes is not yet established.

## 0.1.0.43

- Fix the native crash when selecting a dresser item. The generic list-event helper omitted the tree-item pointer required by the dresser. Submit the exact unsigned callback payload used by the game's ordinary left-click handler instead.
- Preserve native source/category validation, confirmation ownership, final protections and source/destination acknowledgement. Crash-recovery review holds remain in place.
- The preceding live run confirmed 31 withdrawals and prompt return travel; it crashed on the first dresser selection before confirmation. Automated checks cannot establish live native acceptance.

## 0.1.0.42

- Restore retainer collection when eligible gear is already in player inventory or the armoury chest. Existing carried gear no longer prevents missing-copy withdrawals; space, duplicate and protection checks remain.
- Select the dresser source category for each inventory container and wait for native list rebuilding. Match item rows using agent index, source container and slot instead of display names or row order.
- Preserve immediate return after withdrawal passes, including long-running passes. Add retainer/source/player-count evidence to withdrawal logs.
- Automated checks pass; the complete live cycle still needs in-game acceptance. Disposal settings and AutoRetainer's executor are unchanged; outfit-conversion windows remain held.

## 0.1.0.41

- Correct ordinary dresser storage using the recorded manual list-click, owned Yes/No, and inventory/dresser acknowledgement flow. Resolve tree entries independently of agent rows, which exclude headers.
- Bind dresser confirmations and cancellation to the exact selected source and agent context; parentless ordinary confirmations no longer fail ownership checks. Protection and live completion checks remain mandatory.
- Resume collection storage immediately when the AutoRetainer withdrawal pass finishes, clearing the earlier visit's retry timer. Finish carried collection batches before starting another sweep.
- Automated regression checks cover confirmation identity and delayed transfer evidence. Live multi-item acceptance is still required; outfit conversion remains held.

Add an opt-in, read-only manual dresser trace (`/steward trace dresser`, `/steward trace stop`) to capture ordinary-item UI events, exact selection and confirmation ownership without automation. Two-minute / 200-record limit; requires idle/off automation and preserves the execution-review hold. Full-flow audit identifies a stale return-route cooldown and distinguishes ordinary items from the outfit-only reference implementations. This diagnostic build does not fix or enable automatic dresser storage. No protection/settings changes.

Reduce entrust work without relaxing live protection: permission checks refresh current inventory and glamour references, while price/import/prefetch processing stays on its normal background cadence. Plan collection and overflow from one refreshed snapshot, then revalidate each source before submission. Do not queue disposal withdrawals that AutoRetainer has not selected. Request AutoRetainer collection visits when live storage observations are missing instead of waiting for the user to open storage first. Select dresser items without closing the selection window; tolerate the bounded selection-to-confirmation transition while preserving exact source, confirmation ownership and destination acknowledgement checks. Compared GlamourLog's native storage implementation and bulk-only IPC; no bulk bypass or copied AGPL implementation. Both plugin updates are required. In-game acceptance remains pending; AutoRetainer activation lifecycle integration remains the recorded final-release blocker.

Improve dresser failure diagnostics: record the selected item, source slot and actual window ownership/readiness at the rejected step. Correct the misleading cancellation error when the dresser selection window has disappeared but no confirmation remains. Cancellation and submission still require the original confirmation owner. This diagnostic update does not claim to resolve the unverified in-game deposit failure. Navigation and existing automation/protection settings are unchanged.

Fix collection withdrawal planning stopping after selecting a retainer: count waiting dresser items from captured candidates while retaining live protection checks. Apply the fix to both initial planning and pre-transfer capacity checks. Preserve transaction holds and log full exception details for future failures. Existing automation, disposal and protection settings are unchanged. In-game acceptance remains pending. Following AutoRetainer activation for all background collection/storage work remains a final-release requirement, not a capability of this update.

Fix decoded retainer names in storage progress labels and keep the window closed on startup/reload. Publish the previously tested collection/storage exclusions, persistent protection handling, disposal-route logging, turnover-based sale rotation, optional dedicated dye strategy, and confirmed-empty-market Universalis-average fallback. Requires AutoRetainer storage capability 3 for automatic execution. Existing feature settings are preserved; no automation is enabled by this update. In-game collection acceptance remains pending.

Replace category checkboxes with clickable icon buttons: blue highlights for selected categories and amber borders for partial saved selections. Use combined category names and counts in selected summaries. Hide Soul Crystals from the picker and exclude them from Select all. Preserve existing rule scope, four marketboard-style groups, whole-group controls and wrapped icon rows. No automation or protection policy changes.
