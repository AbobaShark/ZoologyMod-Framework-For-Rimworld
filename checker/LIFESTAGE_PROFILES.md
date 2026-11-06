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
are preserved. Tool identity is checked using label, body-part group and
capacities during installation. Individual Tool objects are then cached by
reference. No repeated lookup by attack name or AnimalStats calculation
takes place during an attack.

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
  all five stages are READY. The three older stages have positive CE armor
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
