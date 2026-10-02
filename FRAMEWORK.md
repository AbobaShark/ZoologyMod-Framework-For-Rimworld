# Zoology Framework Reference

This document describes the current public XML-facing surface of Zoology, the integration rules implemented by the RimWorld 1.6 codebase and the data pipeline that produces the animal patches.

It is a reference for mod authors and maintainers. Player-facing settings and gameplay behavior are documented in PLAYER_GUIDE.md. The Python generation toolchain is documented in CHECKER.md.

## 1. Runtime architecture

Zoology has two distinct layers.

The static layer consists of Defs and patches under Zoology/1.6, conditional DLC folders and conditional third-party ModPatches folders. This layer supplies race values, bodies, life stages, biome data, items/products, melee tools, jobs, think-tree insertions, sounds and other XML data.

The runtime layer is the ZoologyMod assembly. Harmony patches and GameComponents implement behavior that cannot be represented reliably by static XML alone: predation decisions, pack hunting, fleeing, corpse defense, childcare, lactation, wild reproduction, pet systems, NPC companions, bionic expansion and runtime species overrides.

Do not treat every internal utility class as a supported external API. The stable modder-facing surface is primarily the XML Def/extension/comp types documented below.

## 2. Load structure

The current LoadFolders.xml loads:

- the mod root;
- Zoology/1.6;
- Zoology/Common;
- DLC/Biotech when Ludeon.Rimworld.Biotech is active;
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

A Zoology/DLC/Royalty directory exists in the repository, but the current LoadFolders.xml does not reference it. Do not assume an XML folder is active merely because it exists in the tree.

The mod metadata declares RimWorld 1.6 and Harmony as the hard requirements. It also declares load ordering after the supported game/DLC and animal/combat frameworks.

## 3. ThingDef marker extensions

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
| verboseLogging | bool | false | Emits additional developer logging when DevMode is active. |
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

The extension does not define its own prey-size field. Zoology uses the race's maxPreyBodySize, and for non-adult growth stages additionally caps the effective limit by current BodySize.

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

## 4. ThingDef comps

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

## 5. Gender-restricted melee tools

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

## 6. Life-stage combat power

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

This adjusted combat power is used by behavior systems such as targeted flee and predator/prey comparisons. It is not a replacement for the PawnKindDef combatPower field stored in XML.

## 7. NPC pawn-group marker

Class:

    ZoologyMod.ZoologyPawnGenOption

Base class:

    RimWorld.PawnGenOption

The class has no additional fields.

Its purpose is narrow: XML options added by Zoology can be identified as Zoology-owned additions so the Animals in NPC groups setting can remove those options without removing vanilla or third-party PawnGenOptions.

Example from the shipped patch pattern:

    <Husky Class="ZoologyMod.ZoologyPawnGenOption, ZoologyMod">0.8</Husky>

The generic companion-safety system does not require this marker. It evaluates animals selected in supported standard mixed human groups regardless of whether the animal option was added by Zoology.

## 8. Beastmastery and direct-control interoperability

Zoology defines these trainable names in its runtime compatibility layer:

- Zoology_Beastmastery;
- Zoology_DraftControl as the legacy Zoology name still recognized internally;
- VEF_Beastmastery when that compatible def exists.

The shipped Zoology_Beastmastery TrainableDef is gated by Odyssey.

For an eligible animal, Zoology synchronizes the linked AttackTarget/draft-control training maps so the compatible trainables do not represent independent progress tracks.

The runtime also detects supported external drafting ownership. If another supported system owns drafting for a pawn/race, Zoology's draft-access test returns false rather than creating two command systems.

This is interoperability, not an invitation to call AnimalDraftControlUtility directly: the implementation class is internal.

## 9. Animal bionics

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

## 10. Runtime species overrides

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

Regeneration is not in this runtime feature catalog.

## 11. Static XML surface shipped by Zoology

The main 1.6 Def layer contains:

- animal body definitions and BodyPartGroups;
- animal-specific ToolCapacity definitions;
- animal bionic hediff variants;
- lactation hediffs;
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

The code additionally performs several runtime corrections that are not separate public extension types:

- replacement of guinea-pig leather references with squirrel leather;
- Biotech insect-cocoon spawn-budget protection;
- cleanup of invalid stored job-giver think-tree keys during save loading.

These are implementation safeguards or global corrections, not framework markers for third-party XML.

## 12. Conditional DLC patches

### Biotech

The loaded Biotech folder contains patches for:

- leather/meat/toxic-related data;
- insect cocoons;
- selected rodent and ruminant race defs.

The runtime cocoon safeguard is also Biotech-gated.

### Odyssey

The loaded Odyssey folder contains patches for:

- biome defs;
- bodies;
- life stages;
- graphics;
- body-clock behavior;
- egg-layer/hatcher data;
- meat;
- Odyssey animal race defs.

Zoology_Beastmastery is also Odyssey-gated.

## 13. Conditional third-party patch sets

The active LoadFolders configuration contains dedicated folders for:

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

These folders are loaded only when their package IDs are active.

Combat Extended receives a separate body/combat integration and runtime CE hooks. Alpha Biomes is primarily biome-distribution integration. The animal-content mods receive race/body/product/biome corrections appropriate to their defs.

## 14. Working data pipeline

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
| EggMilk | Egg incubation/laying, milk and wool production reference/output data. |
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
        -> Animals + Animals CE + Biomes/products
        -> checker
        -> generated RimWorld XML

AnimalStats imports the production export from Calculations with IMPORTRANGE. Calculations is therefore an upstream model workbook, while AnimalStats is the species/game-production workbook.

## 15. Model/data maintenance rules

Generated race patches are downstream artifacts. If a biological value, model or group rule is wrong, fix the relevant source/model/override and regenerate rather than hand-editing a generated output that the checker will overwrite later.

Use Species overrides only for genuine species-level exceptions that should supersede the generic group/base calculation.

The service sheets are part of the production pipeline. They should not be treated as disposable scratch sheets: the checker-visible Animals/Animals CE outputs depend on the calculations they feed.

For the actual generation commands, Google authorization, OriginalXML simulation and patch optimization, see CHECKER.md.
