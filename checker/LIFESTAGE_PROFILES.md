# Species-specific life stages — development status

This feature is **experimental** and remains on
`feature/animal-life-stage-profiles`. It has not been built against the
RimWorld **1.7 / .NET 10** game assemblies or Combat Extended DLLs.
Do not merge to `main` until those builds and in-game regression tests pass.

## Source of calculations

The Checker `life-stages` category generates
`Zoology/Core/Defs/Misc/LifeStageProfiles_Generated.xml`. It reads the
same Animals, Animals CE, Base stats, Groups, and LifeStages values as the
ordinary animal generator. Species-specific values are calculated during
Checker generation, never in a pawn tick.

When input is Google Sheets or XLSX, Checker reads the calculated
`SERVICE_BodySizeMassBands` table and uses its mass-bin midpoints. The
optional `--sheet SERVICE_BodySizeMassBands=<path.tsv>` supplies that table
for separate TSV inputs. If no service table is supplied to a TSV-only
source, Checker's built-in inverse of the current Animals!AO formula is used:
60 kg normalization, the workbook's continuous response, and dynamic
MROUND steps 0.05/0.1/0.25/0.5. The service table takes precedence when
present; no game-time spreadsheet calculation occurs.

The physical mass of a stage comes from the **new BodySize's equivalent
mass bin**, not adult mass times `bodySizeFactor`. For example, the current
Megachicken base BodySize 0.35 at its maximum stage factor 20 yields
BodySize 7.0 and a central-equivalent mass about 11,630 kg. These values
change when the source sheet changes. BodySize below 0.05 has no identified
inverse and must not be assigned a fabricated physical mass.

## Current numeric coverage

- Hunger: Animals!AQ model, including physiology/ectothermy and quantization;
  source-adult baseline is checked.
- FilthRate: Animals!K model, normalized to adult value.
- CE Dodge, Crit and Parry: group coefficients from Groups!BH:BJ, actual
  physical mass-bin midpoint for the stage, exponent -0.165/+0.165 and
  0.05 rounding, normalized by adult Animals CE!X:Z. All accessible adult
  source rows are checked against their Base stats masses and coefficients.
- Missing optional species definitions and unused stage definitions are
  ignored by the game installer; only original life-stage references present
  on a race can be replaced.
- For stage sizes below 0.05 or a zero adult CE chance, the relevant
  mass-derived ratio cannot be inferred and is not generated.

**Not implemented:** independently recalculated stage ArmorRating_Sharp/
Blunt, CombatPowerFactor, and full Animals!H MarketValue. Existing
general source-stage values remain for these fields. No claim of corrected
price or battle strength should be made.

## Individual natural attacks

`LifeStageAttackProfiles` is an optional supplementary source.
Each row addresses **one actual Tool** by a zero-based `ToolIndex` in
the final `ThingDef.tools` list, with optional `ToolLabel` and
`LinkedBodyPartsGroup` verification. Multiple paired natural attacks
require separate indices.

The six independently supported factor columns are:

- `VanillaDamageFactor` and `CEDamageFactor`;
- `VanillaCooldownFactor` and `CECooldownFactor`;
- `CESharpPenetrationFactor` and `CEBluntPenetrationFactor`.

Each value is a **residual factor relative to the existing generic stage
adjustment**, not an absolute attack output. Example: if the stage already
multiplies vanilla damage by 0.35 and a validated attack model predicts
0.42 of adult damage, the per-tool factor is `0.42 / 0.35 = 1.2`.
The same normalization must be performed independently for cooldown and
both CE AP components. Values must be positive, finite, and matched to
the correct source stage and tool; omitted values mean unity.

The game resolves actual Tool references at Def startup and stores them
in dictionaries keyed by stage and Tool, so later lookups are O(1).
The source `ThingDef.tools` list is not replaced.

**Important:** the production workbook currently has no
`LifeStageAttackProfiles` tab. Checker accepts externally calculated
corrections but does **not** derive age-specific head, leg, horn, bite,
and forelimb force/energy/contact-area/CE penetration from the complete
biomechanical sheet models. Those calculations must be developed and
validated before any attack corrections can be considered complete.
Do not fill this table with guessed age exponents.

## Runtime installer and compatibility

`ZoologyLifeStageProfileInstaller.ApplyOnce()` runs after Def loading.
It clones the original stage, copies mutable stat factor and mod-extension
lists, attaches `ZoologyLifeStageOrigin`, and replaces only its matching
`LifeStageAge.def` reference. Source stages, age boundaries, reproductive
attributes, graphics and sounds remain untouched. New names follow
`ZoologyStage_<ThingDefName>_<originalStageName>`.

The installer checks adult BodySize, hunger and original stage size factor;
it now requires successful short-hash allocation before adding a cloned
Def to `DefDatabase`. This code has **not** been validated against the
actual 1.7 managed assemblies; its reflection signature and save behavior
need direct in-game testing. Third-party code that compares
`LifeStageDef` references rather than developmental stage may need an
origin-compatibility adjustment. The CE AP override must never use the UI
selected pawn as a substitute for the real attacker, or modify an equipped
weapon's AP.

Re-run Checker, deploy the XML through its validated staging mechanism, and
**restart RimWorld** to load changes. An edited Python generator by itself
does **not** update the currently committed generated XML.

## Checks

```bash
cd checker
python -m unittest tests.test_body_size_mass_equivalence
python -m unittest tests.test_life_stage_species_profiles
python -m unittest tests.test_life_stages tests.test_royal_life_stages
```

The mass-inverse unit tests were locally executed against regression
anchors read from the live workbook. The full repository test suite and C#
build have not been executed in this environment.

Before `main` integration, verify all of the following on RimWorld 1.7
with CE both enabled and disabled: generated LifeStageDef hashes/indexes,
old-save load/reload, every Royal stage, matching natural Tool identity,
cooldown/damage/AP not being applied twice, infant feeding/lactation,
third-party stage and tool edits, and startup/per-attack CPU cost.
