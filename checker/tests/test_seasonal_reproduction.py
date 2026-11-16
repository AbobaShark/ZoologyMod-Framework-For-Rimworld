import unittest
from pathlib import Path

import pandas as pd
from core.seasonal_reproduction import (
    COLUMNS, duration_ticks, season_mask, seasonal_fields,
    gestation_calendar_possible, TICKS_PER_DAY, native_reproduction,
)
from rimworld_xml_generator import (
    build_mod_extensions_for_child, build_mod_extensions_for_parent, _common_value_and_differing,
)
from core.patch_engine import PatchGenerator
from core.rimworld_patch_apply import PatchApplier
from lxml import etree as ET


class SeasonalReproductionTests(unittest.TestCase):
    def row(self, season="", diapause="", pause=""):
        return pd.Series(dict(zip(COLUMNS, (season, diapause, pause))))

    def test_seasons_and_durations(self):
        self.assertEqual(season_mask("Spring, Autumn"), 5)
        self.assertEqual(season_mask("Winter, Summer"), 10)
        self.assertEqual(season_mask("All"), 15)
        self.assertEqual(season_mask(""), 15)
        self.assertEqual(duration_ticks("4,5"), 270000)
        self.assertEqual(duration_ticks(0), 0)

    def test_species_profile(self):
        fields = seasonal_fields(self.row("Spring", 4, ""))
        self.assertEqual(fields, {"matingSeasonMask": 1, "embryonicDiapauseTicks": 240000})
        self.assertEqual(seasonal_fields(self.row("", "", 58)), {"reproductivePauseTicks": 3480000})
        self.assertEqual(seasonal_fields(self.row("All", 0, 0)), {})
        self.assertEqual(seasonal_fields(self.row(float("nan"), "", "")), {})

    def test_gestation_calendar_wrap_and_edges(self):
        day = TICKS_PER_DAY
        self.assertTrue(gestation_calendar_possible(8, 1, 15 * day))
        self.assertFalse(gestation_calendar_possible(1, 4, 15 * day))
        self.assertTrue(gestation_calendar_possible(1, 4, 15 * day + 1))
        self.assertTrue(gestation_calendar_possible(1, 1, 60 * day))
        row = self.row("Spring", 0)
        row["Gestation period (days)"] = 10
        row["Birth / laying season"] = "Autumn"
        with self.assertRaisesRegex(ValueError, "No mating date"):
            seasonal_fields(row)
        row["Embryonic diapause (game days)"] = 15
        self.assertEqual(seasonal_fields(row)["birthSeasonMask"], 4)
        row["Gestation period (days)"] = "Lay eggs"
        self.assertEqual(seasonal_fields(row)["birthSeasonMask"], 4)

    def test_native_reproduction_not_hoisted(self):
        rows = [
            pd.Series({"Gestation period (days)": "Native reproduction", "Litter size": "Native reproduction"}),
            pd.Series({"Gestation period (days)": 8, "Litter size": 2}),
        ]
        generator = PatchGenerator.__new__(PatchGenerator)
        common, differing, _ = generator.compute_abstract_common(["Native", "Modeled"], dict(zip(["Native", "Modeled"], rows)))
        for col in ("gestationPeriodDays", "Gestation period (days)", "Litter size"):
            self.assertNotIn(col, common)
            self.assertNotIn(col, differing)
            self.assertEqual(_common_value_and_differing(rows, col), (None, False))
        self.assertTrue(native_reproduction(rows[0]["Gestation period (days)"]))
        row = self.row("Spring")
        row["Birth / laying season"] = "Summer"
        row["Gestation period (days)"] = "Native reproduction"
        self.assertEqual(seasonal_fields(row), {"matingSeasonMask": 1, "birthSeasonMask": 2})

    def test_climate_and_birth_profile(self):
        row = self.row("Spring", 28.767123287671232)
        row["Birth / laying season"] = "Winter"
        row["Thermal breeding minimum (°C)"] = ""
        row["Dormancy threshold (°C)"] = "0"
        fields = seasonal_fields(row)
        self.assertEqual(fields["birthSeasonMask"], 8)
        self.assertEqual(fields["embryonicDiapauseTicks"], 1726027)
        self.assertEqual(fields["dormancyThreshold"], "0")
        frog = self.row("Spring, Summer")
        frog["Thermal breeding minimum (°C)"] = "20"
        self.assertEqual(seasonal_fields(frog)["thermalBreedingMinimum"], "20")
        frog["Thermal breeding minimum (°C)"] = "#REF!"
        with self.assertRaises(ValueError):
            seasonal_fields(frog)

    def test_explicit_year_round_laying_is_distinct_from_missing_season(self):
        row = self.row("Spring")
        row["Gestation period (days)"] = "Lay eggs"
        self.assertNotIn("birthSeasonMask", seasonal_fields(row))
        row["Birth / laying season"] = "All"
        self.assertEqual(seasonal_fields(row)["birthSeasonMask"], 15)

    def test_alternative_offspring_counts_and_incubation(self):
        row = self.row("All")
        row["Realistic litter size"] = "11,1"
        row["Captive gestation (game days)"] = 7.5
        fields = seasonal_fields(row)
        self.assertEqual(fields["realisticLitterMean"], "11.1")
        self.assertEqual(fields["captiveGestationDays"], "7.5")
        row = self.row("All")
        row["Realistic egg count range"] = "44~46"
        row["Captive incubation (game days)"] = 7.0
        fields = seasonal_fields(row)
        self.assertEqual(fields["realisticEggCountRange"], "44~46")
        self.assertEqual(fields["captiveIncubationDays"], "7.0")
        for value in (1, "1.0", "1,0"):
            row["Realistic egg count range"] = value
            self.assertEqual(seasonal_fields(row)["realisticEggCountRange"], "1~1")
        for value in ("0", "5~3", "1.5", "#REF!", "1~2147483648"):
            row["Realistic egg count range"] = value
            with self.assertRaises(ValueError):
                seasonal_fields(row)
        row["Realistic egg count range"] = "2~4"
        row["Captive incubation (game days)"] = 0
        with self.assertRaises(ValueError):
            seasonal_fields(row)

    def test_captive_bird_laying_rates(self):
        row = self.row("All")
        row["Captive egg formation (game days)"] = 0.5
        row["Captive real egg formation (game days)"] = 1.503
        row["Captive reproductive pause (game days)"] = 0
        row["Realistic egg count range"] = "1"
        fields = seasonal_fields(row)
        self.assertEqual(fields["captiveEggFormationDays"], "0.5")
        self.assertEqual(fields["captiveRealEggFormationDays"], "1.503")
        self.assertEqual(fields["captiveReproductivePauseDays"], "0")
        self.assertAlmostEqual(60 / float(fields["captiveEggFormationDays"]), 120)
        self.assertAlmostEqual(60 / float(fields["captiveRealEggFormationDays"]), 242.85 * 60 / 365, places=1)
        row["Captive real egg formation (game days)"] = 0
        with self.assertRaises(ValueError):
            seasonal_fields(row)
        row["Captive real egg formation (game days)"] = 1.503
        del row["Captive egg formation (game days)"]
        with self.assertRaises(ValueError):
            seasonal_fields(row)

    def test_captive_parameters(self):
        row = self.row("Spring", 4, 8)
        row["Captive gestation (game days)"] = 6.5
        row["Captive diapause (game days)"] = 0
        row["Captive real diapause (game days)"] = 4
        row["Captive reproductive pause (game days)"] = 0
        row["Captive egg formation (game days)"] = 1
        fields = seasonal_fields(row)
        self.assertEqual(fields["captiveGestationDays"], "6.5")
        self.assertEqual(fields["captiveDiapauseDays"], "0")
        self.assertEqual(fields["captiveRealDiapauseDays"], "4")
        self.assertEqual(fields["captiveReproductivePauseDays"], "0")
        self.assertEqual(fields["captiveEggFormationDays"], "1")
        row["Captive gestation (game days)"] = -1
        with self.assertRaises(ValueError):
            seasonal_fields(row)

    def test_patch_and_def_paths_have_identical_profiles(self):
        row = self.row("Spring", 28.767123287671232, 0)
        row["Captive gestation (game days)"] = 6.5
        row["Birth / laying season"] = "Winter"
        row["WinterDormancy"] = "False"
        row["Maternity denning season"] = "Autumn, Winter"
        row["Dormancy threshold (°C)"] = 1.03
        expected = build_mod_extensions_for_child(row, {}, True)
        winter = expected.xpath("./li[contains(@Class, 'ModExtension_WinterDormancy')]")[0]
        self.assertEqual(winter.findtext("ordinaryDormancy"), "false")
        self.assertEqual(winter.findtext("maternitySeasonMask"), "12")
        root = ET.fromstring("<Defs><ThingDef><defName>TestAnimal</defName><modExtensions><li Class='Other.Custom, Other'/></modExtensions></ThingDef></Defs>")
        generator = PatchGenerator.__new__(PatchGenerator)
        applier = PatchApplier(record_missing=True)
        for _ in range(2):
            patch = ET.Element("Patch")
            patch.extend(generator.create_concrete_mod_extension_patches("TestAnimal", row))
            applier.apply_patch_root(patch, root)
        actual = root.find("./ThingDef/modExtensions")
        for item in expected:
            matches = [x for x in actual if x.get("Class") == item.get("Class")]
            self.assertEqual(len(matches), 1)
            self.assertEqual({c.tag: c.text for c in matches[0]},
                             {c.tag: c.text for c in item})
        self.assertEqual(sum(x.get("Class") == "Other.Custom, Other" for x in actual), 1)
        row = self.row("All")
        row["WinterDormancy"] = "False"
        patch = ET.Element("Patch")
        patch.extend(generator.create_concrete_mod_extension_patches("TestAnimal", row))
        applier.apply_patch_root(patch, root)
        self.assertEqual([x.get("Class") for x in actual], ["Other.Custom, Other"])

    def test_invalid_values(self):
        for value in ("Fall", "Spring, Spring", "All, Winter", "#REF!"):
            with self.assertRaises(ValueError):
                season_mask(value)
        for value in ("-1", "Infinity", "#ERROR!", "99999999999"):
            with self.assertRaises(ValueError):
                duration_ticks(value)
        with self.assertRaises(ValueError):
            seasonal_fields({COLUMNS[0]: "Spring"})

    def test_concrete_extension_not_inherited(self):
        row = self.row("Spring", 4)
        parent = dict(row)
        self.assertIsNone(build_mod_extensions_for_parent(parent))
        for include_all in (False, True):
            result = build_mod_extensions_for_child(row, parent, include_all)
            item = result.find("li")
            self.assertEqual(item.get("Class"), "ZoologyMod.ModExtension_SeasonalReproduction, ZoologyMod")
            self.assertEqual(item.findtext("matingSeasonMask"), "1")
            self.assertEqual(item.findtext("embryonicDiapauseTicks"), "240000")
        self.assertIsNone(build_mod_extensions_for_child(self.row("All"), {}, True))


class DiapauseRuntimeContracts(unittest.TestCase):
    ROOT = Path(__file__).resolve().parents[2] / "Zoology"

    def test_active_diapause_uses_current_duration_and_cannot_restart(self):
        code = (self.ROOT / "Source/Behaviour/Reproduction/AnimalSeasonalReproduction.cs").read_text(encoding="utf-8")
        begin = code.split("internal static void BeginDiapause(", 1)[1].split("private static int Deadline(", 1)[0]
        self.assertIn("state.embryonicDiapauseDuration = delay;", begin)
        self.assertIn("state.diapausePregnancyId = pregnancyId(pregnancy);", begin)
        rescale = code.split("internal static int RescaleDiapauseRemaining(", 1)[1].split(
            "internal static int DiapauseUntil(", 1)[0]
        self.assertIn("if (remaining <= 0 || duration <= 0) return 0;", rescale)
        self.assertIn("(long)Math.Min(remaining, previousDuration) * duration", rescale)
        self.assertIn("(scaled + previousDuration / 2L) / previousDuration", rescale)
        until = code.split("internal static int DiapauseUntil(", 1)[1].split(
            "internal static bool InDiapause(", 1)[0]
        self.assertIn("state.diapausePregnancyId != pregnancyId(pregnancy)", until)
        self.assertLess(until.index("if (remaining <= 0) return state.embryonicDiapauseUntil;"),
                        until.index("int duration = DiapauseTicks(pawn, profile);"))
        self.assertIn("RescaleDiapauseRemaining(remaining, previousDuration, duration)", until)
        self.assertIn("state.embryonicDiapauseUntil = Deadline(adjusted);", until)
        self.assertIn("state.embryonicDiapauseDuration = duration;", until)

    def test_diapause_timer_persists_and_drives_progress_and_hunger(self):
        state = (self.ROOT / "Source/Behaviour/Predation/AnimalStrategies.cs").read_text(encoding="utf-8")
        for field in ("embryonicDiapauseUntil", "embryonicDiapauseDuration", "diapausePregnancyId"):
            self.assertIn(f'Scribe_Values.Look(ref {field}, "{field}"', state)
        seasonal = (self.ROOT / "Source/Behaviour/Reproduction/AnimalSeasonalReproduction.cs").read_text(encoding="utf-8")
        self.assertIn("int until = DiapauseUntil(pregnancy);", seasonal)
        self.assertIn("DiapauseUntil(pregnancy) > GenTicks.TicksGame;", seasonal)
        energy = (self.ROOT / "Source/Behaviour/Reproduction/ZoologyReproductiveEnergetics.cs").read_text(encoding="utf-8")
        self.assertIn("if (AnimalSeasonalReproduction.InDiapause(hediff)) return 0f;", energy)
        self.assertIn("AnimalSeasonalReproduction.DiapauseUntil(hediff) - GenTicks.TicksGame", energy)
        self.assertIn('"Zoology_EmbryonicDiapause".Translate(days)', energy)
        self.assertIn("int days = 1 + (remaining - 1) / GenDate.TicksPerDay;", energy)


class EggSeasonInspectContract(unittest.TestCase):
    def test_season_caption_uses_native_inspect_hook_only(self):
        code = (DiapauseRuntimeContracts.ROOT / "Source/Behaviour/Reproduction/AnimalSeasonalReproduction.cs").read_text(encoding="utf-8")
        inspect = code.split("internal static class Patch_SeasonalEggInspectString", 1)[1].split(
            "internal static class Patch_SeasonalEggLaid", 1)[0]
        self.assertIn('"CompInspectStringExtra"', inspect)
        self.assertIn("EggSeasonPaused(__instance)", inspect)
        self.assertIn('"Zoology_EggSeasonPaused".Translate()', inspect)
        reason = code.split("internal static bool EggSeasonPaused(", 1)[1].split(
            "internal static bool AllowsLaying(", 1)[0]
        self.assertIn("Managed(pawn)", reason)
        self.assertIn("progress <= 0f", reason)
        self.assertIn("progress >= 1f", reason)
        self.assertIn("CanStartEggFormationInSeason", reason)
        self.assertIn("CanLayInSeason", reason)


if __name__ == "__main__":
    unittest.main()
