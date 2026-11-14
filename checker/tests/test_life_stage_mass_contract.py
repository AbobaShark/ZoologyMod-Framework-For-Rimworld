"""Structural regression tests for externally sourced life-stage masses.

Synthetic megachicken/spectrobiont masses below exercise lifecycle structure,
NOT biological estimates; no synthetic value is written to AnimalStats.
"""
import unittest

import pandas as pd

from core.life_stage_mass_contract import audit_mass_profiles


def entry(species, stage, adult, mass, factor=1, *, source="DIRECT"):
    return {
        "XML name": f"<li>{species}</li>",
        "LifeStageDef": stage,
        "BiologicalStageMass_kg": mass,
        "MassReference": "Base stats" if mass == adult else "https://example.org/reference",
        "StageBodySize": factor,
        "bodySizeFactor": factor,
    }


CORE = (
    "AnimalBaby or EusocialInsectLarva",
    "AnimalJuvenile or EusocialInsectJuvenile",
    "AnimalAdult or EusocialInsectAdult",
)


def normal(species="AEXP_Lion", newborn=1.5, juvenile=None, adult=171):
    if juvenile is None:
        juvenile = (adult + newborn) / 2
    return pd.DataFrame([
        entry(species, CORE[0], adult, newborn, factor=.124),
        entry(species, CORE[1], adult, juvenile, factor=.403),
        entry(species, CORE[2], adult, adult, factor=1),
    ])


class LifeStageMassContractTests(unittest.TestCase):
    def test_lion_and_fictional_lion_variants_use_one_baby_series(self):
        lion = normal()
        variants = [(name, adult) for name, adult in (
            ("VAEWaste_Toxlion", 160),
            ("AA_ArcticLion", 300),
            ("AA_SandLion", 300),
        )]
        for name, adult in variants:
            lion = pd.concat([lion, normal(
                name, 1.5 * adult / 171, adult=adult
            )], ignore_index=True)
        result = audit_mass_profiles(lion, require_complete=True)
        self.assertEqual(result["species"], 4)
        self.assertEqual(result["stage_rows"], 12)
        self.assertEqual(result["complete_mass_series"], 4)

    def test_warg_and_muffalo_reference_analogs(self):
        wolf_birth = .45
        wolf_adult = 26.625
        warg = normal("Warg", wolf_birth * 170 / wolf_adult, adult=170)
        bison_birth = 45 * .45359237
        muffalo = normal("Muffalo", bison_birth * 350 / 665.035, adult=350)
        out = audit_mass_profiles(pd.concat([warg, muffalo]), require_complete=True)
        self.assertEqual(out["complete_mass_series"], 2)

    def test_separate_baby_tiny_is_rejected(self):
        rows = normal()
        rows.loc[3] = entry("AEXP_Lion", "AnimalBabyTiny", 171, 1)
        with self.assertRaisesRegex(ValueError, "AnimalBabyTiny"):
            audit_mass_profiles(rows, require_complete=True)

    def test_missing_age_mass_fails_closed(self):
        rows = normal()
        rows.at[0, "BiologicalStageMass_kg"] = None
        with self.assertRaisesRegex(ValueError, "missing"):
            audit_mass_profiles(rows, require_complete=True)

    def test_juvenile_must_be_between_newborn_and_adult(self):
        rows = normal(newborn=20, juvenile=19)
        with self.assertRaisesRegex(ValueError, "strictly increase"):
            audit_mass_profiles(rows, require_complete=True)

    def test_juvenile_can_use_empirical_weaning_mass(self):
        rows = normal()
        rows.at[1, "BiologicalStageMass_kg"] += 1
        result = audit_mass_profiles(rows, require_complete=True)
        self.assertEqual(result["invalid_count"], 0)
        self.assertEqual(result["complete_mass_series"], 1)

    def test_source_and_factor_parity_are_mandatory(self):
        rows = normal()
        rows.at[0, "MassReference"] = ""
        rows.at[1, "bodySizeFactor"] = 2
        with self.assertRaisesRegex(ValueError, "BodySize/factor mismatch"):
            audit_mass_profiles(rows, require_complete=True)

    def test_five_megachicken_stages_are_not_collapsed(self):
        stages = (
            "VAERoy_AnimalBaby", "VAERoy_AnimalJuvenile",
            "VAERoy_AnimalAdult", "VAERoy_AnimalAdultIntermediate",
            "VAERoy_AnimalAdultMax",
        )
        rows = pd.DataFrame([
            entry("VAERoy_Megachicken", stage, 5, mass, factor)
            for stage, mass, factor in zip(stages, (.1, .5, 5, 10, 20),
                                            (.1, .2, 1, 2, 4))
        ])
        self.assertEqual(audit_mass_profiles(rows, require_complete=True)["stage_rows"], 5)
        with self.assertRaisesRegex(ValueError, "lifecycle stages"):
            audit_mass_profiles(rows.iloc[:-1], require_complete=True)

    def test_two_spectrobionta_stage_families(self):
        a = ["SpectrobiontaPredimagoSmall", "SpectrobiontaPredimago",
             "SpectrobiontaImago"]
        b = ["SpectrobiontaLarvaSmall", "SpectrobiontaLarvaMedium",
             "SpectrobiontaLarvaLarge"]
        rows = pd.DataFrame(
            [entry("SpectroPredimagoFixture", stage, 10, mass, mass / 10)
             for stage, mass in zip(a, (1, 4, 10))]
            + [entry("SpectroLarvaFixture", stage, 20, mass, mass / 20)
               for stage, mass in zip(b, (2, 8, 20))]
        )
        result = audit_mass_profiles(rows, require_complete=True)
        self.assertEqual((result["species"], result["stage_rows"]), (2, 6))
        rows.at[0, "LifeStageDef"] = "SpectrobiontaLarvaSmall"
        with self.assertRaisesRegex(ValueError, "mixed incompatible"):
            audit_mass_profiles(rows, require_complete=True)

    def test_four_stage_harpactotyrannus_preserves_older_predimago_cycles(self):
        forms = (
            "SpectrobiontaPredimagoSmall",
            "SpectrobiontaPredimagoMedium",
            "SpectrobiontaPredimago",
            "SpectrobiontaImago",
        )
        masses = (50, 400, 1500, 8540)
        harpactotyrannus = pd.DataFrame([
            entry("Harpactotyrannus", stage, 8540, mass, mass / 8540)
            for stage, mass in zip(forms, masses)
        ])
        report = audit_mass_profiles(harpactotyrannus, require_complete=True)
        self.assertEqual(report["complete_mass_series"], 1)

        # Omitting only the optional medium stage remains valid for other races.
        legacy = harpactotyrannus.drop(index=1)
        self.assertEqual(
            audit_mass_profiles(legacy, require_complete=True)["complete_mass_series"], 1
        )
        # Omitting a required large predimago is never valid.
        incomplete = harpactotyrannus.drop(index=2)
        with self.assertRaisesRegex(ValueError, "lifecycle stages"):
            audit_mass_profiles(incomplete, require_complete=True)

    def test_thrumbo_and_alpha_thrumbo_empirical_ungulate_reconstruction(self):
        # Neonatal estimates use published large-herbivore mass regressions.
        # The stage mass is an evaluated biological input; this test uses a midpoint.
        cases = (
            ("Thrumbo", 6000, 123.45469675300308, 1202.8051002055142,
             0.24129496465835248, 0.5626975108959649),
            ("AlphaThrumbo", 7000, 139.40281871166638, 1384.1964087051365,
             0.2400259397770624, 0.5614078619989306),
        )
        records = []
        for species, adult, baby, young, baby_factor, young_factor in cases:
            for stage, mass, factor in zip(
                CORE, (baby, (baby + adult) / 2, adult), (baby_factor, young_factor, 1)
            ):
                records.append(entry(
                    species, stage, adult, mass, factor,
                    source="GROUP_ALLOMETRY",
                ))
        result = audit_mass_profiles(pd.DataFrame(records), require_complete=True)
        self.assertEqual(result["complete_mass_series"], 2)

    def test_lockjaw_crocodilian_birth_and_juvenile_ontogeny(self):
        # Birth: 11 Crocodilia taxa (Myhrvold 2015).
        # This canonical input happens to use the arithmetic midpoint.
        series = pd.DataFrame([
            entry("AA_Lockjaw", CORE[0], 1000, .1544824169172378,
                  .020138429632334313, source="GROUP_ALLOMETRY"),
            entry("AA_Lockjaw", CORE[1], 1000, (1000 + .1544824169172378) / 2,
                  .22902704159936202, source="PHYLOGENETIC_ANALOG"),
            entry("AA_Lockjaw", CORE[2], 1000, 1000),
        ])
        summary = audit_mass_profiles(series, require_complete=True)
        self.assertEqual(summary["complete_mass_series"], 1)
        self.assertEqual(summary["invalid_count"], 0)

    def test_tasmanian_devil_neonate_below_mass_band_minimum(self):
        # Direct Amniote Life-History Database neonatal mass: 21.75 mg.
        values = (
            (.00002175, .0023971847569779384),
            ((7.15 + .00002175) / 2, .36374806280570937),
            (7.15, 1),
        )
        series = pd.DataFrame([
            entry("AEXP_TasmanianDevil", stage, 7.15, mass, factor)
            for stage, (mass, factor) in zip(CORE, values)
        ])
        report = audit_mass_profiles(series, require_complete=True)
        self.assertEqual(report["complete_mass_series"], 1)

    def test_armor_and_attack_are_not_made_up_by_mass_contract(self):
        # Numerical combat factors must always be calculated by AnimalStats,
        # never filled by a mass-evidence validator.
        data = normal()
        output = audit_mass_profiles(data, require_complete=True)
        self.assertFalse(any("attack" in key.lower() or "armor" in key.lower()
                             for key in output))


if __name__ == "__main__":
    unittest.main()
