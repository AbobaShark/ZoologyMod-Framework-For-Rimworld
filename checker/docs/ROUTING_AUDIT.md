# Routing audit: group registry

`generation.json` was reconstructed from the previous checked-in managed manifest and its 81 generated animal source XML files. Each group includes unique animal DefNames, previously targeted abstract parents, `mod` identity and the exact existing destination under `Zoology/`. The previously verified biome destinations (31) and shared venom Def destinations (3) are recorded separately.

The new loader checks ownership (`Core`, `DLC:<name>`, or `ModPatches/<name>`), duplicate DefNames or paths, `../` path traversal, CE-only categories, and both RimWorld 1.6 and 1.7 `LoadFolders.xml` route conditions. The generator does not choose a path from a spreadsheet MayRequire value: that field is not a reliable source of a Def's original owning mod and can refer to a different dependency. The registry is authoritative; new entries must have an explicitly reviewed destination.

All additional 19 outputs, including 15 reproductive XML, vanilla and CE bionics and vanilla/CE life stages, are declared in `generation.json` under `outputs[]` with source type and load requirement. No destination lists remain in Python. A new generated CE bionic patch is assigned to the CE integration directory; it must be runtime-tested before release. No `Zoology/` files were edited to produce this Checker-only archive.

`output/staged/` contains runtime data only and is never a source of truth for the next generation. `OriginalXML/` and `OriginalPatches/` remain pinned reference snapshots until a separately reviewed update.

The optional reference source updater is described in [ROUTING_REFERENCES_AND_OPTIMIZATION.md](ROUTING_REFERENCES_AND_OPTIMIZATION.md). It never runs implicitly during generation.
