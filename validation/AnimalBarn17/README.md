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

The shared vanilla `ThingFilter` has six Zoology special filters:
reproductive adults, independent juveniles, milk-dependent mammalian
young, lactating adult females, pregnant adult females, and other adult
females (neither lactating nor pregnant). Adults and sex are mandatory
parent conditions: disabling "adults" excludes all adults including
pregnant and lactating females. Both pregnancy and lactation exclusions
apply if both statuses are present. The three female state filters
appear only when adults and females are both selected, in pens and barns.

The fixture checks the full category matrix for adult males, pregnant
females, lactating females, ordinary adult females, nursing young and
independent chicks, including vanilla pen filter acceptance and UI hiding.
It explicitly changes an existing adult-only pig barn to "allow all"
and verifies that a sow and suckling piglet become admissible and can
both be queued for intake. Ordinary and manual barn Handling work
givers are directly orderable by right click, avoiding reliance on
vanilla pen-only commands.

Fresh barns default to hay and kibble; other compatible nutritional
items can be enabled individually, but corpses and hatching eggs
are never valid feed. A corpse from an animal dying inside is
released intact from the ThingOwner at the next successful opportunity.

## Nursing-dependent young and nursery redistribution

A separate Handling work giver redistributes nursing mothers between
enclosed vanilla pens and barns based on actual infant nutrition
demand and species-compatible mothers. It cannot remove the last
compatible mother from a litter. Planning snapshots are reused for
250 ticks and run only when Handling work is requested.

The destination must now explicitly allow adults, females and
lactating females, as well as milk-dependent young if offspring
are to be stored. A restricted nursery can disable "other adult
females" rather than disabling the adult age category. The fixture
checks that disabling adults excludes a nurse and that enabling
adults while excluding other adult females permits that nurse,
in addition to the two-barn nursing distribution test.

## Taming without any vanilla pen

Vanilla `JobDriver_Tame.MakeNewToils` ends with a one-off
`MessageTameNoSuitablePens` notification whenever it cannot lead a newly
tamed roamer to a pen marker, independently of the persistent
`Alert_AnimalPenNeeded`. The existing barn alert patch does not suppress
this notification. A targeted Harmony postfix now wraps only the final
taming toil: after success, a reachable accepting barn can be selected
by the existing Zoology worker and the tamer immediately starts the
ordinary barn rope job instead of emitting the false warning. Vanilla
pen routing and its message remain unchanged when no valid barn is
available; a closer suitable enclosed pen retains priority.

The fixture verifies installation of the tame-job patch and that the
wrapper preserves all earlier vanilla toils and the original final
action when no eligible animal/job exists. It also checks the standard
barn admission condition. In-game: tame a cow on a home map with an
available barn but no pen markers and confirm the tamer leads it inside
without a no-pen message. Repeat with the barn full, forbidden, filtered
against that cow, or disabled: the vanilla warning must still appear.

## Visual packing and command icons

The previously one-sprite-only branch for oversized body graphics
has been removed. A bounded scale is stored alongside each visual
pawn placement, keeping the rendered sprite and collision footprint
consistent without modifying the pawn's actual BodySize or
race-based barn capacity. The fixture asks for multiple visible
camels, checks the 1.5 BodySize-per-tile budget, and retains a
large-animal drawing check. The normal manual-load and cancel-load
gizmos use the vanilla transporter icons; no custom art assets
are required. Visual confirmation in RimWorld 1.7 remains required.
