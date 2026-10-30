# Automatic upstream reference sync (RimWorld 1.7)

Copy `reference_sync_gui.py`, `START_REFERENCE_SYNC.bat`,
`reference_sources.auto.json` to `checker/` and
`tools/reference_sync_auto.py` to `checker/tools/`.
These files are additions: do not replace the existing `tools/reference_sync.py`.

Double-click `START_REFERENCE_SYNC.bat` or run `py reference_sync_gui.py`.
Enter any of these locations: Steam root, Steam library, `steamapps`, RimWorld
installation directory, or `steamapps/workshop/content/294100`. The scanner
uses `About/About.xml`, Workshop item IDs and game `Data/` subfolders. If Steam
has extra libraries, their `libraryfolders.vdf` entries are used when found.
The GitHub URLs of supported repositories are prefilled and editable. Blank
branch means the latest default branch, not a fixed SHA. You can optionally
enter a branch or a 40-digit pinned SHA. GitHub takes precedence; if its XML
cannot be matched or GitHub cannot be reached, a detected local copy is tried.

Select `Preview` to compare; inspect `MISSING`, `REVIEW`, and the origin SHA
of each proposed change. `Apply` replaces only matched, validated changed
files after confirmation, with previous file backups and a JSON report in
`checker/output/reference-backups/`. `HISTORICAL` version snapshots are never
replaced. No upstream files are deleted. Unknown/new filenames are *not*
automatically added. Local UI settings are stored under gitignored `output/`.

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
