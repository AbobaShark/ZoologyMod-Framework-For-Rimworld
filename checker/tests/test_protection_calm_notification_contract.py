"""Regression contracts: protection interruption is not an animal calming down."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "Zoology/Source/Behaviour"
NOTIFY = ROOT / "Shared/ZoologyNotificationUtility.cs"
PREY = ROOT / "Predation/PreyProtection/JobDriver_ProtectPrey.cs"
YOUNG = ROOT / "Childcare/JobDriver_ProtectYoung.cs"


class ProtectionCalmNotificationContracts(unittest.TestCase):
    def test_notification_requires_successful_retreat_and_healthy_actor(self):
        src = NOTIFY.read_text(encoding="utf-8")
        method = src.split("public static void TryNotifyProtectionEnded(", 1)[1].split(
            "private static long MakePairKey", 1)[0]
        self.assertIn("JobCondition condition, bool threatRetreated", method)
        self.assertIn("condition != JobCondition.Succeeded || !threatRetreated", method)
        for check in (
            "protector?.Spawned != true", "protector.Dead",
            "protector.Downed", "protector.Destroyed",
            "protector.InMentalState",
            "aggressor?.Faction != Faction.OfPlayer",
        ):
            self.assertIn(check, method)
        self.assertLess(method.index("condition != JobCondition.Succeeded"),
                        method.index("lastCalmNotificationTickByPairKey[pairKey]"))
        self.assertLess(method.index("protector.InMentalState"),
                        method.index("Messages.Message("))

    def test_both_drivers_report_actual_condition(self):
        for path in (PREY, YOUNG):
            with self.subTest(path=path.name):
                src = path.read_text(encoding="utf-8")
                self.assertIn("private bool threatRetreated;", src)
                self.assertIn("TryNotifyProtectionEnded(", src)
                self.assertIn("condition, threatRetreated", src)
                self.assertIn("JobCondition.Succeeded", src)

    def test_prey_retreat_is_only_normal_exit_with_message(self):
        src = PREY.read_text(encoding="utf-8")
        self.assertEqual(src.count("threatRetreated = true;"), 1)
        retreat = re.search(
            r"if \(!PreyProtectionUtility\.IsPawnWithinProtectionRange"
            r"\(targ, anchorMap, anchorPos, MAX_DISTANCE_SQ\)\)"
            r"\s*\{\s*threatRetreated = true;"
            r"\s*actorPawn\?\.jobs\?\.EndCurrentJob\(JobCondition\.Succeeded",
            src,
        )
        self.assertIsNotNone(retreat)
        self.assertIn("cond, threatRetreated", src)

    def test_young_retreat_requires_no_remaining_threat(self):
        src = YOUNG.read_text(encoding="utf-8")
        self.assertEqual(src.count("threatRetreated = true;"), 1)
        retreat = re.search(
            r"if \(!PreyProtectionUtility\.IsPawnWithinProtectionRange"
            r"\(target, anchorMap, anchorPosition, MaxDistanceSq\)\)"
            r"[\s\S]*?"
            r"if \(ChildcareDefenseUtility\.TryRetargetProtectYoungJobToNearestThreat"
            r"\(actorPawn\)\)[\s\S]*?"
            r"threatRetreated = true;"
            r"\s*actorPawn\?\.jobs\?\.EndCurrentJob\(JobCondition\.Succeeded",
            src,
        )
        self.assertIsNotNone(retreat)
        self.assertIn("AddFinishAction(delegate (JobCondition condition)", src)


if __name__ == "__main__":
    unittest.main()
