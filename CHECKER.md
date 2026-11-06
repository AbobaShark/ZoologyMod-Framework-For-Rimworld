# Zoology Checker — RimWorld 1.7

Checker builds and validates Zoology's generated XML. The maintained documentation is in [checker/README.md](checker/README.md), [portable setup](checker/PORTABLE_SETUP.md), [Google Sheets sources](checker/GOOGLE_ANIMALSTATS.md), [materials](checker/ANIMAL_TEXTILES.md) and [reference synchronization](checker/REFERENCE_SYNC_AUTO_README.md).

## Current entrypoints

From `checker/`:

- `START_REFERENCE_SYNC.bat` or `py reference_sync_gui.py`: preview and import upstream XML into the local `OriginalXML/` reference cache.
- `START_PATCH_GENERATOR.bat` or `py rimworld_patch_generator.py`: generate patches into ignored `output/staged/`, then validate and automatically install generated XML (disable with `--stage-only` or the GUI checkbox).
- `START_DEF_GENERATOR.bat` or `py rimworld_xml_generator.py`: independent ThingDef generator.

To use the Patch Generator CLI:

```powershell
py rimworld_patch_generator.py --check
py rimworld_patch_generator.py --category leathers --category textiles --source "gsheet:1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4"
py rimworld_patch_generator.py --preview
```

## Sources, caches and generated XML

`generation.json` defines 151 XML destinations: 81 animal groups, 29 biome patches, 3 shared Def outputs and 38 other outputs. `reference_sources.auto.json` defines upstream reference groups and paths. CE references reside under `OriginalXML/CEPatches/`.

`checker.local.json` stores ignored per-machine preferences and data sources. Reference Sync stores only preference overrides, not a redundant copy of versioned source paths. Read-only Google Sheets authentication uses local credentials; see the linked source guide.

`OriginalXML/` and `output/staged/` are gitignored local reference and staging directories. Reference Sync and patch generation never run implicitly as part of the other. Generated XML is verified and installed by default. Use `--stage-only` for manual review and a later `--deploy --yes`.

The upstream XML inventory is rebuilt from current game, Workshop, and optional GitHub files without relying on historical reference filenames. Source files retain their original relative paths under `OriginalXML/<owner>/`, without synthetic material or Defs directories; CE patches are stored under `OriginalXML/CEPatches/`. The target RimWorld version comes from the installed game's `Version.txt`. For each mod, Reference Sync selects its newest compatible version folder (or the latest available when the game version is unknown). The previous material-only sync and startup legacy-cleanup code have been removed.

Run `py -m pytest -q tests` from `checker/` for unit tests. The registry preflight and tests do not replace in-game validation with RimWorld 1.7 and the actual loaded mod list.

Life-stage baby overrides are now declared in `checker/generation.json` and generated in their owning race patches. `LifeStages.foodMaxFactor` is 1 for all stages: maximum nutrition is already proportional to the current pawn size, including `bodySizeFactor`.

The `LifeStages` data source now declares `Source mod`; Checker owns separate
Royal Animals base/combat/CE outputs for all five megachicken growth stages.
The Megachicken `race/lifeStageAges` list is preserved from its source mod,
including its five age thresholds. Royal-specific CE changes are additionally
guarded against Royal Animals being absent. The CE folder loads after Royal
Animals so its damage factors take precedence.

Royal Megachicken age-speed multipliers use the inverse mass-bin midpoint
mapping of the actual Animals!AO game BodySize formula, not mass ×
bodySizeFactor. The `SERVICE_BodySizeMassBands` tab documents mass
intervals and the exceptional non-monotonic mapping around 2000 kg. New
`LifeStages` columns `BaseBodySize`, `StageBodySize`,
`BodySizeMassMid_kg` and `BaseMassMid_kg` make the source and
calculation explicit. Checker validates game sizes, corresponding masses,
and Hirt-based speed ratios before writing XML.

The source-specific marketValueFactor is now modeled from CombatPower
and BodySize terms of the Animals pricing formula; Core shared stages
use relative combat-power utility and no absolute adult-reference mass.
The genuine melee attack interval is modified by a Zoology-owned StatDef
and one Harmony postfix on VerbProperties.AdjustedCooldown. CE age AP
factors compensate for CE's inherent melee-damage^0.75 multiplier so
penetration is not scaled twice. CE crit/parry age stat factors are `MROUND(bodySizeFactor^0.165, 0.05)` for all supported stages, independent of adult mass and checked before generation. They multiply CE stat inputs (not CE's opponent-dependent final probabilities); a base stat of zero stays zero.
