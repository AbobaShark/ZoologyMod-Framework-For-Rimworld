# Zoology Checker

Checker is the source-data validation and generation toolchain for Zoology. It reads **AnimalStats**, biomechanical exports, and original game/mod XML, then creates validated XML patches and generated C# profiles for the supported RimWorld installations.

**The spreadsheets own biological numbers and formulas.** Checker validates, joins, routes, and exports those values; generated patches are not the authoritative data source.

## Layout and prerequisites

Place `checker/` beside `Zoology/` in the same repository checkout. The configuration checked into Git is `checker/generation.json`, `reference_sources.auto.json`, and the generator defaults. Machine-specific paths and source choices go into ignored `checker/checker.local.json`. Upstream reference XML belongs in ignored `checker/OriginalXML/` and staged output in ignored `checker/output/staged/`.

From the `checker` directory on Windows:

```powershell
py -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe rimworld_patch_generator.py
```

For direct Google Sheets access, install `requirements-google.txt`. Google API credentials are local and read-only: `google_credentials.json` or `ANIMALSTATS_GOOGLE_CREDENTIALS`, with the token in `google_token.json` or `ANIMALSTATS_GOOGLE_TOKEN`. Never commit credentials or generated tokens. A current local XLSX or TSV needs no Google access.

Windows launchers:

- `START_REFERENCE_SYNC.bat` — upstream reference discovery and comparison.
- `START_PATCH_GENERATOR.bat` — patch generation, validation, and deployment.
- `START_DEF_GENERATOR.bat` — the separate ThingDef generator.

The corresponding entrypoints are `reference_sync_gui.py`, `rimworld_patch_generator.py`, and `rimworld_xml_generator.py`.

## Reference XML

Run Reference Sync before generating patches against changed installed mods. It can discover relevant XML from installed RimWorld, Workshop folders and supported GitHub source repositories. Preview changes before applying them.

```powershell
py reference_sync_gui.py
py -m tools.reference_sync_auto --config reference_sources.auto.json --steam "D:\SteamLibrary"
```

Original source-relative paths are retained under `OriginalXML/<owner>/`, and Combat Extended reference patches under `OriginalXML/CEPatches/`. These are **local input snapshots**, not files loaded by RimWorld or stored in Git. The scanner uses installed mod metadata, game version and the registered supported Defs; it is not a full simulation of RimWorld's XML patch-loading order.

Apply only reviewed changes. Missing or ambiguous source definitions must be resolved from actual source mods. Do not reconstruct absent XML or calculate replacement source values from old generated patches.

## Sources and data ownership

- [AnimalStats — WORKING](https://docs.google.com/spreadsheets/d/1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4/edit) contains species/group data and production-ready tables.
- [Calculations — WORKING](https://docs.google.com/spreadsheets/d/1QWEZNRR6mV_5luNib6dS5oy8SzoF4g1XRcIQHOqbdqg/edit) supplies biomechanics calculations that AnimalStats imports.
- `generation.json` owns group membership, module ownership, output categories, and exact output paths.
- `LoadFolders.xml` determines which generated destinations are actually available in RimWorld 1.6/1.7 and under optional DLC/mods.

Recognized workbook tables include `Animals`, `Animals CE`, `Bionics`, `LifeStages`, `LifeStageProfiles`, `SexDimorphism`, `SexAdultProfiles`, `EggMilkWool`, `Venom profiles`, `Biomes`, `Leathers`, and `Animal textiles`, as required by their respective generator categories. Individual sources can be overridden by workbook, Google URL, or TSV. Use DefName and named columns to join records, never coincident spreadsheet row positions.

### Life stages, dynamic growth, and adult sex profiles

`LifeStageProfiles` defines concrete Baby/juvenile and special-stage endpoints. `SexAdultProfiles` defines male and female endpoints for the ordinary adult life stage. Runtime day-by-day growth interpolates within age intervals; the optional sex-specific adult endpoint is approached from the shared juvenile stage.

Checker exports calculated body/health/locomotion/hunger, attack and armor data. It matches natural attacks by anatomical identity and validates tool references; Combat Extended fields have their own output. Flight availability and juvenile sprite scaling use generated stage data. Do not copy handwritten age-size factors from source-mod XML where a supported calculated profile exists.

The average baseline adult mass is preserved by the male and female mass inputs in AnimalStats. Adult price defaults to the species baseline; sex-specific pricing is exported only when explicitly enabled in the source data. Market value, armor, movement and attack factors must be consumed from the source calculation results, not recomputed in Checker from invented species multipliers.

For young animals, `foodMaxFactor` must not duplicate the existing BodySize contribution to maximum food storage; the separate `hungerRateFactor` controls metabolic consumption. Special lifecycles (including Royal Animals and Spectrobionta) retain their actual source stage identities and transition ages. Conditional CE outputs remain in CE-gated folders.

### Reproduction and offspring ecology

The independent tables inside `EggMilkWool` do **not** have matching row orders. Ordinary egg production, milk, wool and the separate amphibian-development section are joined by explicit species DefName. Only species whose amphibian row declares external aquatic fertilization receive the specialized roe/tadpole definitions.

The calculated aquatic clutch records supply ovum count, wet mass, nutrition/energy, hatching duration, embryo and tadpole survival fractions, larval biomass and metamorphosis timing; the runtime uses one Thing for each clutch or tadpole group, not thousands of animal Pawns.

The runtime settings `EnableAdvancedReproductiveBehavior`, `EnableAdvancedThreatAvoidance`, `EnableWaterHabitatBehavior`, and `EnableWinterDormancy` are defined in C# settings, not as per-species spreadsheet columns. They default to enabled. `OffspringShelter=DEN` takes effect for family denning only when both advanced reproduction and advanced threat avoidance are enabled; winter dormancy is independent. Checker must continue exporting the complete source-data profiles irrespective of the player's switches.

`OffspringShelter` and supported winter-dormancy fields are source-data features, not a request to embed species name lists in Python. Generator and runtime behavior must agree on allowed modes and the actual loaded XML extensions.

### Hunting and venom data

AnimalStats provides species-level strategy, refuge, constrictor, venom and sample-producer assignments. `Venom profiles` maps natural-attack ToolCapacity identifiers to their HediffDefs and antivenom metadata. `core/venom_profiles.py` validates required columns, capacity coverage, consistent profile metadata and sample compatibility, then generates `ZVenom_Samples.xml`, `ZVenom_AntivenomDrugs.xml` and `ZVenom_Recipes.xml` at the registry-owned Core destinations. The runtime dose and constriction behavior are defined by their C# implementations and combat Defs.

Natural wildlife habitat weighting uses the final in-game BodySize, ectothermy and the registered water-dependent race list. Ordinary biome eligibility and commonality are maintained in `Biomes` and exported to the registered biome destinations.

### Materials and production

`Leathers` describes hides/leathers; `Animal textiles` describes wool and silk `ThingDef` properties; `EggMilkWool` describes biological yield, intervals and reproductive outputs. A material's `Source mod` controls its owning route. Combat Extended armor values use their specific source columns and CE-gated destination.

Use actual upstream material definitions. Checker must not fabricate unsupported leathers or write unguarded cross-mod references to unavailable materials. Zoology-owned Core materials include `Leather_Beaver`, `Leather_Ostrich`, `Leather_Otter`, and `Leather_Squirrel`. Core's `Leather_GuineaPig` has its own generated material-stat patches. Odyssey-dependent cross-mod fallback definitions must be conditional on Odyssey being absent.

## Generation, preview, and deployment

From `checker/`:

```powershell
py rimworld_patch_generator.py --check
py rimworld_patch_generator.py --source "gsheet:1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4" --all
py rimworld_patch_generator.py --preview
```

The Patch Generator's CLI accepts `--category`, `--group`, `--source`, `--sheet`, `--no-ce`, `--no-optimize`, `--original-xml`, and `--ce-patches`. Use `--stage-only` to prevent the normal automatic deployment; after inspecting staged changes, deploy manually using `--deploy --yes`. Category examples: `animals`, `leathers`, `textiles`, and `life-stages`.

Checker stages XML and generated source before applying them, validates the registry/destinations and checksums, and does not treat old output as a source of facts. The `--check` registry preflight verifies actual registered destinations against `LoadFolders.xml`. Do not copy outputs into an unrelated mod version, or let an obsolete local snapshot stand in for the currently installed source.

The optimizer eliminates demonstrably redundant patch operations using available upstream references. The optional standalone XML compactor is invoked through `py -m core.rimworld_patch_compactor <staged XML path>`; it is not a substitute for upstream-aware optimization and should not be applied indiscriminately to shipped XML.

## Validation and runtime builds

From `checker/`, local checks can be run with:

```powershell
py -m pytest -q tests
```

The test suite covers input parsing, mass/stage constraints, sex-profile export, venom-profile export, material routing, generated XML structure, localization and atomic deployment. **Python tests do not validate C# compilation, in-game pathfinding, graphics, AI jobs, or Combat Extended combat behavior.**

After changing C# or generated C# sources, rebuild the appropriate `ZoologyMod.dll` for the game version and restart RimWorld. Source changes alone do not update an existing DLL. Exercise the modified mechanics in an actual game with the relevant DLC and mod combination loaded. For growth, inspect animals across multiple biological days rather than skipping straight across an entire stage with a one-year age command.

Documentation for gameplay and player controls belongs in [PLAYER_GUIDE.md](PLAYER_GUIDE.md); XML and runtime extension semantics belong in [FRAMEWORK.md](FRAMEWORK.md).
