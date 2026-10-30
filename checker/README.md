# Zoology Patch Generator — RimWorld 1.7

The Checker builds RimWorld XML patches from AnimalStats **without using any previously generated patch as input**. `generation.json` is the Git-versioned source of group membership, abstract Def names, module ownership and exact XML destinations. Generated XML is published only to ignored `output/staged/` until explicitly reviewed and deployed.

## User entrypoints

- `rimworld_patch_generator.py` — one entrypoint for both Tk GUI (no arguments) and CLI (with arguments); generate selected categories or all, inspect and deploy.
- `rimworld_xml_generator.py` — **unchanged** standalone animal **Def** generator, separate from patch generation.

Run `python rimworld_patch_generator.py` to open the GUI, or pass options to use the
same program as a noninteractive CLI (for example,
`python rimworld_patch_generator.py --all`). The former
`generate_animal_pipeline.py` entrypoint is removed; the generation and routing
modules under `core/` implement the shared generation and routing logic.

## Inputs

Use one multisheet local XLSX/Google Sheets source with tabs `Animals`, `Animals CE`, `Bionics`, `LifeStages`, `EggMilkWool`, `Venom profiles`, and `Biomes`; the actual biome population export uses its existing AnimalStats inputs. Each needed sheet may instead use an individually configured TSV or workbook/Google URL. Both GUI and CLI support source overrides. No one else's Google credentials are included or required for local XLSX/TSV.

`OriginalXML/` and `OriginalPatches/` contain optional, approved optimization references. They are never overwritten by the generator; for explicitly reviewed refreshes from local installations or pinned GitHub commits, see [docs/ROUTING_REFERENCES_AND_OPTIMIZATION.md](docs/ROUTING_REFERENCES_AND_OPTIMIZATION.md). CE output is controlled by an explicit checkbox/flag. The standalone Def generator is independent.

## Output destinations and routing

All **132** destinations, including race, biome, reproduction, bionics and life-stage outputs, come from `generation.json`. No filename guessing and no migration based on preexisting generated XML. The route validator checks each destination against `Zoology/LoadFolders.xml`; CE output is restricted to the CE integration directory. The registry currently covers 81 animal patch groups (407 unique DefNames) and 29 biome patches and 2 reference-only biome markers. The `outputs[]` section adds 15 production targets (including Core and Odyssey hatchers), vanilla/CE bionics, and vanilla/CE life stages; Python no longer enumerates their destinations. Run `python rimworld_patch_generator.py --check` to check all 132 destination declarations and their 18 required static source files on the local installation.

The `reference_only_biomes` list in `generation.json` declares names such as `Alpine` and `MammothSteppe` that occur in AnimalStats `WildBiomes` but have no actual `BiomeDef`. They remain available as spreadsheet markers, but Checker skips their biome XML outputs. Any other unregistered biome still fails generation. If an older staged manifest contains those former outputs, clear the ignored `checker/output/staged/` directory before regenerating.

**Staging is not deployment.** `output/staged/generation-manifest.json` includes SHA-256 of every produced XML. Deployment validates staged checksums and destinations, then replaces XML at selected registered paths regardless of prior ownership or manual changes. Preview shows copy/replace/skip actions without writing. Partial staging preserves other categories and does **not** automatically delete previously deployed files. The output directory and local settings are ignored in Git.

Specialized production files and the base bionic/life-stage XML still need an existing copy of the mixed handwritten XML in the sibling `Zoology/` tree: numeric fields are taken from AnimalStats while manual predicates, recipes and other structural content remain unchanged. The new CE bionic patch is generated from the CE-specific Bionics columns (`CE Damage`, `CE Blunt AP`, `CE Sharp AP`, `CE Attack interval`) and staged at `ModPatches/CombatExtended/Patches/HediffDefs/BionicAnimalVariants_CE.xml`; it is not loaded without Combat Extended. It needs in-game CE 1.7 runtime verification before release.

## Safety and compatibility

Regen inputs must contain the referenced animals; unknown groups, ambiguous or missing destinations, missing CE body shapes and invalid numeric cells fail visibly. `Generation` never writes into the mod; `Deploy` asks for confirmation and rechecks staged checksums and destinations before writing. `Zoology/LoadFolders.xml` and existing repo contents are not changed by distributing this archive.

Run `python -m pytest tests` from `checker/` for isolated unit tests. Integration and runtime tests using the full `Zoology/` tree must be run after installation. See [PORTABLE_SETUP.md](PORTABLE_SETUP.md) for commands. Wool, silk and leather material generation is documented in [ANIMAL_TEXTILES.md](ANIMAL_TEXTILES.md). The `--optimize` flag now also uses conservative postprocessing for biome, product, CE-bionic and life-stage XML `<Patch>` outputs (full `<Defs>` are not rewritten).
