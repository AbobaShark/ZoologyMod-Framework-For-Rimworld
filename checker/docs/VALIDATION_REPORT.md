# Checker-only validation

See the surrounding conversation for run-specific test results. All reproducibility statements require the same source workbook/sheet revision, reference XML revision, enabled CE option and optimization option. The isolated package cannot perform gameplay runtime verification without RimWorld 1.7 and Combat Extended installed.

- New explicit registry: 81 animal groups; 407 unique animals; 31 biomes; 3 shared venom Def targets.
- Clean distribution has **no `generated_patches` folder**; runtime staging goes to ignored `output/staged`.
- Original standalone `rimworld_xml_generator.py` is byte-identical to the input archive.
- The unified `rimworld_patch_generator.py` entrypoint offers GUI with no arguments or CLI with arguments. Both use the same registry and routing core.
- CE bionic renderer uses the CE-specific numeric columns and creates 30 conditional ToolCE replacements; compatibility still requires actual game testing.

## Unified Patch Generator entrypoint (October 2026)

- The obsolete `generate_animal_pipeline.py` launcher was removed. `rimworld_patch_generator.py` opens Tk with no arguments and invokes the same CLI behavior with command-line options.
- The original unified-entrypoint change left the generation core intact. Subsequent routing fixes now derive conditional module requirements, centralize destinations and cache local workbook reads. The manifest schema remains version 1.
- The standalone animal Def generator (`rimworld_xml_generator.py`) was preserved byte-for-byte (SHA-256 `6cc733a5aa5329745a9e5eab9c9ab8e88972e73902ba364677ec35207f50174f`).
- Regression tests cover GUI/CLI selection, all category selection, local setting overrides, `--preview` dry run, and `--deploy --yes` confirmation, plus command-line help.
- On the isolated Checker directory with a virtual Tk display: 87 tests, 80 passed and 7 skipped (full `Zoology/` is not bundled). Game runtime and Combat Extended runtime were not exercised.

## Full routing/manifest review — October 28, 2026

The generated-XML routing failure in `Generate All` was caused by a missing
`requires` field for product XML in conditional `LoadFolders.xml` locations.
The dependency is now derived by `core/xml_routing.route_dependency` from the
longest matching configured load directory and checked for **both v1.6 and
v1.7**. Unknown or inconsistent routes fail rather than silently dropping the
requirement. This does not change the mod's load order or add dependencies to
unconditional Core targets. The same resolver also handles static LifeStages,
vanilla bionics and the new CE-only bionics output. Older staged entries with
an absent `requires` field are revalidated on a later staging run.

The checked GitHub `main` tree SHA was
`4eb6a84813257cabcdaaf4d58859f66733af00ae` (read-only). The 115 paths in
the old managed GitHub manifest (81 race groups, 31 biome patches, 3 venom
Def XML files) all exist at the same paths in that tree. Their sorted pathname
list also matches the group registry in this Checker package (FNV-1a 64-bit
`be3208811219d456` over UTF-8 names separated by `\n`). All 15 production
paths and all three pre-existing static Bionics/LifeStages paths exist in the
Git tree. Two Hatcher files, formerly excluded, are now part of the production
route catalog:

- `Core/Patches/Core/Misc/CompProperties_Hatcher.xml`
- `DLC/Odyssey/Patches/Misc/CompProperties_Hatcher_Odyssey.xml`

The fourth non-race static destination,
`ModPatches/CombatExtended/Patches/HediffDefs/BionicAnimalVariants_CE.xml`, is
**new**, not yet a file in Git main. Its placement matches the active Combat
Extended `LoadFolders` directory, but its CE 1.7 runtime semantics have not
been established by these file-level tests.

The checker now audits **134 distinct potential XML destinations**: 115
registered race/biome/venom targets, 15 production XML, the vanilla bionic Def,
2 LifeStages XML, and the new CE bionic patch. Run
`python rimworld_patch_generator.py --check` to validate destinations and
verify that all 18 required existing static XML are present under the local
sibling `Zoology/` tree. Staged XML still undergo validation of XML roots,
checksum integrity and destination routing during `--preview`/`--deploy` (registered XML targets are replaceable).

Repeated workbook opens were also eliminated for local XLS/XLSX/XLSM through a
revision-aware, read-only per-process `ExcelFile` cache. The selected `Animals`
data frame is reused when generating production output in `Generate All`, and
Bionics/LifeStages sheets are reused for their vanilla and CE outputs. This
does not change tab values or formula evaluation; when reading local XLSX,
only the stored calculation results are available.

### Test scope

- Routing tests: all 134 locations checked against a snapshot of actual
  `LoadFolders.xml` rules (including CE package-ID aliases), and every
  explicitly declared registry route checked for unique ownership.
- Staging/deployment: products across Core/Odyssey/VAE, bionics and LifeStages
  including CE, preserving disjoint staged categories, checksum verification,
  migration of old staged entries with missing conditional dependencies.
- Real workbook smoke checks: all 81 animal groups, 31 biomes, 3 venom
  Def files, vanilla+CE bionics and LifeStages. The combined full `Generate All`
  completed with **119 staged XML**, and its preview gave **116 COPY and
  3 REPLACE** with no routing errors. Re-staging actual numeric EggMilkWool
  values for 3 representative test-only reproductive XML then produced a
  122-file manifest with correctly routed Core/Odyssey/VAE product outputs;
  the previous 119 entries were retained. These use test-only static XML
  skeletons, not the actual hand-authored static mod files. No user's Zoology
  XML was rewritten. The complete full optimization mode was not rerun.
- `EggMilkWool` is the current sheet name. The uploaded local XLSX snapshot
  still used `EggMilk`; its **temporary test copy** was renamed for these
  checks, without altering any cell content. Tests do **not** establish that
  the user's live Google Sheet has the same values.
- The real game/Combat Extended runtime, final fully optimized XML behavior,
  and local deployment over the user's actual working tree are outside these
  isolated tests. Static XML skeletons in tests are not a substitute for the
  exact authored XML in Git.
