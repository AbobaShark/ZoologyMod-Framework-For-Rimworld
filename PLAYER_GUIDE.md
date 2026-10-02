# Zoology Player Guide

Zoology changes much more than animal numbers. It combines a large generated rebalance of animal Defs with runtime behavior systems for predation, reproduction, parenting, feeding, pets, combat and NPC animal companions.

This guide is organized by what a player actually sees in a colony or ecosystem. The complete settings reference is at the end. That separation is intentional: the behavior sections explain what the systems do and how they interact; the settings section tells you how to enable, disable or tune them.

## 1. The data-driven animal overhaul

A large part of Zoology is active before any runtime behavior is considered. Animal race patches are generated from the project's species data and calculation workbooks and then loaded by RimWorld as ordinary XML.

### Species statistics

For supported animals, Zoology can replace or recalculate values such as:

- body size and related mass-scale assumptions;
- health scale;
- movement speed;
- hunger and food consumption;
- life expectancy and growth timing;
- wildness and trainability;
- ecosystem weight and biome commonality;
- Combat Power;
- melee attacks, damage and cooldown;
- meat and leather output;
- reproduction-related values;
- animal products such as eggs, milk and wool.

The goal is not to force every real-world measurement directly into RimWorld. Biological measurements are first converted into game-facing quantities so that encounter budgets, hunting, melee combat and animal husbandry remain playable.

### Anatomy and attack tools

Zoology ships additional or corrected animal body definitions and body-part groups for animals whose anatomy cannot be represented well by the limited vanilla body templates. Supported mod animals can receive the same treatment.

Melee tools are also species-specific. A bite, horn strike, claw swipe, kick or other attack can have its own damage, cooldown, selection weight and, when Combat Extended is present, its own penetration values.

Some attacks can be restricted by sex when the anatomy is sexually dimorphic. This is handled by Zoology's gender-restricted attack system rather than by giving both sexes an attack they should not possess.

### Life stages are mechanically different animals

Babies and juveniles are not treated as adults with a smaller graphic. Zoology changes life-stage factors for body size, health, hunger, movement, armor and melee performance, and it uses separate life-stage factors when behavior systems compare Combat Power.

This matters outside combat as well. A juvenile predator, a newborn prey animal and an adult of the same species can make different decisions because their effective body size and combat strength are different.

The current game files are the authority for what is actually loaded. The working spreadsheet also contains life-stage source values; maintainers should see FRAMEWORK.md for a currently known source/output synchronization discrepancy rather than assuming every workbook value is already present in the shipped XML.

### Pregnancy and animal products

The static patch layer also changes husbandry-related values. Examples include:

- pregnancy increases hunger progressively through its stages;
- egg-laying intervals and clutch sizes are species-specific;
- fertilized egg incubation times are species-specific;
- milk yield and milking intervals are adjusted for supported mammals;
- wool/shearing data are adjusted for supported animals and integrations;
- meat yields are derived from the animal data rather than one generic assumption;
- leather market, protection and insulation values are rebalanced;
- chitin and other non-mammalian materials are handled separately where the animal data require it.

These changes are independent of the optional runtime lactation system. A cow's ordinary milkable comp, for example, is a production mechanic; Zoology's lactation system is about nursing newborn mammals.

### Biomes and ecology

Supported animals receive revised biome commonality and ecological placement. Zoology patches Core biomes, Odyssey biomes and compatible third-party biome/animal mods.

The same ecological data also matter to Zoology's wild-reproduction capacity system: the game can have a biome that supports many animals in one season and substantially fewer in another.

### Cross-breeding and related species

Zoology adds selected cross-breeding relationships where the underlying animals are treated as compatible. In Core, the shipped patches include Arctic/Timber wolf compatibility and, when the relevant expanded-animal package is absent, Labrador/Husky compatibility.

The same race-level compatibility is reused by several runtime systems. A compatible lactating female can nurse a compatible cross-bred baby, and the wild-mating system can search allowed cross-breed targets.

### Other static corrections

The XML layer also contains smaller corrections that are visible in normal play, including selected animal graphics, shadows, sounds, caravan carrier choices, body-clock data, egg/hatcher definitions and compatibility-specific race patches.

For example, the Core caravan patch sets Horse and Donkey carrier entries for Outlander and tribal trader groups. These changes are data patches rather than settings-driven AI features.

## 2. Predation is based on the actual predator and prey

Vanilla predation mostly asks whether a target is legal food and then uses fairly broad heuristics. Zoology adds a second layer intended to make prey choice reflect size, health, life stage and danger.

### Choosing prey

With Advanced predation enabled, Zoology can consider:

- the predator's current BodySize;
- the prey's current BodySize;
- life-stage-adjusted Combat Power;
- current health;
- whether the prey is downed;
- distance;
- whether the target is itself a predator;
- fence protection and other vanilla exclusions;
- whether young are actively protected;
- whether the predator can physically consume the prey under the cannot-chew rules.

For selected large predators, preferred prey size is centered on prey of roughly comparable scale instead of rewarding the predator for choosing the tiniest legal target. Very small prey and excessively large prey are both less attractive.

Downed or badly injured prey become easier targets because Zoology evaluates vulnerability, not only the species' nominal adult combat value.

### Predator-on-predator hunting

Predators do not automatically treat every other predator as safe prey. Zoology applies a stricter combat-power requirement to predator-on-predator hunting than to ordinary prey and can reject a hunt before it starts when the target is too dangerous.

This is one reason young life stages matter: a juvenile predator may be an acceptable target where a healthy adult of the same species is not.

### Pack hunting

Default: enabled.

Pack hunting provides additional support when a herd/pack predator encounters prey that would be too dangerous for one hunter. The acceptance logic can treat supported pack participation as additional combat strength rather than requiring the lead predator to pass the entire danger test alone.

This is not a generic permanent combat buff. The support is used in the predation decision and pack-hunt behavior around the selected prey.

### Chase limits

Zoology tracks predator-prey pursuits. If a hunt turns into a prolonged chase and the predator is still not in immediate melee range, the pursuit can be stopped and that predator/prey pair temporarily blocked.

This prevents a single fast or unreachable prey animal from dragging a predator around the map indefinitely.

### Protected young affect prey choice

The childcare system feeds back into predation. A baby or juvenile that has a credible nearby protector can be penalized or rejected as prey rather than being evaluated as an isolated weak pawn.

That does not make young completely immune. Protection depends on the actual protector, threat and childcare settings.

## 3. Prey fleeing and threat awareness

### Fleeing from an active predator

Default: enabled.

An animal targeted by a predator can detect that pursuit and run before the predator reaches melee range.

Default values:

- predator search radius: 18 cells;
- flee distance from the predator targeting the animal: 24 cells.

### Fleeing from nearby non-hostile predators

Default: enabled.

Potential prey can also avoid a nearby predator that has not yet started a hunt.

Default values:

- non-hostile predator search radius: 12 cells;
- flee distance: 16 cells.

This allows prey animals to maintain distance from predators instead of waiting until a formal PredatorHunt job is already active.

### Fleeing from humans

Default: enabled.

Wild animals can also treat nearby humanlike pawns and relevant mechanoid threats as reasons to flee.

Default values:

- human search radius: 12 cells;
- flee distance: 16 cells.

The species list is configurable. Zoology also excludes or suppresses this behavior when another active behavior should take priority.

For example, an animal normally should not break its current behavior just because a human entered the radius while it is:

- actively hunting;
- feeding;
- in a mental state;
- being tamed or trained;
- defending young;
- defending an egg clutch;
- defending owned prey/carrion under the corresponding protection rules.

### Custom size-aware flee danger

Default: enabled.

Zoology can replace part of vanilla's animal danger check with BodySize thresholds. The default safe thresholds are:

- predator BodySize: 0.7;
- non-predator BodySize: 3.0.

These are RimWorld BodySize values, not kilograms.

### Flying flee start

The Dev feature "Flying flee start" enables Zoology's special flee-start handling for supported flying animals so their escape behavior can begin through the appropriate flight path rather than relying only on ordinary ground-animal assumptions.

## 4. Predators can own and defend a kill

Default: enabled.

A predator that has killed or claimed prey can remain associated with the corpse instead of immediately losing all behavioral relationship to it.

The protection system tracks the corpse even when it is carried and can keep the predator near its food. Competitors that try to take or feed from the protected corpse can trigger a defensive response.

Default protection range: 20 cells.

### Unowned corpses

A corpse without an active owner is not treated identically to a predator's current kill. The "unowned corpse size multiplier" controls how large a corpse has to be before it is important enough to generate the same kind of competitive response.

Default: 5.

### Humans and mechanoids

Default: enabled.

A sufficiently strong predator can defend its prey from humans or mechanoids that threaten the protected food.

Default minimum Combat Power: 70.

This Combat Power test uses Zoology's life-stage-aware combat values where appropriate.

### When protection suppresses fleeing

A predator that has committed to defending food may temporarily stop using ordinary flee behavior. This prevents contradictory AI in which an animal chooses to protect a corpse and simultaneously tries to flee from the pawn contesting it.

## 5. Scavengers are adapted to carrion

Default: enabled for species marked as scavengers.

Zoology's scavenger marker does more than allow a rotten corpse to pass one food check.

### Finding carrion

A marked scavenger can search map corpses during normal food selection and choose a reachable, reservable corpse as food.

The search still respects:

- forbidden status;
- reachability;
- reservations;
- minimum nutrition requirements;
- whether the corpse is flesh;
- the animal's ordinary food rules;
- cannot-chew size restrictions.

### Rotten food

Scavengers are protected from the normal rotten-corpse food-poisoning path when the poisoning cause is rotten carrion.

They are also protected from the supported lung-rot exposure path associated with this feeding niche.

This immunity is specific to the scavenging system; it is not a blanket immunity to every disease or toxin.

### Very rotten / desiccated remains

The per-species allowVeryRotten parameter defaults to false.

When it is enabled, a scavenger can use desiccated remains that would otherwise be rejected. Desiccated carrion is deliberately poor food: the scavenging code reduces its effective nutrition to 10% of the corpse's ordinary nutrition.

### Corpse consumption

Scavengers still consume the corpse through RimWorld's body-part ingestion model. Zoology does not turn a skeleton into a full fresh carcass or silently restore missing nutrition.

## 6. Animals that cannot chew must swallow prey whole

Default global feature: enabled for marked species.

ModExtension_CannotChew represents animals whose feeding strategy cannot reasonably use RimWorld's generic "take bites from any corpse" behavior.

### Prey-size limit

The base maximum comes from the race's maxPreyBodySize.

For a non-adult animal, Zoology additionally caps the limit by that individual's current BodySize. A baby of a species that can swallow large prey as an adult therefore cannot immediately consume adult-sized prey just because the race definition has a large maxPreyBodySize.

### Where the limit is used

The same rule is used in:

- prey acceptability;
- corpse feeding;
- scavenging.

A corpse that is too large is not made legal just because the eater is a scavenger.

## 7. Mammal reproduction is followed by real nursing behavior

Default: enabled for species marked as mammals.

Zoology's mammal system is designed so a newborn mammal does not immediately behave like a miniature adult herbivore or carnivore.

### Birth starts lactation

When a supported female mammal gives birth, Zoology applies the lactating hediff and associates the newborns with the birth context where possible.

A backfill system handles already-existing games and cases where mother/newborn state has to be reconstructed after loading.

The lactation state is maintained by nursing. It decays when the mother no longer feeds young and can be lost under severe malnutrition.

### When a baby wants milk

A mammal baby requests suckling when its food need falls below 33%.

It must still be a real baby life stage, alive and able to participate in the feeding behavior. Mental-state and reachability conditions can interrupt the process.

### A mother must be able to afford nursing

A lactating female must:

- be alive;
- be a mammal;
- not be downed for the active mother-feeding job;
- not be in a mental state;
- still have the lactating hediff;
- have at least 15% of her own food need remaining.

Nursing transfers nutrition from the mother to the baby. It is therefore not free nutrition: a heavily underfed mother cannot indefinitely sustain offspring.

### Who can nurse whom

The nursing search uses species compatibility, not only the direct parent relation. Same-species mothers are compatible, and races listed through RimWorld's canCrossBreedWith relationship can also be compatible.

Faction/host-faction compatibility is also checked. Zoology does not make an unrelated hostile mammal into a universal milk source.

### Baby and mother can initiate the interaction

The system is deliberately two-sided.

A hungry baby can find a reachable compatible lactating mother and request suckling.

A lactating mother can also search for a hungry compatible baby. When more than one baby is available, the mother prioritizes extreme hunger and malnutrition before ordinary distance.

If the mother is standing and mobile, she can go to the baby and feed it.

If the mother is downed or lying in bed, the baby can go to her and suckle instead, provided the mother is still otherwise capable of nursing.

This prevents the system from depending on one specific pawn being the one whose think tree happened to run first.

## 8. Handlers can feed hungry mammal babies

Default: enabled while mammal lactation is enabled.

This is a separate colony work system in the current build, not merely a side effect of nursing.

### What handlers do

A colonist assigned to Handling can bring suitable baby food to a hungry player-owned mammal baby.

The work:

- uses the Handling work type;
- is deliberately lower priority than the core animal-handling jobs above it;
- requires Manipulation;
- cannot be performed by mechs;
- can be given as a direct manual order.

Unlike vanilla patient feeding, the baby does not have to be in a medical bed or resting. The custom job driver feeds the animal where it is standing or lying.

### Which babies qualify

The target must be:

- an animal;
- a mammal according to Zoology's mammal marker;
- in a baby developmental/life stage;
- player-faction;
- alive and spawned;
- not in a mental state;
- hungry enough for RimWorld's feed-patient logic to consider it;
- capable of having its food need filled.

This feature does not turn juveniles or adult animals into handler-fed patients.

### Which food is used

The handler first checks suitable food already carried in inventory.

If none is available, the WorkGiver searches the map for food the baby can actually eat under Zoology's baby-food rules.

The search excludes drugs, corpses, food dispensers and opportunistic plant harvesting for this task. The selected food must still pass the baby's race food restrictions and the food's babiesCanIngest flag.

### Why this matters

Nursing remains the preferred biological path, but a colony is no longer forced to let a mammal baby starve because its mother is absent, inaccessible, no longer lactating or temporarily unable to feed it.

The handler-feeding option is subordinate to the main Mammal lactation setting. Turning lactation off also turns handler baby feeding off.

## 9. Mammal babies use baby food rules

While mammal lactation is active, Zoology changes ordinary food suitability for mammal babies.

A food must be marked as ingestible by babies and must also be a food the animal's race can ever eat.

This prevents a newborn mammal from automatically using the full adult diet just because the adult race is a herbivore, carnivore or omnivore.

The baby-specific logic also blocks the fishing job for mammal babies.

If no nursing interaction happens, a baby can still use suitable ordinary baby food. Handler feeding uses the same suitability rules rather than a separate list.

### Infant training

While this system is active, Zoology suppresses the normal rare training-tracker tick for mammal babies. Infant feeding and survival are therefore not mixed with ordinary adult animal-training maintenance during the baby stage.

## 10. Mammal babies in caravans

The nursing system also runs in caravans, where normal map jobs do not exist.

For a mammal baby in a caravan, Zoology:

1. recognizes the baby as a mammal infant;
2. ensures the biological mother is lactating when the mother and baby are both present and compatible;
3. looks for a compatible lactating feeder in the caravan when the baby wants milk;
4. transfers nutrition from that feeder if possible;
5. falls back to suitable caravan inventory food when necessary.

The result is that leaving the map does not silently disable the newborn feeding model.

## 11. Lactation and auto-slaughter

"Allow slaughtering lactating animals" defaults to off.

When it is off, Zoology removes lactating females from the effective counts used by auto-slaughter and prevents the auto-slaughter WorkGiver from selecting them.

This protection is aimed at automatic herd management. A deliberate manual slaughter designation is not the same as an auto-slaughter selection and is not silently cancelled by the lactation exclusion.

This system is separate from the "Aggression at slaughter" feature. A species can have either, both or neither behavior.

## 12. Childcare keeps young associated with adults

Default: enabled for species marked with Zoology's childcare extension.

The childcare system covers both ordinary young animals and egg-laying species.

### Babies and juveniles stay near their mother

Young animals can use a dedicated wander behavior rooted near the mother instead of wandering as if they were independent adults.

The wander behavior uses a short local radius around the mother.

Zoology first tries to use the actual parent relation. If that relation is unavailable, it can reuse a mother observed by the birth/childcare systems, and as a fallback it can infer a nearby compatible adult female of the same species lineage and factional context.

The fallback exists to keep behavior robust in modded games where the expected relation data are incomplete; it is not meant to redefine every nearby female as the biological mother.

### Adults defend young

An adult with childcare behavior can react when a nearby baby or juvenile is threatened.

The protection system evaluates:

- whether the young belongs to the same supported lineage;
- whether the protector is in a valid state;
- distance;
- the attacking pawn;
- the protector's effective Combat Power;
- whether the threat is human/mechanoid and therefore subject to the configured minimum Combat Power;
- whether a more appropriate protector already has the situation covered.

A starving protector is not expected to defend indefinitely; the defense code has a low-food cutoff.

### Herd/pack participation

For appropriate social animals, protection is not limited to one exact mother. Nearby compatible herd members can participate in protection when the lineage and state checks pass.

This is why protected-young logic also feeds back into predation: a predator may decide that an apparently weak baby is not actually an easy isolated target.

### Retargeting protection

A protector already defending young can switch to a more immediate valid aggressor instead of being locked forever to the first threat that triggered the job.

## 13. Egg clutches have ownership, guarding and incubation

Egg protection is a childcare sub-feature and defaults to enabled.

### Clutch ownership

Zoology records the mother associated with fertilized eggs and maintains ownership information beyond the immediate laying event.

The component can recover ownership from the egg's hatcher parent and maintains records through supported egg-stack changes. This matters because RimWorld can split, merge or move egg stacks after laying.

### Staying near a clutch

A laying female can wander near her clutch. Compatible herd protectors can also be associated with a clutch when the species behavior allows it.

### Active incubation

Supported animals can receive an incubation job for a nearby clutch rather than treating the egg as an inert map object.

The current childcare code searches locally for an incubation target and keeps the incubator at the egg for the configured incubation job duration.

This behavior does not replace the egg's normal hatching comp or its overall days-to-hatch value. It is parental behavior around the egg.

### Defending eggs

A threat to a protected clutch can trigger defensive jobs in the owning/compatible adults.

The same minimum Combat Power setting used for protection from humans and mechanoids applies to this path.

"Do not flee from humans while protecting clutch" defaults to on, so an animal that has actually qualified to defend its eggs does not immediately abandon them through the separate human-flee system.

## 14. Family-aware wild departures

The childcare system also affects ecosystem departures.

When Zoology decides that wild animals should leave an overloaded map, family-aware logic can keep related adults and young together instead of selecting one family member and leaving the rest behind.

A mother actively tied to an egg clutch is protected from ordinary forced-departure selection so population control does not casually break an active clutch-defense state.

## 15. Wild animals reproduce without becoming colony livestock

Default: enabled.

Zoology adds a wild mating job rather than relying only on domesticated-animal mating opportunities.

### Finding a mate

The searching pawn is a fertile male animal. It first searches for a valid female of the same race within a local radius, then checks the race's allowed cross-breed defs.

The female must still pass RimWorld's normal fertile-mate check and ordinary reachability/interaction restrictions.

The custom wild-compatibility path is for cases involving factionless animals. Normal colony/faction animal reproduction is not replaced wholesale by this job.

### Ecosystem capacity

Default: enabled.

Wild-to-wild mating can be paused when the map is already at or above its allowed ecosystem weight.

Zoology does not use a fixed global animal count. It derives the desired capacity from:

- map area;
- the tile's animal density;
- the current biome animal commonalities;
- whether the season is suitable for those animals;
- game-condition animal-density modifiers;
- Biotech pollution when Biotech is active.

Current population pressure is the sum of ecoSystemWeight for the wild animals actually on the map.

The default allowed limit is 1.2 times the calculated desired weight. The setting range is 1.0-3.0.

### Pollution

With Biotech, heavy pollution reduces the ecosystem capacity used by this system rather than being ignored. The calculation uses Zoology's pollution-to-animal-density curve on top of the other density inputs.

### Forced departure

Default: enabled.

When the ecosystem remains overloaded, Zoology can make random wild animals leave the map. This is population relief, not instant deletion: animals are placed into map-exit behavior subject to the family and clutch safeguards above.

## 16. Pet recreation gives colonists something to do with pets

Default: enabled.

Pet recreation is a real joy system rather than a cosmetic animation.

### Who counts as a pet

A candidate animal must:

- be a player animal;
- have race petness greater than zero;
- be at or below the configured maximum wildness;
- be alive, spawned and not downed;
- not be in a mental state;
- have sufficient consciousness and movement;
- be idle and available rather than already busy with an urgent basic need.

Default maximum wildness: 0.2.

### Colonist requirements

The colonist must be an active player colonist, mobile and conscious enough for the activity and not about to satisfy a more urgent basic need.

Outdoor pet activities additionally require the current conditions to be enjoyable outdoors.

### Canine activities

Canines can participate in:

- walks;
- fetch.

Walks search for an outdoor destination and build a safe path for the pair.

Fetch uses the canine-specific pet jobs and movement around a chosen play location.

### Other pets

Other eligible pets can use the general play-with-pet activity, including local toy/chase behavior.

Toy cells must be safe, standable, visible and reachable. The system also avoids forcing a pet through an obviously inappropriate roof transition when the activity starts under cover.

### Distance and bonds

The recreation search uses a maximum pet distance of 30 cells.

Beginning a pet-play session can also attempt to form a bond between the colonist and animal. That is in addition to Zoology's broader optional bonding rules.

## 17. Expanded bonding

Zoology offers three practical bonding modes.

### Vanilla

Uses RimWorld's ordinary bonding eligibility.

### Expanded pet bonding

This is the default Zoology mode.

Eligible pets can bypass the ordinary trainability restriction in Zoology's supported bonding paths while still respecting the other relationship checks.

### Expanded all-animal bonding

Extends that trainability bypass beyond the petness-limited group to eligible animals generally.

### Nuzzling

Expanded bonding also hooks nuzzling: an eligible nuzzle interaction can attempt a bond in addition to vanilla's normal relationship behavior.

The setting changes who is eligible for the bonding check. It does not make every interaction guarantee a bond.

## 18. Direct control of trained animals

Default: enabled.

Zoology provides Beastmastery-based direct control for eligible player animals.

When the required training exists, the player can draft the animal and give direct movement/attack commands rather than relying exclusively on Follow master and Release.

Important restrictions include:

- player ownership;
- a training tracker;
- the required supported training;
- a valid master;
- the master being capable of command;
- the animal being within the master's animal-command range;
- no ordinary direct drafting while the animal is dormant, downed, in deathrest or in an incompatible mental state;
- no new drafting during a ritual.

Zoology also synchronizes compatible Beastmastery/attack training relationships and avoids creating a second drafting path when another supported drafting system already owns the pawn.

## 19. Roamers and trainability

The species editor can mark a race as a roamer.

A roamer:

- receives a configurable roamMtbDays value;
- is forced to Trainability None.

The current editor allows 1-60 days.

A non-roamer can be assigned None, Intermediate or Advanced trainability.

Player-owned roamers have a special close-melee threat correction so the roamer state does not make them ignore an attacker that has just engaged them at close range.

## 20. Small pets are not sensible raid targets

Default: enabled.

Raiders can ignore very small player animals while those animals are behaving as non-combatant pets.

Default small-pet BodySize threshold: 0.45.

The protection is contextual, not permanent invulnerability. A small animal can stop qualifying when it enters combat-relevant behavior, for example by actively following a master into combat or entering a hostile mental state.

"Small pets do not retaliate in melee" defaults to enabled. It prevents a protected tiny pet from defeating the point of the system by automatically joining melee after being attacked.

## 21. NPC groups can have real animal companions

Default: enabled.

Zoology adds selected animal options to standard mixed human pawn groups, but it does not leave those animals as handlerless props.

### Handler validation

A generated companion must have an eligible humanlike handler in the same generated group.

The handler must:

- be capable of the Animals/Handling work;
- have enough Animals skill for the animal's actual minimum handling requirement;
- pass RimWorld's master eligibility rules.

One handler can be assigned at most two tracked companion animals.

### Point preservation

If an animal was selected by the pawn-group generator but no valid handler exists, Zoology removes that animal before the incident reaches the map.

Its selected point cost is returned to the group budget, and Zoology attempts to spend that budget on eligible human pawns from the same group's normal options.

This avoids both broken handlerless animals and raids/trader groups that are silently weaker because an invalid animal simply vanished.

### Companion AI

A tracked NPC companion is not treated as an ordinary human Lord member.

It uses animal-specific follow/defend behavior around its assigned handler.

If the master is lost, Zoology first tries to reassign the animal to another valid handler from the same original generated group.

When the human group withdraws, or no mobile human from the original group remains, the animal transitions out of companion behavior and flees.

### Orphaned companions

"Orphaned NPC animals become wild" defaults to off.

Off: an unreassignable companion panic-flees.

On: it becomes factionless and can remain as a wild/tamable animal.

### Third-party animal options

The generic safety layer is not limited to animal entries added by Zoology. Compatible animal options already present in a standard mixed human group can also benefit from the handler checks.

All-animal and non-human faction groups are intentionally outside this system.

## 22. Wound licking is animal self-tending

Default: enabled.

This feature is not restricted to wild/factionless animals. Any eligible animal can use it.

An animal can lick wounds when it is:

- alive and spawned;
- not downed;
- not in a mental state;
- not in an aggressive mental state;
- carrying a tendable bleeding wound that is on a surface body part.

Internal wounds are excluded: if the injured part is inside the body hierarchy, wound licking does not treat it.

Zoology uses RimWorld's tending utilities to select the wounds that a single treatment should address, applies no medicine, and deliberately uses weak no-skill/self-tend quality assumptions.

It increments normal tending records and marks the animal as self-tended. It is therefore a real low-quality tend, not a hidden regeneration effect.

## 23. Ectotherms use a different cold-injury path

Default: enabled for species marked as ectothermic.

Zoology does not make ectotherms immune to cold.

Instead, it clones the ordinary OrganicStandard hediff-giver set for marked animal races and substitutes the insectoid/ectothermic hypothermia hediff variant where RimWorld's hypothermia giver provides one.

Other organic hediff givers are retained in the cloned set.

Disabling/rebuilding the runtime feature restores the original race hediff-giver sets.

The practical point for a player is that a reptile/invertebrate marked as ectothermic does not use exactly the same cold-injury assumption as an ordinary warm-blooded mammal, while still remaining vulnerable to unsuitable temperatures.

## 24. Animal bionics

Default: enabled.

Zoology extends supported human bionic recipes to compatible animal anatomy during startup.

The patchers check whether an animal actually has the body part required by a recipe. Where an animal uses an alternative corresponding body-part def, Zoology can clone the install/remove recipe for that part rather than simply adding every animal to every human recipe.

Combat-oriented bionic parts have size-dependent animal variants so a tiny animal and a huge animal do not receive the exact same game-facing melee implant.

Animals marked CannotBeAugmented are excluded.

This is a startup DefDatabase modification, which is why changing the bionics setting requires a RimWorld restart to reliably return to a clean definition state.

## 25. Aggression at slaughter

Default: enabled for marked species.

A standing marked animal can react to being designated/slated for slaughter.

The system warns when the slaughter designation is added, and when the slaughterer reaches the animal the normal slaughter action can be replaced by a manhunter response. The slaughter designation is removed and the slaughter job is interrupted.

Downed animals do not trigger this response.

By default the same marker also excludes the animal from peaceful ritual animal roles.

This system is independent of lactation auto-slaughter protection.

## 26. Non-CE size-aware damage reduction

Default: enabled when Combat Extended is absent.

RimWorld's raw melee damage can make extremely small animals or an unarmed human disproportionately effective against much larger animals. Zoology reduces selected natural-attack damage in those size-mismatch cases.

The correction applies only to supported natural pawn attacks.

It does not apply to:

- ranged damage;
- equipped weapons;
- implant/hediff-driven attack weapons;
- unrelated environmental damage.

The correction never increases damage.

Predator status and base/current size are considered so the rule does not simply say "small attacker always weak."

When Combat Extended is active, this system is forced off rather than being stacked on top of CE's armor/penetration model.

## 27. Combat Extended integration

When Combat Extended is active, Zoology loads its CE-specific body and animal combat patches and uses the CE output fields from AnimalStats.

### Separate CE attack values

CE damage, penetration and cooldown values are produced separately from the vanilla combat values. The CE integration is therefore not a conversion of the final vanilla damage number.

### Life-stage penetration

The optional "Override Combat Extended penetration" setting is enabled by Reset when CE is detected.

When active, Zoology applies life-stage penetration factors to animal melee tools and replaces the corresponding CE stat explanation/final display so the shown values match the calculation.

The shipped life-stage penetration defs make babies and juveniles substantially less penetrating than adults.

### Non-CE reduction is disabled

The vanilla size-aware animal damage reduction described above is forced off whenever CE is detected.

## 28. Low-level species behavior features

Several Dev-page systems are normally assigned by species markers or comps. They are exposed to users mainly for compatibility or deliberate customization.

### No Flee

Marked animals can be prevented from ordinary animal flee decisions and supported panic/terror starts.

### Flee from carrier

A marked pawn can make eligible nearby animals flee from the pawn carrying/presenting that threat.

Per-species parameters:

- radius: default 12, editor range 1-60;
- prey BodySize limit: default 0, editor range 0-20; zero means no explicit size ceiling;
- flee distance: default 16, editor range 1-80.

### Cannot be mutated

Prevents marked animals from supported mutation paths.

### Cannot be augmented

Prevents marked animals from supported augmentation paths, including Zoology's own animal bionics.

### Ageless

The ageless comp periodically removes supported age-related hediffs and blocks supported age-heddiff additions.

Default cleanup interval: 6000 ticks. Editor range: 60-120000.

### Drugs immune

The drugs-immune comp blocks/removes supported drug, addiction and tolerance-related hediffs.

Default cleanup interval: 2000 ticks. Editor range: 60-120000.

### Animal clotting

The clotting comp periodically self-tends bleeding injuries.

Default check interval: 360 ticks.

Default tending-quality range: 0.2-0.7.

The editor allows each end of the quality range from 0 to 2 and the interval from 60 to 120000 ticks.

### Animal regeneration

The global regeneration toggle enables behavior for races that already have Zoology's regeneration comp in XML.

The comp can use different regeneration hediffs for baby, juvenile and adult/body-size bands.

The current UI does not provide a per-species regeneration selector.

### No porcupine quill

Marked animals are excluded from the supported porcupine-quill hediff path.

## 29. Automatic technical corrections

A few Zoology runtime patches are not meant to be "features" a player configures, but they can affect a real save.

### Insect cocoon budget safeguard

Biotech only; enabled by default on the Dev page.

If a cocoon has a positive spawn budget but every configured pawn kind costs more than that budget, vanilla can otherwise produce an empty spawn result and consume the cocoon.

Zoology raises that individual cocoon instance's budget to the cheapest valid configured pawn, allowing vanilla to spawn one. Shared defs are not changed by this safeguard.

### Invalid saved think-tree keys

When a saved animal job points to a think-node key that no longer exists in the current think tree, Zoology clears that stale job-giver reference during load.

This is save resilience for changed animal AI trees; it does not rewrite ordinary valid jobs.

### Guinea-pig leather replacement

After defs finish loading, Zoology replaces references to the vanilla guinea-pig leather def with squirrel leather where those ThingDef references occur.

## 30. Settings reference

The settings window has five pages. Defaults below are the current build defaults after Reset; CE-dependent defaults are conditional on whether CE is detected.

### Predator / prey

| Setting | Default | Range / dependency |
| --- | --- | --- |
| Enable prey fleeing | On | Master switch for predator-directed prey fleeing. |
| Predator search radius | 18 | 6-24; shown while prey fleeing is enabled. |
| Flee distance from target predator | 24 | 6-40. |
| Animals flee from non-hostile predators | On | Requires prey fleeing. |
| Non-hostile predator search radius | 12 | 6-24. |
| Flee distance from predator | 16 | 6-40. |
| Enable pack hunting | On | Runtime pack-hunt support. |
| Enable advanced predation logic | On | Enables Zoology's prey acceptance/scoring layer. |
| Enable scavenging | On | Species marker controlled through the configure button. |
| Enable predators defending corpses | On | Enables prey/corpse ownership and defense. |
| Prey protection range | 20 | 10-30. |
| Unowned corpse size multiplier | 5 | 2-10. |
| Defend prey from humans/mechanoids | On | Requires corpse defense. |
| Minimum Combat Power to defend prey | 70 | 0-1000. |

The scavenger species editor also exposes allowVeryRotten.

### Physiology

| Setting | Default | Range / dependency |
| --- | --- | --- |
| Enable mammal lactation | On | Mammal species are configurable. |
| Allow handlers to feed baby mammals | On | Requires mammal lactation; Handling work, direct-orderable, no mechs. |
| Allow slaughtering lactating animals | Off | Requires lactation; controls auto-slaughter exclusion. |
| Enable animal childcare | On | Childcare species are configurable. |
| Young/clutch protection range | 10 | 10-40. |
| Minimum Combat Power to defend young/clutches from humans/mechanoids | 70 | 0-1000. |
| Do not flee from humans while protecting young | Off | Requires childcare + human fleeing. |
| Enable egg protection | On | Childcare sub-feature. |
| Do not flee from humans while protecting clutch | On | Requires childcare + egg protection + human fleeing. |
| Enable wound licking | On | Animal self-tending. |
| Enable ectothermic handling | On | Ectotherm species configurable. |

Turning mammal lactation off also disables handler baby feeding and the lactation auto-slaughter option.

### Combat

| Setting | Default | Range / dependency |
| --- | --- | --- |
| Override Combat Extended penetration | On after Reset when CE is present; Off without CE | Hidden/unavailable without CE. |
| Enable animal draft control | On | Beastmastery/direct-control system. |
| Enable animal damage reduction | On without CE; forced Off with CE | Non-CE natural-attack size correction. |

### Other behavior

| Setting | Default | Range / dependency |
| --- | --- | --- |
| Animals in NPC groups | On | Controls Zoology-added NPC animal options. |
| Orphaned NPC animals become wild | Off | Changes unreassignable companion outcome. |
| Pet recreation | On | Enables Zoology pet joy givers. |
| Maximum pet wildness | 0.2 | 0-1. |
| Expanded pet bonding | On in default bonding mode | See bonding modes above. |
| Expanded all-animal bonding | Off in default bonding mode | Broader bonding scope. |
| Custom flee danger | On | Enables BodySize thresholds. |
| Safe predator BodySize | 0.7 | UI slider 0-30. |
| Safe non-predator BodySize | 3.0 | UI slider 0-30. |
| Ignore small pets by raiders | On | Non-combatant tiny-pet protection. |
| Small-pet BodySize threshold | 0.45 | UI slider 0-30. |
| Small pets do not retaliate in melee | On | Requires small-pet handling. |
| Animals flee from humans | On | Species list configurable. |
| Human search radius | 12 | 6-24. |
| Flee distance from human | 16 | 6-40. |
| Wild animal reproduction | On | Wild mating job. |
| Limit wild reproduction by ecosystem | On | Requires wild reproduction. |
| Force wild animals to leave on overload | On | Ecosystem overload relief. |
| Ecosystem limit factor | 1.2 | 1-3. |
| Configure roamers | — | Per species; roam interval 1-60 days, non-roamer trainability None/Intermediate/Advanced. |
| Human bionics on animals | On | Startup-sensitive; restart after changing. |
| Aggression at slaughter | On | Species configurable. |

### Dev

| Setting | Default | Notes |
| --- | --- | --- |
| Disable all runtime patches | Off | Diagnostic master switch; does not undo already-applied startup Def mutations. |
| Insect cocoon spawn safeguard | On | Biotech only in effect. |
| Cannot be mutated | On | Species configurable. |
| Cannot be augmented | On | Species configurable. |
| No Flee | On | Species configurable. |
| Flee from carrier | On | Species configurable with radius/body-size/distance parameters. |
| Flying flee start | On | Supported flying-animal flee path. |
| Gender-restricted attacks | On | Enables restrictedGender tool handling. |
| Cannot chew | On | Species configurable. |
| Ageless | On | Species configurable; cleanup interval editable. |
| Drugs immune | On | Species configurable; cleanup interval editable. |
| Animal regeneration | On | Global behavior toggle; no current species selector. |
| Animal clotting | On | Species configurable; interval/quality editable. |
| No porcupine quill | On | Species configurable. |

## 31. Runtime changes versus restart-required changes

Most runtime toggles are designed to take effect immediately.

When a relevant setting changes, Zoology can:

- reapply per-species Def extensions/comps;
- rebuild the appropriate Harmony patch set;
- update dynamic availability such as pet recreation.

Per-species override dialogs also write their changes directly.

The main current exception is animal bionics. Bionic recipe expansion mutates DefDatabase during initialization, so a restart is required to guarantee a clean before/after state.

For heavily modded games, a restart after changing low-level Dev compatibility settings is still the safest diagnostic procedure even when the patch itself supports live rebuilding.

## 32. Compatibility

Zoology has dedicated loaded patch sets for:

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

Biotech and Odyssey have their own conditional DLC patch folders.

Animals Are Fun Continued is declared incompatible because its animal-interaction/recreation systems overlap Zoology's pet systems.

Zoology also contains generic runtime interoperability for supported external animal drafting and optional health-system behavior. Those branches are intended to prevent duplicated/conflicting behavior rather than expose a second user-facing integration feature.

## 33. Troubleshooting behavior conflicts

If one AI behavior looks wrong in a heavily modded game, disable the smallest relevant Zoology feature first.

Examples:

- animals abandoning food: inspect prey/human fleeing and corpse-defense interactions;
- babies not nursing: verify mammal assignment, lactation, mother nutrition and reachability;
- handlers not feeding babies: verify Mammal lactation and Allow handlers to feed baby mammals, Handling work, Manipulation and suitable baby food;
- protected young causing unexpected aggression: inspect childcare/egg protection and the Combat Power thresholds;
- tiny pets entering combat: inspect small-pet retaliation and whether the pet is actively participating in combat;
- animal drafting conflicts: test Zoology draft control against the other drafting system;
- CE melee display/penetration disagreement: test the CE penetration override.

The Dev master switch is a last-resort diagnostic control. It is not meant to convert Zoology into a pure XML-stat mod during ordinary play, because the static generated animal patches remain loaded even when runtime Harmony systems are disabled.

For XML/framework behavior and the spreadsheet pipeline, see [FRAMEWORK.md](FRAMEWORK.md). For regeneration and patch-generation tooling, see [CHECKER.md](CHECKER.md).
