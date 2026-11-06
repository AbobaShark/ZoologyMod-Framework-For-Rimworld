"""Deterministic species-stage correction tests; no live Sheets dependency."""
import unittest

import pandas as pd
from lxml import etree as E

from core.body_size_mass_equivalence import body_size_equivalent_mass_kg
from core.life_stage_species_profiles import (
    _hunger_for_body_size, _mround_nonnegative,
    render_species_life_stage_profiles,
)
from tests.test_life_stages import fixture


def sample(body_size=1.0):
    latin = "Testus animalis"
    group = "Test allometry"
    filth = 2
    mass = body_size_equivalent_mass_kg(body_size)
    dod = _mround_nonnegative(mass ** -0.165)
    crit = _mround_nonnegative(0.2 * mass ** 0.165)
    parry = _mround_nonnegative(0.1 * mass ** 0.165)
    animals = pd.DataFrame([{
        "XML name": "<li>TestAnimal</li>",
        "Latin name": latin,
        "baseBodySize": body_size,
        "baseHungerRate": _hunger_for_body_size(body_size, "Bird", False),
        "FilthRate": filth,
        "Ectothermic": "False",
    }])
    base = pd.DataFrame([{"Species": latin, "Skill group": group,
                          "Body mass (kg)": mass}])
    groups = pd.DataFrame([{
        "Name": group,
        "filthMultiplier": filth,
        "Physiology": "Bird",
        "meleeDodgeChanceMultiplier": 1.0,
        "meleeCritChanceMultiplier": 0.2,
        "meleeParryChanceMultiplier": 0.1,
    }])
    ce = pd.DataFrame([{
        "XML name": "<li>TestAnimal</li>",
        "MeleeDodgeChance": dod,
        "MeleeCritChance": crit,
        "MeleeParryChance": parry,
    }])
    return animals, ce, base, groups, fixture()


class SpeciesLifeStageProfileTests(unittest.TestCase):
    def render(self, *sources):
        return render_species_life_stage_profiles(*(sources or sample()))

    def test_age_hunger_and_dirt_follow_absolute_animalstats_formulas(self):
        xml, info = self.render()
        root = E.fromstring(xml)
        baby = root.xpath(
            './ZoologyLifeStageProfileCatalogDef/stages/li'
            '[animalDefName="TestAnimal"][sourceStageDefName="AnimalBaby"]')
        self.assertEqual(len(baby), 1)
        entry = baby[0]
        adult_rate = _hunger_for_body_size(1.0, "Bird", False)
        baby_rate = _hunger_for_body_size(0.2, "Bird", False)
        self.assertAlmostEqual(float(entry.findtext("hungerRateFactor")), baby_rate / adult_rate)
        # Adult: ceil(1*2)=2; baby: ceil(0.2*2)=1.
        stat = entry.xpath("./statFactors/li[stat='FilthRate']/factor/text()")
        self.assertEqual(stat, ["0.5"])
        self.assertEqual(info["profiles"], 5)

    def test_ce_chances_are_mass_based_after_rounding(self):
        sources = sample()
        xml, _ = self.render(*sources)
        root = E.fromstring(xml)
        baby = root.xpath(
            "./ZoologyLifeStageProfileCatalogDef/stages/li"
            "[sourceStageDefName='AnimalBaby']")[0]
        stat = {
            x.findtext("stat"): float(x.findtext("factor"))
            for x in baby.xpath("./statFactors/li")
        }
        mass = body_size_equivalent_mass_kg(0.2)
        baseline = sources[1].iloc[0]
        expected = _mround_nonnegative(mass ** -0.165) / baseline["MeleeDodgeChance"]
        self.assertAlmostEqual(stat["MeleeDodgeChance"], expected, places=9)
        self.assertNotAlmostEqual(stat["MeleeDodgeChance"], 0.2 ** -0.165, places=2)

    def test_attack_profile_addresses_only_one_tool(self):
        animals, ce, base, groups, life = sample()
        attacks = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>",
            "LifeStageDef": "AnimalBaby",
            "ToolIndex": "2",
            "ToolLabel": "bite",
            "LinkedBodyPartsGroup": "Teeth",
            "VanillaDamageFactor": "1.25",
            "CECooldownFactor": "1.1",
            "CESharpPenetrationFactor": "1.2",
        }])
        xml, _ = render_species_life_stage_profiles(
            animals, ce, base, groups, life, attacks)
        node = E.fromstring(xml).xpath(
            "./ZoologyLifeStageProfileCatalogDef/stages/li"
            "[sourceStageDefName='AnimalBaby']/tools/li")
        self.assertEqual(len(node), 1)
        self.assertEqual(node[0].findtext("toolIndex"), "2")
        self.assertEqual(node[0].findtext("label"), "bite")
        self.assertEqual(node[0].findtext("linkedBodyPartsGroup"), "Teeth")
        self.assertEqual(node[0].findtext("vanillaDamageFactor"), "1.25")
        self.assertIsNone(node[0].find("ceDamageFactor"))

    def test_unmatched_tool_profile_fails(self):
        animals, ce, base, groups, life = sample()
        attacks = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>",
            "LifeStageDef": "AlienBaby",
            "ToolIndex": "0",
            "VanillaDamageFactor": "2",
        }])
        with self.assertRaisesRegex(ValueError, "Unmatched LifeStageAttackProfiles"):
            render_species_life_stage_profiles(
                animals, ce, base, groups, life, attacks)

    def test_unresolved_sheet_formula_keeps_original_stage(self):
        animals, ce, base, groups, life = sample()
        animals.loc[0, "baseBodySize"] = "#REF! (Please connect this sheet.)"
        xml, info = render_species_life_stage_profiles(
            animals, ce, base, groups, life)
        self.assertNotIn("TestAnimal", xml.decode("utf-8"))
        self.assertEqual(info["unresolved_species"], ["TestAnimal"])

    def test_cannot_fabricate_below_minimum_game_body_size(self):
        animals, ce, base, groups, life = sample(0.05)
        xml, info = render_species_life_stage_profiles(
            animals, ce, base, groups, life)
        self.assertGreater(info["mass_below_or_above_mapped_bins"], 0)
        baby = E.fromstring(xml).xpath(
            "./ZoologyLifeStageProfileCatalogDef/stages/li"
            "[sourceStageDefName='AnimalBaby']")[0]
        self.assertIsNone(
            baby.xpath("./statFactors/li[stat='MeleeDodgeChance']") or None)


    def test_ectothermic_hunger_model_precedes_bird_model(self):
        animals, ce, base, groups, life = sample()
        animals.loc[0, "Ectothermic"] = "True"
        animals.loc[0, "baseHungerRate"] = _hunger_for_body_size(1.0, "Bird", True)
        xml, _ = self.render(animals, ce, base, groups, life)
        node = E.fromstring(xml).xpath(
            './ZoologyLifeStageProfileCatalogDef/stages/li'
            '[sourceStageDefName="AnimalBaby"]')[0]
        self.assertAlmostEqual(float(node.findtext("hungerRateFactor")),
                               _hunger_for_body_size(.2, "Bird", True)
                               / _hunger_for_body_size(1., "Bird", True))

    def test_ce_disabled_keeps_vanilla_and_omits_ce_stats(self):
        animals, _, base, groups, life = sample()
        xml, _ = self.render(animals, None, base, groups, life)
        root = E.fromstring(xml)
        self.assertEqual(len(root.xpath(".//statFactors/li[ceOnly='true']")), 0)
        self.assertTrue(root.xpath(".//statFactors/li[stat='FilthRate']"))

    def test_source_group_change_requires_recalculated_adult_ce_chances(self):
        animals, ce, base, groups, life = sample()
        groups.loc[0, "meleeDodgeChanceMultiplier"] = 2.0
        with self.assertRaisesRegex(ValueError, "recalculate source"):
            self.render(animals, ce, base, groups, life)
        mass = base.loc[0, "Body mass (kg)"]
        ce.loc[0, "MeleeDodgeChance"] = _mround_nonnegative(2 * mass ** -.165)
        xml, _ = self.render(animals, ce, base, groups, life)
        self.assertIn(b"MeleeDodgeChance", xml)

    def test_broken_market_value_formula_skips_species(self):
        animals, ce, base, groups, life = sample()
        animals.loc[0, "Market value"] = "#REF!"
        xml, info = self.render(animals, ce, base, groups, life)
        self.assertNotIn(b"TestAnimal", xml)
        self.assertIn("TestAnimal", info["unresolved_species"])

    def test_zero_crit_parry_bases_are_not_divided(self):
        animals, ce, base, groups, life = sample()
        ce.loc[0, "MeleeCritChance"] = 0
        ce.loc[0, "MeleeParryChance"] = 0
        groups.loc[0, "meleeCritChanceMultiplier"] = 0
        groups.loc[0, "meleeParryChanceMultiplier"] = 0
        xml, _ = self.render(animals, ce, base, groups, life)
        self.assertNotIn(b"<stat>MeleeCritChance</stat>", xml)
        self.assertNotIn(b"<stat>MeleeParryChance</stat>", xml)

    def test_multiple_independent_attacks_and_deterministic_regeneration(self):
        animals, ce, base, groups, life = sample()
        attacks = pd.DataFrame([
            {"XML name": "<li>TestAnimal</li>", "LifeStageDef": "AnimalBaby",
             "ToolIndex": 0, "ToolLabel": "left claw",
             "VanillaDamageFactor": 1.1, "CEDamageFactor": 1.2},
            {"XML name": "<li>TestAnimal</li>", "LifeStageDef": "AnimalBaby",
             "ToolIndex": 1, "ToolLabel": "right claw",
             "VanillaDamageFactor": 1.3, "CEDamageFactor": 1.4},
        ])
        xml1, _ = render_species_life_stage_profiles(
            animals, ce, base, groups, life, attacks)
        xml2, _ = render_species_life_stage_profiles(
            animals, ce, base, groups, life, attacks)
        self.assertEqual(xml1, xml2)
        tools = E.fromstring(xml1).xpath(
            './ZoologyLifeStageProfileCatalogDef/stages/li'
            '[sourceStageDefName="AnimalBaby"]/tools/li')
        self.assertEqual([node.findtext("toolIndex") for node in tools], ["0", "1"])
        self.assertEqual([node.findtext("ceDamageFactor") for node in tools], ["1.2", "1.4"])

    def test_nonpositive_attack_factor_fails(self):
        animals, ce, base, groups, life = sample()
        attacks = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>", "LifeStageDef": "AnimalBaby",
            "ToolIndex": 0, "VanillaDamageFactor": -1,
        }])
        with self.assertRaisesRegex(ValueError, "VanillaDamageFactor"):
            render_species_life_stage_profiles(animals, ce, base, groups, life, attacks)

    def test_ce_source_missing_species_fails(self):
        animals, ce, base, groups, life = sample()
        ce.loc[0, "XML name"] = "<li>AnotherAnimal</li>"
        with self.assertRaisesRegex(ValueError, "lacks species"):
            self.render(animals, ce, base, groups, life)


if __name__ == "__main__":
    unittest.main()
