"""Static and mathematical contracts for dynamic inland-water wildlife spawn weights."""

from pathlib import Path
import unittest
from lxml import etree as E

ROOT = Path(__file__).resolve().parents[2]
REQUIREMENTS = ROOT / "Zoology/Core/Defs/Misc/WaterBodySpawnRequirements.xml"
VEF_PATCH = ROOT / "Zoology/ModPatches/VanillaAnimalsExpanded/Patches/Misc/RiverNeedingAnimals_VAE.xml"
RUNTIME = ROOT / "Zoology/Source/Behaviour/Reproduction/WaterBodySpawnRequirement.cs"


def habitat_factors(areas, map_cells, body_size, ectothermic, river_cells=0):
    """Python reference for C# algebra, independent of RimWorld APIs."""
    total = sum(areas)
    largest = max(areas, default=0)
    wetness = (total + largest + 2 * river_cells) / (2 * map_cells) if map_cells > 0 else 0
    boost = 2 * min(1, wetness / .048)
    r = 150 * body_size ** 2 * (1 if ectothermic else 2)
    if largest <= r / 3:
        return 0, 1
    h = sum(a * min(1, a / r) for a in areas)
    connected = (largest - r / 3) / (r - r / 3)
    return boost * min(1, connected, h / r), 1


class WaterBodySpawnContractTests(unittest.TestCase):
    def test_species_list_has_no_legacy_threshold(self):
        defs = E.parse(str(REQUIREMENTS)).xpath(
            "/Defs/ZoologyMod.WaterBodySpawnRequirementDef"
        )
        self.assertEqual(len(defs), 1)
        self.assertEqual(defs[0].findtext("defName"), "Zoology_WaterBodySpawnRequirements")
        self.assertIsNone(defs[0].find("minimumConnectedWaterCells"))
        animals = defs[0].xpath("animals/li/text()")
        self.assertEqual(len(animals), len(set(animals)))
        self.assertTrue(
            {"Duck", "Swan", "Heron", "Bullfrog", "ColossusToad", "Capybara",
             "Flamingo", "Alligator", "Hippo", "AEXP_Beaver", "AEXP_Crocodile",
             "AEXP_Hippopotamus", "AEXP_Platypus", "AEXP_WildGoose",
             "AEXP_Anaconda", "AEXP_Alligator", "VAEWaste_Pestigator",
             "Castoroides", "Spinosaurus", "Mink", "AA_FlamingoPhoenix",
             "Titanoboa", "Gigantophis", "Purussaurus", "Josephoartigasia",
             "Enhydriodon"} <= set(animals)
        )

    def test_vef_river_restrictions_only_remove_transferred_species(self):
        doc = E.fromstring("""<Defs>
            <VEF.AnimalBehaviours.RiverNeedingAnimalDef>
              <riverNeedingAnimals>
                <li>AEXP_Beaver</li><li>AEXP_Crocodile</li><li>OtherAnimal</li>
              </riverNeedingAnimals>
            </VEF.AnimalBehaviours.RiverNeedingAnimalDef>
            <VEF.AnimalBehaviours.RiverNeedingAnimalDef>
              <riverNeedingAnimals><li>AEXP_Hippopotamus</li></riverNeedingAnimals>
            </VEF.AnimalBehaviours.RiverNeedingAnimalDef>
        </Defs>""")
        operation, = E.parse(str(VEF_PATCH)).xpath("/Patch/Operation")
        self.assertEqual(operation.get("Class"), "PatchOperationConditional")
        self.assertEqual(operation.find("match").get("Class"), "PatchOperationRemove")
        path = operation.findtext("xpath")
        self.assertEqual(path, operation.findtext("match/xpath"))
        for node in doc.xpath(path):
            node.getparent().remove(node)
        self.assertEqual(doc.xpath("//riverNeedingAnimals/li/text()"), ["OtherAnimal"])

    def test_single_cached_map_scan_uses_patched_race_properties(self):
        source = RUNTIME.read_text(encoding="utf-8")
        for token in (
            '"CommonalityOfAnimalNow"',
            "ConditionalWeakTable<Map, MapWaterCache>",
            "CacheLifetimeTicks = 15000",
            "Array.Clear(cache.visited, 0, count)",
            "areas.Add(next)",
            "totalWater += next",
            "startTerrain.IsOcean",
            "count - oceanCells",
            "terrain.IsWater && !terrain.IsOcean",
            "race.race?.baseBodySize",
            "ZoologyCacheUtility.HasEctothermicExtension(race)",
            "AreaPerBodySizeSquared * size * size",
            "FullHabitatWetness = 0.048f",
            'startTerrain.tags?.Contains("River") == true',
            "riverCells++",
            "totalWater + largestWater + 2f * riverCells",
            "MaximumAquaticFactor = 2f",
            "!RequiredRaces.Contains(animalRace)",
            "largestWater <= minimumConnectedArea",
            "Math.Min(connectedSuitability, effectiveArea / requirement)",
            "area * Math.Min(1f, area / requirement)",
            "cache.aquaticFactors.TryGetValue(animalRace, out float factor)",
            "? factor : 1f",
            "biome.CommonalityOfCoastalAnimal(def) > 0f",
            "BiomeDef biome = ___map.BiomeAt(loc)",
        ):
            self.assertIn(token, source)
        self.assertIn("MarshClimateMinimum = 15f", source)
        self.assertIn("GenTemperature.AverageTemperatureAtTileForTwelfth(map.Tile, (Twelfth)i)", source)
        self.assertIn("annualTemperature / 12f >= MarshClimateMinimum", source)
        self.assertIn('includeMarsh || terrain.defName != "Marsh"', source)
        self.assertIn("IsHabitatWater(startTerrain, includeMarsh)", source)
        self.assertIn("IsHabitatWater(terrain.TerrainAt(index), includeMarsh)", source)
        for offset in ("cell - 1", "cell + 1", "cell - width", "cell + width"):
            self.assertIn(f"EnqueueWater(terrain, {offset}, visited, queue, ref next, includeMarsh)", source)
        self.assertNotIn("CommonalityOfPollutionAnimal", source)
        self.assertNotIn("landFactor", source)
        self.assertNotIn("AnimalStats", source)
        self.assertNotIn("AEXP_Crocodile", source)

    def test_marsh_climate_threshold_and_connectivity(self):
        # Two freshwater ponds separated by a marsh band.
        # The band connects the ponds only in warm climates.
        for annual_temperature, areas in ((-5, [200, 200]),
                                          (14.99, [200, 200]),
                                          (15, [1500]),
                                          (25, [1500])):
            crocodile = habitat_factors(areas, 62500, 1.8, True)[0]
            if annual_temperature < 15:
                self.assertLess(crocodile, 0.1)
            else:
                self.assertEqual(crocodile, 1)
        self.assertEqual(habitat_factors([], 62500, 0.15, False)[0], 0)
        self.assertEqual(habitat_factors([1500], 62500, 1.8, True, river_cells=1500)[0], 2)

    def test_large_animals_require_large_connected_water(self):
        p = lambda areas: habitat_factors(areas, 62500, 4.5, True)[0]
        a = lambda areas: habitat_factors(areas, 62500, 1.5, True)[0]
        self.assertEqual(p([225] * 6), 0)
        self.assertEqual(p([650] * 12), 0)
        self.assertEqual(p([750] * 30), 0)
        self.assertGreater(p([3000]), 0)
        self.assertGreater(p([17700]), p([3000]))
        self.assertGreater(a([650]), 0)
        self.assertGreater(a([1500]), a([650]))
        self.assertEqual(habitat_factors([], 62500, 1.5, True), (0, 1))
        self.assertAlmostEqual(habitat_factors([30000], 62500, 4.5, True)[1], 1)

    def test_land_animals_always_keep_base_weight(self):
        for areas in ([], [3], [100], [1500], [30000]):
            self.assertEqual(habitat_factors(areas, 62500, 0.1, False)[1], 1)

    def test_inland_water_calibration(self):
        # A 495-cell largest pond in 1000 total water cells.
        areas = [495, 250, 255]
        for body_size, ectothermic in ((0.15, False), (0.2, False),
                                       (0.65, False), (1.5, True), (1.8, True)):
            factor = habitat_factors(areas, 62500, body_size, ectothermic)[0]
            self.assertAlmostEqual(factor, 1495 / 3000)
        self.assertEqual(habitat_factors([1500], 62500, 1.8, True)[0], 1)

    def test_river_width_calibration(self):
        for body_size in (1.5, 1.8):
            self.assertAlmostEqual(
                habitat_factors([1000], 62500, body_size, True, river_cells=1000)[0],
                4 / 3)
            for area in (1500, 3500, 7500):
                self.assertEqual(
                    habitat_factors([area], 62500, body_size, True,
                                    river_cells=area)[0], 2)
        self.assertEqual(habitat_factors([1000], 62500, 4.5, True, river_cells=1000)[0], 0)

    def test_marginal_habitat_has_low_weight(self):
        for body_size, ectothermic in ((0.15, False), (0.2, False),
                                       (1.5, True), (1.8, True), (4.5, True)):
            r = 150 * body_size ** 2 * (1 if ectothermic else 2)
            at_threshold = r / 3
            self.assertEqual(habitat_factors([at_threshold], 62500,
                                             body_size, ectothermic)[0], 0)
            marginal = habitat_factors([at_threshold * 1.01] * 10,
                                       62500, body_size, ectothermic)[0]
            self.assertGreater(marginal, 0)
            self.assertLess(marginal, 0.02)

    def test_monotonic_and_bounded(self):
        for body_size, ectothermic in ((0.15, False), (1.8, True), (4.5, True)):
            factors = [habitat_factors([area], 62500, body_size, ectothermic)[0]
                       for area in range(0, 10001, 25)]
            self.assertEqual(factors, sorted(factors))
            self.assertTrue(all(0 <= factor <= 2 for factor in factors))

    def test_equal_coverage_at_different_map_sizes(self):
        self.assertAlmostEqual(
            habitat_factors([100], 10000, 0.15, False)[0],
            habitat_factors([400], 40000, 0.15, False)[0],
        )

    def test_endotherms_require_more_water_at_equal_size(self):
        sizes = [500]
        ecto = habitat_factors(sizes, 62500, 1.5, True)[0]
        endo = habitat_factors(sizes, 62500, 1.5, False)[0]
        self.assertGreater(ecto, endo)


if __name__ == "__main__":
    unittest.main()
