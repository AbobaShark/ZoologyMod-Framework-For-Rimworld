# AnimalStats species-specific life stages

Branch: feature/animal-life-stage-profiles. Main remains unchanged pending a RimWorld 1.7 and CE runtime test.

## Data ownership

AnimalStats owns all numerical species-specific factors. The single generation
source is the LifeStageProfiles sheet; the service tabs compute mass, armor,
attack, CombatPower and MarketValue. Checker validates the calculated results
without implementing formulas for any of these quantities.

For Google Sheets and XLSX, Checker opens LifeStageProfiles **by name**.
TSV override:

    --sheet LifeStageProfiles=/path/to/LifeStageProfiles.tsv

Columns CalculationStatus and ToolStatus must both equal READY. Otherwise
that stage is omitted with a diagnostic; missing coefficients do not become 1.
The existing Adult LifeStageDef is retained: its AnimalStats row validates
the biological adult baseline, but Checker creates no species-specific Adult
clone for the ordinary AnimalAdult/EusocialInsectAdult family. The Royal
five-stage lifecycle and special Spectrobionta stages keep their identities.

For ordinary animals, the Juvenile biological mass is the arithmetic midpoint
between Baby and Adult masses, implemented as a spreadsheet formula referencing
both stage cells; it is not a published weaning mass. The mass contract rejects
a numeric Juvenile value inconsistent with that definition. Scientific evidence
for newborn and adult masses remains in their MassReference cells and notes.

ToolRecordsV2 contains one tab-delimited line per individual natural attack.
Its eleven fields are, in order: VanillaToolIndex, CEToolIndex, label,
linkedBodyPartsGroup, capacities, VanillaDamageFactor,
VanillaCooldownFactor, CEDamageFactor, CECooldownFactor,
CESharpPenetrationFactor, CEBluntPenetrationFactor.

The two indices are independent: Vanilla constructs head, leg, paw, horn,
bite; CE constructs head, paw, leg, horn, bite. Index -1 denotes an attack
missing in that mode, not an error. Zero CE penetration is valid. A missing
penetration component in **both** stage and adult reference corresponds to a
neutral ratio; if only one is missing, validation must not silently succeed.

The C# installer clones only LifeStageDefs referenced by a race's
lifeStageAges; unaffected fields from the original Def and its LifeStageAge
are preserved. LifeStageProfiles defines StageBodySize, StageHealthScaleFactor
and CEArmorDurabilityFactor. The CE durability factor is recomputed by exactly the adult
Animals CE!AC formula: MROUND(250*(BodySize+baseHealthScale),25),
where baseHealthScale = MROUND(BodySize * Groups.HealthMultiplier
* Base stats.Health Multiplier,0.05). Stage BodySize comes from
LifeStageProfiles.StageBodySize; the result is divided by the species'
adult Animals CE!AC value. This is not a skin-mass ratio and preserves
zero durability when the original 25-unit rounding yields zero.
A CE-only CompArmorDurability patch handles this value and avoids NaN
in curDurabilityPercent; shared CompProperties are not mutated. Tool identity is resolved against the loaded animal's actual natural attacks
by body-part group, label and capacities; Vanilla/CE tool indices are used
only as a tie-breaker. The installer emits one aggregate diagnostic for
unresolvable attacks instead of per-attack startup spam. Tool objects are
cached by reference. A CE verb returning a ToolCE copy is matched once
and cached for subsequent requests. No biomechanical calculation occurs
during an attack.

For species stages, `StageHealthScaleFactor` is evaluated using the
same body-size formula as `Animals!AP`:
`MROUND(StageBodySize * Groups.HealthMultiplier *
Base stats.Health Multiplier,0.05) / Animals.baseHealthScale`.
For tiny stages whose rounded health would be zero, the unrounded
numerator is used to retain finite positive health. The two CE AP
combat getters and CE stat panel are distinct code paths. The status
panel and both explanation paths use CE's original skill/stat factors
with the generated per-Tool CE penetration ratios; no ToolCE is mutated.

### Remaining age-size parity exception

Core MoveSpeed still uses the common LifeStages.SpeedFactor =
MROUND(bodySizeFactor^0.17,0.05). It is *not* the same operation as
recalculating a species' Base stats speed model and the Animals/Animals CE
movement mapping at the reduced StageMass_kg. The historical Warg example used a Juvenile mass derived from an obsolete
weaning-stage model. All speed parity comparisons must be rerun using the
current Adult/Baby midpoint masses; previously published numbers are no
longer valid. This discrepancy is
not silently treated as parity. Base stats!M contains empirical fixed
speeds as well as distinct species formula families; there is no
validated model for replacing every fixed-speed datum with a
mass-dependent speed, so no invented species-independent coefficients
are installed by Checker. Combat Power still uses the common age
speed factor as long as this exception remains.

## Spectrobionta life stages

AnimalStats LifeStageProfiles contains six custom stage identities owned by the
`Spectrobionta` source: `SpectrobiontaImago`,
`SpectrobiontaPredimago`, `SpectrobiontaPredimagoSmall`,
`SpectrobiontaLarvaLarge`, `SpectrobiontaLarvaMedium` and
`SpectrobiontaLarvaSmall`. LifeStageProfiles selects an age-stage
family from the group-level `Groups.LifeStageProfileFamily` column.
Three actual stages per species are passed to Checker; no
`AnimalBabyTiny` artifact should be generated for spectrobionts.
The custom LifeStageDefs retain only common visual, life-cycle, body
size and movement factors in the source mod. Zoology supplies
species-specific health, feeding, armor, market value, attack damage,
cooldown, CE penetration and combat power from evaluated AnimalStats.
The original source stage identity must be used when implementing
life-stage-gated gameplay in the source mod.

## Current generated artifacts

- Zoology/Core/Defs/Misc/LifeStageProfiles_Generated.xml: numerical scalars,
  including separate vanilla and CE armor factors.
- Zoology/Source/Behaviour/Shared/ZoologyLifeStageAttackProfiles_Generated.cs:
  separate Vanilla/CE Tool indices and six attack factors.

Both are generated from LifeStageProfiles. The preferred single-C# output is
not implemented yet, so neither file should be removed independently.
Checker uses its existing atomic generation route; it never compiles C#.

The branch must pass the Python tests, an actual RimWorld 1.7 C# build with
CE references and in-game pawn checks before being merged. The checked-in
RimWorld 1.6 DLL cannot establish RimWorld 1.7 compatibility.

Run from checker/:

    python -m unittest -v tests.test_body_size_mass_equivalence tests.test_life_stage_species_profiles tests.test_life_stages tests.test_royal_life_stages

## Local test after pulling the feature branch

Use this branch directly while validation is in progress. Do not merge it into
main to test.

1. Update to the feature branch and install the Checker Python dependencies.
2. Run the Patch Generator with only the life-stages category. A complete
   Generate All operation is not necessary for this test.
3. Let the standard Checker deploy step copy the generated XML and C# to the
   exact registered paths. Check its printed READY/INCOMPLETE summary.
4. Use the **existing** Zoology build/update workflow for RimWorld 1.7.
   Checker never compiles C#. Do not copy an old prebuilt ZoologyMod.dll over
   the newly compiled assembly.
5. Restart RimWorld completely, enable Development Mode, and open the
   debug-actions menu. Search for **Zoology** →
   **Inspect animal life-stage profile**, then click an animal pawn.
   The log prints the original/specialized LifeStageDef, physical BodySize,
   hunger rate factor, stat factors, effective stats, CombatPower, and
   independent Vanilla/CE ratios for each registered natural attack.

PowerShell from the repository's checker directory:

    py -m pip install -r requirements.txt
    py rimworld_patch_generator.py --source "gsheet:1BsPzRPFLFx2HL4UdlVo058kryub3C4a9ezQ54CnGEB4" --category life-stages --ce --auto-deploy

For a local XLSX, replace the --source value with its full path. Direct
Google Sheets access needs configured read-only OAuth credentials (see
PORTABLE_SETUP.md and requirements-google.txt); it is not supplied through
the ChatGPT connection on your computer. For an offline test, use a freshly
exported XLSX with calculated formula results. If XLSX formula caches are
missing or old, use the live Google Sheet source instead.

Recommended validation matrix (use actual species installed by your mod list):

- Polar bear / grizzly: adult, juvenile and baby; hunger, armor, melee,
  CombatPower and MarketValue; CE on and off.
- Ostrich / Cassowary / Emu: four natural attacks (head, two claws, beak),
  independent signatures and individual factors.
- Kangaroo from Animal Expanded, if installed: paired paw and leg attacks
  appear in different orders between Vanilla and Combat Extended.
- Duck or chicken: baby and tiny baby with BodySize below 0.05. Inspect
  displayed BodySize; do not interpret this as physical kilograms.
- Ectothermic animal, e.g. iguana or tortoise: stage modifiers and stat
  availability, without making up CE-only stats.
- Megachicken from Vanilla Animals Expanded Royal, if installed:
  verify all five stages; an INCOMPLETE profile must not be interpreted as READY. The three older stages have positive CE armor
  despite a zero adult baseline; calculated armor additions are installed
  through ZoologyLifeStageArmorStatPart, while their independent Tool ratios
  remain in the generated C# registry.

Expected startup log: "Installed N species life stages, M individually
mapped natural tools". N depends on installed optional mods and actual
source life stages. If N or M is zero for a known installed species with a
nontrivial young-stage factor, look for baseline, Tool signature or shortHash
warnings in the game's player log.

Verify that source and generated stages coexist correctly across saving and
loading, and that vanilla tools belonging to equipment, hediffs, or other mods
are not altered. All runtime tool matching is initialization-only. A blank
or missing age-stage profile is never silently filled with 1.

To run Checker Python unit tests after pulling:

    py -m unittest -v tests.test_body_size_mass_equivalence tests.test_life_stage_species_profiles tests.test_life_stages tests.test_royal_life_stages

The GitHub Actions jobs for this feature have currently not obtained an
online runner (the job data reported runner_id=0 and no execution steps).
A green build and in-game result cannot be inferred from those jobs.
