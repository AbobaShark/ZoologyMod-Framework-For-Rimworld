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
report under `checker/output/reference-backups/`. New material reference XML
(for leather, wool, silk, and CE) is discovered even if its source group had no
references before. After applying, rerun Preview: those references should
normally change from `COPY` to `SKIP`.

`MISSING` means that an **already tracked** reference could not be matched.
Therefore, `MISSING 0` does **not** mean that all materials are available,
especially when there are outstanding `COPY` entries. The GUI displays new
and updated imports first, includes a `COPY` counter, and reports the number
of newly found material XML files. `REVIEW` and `HISTORICAL` are never written.
No upstream files are deleted, and arbitrary new race XML is not imported
without an existing reference. Local preferences are stored in ignored `checker.local.json`, not in a copy of the versioned reference paths. The obsolete `output/reference_sources.local.json`, `OriginalPatches/` and `generated_patches/` are deleted on GUI startup without migration.

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
manual review. Existing historical Megafauna strata are protected. GitHub
branches are not guaranteed to correspond to the most recent Steam Workshop
release; inspect source provenance before accepting replacements. A local
Steam game/mod may still be outdated. The UI reports missing local sources.
