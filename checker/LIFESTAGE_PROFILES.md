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
