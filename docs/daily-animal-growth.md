# Daily animal growth

Growth uses the installed species-specific LifeStageDefs from AnimalStats.
Neither the game nor Checker creates extra per-day LifeStageDefs. The original
life-stage boundaries, species attacks, graphics, reproduction and feeding rules
are retained.

## Model

For biological age \(a\) between successive race stage ages \(a_0\) and
\(a_1\), define

\[
t = \mathrm{clamp}((a-a_0)/(a_1-a_0),0,1).
\]

The game evaluates this using the beginning of each biological day (60,000
biological age ticks), independently of world time and age acceleration.
A stage quantity with endpoints \(x_0\) and \(x_1\) is evaluated as
\(x=x_0+(x_1-x_0)t\). The source numbers remain the calculated AnimalStats
stage endpoints, not hardcoded species overrides. This is linear interpolation
of the *game stage factors*, not a claim that real body mass grows linearly.

The pawn's original RimWorld BodySize and HealthScale getters remain the
authority for all other modifiers. Growth replaces only the current-stage
coefficient by multiplying the original result by \(x/x_0\).
StatDef factors, hunger-rate multiplier, market value and natural attacks have
their own corresponding adjustments. When a natural attack is used, the correct
CE/vanilla stage coefficient is interpolated by Tool and attack component
(damage, cooldown, sharp AP, blunt AP); the shared Tool or LifeStageDef is
never mutated. Adult pawns retain the original behavior.

The graphic's root transform interpolates a size-normalized footprint
between the current and next stage. For each endpoint, divide the authored
`bodyGraphicData.drawSize` by the square root of that stage's game
`bodySizeFactor`; interpolate these normalized dimensions and multiply by
the square root of the interpolated body factor. At the beginning of the stage,
the transform equals 1, preserving the authored drawSize. At the next stage
boundary it matches the next graphic's native drawSize, avoiding a size jump
caused by changing the graphic. In particular, smaller authored juvenile
drawSizes are no longer cancelled by the runtime scaling patch. This is a
visual convention, not a conversion of kilograms to BodySize. The runtime
never rebuilds the graphics or alters the underlying textures.

Spectrobionta are interpolated between consecutive life stages *within the
same race*: e.g. small -> medium -> large predimago when those stages exist
in that race's lifeStageAges. Metamorphosis between different races/PawnKindDefs
is not an interval in a single race's lifeStageAges and therefore remains
discrete. No stage is excluded solely because its name starts with
Spectrobionta. Unmapped animal defs and missing kind graphic data safely
retain vanilla behavior. Actual stage transitions remain discrete.

## Performance design

- One per-pawn snapshot is lazily recomputed once per biological day
  or after a change of current stage/kind.
- Snapshots use weak keys, with no world or map-wide pawn scan, polling
  component, persistent save data, LINQ, or per-tick allocation.
- Species stage intervals and stat-factor dictionaries are built once
  at startup, after species-specific stage installation.
- Harmony getters perform only indexed/dictionary lookups and a cached
  multiplicative factor during normal execution.
- Stat parts are installed only on stats whose stage factors differ and
  MarketValue; natural-attack factors are read only on demand.
- Full render-tree preparation, graphics loading and texture rebuilding
  are *not* performed by this system. Root scaling is restricted to actual
  young animals and preserves existing node transforms.

No numerical FPS/TPS guarantee is implied. Runtime comparison with the
growth patch disabled is required to quantify overhead and detect
interactions with external performance mods.

## Required in-game validation

1. Load RimWorld 1.6 and 1.7 with the generated stage profiles. Confirm the
   log contains daily growth initialization, and no Harmony patch errors.
2. Use **Zoology → Inspect animal life-stage profile** in developer mode.
   Select newborn, mid-stage and near-stage-boundary animals. The diagnostic
   reports biological stage progression, BodySize/HealthScale multipliers,
   visual root scale and actual stage/combat stats.
3. Compare BodySize, HealthScale, MoveSpeed, MarketValue, hunger-rate
   behavior and sprite footprint before/after biological-day boundaries.
   Values must remain unchanged within the same biological day, change on
   the following day, and match stage endpoints at transitions.
4. Specifically exercise tiny baby birds, swine, the Royal megachicken with
   five stages, a large theropod, crocodiles and a Spectrobionta animal.
   For Spectrobionta, verify daily changes between small, medium and large
   predimago (where defined) and between larval size stages, without
   interpolating the cross-race metamorphosis. Verify that stage-gated
   behaviors and graphics identities do not change before the original
   stage boundaries.
5. With and without Combat Extended, compare actual natural attack damage,
   cooldown, both armor penetrations and CE armor durability at young and
   adult stages, and confirm there is no double scaling.
6. Test barn-contained animals as they grow. Existing 150-tick placement
   refresh must react to changed BodySize, avoid crossing fences, and stay
   within displayed occupancy limits.
7. Save/reload during a stage, age animals while unspawned, and confirm
   snapshot recovery without daily replay or additional save records.
8. Profile 1000 young animals for TPS and a zoomed-in mixed barn for FPS,
   comparing the same save with and without this runtime feature.

Source locations: `ZoologyAnimalGrowth.cs`,
`ZoologyAnimalGrowthStatPart.cs`,
`Patch_ZoologyAnimalGrowth.cs`, and the existing
`ZoologyLifeStageProfileInstaller.cs`.

## Pedomorphosis compatibility

Spectrobionta pedomorphosis stops biological age inside the current race.
The daily growth snapshot depends on biological age day, stage and PawnKind,
so it naturally retains the same BodySize, HealthScale, movement, attack
coefficients and render scale as long as the age remains frozen. Releasing
pedomorphosis resumes biological growth on subsequent biological days.
No special per-pawn polling is required in Zoology and no stage transition
or metamorphosis is simulated by interpolation.

Keep `SpectrobiontaPredimagoMedium` as a distinct stage even though the
small and medium predimagos share the Carnophagus texture. It supplies a
biologically calculated piecewise growth endpoint and, independently, a
second allowed pedomorphosis stop, while the graphic transition to the
Harpactotyrannus appearance remains at `SpectrobiontaPredimago`.


## Debug age actions and compiled assemblies

`Make +1 Day Older` and `Make +1 Year Older` change the pawn's
biological age. The growth snapshot is keyed by the resulting biological
day, so the vanilla debug actions require no special hooks. Each year in
RimWorld contains 60 biological days: a one-year increment can cross
several stages in a single action and therefore cannot demonstrate the
visual interpolation inside those skipped intervals. For a valid check,
use repeated one-day increments on a young animal.

RimWorld executes `ZoologyMod.dll`, **not** C# files in `Source`.
Updating the Git checkout or regenerating `LifeStageProfiles_Generated.xml`
does not compile the DLL. After changing source code or generated attack C#,
rebuild the assembly for the actual RimWorld version and restart the game.
For RimWorld 1.7 the loaded file is
`Zoology/1.7/Assemblies/ZoologyMod.dll`. The repository's binary was
last updated on 2026-11-05, before dynamic growth source was introduced
on 2026-11-08. The 1.6 and common assemblies are older still.
Do not infer support from the presence of new `.cs` files.

Select a pawn using **Zoology -> Inspect animal life-stage profile** after
rebuilt assembly deployment. The result reports biological age ticks,
current biological day, Harmony hook presence, stage specialization,
interval progress, BodySize/HealthScale ratios and draw scale. If the
interval is unavailable, the reason is printed. Compare the diagnostic
before and after several `Make +1 Day Older` operations; both the
effective BodySize and draw scale should change while the same
life-stage index remains active. If body ratios change but graphics do
not, investigate rendering/cache behavior, not AnimalStats formulas.
If Harmony hooks show false, inspect the compiled DLL and runtime patch
settings. These developer-only checks add no per-tick overhead.
