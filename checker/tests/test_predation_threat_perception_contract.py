"""Predation risk, hunt eligibility and active stalking are independent contracts."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "Zoology/Source/Behaviour/Predation"
FLEE = ROOT / "Patch_AnimalFleeFromDanger.cs"
HUNT = ROOT / "Patch_IsAcceptablePreyForPredator.cs"
CACHE = ROOT / "PredationDecisionCache.cs"
STRATEGY = ROOT / "AnimalStrategies.cs"


def section(src, start, end):
    return src.split(start, 1)[1].split(end, 1)[0]


class PredationThreatContracts(unittest.TestCase):
    def test_hunt_blocks_do_not_write_perception_cache(self):
        hunt = HUNT.read_text(encoding="utf-8")
        blocked = section(hunt, "internal static void CacheBlockedPair(", "private static void EnsurePackSupport(")
        self.assertIn("PredationDecisionCache.StoreAcceptablePrey(predator, prey, false)", blocked)
        self.assertNotIn("PotentialPreyThreat", blocked)
        self.assertIn("comp.IsPairBlockedNow(predator, prey)", hunt)
        cache = CACHE.read_text(encoding="utf-8")
        self.assertIn("StorePreyDecision(potentialPreyThreatHotCacheSlots, predator, prey, value)", cache)
        self.assertIn("TryGetPreyDecision(potentialPreyThreatHotCacheSlots, predator, prey, out value)", cache)
        self.assertEqual(cache.count("System.Array.Clear(potentialPreyThreatHotCacheSlots"), 2)

    def test_flee_and_refuges_share_physical_risk(self):
        src = FLEE.read_text(encoding="utf-8")
        risk = section(src, "private static bool IsAcceptablePreyForFlee(", "private static bool IsFleeJobThreatMeleeAttackingPawn(")
        self.assertIn("IsSuitableWithoutYoungProtection(predator, prey)", risk)
        self.assertIn("TryGetPotentialPreyThreat(", risk)
        self.assertIn("StorePotentialPreyThreat(", risk)
        self.assertNotIn("TryGetAcceptablePrey(", risk)
        self.assertNotIn("IsPairBlockedNow(", risk)
        self.assertNotIn("ShouldBlockProtectedYoungPredation(", risk)
        refuge = section(src, "private static bool IsRelevantRefugeThreat(", "private static bool TryHandleImmediateProtectPreyThreat(")
        self.assertIn("IsAcceptablePreyForFlee(threat, prey)", refuge)

    def test_land_concealment_requires_current_stalk_and_target(self):
        src = STRATEGY.read_text(encoding="utf-8")
        hidden = section(src, "public static bool HiddenFrom(", "public static bool IsFleeing(")
        self.assertIn("strategy == PredationStrategy.WaterAmbush", hidden)
        self.assertIn("state.ambushPhase == 1", hidden)
        self.assertIn("predator.jobs?.curDriver is JobDriver_AnimalStalk", hidden)
        self.assertIn("predator.CurJob?.targetA.Pawn == state.ambushPrey", hidden)
        self.assertNotIn("state.ambushPhase == 2", hidden)
        self.assertNotIn("suppressReaction", src)
        self.assertIn("state.ambushPhase = 2;\n                    state.revealed = true;", src)

    def test_independent_detection_does_not_use_hunt_cooldown(self):
        src = STRATEGY.read_text(encoding="utf-8")
        detect = section(src, "internal static bool TryDetectStalkingPredator(", "public static bool HiddenFrom(")
        self.assertNotIn("IsPairBlockedNow(", detect)
        src = FLEE.read_text(encoding="utf-8")
        witness = section(src, "private static void TryFleeDetectedStalker(", "internal static void AlertDetectedAmbush(")
        self.assertNotIn("IsPairBlockedNow(", witness)
        self.assertIn("BlockPursuit(predator, observer)", witness)


if __name__ == "__main__":
    unittest.main()
