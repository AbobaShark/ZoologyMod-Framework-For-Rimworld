# Zoology: Realistic Animal Overhaul

<img src="https://i.ibb.co/ZpLXjc8Z/Preview.png" alt="Zoology preview">

Zoology overhauls animals in **RimWorld 1.6 and 1.7**, combining zoological species data, generated XML definitions, and C# behavior systems. Its goal is biologically plausible ecology, development, predation, reproduction, and animal husbandry within RimWorld's gameplay.

**Animal barns and some other features require RimWorld 1.7.**

## Features

### Animal biology and growth

- Species-specific body mass, BodySize, health, movement, hunger, lifespan, and ecological properties.
- Species-specific daily growth in body size and characteristics when **Dynamic animal growth** is enabled.
- **Sexual dimorphism**: male and female differences in adult body size, characteristics and appearance, developing during maturation.
- **Automatic young-animal sprite scaling** based on juvenile and adult body mass.
- Data-derived natural attacks, armor, Combat Power, cooldowns, and Combat Extended values, including stage- and sex-dependent differences.
- Revised anatomy, meat, leather, wool, milk, eggs, and biome assignments for supported animals.

### Ecology and survival

- Prey selection using current size, strength, age, protective adults, and species-specific feeding restrictions.
- Pack hunting, stalking, land and water ambushes, herd warning, and pursuit limits.
- Food selection across live prey, carrion, fishing, plant harvesting and shelter raids; wild animal foraging with Odyssey.
- Constriction, animal venoms, venom samples and craftable antivenoms.
- Flight from credible threats, including water escape, climbing into trees, and entering burrows; concealed or sufficiently slow predators do not automatically provoke every shelter response.
- Predators defending kills; scavengers consuming suitable carrion; specialist animals raiding nests, excavating burrows and digging up graves.
- Wild mating, ecosystem-capacity controls, **per-species wild population limits**, and family-aware departures from overcrowded maps.
- Ectotherm behavior, optional **water-habitat seeking**, wound licking (including colony animals assigned No medical care), and separately configurable **winter dormancy**.
- Inland water habitat influences natural animal spawning, favoring suitable aquatic and wetland species.

### Reproduction and offspring care

- Mammalian lactation, nursing, milk-dependent young, and handler feeding.
- Childcare, parent/offspring association, cooperative defense, and clutch guarding.
- Bird **tree and ground nests** for eggs and chicks; tree nests appear in the canopy. Flight develops with age in supported birds.
- **Shared animal burrows and maternity dens** for hiding, nursing young and defending offspring from intruders.
- **Marsupial pouches** for dependent offspring.
- Amphibian **external fertilization** (Odyssey) of aquatic roe; each clutch develops into a swimming tadpole group, which later releases young frogs. Roe and tadpole groups are not consumed by their own species.

### Husbandry and colony interaction

- The **animal barn** (RimWorld 1.7) houses animals and stores feed, with a configurable total BodySize capacity (default **32.5**) and species, sex, age, lactation, and dependent-young filters.
- Barn and pen handling, nutrition allocation, nursing-mother balancing, breeding, egg production, and auto-slaughter for stored animals.
- Pet recreation, bonding, animal training and direct-control interoperability.
- NPC animal companions in compatible human pawn groups, plus protections for small noncombatant pets.
- Optional animal bionics, species-specific physiological extensions, and compatibility fixes.

## Behavior settings

The following behaviors can be configured in the mod settings:

- **Advanced animal food selection:** nutritional value, accessibility and effort influence feeding choices.
- **Wild animal foraging (Odyssey):** suitable hungry wild animals gather local food.
- **Advanced threat avoidance:** tree, burrow and terrain refuges during predator encounters.
- **Advanced reproductive behavior:** bird nests, marsupial pouches, amphibian roe/tadpole development and related offspring shelter care.
- **Water habitat behavior:** idle movement toward appropriate water and dry-land exits.
- **Winter dormancy:** species-specific seasonal torpor and winter denning.

**Family dens require both advanced reproductive behavior and advanced threat avoidance.**

## Requirements and compatibility

- RimWorld **1.6 or 1.7**;
- [Harmony](https://github.com/pardeike/HarmonyRimWorld).

Zoology includes conditional patches or runtime integration for **Biotech**, **Odyssey**, **Royalty**, **Combat Extended**, **Vanilla Expanded Framework**, **Vanilla Animals Expanded** (including Royal, Endangered, and Waste Animals), **Alpha Animals**, **Alpha Biomes**, **Dinosauria**, and **Megafauna**. Optional content is loaded only when the corresponding mod or DLC is present.

With Combat Extended, Zoology uses specialized animal attack, armor and penetration data.

## Installation

Install Zoology after Harmony in the RimWorld mod list.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3679396881)

## Animal data and generation

The main animal-data workbook is [AnimalStats — WORKING](https://docs.google.com/spreadsheets/d/1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4/edit). Biomechanical source calculations are maintained in [Calculations — WORKING](https://docs.google.com/spreadsheets/d/1QWEZNRR6mV_5luNib6dS5oy8SzoF4g1XRcIQHOqbdqg/edit).

Checker validates these sources and exports XML and generated C# to registered destinations. Numerical source data and calculations belong in their workbooks; changes to generated output files alone may be overwritten during regeneration.

## Documentation

- [PLAYER_GUIDE.md](PLAYER_GUIDE.md) — gameplay mechanics, player controls, defaults, and husbandry.
- [FRAMEWORK.md](FRAMEWORK.md) — modder-facing extensions, integration, and runtime architecture.
- [CHECKER.md](CHECKER.md) — source-data ownership, Checker setup, generation, routing, and validation.

## Contributing

Contributions should target the actual source of a behavior or statistic: the source workbook, generator, XML definition, or C# implementation as appropriate. Validate generated output and affected game behavior; text-matching source tests do not substitute for running RimWorld.

## License

MIT License.
