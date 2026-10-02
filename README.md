# Zoology: Realistic Animal Overhaul

<img src="https://i.ibb.co/ZpLXjc8Z/Preview.png">

Zoology is a data-driven animal overhaul for RimWorld 1.6. It replaces broad vanilla animal assumptions with species-specific statistics, ecology and behavior derived from zoological data, then maps those inputs into RimWorld-compatible gameplay.

The project combines generated XML patches with runtime systems. The generated layer handles animal statistics, life stages, biome placement, products, anatomy and combat definitions; the runtime layer handles behavior that cannot be expressed reliably through static Def patches alone.

## What Zoology changes

Zoology covers the animal system as a whole rather than only melee damage. Depending on the species and installed content, it can change:

- body size, health scale, movement, hunger and carrying-related values;
- growth, life stages, gestation, litter or clutch behavior and wild reproduction;
- biome distribution, ecosystem weight and other ecological placement;
- combat power, melee tools, attack cooldowns and life-stage combat scaling;
- vanilla and Combat Extended melee balance from biomechanical inputs;
- meat, leather, eggs, milk and wool-related production;
- anatomy, body definitions, graphics and selected sound assignments;
- predation, pack hunting, prey fleeing, scavenging and corpse defense;
- mammal lactation, newborn nursing and handler feeding of hungry mammal babies;
- childcare, egg-clutch protection and incubation;
- pet recreation, expanded bonding and direct animal control;
- NPC animal companions in standard mixed human groups;
- ectothermy, wound licking and several species-specific physiological rules;
- per-species runtime feature assignment, human-directed fleeing and roamer/trainability settings.

The target is biological plausibility inside RimWorld's mechanics, not literal simulation. Game-facing quantities such as Combat Power remain separate from the underlying biological inputs so encounter generation and pawn-group budgets remain usable.

## Requirements

- RimWorld 1.6
- Harmony

Official DLC-dependent behavior is enabled only when the relevant content is present. The current load configuration contains dedicated conditional patch folders for Biotech and Odyssey; other DLC-dependent runtime branches are gated by the APIs they use.

## Installation

Install Zoology like any other RimWorld mod and load it after Harmony. The mod metadata also declares ordering after RimWorld and the supported animal/combat frameworks it patches.

Steam Workshop:

https://steamcommunity.com/sharedfiles/filedetails/?id=3679396881

## Major gameplay systems

The main configurable systems are:

- advanced predation and life-stage-aware threat evaluation;
- coordinated pack hunting;
- prey fleeing from active and non-hostile predators;
- custom size-aware flee behavior and configurable fleeing from humans;
- predator ownership and defense of kills;
- scavenging, including optional access to very rotten remains;
- swallow-whole restrictions for animals that cannot chew;
- wild mating, ecosystem-capacity limits and overpopulation departure;
- mammal lactation and nursing, including handler-fed mammal babies when nursing is unavailable;
- childcare, family following, egg incubation and clutch defense;
- pet recreation and expanded bonding;
- Beastmastery-based direct control of eligible trained animals;
- handler-bound NPC animal companions;
- small-pet protection from inappropriate raid targeting;
- human bionics on compatible animals;
- slaughter aggression;
- wound licking;
- non-CE size-aware damage reduction;
- ectothermic cold handling;
- configurable roamers and trainability.

The complete settings reference, defaults, ranges, dependencies and restart-sensitive behavior are documented in [PLAYER_GUIDE.md](PLAYER_GUIDE.md).

## Data-driven combat and statistics

Animal values are produced from two working Google Sheets:

- AnimalStats — WORKING  
  https://docs.google.com/spreadsheets/d/1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4/edit
- Calculations — WORKING  
  https://docs.google.com/spreadsheets/d/1QWEZNRR6mV_5luNib6dS5oy8SzoF4g1XRcIQHOqbdqg/edit

Calculations contains the biomechanical source models used for bite force, tooth or appendage geometry, limb force and ramming calculations. Its production export is imported into AnimalStats, where those outputs are combined with species data, group defaults and explicit species overrides. AnimalStats then produces the final vanilla and Combat Extended tables consumed by the checker.

The workbook structure and public modder-facing XML surface are documented in [FRAMEWORK.md](FRAMEWORK.md). The generation and validation toolchain is documented in [CHECKER.md](CHECKER.md).

## Compatibility

Dedicated conditional patch folders or integration are present for:

- Combat Extended;
- Vanilla Expanded Framework;
- Vanilla Animals Expanded;
- Vanilla Animals Expanded - Royal Animals;
- Vanilla Animals Expanded - Endangered;
- Vanilla Animals Expanded - Waste Animals;
- Alpha Animals;
- Alpha Biomes;
- Dinosauria;
- Megafauna.

Additional runtime interoperability exists where Zoology can detect another drafting implementation or an optional health-system hook without taking ownership of that mod's behavior.

Zoology is declared incompatible with Animals Are Fun Continued because both mods implement overlapping animal-interaction and recreation behavior.

When Combat Extended is active, Zoology uses CE-specific animal combat data and disables its non-CE animal damage-reduction system. The optional CE penetration override affects Zoology's life-stage penetration handling rather than replacing CE as a whole.

## Repository documentation

- [PLAYER_GUIDE.md](PLAYER_GUIDE.md) — gameplay behavior and every user-facing setting.
- [FRAMEWORK.md](FRAMEWORK.md) — XML extensions, comps, interoperability and the spreadsheet/data model.
- [CHECKER.md](CHECKER.md) — data-source loading, XML generation, patch fixing, reference simulation, optimization, compaction and tests.

## Contributing

Changes to generated animal statistics should be made at the source-data or model layer whenever possible. Direct edits to generated race patches can be overwritten by later regeneration.

Useful contributions include better biological sources, corrections to species data, compatibility patches, additional animal datasets, code fixes, performance improvements and reproducible bug reports.

## License

MIT License
