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
        self.assertIn('modifier.stat?.defName == "MeleeCooldownFactor"', code)

    def test_royal_five_and_spectrobionta_six_source_identities(self):
        self.assertEqual(len(ROYAL_STAGES), 5)
        self.assertEqual(len(SPECTROBIONTA_STAGES), 6)
        self.assertEqual(len({s[0] for s in (*ROYAL_STAGES,*SPECTROBIONTA_STAGES)}), 11)

if __name__ == "__main__":
    unittest.main()
