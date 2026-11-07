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
        "AdultMass_kg": adult,
        "BiologicalStageMass_kg": mass,
        "MassEvidenceMethod": "ADULT_ANIMALSTATS" if mass == adult else source,
        "MassReference": "Base stats" if mass == adult else "https://example.org/reference",
        "ExpectedBaseBodySize": 1,
        "MassDerivedStageBodySize": factor,
        "MassDerivedBodySizeFactor": factor,
    }


CORE = (
    "AnimalBaby or EusocialInsectLarva or AnimalBabyTiny",
    "AnimalJuvenile or EusocialInsectJuvenile",
    "AnimalAdult or EusocialInsectAdult",
)


def normal(species="AEXP_Lion", newborn=1.5, weaned=19, adult=171):
    return pd.DataFrame([
        entry(species, CORE[0], adult, newborn, factor=.124),
        entry(species, CORE[1], adult, weaned, factor=.403),
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
                name, 1.5 * adult / 171, 19 * adult / 171, adult
            )], ignore_index=True)
        result = audit_mass_profiles(lion, require_complete=True)
        self.assertEqual(result["species"], 4)
        self.assertEqual(result["stage_rows"], 12)
        self.assertEqual(result["complete_mass_series"], 4)

    def test_warg_and_muffalo_reference_analogs(self):
        wolf_birth = .45
        wolf_weaning = 5.25
        wolf_adult = 26.625
        warg = normal("Warg", wolf_birth * 170 / wolf_adult,
                      wolf_weaning * 170 / wolf_adult, 170)
        bison_birth = 45 * .45359237
        bison_weaning = (350 + 425) / 2 * .45359237
        muffalo = normal("Muffalo", bison_birth * 350 / 665.035,
                         bison_weaning * 350 / 665.035, 350)
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

    def test_birth_must_be_less_than_weaning(self):
        rows = normal(newborn=20, weaned=19)
        with self.assertRaisesRegex(ValueError, "strictly increase"):
            audit_mass_profiles(rows, require_complete=True)

    def test_source_and_factor_parity_are_mandatory(self):
        rows = normal()
        rows.at[0, "MassReference"] = ""
        rows.at[1, "MassDerivedBodySizeFactor"] = 2
        with self.assertRaisesRegex(ValueError, "BodySize/factor mismatch"):
            audit_mass_profiles(rows, require_complete=True)

    def test_five_megachicken_stages_are_not_collapsed(self):
        stages = (
            "VAERoy_AnimalBaby", "VAERoy_AnimalJuvenile",
            "VAERoy_AnimalAdult", "VAERoy_AnimalAdultIntermediate",
            "VAERoy_AnimalAdultMax",
        )
        rows = pd.DataFrame([
            entry("VAERoy_Megachicken", stage, 5, mass, .1 * (i+1))
            for i, (stage, mass) in enumerate(zip(stages, (.1, .5, 1, 2, 5)))
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

    def test_armor_and_attack_are_not_made_up_by_mass_contract(self):
        # Numerical combat factors must always be calculated by AnimalStats,
        # never filled by a mass-evidence validator.
        data = normal()
        output = audit_mass_profiles(data, require_complete=True)
        self.assertFalse(any("attack" in key.lower() or "armor" in key.lower()
                             for key in output))


if __name__ == "__main__":
    unittest.main()
