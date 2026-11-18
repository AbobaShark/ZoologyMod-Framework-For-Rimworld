import unittest

import pandas as pd
from lxml import etree as E

from core.animal_physiology import render_animal_physiology


class AnimalPhysiologyTests(unittest.TestCase):
    def test_group_inherited_coefficients(self):
        rows = [
            {"XML name": "<li>Alligator</li>", "bleedRateFactor": "0,5",
             "WoundInfectionChanceFactor": "0,2"},
            {"XML name": "<li>MonitorLizard</li>", "bleedRateFactor": 0.5,
             "WoundInfectionChanceFactor": 1},
            {"XML name": "<li>Cow</li>", "bleedRateFactor": 1,
             "WoundInfectionChanceFactor": 1},
        ]
        root = E.fromstring(render_animal_physiology(pd.DataFrame(rows)))
        values = root.xpath("//bleedRateFactor/text()")
        self.assertTrue(values)
        self.assertEqual(set(values), {"0.5"})
        self.assertEqual(root.xpath("//li[@Class='ZoologyMod.ModExtension_WoundInfectionChanceFactor, ZoologyMod']/factor/text()"), ["0.2"])
        all_xpaths = root.xpath("//xpath/text()")
        self.assertTrue(any("Alligator" in xpath and "MonitorLizard" in xpath for xpath in all_xpaths))
        self.assertTrue(all("Cow" not in xpath for xpath in all_xpaths))

    def test_zero_is_full_race_immunity(self):
        rows = [{"XML name": "<li>ImmuneAnimal</li>", "bleedRateFactor": 1,
                 "WoundInfectionChanceFactor": 0}]
        root = E.fromstring(render_animal_physiology(pd.DataFrame(rows)))
        self.assertEqual(set(root.xpath("//isImmuneToInfections/text()")), {"true"})
        self.assertFalse(root.xpath("//li[@Class='ZoologyMod.ModExtension_WoundInfectionChanceFactor, ZoologyMod']"))

    def test_invalid_values_rejected(self):
        rows = [{"XML name": "<li>Animal</li>", "bleedRateFactor": 0.5,
                 "WoundInfectionChanceFactor": 1.2}]
        with self.assertRaisesRegex(ValueError, "WoundInfectionChanceFactor"):
            render_animal_physiology(pd.DataFrame(rows))
        rows[0]["WoundInfectionChanceFactor"] = 0.5
        rows[0]["bleedRateFactor"] = 0
        with self.assertRaisesRegex(ValueError, "bleedRateFactor"):
            render_animal_physiology(pd.DataFrame(rows))

    def test_missing_columns_rejected(self):
        with self.assertRaisesRegex(ValueError, "missing physiology columns"):
            render_animal_physiology(pd.DataFrame({"XML name": ["<li>Animal</li>"]}))


if __name__ == "__main__":
    unittest.main()
