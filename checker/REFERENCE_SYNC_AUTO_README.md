# Automatic upstream reference sync (RimWorld 1.7)

Reference Sync is included in current Checker; no manual copying or obsolete updater is required.

Double-click `START_REFERENCE_SYNC.bat` or run `py reference_sync_gui.py`.
Enter any of these locations: Steam root, Steam library, `steamapps`, RimWorld
installation directory, or `steamapps/workshop/content/294100`. The scanner
uses `About/About.xml`, Workshop item IDs and game `Data/` subfolders. If Steam
has extra libraries, their `libraryfolders.vdf` entries are used when found.
The GitHub URLs of supported repositories are prefilled and editable. Blank
branch means the latest default branch, not a fixed SHA. You can optionally
enter a branch or a 40-digit pinned SHA. GitHub takes precedence; if its XML
cannot be matched or GitHub cannot be reached, a detected local copy is tried.

Select `Preview` first. `COPY` means **new reference XML not yet imported**
and `REPLACE` means a previously imported reference has changed. Both are
included by **Apply reviewed changes**, with prior-file backups and a JSON
report under `checker/output/reference-backups/`. Complete source Definition XML and relevant CE patch XML are discovered
without pre-existing reference files. After applying, rerun Preview: those references should
normally change from `COPY` to `SKIP`.

`MISSING` denotes a reference that cannot be resolved to a source. Reference Sync
also imports newly discovered `Defs` XML from game Core/DLC installations and
third-party mods even if `OriginalXML/` is initially empty. It imports race,
PawnKind, biome, egg/item, wool/silk/leather, inherited base and relevant CE
patch XML under source-specific paths. Exact upstream relative paths are
retained under `_Defs/`, `_Leathers/`, or `CEPatches/`; explicit 1.7 files
take precedence over older game-version snapshots.

The source configuration includes Alpha Biomes, Alpha Animals, Vanilla Animals
Expanded modules and Combat Extended through their GitHub repositories and/or
Workshop IDs. Dinosauria and Megafauna have Workshop IDs and are read from
local installations. Disabled or unavailable sources cannot be reconstructed:
review every warning, especially missing registered DefNames. This is a source
coverage check, not proof of complete RimWorld patch-load-order semantics.

Review COPY/REPLACE entries before applying. Existing files are backed up;
the updater does not delete unrelated files during preview or apply. Machine
preferences live in ignored `checker.local.json`; upstream source paths are
versioned in `reference_sources.auto.json`.

The tool is intentionally independent of patch generation. After applying
reference changes, regenerate patches with Checker's existing generator,
review diffs, and run tests/runtime checks before deploying generated XML.

CLI alternative (from `checker/`):

```powershell
py -m tools.reference_sync_auto --config reference_sources.auto.json --steam "D:\\SteamLibrary"
py -m tools.reference_sync_auto --config reference_sources.auto.json --steam "D:\\SteamLibrary" --apply
```

`--no-github` uses only installed mods; with GitHub enabled the newest default
branch is fetched each time without changing your Zoology Git checkout.

**Limitations**: Discovery is conservative, not a RimWorld XML load-order
interpreter. Identical filenames with several different plausible sources
are marked `MISSING` (reason: ambiguous) rather than silently chosen. Files
whose upstream filenames or Def identities have changed radically require
manual review. GitHub
branches are not guaranteed to correspond to the most recent Steam Workshop
release; inspect source provenance before accepting replacements. A local
Steam game/mod may still be outdated. The UI reports missing local sources.
