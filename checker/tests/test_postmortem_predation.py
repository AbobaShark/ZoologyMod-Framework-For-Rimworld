"""Preserve the observed conditions of dead victims."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).resolve().parents[2] / "Zoology/Source/Behaviour/Predation"


class PostmortemPredationTests(unittest.TestCase):
    def test_constriction_is_retained_during_and_after_death(self):
        source = (SOURCE / "AnimalConstriction.cs").read_text(encoding="utf-8")
        self.assertIn("PreserveOnDeath => deathInProgress || pawn?.Dead == true", source)
        self.assertIn("ShouldRemove => !PreserveOnDeath && base.ShouldRemove", source)
        self.assertIn("if (!PreserveOnDeath && !Active) pawn.health.RemoveHediff(this);", source)
        release = source.split("public static void Release(", 1)[1].split("public static bool Applicable(", 1)[0]
        self.assertIn("state.constrictedPrey = null", release)
        self.assertIn("effect != null && !effect.PreserveOnDeath", release)
        death = source.split("internal static class Patch_ReleaseConstrictionOnDeath", 1)[1].split(
            "[HarmonyPatch", 1)[0]
        self.assertIn("HarmonyPriority(Priority.First)", death)
        self.assertLess(death.index("__state.deathInProgress = true"), death.index("AnimalConstriction.Release"))
        self.assertIn("Finalizer", death)
        self.assertIn("__state.deathInProgress = false", death)
        self.assertNotIn("Severity =", death)

    def test_venom_does_not_recover_after_death(self):
        source = (SOURCE / "AnimalVenom.cs").read_text(encoding="utf-8")
        venom = source.split("public class Hediff_AnimalVenom", 1)[1].split("[HarmonyPatch", 1)[0]
        self.assertIn("ShouldRemove => pawn?.Dead != true && base.ShouldRemove", venom)
        self.assertIn("if (pawn?.Dead != true) base.PostTick();", venom)
        self.assertNotIn("Severity =", venom)


if __name__ == "__main__":
    unittest.main()
