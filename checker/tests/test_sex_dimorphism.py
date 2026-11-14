"""Sexual dimorphism mean-mass contract: no bounds or guessed attack model."""
import unittest

import pandas as pd

from core.sex_dimorphism import audit_mean_sex_dimorphism


def fixtures():
    animals = pd.DataFrame([
        {"XML name": "<li>PolarBear</li>", "baseBodySize": 1.9},
        {"XML name": "<li>Fox</li><li>FoxAlias</li>", "baseBodySize": 0.2},
    ])
    base = pd.DataFrame([
        {"Species": "Ursus", "Body mass (kg)": 316.9},
        {"Species": "Vulpes", "Body mass (kg)": 3.0},
    ])
    common = {
        "MaleMeanHealthFactor": 1, "FemaleMeanHealthFactor": 1,
        "MaleMeanSpeedFactor": 1, "FemaleMeanSpeedFactor": 1,
        "MaleMeanLinearScale": 1, "FemaleMeanLinearScale": 1,
        "MaleMeanHungerRateFactor": 1, "FemaleMeanHungerRateFactor": 1,
        "MaleMeanFilthRateFactor": 1, "FemaleMeanFilthRateFactor": 1,
        "MaleMeanCE_ArmorDurabilityFactor": 1, "FemaleMeanCE_ArmorDurabilityFactor": 1,
    }
    polar = dict(common, **{
        "XML name": "<li>PolarBear</li>", "Latin name": "Ursus",
        "SexualDimorphism": True, "BaselineMass_kg": 316.9,
        "MaleMean_kg": 452.7142857142857, "FemaleMean_kg": 181.0857142857143,
        "MaleMeanBodySize": 2.25, "FemaleMeanBodySize": 1.6,
    })
    fox = dict(common, **{
        "XML name": "<li>FoxAlias</li>", "Latin name": "Vulpes",
        "SexualDimorphism": False, "BaselineMass_kg": 3.0,
        "MaleMean_kg": 3, "FemaleMean_kg": 3,
        "MaleMeanBodySize": 0.2, "FemaleMeanBodySize": 0.2,
    })
    return pd.DataFrame([polar, fox]), animals, base


class SexDimorphismContractTests(unittest.TestCase):
    def test_two_mean_profiles_and_multi_thingdef_animal_entry(self):
        report = audit_mean_sex_dimorphism(*fixtures())
        self.assertEqual(report, {"dimorphic": 1, "monomorphic": 1, "species": 2})

    def test_duplicate_thingdef_rejected(self):
        sex, animals, base = fixtures()
        sex.at[1, "XML name"] = sex.at[0, "XML name"]
        with self.assertRaisesRegex(ValueError, "duplicates ThingDef"):
            audit_mean_sex_dimorphism(sex, animals, base)

    def test_non_conserving_rounded_means_rejected(self):
        sex, animals, base = fixtures()
        sex.at[0, "FemaleMean_kg"] = 181.08
        with self.assertRaisesRegex(ValueError, "must equal BaselineMass"):
            audit_mean_sex_dimorphism(sex, animals, base)

    def test_missing_evaluated_coefficient_rejected(self):
        sex, animals, base = fixtures()
        sex.at[0, "MaleMeanSpeedFactor"] = "#REF!"
        with self.assertRaisesRegex(ValueError, "invalid formula result"):
            audit_mean_sex_dimorphism(sex, animals, base)

    def test_negative_durability_rejected_but_zero_accepted(self):
        sex, animals, base = fixtures()
        sex.at[0, "MaleMeanCE_ArmorDurabilityFactor"] = 0
        audit_mean_sex_dimorphism(sex, animals, base)
        sex.at[0, "MaleMeanCE_ArmorDurabilityFactor"] = -1
        with self.assertRaisesRegex(ValueError, "invalid evaluated number"):
            audit_mean_sex_dimorphism(sex, animals, base)

    def test_monomorphic_stats_must_be_identical(self):
        sex, animals, base = fixtures()
        sex.at[1, "FemaleMeanHealthFactor"] = 0.95
        with self.assertRaisesRegex(ValueError, "monomorphic .* differ"):
            audit_mean_sex_dimorphism(sex, animals, base)

    def test_baseline_matching_base_stats(self):
        sex, animals, base = fixtures()
        base.at[0, "Body mass (kg)"] = 317
        with self.assertRaisesRegex(ValueError, "BaselineMass disagrees"):
            audit_mean_sex_dimorphism(sex, animals, base)

    def test_no_min_max_or_extended_adult_input_used(self):
        sex, animals, base = fixtures()
        sex["MaleMin_kg"] = [-999, "UNKNOWN"]
        sex["MaleMax_kg"] = ["#REF!", "#REF!"]
        sex["ProlongedAdultGrowth"] = [True, True]
        sex["MaleLargeAdultMass_kg"] = ["#DIV/0!", "#DIV/0!"]
        self.assertEqual(audit_mean_sex_dimorphism(sex, animals, base)["species"], 2)

    def test_unknown_species_is_rejected(self):
        sex, animals, base = fixtures()
        sex.at[0, "XML name"] = "<li>Unknown</li>"
        with self.assertRaisesRegex(ValueError, "absent from Animals"):
            audit_mean_sex_dimorphism(sex, animals, base)

    def test_invalid_gender_flag_is_rejected(self):
        sex, animals, base = fixtures()
        sex.at[0, "SexualDimorphism"] = "maybe"
        with self.assertRaisesRegex(ValueError, "TRUE or FALSE"):
            audit_mean_sex_dimorphism(sex, animals, base)


if __name__ == "__main__":
    unittest.main()
