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

The graphic's root transform is scaled against an adult visual reference
using the square root of the ratio of current to adult game BodySize. Stage
graphic draw sizes are accounted for so a graphic swap does not introduce an
artificial size jump. This is an intentional visual convention, not a
conversion of kilograms to BodySize. Existing textures, mesh materials and
stage-specific visual identities remain unchanged; no daily graphic rebuilds.

For known metamorphic Spectrobionta stages growth is not interpolated.
Unmapped animal defs and missing kind graphic data safely retain vanilla
behavior. Actual stage transitions remain discrete.

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
   Verify that stage-gated behaviors and graphics identities do not change
   before the original stage boundaries.
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
