# Animal barn validation вЂ” RimWorld 1.7 only

For the actual-game 1,000-chicken performance comparison, results and reproduction
instructions, see `PERFORMANCE.md`. It uses a separate profile and marker.

Run from PowerShell with the installed 1.7 game and Harmony:

```powershell
./validation/AnimalBarn17/run.ps1 -Run
# Optional: -GameRoot 'D:/.../RimWorld' -HarmonyRoot 'D:/.../2009463077'
```

The script builds only `RimWorldVersion=1.7`, creates an isolated game under
`Zoology/Source/codex-build/barn-validation-game`, copies the mod and test assembly,
and uses `%LOCALAPPDATA%/Temp/ZoologyBarnValidation17` for settings, saves and logs.
Installed game assets are read through hardlinks/junctions; the script does not
change the installed game's mods, settings or saves. It requires the installed
expansions used by this repository's validation environment. The test assembly is
never copied into the distributed mod.

Wait for `COMPLETE barn` in `barn-validation.txt`; every assertion must say PASS.
`stdout.log` contains the actual game log. An intentional throwing food produces
one expected barn-feeding warning, proving fallback to another real stack.

## Controlled comparison

The fixture runs in the real game on a paused map. It advances the game tick
counter explicitly, dispatches `DoTick` for both the barn and map control pawns,
and uses a native wait job for the controls. Thus map controls have their actual
health/needs/age trackers but do not walk, graze or seek unrelated food. Rare food
items on the map are dispatched at their native 250-tick cadence.

Each adult cow, chicken and pig starts at age 3 and full food for a 6,000-tick
fast. The 30,000-tick feeding comparison starts at 50% food with 500 kibble units
on each side. The map control eats through `Thing.Ingested` on the same 250-tick
schedule and race-specific appetite threshold. This isolates physiology and
ingestion from travel and eating-job latency. The fixture logs initial/final
nutrition, rates, actual items consumed, age and maximum nutrition. Different
pawn hash phases can change the timing of the 150-tick need update relative to a
meal and the amount clipped at full nutrition. The feeding assertion requires
identical item consumption and a residual smaller than one actual food unit;
the fasting comparison checks equal need levels directly.

Other checks cover juveniles, older animals, malnutrition, actual starvation,
modified Nutrition stats, short stacks, mixed diets, bad ingestibles, food rot,
merging and ejection, milk transfer and lactation restoration, wood/stone fire,
extinguishing, death/corpses, destruction and release, two save/load cycles,
filters, pending loading, native service waits, full-ID cooldowns, stable display
order, camera selection and drawing existing unspawned pawns at four rotations.
Direct owner insertion isolates these tests from handler scheduling; it does not
claim to validate a complete colony's hauling and handling workflow.

The drawing microcheck warms native graphics, binds the drawing delegate once,
and measures 300 submissions for 1, 6 and 100 occupants (the 100-pawn case bypasses capacity through direct owner insertion for stress testing). It includes native render
tree allocations and batched draw-queue growth within one real frame. It is a
targeted submission-cost check, not a zero-allocation claim, GPU benchmark or TPS
benchmark. The barn packs pawns by a 1.5 current-BodySize-per-tile visual budget (with up to 72 cached pawns) and draws all 18 real stored food stacks per frame;
species selection and size-aware layout run only on simulation intervals.

## Remaining manual scenarios

1. In a disposable colony, build wooden and granite barns from the Misc
   construction tab. Assign a handler and hauler. Queue cows, pigs, chickens and a calf with
   its mother; load actual hay/kibble/meat using workers. Change food/species/sex
   filters and verify hauling, roping, relocation and service jobs complete.
2. Remove suitable food and watch hunger/malnutrition. Allow nursing, exhaust
   the mother's reserves, restore her food and confirm nursing resumes. Save
   while a handler is actively milking/shearing, reload and finish that job.
3. Save just before birth and during a newborn/lactation transition. Reload,
   verify one real litter and no invisible map pawns. Repeat with surgery and
   other map-dependent animal services and with the intended external mods.
4. In Animals, toggle the independent barn-visibility setting. Click contained
   and ordinary animals. In Cargo, inspect/select/unload animals and partial
   food stacks. Direct clicks and drag selection over the shelter must select
   the building; clicking a displayed sprite must not produce another object.
5. Ignite wooden shelters with different fire sizes, let fire run, extinguish,
   then destroy a damaged shelter. Compare burns over time, corpses and living
   evacuees. A granite shelter without actual combustion must not burn residents.
6. Inspect 1, 6 and 100 stress-test residents in all building rotations and camera zooms.
   Compare juveniles, very large animals and visual hediffs/alternate graphics.
   Check material tint, foreground beam occlusion and normal gameplay frame time.

The building is available in the Misc construction category (180 selected
material, 40 steel, 4500 work). The processed art is still a development
canopy and needs visual approval in actual gameplay.


## Artwork provenance and decision

One built-in ImageGen call used a local contact sheet of actual RimWorld 1.7
EggBox, Shelf, PenMarker, Fence and FenceGate textures as style references. The
exact prompt is in `art-prompt.txt`. The generated source is preserved locally in
`docs/animal-barn-art-source.png`; the vanilla reference textures are not shipped.
The processed preview is `art-preview.png`.

The PNG has real alpha, including the open center. Processing removed isolated
artifacts, reduced it to five neutral gray tones plus transparency and produced
192 x 384 rear/front textures under `Zoology/1.7/Textures/Things/Building/Zoology`.
Both layers take the building's stuff color. The foreground uses PawnState
altitude, above the pawn render tree; BuildingOnTop is below pawns in 1.7.

The result is readable, geometrically simple, tintable and close to the vanilla
palette, but reads as a frame rather than a finished roofed canopy. The building
is buildable while its art remains under review. No repeat generation was requested or performed.

Native-sized rendering: the root matrix uses Vector3.one, as on the map. The
cached layout now packs each resident independently in two dimensions, and also
limits displayed body size to three adult cow equivalents. Large graphics stay
at their native scale and cannot overlap other animals' footprint. All 18 real food stacks appear in six groups of three along the edges. Small residents can still fill six slots. Seeded vanilla malnutrition recovery differs between
pawn IDs; the sick comparison starts at severity 0.35 to avoid crossing the 0.2
stage boundary for only one member of the pair.

Additional fixture assertions exercise ten-cow admission limits, rejection of an
extra adult, mixed cow/chicken sprite packing, stored food display and body-size
state across the existing save/load cycles. They require the installed RimWorld
1.7 environment; a source-only check is not equivalent to a successful run.

## Configurable barn settings validation

The game fixture checks the default limit (32.5), 10 and 100 bounds,
clamping above 100, the per-feature runtime gate and removal/restoration of
the Misc architect designator. The per-feature checkbox is handled by the
same settings hash/rebuild logic as the other runtime toggles; the slider
updates live capacity without repatching Harmony methods. The fixture
restores the user's original setting values after these checks.

During a manual game test, reduce the capacity below current occupancy:
existing residents should stay, while new admissions are denied. Then
disable barns and confirm existing barns unload animals/feed, stop supplying
specialized animal handling, and disappear from the build list. Re-enable and
verify new livestock can be admitted and the build icon returns. Restart with
the disabled setting saved, and ensure the build icon stays hidden.

## Race-sized capacity and current-size drawing

The admission limit now charges race.baseBodySize for all age classes, not a
juvenile's current BodySize. This prevents growth alone from causing an
overfull barn. A 150-tick capacity reconciliation unloads the latest contained
residents when a lowered limit or changed race definition causes overflow,
using native ThingOwner.TryDrop, and cancels requests that no longer fit.
The fixture compares calf/adult reservations and verifies live, spawned
overflow evacuees.

The visual cache continues to use current Pawn.BodySize. Each of the 18
interior tile regions permits a distributed total up to 1.5, while graphic
footprints must not overlap. Juveniles can therefore occupy more visible
positions than adults. Assertions cover three cows, twelve chickens,
juvenile/adult pigs and tile budgets, independent of the 18 real feed stacks.
The 72-renderer ceiling prevents unbounded draw cost.


## Ideology animal-mass thought

The fixture evaluates the original `PawnUtility.PlayerAnimalBodySizePerCapita`
before storage, after housing a cow, while barn functionality is toggled off,
and after releasing the cow again. It verifies the precise expected increment
of the resident's current BodySize divided by vanilla's free-colonist count,
then confirms the released cow is not double-counted. No IL scan or
unrecognized-source warning remains. The actual mood thought should also
be checked in-game with Ideology installed.


## Multiple barns: prioritized feed distribution

The 1.7 test fixture compares barns with no feed, equal feed and different
numbers of cows, and an empty barn. A stocked nearby barn must lose priority
to an unfed occupied barn regardless of distance. Supplying food lowers
restocking priority; adding another resident increases it. Food a cow cannot
eat must not count as accessible nutrition. The fixture also checks that
dropping actual inventory invalidates the food-coverage cache.

Manual playtest: put one fully fed barn close to the food stockpile and one
unfed barn farther away, both with hungry residents and permitted food.
Haulers should deliver to the less-supplied barn first; when their relative
coverage becomes equal, ordinary path length determines their next target.
Test with mixed food diets and multiple haulers to check that existing
building reservations avoid redundant deliveries. No stocking cap beyond
the existing 18 real stacks was introduced.


## Temperature exposure in indoor and outdoor barns

The test fixture checks that an unspawned barn resident still resolves
`MapHeld`, `PositionHeld` and `AmbientTemperature` through the spawned
barn rather than receiving a fixed temperature. It temporarily sets the
temperature of the actual room containing a test barn to extreme cold and
heat, advances the barn's existing physiology ticks, and checks that vanilla
Hypothermia and Heatstroke hediff severity increases. Temperature and game
ticks are restored in a finally block. Following save/load, it also checks
that a resident's ambient temperature continues to equal its map cell.
The test does not add synthetic hediffs or override the creature's normal
temperature tolerances.

In ordinary play, place one barn outdoors and another inside a heated or
cooled enclosed room. Compare their animals' ambient exposure and health;
neither construction materials nor the barn's artwork should grant thermal
insulation. The game fixture runs only under its existing explicit test flag.


## Barn construction ghost and depth order

For all four rotations, the fixture now calls the placement `PlaceWorker`
with `thing == null` (normal placement and Build Copy) and with an actual
building, then calls `GenDraw.DrawInteractionCells`. Any exception fails
that rotation's check. The interaction offset must lie outside the barn's
occupied rectangle. The static frame must use `MapMeshAndRealTime`, so
it draws in the same map mesh as regular columns and walls; resident/food
visuals and the front wall occupy adjacent low building layers instead
of the much higher `Pawn`/`PawnState` layers.

Manually inspect a placed 3x6 barn next to a lower/southern column and
rotate it in the Architect and Build Copy ghost. Check that the foreground
wall and interaction marker remain visible while adjacent columns have
normal layering.


## Pen/barn special filters, feed defaults, and animal corpses

The vanilla pen-marker `ThingFilter` and Zoology barn `ThingFilter` now
recognize five shared special filters: reproductive adults
(`LifeStageDef.reproductive`, except lactating females), independent
juveniles, milk-dependent young, lactating females, and non-lactating
**adult** females. The final group requires a reproductive life stage
and absence of the Zoology lactation hediff. It never matches female
calves, piglets, chicks or other nonreproductive young. Both lactation
checkboxes are hidden in both GUIs when the vanilla female checkbox is
disabled. The native pen `AcceptsToPen` and barn acceptance use the
same filter behavior without additional per-tick checks.

A new barn defaults to only hay and kibble. Other eligible nutrition-giving
items are selectable in its storage tab; corpses and fertilized hatching eggs
are not selectable as feed. An actual corpse produced by a resident's death
is dropped out of the barn using the normal `ThingOwner.TryDrop` operation
on its next tick, and retries if no placement succeeds.

The fixture checks all three age groups, adult females with and without
lactation, adult males, female calves and chicks. It verifies that excluding
non-lactating adult females affects neither juvenile category, including
in vanilla pens, and that lactating adults remain allowed when ordinary
adults are excluded. It also checks dependent UI visibility, default
hay/kibble, optional food, corpse rejection/drop and save/load persistence.

## Nursing-dependent young and nursery redistribution

The special filters now distinguish adult reproductive animals, independent
juveniles of any age (including chicks and reptile hatchlings), and
milk-dependent mammalian infants as recognized by Zoology's existing
lactation system. Lactating adult females are exempt from the general
adult exclusion, but still obey the female and lactation-specific filters.
The milk-young selection is saved and restored by the standard ThingFilter.

A separate Handling work giver uses the existing vanilla `RopeToPen`
job and Zoology's barn intake/release drivers for pen-to-pen,
pen-to-barn, barn-to-pen and barn-to-barn transfers. Its candidate
locations and comparisons are built at most every 250 game ticks,
only when a handler looks for work. A map-level pending assignment
prevents simultaneous handlers from issuing contradictory moves.
The plan considers actual milk-dependent offspring, current compatible
mothers with sufficient nutrition and the offspring's real food drain.
It never removes the last compatible mother while she is still needed.

The fixture constructs two barns with ten piglets in each and two
lactating mothers in the first. It checks that transferring one mother
improves provision, that transferring the last compatible mother is
rejected, and that a milk-only destination accepts lactating adults
even when ordinary adults are excluded. It also verifies that once
mothers are distributed, an immediate reverse transfer is rejected.
Full multi-handler and pen-to-pen gameplay testing is still required.
