# Dresser implementation review, 2026-10-27

The 05:16:41 log selected item 14799 from inventory 0/15 with both dresser windows visible. Ten milliseconds later both were invisible, before any confirmation appeared. The user reports that the windows were open when the alert appeared; this is not evidence that the user had closed them.

FFXIVClientStructs defines `AtkUnitBase.FireCallback`'s final boolean as `close`, default false. Steward had passed true through its shared callback helper. Dresser item selection now explicitly passes false and retains the existing selection event arguments. The selection-to-confirmation step waits through temporary unreadiness within the existing twelve-second timeout. A ready confirmation still requires the exact selected inventory item and original parent/host, and successful storage still requires source consumption and live collection acknowledgement. This is a code/API-supported fix, not in-game acceptance.

## References inspected

- [FFXIVClientStructs AtkUnitBase](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Component/GUI/AtkUnitBase.cs): callback signature, also verified against the installed assembly metadata.
- [GlamourLog storage task](https://github.com/Jaksuhn/ffxiv_glamourlog/blob/master/GlamourLog/Tweaks/PrismBox/StoreAllDresserTask.cs): uses native `MirageManager.StoreNewOutfit` / `StoreExistingOutfit`, supplies inventory locations, and waits for consumption. This is a different outfit-aware path, not confirmation that Steward's UI event arguments have been exercised in game.
- [GlamourLog IPC](https://github.com/Jaksuhn/ffxiv_glamourlog/blob/master/GlamourLog/Services/IpcProvider.cs): exposes readiness and bulk `EntrustAll`, but no exact approved-item operation. Calling its bulk entry point would bypass Steward's selected subset and is unsuitable for this integration.
- [GlamourLog crystallize handler](https://github.com/Jaksuhn/ffxiv_glamourlog/blob/master/GlamourLog/Tweaks/PrismBox/CrystallizeListHandler.cs): filters and repopulates the native list; it is not an individual selection/confirmation executor.
- Pandora's UI feature inventory contains `DisableKnownGlam`, not a comparable dresser storage executor.

No GlamourLog AGPL implementation was copied into this MIT project. AutoRetainer remains the owner of retainer operations and navigation. A future switch to a native outfit operation needs its own source/destination and ownership validation; it is not included speculatively in this fix.

## Validation and remaining acceptance

Regression coverage includes keeping collection visits separate from execution permission, gearset/exclusion/full-dresser route guards, planning all 421 unselected disposals without queuing any, and retaining live submission checks. Both builds and their safety suites must pass before publishing. The in-game test must confirm the callback row, exact confirmation ownership, source consumption, collection acknowledgement and release of the owned handoff. Inn obstacle navigation is unchanged. Normal activation following AutoRetainer is tracked separately in `autoretainer-activation.md`.
