# Zoology: Realistic Animal Overhaul

<img src="https://i.ibb.co/ZpLXjc8Z/Preview.png">

Zoology is a data-driven animal overhaul for RimWorld 1.6. It recalculates animal stats from biological inputs, corrects a range of ecological and physiological inconsistencies, and adds configurable animal behavior systems such as improved predation, wild reproduction, childcare, lactation, scavenging, pet recreation, handler-bound NPC animal companions and more.

The mod is designed to make animals behave and perform more like animals rather than reskinned pawns, while still remaining compatible with RimWorld's event, combat and ecosystem systems.

## Table of contents

* [Steam Workshop](#steam-workshop)
* [Requirements](#requirements)
* [Overview](#overview)
* [Core biological overhaul](#core-biological-overhaul)
* [Configurable gameplay systems](#configurable-gameplay-systems)
* [Settings](#settings)
* [Mod compatibility](#mod-compatibility)
* [Compatibility notes and limitations](#compatibility-notes-and-limitations)
* [Technical notes for modders](#technical-notes-for-modders)
* [Contributing](#contributing)
* [License](#license)

---

## Steam Workshop

The player version of the mod is available on Steam:

https://steamcommunity.com/sharedfiles/filedetails/?id=3679396881

---

## Requirements

* **RimWorld 1.6**
* **Harmony**

Official DLCs are supported when present. Several systems also contain DLC-specific behavior; for example, mutation protection uses Anomaly mechanics and Zoology's Beastmastery trainable is Odyssey-gated.

---

## Overview

Zoology replaces a large part of vanilla animal balancing with values derived from zoological inputs. The source dataset includes parameters such as:

* average body mass
* gestation period
* litter or clutch size
* growth and life-stage timing
* bite force
* claw and tooth dimensions

Where suitable biological data are available, values are taken from primary or secondary literature and processed through a reproducible TSV-to-XML pipeline. The generated patches then map those biological inputs onto RimWorld statistics and combat definitions.

The overhaul affects much more than melee damage. Depending on the species, generated or manually reviewed patches may alter body size, health and hunger-related values, movement, life stages, reproduction, trainability, prey limits, ecosystem weight, combat power, meat and leather production, temperature-related behavior, melee tools and other animal-specific properties.

The goal is biological plausibility within RimWorld's mechanics, not a literal simulation. Combat Power and other game-facing values are still kept compatible with RimWorld's event-generation and balancing systems.

---

## Core biological overhaul

### Animal stat recalculation

Vanilla and official-DLC animals receive species-specific stat revisions generated from the biological dataset.

Melee DPS and movement performance are often higher than in vanilla because many vanilla animals are substantially underpowered relative to their real morphology and locomotor capabilities. Combat Power remains a separate game-balance quantity and is adjusted so that stronger animals also cost appropriately in encounters and pawn groups.

Zoology's behavior systems also use life-stage-aware Combat Power when comparing animals, so babies and juveniles are not evaluated as if they had full adult threat or defensive strength.

### Ecology and biome distribution

Wild-animal distributions have been revised to remove obvious zoological mismatches while preserving RimWorld's biome structure and usable animal density.

### Audio and presentation fixes

Several animals use corrected or more appropriate sound sets. For example, large cats are no longer forced to use domestic-cat-style vocalizations where a better existing asset is available.

### Other biological corrections

Examples include:

* birds can begin danger fleeing through flight rather than relying on ground sprinting
* ectothermic animals can use a cold-induced metabolic slowdown model instead of ordinary mammalian hypothermia behavior
* ectothermic handling still permits frostbite where appropriate
* guinea pig fur is replaced with squirrel pelt
* selected compatible taxa receive corrected cross-breeding / cross-aggro relationships
* animal pregnancy stages apply progressively higher hunger demand
* multiple species-specific reproduction, body, food and life-stage definitions are corrected

---

## Configurable gameplay systems

Most gameplay-facing behavior systems can be enabled or disabled independently in **Mod Settings → Zoology Mod**. Most are enabled by default.

### Advanced predation logic

Predator prey selection can use more than vanilla food eligibility. Zoology considers factors including:

* body size
* age / life stage
* kinship and species relationships
* relative combat power

Predators are also prevented from treating similarly dangerous predators as ordinary prey unless they have a sufficient combat-power advantage. The current dominance threshold is approximately **30%**.

**Setting:** `Enable advanced predation logic`

### Pack hunting

Pack hunters can coordinate attacks so that a group can pursue prey that would be too dangerous for one individual. Group participation is not limited to animals that independently happen to be hungry at the same moment.

**Setting:** `Enable pack hunting`

### Prey fleeing

Potential prey can actively flee from nearby hunting predators. Pursuit behavior also contains abandonment logic so predators do not chase indefinitely when a target cannot realistically be caught.

A separate option makes animals respond to nearby **non-hostile predators** even before those predators have selected them as prey.

Relevant settings include:

* `Enable prey fleeing`
* `Predator search radius`
* `Flee distance from target predator`
* `Animals flee from non-hostile predators`
* `Non-hostile predator search radius`
* `Flee distance from predator`

### Custom flee-from-danger behavior

Zoology can replace parts of vanilla `ShouldAnimalFleeDanger` handling with size-aware danger evaluation.

Separate body-size thresholds control what animals consider safe when facing predators and non-predator threats.

**Settings:**

* `Enable custom flee danger`
* `Safe predator body size threshold`
* `Safe non-predator body size threshold`

### Animals fleeing from humans

Wild animals can react to nearby colonists and mechanoids rather than ignoring them until directly attacked.

The species list is configurable. Animals can also temporarily suppress human-directed fleeing when they are hunting, feeding, in a mental state, being tamed, or defending protected offspring / prey under the relevant rules.

**Settings:**

* `Animals flee from humans`
* `Human search radius`
* `Flee distance from human`
* `Configure animals fleeing humans`

### Predator protection of kills

Predators can remain associated with a kill and defend it from animals, colonists or mechanoids that attempt to take or feed on it.

The system tracks ownership of prey corpses rather than treating every corpse as globally free food. Very small scavengers relative to the predator can be treated as non-threatening, reducing pointless attacks on tiny competitors.

Relevant settings include:

* `Enable predators defending corpses`
* `Prey protection range`
* `Unowned corpse size multiplier`
* `Allow predators to defend prey from humans and mechanoids`
* `Minimum combat power to defend prey`

Predators that qualify to defend their kill against humans or mechanoids can also suppress ordinary flee behavior while the defense is active.

### Scavenging

Species configured as scavengers can consume rotten corpses. Individual scavenger definitions can additionally allow consumption of very rotten / desiccated remains.

Skeletonized remains provide reduced nutrition relative to fresh food.

**Settings:**

* `Enable scavenging`
* `Configure scavenger species`

### Swallow-whole feeding

Animals configured with Zoology's cannot-chew behavior do not tear ordinary chunks from prey corpses. They must swallow prey whole, and prey larger than the animal's configured maximum prey size cannot be swallowed.

This behavior is species-configurable in the advanced settings.

### Wild animal reproduction and ecosystem regulation

Wild animals receive an explicit mating job that can find compatible mates of the same species or of an allowed cross-breeding species.

The system can regulate **wild-to-wild** reproduction using the map's ecosystem capacity rather than a simple animal-count cap. The target capacity is based on RimWorld's biome animal density and is adjusted for current seasonal suitability, game-condition density modifiers and Biotech pollution when applicable.

When the configured ecosystem limit is reached:

* wild-to-wild mating can be paused
* mating involving a faction animal is not blocked by the wild ecosystem limit
* overpopulation can cause random wild animals to leave the map
* childcare family groups can leave together
* mothers currently guarding egg clutches are excluded from forced departure

**Settings:**

* `Enable wild animal reproduction`
* `Pause wild-to-wild mating when the ecosystem is overloaded`
* `Force random wild animals to leave an overloaded ecosystem`
* `Ecosystem weight limit`

### Mammal lactation and nursing

Mammalian young can use Zoology's suckling and maternal-feeding logic instead of behaving like miniature adult herbivores or carnivores immediately after birth.

The lactation system includes:

* maternal lactation hediffs
* offspring suckling jobs
* mother response to suckling requests
* food suitability handling for mammalian young
* caravan support for feeding mammalian babies
* auto-slaughter handling for lactating animals

**Settings:**

* `Enable mammal lactation`
* `Allow slaughtering lactating animals`
* `Configure mammal species`

### Childcare and offspring following

Species configured for childcare can keep juveniles close to their mother instead of letting them wander independently.

Parents can defend offspring from threats, and nearby herd or pack members can join the defense where the group-response rules apply.

**Settings:**

* `Enable animal childcare`
* `Configure childcare species`
* `Young/clutch protection range`
* `Minimum combat power to defend young and clutches from humans/mechanoids`
* `Do not flee from humans while protecting young`

### Egg-clutch protection and incubation

Egg-laying childcare species have additional clutch behavior rather than only generic offspring defense.

The current implementation can:

* register and track the mother of a clutch
* keep the mother near protected eggs
* run explicit incubation jobs
* defend clutches from threats
* allow group members to participate in clutch defense
* track clutch ownership through stack splitting / merging and related egg-state changes
* prevent protected same-lineage eggs from being treated as ordinary opportunistic food under the relevant childcare rules

**Settings:**

* `Enable animal childcare`
* `Enable egg protection`
* `Do not flee from humans while protecting clutches`

### Pet recreation

Colonists can use suitable player-owned pets as recreation partners.

Current activities include:

* walking eligible canines outdoors
* playing fetch with eligible canines
* local toy play with other eligible pets, including indoors

Pet eligibility depends on petness, wildness, faction, health / movement capacity and current availability. Maximum eligible wildness is configurable.

**Settings:**

* `Pet recreation`
* `Maximum pet wildness`

### Expanded animal bonding

Zoology provides three bonding modes:

1. **Vanilla animal bonding** — normal RimWorld trainability rules.
2. **Expanded pet bonding** — eligible pets can bond regardless of normal trainability restrictions through supported bonding events, pet play and nuzzling.
3. **Expanded animal bonding** — extends that trainability bypass to all animals.

The default expanded mode is **Expanded pet bonding**; expanded all-animal bonding is optional.

### Animal draft control / Beastmastery

Zoology contains direct-control support for animals with access to the Beastmastery special trainable. The Zoology Beastmastery definition itself is gated behind **Odyssey**; the runtime code can also normalize compatible Beastmastery definitions from other frameworks when present.

Once an eligible player animal has learned the required trainable and has an assigned master, it can be drafted and receive direct movement and attack orders.

Direct control remains intentionally tied to the master:

* commands must stay inside the master's animal-command range
* the animal cannot be newly drafted during rituals
* downed, dormant, deathresting or mentally broken animals cannot be controlled
* an unavailable, downed or absent master prevents control
* animals are automatically undrafted if their master is missing, dead or downed, if the animal becomes dormant, or if the master leaves them behind on the map; mental-state and deathrest conditions block control while they last

**Setting:** `Enable animal draft control`

### NPC animals in raids and other human groups

Zoology allows suitable animals to appear as real companions of NPC handlers rather than as ordinary members of a human Lord.

The companion-safety layer is deliberately restricted to **human NPC factions** and standard mixed human/animal `PawnGroupMaker` groups. It does not rewrite all-animal groups and does not apply this behavior to non-human factions.

When an animal is selected for a tracked group:

* it must have an eligible humanlike handler from the same generated group
* the handler must be capable of Handling / Animals work
* the handler's Animals skill must meet the animal's actual minimum handling skill
* RimWorld's normal `CanBeMaster` restrictions must also pass
* one handler can be assigned at most **two** NPC companion animals

If no valid handler exists, the animal is removed before the group reaches the incident. Its exact selected point cost is returned to the group's budget and Zoology attempts to spend those points on eligible human pawns using the same group's normal options and vanilla-style selection constraints.

Registered companions are kept out of the human Lord. Instead they use animal-specific behavior derived from RimWorld's trained-animal AI:

* outside active combat they stay close to their master
* they defend the master against nearby melee threats
* while the master is fighting they can engage threats over a wider radius instead of remaining glued to the master's cell
* ordinary animal danger-fleeing is suppressed while the companion is actively following its master
* when the human group withdraws, or no mobile human member of the original group remains on the map, the companion switches to panic-flee behavior

If a master is killed, destroyed, changes faction or otherwise becomes invalid, Zoology first tries to reassign the animal to another eligible human from the **same original generated group**. If no replacement exists, the default behavior is to panic-flee from the map. An optional setting can instead make the animal factionless and tamable.

Zoology's own NPC group additions are conservative. Core outlander, pirate and tribal templates receive Labradors and Huskies in suitable groups, with several Vanilla Animals Expanded dog breeds added when that mod is present. Biotech-specific handling gives Pigskins a similar domestic-dog pool, Wasters Odyssey Bog Hounds, and replaces Yttakin wild boars with timber wolves while leaving their existing wargs alone. Impid and Neanderthal groups are intentionally not given new animals by this patch.

**Settings:**

* `Animals in NPC groups`
* `Orphaned NPC animals become wild`

`Animals in NPC groups` controls only animal options added by Zoology itself. The generic handler-safety system remains active for vanilla or third-party animal options that already occur inside standard mixed human groups, as long as Zoology's runtime layer is enabled.

### Raiders ignoring small pets

Raiders can ignore sufficiently small, non-threatening player pets instead of treating every cat-sized animal as a combat target.

Small pets cease to qualify for this protection when they are behaving as combatants, for example by following a master into combat or becoming manhunter.

An additional option can prevent very small pets from making ineffective melee attacks against hostiles.

**Settings:**

* `Ignore small pets by raiders`
* `Small pet body size threshold`
* `Small pets do not retaliate in melee`

### Human bionics on animals

Zoology can make many human bionics installable on animals through runtime patching. Body-size-dependent hediff scaling is applied where supported.

Species can explicitly opt out through the cannot-be-augmented extension.

**Setting:** `Enable human bionics on animals`

This system has dedicated handling for **Combat Extended**.

### Aggression at slaughter

Configured animals can react aggressively when a slaughter attempt is made instead of passively accepting it.

Downed animals can still be slaughtered safely. Species assignment is configurable.

**Settings:**

* `Enable aggression at slaughter`
* `Configure slaughter aggression species`

### Wound licking

Wild, factionless animals can tend superficial external bleeding wounds by licking them.

This is deliberately weak self-care rather than a substitute for medicine:

* tending quality is very low
* the system is intended for external bleeding injuries
* it does not restore destroyed organs or solve serious internal trauma
* tamed animals still rely on colonists for proper treatment

**Setting:** `Enable wound licking`

### Animal damage reduction

An optional size-aware damage rule reduces implausible damage when extremely small animals, or unarmed humans, attack much larger animals. Predator-prey interactions are excluded so natural hunting is not unintentionally disabled.

**Setting:** `Enable animal damage reduction`

This setting is automatically disabled while **Combat Extended** is active because CE already models armor and penetration separately.

### Roamers and trainability editor

The settings menu includes a per-species **Animal roamers and trainability** editor.

A species marked as a roamer:

* uses a configurable `RoamMtbDays`
* is forced to `Trainability = None`

A non-roamer can instead be assigned one of the supported trainability levels:

* None
* Intermediate
* Advanced

This makes the vanilla roaming / taming distinction user-configurable without editing XML.

Player-owned roamers also retain normal close-melee threat engagement when they are actually struck, preventing vanilla roamer threat suppression from making them inert in direct melee.

---

## Settings

Zoology's settings are divided into five tabs:

### Predator / prey

Predator fleeing, pack hunting, advanced predation, scavenging and kill protection. Scavenger status can also be configured per species.

### Physiology

Mammal lactation, childcare, egg protection, wound licking and ectothermy. Mammal, childcare and ectotherm assignments can be configured per species.

### Combat

Combat Extended penetration override, animal draft control and non-CE animal damage reduction.

### Other behavior

NPC animals in human groups and orphan handling, pet recreation, bonding mode, custom flee behavior, small-pet raid behavior, human-directed fleeing, wild reproduction, roamer/trainability configuration, animal bionics and slaughter aggression.

### Dev

Advanced runtime-patch and framework controls. This page includes the master runtime-patch switch, an insect-cocoon compatibility safeguard, and per-species controls for lower-level extensions and comps such as:

* cannot-chew behavior
* no-flee behavior
* flee-from-carrier behavior
* mutation / augmentation protection
* agelessness
* drug immunity
* animal clotting and regeneration
* no-porcupine-quill behavior

It also controls flying-flee behavior and gender-restricted attacks. Some changes may require a reload before every patched behavior is fully synchronized.

---

## Mod compatibility

Zoology is designed so that invasive runtime behavior can be disabled if another mod replaces the same systems.

### Dedicated integration present in the current build

The distributed mod contains dedicated patch folders or runtime integration for:

* **Combat Extended**
* **Vanilla Expanded Framework**
* **Vanilla Animals Expanded**
* **Vanilla Animals Expanded - Royal Animals**
* **Vanilla Animals Expanded - Endangered**
* **Vanilla Animals Expanded - Waste Animals**
* **Alpha Animals**
* **Dinosauria**
* **Megafauna**

The current metadata also defines load ordering relative to the official DLCs and several supported animal frameworks.

Other animal mods may work through Zoology's generic runtime systems, but they should not be described as explicitly patched unless a dedicated compatibility path exists in the current build.

### Combat Extended integration

When Combat Extended is installed, Zoology includes CE-specific animal combat patches based on biological inputs including:

* hide thickness and toughness
* claw and tooth dimensions
* bite force

These are mapped to CE-compatible melee and penetration behavior rather than relying on the non-CE damage-reduction layer.

The optional **Override Combat Extended penetration** setting changes life-stage penetration scaling so juvenile animals do not inherit implausibly high adult penetration relative to their actual size.

### Known incompatibility: Animals Are Fun Continued

The current `About.xml` explicitly marks the following package IDs as incompatible:

* `ColossalFossil.AnimalsAreFunContinued`
* `ColossalFossil.AnimalsAreFunContinued_copy`

Do not describe Animals Are Fun Continued as an explicitly compatible mod for this build.

---

## Compatibility notes and limitations

### Runtime compatibility is configurable, not guaranteed

Several Zoology systems patch central RimWorld AI methods. They are individually switchable specifically because large mod lists may contain another mod that replaces the same behavior. If a conflict appears, disabling the overlapping Zoology feature is preferable to assuming both patches can safely control the same AI decision.

---

## Technical notes for modders

Zoology also exposes framework components used by its own patches and available to dependent animal mods.

### Data generation pipeline

Most large species-stat patch sets are generated from a TSV dataset by Python rather than maintained as hand-written XML.

Contributors should prefer changing the source biological data / generator inputs instead of manually editing generated race patches, otherwise later regeneration may overwrite the change.

### Gender-restricted attacks

`ToolWithGender` allows sex-limited melee attacks such as male-only horns, tusks or antlers when Combat Extended is not providing its own equivalent handling.

### Runtime animal feature configuration

`ZoologyRuntimeAnimalOverrides` allows selected extensions and comp-backed features to be enabled or disabled per species from the settings UI. Some supported parameters can also be edited per species at runtime.

### NPC animal group integration

The NPC companion safety layer hooks standard normal human pawn-group generation and trader-guard generation. Handler validation is intentionally generic: an animal option does **not** need to use Zoology's marker class to receive the no-handler safety rule.

`ZoologyPawnGenOption` exists primarily so the **Animals in NPC groups** setting can identify and remove Zoology's own XML additions without removing vanilla or third-party options. Mixed human/animal groups supplied by other mods can therefore benefit from handler assignment, point-preserving rejection and companion AI automatically, while all-animal and non-human faction groups are left alone.

### Main framework components

* `Comp_Ageless` — removes configured age-related hediffs periodically.
* `Comp_DrugsImmune` — blocks / removes drug and addiction hediffs.
* `Comp_AnimalClotting` — periodically self-tends bleeding injuries with configurable quality.
* `Comp_AnimalRegeneration` — applies life-stage / body-size-dependent regeneration hediffs.

### Main marker / behavior extensions

* `ModExtension_IsMammal` — enables mammalian nursing behavior and related young-animal food handling.
* `ModExtensiom_Chlidcare` — enables offspring following and parental protection behavior. The misspelled class name is retained for compatibility with existing XML.
* `ModExtension_AgroAtSlaughter` — enables slaughter aggression.
* `ModExtension_IsScavenger` — enables rotten-corpse scavenging; can optionally allow very rotten remains.
* `ModExtension_CannotChew` — enables swallow-whole feeding restrictions.
* `ModExtension_NoFlee` — blocks supported flee / panic behavior.
* `ModExtension_Ectothermic` — enables ectothermic cold handling.
* `ModExtension_CannotBeMutated` — protects marked animals from supported mutation mechanics.
* `ModExtension_CannotBeAugmented` — prevents supported bionic / implant augmentation.
* `ModExtension_NoPorcupineQuill` — prevents the porcupine-quill hediff where supported.
* `ModExtension_FleeFromCarrier` / scary-carrier behavior — makes nearby eligible animals flee from the carrier according to configured radius, size and distance parameters.

Runtime systems are primarily implemented with Harmony patches. The Dev settings include a global runtime-patch disable switch for troubleshooting and compatibility work.

---

## Contributing

Contributions are welcome, especially:

* corrections to biological source data
* stronger primary or review sources for existing values
* compatibility patches
* additional species datasets
* code fixes and performance improvements
* reproducible bug reports

For generated animal statistics, contributions should target the source dataset / generator whenever possible rather than generated XML output.

---

## License

MIT License
