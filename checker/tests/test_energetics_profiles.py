import unittest
import pandas as pd
from lxml import etree as E
from core.energetics_profiles import (
    render_energetics_profiles, growth_stage_factors, body_nutrition, round_base_hunger_rate,
    extra_clutch_budget, REQUIRED,
)

def row(**values):
    return dict.fromkeys(REQUIRED, "") | values

class EnergeticsProfilesTests(unittest.TestCase):
    def setUp(self):
        self.egg = row(**{"XML name": "<li>Chicken</li> ", "Adult base feed (N/d)": 0.48,
                         "Reproduction mode": "Egg", "Reproductive coverage": "READY",
                         "Egg interval (d)": 1, "Clutch budget (N)": 0.56,
                         "Gravid extra (N/d)": 1.12, "Egg nutrition / ovum (N)": 0.8,
                         "Mean eggs / clutch": 1, "Female adult HungerRateFactor": 1})
        self.gestation = row(**{"XML name": "<li>Pig</li>", "Adult base feed (N/d)": 1.36,
                         "Reproduction mode": "Gestation", "Reproductive coverage": "READY",
                         "Gestation (d)": 6, "Gestation budget (N)": 18,
                         "Pregnancy stage 1 (N/d)": 1, "Pregnancy stage 2 (N/d)": 3,
                         "Pregnancy stage 3 (N/d)": 5})

    def test_round_trip(self):
        xml, report = render_energetics_profiles(pd.DataFrame([self.egg, self.gestation]))
        root = E.fromstring(xml)
        self.assertEqual(report["ready"], 2)
        self.assertEqual(root.xpath("//profiles/li/animalDefName/text()"), ["Chicken", "Pig"])
        self.assertEqual(root.xpath("//eggNutrition/text()"), ["0.8"])
        self.assertEqual(root.xpath("//gestationBudget/text()"), ["18"])

    def test_fractional_egg_interval(self):
        self.egg["Egg interval (d)"] = 1.8438430007299678
        self.egg["Clutch budget (N)"] = extra_clutch_budget(1, 0.8, self.egg["Egg interval (d)"], 0.48)
        self.egg["Gravid extra (N/d)"] = 2 * self.egg["Clutch budget (N)"] / self.egg["Egg interval (d)"]
        xml, _ = render_energetics_profiles(pd.DataFrame([self.egg]))
        root = E.fromstring(xml)
        interval = float(root.findtext(".//eggIntervalDays"))
        extra = float(root.findtext(".//gravidExtra"))
        self.assertAlmostEqual(interval * 0.5 * extra, self.egg["Clutch budget (N)"])

    def test_zero_surcharge_preserves_egg_nutrition(self):
        self.egg.update({"Egg interval (d)": 4, "Clutch budget (N)": 0,
                         "Gravid extra (N/d)": 0})
        xml, _ = render_energetics_profiles(pd.DataFrame([self.egg]))
        root = E.fromstring(xml)
        self.assertEqual(root.findtext(".//eggNutrition"), "0.8")
        self.assertEqual(root.findtext(".//clutchBudget"), "0")
        self.assertEqual(root.findtext(".//gravidExtra"), "0")

    def test_budget_accounts_for_female_factor(self):
        self.egg.update({"Female adult HungerRateFactor": 1.5,
                         "Clutch budget (N)": 0.44, "Gravid extra (N/d)": 0.88})
        render_energetics_profiles(pd.DataFrame([self.egg]))

    def test_gross_budget_even_if_internally_consistent_is_rejected(self):
        self.egg.update({"Clutch budget (N)": 0.8, "Gravid extra (N/d)": 1.6})
        with self.assertRaisesRegex(ValueError, "inconsistent"):
            render_energetics_profiles(pd.DataFrame([self.egg]))

    def test_aquatic_zero_surcharge(self):
        self.egg.update({"Reproduction mode": "Aquatic cohort", "Mean eggs / clutch": 100,
                         "Egg nutrition / ovum (N)": 0.001, "Clutch budget (N)": 0,
                         "Gravid extra (N/d)": 0})
        xml, _ = render_energetics_profiles(pd.DataFrame([self.egg]))
        root = E.fromstring(xml)
        self.assertEqual(root.findtext(".//aquatic"), "true")
        self.assertIsNone(root.find(".//eggNutrition"))
        self.assertEqual(root.findtext(".//gravidExtra"), "0")

    def test_pregnancy_stage_weights(self):
        self.gestation["Pregnancy stage 1 (N/d)"] = 2
        self.gestation["Pregnancy stage 2 (N/d)"] = 2
        with self.assertRaisesRegex(ValueError, "inconsistent"):
            render_energetics_profiles(pd.DataFrame([self.gestation]))

    def test_invalid_budget_rejected(self):
        self.egg["Gravid extra (N/d)"] = 1
        with self.assertRaisesRegex(ValueError, "inconsistent"):
            render_energetics_profiles(pd.DataFrame([self.egg]))

    def test_growth_floor_and_shared_allometry(self):
        growth = row(**{"XML name": "<li>TestAnimal</li>", "Adult base feed (N/d)": 0.8,
                        "Reproductive coverage": "SPECIAL", "Growth coverage": "READY",
                        "Allometric exponent": 0.734, "Growth efficiency": 0.7,
                        "Juvenile age (d)": 2, "Adult age (d)": 4,
                        "Baby BodySize": 0.01, "Juvenile BodySize": 0.1,
                        "Baseline adult BodySize": 1.0, "Male adult BodySize": 1.0,
                        "Female adult BodySize": 1.0, "Growth budget check (N)": 0})
        baby, juvenile, male, female = growth_stage_factors(growth)
        self.assertGreaterEqual(baby * 0.8 * 2,
                                (body_nutrition(0.1) - body_nutrition(0.01)) / 0.7)
        self.assertGreaterEqual(juvenile * 0.8 * 2,
                                (body_nutrition(1.0) - body_nutrition(0.1)) / 0.7)
        self.assertEqual((male, female), (1.0, 1.0))
        # An evaluated stage inconsistent with the daily model is rejected.
        growth.update({"Baby HungerRateFactor": baby, "Juvenile HungerRateFactor": juvenile,
                       "Male adult HungerRateFactor": male, "Female adult HungerRateFactor": female})
        _, report = render_energetics_profiles(pd.DataFrame([self.egg, growth]))
        self.assertEqual(report["growth_checked"], 1)
        growth["Baby HungerRateFactor"] *= 0.9
        with self.assertRaisesRegex(ValueError, "inconsistent"):
            render_energetics_profiles(pd.DataFrame([self.egg, growth]))

    def test_arthropod_metabolism(self):
        growth = row(**{"XML name": "<li>AA_CrystalMit</li>", "Adult base feed (N/d)": 0.144,
                        "Reproductive coverage": "SPECIAL", "Growth coverage": "READY",
                        "Allometric exponent": 0.856, "Growth efficiency": 1.0,
                        "Juvenile age (d)": 24, "Adult age (d)": 48,
                        "Baby BodySize": 0.01049, "Juvenile BodySize": 0.4015,
                        "Baseline adult BodySize": 0.55, "Male adult BodySize": 0.55,
                        "Female adult BodySize": 0.55, "Growth budget check (N)": 0})
        baby, juvenile, male, female = growth_stage_factors(growth)
        growth.update({"Baby HungerRateFactor": baby, "Juvenile HungerRateFactor": juvenile,
                       "Male adult HungerRateFactor": male, "Female adult HungerRateFactor": female})
        xml, report = render_energetics_profiles(pd.DataFrame([self.egg, growth]))
        self.assertEqual(report["growth_checked"], 1)
        self.assertEqual(E.fromstring(xml).xpath("//growthExponent/text()"), ["0.856"])
        growth["Adult base feed (N/d)"] = 0.08
        with self.assertRaisesRegex(ValueError, "adult hunger"):
            growth_stage_factors(growth)

    def test_cephalopod_metabolism(self):
        growth = row(**{"XML name": "<li>AA_TarGuzzler</li>", "Adult base feed (N/d)": 0.368,
                        "Reproductive coverage": "SPECIAL", "Growth coverage": "READY",
                        "Allometric exponent": 0.916, "Growth efficiency": 1.0,
                        "Juvenile age (d)": 60, "Adult age (d)": 120,
                        "Baby BodySize": 0.03478299884610004, "Juvenile BodySize": 1.2201517525003098,
                        "Baseline adult BodySize": 1.6, "Male adult BodySize": 1.6,
                        "Female adult BodySize": 1.6, "Growth budget check (N)": 0})
        baby, juvenile, male, female = growth_stage_factors(growth)
        growth.update({"Baby HungerRateFactor": baby, "Juvenile HungerRateFactor": juvenile,
                       "Male adult HungerRateFactor": male, "Female adult HungerRateFactor": female})
        xml, report = render_energetics_profiles(pd.DataFrame([self.egg, growth]))
        self.assertEqual(report["growth_checked"], 1)
        self.assertEqual(E.fromstring(xml).xpath("//growthExponent/text()"), ["0.916"])
        growth["Adult base feed (N/d)"] = 0.288
        with self.assertRaisesRegex(ValueError, "adult hunger"):
            growth_stage_factors(growth)

    def test_gastropod_metabolism(self):
        growth = row(**{"XML name": "<li>LavaSnail</li>", "Adult base feed (N/d)": 0.096,
                        "Reproductive coverage": "SPECIAL", "Growth coverage": "READY",
                        "Allometric exponent": 0.85, "Growth efficiency": 1.0,
                        "Juvenile age (d)": 36, "Adult age (d)": 72,
                        "Baby BodySize": 0.03204240978363778,
                        "Juvenile BodySize": 0.7064964328293136,
                        "Baseline adult BodySize": 0.95, "Male adult BodySize": 0.95,
                        "Female adult BodySize": 0.95, "Growth budget check (N)": 0})
        baby, juvenile, male, female = growth_stage_factors(growth)
        growth.update({"Baby HungerRateFactor": baby, "Juvenile HungerRateFactor": juvenile,
                       "Male adult HungerRateFactor": male, "Female adult HungerRateFactor": female})
        xml, report = render_energetics_profiles(pd.DataFrame([self.egg, growth]))
        self.assertEqual(report["growth_checked"], 1)
        self.assertEqual(E.fromstring(xml).xpath("//growthExponent/text()"), ["0.85"])
        growth["Adult base feed (N/d)"] = 0.192
        with self.assertRaisesRegex(ValueError, "adult hunger"):
            growth_stage_factors(growth)

    def test_linear_body_nutrition(self):
        for size in (0.01, 0.05, 0.1, 0.2, 1, 5):
            self.assertAlmostEqual(body_nutrition(size), 7 * size)

    def test_hunger_rounding(self):
        for value, expected in ((0.001, 0.01), (0.022, 0.02),
                                (0.123, 0.12), (0.487, 0.49),
                                (0.51, 0.5), (0.53, 0.55)):
            with self.subTest(value=value):
                self.assertAlmostEqual(round_base_hunger_rate(value), expected)

    def test_duplicate_identity_rejected(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            render_energetics_profiles(pd.DataFrame([self.egg, self.egg]))

    def test_unresolved_species_not_exported(self):
        self.gestation["Reproductive coverage"] = "CHECK REPRODUCTION"
        _, report = render_energetics_profiles(pd.DataFrame([self.egg, self.gestation]))
        self.assertEqual(report["unresolved"], ["Pig"])
