# Zoology Checker and Generation Toolchain

The checker directory contains the maintainer toolchain that turns AnimalStats outputs into RimWorld XML, validates them against reference Defs and patches, optimizes the result and deploys generated race patches into the mod tree.

This document is the maintainer reference for the current `checker` toolchain.

## 1. Production inputs

The primary production workbook is:

AnimalStats — WORKING  
https://docs.google.com/spreadsheets/d/1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4/edit

The upstream biomechanical workbook is:

Calculations — WORKING  
https://docs.google.com/spreadsheets/d/1QWEZNRR6mV_5luNib6dS5oy8SzoF4g1XRcIQHOqbdqg/edit

The checker consumes final AnimalStats values. It does not calculate the bite/limb/teeth models itself and it does not reproduce Google Sheets formulas locally.

For normal animal generation the important final tables are:

- Animals — vanilla-facing production values;
- Animals CE — Combat Extended production values.

Other AnimalStats sheets feed those outputs but are not independently read by the ordinary generator path.

## 2. Supported AnimalStats source types

checker/animalstats_source.py recognizes:

- local .xlsx;
- local .xlsm;
- local .xls;
- local .tsv;
- a Google Sheets URL;
- gsheet:<spreadsheet-id>;
- a raw Google spreadsheet ID;
- a local .gsheet pointer created by Google Drive for desktop.

Excel and Google sources are multisheet sources. They can expose both Animals and Animals CE from one source.

A TSV source is one table only. If a workflow needs a separate CE table, use the patch fixer's optional CE-source field or use a multisheet source. The standalone rimworld_xml_generator.py has one source argument and therefore cannot represent two independent TSV files as Animals and Animals CE through that one argument.

## 3. Local dependencies

The scripts use Python 3, pandas and lxml. Excel reading uses the engine available to pandas for the selected Excel format.

Google Sheets mode additionally requires:

    py -m pip install google-auth google-auth-oauthlib

The repository includes checker/requirements-google.txt for those optional authentication packages.

Tkinter is used by the GUI tools and is normally included in standard Windows Python installations.

## 4. Google Sheets authentication

Google access is read-only. The code requests only:

    https://www.googleapis.com/auth/spreadsheets.readonly

Google sources use the official Sheets API and require Google API credentials even when the spreadsheet is publicly viewable in a browser.

### Desktop OAuth

Place a Desktop OAuth client JSON beside the checker scripts as:

    google_credentials.json

or point to it with:

    ANIMALSTATS_GOOGLE_CREDENTIALS

The first authorized read opens the local browser OAuth flow and stores the refreshable authorized-user token in:

    google_token.json

or the path specified by:

    ANIMALSTATS_GOOGLE_TOKEN

Do not commit either credentials file or token file.

### Service account

A service-account JSON is also accepted through the same credentials path.

Share the spreadsheet with the service account's client_email. Service-account credentials are loaded directly and are not serialized into google_token.json.

### Refresh behavior

Cloud sheet contents are not persistently cached by animalstats_source.py. source_cache_key returns no cache key for Google sources, so cloud data are fetched again when requested.

Local sources use a cache identity based on normalized absolute path and file modification time.

### Values returned by Google

The Sheets API request asks for UNFORMATTED_VALUE with row-major data. The loader converts the returned first row into headers, pads uneven rows and trims unused columns before constructing the DataFrame.

The checker reads calculated cell values, not formula text.

## 5. animalstats_source.py

This module is the common source abstraction shared by the generators.

Important responsibilities:

- identify Excel, Google and .gsheet sources;
- parse spreadsheet IDs from supported forms;
- validate whether a source is locally/syntactically available;
- acquire Google credentials;
- call the Sheets values API;
- normalize returned values into pandas DataFrames;
- expose source cache/display helpers.

This module does not generate XML.

## 6. rimworld_xml_generator.py

This is the standalone animal Def generator/updater. It has both a Tk GUI and a CLI.

It reads the Animals table and, for multisheet sources, attempts to read Animals CE from the same workbook/sheet source.

### CLI modes

Generate mode builds new XML output for a selected defName.

Update mode updates known fields in one XML file or recursively in a selected folder.

CLI:

    py rimworld_xml_generator.py --mode generate --xlsx <source> --def-name <DefName>

or:

    py rimworld_xml_generator.py --mode update --xlsx <source> --input-path <xml-or-folder>

Passing --input-path while leaving the mode at generate automatically switches to update mode.

### CLI arguments

| Argument | Meaning |
| --- | --- |
| --mode generate/update | Select new-def generation or existing-XML update. |
| --xlsx | AnimalStats source: Excel, TSV, Google URL/ID, gsheet: ID or .gsheet pointer. |
| --def-name | Animal defName to generate or restrict processing to. |
| --input-path | XML file or folder in update mode. |
| --out-dir | Output directory; defaults to checker/generated_defs. |
| --animals-sheet | Vanilla sheet name; default Animals. |
| --animals-ce-sheet | CE sheet name; default Animals CE. |
| --game-root-dir | RimWorld installation root used for reference Def lookup. |
| --generate-parent | Generate parent abstracts from the table in generate mode. |
| --overwrite-existing | In update mode, write back to source XML instead of out-dir. |
| --emit-ce-patches | In update mode, emit CE patch XML for processed defs. |

### Reference lookup

The generator can search the configured RimWorld game root for existing Core/Odyssey animal definitions. Those existing defs are used to preserve or derive values that are not supplied directly by the AnimalStats row.

### Parent generation

--generate-parent computes common parent values from the full table and can emit parent abstracts. Use it only when the target workflow actually owns those parent defs; it is not necessary for ordinary patch updates.

### Update behavior

Update mode walks one file or every XML file under a folder, finds matching animal defs from the Animals table and updates the known generated fields.

If --overwrite-existing is not used, results are written below out-dir.

If --emit-ce-patches is enabled and CE data exist, CE patch files are produced for the processed defs.

## 7. rimworld_patch_fixer.py

This is the main GUI patch-generation/fixing workflow. It has no independent argparse CLI; its entry point launches the Tk application.

The UI exposes:

- AnimalStats source;
- optional CE source;
- a list of XML files/folders;
- replace-in-place mode;
- preserve runtime XML preconditions;
- compact generated patches;
- Generate/Fix Patches;
- Generate From Original XML;
- Generate Biome Patches;
- table-driven groups;
- output-folder access.

### AnimalStats and CE source fields

AnimalStats source accepts TSV/XLSX/Google sources through the common source layer.

CE source is optional. When blank and AnimalStats is a multisheet source, the fixer reads Animals CE from the same workbook/Google spreadsheet.

For a one-table source such as TSV, supply a separate CE source if CE data are required.

### Replace original XML files in place

When enabled, selected source XML files can be updated directly.

Use this only when the selected files are intentionally maintained as generator outputs. For source-controlled handcrafted XML, generating into checker output and reviewing the diff is safer.

### Preserve runtime XML preconditions

This controls whether optimizer/fixer logic preserves defensive PatchOperationConditional-style guards that may still be needed at runtime even if a closed local reference tree would allow simplification.

The default config currently enables this behavior.

### Compact generated patches

When enabled, the fixer compacts only patch files successfully produced by the current run. Stale files already present in generated_patches are not compacted merely because they exist.

### Generate From Original XML

This workflow uses `checker/OriginalXML` as the baseline source tree and rebuilds patch output from known upstream defs without requiring an existing generated target patch.

### Generate Biome Patches

Biome generation consumes the biome output in AnimalStats and emits the relevant biome patch structure independently of race-field generation.

The fixer writes these files directly to the root of `checker/generated_patches` as `Biomes_<Biome>.xml`. `checker/generated_patches/move_xmls.py` deploys race XML from its mapped subdirectories only; it does not copy these root-level biome files into the mod tree. Review and place biome output through the appropriate biome patch workflow separately.

### Table-driven groups

The group editor stores output XML definitions and the animal defNames that belong in each output. Generate From Groups uses that grouping to rebuild grouped patch files consistently.

The table-group configuration is stored in rimworld_patch_generator_config.json.

## 8. OriginalXML

checker/OriginalXML is a checked-in reference corpus of upstream animal XML used by the patch-generation and optimizer workflows.

It currently contains reference material for:

- Core;
- Biotech;
- Odyssey;
- Alpha Animals;
- Dinosauria;
- Megafauna;
- Vanilla Animals Expanded;
- Vanilla Animals Expanded - Royal Animals;
- Vanilla Animals Expanded - Endangered;
- Vanilla Animals Expanded - Waste Animals.

OriginalXML is not the output directory. It represents source/reference defs that the checker can index and simulate.

## 9. OriginalPatches

checker/OriginalPatches contains upstream patch material that must be applied on top of OriginalXML for a more realistic effective baseline.

The committed reference set currently includes Combat Extended Core race patches.

OriginalXmlIndex can load both the original defs and a patches directory, allowing the optimizer to compare against the effective patched state.

## 10. rimworld_original_xml.py

This module builds the reference Def index used by the fixer and optimizer.

OriginalXmlIndex:

- recursively loads XML source files;
- records ThingDef/PawnKindDef sources;
- tracks duplicate defs and parse errors;
- resolves parent inheritance;
- applies configured reference patches through PatchApplier;
- exposes cloneable effective XML trees;
- knows RimWorld/Zoology-relevant default values through rimworld_patch_defaults.py.

The target parser recognizes direct ThingDef and PawnKindDef XPaths using defName or abstract @Name selectors.

This module is a library component; it is not a standalone command-line application.

## 11. rimworld_patch_apply.py

PatchApplier is the checker-side patch simulator.

It supports the patch forms needed by the current optimizer/reference workflow, including:

- PatchOperationAdd;
- PatchOperationAddModExtension;
- PatchOperationAttributeSet;
- PatchOperationRemove;
- PatchOperationReplace;
- PatchOperationSequence;
- PatchOperationConditional;
- PatchOperationFindMod.

It maintains operation statistics, missing targets and errors.

This is a validation/simulation implementation, not RimWorld's real patch engine. If a new generated patch begins using an unsupported operation, extend the simulator or avoid assuming the simulation proves runtime behavior.

## 12. rimworld_patch_defaults.py

This module defines the default scalar values used when comparing generated patches against effective RimWorld defs.

It currently contains default maps for:

- ThingDef race fields;
- statBases;
- PawnKindDef fields.

Examples include default body size, hunger, health scale, predator flags, max prey size, trainability-related values, armor/stat defaults and combatPower.

The optimizer uses these defaults to avoid emitting redundant operations when the target already has the intended effective value.

## 13. rimworld_patch_optimizer.py

PatchOptimizer receives an OriginalXmlIndex and rewrites generated patch trees while simulating their effect.

Its responsibilities include:

- remove redundant operations;
- collapse safe remove/add pairs into simpler replacements;
- compare scalar fields against effective reference/default values;
- preserve required conditional context;
- keep runtime missing-target guards when configured;
- simulate each retained operation so later optimizations see the updated effective state.

preserve_missing_target_guards is the critical safety mode behind the fixer's Preserve runtime XML preconditions option.

Do not optimize solely against a closed reference corpus if the patch intentionally supports load orders or third-party states not represented in OriginalXML. The preservation option exists for that reason.

## 14. rimworld_patch_compactor.py

The compactor reduces repeated XPath/PatchOperation structure without combining separate source files.

Standalone use:

    py rimworld_patch_compactor.py

With no paths it recursively compacts checker/generated_patches in place.

Specific files/folders:

    py rimworld_patch_compactor.py "generated_patches\Core"

It prints before/after totals for:

- bytes;
- PatchOperations;
- XPath nodes.

The fixer-integrated mode is intentionally narrower: it compacts only outputs successfully generated by that run.

## 15. generated_defs

checker/generated_defs is the default standalone rimworld_xml_generator.py output directory.

It is intended for newly generated/updated Def XML produced outside the main patch-fixer grouped workflow.

The directory may not exist in a clean checkout until a generator run creates it.

## 16. generated_patches

checker/generated_patches contains generated race patch output organized by target integration, including current groups such as:

- Core;
- Biotech;
- Odyssey;
- VanillaAnimalsExpanded;
- VanillaAnimalsExpandedRoyal;
- VanillaAnimalsExpandedEndangered;
- VanillaAnimalsExpandedWasteland;
- AlphaAnimals;
- Dinosauria;
- Megafauna.

These files are downstream staging artifacts generated from the production data and should be regenerated and reviewed when their source values change.

## 17. generated_patches/move_xmls.py

This deployment helper copies generated race XML from mapped `generated_patches` subfolders into the corresponding Zoology `ThingDefs_Races` patch directories.

Current mappings include:

- Biotech -> Zoology/DLC/Biotech/Patches/ThingDefs_Races;
- Core -> Zoology/1.6/Patches/Core/ThingDefs_Races;
- Odyssey -> Zoology/DLC/Odyssey/Patches/ThingDefs_Races;
- supported Vanilla Animals Expanded modules -> their ModPatches ThingDefs_Races folders;
- Alpha Animals -> its ModPatches ThingDefs_Races folder;
- Dinosauria -> its ModPatches ThingDefs_Races folder;
- Megafauna -> its ModPatches ThingDefs_Races folder.

Existing destination files are replaced. Root-level outputs such as generated `Biomes_*.xml` are outside this helper's mapping and are not copied.

The script locates the project root by searching upward for sibling Zoology and checker directories. It supports `--pause` and `--no-pause` console behavior on Windows.

Run it only after inspecting generated_patches, because it writes directly into the mod's source-controlled patch tree.

## 18. Configuration files

### rimworld_xml_generator_config.json

Stores GUI/default values for the standalone generator, including:

- AnimalStats source;
- output directory;
- selected sheet names;
- game root;
- generate-parent flag;
- mode;
- existing/update input path;
- overwrite mode;
- CE-patch emission.

The current committed config contains machine-specific Windows paths. Treat those paths as local convenience values, not portable project defaults.

### rimworld_patch_generator_config.json

Stores patch-fixer state, including:

- AnimalStats source;
- optional CE source;
- selected XML paths;
- replace-in-place;
- preserve runtime preconditions;
- table-driven groups;
- compact-generated-patches.

This config also contains machine-specific selected XML paths in the current repository. Review them before running on another machine.

## 19. Tests

checker/tests currently contains four test modules.

### test_animalstats_source.py

Covers:

- Google URL and gsheet: ID recognition;
- Google Drive .gsheet pointer recognition;
- DataFrame normalization;
- standalone generator use of the common Google loader;
- patch fixer use of the common Google loader.

### test_patch_compactor.py

Unit tests for compactor transformation behavior.

### test_compactor_integration.py

Checks fixer integration, including the rule that only successful outputs from the current run are compacted and that disabled compaction leaves outputs untouched.

### test_patch_safety.py

Safety/regression tests for optimizer and generated patch behavior, including:

- missing-field precondition preservation;
- closed-world optimization behavior;
- defensive remove retention;
- list replacement safety;
- Combat Extended body-shape handling;
- committed-patch structural checks;
- Alpha Biomes compatibility/MayRequire behavior.

Run the test suite after changing source loading, optimizer semantics, compaction or patch-generation rules.

### Runtime/gameplay tests outside checker

`checker/tests` does not cover the whole Zoology assembly. Runtime behavior has a separate `Zoology/Tests` suite.

The current runtime tests are:

- `test_animal_draft_compatibility.py`;
- `test_handler_baby_feeding.py`;
- `test_npc_animal_companion_hot_path.py`;
- `test_runtime_patch_registration.py`.

When a change touches both generated XML and runtime behavior, run the relevant tests from both directories. The handler-baby-feeding feature is a current example: its JobDef/WorkGiverDef are static XML, while target selection and the JobDriver live in the assembly.

## 20. Recommended maintainer workflows

### Refresh generated race patches from the live workbook

1. Ensure the AnimalStats workbook is in a valid QA state.
2. If using direct Google mode, configure read-only credentials.
3. Open rimworld_patch_fixer.py.
4. Point AnimalStats source at the working AnimalStats sheet.
5. Leave CE source blank when using the same Google/Excel workbook.
6. Select the relevant existing patches, OriginalXML workflow or table-driven groups.
7. Keep Preserve runtime XML preconditions enabled unless you have a specific reason to prove the closed-world result is sufficient.
8. Generate.
9. Review generated_patches.
10. Run checker tests and any affected runtime tests under Zoology/Tests.
11. Compare generated race patches with the live mod tree and separately check shared static patches that are not produced by the race generator.
12. Use `move_xmls.py` only for reviewed generated race XML covered by its mappings.
13. If biome patches were generated, review and place the root-level `generated_patches/Biomes_*.xml` outputs through the biome patch workflow separately.
14. Review the final repository diff before committing.

### Generate one new Def

1. Choose a multisheet AnimalStats source.
2. Run rimworld_xml_generator.py in generate mode with --def-name.
3. Supply --game-root-dir so manual/reference lookup can find upstream defs.
4. Use --generate-parent only if you intentionally need generated parent abstracts.
5. Inspect generated_defs before moving the result into the mod.

### Update existing XML without touching source files

Use update mode without --overwrite-existing and set --out-dir to a review location.

### Rebuild against upstream reference XML

Use Generate From Original XML in the patch fixer to derive output from `checker/OriginalXML` and the configured reference patches.

## 21. Common failure modes

### Google source is recognized but cannot be read

Cause: Google credentials are missing, invalid or do not have access.

Fix: configure google_credentials.json or ANIMALSTATS_GOOGLE_CREDENTIALS, install the optional auth packages and authorize an account that can open the spreadsheet.

### .gsheet path is rejected

The loader accepts a .gsheet pointer only when the file exists locally and contains a recognizable spreadsheet ID or URL. A misspelled/nonexistent .gsheet filename is intentionally not treated as a valid cloud source.

### Animals CE is missing

For Excel/Google, verify the configured Animals CE sheet name.

For TSV, remember that a TSV is one table. Use the fixer's separate CE-source field if CE output is needed.

### Output contains redundant patch operations

Run the optimizer/fixer with an OriginalXML reference and/or enable compaction. Runtime guards that support states outside the local reference tree must be preserved.

### Generated patches differ from the live mod tree

generated_patches is a staging area. move_xmls.py performs the explicit copy into Zoology. If deployment has not been run, the generated staging output and mod tree can legitimately differ.

## 22. Maintaining production data

Biological/model values are maintained in the spreadsheet pipeline documented in `FRAMEWORK.md`. `checker/generated_patches` contains downstream transformation output.

The checker performs transformation, reference-aware patch construction, validation and deployment. Scientific coefficients and species values remain in the workbook/model layer.

