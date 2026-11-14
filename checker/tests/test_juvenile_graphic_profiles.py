"""Canonical juvenile sprite endpoints generated from LifeStageProfiles only."""
import math
import unittest

import pandas as pd
from lxml import etree as E

from core.life_stage_species_profiles import render_juvenile_graphic_profiles

CORE = (
    "AnimalBaby or EusocialInsectLarva",
    "AnimalJuvenile or EusocialInsectJuvenile",
    "AnimalAdult or EusocialInsectAdult",
)


def rows(name="AEXP_Kangaroo", masses=(0.0008558177902244994,
                                       8.15262215762, 46)):
    return pd.DataFrame([
        {"XML name": f"<li>{name}</li>", "LifeStageDef": stage,
         "BiologicalStageMass_kg": mass, "CalculationStatus": "READY",
         "StageBodySize": 100 + i, "bodySizeFactor": 300 + i,
         "original_juvenile_drawSize": 9000 - i}
        for i, (stage, mass) in enumerate(zip(CORE, masses))
    ])


def profiles(table):
    xml, info = render_juvenile_graphic_profiles(table)
    return E.fromstring(xml).xpath(
        "./ZoologyJuvenileGraphicCatalogDef/profiles/li"), info


class JuvenileGraphicProfileTests(unittest.TestCase):
    def test_kangaroo_exact_canonical_masses(self):
        nodes, info = profiles(rows())
        self.assertEqual(info["young_stages"], 2)
        self.assertEqual(len(nodes), 2)
        expected = (0.36625741946668344, 1.6863165357372578)
        for node, draw in zip(nodes, expected):
            self.assertAlmostEqual(float(node.findtext("visualScale")) * 2.25,
                                   draw, places=6)
            self.assertEqual(float(node.findtext("adultMass")), 46)
            self.assertEqual(node.findtext("adultStageDefName"), CORE[2])

    def test_juvenile_author_drawsize_and_body_size_are_not_inputs(self):
        base = rows()
        first, _ = profiles(base)
        changed = base.copy()
        changed["original_juvenile_drawSize"] = [-200, 2000, 1]
        changed["StageBodySize"] = [100000, 0.01, 33]
        changed["bodySizeFactor"] = [1, 900, 1e-10]
        second, _ = profiles(changed)
        self.assertEqual(
            [x.findtext("visualScale") for x in first],
            [x.findtext("visualScale") for x in second])

    def test_royal_adult_reference_is_first_adult(self):
        royal = ["VAERoy_AnimalBaby", "VAERoy_AnimalJuvenile",
                 "VAERoy_AnimalAdult", "VAERoy_AnimalAdultIntermediate",
                 "VAERoy_AnimalAdultMax"]
        table = pd.DataFrame([
            {"XML name": "<li>VAERoy_Megachicken</li>",
             "LifeStageDef": stage,
             "BiologicalStageMass_kg": mass,
             "CalculationStatus": "READY"}
            for stage, mass in zip(royal, [2, 85, 1500, 7000, 25000])
        ])
        nodes, _ = profiles(table)
        self.assertEqual(len(nodes), 2)
        self.assertTrue(all(node.findtext("adultMass") == "1500"
                            for node in nodes))
        self.assertAlmostEqual(float(nodes[0].findtext("visualScale")),
                               (2 / 1500) ** (1 / 6), places=9)

    def test_missing_mass_is_rejected(self):
        table = rows()
        table.loc[1, "BiologicalStageMass_kg"] = None
        with self.assertRaisesRegex(ValueError, "missing required numeric cell"):
            profiles(table)

    def test_incomplete_stage_set_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "incomplete juvenile"):
            profiles(rows().iloc[:2])

    def test_nonmonotonic_stage_masses_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "must increase"):
            profiles(rows(masses=(1, 51, 46)))

    def test_metameric_stages_are_not_youth_graphic_profiles(self):
        table = rows()
        extra = pd.DataFrame([{
            "XML name": "<li>Harpactotyrannus</li>",
            "LifeStageDef": "SpectrobiontaPredimago",
            "BiologicalStageMass_kg": 1000,
            "CalculationStatus": "READY"}])
        nodes, _ = profiles(pd.concat([table, extra], ignore_index=True))
        self.assertEqual(len(nodes), 2)


if __name__ == "__main__":
    unittest.main()
