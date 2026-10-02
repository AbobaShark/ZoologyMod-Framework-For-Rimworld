# Zoology Player Guide

This guide describes the behavior exposed to players by the current RimWorld 1.6 build of Zoology. It covers the generated animal overhaul, runtime systems, all five settings pages, per-species editors, Combat Extended behavior and the cases where a restart matters.

## 1. What is always part of the overhaul

Zoology is not only a collection of optional AI features. A large part of the mod is static data generated into XML patches.

Depending on the animal, the generated and reviewed patch layer can change body size, health scale, movement, hunger, carrying-related values, wildness, trainability, growth, life stages, gestation, litter or clutch values, biome placement, ecosystem weight, combat power, melee tools, meat and leather output and other race-specific values.

The project also includes static or generated corrections for:

- animal body and body-part definitions used by supported species;
- biome animal placement;
- life-stage definitions and life-stage combat factors;
- egg laying and hatching values;
- milk and wool production;
- pregnancy hunger scaling;
- meat and leather definitions;
- selected cross-breeding relationships;
- selected graphics and sound assignments;
- caravan-related animal properties;
- supported DLC and third-party animal definitions.

These values remain active independently of most runtime behavior toggles.

## 2. Combat and life-stage scaling

Vanilla and Combat Extended use separate final combat outputs.

For ordinary RimWorld combat, Zoology supplies species-specific melee tools and cooldowns together with game-facing Combat Power. Babies and juveniles are not treated as full adults by Zoology's threat comparisons: animal infant combat power uses a factor of 0.2 and juvenile combat power uses 0.5 unless a LifeStageDef provides an explicit Zoology combat-power extension.

The same life-stage distinction is also used by systems that compare animals for predation, fleeing and protection behavior.

With Combat Extended installed, Zoology loads CE-specific animal body/combat patches and uses the CE columns produced by AnimalStats. The non-CE animal damage-reduction feature is disabled while CE is present.

## 3. Predator / prey settings

### Enable prey fleeing

Default: On.

Potential prey can actively flee from nearby predators. The system distinguishes a predator that is already targeting the animal from nearby predators that are not currently hostile.

When enabled:

- Predator search radius: default 18, range 6-24.
- Flee distance from target predator: default 24, range 6-40.
- Animals flee from non-hostile predators: default On.
- Non-hostile predator search radius: default 12, range 6-24.
- Flee distance from predator: default 16, range 6-40.

Pursuit state is tracked separately so predators can abandon unrealistic chases instead of following a fleeing target indefinitely.

### Enable pack hunting

Default: On.

Eligible predators can cooperate when a prey animal is too dangerous for one hunter. Pack participation is evaluated as a group behavior rather than requiring every member to independently select the same prey at the same moment.

### Enable advanced predation logic

Default: On.

Predator prey selection adds Zoology checks on top of ordinary food eligibility. The runtime logic considers body size, life stage, species/kinship relationships and relative combat power.

When both sides are animals, Zoology uses a dominance factor of 1.3 for targeted threat comparison. This prevents animals of similar effective combat power from being treated as ordinary prey by default.

### Enable scavenging

Default: On.

Species carrying the Zoology scavenger extension can eat rotten corpses. The per-species editor can add or remove scavenger status and can set the extension's allowVeryRotten parameter.

allowVeryRotten defaults to Off. When enabled for a species, very rotten or desiccated remains can also be accepted by the scavenging logic. Skeletonized remains provide reduced nutrition rather than fresh-corpse nutrition.

### Enable predators defending corpses

Default: On.

Predators can retain ownership of a kill and remain near it rather than treating every corpse as immediately unowned food.

Related controls:

- Prey protection range: default 20, range 10-30.
- Unowned corpse size multiplier: default 5, range 2-10.
- Allow predators to defend prey from humans and mechanoids: default On.
- Minimum combat power to defend prey: default 70, range 0-1000.

A predator that qualifies to defend a kill can suppress ordinary fleeing while that defense is active. Very small competitors can be treated as insufficient threats so large predators do not repeatedly attack trivial scavengers.

## 4. Feeding restrictions

### Cannot-chew animals

Default global feature toggle: On.

The per-species Dev editor can attach or remove ModExtension_CannotChew.

For a marked animal, Zoology prevents normal chunk-by-chunk corpse feeding where the animal is expected to swallow prey whole. The maximum permitted prey size comes from the race's maxPreyBodySize. For non-adult growth stages, the effective limit is additionally capped by the animal's current BodySize.

The marker itself has no numerical size parameter; the size rule is derived from the race and current life stage.

## 5. Physiology settings

### Enable mammal lactation

Default: On.

The mammal selector controls which species receive Zoology's mammal marker. Eligible mothers can receive lactation state after giving birth, young can request suckling, mothers can respond to those requests, and young-animal food selection is adjusted so newborn mammals do not behave like miniature adults.

The system also integrates with:

- animal feeding think trees;
- caravan feeding;
- auto-slaughter;
- existing animals in a loaded game through a lactation backfill component.

Allow slaughtering lactating animals defaults to Off. This option is unavailable while lactation is disabled.

### Enable animal childcare

Default: On.

The childcare selector controls which species receive the childcare marker.

The system can:

- keep young near their mother;
- assign explicit protect-young behavior;
- let nearby compatible herd or pack members participate in defense;
- keep family groups together when wild animals leave an overloaded map.

Controls:

- Young/clutch protection range: default 10, range 10-40.
- Minimum combat power to defend young and clutches from humans/mechanoids: default 70, range 0-1000.
- Do not flee from humans while protecting young: default Off.

The final option is meaningful only when childcare and human-directed fleeing are both enabled.

### Enable egg protection

Default: On.

Egg protection is a childcare sub-feature. Zoology can register clutch ownership, keep a mother near protected eggs, run incubation jobs, defend a clutch, let group members assist and preserve ownership information through supported egg stack changes.

Do not flee from humans while protecting clutches defaults to On. It requires childcare, egg protection and human-directed fleeing.

A development gizmo can force supported eggs to hatch immediately; this is a developer convenience, not ordinary gameplay.

### Enable wound licking

Default: On.

Eligible wild or factionless animals can weakly self-tend bleeding external injuries by licking them. This is intentionally low-quality self-care and does not replace proper tending, repair destroyed organs or solve major internal trauma.

### Enable ectothermic handling

Default: On.

The per-species ectotherm selector controls which animals use Zoology's ectothermic handling. Marked animals use the mod's cold-response logic rather than ordinary mammalian assumptions. This does not imply immunity to cold injury; the system still permits cold damage where appropriate.

## 6. Combat settings

### Override Combat Extended penetration

Available only when Combat Extended is detected.

Resetting Zoology settings while CE is present enables this option; without CE the setting is forced off.

When enabled, Zoology applies its CE melee/life-stage penetration handling, including life-stage scaling used by the Zoology animal combat data. It does not replace Combat Extended's combat system.

### Enable animal draft control

Default: On.

Eligible trained player animals can receive direct draft, movement and attack commands through Zoology's Beastmastery system.

The Zoology Beastmastery TrainableDef is Odyssey-gated. Runtime compatibility also recognizes compatible Beastmastery definitions exposed by supported external frameworks.

Requirements and restrictions include:

- the animal must be player-owned;
- it must have a training tracker and access to a supported draft-control trainable;
- the relevant trainable must be learned;
- it must have a master;
- the master must be present and capable of command;
- the animal cannot be newly drafted during a ritual;
- downed, dormant, deathresting or mentally broken animals cannot be controlled normally;
- commands remain tied to the master's animal-command range.

Zoology keeps linked draft/attack training state synchronized across the supported Beastmastery/attack trainables. If another supported drafting system already owns an animal, Zoology yields rather than creating a second direct-control path.

### Enable animal damage reduction

Default: On without Combat Extended.

This feature reduces implausible damage when extremely small animals, or unarmed humans, strike much larger animals. Predator-prey attacks are excluded so ordinary hunting is not disabled.

When Combat Extended is detected, Zoology forces this system off because CE already supplies a separate armor/penetration model.

## 7. Other behavior settings

### Animals in NPC groups

Default: On.

Zoology adds selected animal options to standard mixed human PawnGroupMaker groups and then validates the resulting animal against the generated human group.

For an animal companion to remain in the group:

- the group must be a standard mixed human/animal group;
- there must be an eligible humanlike handler in that generated group;
- the handler must be capable of Handling/Animals work;
- the handler's Animals skill must meet the animal's minimum handling skill;
- RimWorld's ordinary CanBeMaster restrictions must pass;
- one handler can own at most two tracked companion animals.

If an animal cannot be assigned safely, Zoology removes it before the incident reaches the map, returns its selected point cost to the group budget and attempts to spend those points on eligible human pawns using the same group's normal options.

Tracked companions are kept out of the human Lord and instead use animal-specific follow/defend behavior. When the human group withdraws, or no mobile human member of the original group remains, the animal transitions to flee behavior.

If a companion loses its master, Zoology first attempts reassignment within the same original generated group.

Orphaned NPC animals become wild defaults to Off. When Off, an unreassignable companion panic-flees. When On, it becomes factionless and tamable instead.

The generic handler-safety layer is not limited to Zoology's own XML marker: compatible vanilla or third-party animals already present in a standard mixed human group can also receive the safety checks.

### Pet recreation

Default: On.

Eligible player pets can act as recreation partners. Current activities include outdoor walking and fetch for eligible canines and local toy play for other eligible pets.

Eligibility considers faction, pet suitability, wildness, health/movement state and current availability.

Maximum pet wildness defaults to 0.2 and can be set from 0 to 1.

### Bonding mode

The settings UI exposes three effective bonding modes:

- vanilla bonding;
- expanded pet bonding;
- expanded all-animal bonding.

The default expanded configuration is pet bonding enabled and all-animal bonding disabled.

Expanded pet bonding allows eligible pets to form bonds even when normal trainability restrictions would prevent it in the supported bonding paths. Expanded all-animal bonding extends that trainability bypass to all animals. Ordinary RimWorld restrictions and chance modifiers not explicitly replaced by Zoology continue to apply.

### Custom flee danger

Default: On.

Zoology can replace the relevant vanilla danger decision with body-size-aware thresholds.

- Safe predator body-size threshold: default 0.7; settings UI range 0-30.
- Safe non-predator body-size threshold: default 3; settings UI range 0-30.

These are gameplay thresholds, not real biological masses.

### Ignore small pets by raiders

Default: On.

Very small player animals can be ignored as combat targets while they are not behaving as combatants.

- Small-pet body-size threshold: default 0.45; settings UI range 0-30.
- Small pets do not retaliate in melee: default On.

Combat participation can remove the protection; for example, an animal actively following its master into combat or entering a hostile mental state is no longer treated as a harmless small pet.

### Animals flee from humans

Default: On.

Wild animals can treat nearby colonists and mechanoids as threats under Zoology's human-directed flee system.

- Human search radius: default 12, range 6-24.
- Flee distance from human: default 16, range 6-40.

The per-species editor allows explicit opt-in or opt-out. Zoology also has built-in default exclusions for special categories and for animals whose own no-flee or carrier-threat behavior would conflict with ordinary human fleeing.

Human-directed fleeing can be temporarily suppressed while an animal is hunting, feeding, in a mental state, being tamed or defending protected offspring, eggs or prey when the corresponding rules permit it.

### Wild animal reproduction

Default: On.

Zoology provides a wild mating job that searches for compatible same-species or allowed cross-breeding partners.

Pause wild-to-wild mating when the ecosystem is overloaded defaults to On.

Force random wild animals to leave an overloaded ecosystem defaults to On.

Ecosystem weight limit defaults to 1.2 times the underlying vanilla-derived capacity and has a settings range of 1-3.

The ecosystem calculation incorporates the map's animal-density basis together with current seasonal suitability and supported game-condition modifiers. Biotech pollution is considered when Biotech is active.

The capacity rule applies to wild-to-wild reproduction; mating involving a faction animal is not blocked by the wild ecosystem limit. Family groups can leave together, and mothers currently guarding egg clutches are excluded from forced departure.

### Animal roamers and trainability

The per-species editor allows a race to be marked as a roamer or a non-roamer.

A roamer:

- has a configurable RoamMtbDays;
- is forced to Trainability None.

The editable roamer interval is clamped to 1-60 days in the current selector.

A non-roamer can be assigned one of the supported trainability defs:

- None;
- Intermediate;
- Advanced.

Player-owned roamers retain close-melee threat engagement after being struck, preventing vanilla roamer threat suppression from making them inert in immediate combat.

### Enable human bionics on animals

Default: On.

At startup, Zoology extends supported human bionic recipes to compatible animal body layouts and provides size-dependent animal variants for the combat-oriented implants it handles.

Animals carrying ModExtension_CannotBeAugmented are excluded.

This is the main restart-sensitive user setting. The bionic patchers mutate DefDatabase recipe/hediff relationships during initialization; turning the setting off at runtime does not reconstruct the original database state. Change this option before loading a play session and restart RimWorld after changing it.

### Enable aggression at slaughter

Default: On.

The per-species selector attaches or removes Zoology's slaughter-aggression marker.

A marked, standing animal can react when designated and when a slaughter job reaches it. The runtime implementation can replace the slaughter action with a manhunter response and remove the slaughter designation. Downed animals do not trigger this reaction.

The extension can also exclude the animal from peaceful ritual animal roles.

## 8. Dev settings

The Dev page exposes lower-level runtime systems that are normally controlled by XML markers or comps.

### Master runtime switch

Disable all runtime patches defaults to Off.

Changing runtime toggles in the settings window causes Zoology to reapply runtime Def overrides and rebuild its Harmony patch set. Therefore most toggle changes take effect without a game restart.

The master switch does not undo startup mutations that have already changed DefDatabase content, most importantly animal bionic recipe expansion. A restart is required for those cases.

### Insect cocoon spawn safeguard

Default: On.

Biotech only.

If a vanilla insect cocoon has a positive spawn budget but every configured pawn kind costs more combat power than the current budget, vanilla can otherwise generate no pawn and consume the cocoon. Zoology raises that cocoon instance's budget to the cheapest configured valid pawn so vanilla can generate one normally. Shared defs are not modified by this patch.

### Mutation protection

Default: On.

The per-species editor controls ModExtension_CannotBeMutated. The marker protects applicable animals from supported mutation mechanics while the runtime feature is enabled.

### Augmentation protection

Default: On.

The per-species editor controls ModExtension_CannotBeAugmented. The marker is also respected by Zoology's own animal-bionics patchers.

### No-flee behavior

Default: On.

The per-species editor controls ModExtension_NoFlee.

A marked animal can be prevented from ordinary ShouldAnimalFleeDanger behavior and from supported panic/terror mental-state starts.

### Flee from carrier

Default: On.

The per-species editor controls ModExtension_FleeFromCarrier.

Parameters:

- flee radius: default 12, editable range 1-60;
- flee body-size limit: default 0, editable range 0-20; zero means no explicit size cap;
- flee distance: default 16, editable range 1-80.

A marked carrier can be treated as a local threat by eligible animals when faction, distance, line-of-sight/reach and body-size checks pass.

### Flying flee start

Default: On.

This enables Zoology's special flee-start handling for supported flying animals.

### Gender-restricted attacks

Default: On.

Zoology's ToolWithGender class can restrict a melee tool to a specific RimWorld Gender value. The runtime patch also recognizes compatible Tool subclasses that expose a public restrictedGender field.

### Ageless comp

Default: On.

The per-species editor can attach or remove CompProperties_Ageless.

Its cleanup interval defaults to 6000 ticks and is editable from 60 to 120000 ticks. The comp periodically removes age-related hediffs that Zoology identifies as forbidden for that pawn, while Harmony guards block supported age-related additions before they take effect.

### Drugs-immune comp

Default: On.

The per-species editor can attach or remove CompProperties_DrugsImmune.

Its cleanup interval defaults to 2000 ticks and is editable from 60 to 120000 ticks. The implementation blocks supported drug/addiction hediff additions and periodically removes any that remain.

### Animal regeneration

Default: On.

This is a global runtime toggle for Comp_AnimalRegeneration behavior. The current settings UI does not provide a per-species regeneration selector.

Where the comp is defined in XML, it can choose baby, juvenile and adult regeneration hediffs, body-size fractions and a check interval. Those XML fields are described in FRAMEWORK.md.

### Animal clotting

Default: On.

The per-species editor can attach or remove CompProperties_AnimalClotting.

- check interval default: 360 ticks, editable 60-120000;
- tending quality default: 0.2-0.7;
- editable minimum and maximum are each clamped to 0-2.

The comp periodically tends bleeding hediffs on the animal.

### No porcupine quill

Default: On.

The per-species selector controls ModExtension_NoPorcupineQuill, which prevents the supported porcupine-quill hediff path from affecting marked animals.

## 9. Non-configurable runtime corrections

A few runtime patches are support infrastructure rather than standalone gameplay options.

### Guinea-pig leather replacement

After defs finish loading, Zoology replaces references to the vanilla guinea-pig leather def with squirrel leather where those ThingDef references occur. This is an automatic biological/material correction.

### Save-load think-tree cleanup

When loading a save, Zoology checks stored Job job-giver keys. If a key no longer resolves in the associated think tree, it clears the stale key and cached job giver so vanilla can continue safely instead of retaining an invalid think-node reference.

This exists to keep saves resilient when animal think-tree definitions change.

## 10. Compatibility behavior

### Combat Extended

With CE active:

- CE-specific patch folders are loaded;
- Zoology's non-CE size-aware damage reduction is disabled;
- the CE penetration override becomes available;
- animal combat data come from the Animals CE output rather than the vanilla damage columns where CE expects its own values.

### Vanilla Expanded Framework and animal mods

Zoology contains conditional patches for Vanilla Expanded Framework and supported Vanilla Animals Expanded modules. The direct-control system can normalize compatible Beastmastery data rather than requiring a duplicate training path.

### Alpha Animals, Alpha Biomes, Dinosauria and Megafauna

Dedicated patch folders are loaded only when the corresponding package is active.

### Optional runtime interoperability

The runtime code detects supported external animal-drafting ownership and avoids stacking a duplicate Zoology drafting path. The ageless system also contains an optional health-system hook that is ignored silently when that external system is absent.

### Known incompatibility

Animals Are Fun Continued is declared incompatible because it overlaps Zoology's animal interaction and recreation systems.

## 11. When settings take effect

Most settings are live or are synchronized immediately after a toggle changes. Zoology compares the runtime-toggle state before and after drawing the settings window, reapplies per-species Def overrides and rebuilds the relevant Harmony patches when needed.

Per-species editors write their overrides immediately.

A restart is still appropriate when a setting controls startup Def mutation rather than a reversible runtime patch. The important current case is human bionics on animals.

If a heavily modded game has an AI conflict, disable the smallest overlapping Zoology system first rather than disabling the whole mod. The Dev master switch exists as a diagnostic fallback, not as the normal way to configure gameplay.

## 12. Reset to defaults

The Reset button clears per-species runtime overrides and restores current build defaults. CE-dependent defaults are recalculated from whether CE is detected: non-CE damage reduction is enabled only without CE, while the CE penetration override is enabled by reset when CE is present.

For the XML/data model behind these systems, see [FRAMEWORK.md](FRAMEWORK.md). For maintainers regenerating animal patches, see [CHECKER.md](CHECKER.md).
