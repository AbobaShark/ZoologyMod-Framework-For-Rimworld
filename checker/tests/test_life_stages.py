"""Life-stage topology and removal of historical general-purpose scaling."""
import json
from pathlib import Path
import unittest
from core.life_stages import STAGE_GROUPS, ROYAL_STAGES, SPECTROBIONTA_STAGES

ROOT = Path(__file__).resolve().parents[2]

class StageTopologyTests(unittest.TestCase):
    def test_core_has_exactly_three_semantic_stages(self):
        self.assertEqual(len(STAGE_GROUPS), 3)
        self.assertEqual([x[0] for x in STAGE_GROUPS],
                         ["AnimalAdult", "AnimalJuvenile", "AnimalBaby"])
        self.assertNotIn("AnimalBabyTiny", repr(STAGE_GROUPS))

    def test_no_static_life_stage_outputs_registered(self):
        registry = json.loads((ROOT/"checker/generation.json").read_text("utf-8"))
        outputs = {e["id"] for e in registry["outputs"] if e["pipeline_category"] == "life-stages"}
        self.assertEqual(outputs, {"life-stages.species", "life-stages.attacks.cs"})
        self.assertNotIn("baby_tiny_defs", registry)

    def test_runtime_resolves_original_tiny_to_baby(self):
        code = (ROOT/"Zoology/Source/Behaviour/Shared/ZoologyLifeStageProfileInstaller.cs").read_text("utf-8")
        self.assertIn('profile.sourceStageDefName == "AnimalBaby" && source.defName == "AnimalBabyTiny"', code)
        self.assertNotIn("profile.expectedBodySizeFactor", code)
        self.assertIn("IsAgePhysicalStatFactor(modifier.stat)", code)
        self.assertIn("bool changed = false;", code)
        self.assertIn('case "MeleeCooldownFactor":', code)
        self.assertIn('case "Zoology_MeleeCooldownFactor":', code)
        self.assertIn('case "MoveSpeed":', code)
        self.assertIn('case "MeleeCritChance":', code)
        self.assertIn('case "MeleeParryChance":', code)

    def test_obsolete_universal_physical_age_patches_are_deleted(self):
        retired = (
            "Core/Patches/Core/LifeStageDefs/LifeStages.xml",
            "Core/Patches/Core/LifeStageDefs/CombatPowerFactors.xml",
            "ModPatches/CombatExtended/Patches/LifeStageDefs/LifeStages_CE.xml",
            "ModPatches/VanillaAnimalsExpandedRoyal/Patches/LifeStageDefs/LifeStages_VAER.xml",
            "ModPatches/VanillaAnimalsExpandedRoyal/Patches/LifeStageDefs/CombatPowerFactors_VAER.xml",
            "ModPatches/CombatExtended/Patches/LifeStageDefs/LifeStages_VAER_CE.xml",
        )
        for path in retired:
            with self.subTest(path=path):
                self.assertFalse((ROOT / "Zoology" / path).exists())
        self.assertTrue((ROOT / "Zoology/Core/Patches/Core/LifeStageDefs/LifeStages_Insect.xml").is_file())

    def test_royal_five_and_spectrobionta_seven_source_identities(self):
        self.assertEqual(len(ROYAL_STAGES), 5)
        self.assertEqual(len(SPECTROBIONTA_STAGES), 7)
        self.assertEqual(len({s[0] for s in (*ROYAL_STAGES, *SPECTROBIONTA_STAGES)}), 12)
        self.assertIn(("SpectrobiontaPredimagoMedium",), SPECTROBIONTA_STAGES)

    def test_runtime_preserves_source_life_stage_flags_and_age_metadata(self):
        source = (ROOT / "Zoology/Source/Behaviour/Shared/ZoologyLifeStageProfileInstaller.cs").read_text("utf-8")
        self.assertIn("var replacement = ShallowClone(original);", source)
        self.assertIn("var copiedAge = ShallowClone(ages[stageIndex]);", source)
        self.assertIn("copiedAge.def = replacement;", source)
        self.assertIn("animal.race.lifeStageAges = copiedAges;", source)
        self.assertIn("new ZoologyLifeStageOrigin { sourceStage = original }", source)
        # Lifecycle, reproductive and voice fields are inherited unchanged.
        for flag in ("reproductive", "visible", "milkable", "shearable",
                     "caravanRideable", "voxPitch", "voxVolume"):
            with self.subTest(flag=flag):
                self.assertNotIn("replacement." + flag + " =", source)

    def test_eusocial_and_tiny_stage_matching_against_actual_race(self):
        source = (ROOT / "Zoology/Source/Behaviour/Shared/ZoologyLifeStageProfileInstaller.cs").read_text("utf-8")
        self.assertIn("source.defName, profile.sourceStageDefName", source)
        self.assertIn('profile.sourceStageDefName == "AnimalBaby" && source.defName == "AnimalBabyTiny"', source)
        self.assertIn("var ages = animal.race.lifeStageAges;", source)
        self.assertIn("return false; // Do not manufacture stages the race does not use.", source)

if __name__ == "__main__":
    unittest.main()
