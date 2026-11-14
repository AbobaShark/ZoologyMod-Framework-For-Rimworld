"""Static regressions for reachable tree nests and egg-laying fallback."""
from pathlib import Path
import unittest
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
NURSERY = ROOT / "Zoology/Source/Behaviour/Childcare/AnimalNursery.cs"
CANOPY = ROOT / "Zoology/Source/Behaviour/Flee/AnimalTreeCanopy.cs"
EGGS = ROOT / "Zoology/Source/Behaviour/Childcare/CompAnimalNestEggContainer.cs"
JOBS = ROOT / "Zoology/Core/Defs/JobDefs/AnimalNurseryJobs.xml"


class TreeNestLayingContracts(unittest.TestCase):
    def test_tree_site_is_reached_by_touch_not_standable(self):
        src = CANOPY.read_text(encoding="utf-8")
        start = src.index("internal bool NurserySiteAvailable(")
        end = src.index("internal bool TryClaimNurserySite(", start)
        body = src[start:end]
        self.assertIn("Plant tree = null", body)
        self.assertIn("tree.Position != cell", body)
        self.assertIn("builder.CanReach(tree, PathEndMode.Touch, Danger.Some)", body)
        self.assertIn("else if (!cell.Standable(map))", body)
        self.assertNotIn("!cell.Standable(map)\n                ||", body)
        self.assertIn("NurserySiteAvailable(cell, builder, tree)", src)

        nursery = NURSERY.read_text(encoding="utf-8")
        self.assertIn("NurserySiteAvailable(tree.Position, pawn, tree)", nursery)
        self.assertIn("result = tree.Position;", nursery)
        self.assertIn("Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)", nursery)
        self.assertIn("Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell)", nursery)

    def test_tree_nest_egg_job_uses_touch(self):
        src = NURSERY.read_text(encoding="utf-8")
        egg_job = src.split("public sealed class JobDriver_LayEggInTreeNest", 1)[1].split(
            "[HarmonyPatch(typeof(Thing)", 1)[0]
        self.assertIn("Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)", egg_job)
        self.assertIn("layer.ProduceEgg()", egg_job)
        self.assertIn("container?.Accepts(egg.def) == true", egg_job)
        self.assertIn("container.innerContainer.TryAdd(egg)", egg_job)
        self.assertIn("GenPlace.TryPlaceThing(egg, pawn.Position, pawn.Map", egg_job)
        self.assertLess(egg_job.index("layer.ProduceEgg()"),
                        egg_job.index("container?.Accepts(egg.def)"))

        xml = ET.parse(JOBS).getroot()
        jobs = {job.findtext("defName"): job for job in xml.findall("JobDef")}
        driver = jobs["Zoology_LayEggInTreeNest"].findtext("driverClass")
        self.assertEqual(driver, "ZoologyMod.JobDriver_LayEggInTreeNest, ZoologyMod")

    def test_egg_laying_falls_back_to_vanilla_without_nest(self):
        src = NURSERY.read_text(encoding="utf-8")
        lay = src.split("internal static class Patch_LayEgg_UseAnimalNursery", 1)[1].split(
            "[HarmonyPatch(typeof(Thing)", 1)[0]
        self.assertIn("nursery.TreeNest ? PathEndMode.Touch : PathEndMode.OnCell", lay)
        self.assertIn('"Zoology_LayEggInTreeNest"', lay)
        self.assertIn("return true;", lay[-420:])
        self.assertNotIn("Never redirect species with obligatory nests", lay)
        self.assertIn("AnimalNestEggUtility.HasUsableEggBox(pawn)", lay)

    def test_nest_build_does_not_require_fertilization(self):
        src = NURSERY.read_text(encoding="utf-8")
        ready = src.split("internal static bool ReadyToPrepareNest(", 1)[1].split(
            "internal static bool HasUsableEggBox(", 1)[0]
        self.assertIn("layer.CanLayNow", ready)
        self.assertNotIn("IsFertilizedEgg(", ready)
        self.assertIn("pawn.gender != Gender.Female", ready)
        eggs = EGGS.read_text(encoding="utf-8")
        self.assertIn("eggDef.GetCompProperties<CompProperties_Hatcher>() != null", eggs)
        self.assertIn("IsFertilizedEgg(thingDef) && base.Accepts(thingDef)", eggs)
        self.assertIn("CompAnimalNestEggContainer.IsFertilizedEgg(", src)
        self.assertIn("nursery?.CanReceiveEgg(pawn) == true", src)


if __name__ == "__main__":
    unittest.main()
