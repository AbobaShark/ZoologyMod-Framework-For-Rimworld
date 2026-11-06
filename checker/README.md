# Zoology Patch Generator — RimWorld 1.7

The Checker builds RimWorld XML patches from AnimalStats **without using any previously generated patch as input**. `generation.json` is the Git-versioned source of group membership, abstract Def names, module ownership and exact XML destinations. Generated XML is first published to ignored `output/staged/`, verified against the registry and checksums, then automatically installed into `Zoology/` by default. Disable automatic installation using `--stage-only` (CLI) or the GUI checkbox.

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

`OriginalXML/` and `OriginalXML/CEPatches/` are local, gitignored upstream snapshots populated by Reference Sync. No source XML snapshots are distributed inside Git. They are never overwritten by the generator; for explicitly reviewed refreshes from local installations or pinned GitHub commits, see [docs/ROUTING_REFERENCES_AND_OPTIMIZATION.md](docs/ROUTING_REFERENCES_AND_OPTIMIZATION.md). CE output is controlled by an explicit checkbox/flag. The standalone Def generator is independent.

## Output destinations and routing

All registered destinations, including race, biome, reproduction, bionics and life-stage outputs, come from `generation.json`. No filename guessing and no migration based on preexisting generated XML. The route validator checks each destination against `Zoology/LoadFolders.xml`; CE output is restricted to the CE integration directory. The registry currently covers 81 animal patch groups (407 unique DefNames) and 29 biome patches and 2 reference-only biome markers. The `outputs[]` section adds 15 production targets (including Core and Odyssey hatchers), vanilla/CE bionics, and vanilla/CE life stages and life-stage combat power; Python no longer enumerates their destinations. Run `python rimworld_patch_generator.py --check` to check all registered destination declarations and their 19 required static source files on the local installation.

The `reference_only_biomes` list in `generation.json` declares names such as `Alpine` and `MammothSteppe` that occur in AnimalStats `WildBiomes` but have no actual `BiomeDef`. They remain available as spreadsheet markers, but Checker skips their biome XML outputs. Any other unregistered biome still fails generation. If an older staged manifest contains those former outputs, clear the ignored `checker/output/staged/` directory before regenerating.

**Staging precedes deployment.** `output/staged/generation-manifest.json` includes SHA-256 of every produced XML. Deployment validates staged checksums and destinations, then replaces XML at selected registered paths regardless of prior ownership or manual changes. Preview shows copy/replace/skip actions without writing. Partial staging preserves other categories and does **not** automatically delete previously deployed files. The output directory and local settings are ignored in Git.

Specialized production files and the base bionic XML may retain structural fields from installed XML. Life-stage XML, including Combat Power and CE penetration, is regenerated deterministically from all required columns of the `LifeStages` sheet. Missing or nonnumeric life-stage factors stop generation; CE-only fields are written exclusively to the CE-gated load folder. The new CE bionic patch is generated from the CE-specific Bionics columns (`CE Damage`, `CE Blunt AP`, `CE Sharp AP`, `CE Attack interval`) and staged at `ModPatches/CombatExtended/Patches/HediffDefs/BionicAnimalVariants_CE.xml`; it is not loaded without Combat Extended. It needs in-game CE 1.7 runtime verification before release.

## Safety and compatibility

Regen inputs must contain the referenced animals; unknown groups, ambiguous or missing destinations, missing CE body shapes and invalid numeric cells fail visibly. Generation writes to staging first; automatic installation (enabled by default) then validates checksums and destinations before replacing mod XML. Manual deployment still asks for confirmation. `Zoology/LoadFolders.xml` and existing repo contents are not changed by distributing this archive.

Run `python -m pytest tests` from `checker/` for isolated unit tests. Integration and runtime tests using the full `Zoology/` tree must be run after installation. See [PORTABLE_SETUP.md](PORTABLE_SETUP.md) for commands. Wool, silk and leather material generation is documented in [ANIMAL_TEXTILES.md](ANIMAL_TEXTILES.md). The `--optimize` flag uses conservative postprocessing for biome, product, CE-bionic, leather and textile XML `<Patch>` outputs; complete life-stage factors are never stripped by the optimizer (full `<Defs>` are not rewritten).

## Material ownership and launchers

Every leather stat in AnimalStats is generated from upstream ThingDefs and CE reference XML. There are **no handwritten leather-stat or fallback patch templates**. The only four Zoology-owned material ThingDefs are `Leather_Beaver`, `Leather_Ostrich`, `Leather_Otter`, and `Leather_Squirrel`, updated directly in `Zoology/Core/Defs/ThingDefs_Items/Items_Resource_Stuff_Leather_Zoology.xml`. Their different Combat Extended armor is handled by a *generated* CE override. Animal leather assignments are guarded by material availability. If an Odyssey leather is required by an animal from a different mod, Checker generates a guarded fallback using Core LeatherBase in the consumer mod's own leather patch, but only when Odyssey is absent.

The three Windows launchers are `START_REFERENCE_SYNC.bat`, `START_PATCH_GENERATOR.bat`, and `START_DEF_GENERATOR.bat`. The Python scripts remain directly callable for CLI usage. The reference updater discovers only registry-relevant upstream race, animal PawnKind, biome, material, egg/hatcher, life-stage and CE patch XML directly from game/mod sources, including from an empty `OriginalXML`; it does not import every ThingDef or human PawnKind. It also reports registered animals and biomes missing from the discovered source snapshots; source availability and warning details must be checked before generation.

## Food capacity and baby stages

For every life stage in AnimalStats, `foodMaxFactor` is 1: `Need_Food.MaxLevel` already multiplies the pawn's actual `BodySize` by this factor. `BodySize` includes `bodySizeFactor`. A second age-dependent correction would change the amount of nutrition stored by a same-sized animal. `hungerRateFactor` remains an independent rate parameter.

The 15 species requiring `AnimalBabyTiny` are declared once in `generation.json` under `baby_tiny_defs`, validated against animal group ownership, and patched after the generator builds `race/lifeStageAges`. The obsolete standalone baby XML and duplicate Overgrown Colossus meat patch were removed. CE output is guarded by `LoadFolders.xml`; `--no-ce` leaves installed CE-gated XML untouched but excludes it from that run's deployment. Regenerate with CE enabled when you intend to use CE.

## Custom life-stage sources

The `LifeStages` sheet now has a strict `Source mod` column, analogous to
the leather source column. `Core` contains the shared vanilla and eusocial
insect stages. `Vanilla Animals Expanded — Royal Animals` contains five
independent Megachicken stages (`VAERoy_AnimalBaby`, `VAERoy_AnimalJuvenile`,
`VAERoy_AnimalAdult`, `VAERoy_AnimalAdultIntermediate`,
`VAERoy_AnimalAdultMax`), with body-size factors 0.2, 4, 13, 15 and 20.
The five age breakpoints (0, 3, 8, 12 and 18 years) are owned by Royal
Animals and must not be replaced with the ordinary three-stage animal model.

Checker validates every source/stage row and generates three additional
files: Royal base life-stage factors and combat power in the Royal-specific
conditionally loaded folder, and Royal CE factors/penetration in the CE
folder. The CE patch also tests for the actual Royal `LifeStageDef` before
adding penetration definitions, so CE without Royal Animals is safe. The
LoadFolders order intentionally puts CE **after** Royal Animals: CE damage
must not be overwritten by the base Royal stage patch.

An upstream Alpha Animals integration for Biomes! Islands defines three
additional Fangsquid stages; that animal is not in Zoology's registered
animal groups and is not yet a supported LifeStages output.

### BodySize and life-stage speed

RimWorld's `baseBodySize` is **not a mass multiplier**. The `Animals!AO`
mapping assigns a rounded game BodySize from body mass in kilograms using
one smooth monotonic function, normalized to 60 kg -> 1 and 4500 kg -> 5.
The MROUND step depends on the unrounded BodySize (0.05 up to 1, 0.1 up to
2, 0.25 up to 5, and 0.5 above 5). `SERVICE_BodySizeMassBands` lists the
inverse mass intervals of the rounded mapping. Each attainable size has
one contiguous mass interval; its representative mass is the midpoint.
When an age stage has a BodySize that no adult could obtain from the
rounded mapping (e.g. 0.06 or 3.9), a midpoint is interpolated between the
closest attainable game sizes.

For Royal Megachicken, `LifeStages.BaseBodySize` reads the actual game
`Animals!AO` value (0.35 for the current 5 kg Megachicken), `StageBodySize=BaseBodySize *
bodySizeFactor`, and `BodySizeMassMid_kg` uses the inverse map.
`BaseMassMid_kg` is the inverse mass midpoint of the unscaled 0.35 BodySize,
not an arbitrary 70 kg reference. The dimensionless age-speed multiplier is
`v(BodySizeMassMid_kg)/v(BaseMassMid_kg)` with Hirt et al. (2017),
doi:10.1038/s41559-017-0241-4,
`v(M)=25.5 M^0.26 (1-exp(-22 M^-0.6))` (M in kg).
This is an empirical maximum running-speed curve applied as a proxy for
game movement, **not** an experimentally validated juvenile walking curve.
Checker independently checks all mapped masses, game BodySizes and speeds.

At the current game sizes 0.06, 1.2, 3.9, 4.5, and 6.0, the representative
masses are about 0.103, 98, 1,955, 3,357 and 7,532 kg, respectively.
The corresponding stage speeds are 0.370, 1.658, 0.995, 0.855 and 0.670.
The underlying AnimalStats!Animals!AO formula has a discontinuity near
2000 kg; bins may overlap or be disjoint. This is exposed explicitly in
the service tab instead of hiding the ambiguity.

Core LifeStageDefs are shared by very different animal masses, locomotion
types and body sizes. Their `SpeedFactor` retains a dimensionless relative
game-size approximation `bodySizeFactor^0.17` (rounded to 0.05);
**no fixed human/70 kg reference mass** is introduced. A single static
LifeStageDef cannot express the exact species-specific Hirt curve.

### Market value, melee interval and CE penetration

Vanilla `PriceUtility.PawnQualityPriceFactor` applies
`LifeStageDef.marketValueFactor` separately from health and capacity
modifiers; age does **not** automatically multiply the price by BodySize.
Core age prices use the same dimensionless approximate relative combat-
potential factor as Zoology's combat-power extension. Royal prices are
reconstructed from the actual Megachicken `Animals!H` pricing equation:
the source's combat-power and game-BodySize terms vary by stage; fixed
value contributions are preserved. These are modeled price ratios, not
guarantees of actual market value with injuries or trade modifiers.

`MeleeCooldownFactor` is **not** a vanilla/CE pawn StatDef.
Zoology declares `Zoology_MeleeCooldownFactor` and applies its
life-stage stat factor to `VerbProperties.AdjustedCooldown(Tool,Pawn,Thing)`
through one postfix on attack cooldown calculation, for vanilla and CE.
Unlike the old unrecognized XML stat, this changes the real interval.
Disabling all Zoology runtime patches disables that postfix.

For Combat Extended, Zoology's armor penetration hook multiplies the
natural tool AP by `MeleeDamageFactor^0.75` **and** the extra age AP
factor. The sheet therefore writes extra factors as
`bodySizeFactor^(1/3) / meleeDamageFactor(CE)^0.75` (sharp) and
`bodySizeFactor^(2/3) / meleeDamageFactor(CE)^0.75` (blunt);
the resulting total ratios are exactly the specified relative game-size
geometric proxies without double-counting damage. They are explicit
geometric assumptions, not empirically fitted penetration laws.
CE crit/parry **stat** factors follow the shared relative-BodySize
proxy `MROUND(bodySizeFactor^0.165, 0.05)`, complementing the reciprocal
`MeleeDodgeChance` size heuristic. No adult mass or 70 kg reference is
introduced. This is a dimensionless game-balance approximation, not an
empirically established universal biological law. CE still computes final
crit/parry probabilities from comparative stats, skill and other modifiers;
when a species has zero base Crit/Parry stat, multiplication does not
create a nonzero chance. Dodge remains a
size-relative heuristic rather than a direct mass-derived estimate.
