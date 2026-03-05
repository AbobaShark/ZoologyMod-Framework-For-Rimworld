# Zoology

## Table of contents
- [Overview](#overview)
- [What’s changed (high level)](#whats-changed-high-level)
- [Optional, configurable mechanics (toggleable)](#optional-configurable-mechanics-toggleable)
- [Mod compatibility](#mod-compatibility)
- [Installation](#installation)
- [Technical notes for modders](#technical-notes-for-modders)
- [Final remarks](#final-remarks)
- [Change log & testing status](#change-log--testing-status)
- [License](#license)

---

## Overview
Zoology is a comprehensive animal overhaul that maps in-game animal performance to real-world biology without breaking gameplay. All vanilla animals plus those added by the official DLCs have had their core stats recalculated from biological inputs — average body mass, gestation length, litter/clutch size, claw & tooth dimensions, bite force, growth rate, and more — assembled from scientific literature and automated into the mod’s data pipeline. The mod balances these more realistic numbers against RimWorld’s in-game formulas so animals feel stronger and faster in a way that still fits the game.

## What’s changed (high level)
•	Complete stat rewrite for every vanilla animal and animals from the official DLCs. All core stats (average body mass, gestation period, litter/clutch size, claw/fang lengths, bite force, etc.) were compiled from scientific literature and converted into in-game parameters. Melee DPS and movement speeds of animals are, on average, substantially increased compared to vanilla to reflect biologically realistic performance while Combat Power (used for in-game event generation) is still calculated using in-game formulas plus Mod Extension influences so balance with the rest of the game is preserved. Forget about omnivorous monitor lizards gobbling up your rice or bloodthirsty ostriches with a 100% manhunter chance, and be careful when dealing with a herd of boars.  
•	Rebalanced animal economy — milk production, growth rates, gestation and litter sizes have been rescaled to produce a new husbandry balance: cows are now more viable as milk sources rather than meat (slow growth, low fertility), whereas highly fertile fast-growing pigs are far better for meat.  
•	Animal sounds have been improved. Say goodbye to meowing cougars and tigers.  
•	Ecology & distribution tweaks — biome distributions have been adjusted to remove glaring inaccuracies (e.g., tropical reptiles in deserts or wild alpacas). These changes are intentionally conservative and aimed at removing obvious zoological while preserving game balance.  
•	Behavioral and physiological fixes — birds flee using flight rather than ground sprinting; ectotherms (reptiles, crabs, snails) suffer metabolic slowdown in cold rather than vanilla hypothermia mechanics (but they still can get frost bites); guinea pig fur replaced by squirrel pelt, and other biological corrections.

## Optional, configurable mechanics (toggleable)
Zoology offers many player options so you can pick realism without sacrificing fun. Every major behavioral change can be enabled or disabled via config to suit your preferred playstyle or to help compatibility testing. All of the below are optional and configurable via in-game settings.  
•	Advanced predation logic — predators choose prey by size, kinship, age and combat strength (less emphasis on raw body size). Pack hunters can coordinate group attacks even if not all pack members are hungry. Enable predator-on-predator hunt check tightens predator targeting so a predator only attacks another predator if its Combat Power is >33% higher.  
•	Prey fleeing — prey species attempt flight from hunting predators; predators will abandon pursuit if they cannot catch prey within some amount of time. Controlled by Enable prey fleeing from predators.  
•	Custom flee-from-danger override — replace the vanilla ShouldAnimalFleeDanger with a scalable system controlling which sizes of animals flee on their home maps. Safe Body Size Thresholds, Safe Predator Body Size Threshold, and Safe Non-Predator Body Size Threshold let you tune which animals consider raiders and other dangers worth fleeing from.  
•	Raiders ignore small pets — make raiders treat small, non-threatening pets (e.g., cats) like penned livestock unless they follow the master or go mad. Enable raiders ignoring small pets and Small Pet Body Size Threshold control this behavior; Enable small pets fleeing from raiders lets pets themselves react to raiders if desired.  
•	Predator protection of kills — predators may guard their kills against scavengers and colonists within a configurable Prey protection range. Protection trigger size difference threshold stop large predators from needlessly attacking minor scavengers.  
•	Scavenging — animals marked with ModExtension_ IsScavenger will eat spoiled or even partially skeletonized corpses (with reduced nutrition) if Enable scavengering (scavenger behavior) is enabled.  
•	Human bionics on animals — Automatically enables many vanilla bionics on animals at runtime (excluding animals flagged ModExtension_CannotBeAugmented). Bionics scale with animal body size and apply appropriately scaled hediffs — useful for augmenting animals without installing separate augmentation mods; Combat Extended compatibility is preserved.  
•	Agro at slaughter — animals marked with ModExtension_AgroAtSlaughter can become aggressive when slaughtering is attempted. Downed animals may still be safely slaughtered.  
•	Mammal lactation & juvenile nursing — enable mammalian neonates to consume milk as the first diet and have mothers receive a lactation hediff that transfers nourishment to their young. This is separate from vanilla milking mechanics and includes an option to control how auto-slaughter treats lactating animals via Lactation auto-slaughter handling.  
•	Animal damage reduction — optionally reduces injurious damage small animals inflict upon much larger animals (excluding genuine predator → prey interactions), to avoid unrealistic rapid bleeding from tiny attackers. This option is disabled when Combat Extended is installed (CE handles realistic damage through armor and armor penetration).

## Mod compatibility
Zoology aims to be broadly compatible; most invasive behavior patches are toggleable to avoid conflicts. Explicit compatibility work has been done for the following:  
•	All official DLCs — full compatibility (DLC animals and mechanics included in the stat overhaul).  
•	Vanilla Expanded Framework — full compatibility.  
•	Animal are fun — full compatibility.  
•	Vanilla Animals Expanded — compatibility patches currently implemented for the included non-Odyssey animals.  
•	Combat Extended — full compatibility. When CE is installed, animal attack damage, penetration, and defensive properties are recalculated using biological inputs (hide thickness and toughness, claw/teeth length, bite force, etc.) mapped onto CE formulas; only CE attack cooldown calculations have been adjusted by the mod author to fit animal mechanics. An option “Override CE Penetration for animal life stages” adjusts how size affects penetration to prevent juveniles from being unrealistically lethal compared to small adult animals. The combined effect with higher realistic speeds makes animals significantly more dangerous under CE.  
Planned future compatibility with other mods like Alpha Animals and Dinosauria (not currently included).  
Notes: no explicit conflicts are currently known; but because Zoology changes every animal’s stat and runtime behaviors, conflicts are possible with mods that do similar rewrites. Most behavior patches are optional and toggleable to help avoid those conflicts.

## Installation
•	Download the latest release package (Workshop or GitHub releases).  
•	Unzip the mod folder into RimWorld/Mods/.  
•	Enable Zoology in the in-game Mods menu and restart the game if required.  
•	Configure options in the Mod options UI if you want something other than the defaults.

## Technical notes for modders
•	ModExtensions & Comps: Zoology works as a framework for modding and ships multiple ModExtension types and Comp implementations that let other mods query animal reproduction, lactation state, scavenging, etc. These extensions are intended to be modder-friendly and are documented in the author’s repository.  
•	Runtime autopathing: features such as human bionics on animals and certain behavioral patches are applied at runtime via autopathing/Harmony.  
•	Automated, auditable pipeline — XML patches were generated from a TSV dataset and written via an automated Python pipeline to avoid manual XML errors and to speed up large-scale edits. Contributors can propose TSV updates rather than hand-editing XML  
•	Harmony patch footprint: some features (notably scavenging and deep predation logic) require heavy Harmony patching and are more invasive — they can be disabled if you want a minimal footprint.

## Final remarks
Zoology is intended for players who want animals that behave and perform more like their real-world counterparts while preserving RimWorld’s tactical and balance considerations. Many of the realism features are optional — tune them to match the experience you want, from a conservative “fix obvious errors” setup to a full biological overhaul.  
If you are a modder and want to integrate your animal additions, consult the ModExtension docs included with the mod and consider contributing TSV entries for your species so Zoology can incorporate them using the same generation pipeline.  
Change log & testing status: The author reports extensive in-game testing. Some features are still being refined; watch the workshop page for updates and follow the included notes for known edge cases.

## License
MIT License
