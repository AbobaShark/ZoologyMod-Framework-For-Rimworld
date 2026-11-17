"""Contracts for incubating natural nests and fertilization-dependent duck laying."""
from pathlib import Path
import unittest
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
CHILDCARE = ROOT / "Zoology/Source/Behaviour/Childcare"


class NestIncubationContracts(unittest.TestCase):
    def test_nest_search_uses_parent_reference_before_ground_search(self):
        source = (CHILDCARE / "ChildcareDefenseUtility.cs").read_text(encoding="utf-8")
        search = source.split("public static Thing TryFindEggIncubationTarget(", 1)[1].split(
            "internal static Thing GetIncubationEgg(", 1)[0]
        self.assertIn("mother.TryGetComp<CompAnimalStrategyState>()?.nursery", search)
        self.assertLess(search.index("return nest;"), search.index("GenClosest.ClosestThingReachable("))
        self.assertIn("mother.CanReach(nest, PathEndMode.Touch, Danger.Some)", search)
        self.assertIn("EggIncubationSearchFailCooldownTicks", search)

    def test_fertile_clutch_and_repair_threshold(self):
        source = (CHILDCARE / "ChildcareDefenseUtility.cs").read_text(encoding="utf-8")
        target = source.split("private static bool IsEggIncubationTargetForMother(", 1)[1].split(
            "private static bool IsEggCompatibleWithMother(", 1)[0]
        self.assertIn("if (!IsFertilizedEgg(egg))", target)
        self.assertIn("nest.Mother != mother", target)
        self.assertIn("!NeedsEggIncubation(egg) && !NeedsEggIncubation(nest)", target)
        self.assertIn("thing.HitPoints * 5 < thing.MaxHitPoints * 4", target)
        self.assertIn("egg.HitPoints = egg.MaxHitPoints;", source)
        self.assertIn("nest.HitPoints = nest.MaxHitPoints;", source)
        self.assertIn("ReferenceEquals(GetIncubationEgg(nest), egg)", source)

    def test_job_reserves_spawned_target_and_tracks_real_egg(self):
        giver = (CHILDCARE / "JobGiver_IncubateEggClutch.cs").read_text(encoding="utf-8")
        driver = (CHILDCARE / "JobDriver_IncubateEggClutch.cs").read_text(encoding="utf-8")
        self.assertIn("JobMaker.MakeJob(jobDef, target, ChildcareDefenseUtility.GetIncubationEgg(target))", giver)
        self.assertIn("pawn.Reserve(target, job", driver)
        self.assertIn("job.GetTarget(TargetIndex.B).Thing", driver)
        self.assertIn("CanContinueEggIncubation(pawn, IncubationTarget, egg)", driver)
        self.assertIn("MarkEggIncubated(egg)", driver)
        self.assertIn("FinishEggIncubation(IncubationTarget, EggTarget)", driver)

    def test_duck_requires_fertilization(self):
        path = ROOT / "Zoology/Core/Patches/Core/Misc/EggFertilization.xml"
        root = ET.parse(path).getroot()
        self.assertIn('defName="Duck"', root.findtext("Operation/xpath"))
        for branch in ("match", "nomatch"):
            self.assertEqual(root.findtext(f"Operation/{branch}/value/eggProgressUnfertilizedMax"), "0.5")

    def test_burrow_open_command_has_icon(self):
        source = (ROOT / "Zoology/Source/Behaviour/Flee/AnimalRefugeOpening.cs").read_text(encoding="utf-8")
        self.assertIn("[StaticConstructorOnStartup]\n    internal static class AnimalRefugeOpening", source)
        self.assertIn('private static readonly Texture2D OpenIcon = ContentFinder<Texture2D>.Get("UI/Designators/Open");', source)
        self.assertIn("icon = OpenIcon,", source)
        self.assertIn("toggleAction =", source)


if __name__ == "__main__":
    unittest.main()
