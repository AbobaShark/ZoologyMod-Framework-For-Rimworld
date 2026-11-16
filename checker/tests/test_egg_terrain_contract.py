"""Source contracts for water-bound cohorts and terrestrial egg placement."""
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2] / "Zoology"
SOURCE = ROOT / "Source/Behaviour"


class EggTerrainContracts(unittest.TestCase):
    def test_adult_emergence_is_a_linked_profile_property(self):
        utility = (SOURCE / "Reproduction/AquaticEggLayerUtility.cs").read_text(encoding="utf-8")
        self.assertIn("larva?.emergeAsAdult == true && larva.pawnKind?.race == race", utility)
        self.assertIn("race.GetCompProperties<CompProperties_EggLayer>()?.eggFertilizedDef", utility)
        self.assertIn("AdultEmergenceRaces.Contains(race)", utility)
        self.assertNotIn("Meganeura", utility)
        generation = (SOURCE / "Reproduction/AquaticAdultGeneration.cs").read_text(encoding="utf-8")
        self.assertIn("UsesAdultEmergence(request.KindDef?.race)", generation)
        self.assertNotIn("Meganeura", generation)

    def test_water_cells_are_random_and_terrestrial_jobs_validate_dry_cells(self):
        index = (SOURCE / "Flee/AnimalRefuges.cs").read_text(encoding="utf-8")
        random_search = index.split("internal bool TryRandomWaterCell(", 1)[1].split("public void Register(", 1)[0]
        self.assertIn("Rand.Range(0, candidates.Count)", random_search)
        self.assertIn("cell.GetTerrain(map).IsWater", random_search)
        self.assertIn("pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)", random_search)
        self.assertNotIn("TryFindShore", random_search)
        nursery = (SOURCE / "Childcare/AnimalNursery.cs").read_text(encoding="utf-8")
        self.assertIn("water.TryRandomWaterCell(pawn", nursery)
        job = (SOURCE / "Reproduction/AnimalEggLayingTerrain.cs").read_text(encoding="utf-8")
        self.assertIn("cell.GetTerrain(map).IsWater == aquatic", job)
        self.assertIn("TryDryCell(pawn, out cell)", job)
        self.assertIn("ThingPlaceMode.Direct", job)
        self.assertLess(job.index("!AnimalEggLayingTerrain.CellSuitable(pawn, TargetA.Cell, aquatic)"),
                        job.index("layer.ProduceEgg()"))
        defs = ET.parse(ROOT / "Core/Defs/JobDefs/AnimalNurseryJobs.xml")
        self.assertEqual(defs.findtext(".//JobDef[defName='Zoology_LayEggOnTerrain']/driverClass"),
                         "ZoologyMod.JobDriver_LayEggOnTerrain, ZoologyMod")

    def test_nonbrooding_egg_protection_does_not_require_a_living_mother(self):
        care = (SOURCE / "Childcare/ChildcareDefenseUtility.cs").read_text(encoding="utf-8")
        method = care.split("public static bool ShouldNullifyEggDeterioration(", 1)[1].split(
            "public static void RegisterNearbyLaidEggsForMother(", 1)[0]
        self.assertIn("GetCompProperties<CompProperties_Hatcher>()?.hatcherPawn", method)
        self.assertIn("return !incubates ||", method)
        self.assertIn("TryGetComp<CompAmphibianRoe>() != null", method)
        patch = (SOURCE / "Childcare/Patch_Childcare_Eggs.cs").read_text(encoding="utf-8")
        self.assertIn("RegisterOwnership(mother, __result)", patch)

    def test_mature_nymphs_seek_shore_only_at_emergence(self):
        roe = (SOURCE / "Reproduction/AnimalAmphibianRoe.cs").read_text(encoding="utf-8")
        self.assertIn("TryStepTowardShore(parent.Map, parent.Position", roe)
        self.assertIn("Props.emergeAsAdult && progress >= 1f", roe)


if __name__ == "__main__":
    unittest.main()
