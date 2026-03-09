<img src="https://i.ibb.co/ZpLXjc8Z/Preview.png">

## Table of contents

* [Steam Workshop](#steam-workshop)  
* [Overview](#overview)  
* [Features](#features)  
* [Optional gameplay system](#optional-gameplay-system)  
* [Mod compatibility](#mod-compatibility)  
* [Technical notes for modders](#technical-notes-for-modders)  
* [Contributing](#contributing)  
* [License](#license)

---

## Steam Workshop

The player version of the mod is available on Steam:

Workshop page: https://steamcommunity.com/sharedfiles/filedetails/?id=3679396881

---

## Overview

Zoology is a comprehensive animal overhaul that maps in-game animal performance to real-world biology without breaking gameplay.

This mod recalculates core animal parameters using ecologically and physiologically meaningful inputs — for example, body mass, gestation length, bite force and claw/tooth dimensions — and transforms those inputs into RimWorld-compatible statistics via an automated data pipeline. Source data are collected from primary and secondary biological literature where available, processed in a reproducible TSV dataset and converted to XML by a Python script. The goal is to make animals behave and perform in ways that feel biologically plausible while preserving the game's tactical and balance constraints.

All major gameplay-facing systems are configurable. If you prefer minimal changes, most behavioral systems can be turned off in mod settings; if you want a deep biological overhaul, enable the full feature set. Zoology is intended both for players who want more realistic animal interactions and for modders who want a data-driven framework to base their animal mods on.

The mod has undergone extensive in-game testing, though some systems are still evolving. Bug reports and compatibility reports are welcome.

---

## Features

### Complete biological stat overhaul

Every vanilla animal and DLC animal has had its core stats recalculated. All animals (vanilla + official DLC) were recalculated from biological inputs:

* average body mass
* gestation period
* litter / clutch size
* * growth rate
* claw & tooth dimensions
* bite force

These values were compiled from scientific literature and processed through an automated Python script that generates the mod’s XML patches.

The resulting numbers are balanced against RimWorld’s internal formulas so animals feel stronger, faster and more believable while still fitting the game’s combat and event systems. Melee DPS and movement speeds of animals are, on average, increased to better reflect biologically realistic performance. Combat Power (used for in-game event generation) is still calculated using RimWorld’s internal formulas, with ModExtension influences applied to preserve overall balance.

---

### Improved ecology and animal distribution

Biome distributions have been adjusted to remove obvious zoological inaccuracies. These changes are intentionally conservative and aim to improve ecological plausibility without disrupting RimWorld’s biome balance.

---

### Audio improvements

Animal sounds have been corrected: several species now use more appropriate audio assets (for example, big cats no longer use domestic cat meows).

---

### Biological fixes

Various biological inaccuracies in vanilla animals have been corrected. Examples include:

* birds flee using flight instead of ground sprinting  
* ectothermic animals (reptiles, crabs, snails) experience metabolic slowdown in cold rather than vanilla hypothermia mechanics  
* guinea pig fur replaced with squirrel pelt  
* additional zoological corrections

---

## Optional gameplay system

Many behavioral systems are configurable and can be enabled or disabled in mod settings. These systems allow players to tune the balance between realism and gameplay convenience.

### Advanced predation logic

Predators choose prey using multiple factors:

* body size  
* kinship  
* age  
* combat power

Predators will only attack other predators if their Combat Power is more than **33% higher than the other predator's**.

**Controlled by**

**Enable advanced prey selection logic**  

---

### Pack hunt behavior

Pack hunters can coordinate group attacks even if not all pack members are hungry.

**Controlled by**

* **Enable pack hunt behavior**

---

### Prey fleeing behavior

Prey animals attempt to flee from hunting predators.

Predators will abandon the pursuit if they cannot catch prey within a certain amount of time.

**Controlled by**

* **Enable prey fleeing from predators**

---

### Custom flee-from-danger override

Replaces the vanilla `ShouldAnimalFleeDanger` logic.

A scalable system determines which animals flee from threats on their home maps.

**Configurable thresholds include**

* **Safe Predator Body Size Threshold**  
* **Safe Non-Predator Body Size Threshold**

These allow fine control over which animals consider raiders or other dangers worth fleeing from.

---

### Raiders ignore small pets

Raiders treat small, non-threatening pets (for example, cats) like penned livestock unless they:

* follow their master
* go manhunter

**Settings controlling this system**

* **Enable raiders ignoring small pets**  
* **Small Pet Body Size Threshold**  
* **Enable small pets fleeing from raiders**

---

### Predator protection of kills

Predators may guard their kills against scavengers and colonists.

**Configurable settings**

* **Prey protection range**  
* **Enable predator defending their kills**
* **Protection trigger size difference threshold**

A protection trigger threshold prevents large predators from attacking minor scavengers unnecessarily.

---

### Scavenging

Animals marked with `ModExtension_IsScavenger` can consume:

* rotten corpses  
* skeletonized corpses if **allowVeryRotten** is set to true.

The nutritional value of skeletonized corpses is reduced compared to fresh corpses.

**Controlled by**

* **Enable scavenging (scavenger behavior)**

---

### Human bionics on animals

Allows many vanilla human bionics to be installed on animals, implemented through runtime patching.

Excluded animals can be flagged using:

* `ModExtension_CannotBeAugmented`

Bionics scale with animal body size and apply appropriately scaled hediffs. This allows animal augmentation without requiring additional mods.

Compatible with **Combat Extended**.

---

### Aggro at slaughter

Animals marked with `ModExtension_AgroAtSlaughter` may become aggressive when slaughtering is attempted.

**Setting**

* **Enable aggro-at-slaughter**

Downed animals can still be safely slaughtered.

---

### Mammal lactation and juvenile nursing

Mammalian neonates can consume milk as their first diet.

Mothers receive a lactation hediff that transfers nutrition to their young.

**Controlled by**

* **Enable mammals lactation**

Includes a setting controlling auto-slaughter behavior:

* **Lactation auto-slaughter handling**

---

### Animal damage reduction

Optional system that reduces unrealistic damage from very small animals attacking much larger animals. Predator–prey interactions are excluded.

**Controlled by**

* **Enable animal damage reduction**

Automatically disabled when **Combat Extended** is installed, since CE handles damage realism through armor and penetration mechanics.

---

## Mod compatibility

Zoology aims to remain broadly compatible with the RimWorld mod ecosystem.

Most invasive behavior systems can be disabled in mod settings if conflicts occur.

**Explicit compatibility exists for**

* All official DLCs  
* Vanilla Expanded Framework  
* Animals Are Fun  
* Vanilla Animals Expanded (non-Odyssey animals)  
* Combat Extended

---

### Combat Extended integration

When **Combat Extended** is installed, animal combat stats are recalculated from biological inputs:

* hide thickness and toughness  
* claw and tooth length  
* bite force

These values are mapped onto CE formulas to produce results consistent with CE’s combat system.

The option **Override CE Penetration for animal life stages** modifies penetration scaling to prevent juveniles from becoming unrealistically lethal compared to small adult animals.

---

### Planned compatibility

Future compatibility work is planned for additional animal mods such as:

* Alpha Animals  
* Dinosauria

---

## Technical notes for modders

### ToolWithGender support

Similar to Combat Extended’s gender-specific weapons, Zoology implements a **ToolWithGender** type. This allows animals to have **sex-limited attacks** (for example male-only horns, tusks, or antlers) when **Combat Extended is not present**.

---

### Runtime autopatching

Some systems are applied dynamically at runtime using **Harmony** patches, including:

* behavioral overrides  
* scavenger logic  
* predator logic  
* animal bionics

---

### Data generation pipeline

Animal XML definitions are generated automatically.

The mod uses a **TSV dataset** containing biological parameters such as:

* body mass  
* gestation period  
* litter size  
* bite force  
* claw/tooth size  
* growth rate

A Python script converts the dataset into RimWorld XML patches.

**Advantages**

* prevents manual XML errors  
* allows reproducible stat generation  
* simplifies large-scale edits

Contributors are encouraged to submit **TSV updates** instead of editing generated XML files directly.

---

### Framework components for modders

The mod also acts as a **framework**, providing several Comps and ModExtensions that can be used by dependent mods. Some are primarily for mod integration rather than vanilla animal patches.

#### Comp_Ageless

Removes age-related hediffs periodically via `cleanupIntervalTicks`. Animals with this comp effectively **do not age**.

The list of hediffs is generated at runtime for better compatibility with mods that add new age-related hediffs.

```xml
<comps>
  <li Class="ZoologyMod.CompProperties_Ageless">
    <cleanupIntervalTicks>6000</cleanupIntervalTicks>
  </li>
</comps>
```

#### Comp_DrugsImmune

Removes drug/addiction hediffs via `cleanupIntervalTicks` and blocks them from being applied. This effectively makes the animal **immune to drugs**.

```xml
<comps>
  <li Class="ZoologyMod.CompProperties_DrugsImmune">
    <cleanupIntervalTicks>2000</cleanupIntervalTicks>
  </li>
</comps>
```

#### Comp_AnimalClotting

Periodically treats bleeding hediffs similarly to the **Superclotting** gene.

**Configurable:**

* `tendingQuality`
* `checkInterval`

```xml
<comps>
  <li Class="ZoologyMod.CompProperties_AnimalClotting">
    <checkInterval>360</checkInterval>
    <tendingQuality>
      <min>0.2</min>
      <max>0.7</max>
    </tendingQuality>
  </li>
</comps>
```

#### Comp_AnimalRegeneration

Adds regeneration (or other) hediffs depending on life stage or body size.  
The hediff changes dynamically when the animal crosses size thresholds.

**Life stage fractions:**

* `babyFraction` — fraction of body size to apply hediff for babies
* `juvenileFraction` — fraction of body size to apply hediff for juveniles
* `adultFraction` — fraction of body size to apply hediff for adults

**Check interval:**  
* `checkIntervalTicks` — how often regeneration hediff check is applied

```xml
<comps>
  <li Class="ZoologyMod.CompProperties_AnimalRegeneration">
    <hediffBaby>Zoology_Regen_Baby</hediffBaby>
    <hediffJuvenile>Zoology_Regen_Juvenile</hediffJuvenile>
    <hediffAdult>Zoology_Regen_Adult</hediffAdult>

    <babyFraction>0.2</babyFraction>
    <juvenileFraction>0.5</juvenileFraction>
    <adultFraction>1.0</adultFraction>

    <checkIntervalTicks>720</checkIntervalTicks>
  </li>
</comps>
```

#### ModExtension_IsMammal
Marks an animal as a mammal. Mammals will nurse their young with milk. Offspring have a restricted diet, consuming only milk and similar products. Adult mammalian predators will **not** attack their own young (cross-breeding considered).

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_IsMammal" />
</modExtensions>
```

---

#### ModExtension_AgroAtSlaughter
Animals with this extension will become aggressive when a slaughter attempt is made. If `excludeFromRituals` is set to true, they are excluded from sacrifice rituals. This behavior only triggers if the animal is **not** downed. Use `verboseLogging` for debugging purposes.

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_AgroAtSlaughter">
    <verboseLogging>false</verboseLogging>
    <excludeFromRituals>true</excludeFromRituals>
  </li>
</modExtensions>
```

---

#### ModExtension_IsScavenger
Allows the animal to feed on rotten corpses. If `allowVeryRotten` is set to true, the animal can even consume desiccated (skeletal) remains.

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_IsScavenger">
    <allowVeryRotten>false</allowVeryRotten>
  </li>
</modExtensions>
```

---

#### ModExtension_NoFlee
Blocks vanilla flee jobs and prevents the animal from entering panic or terror mental states. The animal will **not** run away from danger or environmental threats. Includes `verboseLogging` for debugging.

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_NoFlee">
    <verboseLogging>true</verboseLogging>
  </li>
</modExtensions>
```

---

#### ModExtension_CannotBeMutated
A marker extension that prevents the animal from being targeted by bio-mutations and related mechanics (specifically those introduced in the **Anomaly DLC**).

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_CannotBeMutated" />
</modExtensions>
```

---

#### ModExtension_Ectothermic
Replaces the standard hypothermia logic with a metabolism slowdown mechanic, similar to vanilla giant insects. While it changes how the animal reacts to cold, it still allows for **frostbite** damage.

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_Ectothermic" />
</modExtensions>
```

---

#### ModExtension_NoPorcupineQuill
A specific marker used to prevent the `PorcupineQuill` hediff from appearing or to remove it if it is already present on the animal.

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_NoPorcupineQuill" />
</modExtensions>
```

---

#### ModExtension_FleeFromCarrier
Makes the animal intimidating to others. Within a customizable `fleeRadius`, other animals will attempt to flee from the carrier. You can set a `fleeBodySizeLimit` to determine which animals are affected and `fleeDistance` to set how far they run.

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_FleeFromCarrier">
    <fleeRadius>18</fleeRadius>
    <fleeBodySizeLimit>0</fleeBodySizeLimit>
    <fleeDistance>24</fleeDistance>
  </li>
</modExtensions>
```

---

#### ModExtension_CannotBeAugmented
A marker extension that prohibits the installation of bionics, implants, or any other augmentations on this animal.

```xml
<modExtensions>
  <li Class="ZoologyMod.ModExtension_CannotBeAugmented" />
</modExtensions>
```

## Contributing

Contributions are welcome.

**Possible contribution types:**

* biological data improvements
* compatibility patches
* additional species datasets
* bug fixes

Issues and discussions can be opened in the repository.

## License

MIT License
