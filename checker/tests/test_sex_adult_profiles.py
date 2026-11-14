"""Numerical contracts for evaluated mean-sex adult endpoints."""
import unittest

import pandas as pd

from core.sex_adult_profiles import audit_sex_adult_profiles
from tests.test_sex_dimorphism import fixtures


def inputs():
    sex, animals, _ = fixtures()
    profiles = []
    for gender, mass, body in (
        ("Male", 452.7142857142857, 2.25),
        ("Female", 181.0857142857143, 1.6),
    ):
        profiles.append({
            "XML name": "<li>PolarBear</li>",
            "Sex": gender,
            "AdultMass_kg": mass,
            "BaselineMass_kg": 316.9,
            "AdultBodySize": body,
            "BodySizeFactor": body / 1.9,
            "HealthScaleFactor": 1,
            "SpeedFactor": 1,
            "CE_SpeedFactor": 1,
            "HungerRateFactor": 1,
            "FilthRateFactor": 1,
            "CE_ArmorDurabilityFactor": 1,
            "LinearScale": 1,
            "CombatPowerFactor": 1.2,
            "MarketValueFactor": 1.1,
            "CE_ArmorSharp": 0.16,
            "CE_ArmorBlunt": 0.3,
            "ToolRecords9": "0\thead\tHeadAttackTool\t1.1\t1.05\t1.2\t1.04\t0.3\t0.4",
            "ToolCount": 1,
            "CalculationStatus": "READY",
            "GroupName": "Plantigrade carnivoran",
        })
    return pd.DataFrame(profiles), sex, animals


class SexAdultProfilesTests(unittest.TestCase):
    def test_complete_pair_and_one_natural_tool_per_sex(self):
        report = audit_sex_adult_profiles(*inputs())
        self.assertEqual(report, {
            "dimorphic_species": 1, "adult_endpoints": 2, "natural_tools": 2})

    def test_missing_female_endpoint_rejected(self):
        profiles, sex, animals = inputs()
        with self.assertRaisesRegex(ValueError, "Missing mean-sex adult endpoints"):
            audit_sex_adult_profiles(profiles.iloc[:1], sex, animals)

    def test_monomorphic_profile_rejected(self):
        profiles, sex, animals = inputs()
        profiles.at[0, "XML name"] = "<li>FoxAlias</li>"
        with self.assertRaisesRegex(ValueError, "no matching dimorphic species"):
            audit_sex_adult_profiles(profiles, sex, animals)

    def test_no_individual_size_bounds_are_consumed(self):
        profiles, sex, animals = inputs()
        sex["MaleMin_kg"] = "#REF!"
        sex["FemaleMax_kg"] = "#DIV/0!"
        profiles["IndividualScale"] = None
        self.assertEqual(audit_sex_adult_profiles(profiles, sex, animals)["adult_endpoints"], 2)

    def test_sex_market_value_must_remain_numeric(self):
        profiles, sex, animals = inputs()
        with self.assertRaisesRegex(ValueError, "missing"):
            audit_sex_adult_profiles(
                profiles.drop(columns=["MarketValueFactor"]), sex, animals)

    def test_missing_combat_ratio_rejected(self):
        profiles, sex, animals = inputs()
        profiles.at[0, "CombatPowerFactor"] = ""
        with self.assertRaisesRegex(ValueError, "missing evaluated numeric value"):
            audit_sex_adult_profiles(profiles, sex, animals)

    def test_ce_armor_zero_allowed(self):
        profiles, sex, animals = inputs()
        profiles.at[0, "CE_ArmorSharp"] = 0
        audit_sex_adult_profiles(profiles, sex, animals)

    def test_geometry_only_changes_tool_physics_if_source_says_so(self):
        profiles, sex, animals = inputs()
        profiles.at[0, "ToolRecords9"] = (
            "0\thead\tHeadAttackTool\t1.1\t1.05\t1.2\t1.04\t0.3\t0.4"
            "\n0\tclaw\tFrontPaw\t1.4\t1\t1.5\t1\t0.6\t0.3"
        )
        profiles.at[0, "ToolCount"] = 2
        with self.assertRaisesRegex(ValueError, "duplicate or negative Tool ordinal"):
            audit_sex_adult_profiles(profiles, sex, animals)

    def test_ce_cooldown_must_be_positive(self):
        profiles, sex, animals = inputs()
        profiles.at[1, "ToolRecords9"] = (
            "0\thead\tHeadAttackTool\t1\t1\t1\t0\t0.5\t0.5")
        with self.assertRaisesRegex(ValueError, "invalid Tool component"):
            audit_sex_adult_profiles(profiles, sex, animals)

    def test_body_size_must_reference_actual_race_baseline(self):
        profiles, sex, animals = inputs()
        profiles.at[0, "BodySizeFactor"] = 2
        with self.assertRaisesRegex(ValueError, "BodySize factor"):
            audit_sex_adult_profiles(profiles, sex, animals)

    def test_wrong_source_mass_detected(self):
        profiles, sex, animals = inputs()
        profiles.at[0, "AdultMass_kg"] = 470
        with self.assertRaisesRegex(ValueError, "mass differs"):
            audit_sex_adult_profiles(profiles, sex, animals)


if __name__ == "__main__":
    unittest.main()
