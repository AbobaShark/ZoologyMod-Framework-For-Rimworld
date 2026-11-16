# Zoology Checker

Checker reads **AnimalStats**, biomechanical calculations and original game and mod XML to generate Zoology's animal definitions, patches and C# profiles.

**Biological values and formulas are maintained in the spreadsheets.** Checker checks the calculated values and writes them to the corresponding XML and C# files.

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

Recognized workbook tables include `Animals`, `Animals CE`, `Bionics`, `LifeStages`, `LifeStageProfiles`, `SexDimorphism`, `SexAdultProfiles`, `EggMilkWool`, `EnergeticsProfiles`, `Venom profiles`, `Biomes`, `Leathers`, and `Animal textiles`, as required by their respective generator categories. Individual sources can be overridden by workbook, Google URL, or TSV. Use DefName and named columns to join records, never coincident spreadsheet row positions.

### Life stages, dynamic growth, and adult sex profiles

`LifeStageProfiles` defines concrete Baby/juvenile and special-stage endpoints. `SexAdultProfiles` defines male and female endpoints for the ordinary adult life stage. Runtime day-by-day growth interpolates within age intervals; the optional sex-specific adult endpoint is approached from the shared juvenile stage.

Checker exports calculated body/health/locomotion/hunger, attack and armor data. It matches natural attacks by anatomical identity and validates tool references; Combat Extended fields have their own output. Flight availability and juvenile sprite scaling use generated stage data. Do not copy handwritten age-size factors from source-mod XML where a supported calculated profile exists.

The average baseline adult mass is preserved by the male and female mass inputs in AnimalStats. Adult price defaults to the species baseline; sex-specific pricing is exported only when explicitly enabled in the source data. Market value, armor, movement and attack factors must be consumed from the source calculation results, not recomputed in Checker from invented species multipliers.

For young animals, `foodMaxFactor` must not duplicate the existing BodySize contribution to maximum food storage; the separate `hungerRateFactor` controls metabolic consumption. Special lifecycles (including Royal Animals and Spectrobionta) retain their actual source stage identities and transition ages. Conditional CE outputs remain in CE-gated folders.

### Reproduction and offspring ecology

The reproductive data are maintained in three main tables:

| Sheet | Contents |
| --- | --- |
| `Animals` | Gestation, breeding seasons, thermal limits, diapause, reproductive pauses, captive durations, biological litter and clutch sizes, shelter preferences and winter dormancy. |
| `EggMilkWool` | Egg counts, egg formation and incubation, egg nutrition, milk and wool yields, and aquatic egg and tadpole development. |
| `EnergeticsProfiles` | Nutritional costs of growth, pregnancy and egg production. |

Checker matches animals by DefName. The sections of `EggMilkWool` have independent row orders.

The ordinary litter curve, egg count range and calendar-scaled development periods are exported to the species' usual game definitions. Captive bird laying durations use the mean game-balanced clutch size and biological annual egg production: the balanced mode scales production relative to two chicken eggs per day, and the realistic mode uses biological eggs per 365 days. Alternative reproduction values from `Animals` are written to each species' `ModExtension_SeasonalReproduction` in its race XML:

| AnimalStats column | XML field |
| --- | --- |
| Captive gestation (game days) | `captiveGestationDays` |
| Captive diapause (game days) | `captiveDiapauseDays` |
| Captive real diapause (game days) | `captiveRealDiapauseDays` |
| Captive egg formation (game days) | `captiveEggFormationDays` |
| Captive real egg formation (game days) | `captiveRealEggFormationDays` |
| Captive reproductive pause (game days) | `captiveReproductivePauseDays` |
| Captive incubation (game days) | `captiveIncubationDays` |
| Realistic litter size | `realisticLitterMean` |
| Realistic egg count range | `realisticEggCountRange` |

Biological litter size comes from `Base stats`; biological clutch sizes and incubation calculations use the egg data in `EggMilkWool`. The game chooses the applicable values using its reproduction settings. Both sets are exported regardless of the selected settings.

`EnergeticsProfiles` is exported to `Core/Defs/Misc/EnergeticsProfiles_Generated.xml`. Its growth model assigns half of ordinary allometric intake to tissue formation and adds any uncovered stage growth requirement. Standard life-stage hunger factors are calculated as the mean daily intake over the stage relative to the adult reference. The catalog provides the allometric exponent and growth efficiency for dynamic growth using loaded stage durations and sex-specific endpoints. The catalog stores the full fetal tissue budget, developmental stage weights, standard mean litter and clutch sizes, egg nutrition and the female's food requirement. The game derives the additional pregnancy and clutch costs from the full production budgets after crediting half of ordinary intake over the applicable active developmental duration. Edible egg nutrition matches hatchling nutrition for ordinary laid eggs.

The aquatic egg and tadpole definitions use the count, wet mass and energy per ovum, development durations, and survival fractions in `EggMilkWool`. Only animals with external aquatic fertilization receive these definitions. Each clutch and tadpole group is represented collectively until juvenile amphibians emerge.

Winter shelter eligibility and maternity denning seasons are exported through `ModExtension_WinterDormancy`. Natural reproductive shelters are defined by `OffspringShelter`.

After changing reproduction data, generate both the **animals** and **products** categories, or use **Generate All**.

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

Checker prepares the output in a staging folder and checks the files before installing them. Use `--check` to verify that the configured output paths agree with `LoadFolders.xml`. Keep the game and mod reference XML matched to the version you are generating for.

The optimizer eliminates demonstrably redundant patch operations using available upstream references. The optional standalone XML compactor is invoked through `py -m core.rimworld_patch_compactor <staged XML path>`; it is not a substitute for upstream-aware optimization and should not be applied indiscriminately to shipped XML.

## Validation and runtime builds

From `checker/`, local checks can be run with:

```powershell
py -m pytest -q tests
```

The tests check the source tables, generated files and deployment. Build the mod and test animal behavior in RimWorld with the required DLCs and other mods enabled.

After changing C# or generated C# sources, rebuild the appropriate `ZoologyMod.dll` for the game version and restart RimWorld. Source changes alone do not update an existing DLL. Exercise the modified mechanics in an actual game with the relevant DLC and mod combination loaded. For growth, inspect animals across multiple biological days rather than skipping straight across an entire stage with a one-year age command.

Documentation for gameplay and player controls belongs in [PLAYER_GUIDE.md](PLAYER_GUIDE.md); XML and runtime extension semantics belong in [FRAMEWORK.md](FRAMEWORK.md).
