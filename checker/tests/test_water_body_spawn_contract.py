"""Static and mathematical contracts for dynamic inland-water wildlife spawn weights."""

from pathlib import Path
import unittest
from lxml import etree as E

ROOT = Path(__file__).resolve().parents[2]
REQUIREMENTS = ROOT / "Zoology/Core/Defs/Misc/WaterBodySpawnRequirements.xml"
VEF_PATCH = ROOT / "Zoology/ModPatches/VanillaAnimalsExpanded/Patches/Misc/RiverNeedingAnimals_VAE.xml"
RUNTIME = ROOT / "Zoology/Source/Behaviour/Reproduction/WaterBodySpawnRequirement.cs"


def habitat_factors(areas, map_cells, body_size, ectothermic):
    """Python reference for C# algebra, independent of RimWorld APIs."""
    total = sum(areas)
    largest = max(areas, default=0)
    wetness = (total + largest) / (2 * map_cells) if map_cells > 0 else 0
    t = max(0, min(1, (wetness - .01) / .11))
    boost = 1 + 2 * t
    r = 150 * body_size ** 2 * (1 if ectothermic else 2)
    h = sum(a * min(1, a / r) for a in areas)
    aquatic = boost * min(1, h / r) if largest >= r / 3 else 0
    return aquatic, 1 / boost


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
        operation = E.parse(str(VEF_PATCH)).getroot()[0]
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
            "largestWater < requirement / 3f",
            "area * Math.Min(1f, area / requirement)",
            "cache.aquaticFactors.TryGetValue(animalRace, out float factor)",
            "cache.landFactor = 1f / aquaticBoost",
            "biome.CommonalityOfCoastalAnimal(def) > 0f",
            "BiomeDef biome = ___map.BiomeAt(loc)",
        ):
            self.assertIn(token, source)
        self.assertNotIn("TerrainDefOf.Marsh", source)
        # A normal rat is no longer protected merely because it also occurs
        # in the biome's pollution pool.
        self.assertNotIn("CommonalityOfPollutionAnimal", source)
        self.assertIn("biome.CommonalityOfCoastalAnimal(def) > 0f", source)
        self.assertNotIn("AnimalStats", source)
        self.assertNotIn("AEXP_Crocodile", source)

    def test_large_animals_require_large_connected_water(self):
        p = lambda areas: habitat_factors(areas, 62500, 4.5, True)[0]
        a = lambda areas: habitat_factors(areas, 62500, 1.5, True)[0]
        self.assertEqual(p([225] * 6), 0)
        self.assertEqual(p([650] * 12), 0)
        self.assertEqual(p([750] * 30), 0)
        self.assertGreater(p([3000]), 0)
        self.assertGreater(p([17700]), p([3000]))
        self.assertGreater(a([650]), 0)
        self.assertGreater(a([3000]), a([650]))
        self.assertEqual(habitat_factors([], 62500, 1.5, True), (0, 1))
        self.assertAlmostEqual(habitat_factors([30000], 62500, 4.5, True)[1], 1/3)

    def test_pollution_pool_species_use_ordinary_land_wetness_factor(self):
        # Rat (base commonality 0.5) must be reduced to one third of
        # its original weight at maximum wetness; coastal pools are
        # independently exempted by the C# Postfix.
        dry = habitat_factors([], 62500, 0.1, False)[1]
        wet = habitat_factors([30000], 62500, 0.1, False)[1]
        self.assertAlmostEqual(0.5 * dry, 0.5)
        self.assertAlmostEqual(0.5 * wet, 1 / 6)

    def test_equal_coverage_at_different_map_sizes(self):
        self.assertAlmostEqual(
            habitat_factors([1000], 10000, 1.5, True)[1],
            habitat_factors([4000], 40000, 1.5, True)[1],
        )

    def test_endotherms_require_more_water_at_equal_size(self):
        sizes = [500]
        ecto = habitat_factors(sizes, 62500, 1.5, True)[0]
        endo = habitat_factors(sizes, 62500, 1.5, False)[0]
        self.assertGreater(ecto, endo)


if __name__ == "__main__":
    unittest.main()
