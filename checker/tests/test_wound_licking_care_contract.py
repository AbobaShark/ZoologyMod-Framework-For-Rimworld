"""Contracts for wound licking and medical care."""
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
BEHAVIOUR = ROOT / "Zoology/Source/Behaviour"


class WoundLickingCareContracts(unittest.TestCase):
    def setUp(self):
        self.utility = (BEHAVIOUR / "Healing/AnimalWoundLickingUtility.cs").read_text(encoding="utf-8")
        self.giver = (BEHAVIOUR / "Healing/JobGiver_AnimalLickWounds.cs").read_text(encoding="utf-8")
        self.driver = (BEHAVIOUR / "Healing/JobDriver_AnimalLickWounds.cs").read_text(encoding="utf-8")
        self.refuges = (BEHAVIOUR / "Flee/AnimalRefuges.cs").read_text(encoding="utf-8")

    def test_exact_medical_care_policy(self):
        self.assertRegex(
            self.utility,
            r"return pawn\.Faction\?\.IsPlayer != true\s*\|\| pawn\.playerSettings\?\.medCare == MedicalCareCategory\.NoCare;")
        self.assertIn("!settings.DisableAllRuntimePatches && settings.EnableAnimalWoundLicking", self.utility)

    def test_think_tree_reaches_job_giver_for_all_factions(self):
        root = ET.parse(ROOT / "Zoology/Core/Defs/ThinkTreeDef/Insert_AnimalTending_ThinkTree_AnimalPreMain.xml").getroot()
        self.assertIsNotNone(root.find(".//thinkRoot/subNodes/li[@Class='ZoologyMod.JobGiver_AnimalLickWounds, ZoologyMod']"))
        self.assertIsNone(root.find(".//*[@Class='ThinkNode_ConditionalOfPlayerFaction']"))

    def test_ground_treatment_checks_care_at_start_and_completion(self):
        self.assertIn("AnimalWoundLickingUtility.CanUseWoundLicking(pawn)", self.giver)
        self.assertIn("this.FailOn(() => !HasValidPatient || !AnimalWoundLickingUtility.CanUseWoundLicking(pawn))", self.driver)
        self.assertIn("AnimalWoundLickingUtility.TryApplyWoundLicking(pawn)", self.driver)
        self.assertIn("if (!CanUseWoundLicking(pawn) || pawn.health?.hediffSet == null)", self.utility)
        self.assertIn("if (TryCollectLickableHediffs(pawn, tmpCandidateHediffs) <= 0)", self.utility)

    def test_tree_treatment_uses_same_policy(self):
        start = self.refuges.split("private void TryStartWoundLicking()", 1)[1]
        start, tick = start.split("private void TickWoundLicking()", 1)
        tick = tick.split("public override void Notify_DamageTaken", 1)[0]
        self.assertIn("AnimalWoundLickingUtility.HasLickableWounds(pawn)", start)
        self.assertIn("if (!AnimalWoundLickingUtility.CanUseWoundLicking(pawn))", tick)
        self.assertIn("treeWoundLickFinishTick = 0;", tick)
        self.assertIn("AnimalWoundLickingUtility.TryApplyWoundLicking(pawn)", tick)

    def test_only_treatable_external_bleeding_wounds_are_selected(self):
        self.assertIn("!hediff.Bleeding || !hediff.TendableNow()", self.utility)
        self.assertIn("IsSurfaceBodyPart(hediff.Part)", self.utility)
        self.assertIn("current.depth == BodyPartDepth.Inside", self.utility)


if __name__ == "__main__":
    unittest.main()
