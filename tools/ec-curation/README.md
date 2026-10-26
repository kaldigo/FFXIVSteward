# Internal Eorzea Collection update workflow

These are the original collection/preparation/finalization helpers used for the current bundle, moved from the local working folder into version control with explicit working-directory arguments. They are maintainer tooling, not plugin settings or runtime dependencies. No player configuration is read. No live crawl is started by building or running Steward.

Review this dataset for each major FFXIV patch. This is a release-maintenance step, not an unattended scheduler. The current list is a supplement to the default rarity/source/design protections, not an exhaustive EC ranking; switching off those other filters does not expand this dataset.

## Reproduce the current bundle

From the Steward repository root:

```powershell
python tools/build_common_glamour.py --check
python -m unittest discover -s tests -p 'test_common_glamour.py'
```

The frozen input is `docs/data/common-glamour-evidence.json`. Outputs are `Steward/data/common-glamour.json` and `docs/common-glamour.md`. The builder validates counts, links, scope, exact game IDs, coverage and preservation of existing protected IDs.

## Collect a new patch review

1. Create a separate working directory outside the plugin package. Export the installed English item/recipe/cabinet metadata with `tools/curation-game-data/Export.csproj` into that directory's `metadata.json`; see that tool's README.
2. Run `python tools/ec-curation/collector.py <work-directory>`. It binds to loopback only (default port 8769) and accepts manually reviewed public browser observations through its form. Stop it when done. It is a local evidence sink, not a crawler. GET endpoints expose the five named working JSON files to the local browser.
3. Collect across all jobs and supported equipment slots. Save `discovery.json` and `observations.json` using the same structures as the frozen evidence's discovery/catalogue/observations. Preserve full observed item names and per-piece EC search links. The original sampling uses six recent and six most-loved outfits per job plus census and historical seed pieces; review new jobs, level caps and slot coverage when the game changes. Do not silently label a bounded sample exhaustive.
4. Run `python tools/ec-curation/prepare.py <work-directory>`. It produces `catalogue.json`, resolves exact names against game metadata, and labels candidates already covered by default protection. Review acquisition evidence and any unresolved names; never infer ordinary acquisition from missing information.
5. Measure each remaining exact piece globally across all jobs/all time. Save `counts.json` with `kind`, `rows`, and `census`; use the current frozen `usage.rows` and `census` as the schema contract. Each usage row needs measurementVersion 2, complete first and last page evidence, page count, final-page count, page size 36, uses, top-page likes in descending order, observation time and most-liked-post link. Frequency is primary (current floor: 25 appearances); likes are supporting evidence, not a substitute. Failed/blocked pages remain unresolved rather than zero. Refresh the census and explicitly review any omitted or independently protected pieces.
6. Run `python tools/ec-curation/finalize.py <work-directory>`. This freezes the reviewed evidence into the repository only after the existing builder validates it and checks that existing protection is not silently removed.
7. Run `python tools/build_common_glamour.py`, then `--check`, the glamour tests and the full plugin checks. Review the evidence/bundle/report diff before publishing the patch update. Do not silently lower protection when an observation vanishes.

The raw working files are not installed with the plugin. Keep them in the maintainer workspace for audit. The runtime reads only the embedded exact-ID bundle and exposes its enable/disable filter, never its file path or update tools.
