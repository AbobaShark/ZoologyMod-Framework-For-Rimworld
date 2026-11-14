"""Protection notifications are emitted once per newly started defense."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "Zoology/Source/Behaviour"
CHILD = ROOT / "Childcare/ChildcareDefenseUtility.cs"
NOTIFY = ROOT / "Shared/ZoologyNotificationUtility.cs"
NEST = ROOT / "Childcare/AnimalNursery.cs"
BURROW = ROOT / "Flee/AnimalBurrow.cs"


class ProtectionStartContracts(unittest.TestCase):
    def test_shared_dispatch_is_gated_by_ongoing_defense_and_success(self):
        src = NOTIFY.read_text(encoding="utf-8")
        notify = src.split("public static void TryNotifyProtectionStarted(", 1)[1].split(
            "public static void TryNotifyProtectionEnded(", 1)[0]
        self.assertIn("if (alreadyActive || aggressor?.Faction != Faction.OfPlayer", notify)
        self.assertIn("protectors.Count == 0", notify)
        self.assertEqual(notify.count("ReceiveLetter("), 1)
        self.assertEqual(notify.count("Messages.Message("), 1)
        self.assertIn("bool pack = protectors.Count > 1;", notify)

    def test_childcare_checks_live_jobs_before_starting_and_reporting(self):
        src = CHILD.read_text(encoding="utf-8")
        begin = src.split("private static bool TryBeginProtection(", 1)[1].split(
            "internal static bool TryRetargetProtectYoungJobToNearestThreat(", 1)[0]
        self.assertLess(begin.index("alreadyActive = true;"), begin.index("TryTakeProtectionJob("))
        self.assertIn("if (triggeredProtectorsScratch.Count == 0)", begin)
        self.assertIn("triggeredProtectorsScratch, alreadyActive, eggClutch", begin)
        self.assertEqual(begin.count("TryNotifyProtectionStarted("), 1)
        take = src.split("private static bool TryTakeProtectionJob(", 1)[1].split(
            "private static void TryForceAggressorFleeFromProtectYoung(", 1)[0]
        self.assertLess(take.index("IsActiveProtection("), take.index("StartJob("))
        self.assertLess(take.index("StartJob("), take.index("ReferenceEquals(protector.CurJob, job)"))
        self.assertIn("JobMaker.MakeJob(jobDef ?? JobDefOf.AttackMelee, aggressor, protectedThing)", take)

    def test_active_job_collection_is_not_used_by_food_scoring(self):
        src = CHILD.read_text(encoding="utf-8")
        self.assertIn("bool includeActiveDefense = false", src)
        self.assertIn("TryFillEggProtectors(egg, eater, eggProtectorsScratch)", src)
        self.assertIn("TryFillEggProtectors(protectedThing, aggressor, candidates, includeActiveDefense: true)", src)
        self.assertIn("List<Pawn> candidates = youngProtectorsScratch;", src)

    def test_nest_parents_are_processed_together_and_burrow_fallback_is_shared(self):
        nest = NEST.read_text(encoding="utf-8")
        attack = nest.split("public void NotifyAttacked(", 1)[1].split(
            "internal static class AnimalNestEggUtility", 1)[0]
        self.assertEqual(attack.count("TryOrderProtection("), 1)
        self.assertIn("first, attacker, protectedThing, second", attack)
        burrow = BURROW.read_text(encoding="utf-8")
        attack = burrow.split("public void NotifyAttacked(", 1)[1].split(
            "private bool SafeToJoin(", 1)[0]
        self.assertIn("allowBurrowFallback: true", attack)
        self.assertNotIn("StartJob(", attack)


if __name__ == "__main__":
    unittest.main()
