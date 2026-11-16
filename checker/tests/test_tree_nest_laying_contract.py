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
            "public sealed class JobDriver_LayEggInTreeNest", 1)[0]
        self.assertIn("nest.TreeNest ? PathEndMode.Touch : PathEndMode.OnCell", src)
        self.assertIn("AnimalNestEggUtility.FindReusableNest(pawn, mode)", lay)
        self.assertIn('"Zoology_LayEggInTreeNest"', lay)
        import re
        code = re.sub(r"//[^\n]*", "", lay)
        self.assertRegex(code, r"return false;\s*\}\s*return true;\s*\}\s*\}\s*$")
        self.assertNotIn("Never redirect species with obligatory nests", lay)
        self.assertIn("AnimalNestEggUtility.HasUsableEggBox(pawn)", lay)

    def test_nest_build_does_not_require_fertilization(self):
        src = NURSERY.read_text(encoding="utf-8")
        ready = src.split("internal static bool ReadyToPrepareNest(", 1)[1].split(
            "internal static bool HasUsableEggBox(", 1)[0]
        self.assertIn("layer.CanLayNow", ready)
        self.assertNotIn("IsFertilizedEgg(", ready)
        self.assertIn("pawn.gender != Gender.Female", ready)

    def test_nests_use_standard_egg_acceptance_for_both_egg_types(self):
        eggs = EGGS.read_text(encoding="utf-8")
        self.assertIn("class CompAnimalNestEggContainer : CompEggContainer", eggs)
        self.assertNotIn("override bool Accepts(", eggs)
        self.assertNotIn("IsFertilizedEgg(", eggs)

        src = NURSERY.read_text(encoding="utf-8")
        receive = src.split("public bool CanReceiveEgg(", 1)[1].split(
            "public bool TryReceiveBaby(", 1)[0]
        self.assertIn("Nest && Spawned && OwnedBy(layer)", receive)
        self.assertIn("eggs.Accepts(eggLayer.NextEggType())", receive)
        self.assertNotIn("IsFertilizedEgg(", receive)
        self.assertNotIn("CompHatcher", receive)

        lay = src.split("internal static class Patch_LayEgg_UseAnimalNursery", 1)[1].split(
            "public sealed class JobDriver_LayEggInTreeNest", 1)[0]
        self.assertNotIn("IsFertilizedEgg(", lay)
        self.assertNotIn("NextEggType()", lay)
        self.assertIn("AnimalNestEggUtility.FindReusableNest(pawn, mode)", lay)
        self.assertIn("nursery.ClaimFor(pawn);", lay)
        self.assertIn(": JobDefOf.LayEgg;", lay)
        self.assertIn("__result = JobMaker.MakeJob(layDef, nursery);", lay)

        nests = ET.parse(ROOT / "Zoology/Core/Defs/ThingDefs/AnimalNursery.xml").getroot()
        nodes = {node.get("Name"): node for node in nests.findall("ThingDef") if node.get("Name")}
        def inherited(node, path):
            value = node.findtext(path)
            if value is not None:
                return value
            parent = nodes.get(node.get("ParentName"))
            return inherited(parent, path) if parent is not None else None
        for nest in nests.findall("ThingDef"):
            if nest.find("defName") is not None:
                self.assertEqual(inherited(nest, "comps/li/compClass"),
                                 "ZoologyMod.CompAnimalNestEggContainer, ZoologyMod")

    def test_large_ground_nest_reserves_entire_footprint(self):
        src = NURSERY.read_text(encoding="utf-8")
        canopy = CANOPY.read_text(encoding="utf-8")
        self.assertIn("builder?.RaceProps?.baseBodySize >= LargeGroundNestBodySize", src)
        self.assertIn("NurseryDef(mode, pawn)", src)
        self.assertIn("GroundNurserySiteAvailable(cell, pawn)", src)
        self.assertIn("foreach (IntVec3 part in AnimalRefugeMapComponent.NurseryFootprint(", src)
        self.assertIn("pawn.Reserve(new LocalTargetInfo(part)", src)
        self.assertIn("GenAdj.OccupiedRect(cell, Rot4.North,", canopy)
        self.assertIn("foreach (IntVec3 part in footprint.Cells)", canopy)
        self.assertIn("foreach (IntVec3 part in claim.footprint.Cells)", canopy)
        self.assertIn("IsNestBuildJob(p, claim.tree, claim.anchor)", canopy)
        defs = ET.parse(ROOT / "Zoology/Core/Defs/ThingDefs/AnimalNursery.xml").getroot()
        nests = {node.findtext("defName"): node for node in defs.findall("ThingDef")
                 if node.find("defName") is not None}
        small = nests["Zoology_AnimalGroundNest"]
        large = nests["Zoology_AnimalGroundNestLarge"]
        self.assertEqual(small.attrib["ParentName"], large.attrib["ParentName"])
        self.assertEqual(small.findtext("size"), "(1,1)")
        self.assertEqual(large.findtext("size"), "(2,2)")


if __name__ == "__main__":
    unittest.main()
