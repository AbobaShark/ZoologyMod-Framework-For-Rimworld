# Zoology Player Guide

This guide describes the currently implemented Zoology features in RimWorld 1.6/1.7. Some buildings are available only in RimWorld 1.7.

Zoology combines species-specific animal biology with predation, reproduction, parental care, feeding, husbandry, combat and interactions with people.

The settings reference is at the end of the guide.

## Animal biology and development

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

Biological measurements are converted into game-facing quantities so that encounter budgets, hunting, melee combat and animal husbandry remain playable.

### Anatomy and attack tools

Animal anatomy and natural attacks reflect the species' body plan.

Melee tools are also species-specific. A bite, horn strike, claw swipe, kick or other attack can have its own damage, cooldown, selection weight and, when Combat Extended is present, its own penetration values.

Sex-restricted melee tools expose sexually dimorphic attacks only to the appropriate sex.

### Life stages are mechanically different animals

Zoology gives babies and juveniles distinct life-stage factors for body size, health, hunger, movement, armor and melee performance, and it uses separate life-stage factors when behavior systems compare Combat Power. A juvenile predator, a newborn prey animal and an adult of the same species can therefore make different decisions because their effective body size and combat strength differ.

### Daily animal growth

With **Dynamic animal growth** enabled (default: on), young animals gradually increase in size and develop their species-specific characteristics each biological day, including their health, hunger, armor, movement and natural attacks. With growth disabled, these characteristics change at life-stage transitions.

### Sexual dimorphism

With **Sexual dimorphism** enabled (default: on), supported species have distinct male and female adult sizes, appearance and characteristics. Differences develop during the juvenile-to-adult interval when dynamic growth is active. The two settings can be configured independently.

### Flight development

Flight availability depends on biological age in supported flying species. Hatchlings of species that develop flight later acquire the ability as they mature.

### Automatic young-animal graphics

**Automatic juvenile sprite scaling** is a separate enabled-by-default option. Zoology scales baby and juvenile graphics using their body masses relative to adults. Female-specific textures are used when the animal has separate artwork; otherwise a common texture is scaled to the animal's size. Dynamic growth applies its day-by-day visual interpolation separately.

### Pregnancy and animal products

Species-specific husbandry characteristics include:

- pregnancy increases hunger progressively through its stages;
- egg-laying intervals and clutch sizes are species-specific;
- fertilized egg incubation times are species-specific;
- milk yield and milking intervals are adjusted for supported mammals;
- wool/shearing data are adjusted for supported animals and integrations;
- meat yields are derived from species data;
- leather market, protection and insulation values are rebalanced;
- chitin and other non-mammalian materials are handled separately where the animal data require it.

### Biomes and ecology

Supported animals receive revised biome commonality and ecological placement. Zoology patches Core biomes, Odyssey biomes and compatible third-party biome/animal mods.

The same ecological data also matter to Zoology's wild-reproduction capacity system: the game can have a biome that supports many animals in one season and substantially fewer in another.

Wild population management also includes an independent, optional per-species cap (30 by default, configurable from 10 to 100). The cap counts wild animals of each race on the map, not owned animals. Wild breeding and routine wildlife spawning avoid species at capacity. Over-limit populations are encouraged to leave using the existing family-aware departure jobs; dependent young do not leave their mother behind. When the total ecosystem-weight limit is exceeded, departure favors species that contribute most to the excess instead of picking a uniformly random animal. Scripted and quest-related spawns are not rejected by the cap.

### Natural spawns of water-dependent species

Natural wild animal spawning responds to inland water, including
rivers, lakes, ponds, and marsh terrain. Ocean water is excluded. The size
and connectivity of all inland water areas are evaluated together.
Smaller water-dependent animals can spawn near smaller ponds, whereas
giant crocodilians and hippopotamuses require substantial connected
water habitats. Requirements use the final in-game body size and
ectothermic classification, not a fixed common water-cell threshold.

On very wet maps, water-dependent wildlife can gain up to three times
its usual spawning selection weight; ordinary land wildlife can fall
to one-third of its usual weight. Only animals included in a biome's
coastal spawn pool are exempt. Species in pollution spawn pools are
also weighted according to inland water habitat.
Normal biome eligibility, temperatures, group sizes and other
game spawning restrictions still apply. Incidents, quests, and
explicitly spawned animals are unchanged.

### Cross-breeding and related species

Zoology adds selected cross-breeding relationships where the underlying animals are treated as compatible. In Core, the shipped patches include Arctic/Timber wolf compatibility and, when the relevant expanded-animal package is absent, Labrador/Husky compatibility.

Compatible species can also nurse related cross-bred offspring and mate in the wild.

### Other animal characteristics

Selected animals have revised graphics, sounds, day/night activity and caravan roles.

Outlander and tribal traders can use horses and donkeys as pack animals.

## Predation is based on the actual predator and prey

Zoology extends prey selection with size, health, life stage and danger checks after RimWorld determines that a target is legal food.

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

Predator-on-predator hunting uses a stricter combat-power requirement than hunting ordinary prey, and targets that are too dangerous are rejected before the hunt begins.

This is one reason young life stages matter: a juvenile predator may be an acceptable target where a healthy adult of the same species is not.

### Pack hunting

Default: enabled.

Pack hunting provides additional support when a herd/pack predator encounters prey that would be too dangerous for one hunter. Nearby supported pack participation contributes additional combat strength to prey-selection and pack-hunt decisions around the selected prey.

### Hunting strategies

Different predators hunt in different ways. A wolf relies on pursuit, a cougar tries to get close before its prey reacts, and an alligator can exploit the water to surprise animals on the shore. The effectiveness of these strategies depends on the actual individuals involved: a young, injured or exhausted predator may be much less capable than a healthy adult.

| Strategy | How the animal hunts | Examples |
| --- | --- | --- |
| Pursuit | Approaches and chases prey directly, relying on movement speed and combat ability. | Timber and arctic wolves, red foxes, grizzly bears |
| Land ambush | Tries to approach undetected before rushing into melee range. | Cougars, panthers, lynxes, cobras |
| Water ambush | Uses water for concealment and considers whether prey can be caught on land or near the shore. | Alligators and bullfrogs (Odyssey); crocodiles (Vanilla Animals Expanded) |

#### Stalking and the final attack

A stalking predator tries to reach a position close to its chosen prey before launching the attack. A very slow target may be approached directly, whereas a fast or flying target encourages a closer stealth approach. The presence of a nearby tree or burrow that the prey can use also influences how close the predator needs to get.

While stalking, the hunter may be hidden from potential prey. Detection is more likely for a large, moving predator at close range with a clear line of sight. A motionless hunter is less conspicuous, and stalking ectotherms are harder to detect. Sleeping or downed animals do not act as alert observers.

Nearby alert members of the intended prey's species can detect the hunter and warn their group. Unrelated animals can notice the same predator and flee independently. Prey that recently escaped a hunter can remain alert to it.

Once detected, the predator may continue the hunt as an ordinary chase. If the prey is already much faster or has escaped into a tree, the predator may abandon the attempt instead. For a successful hidden approach, the hunter retains its concealment into the initial attack.

#### Water ambushes

A water-ambushing predator can remain concealed from animals on dry land while it is in water. Swimming ability, land speed and the location of the prey matter. A fast terrestrial animal far from water is a poor target for a slow water specialist; prey at the shoreline or in water may be much easier to catch.

Animals that reach the water are therefore not automatically safe. Water can be an escape route from a land hunter and a dangerous place when an aquatic predator is nearby.

#### What this means for your colony

Treat different predators as different hazards. A visible wolf on open ground may give its prey time to run; a stalking cat or a concealed alligator can get much closer before an attack. Keep vulnerable livestock away from dangerous shorelines and pay attention to the current activity of nearby predators.

### Chase limits

An unsuccessful hunt consumes time and leaves the predator hungry. Zoology tracks individual pursuits and can terminate a chase when the hunter has failed to reach melee distance within the allowed time. Ambush hunters have a shorter pursuit limit than direct chasers.

A fleeing animal that is more than 50% faster than its pursuer in the relevant terrain can be recognized as impractical to catch. Injury, flight and the terrain can change that assessment. An abandoned pursuit temporarily discourages the same predator from selecting the same prey again.

### Protected young affect prey choice

The childcare system feeds back into predation. A baby or juvenile with a credible nearby protector receives that protection in the prey evaluation and can be penalized or rejected as a target.

That does not make young completely immune. Protection depends on the actual protector, threat and childcare settings.

## Prey fleeing and threat awareness

### Stalking, herd awareness and escape routes

With **Advanced threat avoidance** enabled, an animal can choose an escape route suited to its species rather than simply running across open ground. Squirrels and raccoons can climb trees; burrowing species such as rats and foxes can hide underground; suitable swimmers can retreat into water.

Refuges are selected in response to credible danger. A sleeping, incapacitated or undetected predator does not automatically send nearby animals into hiding. A much faster animal can often continue ordinary escape behavior instead of hiding from a slower predator. A predator that is actively attacking or has approached within striking distance can still provoke a refuge response.

#### Climbing trees

Suitable animals can climb a sufficiently grown, accessible tree. Both the animal's current BodySize and the space remaining in the crown matter. Several small animals can occupy one tree if its canopy has room. A tree already occupied by a nest has less space available.

While at canopy height, the animal is protected from melee attacks by creatures that cannot climb that tree or fly. Climbing or flying predators and ranged attacks remain dangerous. An animal can also sleep in a tree or lick its wounds while sheltering there.

A hiding animal reassesses nearby danger before descending. It normally waits until the area has remained clear for three consecutive checks (180 ticks). Hunger below 20% or a threat capable of reaching the animal can make it descend and attempt a ground escape. An animal sleeping in a tree can descend when rested.

To inspect a perched animal, select its visible sprite in the canopy or select the tree and use the selection commands for its occupants. A selected perched animal or tree nest also provides a command to select the supporting tree. Cutting down a tree removes the refuge and destroys any attached nest.

#### Burrows and escape into water

Animals can use accessible burrows with suitable entrances and sufficient room. Some animals dig their own burrows during safe periods and later use them for rest or emergency shelter. Underground refuges can be shared, but their occupants may be vulnerable to specialist digging predators.

An animal that moves particularly well in water may flee toward water instead of running away on land. Conversely, a land animal in water can seek a safer dry escape route. The relative movement abilities of the prey and its pursuer influence which escape route is useful.

**Advanced threat avoidance** controls these specialized escapes. The predator-fleeing and human-fleeing options control which ordinary encounters provoke flight.

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

Prey can therefore maintain distance before a formal PredatorHunt job starts.

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

Zoology can replace part of RimWorld's animal danger check with race base-BodySize thresholds for player animals on a player home map. The default safe thresholds are:

- predator base BodySize: 0.7;
- non-predator base BodySize: 3.0.

These are RimWorld BodySize values, not kilograms. Player animals at or above the applicable threshold are treated as safe from the ordinary flee-danger response on the home map.

### Flying flee start

The Dev feature "Flying flee start" enables Zoology's special flee-start handling for supported flying animals so their escape behavior can begin through the appropriate flight path.

## Predators can own and defend a kill

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

A predator that has committed to defending food may temporarily stop using ordinary flee behavior while contesting the protected corpse.

### Defense warnings

When animals become aggressive toward a colonist or another colony member while defending a kill, young, eggs, a nest or a family den, the game warns you. Defense by several animals together is reported as a major threat.

## Animal food selection and foraging

**Advanced animal food selection** (on by default, under Other behavior) gives animals a broader choice of ways to obtain food. An animal can pass up a nearby but unrewarding option for a more useful one. The preferred food is not necessarily the same for every individual, even within one species.

### How animals choose food

Food must be edible for the species and life stage, accessible, and suitable under the normal feeding restrictions. Among available alternatives, Zoology considers the value of the food and how difficult it is to obtain.

| Food source | What makes it attractive |
| --- | --- |
| Live prey | Suitable prey size, low danger, vulnerability, a favorable pursuit and the ability to consume the prey |
| Unclaimed carcass | Edible meat that can be reached without catching and killing a living animal |
| Raw food | Suitable available meat, fruit or other raw food; favored over prepared meals when access is comparable |
| Plant harvest | A useful edible crop yield relative to the harvesting effort and the animal's food capacity |
| Fishing (Odyssey) | Accessible fishing water and sufficiently fast fishing for the animal |
| Shelter raid | Edible eggs, prey or carrion worth the effort of accessing a nest, burrow or grave |
| Wild foraging (Odyssey) | A small nutritious supply of local biome food relative to the gathering effort |

The predator-specific comparisons use **Advanced predation logic** together with advanced food selection. For a hunting predator, a vulnerable nearby prey animal may be more attractive than a distant carcass; a free carcass can be more attractive than a dangerous chase. A guarded carcass can provoke aggression, so another reachable food source may be the better choice.

The preference for raw food applies to both wild and owned animals. Keep suitable animal food available separately from colonist meals. Animals can still eat prepared meals if those are suitable for their diet and the available feeding options favor them.

### Plant harvesting

Omnivorous animals that can eat both meat and vegetables or fruit may harvest edible products from mature plants. This lets a suitable animal obtain berries or another harvestable crop rather than simply eating the plant itself.

The estimated edible yield depends on plant maturity and condition. Harvesting also costs time: the game uses the work speed and yield associated with a Plants skill of 5 as the productivity reference. A plant that yields more food for the same amount of work is more attractive than a damaged or low-yield plant. The same harvest can be a substantial meal for a small animal and a poor investment for a large hungry bear.

Animals harvest edible products, so crops accessible to wildlife may be lost to animal feeding. Fences and appropriate allowed areas can help control access by colony animals.

### Fishing (Odyssey)

Animals able to fish can compare a fishing trip against accessible prey, carrion and other food. Fishing requires a usable body of water containing fish. Fishing Speed determines the work required after the animal reaches its target. A proficient fisher near water can spend less time obtaining its food than a predator facing a difficult chase.

Fishing, plant harvesting and wild foraging each use their own work and food-value estimates. No fixed order makes one of these activities the best food source for every animal.

### Wild foraging (Odyssey)

**Wild animal foraging (Odyssey)** allows hungry, factionless animals whose species supports the Forage ability to gather local food. The current biome must supply foraged food that the animal can eat. Suitable animals can use this ability without a player training them.

A successful gathering job takes 240 ticks of work and produces 2–5 units of the biome's food. It uses the expected nutrition relative to the animal's food capacity, gathering effort and travel distance to compete with other feeding options. A small animal can obtain a useful portion from the same amount of food that barely feeds a large animal.

Wild foraging requires Odyssey and advanced food selection. Both options can be changed during play.

### Raiding shelters and graves

Some food is protected by a natural structure rather than lying in the open. A sufficiently hungry predator may choose to access that food, provided its abilities and the target's suitability allow it.

- **Burrows:** predators capable of digging, such as foxes, can excavate a burrow to reach suitable prey. The prey must still satisfy the predator's size and danger restrictions. A mother's protection of dependent young can make a family den a poor target.
- **Nests:** predators that can reach a nest can attack it to obtain edible eggs or hatchlings. A tree nest is harder to access for a ground-bound animal.
- **Graves:** sufficiently large animals capable of excavating soil (current BodySize of at least 1) can dig up edible corpses from ordinary graves. Grizzly bears are among the animals able to excavate. A sarcophagus is not an ordinary grave and does not provide this food source.

Breaking into a shelter takes time, and prey inside may be guarded by a parent. A hungry animal can therefore prefer ordinary food, hunting or fishing if that offers a better opportunity.

Keep vulnerable livestock and burial sites away from animals that can reach them. An occupied natural refuge offers protection, not guaranteed immunity from predation.

## Constriction and venom

Some animals use specialized natural weapons to disable or kill other creatures. These abilities matter both during wildlife encounters and when a colonist or owned animal fights the species.

### Constriction

Constrictors such as the anaconda from Vanilla Animals Expanded can seize prey following a successful nearby natural attack. The prey needs compatible anatomy and must be within the snake's maximum constrictible BodySize. The standard limit is the snake's own current BodySize. A smaller victim is easier to seize than a large one.

The chance of grabbing a suitable target after an eligible attack depends on the relative sizes of the snake and its victim. For two animals of equal BodySize, the chance is 50% during hunting. Constriction is much less likely during ordinary combat outside a hunt.

Once the snake takes hold, both animals occupy the same cell and the victim is immobilized or severely restricted. Its Health tab shows the **constriction** condition, which progresses through circulatory and respiratory impairment.

| Constriction severity | Effect |
| --- | --- |
| Initial hold | Movement capacity capped at 15%; the hold restricts escape. |
| 25% | Blood pumping and breathing become impaired. |
| 55% | Severe circulatory impairment, reduced breathing and consciousness. |
| 85% | Critical, life-threatening impairment. |
| 100% and above | Circulatory collapse; continued constriction can kill the victim. |

With the standard constriction profile, severity rises by 2.5 percentage points per real-time second at normal game speed. For ectothermic prey the standard rate is one quarter as fast. A conscious victim can periodically struggle free, with its relative size and consciousness affecting its chance.

Attacking the constrictor can break its hold. More damaging hits relative to the snake's health scale are more likely to force a release. Killing or removing the snake also releases the victim. If a colonist or valuable animal is caught, intervening against the snake is therefore more useful than waiting for the victim to walk away.

A predator that kills its prey by constriction can claim the carcass as food.

### Venom

Venom is delivered by designated natural attacks, including bites, stings and certain specialized spurs or other weapons. An attack must penetrate and cause injury for its venom to take effect. The delivered dose depends on the attack's venom profile, the penetrating damage and any applicable size or resistance scaling.

A victim with a compatible biological anatomy can develop an envenomation condition in its Health tab. Different venoms affect different organs and functions. Incapacitation and death can occur after the original attack rather than immediately during combat.

#### Venom types based on real animals

| Animal | Venom type | Main effects | Matching antivenom |
| --- | --- | --- | --- |
| Cobra (Core) | Neurotoxic | Progressive weakness, impaired movement and manipulation, respiratory paralysis | Cobra |
| Rattlesnake (Vanilla Animals Expanded) | Hemotoxic | Bleeding, clotting disturbances and circulatory impairment | Viper |
| Monitor lizard (Odyssey) | Varanid | Clotting and cardiovascular disturbances | — |
| Gila monster (Vanilla Animals Expanded) | Gila | Pain, swelling, weakness and reduced consciousness in severe cases | — |
| Male platypus (Vanilla Animals Expanded) | Platypus | Severe pain, swelling and reduced movement and manipulation | — |
| Megascorpion (Vanilla Animals Expanded) | Scorpion | Pain, weakness and progressive neuromuscular impairment | Scorpion |

Zoology also supports venomous creatures from **Alpha Animals**, with additional venom profiles, physiological effects and corresponding antivenoms where applicable.

#### Treating envenomation

Inspect the victim's Health tab to identify the venom and current severity. Venom generally progresses before immunity develops; tending reduces progression and can help the patient recover. Severe venom can become lethal before the patient's immunity catches up, especially after repeated attacks.

The correct antivenom removes the corresponding active venom condition when taken. Mismatched antivenom does not treat that condition. Ordinary injuries and other medical conditions still need appropriate care.

#### Obtaining and crafting antivenom

Designated venom-producing animals provide a venom sample when butchered at a reproductive life stage. For example, a rattlesnake yields the sample used for viper antivenom.

Research **Medicine production** and use a drug lab to craft the appropriate antivenom. Each recipe consumes two herbal medicines and one matching venom sample, and uses Intellectual work. Stock the relevant types if venomous wildlife is common near the colony.

## Scavengers are adapted to carrion

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

The scavenger species editor includes an option to allow consumption of desiccated remains; it is off by default.

When it is enabled, a scavenger can use desiccated remains that would otherwise be rejected. Desiccated carrion is deliberately poor food: the scavenging code reduces its effective nutrition to 10% of the corpse's ordinary nutrition.

### Corpse consumption

Scavengers still consume the corpse through RimWorld's body-part ingestion model. Zoology does not turn a skeleton into a full fresh carcass or silently restore missing nutrition.

## Animals that cannot chew must swallow prey whole

Default global feature: enabled for marked species.

ModExtension_CannotChew represents animals whose feeding strategy cannot reasonably use RimWorld's generic "take bites from any corpse" behavior.

### Prey-size limit

Advanced predation scales the maximum swallowable prey size to the individual's current BodySize relative to the species' average adult. Sex, age and growth therefore affect which prey can be swallowed.

For a non-adult animal marked `CannotChew`, the scaled limit is also capped at its current BodySize. A baby that can only swallow prey whole therefore cannot swallow adult-sized prey simply because its species has a large adult limit.

### Where the limit is used

The same rule is used in:

- prey acceptability;
- corpse feeding;
- scavenging.

A corpse that is too large is not made legal just because the eater is a scavenger.

## Mammal reproduction is followed by real nursing behavior

Default: enabled for species marked as mammals.

Zoology's mammal system is designed so a newborn mammal does not immediately behave like a miniature adult herbivore or carnivore.

### Birth starts lactation

When a supported female mammal gives birth, Zoology applies the lactating hediff and associates the newborns with the birth context where possible.

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

Either side can therefore initiate nursing when its own think tree is evaluated first.

## Handlers can feed hungry mammal babies

Default: enabled while mammal lactation is enabled.

### What handlers do

A colonist assigned to Handling can bring suitable baby food to a hungry player-owned mammal baby.

The work:

- uses the Handling work type;
- is deliberately lower priority than the core animal-handling jobs above it;
- requires Manipulation;
- cannot be performed by mechs;
- can be given as a direct manual order.

The custom job driver feeds the baby where it is standing or lying; medical-bed or resting status is not required.

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

Otherwise the handler finds suitable baby food on the map.

The food must be suitable both for infants and for the species' diet.

Handler feeding provides suitable food when the mother is absent, inaccessible, no longer lactating or temporarily unable to nurse.

The handler-feeding option is subordinate to the main Mammal lactation setting. Turning lactation off suspends handler feeding but preserves the saved choice, which takes effect again when lactation is re-enabled. Switching handler feeding takes effect immediately and does not disable nursing by mothers.

## Mammal babies use baby food rules

While mammal lactation is active, Zoology changes ordinary food suitability for mammal babies.

A food must be marked as ingestible by babies and must also be a food the animal's race can ever eat.

The baby-specific logic also blocks the fishing job for mammal babies.

If no nursing interaction happens, a baby can still use suitable ordinary baby food. Handler feeding uses the same baby-food suitability rules.

### Infant training

Training maintenance is suspended for mammalian infants while the lactation system is active.

## Mammal babies in caravans

The nursing system also runs in caravans, where normal map jobs do not exist.

For a mammal baby in a caravan, Zoology:

1. recognizes the baby as a mammal infant;
2. ensures the biological mother is lactating when the mother and baby are both present and compatible;
3. looks for a compatible lactating feeder in the caravan when the baby wants milk;
4. transfers nutrition from that feeder if possible;
5. falls back to suitable caravan inventory food when necessary.

Caravans therefore preserve the newborn feeding model without map jobs.

## Lactation and auto-slaughter

"Allow slaughtering lactating animals" defaults to off.

When it is off, auto-slaughter excludes lactating females from herd limits and slaughter selection.

This protection is aimed at automatic herd management. A deliberate manual slaughter designation is still possible; the player receives a warning about the dependent young.

This system is separate from the "Aggression at slaughter" feature. A species can have either, both or neither behavior.

## Childcare keeps young associated with adults

Default: enabled for species marked with Zoology's childcare extension.

The childcare system covers both ordinary young animals and egg-laying species.

### Babies and juveniles stay near their mother

Young animals can use a dedicated wander behavior rooted near the mother instead of wandering as if they were independent adults.

The wander behavior uses a short local radius around the mother.

Zoology first tries to use the actual parent relation. If that relation is unavailable, it can reuse a mother observed by the birth/childcare systems, and as a fallback it can infer a nearby compatible adult female of the same species lineage and factional context.

Dependent player-owned babies follow their biological mother's allowed area while they remain together. The Animals tab displays the inherited area. A sleeping baby wakes to follow its mother if she moves too far away and can be reached.

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

Nearby compatible protectors also contribute to predation decisions involving the young they guard.

### Retargeting protection

A protector already defending young can switch to a more immediate valid aggressor instead of being locked forever to the first threat that triggered the job.

## Egg clutches have ownership, guarding and incubation

Egg protection is a childcare sub-feature and defaults to enabled.

### Clutch ownership

Zoology records the mother associated with fertilized eggs and maintains ownership information beyond the immediate laying event.

The component can recover ownership from the egg's hatcher parent and maintains records when RimWorld splits, merges or moves supported egg stacks after laying.

### Staying near a clutch

A laying female can wander near her clutch. Compatible herd protectors can also be associated with a clutch when the species behavior allows it.

### Active incubation

Supported animals can receive an incubation job for a nearby clutch. The childcare code searches locally for an incubation target and keeps the incubator at the egg for the configured incubation job duration.

Incubation restores egg hit points.

### Defending eggs

A threat to a protected clutch can trigger defensive jobs in the owning/compatible adults.

The same minimum Combat Power setting used for protection from humans and mechanoids applies to this path.

"Do not flee from humans while protecting clutch" defaults to on, so an animal that has actually qualified to defend its eggs does not immediately abandon them through the separate human-flee system.

## Reproduction shelters, aquatic development and husbandry

### Tree and ground nests

With **Advanced reproductive behavior** enabled, nesting birds can prepare a natural nest before laying their eggs. The kind of nest follows the species' reproductive strategy and normal habitat.

| Nesting strategy | Examples | Hatchling behavior |
| --- | --- | --- |
| Tree nest, dependent (altricial) young | Sparrows, bluebirds, crows and macaws (Odyssey) | Chicks remain in the nest and receive food from parents. |
| Ground nest, mobile (precocial) young | Chickens, ducks, geese, ostriches; quail (Odyssey) | Hatchlings leave the egg and can move and feed independently. |

#### Building and laying

A bird looks for a suitable reachable nest location. Tree-nesting birds need an appropriate living tree, and ground-nesting birds use a suitable site on the ground. A nest can be prepared before the female's eggs are fertilized. Only fertilized eggs are accepted into the nest. If the bird cannot find a usable nest or build one, it can lay its eggs outside.

Tree nests appear in the crown and follow the tree's swaying movement. Select the nest itself to inspect its contents or select the supporting tree to access its attached nests.

#### Incubation and parental care

The nest holds the eggs, which develop according to their species' normal incubation period. Adults with egg care can incubate and defend them. Incubation repairs damaged eggs while the normal hatching progress continues.

For altricial species, hatchlings can remain inside the nest until the next life stage. Both parents can feed their dependent offspring if they are mobile and have enough food. A parent with at least 55% of its food need filled can go to feed a chick whose food level is below 55%. Feeding transfers actual nutrition from the parent, leaving the adult with at least 35% of its food need. If several chicks are hungry, the available nutrition is shared according to their needs.

Precocial hatchlings are not kept inside the nest for this feeding stage. They can begin moving and obtaining suitable food after hatching, even if their flight ability is still developing.

#### Protecting nests

Predators may attack accessible nests to reach eggs or chicks. Both parents can come to the defense of their offspring if they are nearby and able to fight. Protecting a breeding area and its food supply is particularly useful for tamed nesting birds with dependent young.

Nests are natural structures. They gradually deteriorate after becoming empty, and a tree nest depends on its supporting tree. If a nest is destroyed or the tree is cut down, eggs and surviving young are released. A natural nest cannot be deconstructed as an ordinary player building.

### Shared burrows and maternity dens

Burrows and dens serve several purposes: resting, escaping from predators, raising dependent young and, for certain species, spending the winter underground.

Ordinary refuge behavior requires **Advanced threat avoidance**. Family denning requires both **Advanced threat avoidance** and **Advanced reproductive behavior**. Winter denning has its own seasonal setting.

#### Creating and sharing a shelter

Capable animals dig natural shelters on suitable ground. Foxes and rats are examples of burrowing species; wolves and bears can use family dens. They can also enter a compatible existing refuge instead of digging a new one.

The shelter's entrance size is based on the animal that built it. Its footprint and total capacity depend on the builder's current BodySize:

| Builder BodySize | Shelter footprint |
| --- | --- |
| Below 0.5 | 1 × 1 |
| 0.5 to below 2 | 2 × 2 |
| 2 or greater | 3 × 3 |

The adult occupancy budget is the entrance BodySize multiplied by the number of cells in the footprint. A small animal may therefore be able to use many more existing shelters than a large one. A family den also accommodates dependent young associated with its mother.

Select the shelter to see its occupants and capacity. Natural shelters gradually lose hit points when unused, and occupied shelters can be maintained. Animals use them for sleep and may seek them when threatened.

#### Maternity dens

Suitable mothers prepare a den for their offspring. After birth they can carry dependent babies into it, where the young are protected while they grow. The mother may nurse while inside or return to the entrance to feed her offspring. A hungry infant can receive milk without forcing its mother to abandon the shelter.

When the mother needs food, she can leave the den and obtain it. Young that reach the juvenile stage can emerge and begin following or feeding outside.

A mother can defend her family den against a predator attempting to dig it open. An attack on the shelter can provoke defense from the beginning of excavation rather than only when the predator reaches a baby.

#### Leaving the refuge

Animals leave an ordinary refuge when hunger drops below 20%, when they have rested and the threat has passed, or when sufficiently urgent medical or reproductive needs require them to emerge. Winter-dormant occupants follow their seasonal exit rules. Destroying a shelter releases surviving occupants.

For colony management, inspect the natural shelter before assuming an animal has disappeared. The occupants are still living animals with their own age, hunger and health.

### Marsupial pouches

With **Advanced reproductive behavior** enabled, pouch-bearing mammals such as kangaroos and koalas from Vanilla Animals Expanded keep their dependent young in a maternal pouch.

A newborn is carried by its mother during its infant stage. It grows, ages and experiences hunger and other physiological needs inside the pouch. Nursing draws nutrition from the mother just as it does for other lactating mammals. A nursing mother therefore needs adequate food even when her offspring are not visibly moving around the map.

Select a female with a pouch and open the **Pouch** tab. It shows the number of young and allows you to select an individual offspring or open its information card.

If a dependent baby is outside the pouch, the mother can retrieve it. Once it grows out of the infant stage, the baby leaves the pouch and begins life outside. A mother's pouch is also functional in caravans, so a travelling infant continues to age and receive milk. If the mother dies, surviving dependent young are released.

Keep lactating pouch-bearing females well fed and protect them from combat; a newborn depends on the mother's mobility, safety and milk supply.

### Amphibian external fertilization (Odyssey)

Bullfrogs and colossus toads reproduce in water with **Advanced reproductive behavior** enabled. Their offspring pass through aquatic egg and tadpole stages before becoming ordinary young amphibians.

#### Egg laying and external fertilization

A mature female develops an egg clutch and seeks suitable water to lay it. The clutch is deposited as unfertilized roe. An eligible male seeks a compatible unfertilized clutch in water and fertilizes it there. Fertilization is a visible activity with a progress bar and heart effects and takes 250 ticks once the male begins the interaction.

The fertilized roe can be selected to inspect its development and egg count. It must remain in water to develop. Roe is protected against deterioration while immersed.

#### Tadpoles and metamorphosis

After incubation, the eggs hatch into a single group of swimming tadpoles. Select the group to see its development percentage, source clutch size, number of living tadpoles and number of metamorphosed young. The group moves between suitable water cells, and development continues as long as it is in water.

The number of larvae declines during development while the surviving tadpoles grow. The group's weight and nutritional value accordingly change. When metamorphosis reaches completion, the group releases individual froglets or toadlets one at a time. They inherit their parents' relationship and faction information. The wild population cap can limit the number released by wild cohorts.

| Species | Egg incubation after fertilization | Tadpole development | Tadpoles at hatching | Young after metamorphosis |
| --- | --- | --- | --- | --- |
| Bullfrog | 1 day | 60 days | 200–400 | 10–20 |
| Colossus toad | 1 day | 8 days | 200–400 | 10–20 |

The counts are the normal generated cohort ranges; losses to other animals or the wild population cap can reduce the eventual number of young.

Adults and young of the same species cannot eat their own species' roe or tadpole groups. Other animals with suitable diets may still consume them. A secluded breeding pool can therefore help protect the developing offspring.

#### Breeding owned amphibians

Provide accessible water and reproductively capable animals of both sexes. If you house amphibians in an animal barn, a female retains her mature clutch while inside. Release her to lay roe in the water and allow a compatible male to access the clutch.

Disabling advanced reproduction pauses the development and swimming of existing aquatic cohorts. Re-enable it to resume their development.

### Seasonal behavior and ectotherms

Animals use different strategies to cope with cold, seasonal food shortages and their preferred habitat. Two independently configurable systems are **Winter dormancy** and **Water habitat behavior**.

#### Winter dormancy

During winter, suitable wild animals can seek or dig a shelter before entering a dormant state. Entry requires an animal with at least 80% of its food need filled, able to reach the shelter and free from immediate threats. A species' winter strategy determines how much energy it saves.

| Winter strategy | Basal hunger while dormant | Description |
| --- | --- | --- |
| Deep hibernation | 4% of normal | Very strong energy conservation; for example, hedgehogs from Vanilla Animals Expanded. |
| Winter rest | 100% of normal | Seasonal resting in a shelter with the normal basal hunger rate. |
| Denning | 25% of normal | Reduced basal hunger during winter denning, as in grizzly bears. |
| Maternity denning | 25% of normal | Winter denning of pregnant females or mothers with dependent infants, as in polar bears. |

The basal hunger reduction conserves food reserves. Pregnancy and milk production can have their own nutritional demands. Dormant animals remain in the shelter during ordinary rest recovery and can emerge when winter ends, hunger becomes urgent, they are bleeding, birth approaches or a dangerous ranged threat is present.

Only wild animals enter this seasonal dormancy. Tamed animals continue ordinary activity, making it possible to manage them and feed them through the winter. Suitable mothers can still use family dens for reproduction.

#### Ectotherms and water habitat

Ectothermic animals use their own cold-related physiological handling. Suitable ectotherms can bask on land under appropriate temperature and environmental conditions.

With **Water habitat behavior** active, water-adapted animals may seek water as part of ordinary idle movement. Terrestrial herd animals in water may look for accessible dry ground. This affects everyday habitat use; the water-escape response during an emergency is governed by advanced threat avoidance.

A habitat-adapted species is also more likely to spawn in a suitable environment. See the natural-spawn section for how inland water affects the composition of wildlife.

### Animal barns (RimWorld 1.7)

An **animal barn** is a covered livestock building that houses player-owned roamer animals and stores their feed. It complements ordinary pens and allows livestock to live, eat, grow and breed together indoors.

#### Building a barn

The barn occupies a 3 × 6 footprint and costs 180 units of wood and 40 steel. Leave access to its entrance for animals, handlers and haulers, and provide space for medical care or animal products nearby.

The barn's animal capacity is 32.5 total base BodySize by default. It is configurable from 10 to 100 per barn. Capacity uses the species' typical adult BodySize for each resident, including young animals. For example, ten animals of a species with base BodySize 2 occupy 20 units of capacity, regardless of their temporary growth or pregnancy status.

The barn also holds up to 18 stacks of feed. The inspection panel displays animal count, occupied animal capacity and food-stack usage.

#### Choosing which animals live inside

Use the barn's animal filter to choose the species, sexes, age groups and reproductive states accepted as residents. Relevant filters include lactating females and dependent young. Animals must be owned by the colony, eligible roamers, capable of moving, and have room inside.

Handlers bring qualifying animals into the barn under ordinary animal management. The barn can be used alongside pens; nursing-mother balancing attempts to keep mothers and milk-dependent young together.

To request an individual animal, select the barn and choose **Assign animal loading**, then select an eligible animal. A handler brings it to the entrance. This manual order overrides the normal barn animal filter and pen preference, but still requires appropriate animal eligibility and space. Cancel pending orders with **Cancel animal loading**.

#### Inspecting and releasing residents

Select the barn to open its animal list and contents panel. Residents are individual animals: you can inspect their age, health and other animal information. Use the contents panel to release a particular animal or remove stored items. If you prefer to keep the main Animals tab focused on animals outside, enable **Hide barn animals from Animals tab** in Zoology's settings.

Animals may leave a barn automatically for specific services or if the barn can no longer house them. The barn seeks to accommodate the residents according to its capacity and filters.

#### Feed and nursing

Haulers replenish food that is suitable for the residents. Feed is stored as ordinary items and spoils according to ambient temperature. Different diets need compatible supplies: a carnivore cannot meet its food needs with hay simply because it lives beside herbivores.

Residents consume actual food from storage when hungry. Milk-dependent babies first attempt to nurse from their mother or a compatible lactating resident. If nursing is not possible, suitable stored baby food can provide nutrition.

Pay attention to the warning about barn young without milk. Supply compatible food or move a suitable nursing female into the same barn. Separate large hungry adults from small dependent animals when necessary to ensure adequate supplies.

#### Breeding and births

Compatible fertile males and females housed together can mate at their species' normal reproductive rate. Pregnancy, biological aging and the ordinary physiological requirements continue inside.

Newborn mammals can be admitted to their mother's barn if their group fits the available capacity and passes the admission and nursing requirements. If the barn cannot accommodate the family appropriately, affected animals may be released so that they can be cared for outside.

Ordinary egg-laying animals can produce eggs in the barn. Place an egg box adjacent to the barn to receive suitable eggs. Eggs may otherwise be held by the barn, and suitable food eggs can enter its feed storage. Fertilized hatching eggs are preserved as eggs rather than consumed as ordinary feed. Hatchlings from adjacent egg boxes can enter the barn if they meet its admission requirements.

For bullfrogs and colossus toads, release mature females when they are ready to lay: aquatic roe must be deposited in water and fertilized externally by a male. See the amphibian section for their complete reproductive cycle.

#### Milk, wool and medical care

Residents continue producing applicable animal resources. Animals with harvestable milk or wool may temporarily emerge so a handler can perform normal gathering work. The barn reserves space for the animals awaiting service and tries to return them afterwards. Up to two residents can be outside for routine product collection at once.

Animals that need outside medical care or an active operation are released for normal veterinary treatment. Provide appropriate animal beds and assign colonists to the necessary work.

#### Auto-slaughter

Stored animals count toward ordinary auto-slaughter population management. Use the game's auto-slaughter settings as you would for pen animals. Zoology includes barn residents in the relevant population counts and releases selected animals for slaughter. The lactation setting continues to protect eligible nursing females from automatic slaughter.

#### Practical setup

For long-term husbandry, configure the barn's animal filter, provide suitable feed, assign handlers and haulers, and leave room for births. Place egg boxes beside breeding poultry barns where useful. Use the contents panel to inspect food and livestock regularly, and respond to warnings about dependent young.

An outdoor breeding area is essential for amphibians. Keep suitable water accessible for released females and males, and consider using a separate barn or pen for animals with different dietary requirements.

## Family-aware wild departures

The childcare system also affects ecosystem departures.

When Zoology decides that wild animals should leave an overloaded map, family-aware logic can keep related adults and young together instead of selecting one family member and leaving the rest behind.

A mother actively tied to an egg clutch is protected from ordinary forced-departure selection so population control does not casually break an active clutch-defense state.

## Wild animals reproduce without becoming colony livestock

Default: enabled.

Zoology adds a mating job for compatible factionless wild animals.

### Finding a mate

A fertile wild male searches for a nearby compatible female, preferring its own species before related cross-breeding partners.

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

With Biotech, heavy pollution reduces the ecosystem capacity used by this system. The calculation applies Zoology's pollution-to-animal-density curve on top of the other density inputs.

### Forced departure

Default: enabled.

When the ecosystem remains overloaded, animals from species contributing most to the surplus are prioritized for departure. Species above their individual population cap are considered first. Eligible animals leave the map with dependent family members, subject to the clutch safeguards above.

## Pet recreation gives colonists something to do with pets

Default: enabled.

Pet recreation gives colonists joy through walks, fetch and local play.

### Who counts as a pet

A candidate animal must:

- be a player animal;
- have race petness greater than zero;
- be at or below the configured maximum wildness;
- be alive, spawned and not downed;
- not be in a mental state;
- have sufficient consciousness and movement;
- be idle and available, with no urgent basic need taking priority.

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

## Expanded bonding

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

## Direct control of trained animals

Default: enabled.

Zoology provides Beastmastery-based direct control for eligible player animals when Odyssey is active. The combat-control trainables require Odyssey.

When the required training exists, the player can draft the animal and give direct movement and attack commands.

Important restrictions include:

- player ownership;
- a training tracker;
- the required supported training;
- a valid master;
- the master being capable of command;
- the animal being within the master's animal-command range;
- no ordinary direct drafting while the animal is dormant, downed, in deathrest or in an incompatible mental state;
- no new drafting during a ritual.

Compatible Beastmastery and attack training are synchronized. Zoology supports interoperability with other recognized animal-drafting systems.

## Roamers and trainability

The species editor can mark a race as a roamer.

A roamer:

- receives a configurable roamMtbDays value;
- is forced to Trainability None.

The current editor allows 1-60 days.

A non-roamer can be assigned None, Intermediate or Advanced trainability.

Player-owned roamers have a special close-melee threat correction so the roamer state does not make them ignore an attacker that has just engaged them at close range.

## Small pets are not sensible raid targets

Default: enabled.

Raiders can ignore very small player animals while those animals are behaving as non-combatant pets.

Default small-pet race base BodySize threshold: 0.45.

The protection applies to player animals below that race base-BodySize threshold that are not roamers. It is contextual: an animal stops qualifying while following a master into combat, in an aggressive mental state, or after combat engagement makes it an active participant.

The same small-pet system can make an eligible pet flee from nearby hostile raiders instead of remaining a passive target.

"Small pets do not retaliate in melee" defaults to enabled. It suppresses automatic melee retaliation while the animal still qualifies for small-pet protection.

## NPC groups can have real animal companions

Default: enabled.

Zoology adds selected animal options to standard mixed human pawn groups, but it does not leave those animals as handlerless props.

For Yttakin pirate groups, Zoology replaces certain vanilla WildBoar options with timber wolves. Disabling Animals in NPC groups restores the wild boar options instead of removing them, so these original group slots remain available.

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

The replacement spend keeps the generated group from losing the selected animal's point cost when that companion cannot be assigned safely.

### Companion AI

NPC animal companions follow and defend their assigned handlers.

If the master is lost, Zoology first tries to reassign the animal to another valid handler from the same original generated group.

When the human group withdraws, or no mobile human from the original group remains, the animal transitions out of companion behavior and flees.

### Orphaned companions

"Orphaned NPC animals become wild" defaults to off.

Off: an unreassignable companion panic-flees.

On: it becomes factionless and can remain as a wild/tamable animal.

### Third-party animal options

The generic safety layer is not limited to animal entries added by Zoology. Compatible animal options already present in a standard mixed human group can also benefit from the handler checks.

All-animal and non-human faction groups are intentionally outside this system.

## Wound licking is animal self-tending

Default: enabled.

Animals can lick their own bleeding surface wounds to stop bleeding and aid recovery. Wild animals and animals belonging to other factions do this autonomously. For colony animals, select **No medical care** in the medical care menu on the Health tab to allow wound licking. Animals assigned veterinary care, including care without medicine, rely on medical treatment instead.

An animal must be able to act normally and must not be downed. Only bleeding, treatable wounds on external body parts can be licked. Each treatment takes time and provides low-quality tending without medicine. An animal can also lick its wounds while sheltering in a tree.

For serious injuries, internal bleeding or infection, veterinary treatment remains important. To let a colony doctor treat an injured animal, choose an appropriate medical care level in its Health tab.

## Ectotherms use a different cold-injury path

Default: enabled for species marked as ectothermic.

Zoology does not make ectotherms immune to cold.

Instead, it clones the ordinary OrganicStandard hediff-giver set for marked animal races and substitutes the insectoid/ectothermic hypothermia hediff variant where RimWorld's hypothermia giver provides one.

Other organic hediff givers are retained in the cloned set.

Marked ectotherms remain vulnerable to unsuitable temperatures while using the ectothermic hypothermia path instead of the ordinary warm-blooded animal assumption.

## Animal bionics

Default: enabled.

Zoology extends supported human bionic recipes to compatible animal anatomy during startup.

Bionic surgery is available for animals with compatible anatomy, including corresponding animal-specific body parts.

Combat-oriented bionic parts have size-dependent animal variants so a tiny animal and a huge animal do not receive the exact same game-facing melee implant.

Animals marked CannotBeAugmented are excluded.

Restart RimWorld after changing the animal bionics setting.

## Aggression at slaughter

Default: enabled for marked species.

A standing marked animal can react to being designated/slated for slaughter.

The system warns when the slaughter designation is added, and when the slaughterer reaches the animal the normal slaughter action can be replaced by a manhunter response. The slaughter designation is removed and the slaughter job is interrupted.

Downed animals do not trigger this response.

By default the same marker also excludes the animal from peaceful ritual animal roles.

This system is independent of lactation auto-slaughter protection.

## Non-CE size-aware damage reduction

Default: enabled when Combat Extended is absent.

RimWorld's raw melee damage can make extremely small animals or an unarmed human disproportionately effective against much larger animals. Zoology reduces selected natural-attack damage in those size-mismatch cases.

The correction applies only to supported natural pawn attacks.

It does not apply to:

- ranged damage;
- equipped weapons;
- implant/hediff-driven attack weapons;
- unrelated environmental damage.

The correction never increases damage.

The correction considers predator status and the attacker's and target's base/current size.

Combat Extended disables this non-CE size-aware damage-reduction system.

## Combat Extended integration

With Combat Extended active, Zoology uses CE-specific animal combat and armor data.

### Separate CE attack values

CE damage, penetration and cooldown values are produced separately from the vanilla combat values. The CE integration is therefore not a conversion of the final vanilla damage number.

### Life-stage and sex-specific Combat Extended penetration

When CE is present, Zoology applies its generated life-stage and applicable adult sex-profile attack factors, including penetration, and presents the corresponding values in CE statistics.

## Low-level species behavior features

Several Dev-page systems are normally assigned by species markers or comps. They are exposed to users mainly for compatibility or deliberate customization.

### No Flee

Marked animals can be prevented from ordinary animal flee decisions and supported panic/terror starts.

### Flee from carrier

Animals of threatening species can cause nearby vulnerable animals to flee.

Per-species parameters:

- radius: default 12, editor range 1-60;
- prey BodySize limit: default 0, editor range 0-20; zero means no explicit size ceiling;
- flee distance: default 16, editor range 1-80.

### Cannot be mutated

Prevents marked animals from supported mutation paths.

### Cannot be augmented

Prevents marked animals from supported augmentation paths, including Zoology's own animal bionics.

### Ageless

Ageless species are protected against supported age-related health conditions.

Default cleanup interval: 6000 ticks. Editor range: 60-120000.

### Drugs immune

Drug-immune animals are protected against supported drug effects, addiction and tolerance.

Default cleanup interval: 2000 ticks. Editor range: 60-120000.

### Animal clotting

Animals with enhanced clotting periodically tend their own bleeding injuries.

Default check interval: 360 ticks.

Default tending-quality range: 0.2-0.7.

The editor allows each end of the quality range from 0 to 2 and the interval from 60 to 120000 ticks.

### Animal regeneration

The global regeneration setting enables regeneration in eligible species.

Regeneration may vary with age and body size.

### No porcupine quill

Marked animals are excluded from the supported porcupine-quill hediff path.

## Insect cocoon spawning (Biotech)

The insect cocoon safeguard allows a cocoon with a positive spawn budget to produce its cheapest available insect when the budget would otherwise be too small.

## Settings reference

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
| Dynamic animal growth | On | Day-by-day stats and graphic interpolation between stages. |
| Sexual dimorphism | On | Male/female adult endpoints for configured species. Independent of growth. |
| Automatic juvenile sprite scaling | On | Mass-based baby/juvenile graphic sizes. |
| Advanced reproductive behavior | On | Bird nests, marsupial pouches, external amphibian fertilization and tadpole cohorts. Family dens require advanced threat avoidance too. |
| Enable mammal lactation | On | Mammal species are configurable. |
| Allow handlers to feed baby mammals | On | Requires mammal lactation; Handling work, direct-orderable, no mechs. |
| Allow slaughtering lactating animals | Off | Requires lactation; controls auto-slaughter exclusion. |
| Enable animal childcare | On | Childcare species are configurable. |
| Young/clutch protection range | 10 | 10-40. |
| Minimum Combat Power to defend young/clutches from humans/mechanoids | 70 | 0-1000. |
| Do not flee from humans while protecting young | Off | Requires childcare + human fleeing. |
| Enable egg protection | On | Childcare sub-feature. |
| Do not flee from humans while protecting clutch | On | Requires childcare + egg protection + human fleeing. |
| Enable wound licking | On | Animal self-tending; colony animals require No medical care. |
| Enable ectothermic handling | On | Ectotherm species configurable. |

Turning mammal lactation off suspends handler baby feeding (without clearing its saved checkbox) and disables the lactation auto-slaughter option.

### Combat

| Setting | Default | Range / dependency |
| --- | --- | --- |
| Enable animal draft control | On | Beastmastery/direct-control system; Zoology's shipped trainables require Odyssey. |
| Enable animal damage reduction | On without CE; forced Off with CE | Non-CE natural-attack size correction. |

### Other behavior

| Setting | Default | Range / dependency |
| --- | --- | --- |
| Advanced animal food selection | On | Food preferences, harvesting and comparison of hunting and other food sources. |
| Wild animal foraging (Odyssey) | On | Requires Odyssey and advanced animal food selection. |
| Animals in NPC groups | On | Controls Zoology-added NPC animal options. |
| Orphaned NPC animals become wild | Off | Changes unreassignable companion outcome. |
| Animal barn (1.7) | On | Enables the animal barn construction and handling system. |
| Barn total BodySize capacity (1.7) | 32.5 | 10–100, increments of 0.5. |
| Hide barn animals from Animals tab | Off | Optional display filter for stored livestock. |
| Pet recreation | On | Enables Zoology pet joy givers. |
| Maximum pet wildness | 0.2 | 0-1. |
| Expanded pet bonding | On in default bonding mode | See bonding modes above. |
| Expanded all-animal bonding | Off in default bonding mode | Broader bonding scope. |
| Advanced threat avoidance | On | Tree, burrow and water refuges from credible threats; family dens additionally require advanced reproduction. |
| Water habitat behavior | On | Idle search for suitable water or dry land, independent of threat-driven fleeing. |
| Winter dormancy | On | Seasonal dormancy and winter dens for configured wild species, independent of threat avoidance. |
| Custom flee danger | On | Enables home-map race base-BodySize safety thresholds. |
| Safe predator BodySize | 0.7 | UI slider 0-30. |
| Safe non-predator BodySize | 3.0 | UI slider 0-30. |
| Ignore small pets by raiders | On | Non-combatant tiny-pet protection. |
| Small-pet base BodySize threshold | 0.45 | UI slider 0-30; applies to non-roamer player animals. |
| Small pets do not retaliate in melee | On | Requires small-pet handling. |
| Animals flee from humans | On | Species list configurable. |
| Human search radius | 12 | 6-24. |
| Flee distance from human | 16 | 6-40. |
| Wild animal reproduction | On | Wild mating job. |
| Limit wild animals per species | On | Maximum wild population for each race; independent of owned animals. |
| Wild animal species limit | 30 | 10–100, shown when the species cap is enabled. |
| Limit wild reproduction by ecosystem | On | Requires wild reproduction. |
| Force wild animals to leave on overload | On | Ecosystem overload relief. |
| Ecosystem limit factor | 1.2 | 1-3. |
| Configure roamers | — | Per species; roam interval 1-60 days, non-roamer trainability None/Intermediate/Advanced. |
| Human bionics on animals | On | Startup-sensitive; restart after changing. |
| Aggression at slaughter | On | Species configurable. |

### Dev

| Setting | Default | Notes |
| --- | --- | --- |
| Disable all runtime patches | Off | Disables Zoology's optional runtime behavior; static animal data remain active. |
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
| Animal regeneration | On | Global behavior toggle. |
| Animal clotting | On | Species configurable; interval/quality editable. |
| No porcupine quill | On | Species configurable. |

## Changing settings

Most behavior settings, including advanced food selection and wild foraging, apply during play and preserve their values in the mod settings. Disabling a reproductive or refuge feature releases affected held offspring or pauses aquatic development as appropriate.

Restart RimWorld after changing animal bionics. For troubleshooting, a restart after changing low-level Dev settings ensures a clean game initialization.

## Compatibility

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

DLC-specific features require the corresponding expansion.

### Biotech-specific changes

In addition to Biotech animal race/product data and the cocoon safeguard, Zoology patches toxic-meat recipe handling when Vanilla Animals Expanded - Waste Animals is not present. ToxicMeat is excluded from the relevant pemmican and ordinary meal ingredient filters in that configuration.

### Odyssey-specific changes

Odyssey receives:

- its own animal race balance;
- biome distributions for Odyssey biomes;
- Odyssey-specific body definitions;
- baby life-stage patches;
- egg-laying and hatching data;
- meat and graphics corrections.

When Vanilla Expanded Framework is also active, Zoology assigns VEF crepuscular/nocturnal body-clock extensions to selected Odyssey animals.

### Vanilla Expanded Framework behavior

When VEF is active, Zoology uses VEF systems for several ecological behaviors.

With VEF, supported animals receive:

- crepuscular/nocturnal body-clock assignments for selected Core, Odyssey and Vanilla Animals Expanded animals;
- VEF cross-breeding extensions for supported cross-breed pairs;
- insect-foraging/auto-nutrition behavior for selected insectivorous animals.

Examples of the foraging integration include fennec foxes and, when the relevant content is loaded, pangolins, hedgehogs, armadillos, crows and bullfrogs. These animals use VEF's AutoNutrition behavior; Zoology supplies the species assignment.

### Vanilla Animals Expanded modules

Depending on the installed module, the integration includes:

- biome placement;
- additional body definitions and Odyssey fallback bodies;
- life-stage patches;
- damage defs;
- graphics;
- egg-layer and hatcher data;
- milk and shearable data;
- leather/item corrections;
- body-clock assignments through VEF;
- cross-breeding relationships;
- crossAggroWith relationships among compatible cat and dog groups.

The Endangered integration additionally removes the Rockhopper Penguin DigWhenHungry comp and the module's raw-fish defs in the configuration Zoology expects, while the VEF foraging patch supplies the replacement insect/ant-feeding behavior for the relevant species.

The Waste Animals integration includes egg/hatcher data and damage corrections in addition to race values.

### Alpha Animals, Alpha Biomes, Dinosauria and Megafauna

Alpha Animals receives generated race patches plus fallback body definitions for several unusual body plans when Odyssey is not providing the corresponding bodies.

Alpha Biomes integration is a biome-distribution layer for the supported Alpha Biomes environments.

Dinosauria receives race balance plus dedicated animal graphics/shadow corrections.

Megafauna currently uses the generated race patch set.

**Animals Are Fun! (Continued):** There is no known technical incompatibility between the two mods, but both implement recreational interactions between colonists and pets. They can be used together, although some activities overlap. If you prefer the interactions from Animals Are Fun! (Continued), disable **Pet recreation** in Zoology's mod settings. This does not require disabling Zoology's separate bonding options.

Zoology also detects supported external animal-drafting ownership and optional health-system hooks so overlapping runtime behavior can defer to the installed integration.

## Troubleshooting behavior conflicts

If one AI behavior looks wrong in a heavily modded game, disable the smallest relevant Zoology feature first.

Examples:

- animals abandoning food: inspect prey/human fleeing and corpse-defense interactions;
- babies not nursing: verify mammal assignment, lactation, mother nutrition and reachability;
- handlers not feeding babies: verify Mammal lactation and Allow handlers to feed baby mammals, Handling work, Manipulation and suitable baby food;
- protected young causing unexpected aggression: inspect childcare/egg protection and the Combat Power thresholds;
- tiny pets entering combat: inspect small-pet retaliation and whether the pet is actively participating in combat;
- animal drafting conflicts: test Zoology draft control against the other drafting system;
- CE melee display/penetration disagreement: verify generated CE age/sex profiles, matching compiled assembly, and active CE integration.

The Dev master switch disables optional runtime behavior while retaining the static animal rebalance.

For XML/framework behavior and the spreadsheet pipeline, see [FRAMEWORK.md](FRAMEWORK.md). For regeneration and patch-generation tooling, see [CHECKER.md](CHECKER.md).
