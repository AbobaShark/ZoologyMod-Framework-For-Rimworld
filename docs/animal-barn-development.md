# Animal barn — construction-ready gameplay implementation (RimWorld 1.7)

The barn is now available in the Misc construction category, with a cow-equivalent body-size limit and side-displayed real food stacks. The earlier 2026-11-04 audit compiled and exercised the container, physiology, feeding, save/load, selection, rendering and fire workflows in RimWorld 1.7.4998 rev1299. The new capacity/rendering/construction changes have source-level checks and additional test fixture assertions but have not yet been re-run in the installed game. Full-colony handling, births and external-mod integrations still need validation; the canopy art remains a development frame.

## 2026-11-04 audit

- Critical fix: 1.7 `Thing.DoTick` automatically ticks a holder's contents after the building tick. The barn previously also ticked physiology/items manually, causing double food/age/health and item simulation and allowing unwanted Pawn ticks. The barn now implements `IThingHolderTickable` with `ShouldTickContents = false`; its existing explicit vanilla tracker/item dispatcher is the sole simulation path. Controlled fasting now matches map controls.
- Feeding uses `RaceProps.FoodLevelPercentageWantEat`, matching vanilla `JobGiver_GetFood`, instead of a fixed 0.35. Nursing runs under its own existing hungry-baby eligibility first. Each eligible item passes `FoodUtility.WillEat` and eater-specific finite positive `NutritionForEater`; `Thing.Ingested(NutritionWanted)` consumes real whole units and returns nutrition applied once through the ordinary need setter. Exceptions and zero-yield items do not stop the search for another real food stack. Short stacks can be followed by another eligible stack.
- `releasedForService` remains transient and is never serialized. The native saved wait job now names its barn as target; the first service interval after map load reconstructs tracking from those jobs. Admission recognizes that saved job even before reconstruction. Persistent cooldown keys use full `ThingID` strings because numeric IDs are per def and can collide between species. No old unpublished-save compatibility path is retained.
- XML disables `containedPawnsSelectable`. Vanilla Animals rows already use `CameraJumper.TryJumpAndSelect`, whose holder traversal chooses the barn. No selection Harmony patch was added. Cargo still explicitly selects an individual animal and opens its info card or unloads it.
- Drawing uses each animal's **current Pawn.BodySize**, independently of storage capacity and texture dimensions. The 3 × 6 interior allows 1.5 BodySize per tile. Spatial overlap and tile load use virtual footprints proportional to current BodySize, with no dependence on `BodyGraphic.drawSize` or transparent margins. The original pawn render trees draw at native scale, without shrinking, clipping or a texture-driven restriction on the number of visible animals; large textures may visually extend beyond their virtual footprints. Residents have separate whole-render-tree depth ranks to prevent node-by-node intersections. A once-precomputed, farthest-first position sequence spreads small flocks across the safe interior rather than packing them around the center; even small animals' center positions have an extra fixed inset from the fence, independent of texture dimensions. Flying animals land and clear stale animations before housing, and old held flight animations are reset on save loading. Up to 72 render slots provide a performance safety ceiling; there is no artificial six-animal or zoom-out cap. The 150-tick refresh prepares deterministic positions and all 18 actual food stacks along the edges. No owner scan or simulation work runs during DrawAt.
- Every 150 ticks, actual static fires in the occupied footprint (or an attached fire) are checked. Only a flammable, currently burning barn exposes residents. The existing 1.7 `Fire.DoFireDamage` method supplies intensity-dependent damage, random rounding, outside body regions, flame damage workers and battle logs. No temporary Spawn or stored 'was burning' flag is used. The vanilla enclosing-container death path produces a corpse in the owner; destruction releases survivors.
- The 3 × 6 shelter accepts Woody, Metallic and Stony stuff. Construction requires 180 selected material, 40 steel and 4500 work, and is available in the normal Misc tab. Earlier runtime checks confirmed differences in wood/granite durability, flammability and color; the revised recipe awaits an in-game construction check.
- Current 1.7 builds reference installed `UnityStubs`; the 1.6 Unity references and 1.6 build behavior are unchanged. All new barn runtime code is under `RIMWORLD17`.

Reproducible fixture, raw latest results, comparison methodology, art prompt and remaining manual steps: `validation/AnimalBarn17/` at repository root.


## Animal handling and filters

- A 3 × 6 barn reserves **ThingDef.race.baseBodySize** per resident, not the growing pawn's current BodySize, against the configurable total limit (10–100; default 32.5). A calf therefore reserves the same capacity as an adult of its race. This prevents growth alone from overfilling the barn. Every 150 ticks the barn also reconciles actual occupancy with the current settings: an overfull barn drops the last stored residents into the map until within capacity, relinquishing reservations for animals already outside first. Queued manual admissions that no longer fit are cancelled. Failed drops are retried without destroying the animal. Admission, newborn litters, transfers and milk/shearing service reservations all use race baseBodySize. Occupants remain individual pawns in the saved ThingOwner.
- The Animals tab uses the same ThingFilter UI as vanilla pen markers. Admission observes selected species and male/female filters. A separate optional setting hides contained animals **only** from the vanilla Animals management tab. The original MapPawns roster and auto-slaughter remain unchanged; the visibility Harmony patch is installed only while that setting is enabled.
- The Nutrition tab uses the unmodified vanilla biosculpter storage-filter interface and StorageSettings. The player's filter alone determines which edible items may be loaded; there is no extra per-species loading filter, and food may be stocked before animals arrive. The development food-loading menu also restricts choices to real haulable Item-category foods that currently fit the filter and available stack space.
- A handler uses the vanilla rope-to-destination driver rather than a Goto order. An animal already in a valid pen stays there; when a pen is closer than a barn, the pen takes precedence. The barn work-giver's priority is synchronized with the vanilla pen rope work giver at startup.
- A barn on a player-home map is recognized as player infrastructure even when spawned in debug mode without an owner. A suitable barn satisfies the vanilla **Animal pen needed** alert without changing the normal pen marker rules; no handlers, forbidden species, full barns or inaccessible entrances do not hide the warning. The vanilla right-click **Lead to pen** order still falls back to a suitable barn. Barn intake and specifically queued barn loading are now also directly orderable through the vanilla work-giver context menu, so an animal waiting for a barn is not limited to an irrelevant «no suitable pens» command.
- The **Assign animal loading** command queues specific, player-selected Roamers. An additional, higher-priority Handling work giver ropes these animals into that barn, bypassing pen preference and barn species filter. Safety and space constraints still apply. Manual requests survive saves and can be cancelled.
- If a resident is now forbidden by the barn's species/sex ThingFilter, a higher-priority Handling work giver searches for an available enclosed vanilla pen, another accepting barn, a caravan hitching post, or an unenclosed pen as the final fallback. Only if an accessible destination exists does the handler go to the source barn, physically release the animal and queue a native rope job (or Zoology's existing barn rope job) for the same handler. Until a suitable destination exists, the animal stays contained; a full or forbidden alternative is not selected. No new Harmony patches or persistent relocation state are needed.
- The native-style **Cargo** inspect tab replaces the temporary animal-inspection gizmo. As in the vanilla shuttle it displays pending manual loading orders on the left and the actual `ThingOwner` contents (animals and food) on the right, with vanilla info-card, selection and remove controls. Cancelling a pending row removes only its loading request; removing a contained animal releases it normally, without any admission cooldown. Stored food can also be dropped by stack count.
- Haulers deliver actual food items. Maximum food storage is **18 stacks** (the combined capacity of three vanilla large shelves); animals occupy separate container entries and do not consume food slots. Compatible partial stacks can be topped up even if all 18 slots are in use. Vanilla ThingOwner handles stack merging and partial transfers, with acceptance recalculated on delivery. The limit does not depend on the animals' food needs or the nutritional value of any item.
- During each existing 250-tick feeding check, individual animals consume only food that vanilla FoodUtility.WillEat allows for their species. Animals sharing the barn need not eat the same foods; selecting food for storage does not force an animal to consume it.
- Food remains real, ticked Things. Normal item ticks, spoilage and temperature-dependent rotting are retained; the barn does not refrigerate food or reset its spoilage timer. Non-animal objects temporarily retained because egg placement failed also occupy stack slots, without making fertilized eggs eligible for normal food hauling.

## Physiology and husbandry

- Occupants retain individual health, needs, age and resource production while ordinary pawn AI is suspended. Contained animals stop simulated sleeping at full rest (100%); after updating needs, any overfilled rest is normalized to the ordinary maximum, including when Need Bar Overflow modifies the setter. This shared physiology fix also applies to animal refuges, without a new Harmony patch.
- Vanilla and compatible gathering jobs operate on temporarily spawned animals, up to two at a time. Animals return to the barn if still eligible and near its entrance. The temporary Pawn-keyed released-for-service dictionary is no longer serialized: on save load, spawned service animals resume through ordinary map jobs rather than requiring fragile cross-reference dictionary working lists.
- Mammalian newborns can be fed by their biological mother or another compatible, lactating female in the same barn. Transfer consumes the female's actual food reserves. Hunger in a known biological newborn can restore a co-housed mother's expired lactation (respecting severe malnutrition), just as on the map. The shared suckling routine also supports caravan co-location, replacing a separate caravan-only nutrient-transfer algorithm.
- A persistent alert identifies newborn mammals with no compatible lactating female in their barn. RimWorld's own starving-animals alert includes stored animals and their names.
- Mating intervals use the species' original mateMtbHours. Pregnancy and egg fertilization use RimWorld's PawnUtility.Mated.
- The vanilla birth routine retains litter size, parents and genetics. Newborns are placed at the entrance and admitted together after the birth. If there is insufficient capacity or the offspring are disallowed by the filter, mother and litter stay outside.
- Auto-slaughter calculates targets over both spawned and contained animals, respecting vanilla species, sex, age, bonds and pregnancy settings and Zoology's lactation protection. Selected stored animals are released for real slaughter jobs.
- There is no manual-release admission cooldown and no associated map component. After unloading, the ordinary pen/barn workers can immediately manage the animal according to the current filters. Exception: animals with surgery bills or medical-rest needs cannot be automatically re-admitted while care is required. During the existing 250-tick service check, medically needy residents are released if a reachable medical or ordinary animal bed has room; vanilla patient and doctor jobs choose the actual bed, prioritizing medical beds and perform all treatment outside the barn.
- Vanilla and compatible egg layers deliver actual eggs to adjacent vanilla CompEggContainer boxes, respecting their native capacity and stack compatibility. Overflow eggs appear on the map; unfertilized eggs can be stored as food if permitted. Fertilized eggs retain native incubation and spoilage. VEF exploding eggs stay outside boxes.
- When a stack of fertilized eggs hatches in any adjacent CompEggContainer, the vanilla CompHatcher.Hatch completion hook collects only the newborns spawned by that hatch after parentage has been set. Eligible player-owned Roamers are admitted directly into the barn, one by one, through ordinary species/sex/capacity checks. If ten babies hatch with five available slots, only five are admitted and the other five remain spawned on the map. Hatching in unrelated egg boxes is never modified: the hook checks the eight neighboring cells via the vanilla ThingGrid and returns without allocating its hatch context when no barn is adjacent. No egg scanning or extra polling is performed each tick. Moving an egg box or barn changes their relationship automatically, without cached adjacency state.
- VEF CompAnimalProduct and its custom output continue through the original component and real gathering jobs. The original CompEggLayer.ProduceEgg is used for VEF crossbreeding patches. VEF CompExplodingEggLayer is called through its own methods rather than simulated with generic eggs.
- VEF CompAsexualReproduction and HediffComp_AsexualReproduction receive brief real-map simulation sessions instead of having their Map-dependent logic skipped in storage. The caravan-only product component remains caravan-only.

## Still required before release

- Complete-colony verification of handling/hauling, active milking/shearing across saves, birth transitions and the full intended mod list. Core automatic game checks are now covered by the dedicated barn fixture.
- Game tests for Vanilla Expanded Framework and Alpha Animals special components, genetics, pregnancy, diseases, medical care (including operation bills and bed selection) and egg handling. Other map-dependent effects still need examination; these targeted integrations do not guarantee compatibility with every animal.
- Additional incubation controls and policies for retaining fertilized eggs independently of vanilla egg boxes; overflow currently goes to the map.
- Full integration into standard animal lists and validation of auto-slaughter UI counts.
- Full-colony balancing and a final canopy artwork decision. The building is now visible in the construction menu; test its recipe, orientations, footprint and visuals in ordinary play.
- Controlled TPS benchmarks for 30 and 100 animals with population and physiology held constant.

The building can be constructed in normal play, but the new admission/layout changes and the complete husbandry workflow require an additional 1.7 playtest before treating this as a finished feature.
## Configurable barn capacity and feature toggle

- **Other Behavior** settings contain an enabled-by-default animal barn checkbox and a total BodySize slider (10–100, default 32.5, increments of 0.5).
- The inspect pane shows only current/maximum total body size; the cow-equivalent note has been removed from localized text and building descriptions.
- New limits apply immediately to ordinary admission, transfer, manual intake and newborns. Shrinking the limit below current occupancy unloads enough residents at the next 150-tick reconciliation, leaving the animals alive on the map. Growth within the same race does not change capacity usage.
- Toggling barns off hides their architect entry and makes them inoperative, releasing live animals and stored feed via the existing ThingOwner path. The building itself is retained, so re-enabling barns restores their functionality. The global runtime-disable switch also disables barns.
- Disabling the feature omits its specific Harmony hooks on the normal settings patch-rebuild path. Changing only the numeric capacity does not require Harmony patching.


## Foreground wall in placement preview

The foreground wall is a separate high-altitude texture drawn after the contained animals. Standard RimWorld placement ghosts draw only the ThingDef's base graphic, so their lower/foreground wall was missing. A barn-only PlaceWorker now draws the existing AnimalBarnFront with vanilla ghost shading, centered using GenThing.TrueCenter for even-sized 3×6 rotations. It changes neither the textures nor the built-building draw order. The 1.7 validation fixture invokes the worker in all four orientations; an in-game preview check remains required.


## Ideology ranching compatibility

The Ideology Ranching: Central thought reads `PawnUtility.PlayerAnimalBodySizePerCapita()` directly from `ShouldHaveThought` and `PostProcessDescription`. Previous code incorrectly targeted `CurrentStateInternal`, found no collection and produced a warning without changing the mood.

The updated patch adds current BodySize for living player-faction animals stored in player-faction barns to the **return value of this vanilla method**. Its denominator is the same count of free, non-quest-lodger colonists across player maps/caravans/transporters; resident animals already included in PawnsFinder are skipped. Thus vanilla mood thresholds, expectation restrictions, description and other callers of the colony animal-mass statistic agree, without modifying game-wide pawn collections or applying an IL transpiler. The patch is only installed when Ideology and the barn feature are enabled. The in-game fixture now checks the actual value before loading, while housed, with barns disabled, and after release; it no longer tests a fictional getter in the thought worker.


## Feed distribution between multiple barns

The former feed work giver selected the nearest available barn and continued
filling its 18 slots regardless of whether other barns had no nutrition.
The barn-feed work giver now uses vanilla's prioritized target search and
ranks occupied barns by **available days of edible nutrition** relative to
their residents' actual `FoodFallPerTick`. The total-supply calculation
counts shared stacks only once; the per-animal minimum also catches cases
where some species cannot eat the stocked food. Equal-priority barns still
use vanilla distance tie-breaking. Empty barns can be pre-stocked, but rank
behind occupied barns.

The normal stock filter and 18-stack capacity remain unchanged. When choosing
food to haul, a hauler first prefers permitted food which at least one
current resident can eat, falling back to any allowed food if none is
reachable. The barn's priority is cached for up to 150 ticks and invalidated
on intake, release, stock transfer and consumption; work scanning does not
simulate extra food consumption. Existing barn reservations prevent two
haulers from accepting simultaneous jobs for the same destination.


## Ambient temperature and vanilla thermal health effects

A barn does **not** provide environmental insulation or its own temperature.
Its unspawned animals remain inside a spawned `IThingHolder`, so vanilla
`Pawn.AmbientTemperature` resolves via `PositionHeld` and `MapHeld` to
`GenTemperature.GetTemperatureForCell`: this is the actual enclosing room's
temperature or the outside temperature where the barn stands. The barn's
existing `AnimalGameApi.TickContainedPhysiology` calls the normal
`Pawn_HealthTracker.HealthTick` and `HealthTickInterval` on each resident,
including the vanilla hediff givers that apply hypothermia, heatstroke and
possible frostbite. No artificial thermoregulation or hediff severity rules
have been introduced. Construction materials and roof artwork alone do not
alter temperature exposure.

The RimWorld 1.7 fixture checks the contained pawn's map-cell temperature
before and after saving, and drives actual cold and heat exposure by changing
the test room's temperature while executing ordinary barn ticks. It checks
vanilla hypothermia and heatstroke progression and restores the original
temperature. A separate normal-play test in an enclosed temperature-controlled
room remains necessary.


## Barn depth and construction ghost

The static frame now uses `MapMeshAndRealTime` instead of drawing the whole
3x6 sprite late in the realtime pass. Like vanilla neighboring walls/columns,
it is printed into the ordinary building mesh. Cached food and animal visuals
are drawn at `BuildingBelowTop`; the separate foreground frame is at
`BuildingOnTop`. This replaces the excessively high `Pawn` and
`PawnState` layers, which overlaid nearby foreground columns regardless
of their placement. Interior animals remain behind the foreground wall.

The placement `PlaceWorker` now uses `Graphic.DrawFromDef`, not
`Graphic.Draw`: the latter dereferenced a null `thing` supplied by
Architect and Build Copy placement, aborting the preview before both its
foreground wall and interaction-cell marker could be displayed. Both ghost
layers use `Blueprint` altitude and the same occupied rectangle center
for all rotations. The game fixture checks both null and non-null ghost
calls, interaction-cell location, and building mesh mode in all four
cardinal orientations. Actual in-game draw ordering still requires a
visual test with a nearby vanilla column.


## Shared pen/barn animal filters and feed presets

Six 1.7 `SpecialThingFilterDef` workers extend the common vanilla
`ThingFilter` used by pen markers and barn intake. Three age groups
are mutually exclusive: all reproductive adult stages, independently
feeding nonreproductive juveniles (including chicks and reptiles), and
milk-dependent mammals identified by Zoology's actual lactation rules.
**The adult filter always applies to adult animals**, even if they are
pregnant or lactating; no hidden admission bypass remains.

When adults and females are both allowed, three more filters can restrict
adult females: lactating, pregnant, and other (neither lactating nor pregnant).
Pregnancy is checked using vanilla Hediff_Pregnant (including subclasses).
A pregnant lactating female must pass both state filters. Switching off
females or adults hides all three dependent checkboxes in both UIs,
without changing saved selections; reopening the parent restores them.
The existing nonlactating defName and saveKey remain stable while its
new label and matching logic mean "other adult females." New pregnancy
filters default allowed.

All checks occur only when the usual `ThingFilter` considers an animal:
there are no additional population scans or animal ticks. Automatic
admission and relocation both use these same filters. Existing
`ThingFilter.SetAllowAll(parentFilter)` resets special-filter exclusions,
including when an already-built adult-only barn becomes unrestricted.

New barns start with hay and kibble selected; other non-drug nutritious
items remain manually selectable. Corpses and fertilized hatching eggs
are excluded from the available-food list. Dead residents' real corpses
are dropped out of the barn using vanilla TryDrop, retrying if blocked.

## Milk-dependent young and automatic nursing-mother allocation

`AnimalNursingBalance` supplements existing pen/barn handlers and
lactation routines. During normal Handling job searches, it evaluates
compatible nursing females against milk-dependent young in enclosed
vanilla pens and enabled barns. Child demand comes from the real
FoodFallPerTick; candidate mothers must be alive, actively lactating,
able to feed, and species-compatible. It prioritizes reducing uncovered
infant demand, then distributing nutrition demand among mothers, never
leaving a formerly supported litter without its final compatible mother.

**No nursing exception overrides the adult filter.** To create a nursery,
enable adults, females, milk-dependent young and lactating females,
then disable "other adult females" and any other unwanted adult
categories. Vanilla adult males are controlled by the sex filter.
The map-level plan is cached for 250 ticks; only one assignment may
be outstanding at once, and existing rope/admission jobs do the actual
work. The separate pen-balancing patch prevents undoing maternal
reassignments when nursing young are present.

## Taming notifications and barn destinations

Vanilla taming has its own `JobDriver_Tame` final toil that emits
`MessageTameNoSuitablePens` if it cannot assign an animal to a pen.
This is **not** the same mechanism as the persistent
`Alert_AnimalPenNeeded`, which Zoology already adjusts for barns.
The new barn-only Harmony postfix attaches a fallback to that final
toil, allowing the successful tamer to start an actual
`Zoology_BarnAdmit` rope job when `WorkGiver_BarnAdmit.FindBarn`
finds an accepting, accessible barn. It preserves vanilla pen routing
when no barn is selected, including when every barn is full, disabled,
forbidden or unsuitable. The same filter, capacity and reachability
checks as normal automatic barn handling are reused; no artificial
pen marker is registered and no recurring work is introduced.

## Body-size-based animal display and barn commands

Visual packing uses only each animal's current `Pawn.BodySize`: a virtual
footprint proportional to that value determines positions and per-tile
visual load. It never consults `BodyGraphic.drawSize`. The vanilla
render tree draws at scale 1, so large original textures retain their
map size and are neither fitted to the opening nor cropped. The separate
race `baseBodySize` still governs actual barn capacity. The existing
150-tick visual cache avoids per-frame searches. Assign-loading and
cancel-loading gizmos use the vanilla transporter textures.
