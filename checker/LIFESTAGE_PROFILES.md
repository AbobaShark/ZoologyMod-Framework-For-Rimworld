# AnimalStats-owned species-specific life stages

**Status: experimental feature branch.** All changes are in
`feature/animal-life-stage-profiles`; do not merge into `main` until the
biomechanical attack and economic inputs are complete and RimWorld 1.7 / CE
runtime tests pass.

## Numerical authority

All species-specific numbers are calculated in the **AnimalStats spreadsheet**.
Checker **does not** implement its own life-stage hunger, CE chance, armor,
damage, cooldown, penetration, or combat-power model.

The new workbook tabs are:

- **LifeStageSpeciesProfiles** — every species and original LifeStageDef group,
  calculated stage/adult hunger, FilthRate, CE Dodge/Crit/Parry and placeholder
  outputs for ArmorRating, CombatPower and MarketValue. `CalculationStatus`
  is `INCOMPLETE` until all required physical/economic predictions exist.
  Fully calculated individual fields may be used even if other fields remain
  incomplete; uncalculated fields remain neutral at the parent Def.
- **LifeStageAttackProfiles** — one row per explicitly identified
  `ThingDef.tools` entry and source stage, with six independent
  `Stage / Adult` coefficients. Each coefficient is calculated by the sheet
  from an adult and a stage prediction. A row with missing predictions is
  `INCOMPLETE`; negative and nonfinite coefficients are invalid. Damage and AP may be
  zero when the modeled attack produces no damage or penetration; cooldown
  factors must remain strictly positive. `READY` rows require all six
  calculated, physically valid coefficients.

Both tabs are read automatically from the main Google Sheets/XLSX source.
When using separate TSV inputs, provide
`--sheet LifeStageSpeciesProfiles=...tsv` and
`--sheet LifeStageAttackProfiles=...tsv`. The existing GUI also exposes
these paths.

### Calculated head attacks

The workbook now recalculates **head / body-ramming attacks** directly from
AnimalStats' BodyRamming force regression, body-mass-dependent work and
kinetic-energy limits, tissue damage envelope, vanilla and CE damage maps,
separate attack cycles and CE blunt penetration.

The five independently checked adult source cases (polar bear,
Harpactotyrannus, ostrich, raccoon, and Royal Megachicken) all reproduced
their original adult head attack characteristics exactly. The persistent
`SERVICE_LifeStageHeadParity` table recalculates head attacks at the actual
adult physical mass for **all 414 species** and checks all six components
against Animals / Animals CE: as of this update **414 MATCH, 0 MISMATCH**.
`LifeStageAttackProfiles!AdultModelParity` reads that status. A mismatched
adult model forces `CalculationStatus = INVALID`, and Checker rejects the
generation rather than keeping outdated coefficients.

Currently **1145 attack rows are READY**, **99 are INCOMPLETE** because
stage BodySize is below the mass mapping's lower bound of 0.05. A further
417 reserved rows are blank (including the Core adult stage). The 1145
READY rows compile to **1930 concrete stage/Tool keys** after expanding
"AnimalBaby or EusocialInsectLarva" and the equivalent juvenile stage
alias. Their C# static initializer is split into **31 bounded methods**;
this avoids one oversized initialization method when thousands of tools
are generated.

Only head attacks have complete age-specific models. Other attack
mechanisms require their *own* force/energy/contact geometry/cycle/AP
re-evaluation from the existing biomechanical models in AnimalStats.
Missing limb, claw, horn/tusk and bite outputs **must not** be filled using
generic scaling exponents. Those per-tool models remain incomplete.

## Physical mass and common life stages

`LifeStages` contains universal body-size and health-scale factors plus
general speed where appropriate, including the existing separately calculated
Royal Megachicken speed factors. General damage, cooldown, AP, hunger, filth,
armor, CE Dodge/Crit/Parry, CombatPower and MarketValue factors are neutral
(1): species-specific outputs belong in the new tabs.

The stage size is `adult BaseBodySize * stage bodySizeFactor`. That is a
**game BodySize, not a mass ratio**. Stage physical mass comes from the
`SERVICE_BodySizeMassBands` interpolation. As of the current workbook,
VAERoy_Megachicken's maximum stage is BodySize 7.0, about 11,630 kg
equivalent central mass. The model does not assign an invented physical mass
to stages below 0.05. Adult stage 1.0 uses the actual adult physical mass from
Base stats rather than the representative midpoint of the BodySize interval.

Only original stages genuinely referenced by `race.lifeStageAges` are
specialized. A generated stage is a clone of the original loaded Def and
inherits the source's other attributes (including minAge, reproduction,
graphics, sounds, and unrelated mod extensions). The copied LifeStageAge
still retains its source age bounds.

## Generated outputs and compile workflow

Checker stages and atomically deploys both:

- `Zoology/Core/Defs/Misc/LifeStageProfiles_Generated.xml`, containing
  species-stage calculated scalar/stat factors.
- `Zoology/Source/Behaviour/Shared/ZoologyLifeStageAttackProfiles_Generated.cs`,
  containing a compiled dictionary of individual READY Tool coefficients.

The C# source is a **strictly whitelisted** generated destination in
`checker/generation.json` and the same checksum-backed staging/deploy
mechanism as the XML. Mod compilation is deliberately **not** performed by
Checker. The existing user build process compiles the generated .cs into
ZoologyMod.

At Def initialization, the generic Zoology installer resolves actual
`ThingDef.tools` indices and checks the Tool's label and linked body-part
group. It then caches stage/Tool-reference pairs. Normal attacks only do
O(1) lookups; neither the spreadsheet nor biomechanical formulas are used
at game time.

The six per-tool coefficients are **absolute stage/adult ratios**:

1. Vanilla damage
2. Vanilla cooldown
3. CE damage
4. CE cooldown
5. CE sharp AP
6. CE blunt AP

The old generic `meleeDamageFactor` and cooldown/AP age multipliers are
neutral, preventing double application. The existing generic C# applies
per-tool damage and cooldown; the CE AP patch uses the specific AP factor
**instead of** the source-stage AP factor. Unrelated weapons and non-target
tools must retain their normal CE/vanilla handling.

## Remaining work

The production workbook now calculates stage head attacks in full, but
**does not yet calculate stage-specific leg, forelimb/claw, horn/tusk, or bite
attacks**, stage integument armor, CombatPower or the full Animals!H
MarketValue model. Their absent predictions are deliberately left
unmodified. A life stage that lacks a physical mass below BodySize 0.05
retains only the independently computable scalars. The new head model is
scientifically checked against source adult values, but it has **not**
been tested in RimWorld's actual CE combat execution path.

The current checked-in XML and generated C# were synchronized from evaluated
workbook cells. The common life-stage patch XMLs were neutralized to match
LifeStages. The Python Checker pipeline and its atomic staging/deploy route
still require successful automated execution before merging.

The C# and RimWorld 1.7 integration have **not** been compiled against real
game DLLs here. Verify dynamic Def registration and short hashes, old saves,
Royal stages, CE-on and CE-off attack paths, feeding/lactation, compatibility
with third-party Tool patches and actual per-attack CPU costs.

## Tests

```bash
cd checker
python -m unittest -v \
  tests.test_body_size_mass_equivalence \
  tests.test_life_stage_species_profiles \
  tests.test_life_stages tests.test_royal_life_stages \
  tests.test_xml_routing tests.test_complete_routing
```

The GitHub Actions workflow is committed, but recent jobs failed without
running any steps (no successful Python or C# build is claimed).
