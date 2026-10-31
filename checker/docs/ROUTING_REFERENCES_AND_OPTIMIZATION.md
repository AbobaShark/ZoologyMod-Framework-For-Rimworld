# Declarative routes, references and optimization (RimWorld 1.7)

`generation.json` is the authority for the current 147 output destinations (81 animal groups, 29 biome patches, 3 shared Def files, 34 additional outputs). Three reference-only biome markers do not create XML. Every output records its destination, category, dependency and source mode; `core/xml_routing.py` validates those against `Zoology/LoadFolders.xml`.

From `checker/`:

```powershell
py rimworld_patch_generator.py --check
py rimworld_patch_generator.py --all
py rimworld_patch_generator.py --preview
```

The generator stages XML and checks SHA-256 checksums before explicit deployment; it does not deploy on registry edits or automatically delete unselected outputs.

## References

Use `START_REFERENCE_SYNC.bat` / `py reference_sync_gui.py` to preview upstream XML changes and explicitly apply them. `reference_sources.auto.json` supplies the current reference groups, source repositories and paths. CE patch XML belongs under `OriginalXML/CEPatches/`; all `OriginalXML/` is gitignored.

GitHub sources use the latest default branch unless a branch or exact commit is explicitly chosen. Local game installations and Workshop mods can be used as sources. The GUI displays COPY, REPLACE, SKIP, MISSING and REVIEW; reviewed replacements are backed up under ignored `output/reference-backups/`. Reference Sync does not modify the Zoology mod or run automatically with generation.

User preferences are stored in `checker.local.json` and cannot override versioned reference directory locations. Reference Sync imports recognized upstream Def XML and relevant CE patches without requiring pre-existing files. It reports registered animals or biomes that cannot be resolved to a source. No obsolete-file migration or startup cleanup remains. The current `OriginalXML/` snapshots and staged files are not deleted automatically.

## Optimization

The optional optimizer compares generated Patch operations with the available original Defs and CE patches, discarding only demonstrably redundant operations. It also compacts compatible operations in generated biome, production, CE-bionic and life-stage patches. It does not alter complete `Defs` outputs. Missing or ambiguous reference data is not proof that a runtime operation is unnecessary. In-game validation remains necessary for complex mod combinations.
