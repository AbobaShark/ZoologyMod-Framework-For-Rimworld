# Install and run Checker (RimWorld 1.7)

Place the `checker` directory next to the `Zoology` directory from the *same* current project checkout. Use the three `START_*.bat` files for the reference updater, patch generator and Def generator. **Do not copy the old `generated_patches` directory**; the new architecture does not need it.

```powershell
cd checker
py -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe rimworld_patch_generator.py
```

For Google Sheets sources install the optional OAuth dependencies:

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements-google.txt
```

In the GUI choose: (1) the common AnimalStats workbook or Google Sheets URL, (2) `generation.json` (supplied), (3) any separate TSV/other workbook sources for individual tabs, (4) local `OriginalXML` directory (including `OriginalXML/CEPatches` for CE XML) (populated by Reference Sync, not supplied in Git). Enable CE and optimization as required. Generate animal groups, products, venom, biomes, bionics, life stages, leathers, wool and silk, or everything. Material generation requires upstream material snapshots from Reference Sync (see [ANIMAL_TEXTILES.md](ANIMAL_TEXTILES.md)). Inspect the staged deployment before applying. The local paths are written to `checker.local.json` (ignored by Git), never the shared config.

Examples from `checker/` (launch without arguments to open the GUI; any options run the CLI without opening a window):

```powershell
py rimworld_patch_generator.py --source "D:\Data\AnimalStats.xlsx" --category animals
py rimworld_patch_generator.py --source "D:\Data\AnimalStats.xlsx" --all
py rimworld_patch_generator.py --source "D:\Data\Animals.tsv" --sheet "Animals CE=D:\Data\AnimalsCE.tsv" --sheet "Venom profiles=D:\Data\VenomProfiles.tsv" --category animals
py rimworld_patch_generator.py --preview
py rimworld_patch_generator.py --deploy --yes
```

CLI also accepts `--registry`, `--group`, `--no-ce`, `--no-optimize`, `--original-xml` and `--original-patches`. For full TSV operation, supply all necessary tab sources; a missing table is an error, not a signal to delete old output. No Git push or modifying the upstream repository is performed.

`output/staged/` is ignored, dynamically created, and can be deleted after deployment. `generation.json` must remain tracked. The same source sheets and versioned registry produce the same planned target paths on any machine. Contents depend on the input revision and original-reference snapshot, so pin/record the workbook and references when requiring reproducibility.

**Routing preflight:** `py rimworld_patch_generator.py --check` verifies all 132 intended destinations against `LoadFolders.xml` and checks the 18 required existing static XML before any generation. The new CE bionic patch is intentionally a new file, not a pre-existing source.

**Current limits:** The archived checker does not contain the sibling mod tree needed to test existing static production and life-stage XML, or to validate runtime CE behaviour. The read-only file validator is not an alternative to testing RimWorld 1.7 and CE in-game. Def generator `rimworld_xml_generator.py` is kept byte-for-byte unchanged.

`generation.json` lists the nonexistent `Alpine` and `MammothSteppe` markers under `reference_only_biomes`, not generated biome destinations. Existing AnimalStats `WildBiomes` values do not need editing; other unknown biome names remain validation errors.
