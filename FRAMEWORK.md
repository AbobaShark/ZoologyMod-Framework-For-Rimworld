# Zoology Framework Reference

This document describes the current public XML-facing surface of Zoology, the integration rules implemented for RimWorld 1.6 and 1.7 and the data pipeline that produces the animal patches.

It is a reference for mod authors and maintainers. Player-facing settings and gameplay behavior are documented in PLAYER_GUIDE.md. The Python generation toolchain is documented in CHECKER.md.

## Runtime architecture

Zoology has two distinct layers.

The static layer contains versioned Defs and patches under `Zoology/1.6` or `Zoology/1.7`, shared Core/Common content, conditional DLC folders, and conditional third-party `ModPatches` folders. This layer supplies race values, bodies, life stages, biome data, items/products, melee tools, jobs, think-tree insertions, sounds and other XML data.

The runtime layer is the ZoologyMod assembly. Harmony patches and GameComponents implement behavior that cannot be represented reliably by static XML alone: predation decisions, pack hunting, fleeing, corpse defense, childcare, lactation, wild reproduction, pet systems, NPC companions, bionic expansion and runtime species overrides.

The supported modder-facing surface consists primarily of the XML Def, DefModExtension, CompProperties and Tool types documented below. Internal runtime utility classes are implementation details.

## Load structure

The current LoadFolders.xml loads:

- the mod root;
- `Zoology/1.6` or `Zoology/1.7`, according to the game version;
- `Zoology/Core` and `Zoology/Common`;
- `DLC/Biotech` when Biotech is active;
- `DLC/Royalty` when Royalty is active;
- DLC/Odyssey when Ludeon.Rimworld.Odyssey is active;
- Combat Extended patches;
- Vanilla Expanded Framework patches;
- Vanilla Animals Expanded patches;
- Vanilla Animals Expanded - Royal Animals patches;
- Vanilla Animals Expanded - Endangered patches;
- Vanilla Animals Expanded - Waste Animals patches;
- Alpha Animals patches;
- Dinosauria patches;
- Megafauna patches;
- Alpha Biomes patches.

`Zoology/DLC/Royalty` is included conditionally in both `v1.6` and `v1.7` load folder lists.

The mod metadata supports RimWorld 1.6 and 1.7 and requires Harmony. It also declares load ordering after the supported game/DLC and animal/combat frameworks.

## ThingDef marker extensions

Zoology marker extensions are attached through the normal ThingDef modExtensions list.

Minimal marker form:

    <modExtensions>
      <li Class="ZoologyMod.ModExtension_IsMammal, ZoologyMod"/>
    </modExtensions>

### ModExtension_IsMammal

Class:

    ZoologyMod.ModExtension_IsMammal

Fields: none.

Meaning: marks a race or compatible def as a mammal for Zoology lactation and mammalian-young feeding logic.

The runtime mammal test checks the pawn race def and pawn kind def through Zoology's extension cache.

The global mammal-lactation setting gates the behavior. The settings UI can also add or remove the marker per animal at runtime.

### ModExtensiom_Chlidcare

Class:

    ZoologyMod.ModExtensiom_Chlidcare

Fields: none.

Meaning: enables Zoology childcare behavior for the marked animal: young following, protection behavior and the egg-clutch systems that depend on childcare.

The misspellings in both Extensiom and Chlidcare are part of the actual class name and therefore part of the XML contract.

### ModExtension_AgroAtSlaughter

Class:

    ZoologyMod.ModExtension_AgroAtSlaughter

Fields:

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| verboseLogging | bool | false | Emits additional logging for slaughter designation, slaughter-response and ritual-role paths. |
| excludeFromRituals | bool | true | Excludes marked animals from supported peaceful ritual animal roles. |

Example:

    <modExtensions>
      <li Class="ZoologyMod.ModExtension_AgroAtSlaughter, ZoologyMod">
        <excludeFromRituals>true</excludeFromRituals>
      </li>
    </modExtensions>

Standing marked animals can react to a slaughter attempt. Downed animals bypass the aggression path.

### ModExtension_IsScavenger

Class:

    ZoologyMod.ModExtension_IsScavenger

Fields:

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| allowVeryRotten | bool | false | Allows very rotten/desiccated remains in the scavenging path. |

Example:

    <modExtensions>
      <li Class="ZoologyMod.ModExtension_IsScavenger, ZoologyMod">
        <allowVeryRotten>true</allowVeryRotten>
      </li>
    </modExtensions>

The global scavenging setting gates the runtime behavior. The per-species runtime editor can override marker presence and allowVeryRotten.

### ModExtension_CannotChew

Class:

    ZoologyMod.ModExtension_CannotChew

Fields: none.

Meaning: enables swallow-whole restrictions for the marked animal.

The extension does not define its own prey-size field. Under advanced predation, Zoology interprets the race's `maxPreyBodySize` as the mean-adult reference and scales the effective limit by `pawn.BodySize / pawn.RaceProps.baseBodySize`. This incorporates sex, life stage, daily growth and individual size modifiers without new per-species constants. Unlimited race limits remain unlimited. For a non-adult animal marked CannotChew, the scaled limit is additionally capped by its current BodySize.

This marker affects corpse eating, scavenging and predation acceptability through the shared CannotChewUtility.

### ModExtension_NoFlee

Class:

    ZoologyMod.ModExtension_NoFlee

Fields:

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| verboseLogging | bool | false | Emits developer logging for supported blocked flee/mental-state paths. |

The runtime code can force ShouldAnimalFleeDanger to false and block supported PanicFlee/Terror starts for marked pawns while the feature is enabled.

### ModExtension_Ectothermic

Class:

    ZoologyMod.ModExtension_Ectothermic

Fields: none.

Meaning: opts the animal into Zoology's ectothermic cold-handling path.

### ModExtension_CannotBeMutated

Class:

    ZoologyMod.ModExtension_CannotBeMutated

Fields: none.

Meaning: excludes the marked animal from supported mutation mechanics while the protection feature is active.

### ModExtension_CannotBeAugmented

Class:

    ZoologyMod.ModExtension_CannotBeAugmented

Fields: none.

Meaning: excludes the marked animal from supported augmentation mechanics. Zoology's own animal-bionic patchers respect this marker when building the set of augmentable animal defs.

### ModExtension_NoPorcupineQuill

Class:

    ZoologyMod.ModExtension_NoPorcupineQuill

Fields: none.

Meaning: prevents the supported porcupine-quill hediff path from affecting the marked animal.

### ModExtension_FleeFromCarrier

Class:

    ZoologyMod.ModExtension_FleeFromCarrier

Source file name:

    ModExtension_Scary.cs

The source-file name is not the XML class name.

Fields:

| Field | Type | Default | Meaning |
| --- | --- | --- | --- |
| fleeRadius | float | 12 | Maximum radius in which the carrier can act as the local threat. |
| fleeBodySizeLimit | float | 0 | Optional prey BodySize ceiling; values <= 0 disable this ceiling. |
| fleeDistance | nullable int | 16 | Requested flee distance. |

Example:

    <modExtensions>
      <li Class="ZoologyMod.ModExtension_FleeFromCarrier, ZoologyMod">
        <fleeRadius>12</fleeRadius>
        <fleeBodySizeLimit>1.5</fleeBodySizeLimit>
        <fleeDistance>16</fleeDistance>
      </li>
    </modExtensions>

A carrier is considered only when it is a valid spawned pawn on the same map, is in range, is factionally relevant to the prey and passes line-of-sight/reach and body-size checks.

The settings editor supports ranges 1-60 for radius, 0-20 for body-size limit and 1-80 for flee distance.

## ThingDef comps

The public XML type is the CompProperties class. The ThingComp class listed here is the runtime implementation.

The repository does not currently contain a committed race XML that demonstrates all four comp classes directly, so the examples below use standard RimWorld ThingDef comps syntax and only fields that are public in the corresponding CompProperties classes.

### CompProperties_Ageless

XML class:

    ZoologyMod.CompProperties_Ageless

Runtime comp:

    ZoologyMod.CompAgeless

Fields:

| Field | Type | Default | Runtime rule |
| --- | --- | --- | --- |
| cleanupIntervalTicks | int | 6000 | Clamped to at least 60 on spawn. |

Example:

    <comps>
      <li Class="ZoologyMod.CompProperties_Ageless, ZoologyMod">
        <cleanupIntervalTicks>6000</cleanupIntervalTicks>
      </li>
    </comps>

The comp periodically removes age-related hediffs identified by Zoology. Harmony guards also block supported forbidden age hediffs from being added in the first place.

### CompProperties_DrugsImmune

XML class:

    ZoologyMod.CompProperties_DrugsImmune

Runtime comp:

    ZoologyMod.CompDrugsImmune

Fields:

| Field | Type | Default | Runtime rule |
| --- | --- | --- | --- |
| cleanupIntervalTicks | int | 2000 | Clamped to at least 60 on spawn. |

The implementation identifies hediffs associated with drug ingestion, addiction/tolerance relationships and a small set of optional external drug-like hediff names. Supported additions are blocked and residual matching hediffs are periodically removed.

### CompProperties_AnimalClotting

XML class:

    ZoologyMod.CompProperties_AnimalClotting

Runtime comp:

    ZoologyMod.Comp_AnimalClotting

Fields:

| Field | Type | Default | Runtime rule |
| --- | --- | --- | --- |
| checkInterval | int | 360 | Clamped to at least 60. |
| tendingQuality | FloatRange | 0.2-0.7 | A random quality in the configured range is used for periodic tending. |

Example:

    <comps>
      <li Class="ZoologyMod.CompProperties_AnimalClotting, ZoologyMod">
        <checkInterval>360</checkInterval>
        <tendingQuality>
          <min>0.2</min>
          <max>0.7</max>
        </tendingQuality>
      </li>
    </comps>

The comp periodically tends bleeding hediffs on the pawn.

The runtime species editor can override presence, checkInterval and the minimum/maximum tending quality for this comp.

### CompProperties_AnimalRegeneration

XML class:

    ZoologyMod.CompProperties_AnimalRegeneration

Runtime comp:

    ZoologyMod.Comp_AnimalRegeneration

Fields:

| Field | Type | Default |
| --- | --- | --- |
| hediffBaby | HediffDef | null |
| hediffJuvenile | HediffDef | null |
| hediffAdult | HediffDef | null |
| babyFraction | float | 0.2 |
| juvenileFraction | float | 0.5 |
| adultFraction | float | 1.0 |
| checkIntervalTicks | int | 720 |

Fractions must all be positive and strictly ordered baby < juvenile < adult. Invalid values are replaced by the default 0.2/0.5/1.0 set.

The comp caches thresholds from the race's baseBodySize multiplied by those fractions, then chooses the corresponding hediff from the pawn's current BodySize. checkIntervalTicks is clamped to at least 60.

The current settings UI has a global regeneration toggle but no per-species regeneration selector.

## Mammal infant feeding integration

The mammal marker is also the entry point for Zoology's newborn feeding systems. A third-party animal does not need a separate "handler feeding" marker: if the animal is recognized as a mammal, is in a baby life stage and otherwise passes the food rules, the lactation/feeding systems can use it.

### Nursing jobs

The shipped lactation layer contains separate jobs and think-tree nodes for both sides of nursing:

- `Zoology_Breastfeed` / `JobDriver_AnimalBreastfeed` for a mobile lactating female going to a hungry baby;
- `Zoology_YoungSuckle` / `JobDriver_YoungSuckle` for a baby going to a compatible mother;
- `JobGiver_MotherRespondToSuckleRequests`;
- `JobGiver_YoungSuckleFromMother`;
- `JobGiver_AnimalAutoFeed`.

`AnimalLactationUtility` is internal runtime infrastructure for infant-stage recognition, mother eligibility, cross-breed compatibility, food thresholds, mother search and nursing job construction.

The current behavior requests nursing below 33% baby food level. A mother must retain at least 15% of her own food level to nurse.

### Handler feeding

Current builds also ship:

    JobDef: Zoology_FeedMammalBaby
    driverClass: ZoologyMod.JobDriver_FeedMammalBaby

and:

    WorkGiverDef: Zoology_FeedMammalBaby
    giverClass: ZoologyMod.WorkGiver_FeedMammalBaby
    workType: Handling
    priorityInType: 50
    directOrderable: true
    canBeDoneByMechs: false
    required capacity: Manipulation

The WorkGiver scans player-faction animals and only offers work for hungry mammal babies that pass `MammalBabyCache.ShouldUseBabyFoodRules`.

Food selection first checks the handler's inventory and then the map. The job deliberately excludes drugs, corpses, dispensers and opportunistic plant harvesting from this feeding search. Final suitability still goes through the baby's own race diet and Zoology's baby-food rules.

`JobDriver_FeedMammalBaby` derives from RimWorld's patient-feeding driver but removes the medical-rest requirement: animal babies can be fed where they are. The same WorkGiver is direct-orderable, so the player can force the feeding job.

The feature is gated by both mammal lactation and the `EnableHandlerBabyFeeding` setting. The WorkGiver and its running JobDriver check this setting directly, so switching handler feeding takes effect without rebuilding unrelated Harmony patches. Disabling mammal lactation suspends handler feeding without clearing its saved setting; mother-to-young nursing remains independent of the handler-feeding toggle.

### Baby food suitability

While lactation is active, the baby-food patch requires a food to satisfy both:

- `IngestibleProperties.babiesCanIngest`;
- the animal race's ordinary `CanEverEat` rule.

The mammal-baby path also prevents the normal fishing job from being used as a newborn feeding solution.

### Caravan feeding

The caravan patch runs the same biological model without map jobs. It can ensure the biological mother is lactating when both mother and baby are present, choose a compatible lactating feeder, transfer nutrition from that feeder and then fall back to suitable caravan inventory food.

### Auto-slaughter

When `AllowSlaughterLactating` is false, Zoology adjusts auto-slaughter counts and rejects lactating animals in the auto-slaughter WorkGiver. This is specifically automatic herd management; it is distinct from a player's explicit slaughter designation and from `ModExtension_AgroAtSlaughter`.

## Gender-restricted melee tools

Class:

    ZoologyMod.ToolWithGender

Field:

| Field | Type | Default |
| --- | --- | --- |
| restrictedGender | RimWorld Gender | None |

Example:

    <li Class="ZoologyMod.ToolWithGender, ZoologyMod">
      <label>horn</label>
      <capacities>
        <li>Poke</li>
      </capacities>
      <power>13.5</power>
      <cooldownTime>2.25</cooldownTime>
      <restrictedGender>Male</restrictedGender>
    </li>

The Harmony patch checks Zoology's own ToolWithGender directly. It also recognizes another Tool subtype that exposes a public restrictedGender field of type Gender.

This feature is controlled by the Gender-restricted attacks Dev setting.

## Life-stage combat power

Class:

    ZoologyMod.LifeStageCombatPowerExtension

Field:

| Field | Type | Default |
| --- | --- | --- |
| combatPowerFactor | float | 1 |

The shipped Core patch attaches:

- 0.2 to AnimalBaby, AnimalBabyTiny and EusocialInsectLarva;
- 0.5 to AnimalJuvenile and EusocialInsectJuvenile.

Example:

    <modExtensions>
      <li Class="ZoologyMod.LifeStageCombatPowerExtension, ZoologyMod">
        <combatPowerFactor>0.5</combatPowerFactor>
      </li>
    </modExtensions>

AnimalCombatPowerUtility uses the explicit extension when present. If it is absent, the utility falls back to Zoology's infant/juvenile recognition and then to factor 1 for other stages.

Combat Power and Combat Extended melee-damage factors are different quantities. The runtime fallback is 0.2 for babies and 0.5 for juveniles, while per-species LifeStageProfiles and CE-specific generated data provide the supported species' precise stage and tool values. Do not interpret the fallback as a universal CE penetration or natural-attack multiplier.

This adjusted combat power is used by behavior systems such as targeted flee and predator/prey comparisons. It is not a replacement for the PawnKindDef combatPower field stored in XML.

### Combat Extended animal life-stage penetration

Combat Extended's natural-attack armor penetration uses the per-tool, per-species
factors generated from the AnimalStats LifeStageProfiles catalog. During
dynamic growth the factors are interpolated daily between adjacent life stages.
The original CE getter retains its penetration, skill and equipment modifiers;
Zoology multiplies the final natural-attack result by the appropriate stage
ratio once. The stat display shows the corresponding values.

The generic `LifeStagePenetrationDef` lookup and its optional CE penetration
override have been removed. There is no second life-stage penetration model.
The **Dynamic animal growth** setting is enabled by default and governs
runtime body-size, health, hunger, graphical scale, natural attacks, CE
penetration and durability, and other supported age-related stat modifiers.
Disabling it leaves the authored LifeStageDefs in place, while restoring
stepwise changes at the life-stage boundaries.

CEChecker detects CE by its runtime types and package ID, while CEReflectionUtility isolates the reflection-based access to CE internals. This avoids a hard compile-time dependency on CE.

## NPC pawn-group marker

Class:

    ZoologyMod.ZoologyPawnGenOption

Base class:

    RimWorld.PawnGenOption

The class has no additional fields.

Its purpose is narrow: XML options added by Zoology can be identified as Zoology-owned additions so the Animals in NPC groups setting can remove those options without removing vanilla or third-party PawnGenOptions.

Example from the shipped patch pattern:

    <Husky Class="ZoologyMod.ZoologyPawnGenOption, ZoologyMod">0.8</Husky>

The generic companion-safety system does not require this marker. It evaluates animals selected in supported standard mixed human groups regardless of whether the animal option was added by Zoology.

`ZoologyMod.ZoologyPawnGenOption_WildBoarReplacement` is a second, specialized XML option type derived from `ZoologyPawnGenOption`. The Core NPC-group patch uses it to replace specific `WildBoar` options with `Wolf_Timber` for Yttakin pirates, preserving their selection weights. Unlike ordinary Zoology-added options, these replacements remain in the group when Animals in NPC groups is disabled: the runtime registry restores their `WildBoar` kind when that PawnKindDef is available. No additional XML fields are defined by this subclass.

## Beastmastery and direct-control interoperability

Zoology recognizes these trainable names in its runtime compatibility layer:

- `Zoology_Beastmastery` — current Zoology trainable;
- `Zoology_DraftControl` — shipped legacy compatibility trainable;
- `VEF_Beastmastery` when that compatible Def exists.

The shipped `Zoology_Beastmastery` and `Zoology_DraftControl` TrainableDefs are gated by Odyssey.

For an eligible animal, Zoology synchronizes the linked AttackTarget/draft-control training maps so compatible trainables share one progress state.

The runtime detects supported external drafting ownership. When another supported system owns drafting for a pawn/race, Zoology disables its own draft-access path for that pawn/race.

This is interoperability, not an invitation to call AnimalDraftControlUtility directly: the implementation class is internal.

## Animal bionics

The Enable human bionics on animals feature is implemented by runtime Def patchers, not by a generic public registration API.

At initialization, Zoology:

- finds animal ThingDefs that pass CanBeAugmented;
- checks the body parts required by supported vanilla install recipes;
- extends existing recipeUsers where the same body-part def can be used;
- clones install/remove recipes when an animal uses an alternate body-part def;
- creates or resolves size-dependent combat hediff variants where the combat patcher requires them.

The hard-coded simple mapping includes supported eye, ear, spine, heart, stomach, brain, kidney, lung, coagulator/vacskin and related vanilla implants. Combat and special mappings are handled by separate patchers.

Body-size categories used by BionicPatcherUtils are:

- VerySmall: BodySize < 0.2;
- Small: < 0.5;
- Medium: < 1;
- Large: < 2;
- VeryLarge: < 3.5;
- Huge: >= 3.5.

ModExtension_CannotBeAugmented is the supported XML opt-out.

Because these patchers mutate DefDatabase content during initialization, the user-facing bionics toggle is restart-sensitive.

## Runtime species overrides

ZoologyRuntimeAnimalOverrides is an internal implementation service used by the settings UI. It is not a public C# API.

Its current feature catalog can add/remove these entries on animal ThingDefs at runtime:

- ModExtensiom_Chlidcare;
- ModExtension_Ectothermic;
- ModExtension_IsMammal;
- ModExtension_IsScavenger;
- ModExtension_AgroAtSlaughter;
- ModExtension_CannotBeMutated;
- ModExtension_CannotBeAugmented;
- ModExtension_NoFlee;
- ModExtension_FleeFromCarrier;
- ModExtension_NoPorcupineQuill;
- ModExtension_CannotChew;
- CompProperties_Ageless;
- CompProperties_DrugsImmune;
- CompProperties_AnimalClotting.

The runtime editor records differences from the loaded Def baseline. It does not require mod authors to generate separate XML for a player's local override.

Supported editable parameters are:

- scavenger allowVeryRotten;
- FleeFromCarrier fleeRadius, fleeBodySizeLimit and fleeDistance;
- ageless cleanupIntervalTicks;
- drugs-immune cleanupIntervalTicks;
- clotting checkInterval and tending-quality min/max.

## Growth, reproduction and shelter contracts

### Species-specific life stages and continuous growth

`LifeStageProfiles` provides the concrete young/special-stage endpoints for each supported race; the species-specific installer retains the original `LifeStageAge` metadata, merges defined stats and attacks and leaves unrelated stages unchanged. `ZoologyAnimalGrowth` reads a cached biological-day snapshot, interpolating health, hunger, body size, natural attacks, armor and drawn size when the corresponding setting is enabled.

`SexAdultProfiles_Generated.xml` supplies male and female **adult** endpoints. `ZoologySexAdultProfiles` selects the endpoint for the pawn's gender; if dynamic growth is on, the same juvenile-to-adult interval approaches that endpoint. Adults remain on the final normal adult life stage. `EnableSexualDimorphism` and `EnableDynamicAnimalGrowth` are separate runtime settings. The optional `EnableAutomaticJuvenileSpriteScaling` uses stage mass to replace inconsistent young sprite sizes. An existing distinct female texture is retained; art scaling does not require duplicating a texture.

With Combat Extended active, supported natural attacks use generated CE life-stage and adult-sex attack and armor values.

### Animal offspring shelter markers

`ModExtension_OffspringShelter` is a race-specific data-driven marker. The supported values in the generator include `POUCH`, `DEN`, `GROUND_NEST_ALTRICIAL`, `TREE_NEST_ALTRICIAL`, `GROUND_NEST_PRECOCIAL`, `TREE_NEST_PRECOCIAL`, and `AQUATIC_CLUTCH`. Source ownership is in AnimalStats, not hardcoded DefName switches in Checker.

- `CompAnimalStrategyState` tracks applicable strategy decisions and planned searches.
- `Building_AnimalBurrow` and `AnimalNursery` implement shared shelters, family occupancy and natural bird nests.
- `CompAnimalNestEggContainer` retains fertilized eggs and young as genuine held Things; rendering and interaction follow the visual canopy where a tree nest is placed.
- `AnimalOffspringPouch` handles dependent marsupial offspring.
- `CompAmphibianRoe`, `CompTadpole` and `JobDriver_FertilizeAmphibianRoe` model externally fertilized aquatic clutches and larval cohorts using one Thing per group with cohort mass and nutrition. Stage-transition counts are derived from actual clutch size and species-specific embryo and larval survival fractions in `EggMilkWool`. The shipped aquatic definitions and egg-layer patches are Odyssey-gated.

Nests and dens are natural animal structures. `AnimalTreeSelection` connects the selected tree to its current perched animals and anchored nests through vanilla-style selection gizmos; occupants are resolved through the refuge map component. The tree claim enforces a shared BodySize budget.

### Runtime behavior gates and feature combinations

`ModConstants.AdvancedReproduction`, `AdvancedThreatAvoidance`, `WaterHabitatBehavior` and `WinterDormancy` provide constant-time gates sourced from the corresponding persisted `ZoologyModSettings` flags. Each gate also checks `DisableAllRuntimePatches`. They are checked at ThinkTree job-giver entry, before any target or path search, and at the existing periodic ThingComp processing intervals.

`AnimalOffspringShelter.For` returns `NONE` for specialized reproductive shelter modes when advanced reproduction is disabled. **DEN is additionally disabled unless advanced threat avoidance is enabled.** `AnimalPouchBootstrap` uses the ungated configured mode only to install the component once, preserving safe live toggles. Winter denning does not depend on threat avoidance and is controlled exclusively by its own seasonal gate. Water seeking and dry-ground recovery are separate from choosing emergency water refuges.

When switches are disabled during an existing game, previously contained offspring must not be discarded: nests eject their real eggs and chicks, pouches release babies to the map or caravan, and family burrows release non-dormant residents. Already spawned amphibian clutches/tadpole cohorts retain their saved progress while the advanced reproduction switch is off. No map-wide periodic scan is added by these settings.

The developer master switch unpatches known Harmony IDs and checks remaining Harmony patch methods belonging to this assembly **once at disable time**. However, disabling Harmony patches alone cannot remove injected ThinkTree nodes, CompProperties or already loaded XML definitions. All new behavior entrypoints must therefore also honor the runtime gates. Mammalian nursing interacts with mother and dependent-young holders. The physical shelter, visible sprite and click target must remain consistent.

### Winter dormancy and refuges

`ModExtension_WinterDormancy.mode` accepts `NONE`, `HIBERNATION`, `WINTER_REST`, `DENNING` and `MATERNITY_DENNING`. Runtime entry requires suitable wild animals during winter with food level at least 80%; maternity denning requires pregnancy or dependent young. Basal hunger factors are 0.04 for hibernation, 0.25 for denning/maternity denning and 1 for winter rest. The system can reuse compatible burrows and makes decisions in idle job scheduling. Ectotherm idle behavior and habitat selection are implemented separately. Refuge decision-making uses actual visible and overt threat information, movement speed and shelter reachability.

### Wild population, predation and husbandry

The `WildAnimalEcosystemUtility` and per-species limit handle wild-only population capacity; the default optional species limit is **30** (10–100). WildAnimalOverpopulationExitMapComponent prioritizes species over their count limit and then species with excess ecosystem-weight contribution relative to the expected biome composition, subject to family/dependent-young constraints. Predation uses current pawn characteristics, herd awareness and chase state. Escape into tree crowns, water and burrows is threat-dependent rather than a universal response to all nearby hostile races.

The 1.7 `Building_AnimalBarn` is a different system from natural burrows and nests. It holds pawns and real food stacks, filters intake by animal/sex/age and nursing requirements and has a configurable total base-BodySize budget (10–100, default **32.5**). It is gated by `EnableAnimalBarn`. The barn stores up to 18 food stacks. `AnimalBarnReproduction` uses species mating intervals and vanilla mate/birth handling; the barn routes produced eggs via `AnimalBarnEggUtility` to adjacent egg boxes, held contents or food storage as appropriate. Aquatic clutch producers retain mature roe until released to lay in water. `HideBarnAnimalsFromAnimalTab` controls the livestock display.

## Hunting strategies, feeding and venom extensions

### Predation and refuge extensions

These `ThingDef` mod extensions are exported from species data and consumed by the strategy, refuge and natural-combat systems.

| Extension | Fields | Semantics |
| --- | --- | --- |
| `ModExtension_PredationStrategy` | `strategy`, optional `lifeStageIndices` | `No`, `Chasing`, `WaterAmbush`, `Ambush`; restrict to the listed native life-stage indices when provided. |
| `ModExtension_Burrower` | `maxBodySize` (2) | Burrow/refuge use up to the configured current BodySize; enables digging for suitable prey. |
| `ModExtension_TreeClimber` | `maxBodySize` (2), `minTreeGrowth` (0.65) | Tree refuge eligibility by animal size and tree growth. |
| `ModExtension_Constrictor` | `maxPreySizeRatio` (1), `severityPerSecond` (0.025), `ectothermFactor` (0.25) | Constriction after eligible natural attacks, maximum prey-to-snake BodySize ratio and severity progression. |
| `ModExtension_FlightDevelopment` | Marker | Blocks flight in the infant stage; a species-specific generated flight rule takes precedence. |

`AnimalHuntStrategy` and `AnimalLocomotion` resolve pursuit, concealment and terrain-dependent speed. `AnimalBurrowHunt` selects eligible held prey, nest contents and edible grave corpses; grave excavation requires a suitable digging animal of current BodySize at least 1.

### Food selection

`EnableAnimalFoodChoice` gates the unified food-source and food-job patches. With advanced predation enabled, predators compare live prey, available food, fishing and shelter raids. Food-source ranking prefers suitable raw food and accessible carrion; harvesting is available to animals with both Meat and VegetableOrFruit diet flags. Plant work speed and yield use vanilla Plants-5 stat curves and expected edible crop yield.

`EnableWildAnimalForage` requires advanced food selection and Odyssey. It gates `JobDriver_WildAnimalForage` for factionless animals whose race has the Forage trainable and can eat the biome's foraged food. The job produces 2–5 units in 240 ticks; its score uses expected nutrition relative to food capacity, work and distance. Fishing is scored separately using FishingSpeed. Food acquisition uses existing food decisions and bounded searches. Both settings are persisted, enabled by default and honor the runtime master gate; disabling the food feature or wild foraging interrupts affected custom jobs safely.

### Venom and constriction

`Hediff_AnimalVenom` applies venom from the corresponding natural-attack DamageDef, with dose computed using penetrating damage and the configured victim scaling. Applicable body capacities and flesh status determine susceptibility. `Hediff_AnimalConstriction` tracks a constricting predator and accumulating severity. Delayed predation attribution connects deaths caused by these effects to the original hunter.

`ModExtension_VenomSample.sample` specifies the ThingDef obtained when an eligible reproductive-stage animal is butchered. The `IngestionOutcomeDoer_AnimalAntivenom.venom` field identifies the venom HediffDef removed by that drug. The `Venom profiles` table maps tool capacities to venom effects, sample items and matching antivenom recipes; generated sample, drug and recipe Defs reside in Core.

## Static XML surface shipped by Zoology

The main 1.6 Def layer contains:

- animal body definitions and BodyPartGroups;
- animal-specific ToolCapacity definitions;
- animal bionic hediff variants;
- lactation hediffs;
- lactation/nursing and handler baby-feeding JobDefs;
- the Zoology_FeedMammalBaby Handling WorkGiverDef;
- childcare, pet-play and predation JobDefs;
- pet-play JoyGiverDefs;
- animal sound defs;
- think-tree insertions for feeding, tending, incubation, companion AI, family following, corpse defense and wild mating;
- Zoology trainables.

Core patches cover:

- biome defs;
- body defs;
- life stages;
- animal race defs;
- animal resource items;
- graphics;
- NPC animal group options;
- caravan properties;
- egg-layer and hatcher comps;
- milkable comps;
- cross-breeding;
- meat;
- pregnancy.

Runtime safeguards include:

- Biotech insect-cocoon spawn-budget protection;
- cleanup of invalid stored job-giver think-tree keys during save loading.

### Validation scope

Automated tests under `checker/tests` exercise source data, generated XML and deployment rules. Build the C# assembly for the target game version and validate CE combat, animation, selection, AI jobs and pathfinding in RimWorld.

## Conditional DLC patches

### Biotech

The loaded Biotech folder contains:

- `Leather_Biotech.xml`;
- `Meat_Biotech.xml`;
- `ToxicDefs_Biotech.xml`;
- insect-cocoon building patches;
- rodent race patches;
- ruminant race patches.

`ToxicDefs_Biotech.xml` conditionally excludes `ToxicMeat` from the relevant pemmican and meal ingredient filters when Vanilla Animals Expanded - Waste Animals is not active.

The runtime cocoon safeguard is also Biotech-gated.

### Odyssey

The loaded Odyssey folder contains:

- Glacial Plain, Glowforest, Grasslands, Lava Field and Scarlands biome patches, including a separate manual Grasslands patch;
- Odyssey body definitions;
- Odyssey baby life-stage patches;
- animal graphic data;
- VEF-backed body-clock assignments when VEF is present;
- egg-layer and hatcher patches;
- meat patches;
- grouped Odyssey animal race patches.

`Zoology_Beastmastery` is also Odyssey-gated.

## Conditional third-party patch sets

The active `LoadFolders.xml` conditionally loads the integration folders below.

### Combat Extended

The CE folder contains:

- CE variants for Zoology animal body definitions;
- a CE added-parts HediffDef patch;
- CE life-stage melee-damage and penetration data.

The assembly adds runtime CE penetration calculation/UI hooks without a compile-time CE reference.

### Vanilla Expanded Framework

The VEF folder contains three behavior-level integrations:

- `Foraging_VEF.xml` assigns VEF `CompProperties_AutoNutrition` to selected insectivores and replaces the relevant pangolin ant-feeding comp when the Endangered module is present;
- `ModExtension_BodyClock_VEF.xml` assigns crepuscular/nocturnal body clocks to selected animals through VEF's `ExtendedRaceProperties`;
- `ModExtension_CrossBreeding_VEF.xml` supplies VEF cross-breed extension data for supported Core cross-breed pairs.

The current foraging assignments include fennec fox and, when their source content is active, pangolin, hedgehog, armadillo, crow and bullfrog.

### Vanilla Animals Expanded

The main VAE folder contains more than generated race values. Its current non-race surface includes:

- biome patches;
- VAE body definitions and an Odyssey-fallback pinniped body;
- VAE damage defs;
- baby life-stage patches;
- animal graphics;
- cat and dog `crossAggroWith` relationship patches;
- egg-layer/hatcher patches;
- milkable and shearable patches;
- VEF body-clock assignments;
- VEF/race cross-breeding data;
- leather/item patches including Odyssey fallback material data.

### Vanilla Animals Expanded - Endangered

The Endangered folder contains:

- egg-layer patches;
- hatcher patches;
- Odyssey-fallback leather/item data;
- `Foraging_VAEE.xml`.

The foraging patch removes the Rockhopper Penguin `DigWhenHungry` comp and removes the module's raw-fish base/def in the supported integration configuration. The VEF foraging patch supplies the corresponding auto-nutrition behavior for species Zoology maps to insect/ant feeding.

### Vanilla Animals Expanded - Royal Animals

The Royal Animals integration contains generated race patches plus egg-layer and hatcher corrections.

### Vanilla Animals Expanded - Waste Animals

The Waste Animals integration contains generated race patches plus egg-layer, hatcher and damage corrections.

### Alpha Animals

Alpha Animals receives generated race patches and fallback body definitions for crab-, larva- and snail-like body plans when those Odyssey body definitions are unavailable.

### Alpha Biomes

Alpha Biomes has a dedicated biome-distribution patch set for its supported custom biomes. It does not depend on generated race XML alone.

### Dinosauria

Dinosauria receives generated race patches plus dedicated animal graphic and shadow corrections.

### Megafauna

Megafauna currently uses its generated race patch set.

## Working data pipeline

The production animal data are split between two Google Sheets.

### AnimalStats — WORKING

https://docs.google.com/spreadsheets/d/1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4/edit

Current sheets:

| Sheet | Role |
| --- | --- |
| Groups | Group-level biological/game defaults and inherited values used by species rows. |
| Base stats | Main species data and calculated baseline fields. |
| Species overrides | Explicit per-species exceptions layered over group/base logic. |
| Animals | Final vanilla-facing production table consumed by the checker. |
| Animals CE | Final Combat Extended production table consumed alongside Animals. |
| Biomes | Per-animal biome distribution/output data. |
| Leathers | Leather market, armor and insulation data. |
| EggMilkWool | Independently keyed egg, milk, wool and amphibian reproduction output tables. |
| Animal textiles | Wool and silk material stats and source-mod ownership. |
| Venom profiles | Natural-attack capacity mappings, venom effects, sample producers and antivenom definitions. |
| LifeStageProfiles | Species-specific young/special life-stage stat, attack and graphic endpoints. |
| SexDimorphism | Species-level adult female/male mass input, preserving mean baseline mass. |
| SexAdultProfiles | Final male/female adult stat, natural-attack and graphics endpoints. |
| Meat | Meat yield and food-consumption/profitability calculations. |
| LifeStages | Shared life-stage factors for body size, health, hunger, melee and CE values. |
| Bionics | Size-based bionic combat values used by the animal bionic definitions. |
| SERVICE_BiomechCatalog | Catalog/lookup layer for biomechanical models available to AnimalStats. |
| SERVICE_CalculationCache | Imported production results from Calculations. |
| SERVICE_TechnicalQA | Workbook integrity and production QA. |
| SERVICE_ModelPolicy | Model-selection/policy data used by production formulas. |
| CE DPS calibration | CE damage/cooldown calibration workspace and checks. |

The public checker reads final calculated values. It does not evaluate Google Sheets formulas itself.

### Calculations — WORKING

https://docs.google.com/spreadsheets/d/1QWEZNRR6mV_5luNib6dS5oy8SzoF4g1XRcIQHOqbdqg/edit

Current sheets:

| Sheet | Role |
| --- | --- |
| Bite force | Bite-force source blocks and fitted/fixed production models. |
| Teeth | Tooth/mandible/appendage geometry source blocks and models. |
| Limb force | Limb/appendage force source blocks and models. |
| Ramming force | Ramming-force/energy calculation inputs. |
| SERVICE_ProductionExport | Normalized model output exported to AnimalStats. |
| SERVICE_TechnicalQA | Source-block/model/export integrity checks. |
| SERVICE_ModelRegistry | Registry of production model keys, mode and provenance. |

SERVICE_ModelRegistry distinguishes FIT models from fixed/literature-transfer models and records their production keys. SERVICE_TechnicalQA checks that registered eligible models and exported models remain synchronized and that active source/export formulas are valid.

### Workbook relationship

The production flow is:

    Calculations source blocks
        -> Calculations / SERVICE_ProductionExport
        -> AnimalStats / SERVICE_CalculationCache
        -> Groups + Base stats + Species overrides
        -> Animals + Animals CE + LifeStageProfiles + SexAdultProfiles
        -> Biomes + products + reproduction
        -> checker
        -> generated RimWorld XML and C# profiles

AnimalStats imports the production export from Calculations with IMPORTRANGE. Calculations is therefore an upstream model workbook, while AnimalStats is the species/game-production workbook.

## Model/data maintenance rules

Generated race patches are downstream artifacts. Biological values, model changes and group/species rules are maintained in the relevant workbook source/model/override and then regenerated into XML.

Use Species overrides only for genuine species-level exceptions that should supersede the generic group/base calculation.

The service sheets are part of the production pipeline. They should not be treated as disposable scratch sheets: the checker-visible Animals/Animals CE outputs depend on the calculations they feed.

For the actual generation commands, Google authorization, OriginalXML simulation and patch optimization, see CHECKER.md.

### Life-stage source ownership

Checker reads source-mod ownership and the stage identity from `LifeStages`
and `LifeStageProfiles`. Ordinary Core/eusocial stages use the compatible
species-specific endpoints; Vanilla Animals Expanded — Royal Animals retains
its five native Megachicken stages. The Royal source retains its own native
Megachicken age thresholds and graphics. Zoology regenerates only the
stage-stat factors and stage combat-power extension, with CE-specific values
loaded in the CE folder after Royal Animals.

### Life-stage allometry and engine application

The game `BodySize` is not physical mass. `AnimalStats!Animals!AO`
converts physical mass to a rounded, smooth monotonic `baseBodySize`
(60 kg -> 1, 4500 kg -> 5); for Royal
Megachicken, the five growth-stage body sizes are inverted to corresponding
body-mass range midpoints using `SERVICE_BodySizeMassBands`. Hirt's
terrestrial maximum-running-speed curve is applied to the *mapped* masses;
bodySizeFactor is never substituted for a physical mass multiplier.

Shared Core LifeStageDefs provide stage identities; `LifeStageProfiles` and
`ZoologyLifeStageProfileInstaller` install the corresponding species-specific
factors while preserving the native age-stage topology. These stage endpoints
are derived from the relevant species masses, not a universal reference adult. Vanilla `PriceUtility.PawnQualityPriceFactor`
handles marketValueFactor as its own life-stage multiplier, separate from
health and capacities. Engine cooldown does not read the nonexistent pawn
`MeleeCooldownFactor` stat: Zoology declares `Zoology_MeleeCooldownFactor`
and applies it to real natural melee-tool cooldowns via a Harmony postfix.
CE armor penetration already includes the stage melee-damage multiplier
raised to 0.75 and age-specific AP factors compensate it explicitly.

## Water-dependent natural wild spawns

The shared `Zoology_WaterBodySpawnRequirements` Def in
`Zoology/Core/Defs/Misc/WaterBodySpawnRequirements.xml` lists
water-dependent noncoastal wildlife, with optional missing mods ignored.
The requirements use final, XML-patched in-game `race.baseBodySize`
and `ModExtension_Ectothermic`, not the AnimalStats workbook.

The existing `WildAnimalSpawner.CommonalityOfAnimalNow` Postfix adjusts
weights of natural map-generation and subsequent wildlife spawns.
Explicit PawnKinds supplied by quests, incidents or developer commands
are unchanged. Only species with positive `coastalWildAnimals`
commonality in the current biome are exempt from these changes.
Species listed in `pollutionWildAnimals` follow normal inland-water
weighting, so rats and other ordinary animals are not exempted
merely for tolerating polluted environments.

A single four-connected flood fill measures all non-ocean water
components, including `Marsh`, and caches their sizes `A_i`, total
water area `A`, largest component `L`, and eligible map area
`N = map cells - ocean cells`. Wetness `D = (A + L)/(2N)` gives
`T = clamp((D - 0.01)/0.11, 0, 1)`. Ordinary non-water-dependent
animals have their selection weight multiplied by `1/(1+2*T)`,
ranging from 1 to 1/3.

For each water-dependent animal, `B = race.baseBodySize` and
`R = 150*B*B*(ectothermic ? 1 : 2)`. The area contribution of each
water component is `A_i*min(1, A_i/R)`; summed over all components
this yields `H`. If `L < R/3`, the species is excluded from natural
spawns on that map. Otherwise its spawn-weight multiplier is
`(1+2*T)*min(1, H/R)`, ranging from 0 to 3.

A `ConditionalWeakTable<Map, MapWaterCache>` keeps map scores
and per-race factors for 15,000 ticks. Scan buffers are reused on
refresh, and the hot path uses only biome pool checks, an O(1)
dictionary lookup and a multiplication. No new Harmony hook, job,
or periodic tick-based scanner is introduced. Biome eligibility, temperature checks and VEF river-list compatibility also apply.
