"""Checker reads AnimalStats stage outputs; it never estimates species attacks."""
import unittest

import pandas as pd
from lxml import etree as E

from core.life_stages import STAGE_GROUPS, ROYAL_STAGES
from core.life_stage_species_profiles import (
    render_species_life_stage_profiles, render_life_stage_attack_profiles_cs)
from tests.test_life_stages import fixture


def sources(base_size=1.0):
    animals = pd.DataFrame([{
        "XML name": "<li>TestAnimal</li>",
        "baseBodySize": base_size,
        "baseHungerRate": 1.0,
    }])
    profile_rows = []
    for i, group in enumerate(STAGE_GROUPS):
        profile_rows.append({
            "XML name": "<li>TestAnimal</li>",
            "LifeStageDef": " or ".join(group),
            "ExpectedBaseBodySize": base_size,
            "ExpectedBaseHungerRate": 1.0,
            "ExpectedBodySizeFactor": (1, 0.5, 0.2, 0.15)[i],
            "HungerRateFactor": (1, 0.6, 0.3, 0.25)[i],
            "FilthRate": (1, 0.5, 0.3, 0.2)[i],
            "MeleeDodgeChance": (1, 1.2, 1.5, 1.7)[i],
            "MeleeCritChance": (1, 0.9, 0.8, 0.75)[i],
            "MeleeParryChance": (1, 0.85, 0.65, 0.6)[i],
            "ArmorRating_Sharp": "",
            "ArmorRating_Blunt": "",
            "CombatPowerFactor": "",
            "MarketValueFactor": "",
            "CalculationStatus": "INCOMPLETE",
        })
    profiles = pd.DataFrame(profile_rows)
    empty_attacks = pd.DataFrame(columns=[
        "XML name", "LifeStageDef", "ToolIndex", "ToolLabel",
        "LinkedBodyPartsGroup", "CalculationStatus",
        "VanillaDamageFactor", "VanillaCooldownFactor",
        "CEDamageFactor", "CECooldownFactor",
        "CESharpPenetrationFactor", "CEBluntPenetrationFactor",
    ])
    return profiles, empty_attacks, animals, fixture()


def render(*args, ce=True):
    return render_species_life_stage_profiles(*args, ce=ce)


def nodes(xml):
    return E.fromstring(xml).xpath(
        "./ZoologyLifeStageProfileCatalogDef/stages/li")


class SpeciesLifeStageProfilesTests(unittest.TestCase):
    def test_reads_exact_existing_sheet_values_without_recalculation(self):
        rows = list(sources())
        rows[0].loc[2, "HungerRateFactor"] = .137
        xml, diagnostic = render(*rows)
        baby = [x for x in nodes(xml)
                if x.findtext("sourceStageDefName") == "AnimalBaby"]
        self.assertEqual(len(baby), 1)
        self.assertEqual(float(baby[0].findtext("hungerRateFactor")), .137)
        self.assertEqual(diagnostic["species_rows_incomplete"], 4)

    def test_eusocial_alternative_stage_uses_same_read_data(self):
        xml, _ = render(*sources())
        stage_names = [x.findtext("sourceStageDefName") for x in nodes(xml)]
        self.assertIn("AnimalBaby", stage_names)
        self.assertIn("EusocialInsectLarva", stage_names)
        self.assertIn("AnimalJuvenile", stage_names)
        self.assertIn("EusocialInsectJuvenile", stage_names)

    def test_zero_or_missing_ce_stat_remains_unmodified(self):
        args = list(sources(.05))
        args[0].loc[2, "MeleeCritChance"] = ""
        args[0].loc[2, "MeleeParryChance"] = ""
        xml, _ = render(*args)
        baby = [x for x in nodes(xml)
                if x.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertEqual(baby.xpath("./statFactors/li[stat='MeleeCritChance']"), [])
        self.assertEqual(baby.xpath("./statFactors/li[stat='MeleeParryChance']"), [])

    def test_ce_disabled_never_emits_ce_only_chances(self):
        xml, _ = render(*sources(), ce=False)
        self.assertFalse(E.fromstring(xml).xpath(".//ceOnly"))
        self.assertTrue(E.fromstring(xml).xpath(".//statFactors/li[stat='FilthRate']"))

    def test_new_group_output_changes_stage_without_python_formula(self):
        args = list(sources())
        first, _ = render(*args)
        args[0].loc[2, "MeleeDodgeChance"] = 2.8
        second, _ = render(*args)
        self.assertNotEqual(first, second)
        self.assertIn(b"<factor>2.8</factor>", second)

    def test_multiple_individual_tool_rows(self):
        args = list(sources())
        args[1] = pd.DataFrame([
            {"XML name": "<li>TestAnimal</li>", "LifeStageDef": "AnimalBaby",
             "ToolIndex": 1, "ToolLabel": "left claw",
             "LinkedBodyPartsGroup": "FrontLeftPaw", "CalculationStatus": "READY",
             "VanillaDamageFactor": 0.4, "VanillaCooldownFactor": 1.25,
             "CEDamageFactor": 0.3, "CECooldownFactor": 1.5,
             "CESharpPenetrationFactor": 0.6, "CEBluntPenetrationFactor": 0.7},
            {"XML name": "<li>TestAnimal</li>", "LifeStageDef": "AnimalBaby",
             "ToolIndex": 2, "ToolLabel": "right claw",
             "LinkedBodyPartsGroup": "FrontRightPaw", "CalculationStatus": "READY",
             "VanillaDamageFactor": 0.5, "VanillaCooldownFactor": 1.15,
             "CEDamageFactor": 0.25, "CECooldownFactor": 1.2,
             "CESharpPenetrationFactor": 0.8, "CEBluntPenetrationFactor": 0.9},
        ])
        xml, info = render(*args)
        baby = [x for x in nodes(xml)
                if x.findtext("sourceStageDefName") == "AnimalBaby"][0]
        tools = baby.xpath("./tools/li")
        self.assertEqual([x.findtext("toolIndex") for x in tools], ["1", "2"])
        self.assertEqual([x.findtext("vanillaDamageFactor") for x in tools], ["0.4", "0.5"])
        self.assertEqual(info["attack_tools_ready"], 2)

    def test_incomplete_attack_row_is_not_interpreted_as_neutral_coefficients(self):
        args = list(sources())
        args[1] = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>",
            "LifeStageDef": "AnimalBaby", "ToolIndex": 0,
            "CalculationStatus": "INCOMPLETE", "VanillaDamageFactor": "",
            "VanillaCooldownFactor": "", "CEDamageFactor": "",
            "CECooldownFactor": "", "CESharpPenetrationFactor": "",
            "CEBluntPenetrationFactor": "",
        }])
        xml, info = render(*args)
        self.assertEqual(len(E.fromstring(xml).xpath(".//tools/li")), 0)
        self.assertEqual(info["attack_rows_incomplete"], 1)

    def test_ready_attack_with_missing_or_negative_factor_fails(self):
        args = list(sources())
        row = {
            "XML name": "<li>TestAnimal</li>", "LifeStageDef": "AnimalBaby",
            "ToolIndex": 0, "ToolLabel": "head", "CalculationStatus": "READY",
            "VanillaDamageFactor": 1.0, "VanillaCooldownFactor": 1.0,
            "CEDamageFactor": 1.0, "CECooldownFactor": 1.0,
            "CESharpPenetrationFactor": 1.0, "CEBluntPenetrationFactor": "",
        }
        args[1] = pd.DataFrame([row])
        with self.assertRaisesRegex(ValueError, "missing required numeric"):
            render(*args)
        row["CEBluntPenetrationFactor"] = -1
        with self.assertRaisesRegex(ValueError, "invalid positive"):
            render(*args)

    def test_stale_adult_baselines_are_rejected(self):
        args = list(sources())
        args[2].loc[0, "baseBodySize"] = 1.25
        with self.assertRaisesRegex(ValueError, "source baseline changed"):
            render(*args)

    def test_ready_species_requires_armor_combat_and_market(self):
        args = list(sources())
        args[0].loc[1, "CalculationStatus"] = "READY"
        with self.assertRaisesRegex(ValueError, "missing outputs"):
            render(*args)

    def test_regeneration_is_deterministic(self):
        args = sources()
        first, _ = render(*args)
        second, _ = render(*args)
        self.assertEqual(first, second)
        self.assertNotIn(b"<sourceStageDefName>AnimalAdult</sourceStageDefName>", first)

    def test_optional_missing_species_is_kept_in_xml_but_not_installed_by_game(self):
        args = list(sources())
        second = args[0].iloc[2].copy()
        second["XML name"] = "<li>OptionalModAnimal</li>"
        args[0] = pd.concat([args[0], pd.DataFrame([second])], ignore_index=True)
        args[2] = pd.concat([args[2], pd.DataFrame([{
            "XML name": "<li>OptionalModAnimal</li>",
            "baseBodySize": 1.0, "baseHungerRate": 1.0,
        }])], ignore_index=True)
        xml, _ = render(*args)
        self.assertIn(b"OptionalModAnimal", xml)


    def test_compiled_attack_source_is_from_ready_rows_only(self):
        attack = pd.DataFrame([
            {"XML name": "<li>TestAnimal</li>", "LifeStageDef": "AnimalBaby",
             "ToolIndex": 2, "ToolLabel": "right claw",
             "LinkedBodyPartsGroup": "FrontRightPaw",
             "CalculationStatus": "READY",
             "VanillaDamageFactor": 0.4, "VanillaCooldownFactor": 1.2,
             "CEDamageFactor": 0.35, "CECooldownFactor": 1.1,
             "CESharpPenetrationFactor": 0.8, "CEBluntPenetrationFactor": 0.7},
            {"XML name": "<li>TestAnimal</li>", "LifeStageDef": "AnimalJuvenile",
             "ToolIndex": 0, "CalculationStatus": "INCOMPLETE",
             "VanillaDamageFactor": "", "VanillaCooldownFactor": "",
             "CEDamageFactor": "", "CECooldownFactor": "",
             "CESharpPenetrationFactor": "", "CEBluntPenetrationFactor": ""},
        ])
        output, info = render_life_stage_attack_profiles_cs(attack)
        self.assertTrue(output.startswith(b"// <auto-generated>"))
        self.assertIn(b"vanillaDamageFactor = 0.4f", output)
        self.assertIn(b"ceSharpPenetrationFactor = 0.8f", output)
        self.assertIn(b"FrontRightPaw", output)
        self.assertNotIn(b"AnimalJuvenile", output)
        self.assertEqual(info["attack_rows_incomplete"], 1)
        self.assertEqual(output, render_life_stage_attack_profiles_cs(attack)[0])
        self.assertGreater(output.count(b"\n"), 10)


if __name__ == "__main__":
    unittest.main()
