# AutoRetainer activation requirement

Status: outstanding final-release blocker, recorded 2026-10-27.

Normal Steward automation must run as part of AutoRetainer's configured run. If AutoRetainer waits for bell interaction, Steward must also wait. Enabling Steward, loading it or standing near a bell must not independently initiate collection, overflow or storage UI work. Passive previews and cache refreshes may continue.

Retainer postprocessing already uses AutoRetainer's integration. However, `Plugin.Update` currently polls `AutomaticStorageWork` while idle, and that method requests independent collection/overflow sweeps. This must be integrated with AutoRetainer's run lifecycle before final release. Do not duplicate upstream navigation or dialogue handling.

Acceptance must cover bell-interaction-only activation, AutoRetainer's other supported start modes, startup/reload, no-work runs, cancellation, suppression, logout and resuming only after a new authorized run. Include collection from retainers followed by dresser/armoire work, full-dresser overflow and coordination with other postprocessing plugins. Keep explicit manual actions distinct, preserve all review/protection gates and release only owned locks on cancellation or failure.
