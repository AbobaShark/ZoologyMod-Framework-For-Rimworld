import unittest
import pandas as pd
from lxml import etree as E
from core.energetics_profiles import render_energetics_profiles, REQUIRED

def row(**values):
    return dict.fromkeys(REQUIRED, "") | values

class EnergeticsProfilesTests(unittest.TestCase):
    def setUp(self):
        self.egg = row(**{"XML name": "<li>Chicken</li> ", "Adult base feed (N/d)": 0.48,
                         "Reproduction mode": "Egg", "Reproductive coverage": "READY",
                         "Egg interval (d)": 1, "Clutch budget (N)": 0.8,
                         "Gravid extra (N/d)": 1.6, "Egg nutrition / ovum (N)": 0.8})
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

    def test_invalid_budget_rejected(self):
        self.egg["Gravid extra (N/d)"] = 1
        with self.assertRaisesRegex(ValueError, "inconsistent"):
            render_energetics_profiles(pd.DataFrame([self.egg]))

    def test_duplicate_identity_rejected(self):
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            render_energetics_profiles(pd.DataFrame([self.egg, self.egg]))

    def test_unresolved_species_not_exported(self):
        self.gestation["Reproductive coverage"] = "CHECK REPRODUCTION"
        _, report = render_energetics_profiles(pd.DataFrame([self.egg, self.gestation]))
        self.assertEqual(report["unresolved"], ["Pig"])
