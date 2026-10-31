# Zoology Checker — RimWorld 1.7

Checker builds and validates Zoology's generated XML. The maintained documentation is in [checker/README.md](checker/README.md), [portable setup](checker/PORTABLE_SETUP.md), [Google Sheets sources](checker/GOOGLE_ANIMALSTATS.md), [materials](checker/ANIMAL_TEXTILES.md) and [reference synchronization](checker/REFERENCE_SYNC_AUTO_README.md).

## Current entrypoints

From `checker/`:

- `START_REFERENCE_SYNC.bat` or `py reference_sync_gui.py`: preview and import upstream XML into the local `OriginalXML/` reference cache.
- `START_PATCH_GENERATOR.bat` or `py rimworld_patch_generator.py`: generate patches into ignored `output/staged/`, then preview and explicitly deploy.
- `START_DEF_GENERATOR.bat` or `py rimworld_xml_generator.py`: independent ThingDef generator.

To use the Patch Generator CLI:

```powershell
py rimworld_patch_generator.py --check
py rimworld_patch_generator.py --category leathers --category textiles --source "gsheet:1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4"
py rimworld_patch_generator.py --preview
```

## Sources, caches and generated XML

`generation.json` defines 147 XML destinations: 81 animal groups, 29 biome patches, 3 shared Def outputs and 34 other outputs. `reference_sources.auto.json` defines upstream reference groups and paths. CE references reside under `OriginalXML/CEPatches/`.

`checker.local.json` stores ignored per-machine preferences and data sources. Reference Sync stores only preference overrides, not a redundant copy of versioned source paths. Read-only Google Sheets authentication uses local credentials; see the linked source guide.

`OriginalXML/` and `output/staged/` are gitignored local reference and staging directories. Reference Sync and patch generation never run implicitly as part of the other. Generated XML must be reviewed before deployment.

The upstream XML inventory is rebuilt from current game, Workshop, and optional GitHub files without relying on historical reference filenames. Sources are laid out under `OriginalXML/<owner>/_Defs/`, material files under `OriginalXML/<owner>/_Leathers/`, and CE patches under `OriginalXML/CEPatches/`. The previous material-only sync and startup legacy-cleanup code have been removed.

Run `py -m pytest -q tests` from `checker/` for unit tests. The registry preflight and tests do not replace in-game validation with RimWorld 1.7 and the actual loaded mod list.
