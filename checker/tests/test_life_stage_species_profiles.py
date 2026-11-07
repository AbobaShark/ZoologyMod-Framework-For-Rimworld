"""Regression tests for precomputed life-stage profiles and separate Tool indices."""
import unittest

import pandas as pd
from lxml import etree as E

from core.life_stages import STAGE_GROUPS
from core.life_stage_species_profiles import (
    render_species_life_stage_profiles, render_life_stage_attack_profiles_cs)


def sources(base_size=1):
    animals = pd.DataFrame([{
        "XML name": "<li>TestAnimal</li>",
        "baseBodySize": base_size,
        "baseHungerRate": 1,
    }])
    profiles = pd.DataFrame([{
        "XML name": "<li>TestAnimal</li>",
        "LifeStageDef": " or ".join(group),
        "BiologicalStageMass_kg": (10, 2, .05)[i],
        "StageBodySize": base_size * (1, .5, .2)[i],
        "bodySizeFactor": (1, .5, .2)[i],
        "SpeedFactor": (1, .85, .65)[i],
        "FoodMaxFactor": (1, .2, .005)[i],
        "StageHealthScaleFactor": (1, .5, .2)[i],
        "CE_ArmorDurabilityFactor": (1, .5, .2)[i],
        "HungerRateFactor": (1, .6, .3)[i],
        "FilthRate": (1, .5, .3)[i],
        "CE_MeleeDodgeChance": (1, 1.2, 1.5)[i],
        "CE_MeleeCritChance": (1, .9, .8)[i],
        "CE_MeleeParryChance": (1, .85, .65)[i],
        "ArmorRating_Sharp": "",
        "ArmorRating_Blunt": "",
        "CE_ArmorRating_Sharp": (1, .8, .5)[i],
        "CE_ArmorRating_Blunt": (1, .9, .6)[i],
        "CombatPowerFactor": (1, .8, .4)[i],
        "MarketValueFactor": (1, .75, .5)[i],
        "CalculationStatus": "READY",
        "ToolStatus": "READY",
        "ToolCount": 0,
        "ToolRecordsV2": "",
    } for i, group in enumerate(STAGE_GROUPS)])
    return profiles, profiles, animals


def tool_record(v_index=1, ce_index=3, label="left claw", group="FrontLeftPaw",
                capacities="Scratch", factors=(.4, 1.2, .3, 1.1, 0, .5)):
    fields = (v_index, ce_index, label, group, capacities, *factors)
    return "\t".join(str(field) for field in fields)


def with_tools(args, records, stage_row=2):
    args = list(args)
    args[0] = args[0].copy()
    args[0].at[stage_row, "ToolRecordsV2"] = "\n".join(records)
    args[0].at[stage_row, "ToolCount"] = len(records)
    args[1] = args[0]
    return args


def render(*args, ce=True):
    return render_species_life_stage_profiles(*args, ce=ce)


def nodes(xml):
    return E.fromstring(xml).xpath(
        "./ZoologyLifeStageProfileCatalogDef/stages/li")


class SpeciesLifeStageProfilesTests(unittest.TestCase):
    def test_custom_spectrobiont_stage_can_compile_species_factors(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[0, "LifeStageDef"] = "SpectrobiontaImago"
        args[0].at[1, "LifeStageDef"] = "SpectrobiontaLarvaMedium"
        args[0].at[1, "StageHealthScaleFactor"] = .48
        args[1] = args[0]
        xml, diagnostics = render(*args)
        defs = [node.findtext("sourceStageDefName") for node in nodes(xml)]
        self.assertIn("SpectrobiontaImago", defs)
        self.assertIn("SpectrobiontaLarvaMedium", defs)
        self.assertGreater(diagnostics["profiles"], 0)


    def test_exact_animalstats_factor_and_eusocial_alternative(self):
        args = list(sources())
        args[0].at[2, "HungerRateFactor"] = .137
        args[1] = args[0]
        xml, info = render(*args)
        baby = [n for n in nodes(xml) if n.findtext("sourceStageDefName") == "AnimalBaby"]
        self.assertEqual(len(baby), 1)
        self.assertEqual(float(baby[0].findtext("hungerRateFactor")), .137)
        stages = [node.findtext("sourceStageDefName") for node in nodes(xml)]
        self.assertIn("EusocialInsectLarva", stages)
        self.assertEqual(info.get("species_rows_incomplete", 0), 0)

    def test_ce_armor_is_separate_from_vanilla_armor(self):
        xml, _ = render(*sources())
        baby = [n for n in nodes(xml) if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        ce_armor = baby.xpath("./statFactors/li[stat='ArmorRating_Sharp']/ceOnly")
        self.assertEqual(len(ce_armor), 1)
        self.assertEqual(ce_armor[0].text, "true")
        self.assertEqual(baby.xpath("./statFactors/li[stat='ArmorRating_Sharp'][not(ceOnly)]"), [])

    def test_ce_armor_durability_comes_from_animalstats(self):
        xml, _ = render(*sources())
        baby = [n for n in nodes(xml) if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertEqual(baby.findtext("ceArmorDurabilityFactor"), "0.2")
        xml_without_ce, _ = render(*sources(), ce=False)
        baby_without_ce = [n for n in nodes(xml_without_ce) if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertIsNone(baby_without_ce.find("ceArmorDurabilityFactor"))

    def test_zero_ce_armor_durability_is_valid_after_sheet_rounding(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[2, "CE_ArmorDurabilityFactor"] = 0
        args[1] = args[0]
        xml, _ = render(*args)
        baby = [n for n in nodes(xml) if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertEqual(baby.findtext("ceArmorDurabilityFactor"), "0")
        xml_vanilla, _ = render(*args, ce=False)
        baby_vanilla = [n for n in nodes(xml_vanilla) if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertIsNone(baby_vanilla.find("ceArmorDurabilityFactor"))

    def test_missing_ce_armor_durability_is_not_silently_accepted(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[2, "CE_ArmorDurabilityFactor"] = ""
        args[1] = args[0]
        with self.assertRaisesRegex(ValueError, "missing required"):
            render(*args)

    def test_negative_ce_armor_durability_is_invalid(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[2, "CE_ArmorDurabilityFactor"] = -0.5
        args[1] = args[0]
        with self.assertRaisesRegex(ValueError, "invalid nonnegative"):
            render(*args)

    def test_species_body_and_health_factors_can_change_independently_of_core(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[2, "StageBodySize"] = .1
        args[0].at[2, "bodySizeFactor"] = .1
        args[0].at[2, "StageHealthScaleFactor"] = .1
        args[1] = args[0]
        xml, _ = render(*args)
        baby = [n for n in nodes(xml) if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertIsNone(baby.find("expectedBodySizeFactor"))
        self.assertEqual(baby.findtext("bodySizeFactor"), "0.1")
        self.assertEqual(baby.findtext("healthScaleFactor"), "0.1")

    def test_ce_disabled_keeps_only_vanilla_stats(self):
        xml, _ = render(*sources(), ce=False)
        self.assertEqual(E.fromstring(xml).xpath(".//ceOnly"), [])
        self.assertTrue(E.fromstring(xml).xpath(".//statFactors/li[stat='FilthRate']"))

    def test_missing_nonapplicable_stats_remain_absent(self):
        args = list(sources(.05))
        args[0] = args[0].copy()
        args[0].at[2, "CE_MeleeCritChance"] = ""
        args[0].at[2, "CE_MeleeParryChance"] = ""
        args[1] = args[0]
        xml, _ = render(*args)
        baby = [n for n in nodes(xml) if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertEqual(baby.xpath("./statFactors/li[stat='MeleeCritChance']"), [])
        self.assertEqual(baby.xpath("./statFactors/li[stat='MeleeParryChance']"), [])

    def test_zero_adult_armor_uses_absolute_ce_offset(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0]["CE_ArmorRating_Sharp_Offset"] = ""
        args[0]["CE_ArmorRating_Blunt_Offset"] = ""
        args[0].at[2, "CE_ArmorRating_Sharp"] = 1
        args[0].at[2, "CE_ArmorRating_Blunt"] = 1
        args[0].at[2, "CE_ArmorRating_Sharp_Offset"] = .02
        args[0].at[2, "CE_ArmorRating_Blunt_Offset"] = .05
        args[1] = args[0]
        xml, _ = render(*args)
        baby = [n for n in nodes(xml) if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertEqual([n.findtext("offset") for n in baby.xpath("./statOffsets/li")],
                         ["0.02", "0.05"])
        self.assertEqual([n.findtext("ceOnly") for n in baby.xpath("./statOffsets/li")],
                         ["true", "true"])
        self.assertFalse(baby.xpath("./statFactors/li[stat='ArmorRating_Sharp']"))
        xml_without_ce, _ = render(*args, ce=False)
        baby_without_ce = [n for n in nodes(xml_without_ce)
                           if n.findtext("sourceStageDefName") == "AnimalBaby"][0]
        self.assertEqual(baby_without_ce.xpath("./statOffsets/li"), [])

    def test_armor_offset_rejects_non_neutral_factor_and_bad_formula(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[2, "CE_ArmorRating_Sharp_Offset"] = .02
        args[1] = args[0]
        with self.assertRaisesRegex(ValueError, "requires neutral"):
            render(*args)
        args[0].at[2, "CE_ArmorRating_Sharp"] = 1
        args[0].at[2, "CE_ArmorRating_Sharp_Offset"] = "#REF!"
        with self.assertRaisesRegex(ValueError, "source formula error"):
            render(*args)

    def test_tool_has_distinct_indices_and_six_independent_factors(self):
        args = with_tools(sources(), [
            tool_record(1, 3),
            tool_record(2, 4, "right claw", "FrontRightPaw", "Scratch",
                        (.5, 1.3, .2, 1.4, .6, .9)),
        ])
        cs, info = render_life_stage_attack_profiles_cs(args[0])
        self.assertIn(b"vanillaToolIndex = 1", cs)
        self.assertIn(b"ceToolIndex = 3", cs)
        self.assertIn(b"ceToolIndex = 4", cs)
        self.assertIn(b"capacities = \"Scratch\"", cs)
        self.assertIn(b"ceSharpPenetrationFactor = 0f", cs)
        self.assertIn(b"ceBluntPenetrationFactor = 0.5f", cs)
        self.assertIn(b"vanillaCooldownFactor = 1.2f", cs)
        self.assertEqual(info["compiled_stage_keys"], 2)
        self.assertEqual(info["attack_tools_ready"], 2)
        self.assertEqual(cs, render_life_stage_attack_profiles_cs(args[0])[0])

    def test_tool_missing_in_one_mode_is_valid(self):
        args = with_tools(sources(), [
            tool_record(-1, 0, factors=("", "", .3, 1.1, 0, .5))
        ])
        cs, _ = render_life_stage_attack_profiles_cs(args[0])
        self.assertIn(b"vanillaToolIndex = -1", cs)
        self.assertIn(b"ceToolIndex = 0", cs)
        args = with_tools(sources(), [
            tool_record(0, -1, factors=(.4, 1.2, "", "", "", ""))
        ])
        cs, _ = render_life_stage_attack_profiles_cs(args[0])
        self.assertIn(b"ceToolIndex = -1", cs)

    def test_unmatched_mode_with_non_neutral_data_is_rejected(self):
        args = with_tools(sources(), [tool_record(-1, 0)])
        with self.assertRaisesRegex(ValueError, "non-neutral factor for attack absent"):
            render_life_stage_attack_profiles_cs(args[0])

    def test_two_missing_indices_rejected(self):
        args = with_tools(sources(), [tool_record(-1, -1)])
        with self.assertRaisesRegex(ValueError, "absent in both modes"):
            render_life_stage_attack_profiles_cs(args[0])

    def test_duplicate_index_in_same_mode_rejected(self):
        args = with_tools(sources(), [
            tool_record(1, 3),
            tool_record(1, 4, "right claw", "FrontRightPaw"),
        ])
        with self.assertRaisesRegex(ValueError, "duplicate Vanilla ToolIndex"):
            render_life_stage_attack_profiles_cs(args[0])

    def test_corrupt_formula_or_missing_factor_rejected(self):
        args = with_tools(sources(), [tool_record(factors=(.2, 1, .3, 1, .4, "#REF!"))])
        with self.assertRaisesRegex(ValueError, "source formula error"):
            render_life_stage_attack_profiles_cs(args[0])
        args = with_tools(sources(), [tool_record(factors=(.2, 0, .3, 1, .4, .5))])
        with self.assertRaisesRegex(ValueError, "invalid positive"):
            render_life_stage_attack_profiles_cs(args[0])

    def test_adult_tool_must_be_neutral_and_never_compiled(self):
        args = with_tools(sources(), [tool_record()], stage_row=0)
        with self.assertRaisesRegex(ValueError, "Adult must have neutral Tool factors"):
            render(*args)
        with self.assertRaisesRegex(ValueError, "Adult must have neutral Tool factors"):
            render_life_stage_attack_profiles_cs(args[0])

    def test_incomplete_status_does_not_supply_neutral_attack(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[2, "CalculationStatus"] = "INCOMPLETE"
        args[0].at[2, "ToolStatus"] = "INCOMPLETE"
        args[1] = args[0]
        xml, info = render(*args)
        self.assertEqual(info["species_rows_incomplete"], 1)
        self.assertNotIn(b"<sourceStageDefName>AnimalBaby</sourceStageDefName>", xml)
        cs, report = render_life_stage_attack_profiles_cs(args[0])
        self.assertEqual(report["attack_rows_incomplete"], 1)
        self.assertNotIn(b"TestAnimal\\nAnimalBaby", cs)

    def test_stale_baseline_is_rejected(self):
        args = list(sources())
        args[2].at[0, "baseBodySize"] = 1.25
        with self.assertRaisesRegex(ValueError, "source baseline changed"):
            render(*args)

    def test_ready_scalar_requires_combat_and_market(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[2, "MarketValueFactor"] = ""
        args[1] = args[0]
        with self.assertRaisesRegex(ValueError, "missing outputs"):
            render(*args)

    def test_unknown_stage_rejected(self):
        args = list(sources())
        args[0] = args[0].copy()
        args[0].at[2, "LifeStageDef"] = "UnknownInfant"
        args[1] = args[0]
        with self.assertRaisesRegex(ValueError, "unknown stage"):
            render_life_stage_attack_profiles_cs(args[0])

    def test_tool_count_must_match(self):
        args = with_tools(sources(), [tool_record()])
        args[0].at[2, "ToolCount"] = 2
        with self.assertRaisesRegex(ValueError, "ToolRecordsV2 and ToolCount disagree"):
            render_life_stage_attack_profiles_cs(args[0])

    def test_group_parameter_changes_output_without_checker_physics(self):
        args = list(sources())
        first, _ = render(*args)
        args[0] = args[0].copy()
        args[0].at[2, "CE_MeleeDodgeChance"] = 2.8
        args[1] = args[0]
        second, _ = render(*args)
        self.assertNotEqual(first, second)
        self.assertIn(b"<factor>2.8</factor>", second)

    def test_absent_optional_mod_species_stays_declarative(self):
        args = list(sources())
        duplicate = args[0].iloc[2].copy()
        duplicate["XML name"] = "<li>OptionalModAnimal</li>"
        args[0] = pd.concat([args[0], pd.DataFrame([duplicate])], ignore_index=True)
        args[1] = args[0]
        args[2] = pd.concat([args[2], pd.DataFrame([{
            "XML name": "<li>OptionalModAnimal</li>",
            "baseBodySize": 1, "baseHungerRate": 1,
        }])], ignore_index=True)
        xml, _ = render(*args)
        self.assertIn(b"OptionalModAnimal", xml)

    def test_adult_reuses_existing_def_and_young_are_species_specific(self):
        xml, diagnostics = render(*sources())
        self.assertNotIn(b"<sourceStageDefName>AnimalAdult</sourceStageDefName>", xml)
        self.assertNotIn(b"<sourceStageDefName>EusocialInsectAdult</sourceStageDefName>", xml)
        self.assertIn(b"<sourceStageDefName>AnimalJuvenile</sourceStageDefName>", xml)
        self.assertIn(b"<sourceStageDefName>AnimalBaby</sourceStageDefName>", xml)
        self.assertEqual(diagnostics["adult_reference_rows"], 1)
        self.assertNotIn(b"AnimalBabyTiny", xml)


if __name__ == "__main__":
    unittest.main()
