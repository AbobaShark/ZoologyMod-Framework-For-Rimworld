"""Static and mathematical contracts for dynamic inland-water wildlife spawn weights."""

from pathlib import Path
import unittest
from lxml import etree as E

ROOT = Path(__file__).resolve().parents[2]
REQUIREMENTS = ROOT / "Zoology/Core/Defs/Misc/WaterBodySpawnRequirements.xml"
VEF_PATCH = ROOT / "Zoology/ModPatches/VanillaAnimalsExpanded/Patches/Misc/RiverNeedingAnimals_VAE.xml"
RUNTIME = ROOT / "Zoology/Source/Behaviour/Reproduction/WaterBodySpawnRequirement.cs"


def habitat_factor(areas, map_cells, body_size, river_cells=0):
    total = sum(areas)
    largest = max(areas, default=0)
    wetness = (total + largest + 4.2 * river_cells) / (2 * map_cells) if map_cells > 0 else 0
    boost = 3 * min(1, wetness / .111)
    requirement = 200 * body_size ** (2 if body_size <= 1 else 4 / 3)
    if largest <= requirement / 3:
        return 0
    effective_area = sum(a * min(1, a / requirement) for a in areas)
    connected = (largest - requirement / 3) / (requirement * 2 / 3)
    return boost * min(1, connected, effective_area / requirement)


def commonality_factor(factor, base_commonality, aquatic=True):
    if not aquatic:
        return 1
    return min(factor, .5 / base_commonality) if base_commonality > 0 else factor


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
            {"Duck", "Goose", "Swan", "Heron", "Bullfrog", "ColossusToad", "Capybara",
             "Flamingo", "Alligator", "Hippo", "AEXP_Beaver", "AEXP_Crocodile",
             "AEXP_Hippopotamus", "AEXP_Platypus", "AEXP_WildGoose",
             "AEXP_Anaconda", "AEXP_Alligator", "VAEWaste_Pestigator",
             "Castoroides", "Spinosaurus", "Baryonyx", "Deinocheirus", "Mink", "AA_FlamingoPhoenix",
             "Titanoboa", "Gigantophis", "Purussaurus", "Josephoartigasia",
             "Enhydriodon", "VAERoy_Swan", "VAERoy_Crane", "AEXP_Tapir",
             "Meganeura", "Platybelodon"} <= set(animals)
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
            "AreaPerBodySizePower = 200f",
            "size, size <= 1f ? 2.0 : 4.0 / 3.0",
            "FullHabitatWetness = 0.111f",
            'startTerrain.tags?.Contains("River") == true',
            "riverCells++",
            "RiverAreaBonus = 4.2f",
            "totalWater + largestWater + RiverAreaBonus * riverCells",
            "MaximumAquaticFactor = 3f",
            "MaximumAquaticCommonality = 0.5f",
            "!RequiredRaces.Contains(animalRace)",
            "largestWater <= minimumConnectedArea",
            "Math.Min(connectedSuitability, effectiveArea / requirement)",
            "area * Math.Min(1f, area / requirement)",
            "cache.aquaticFactors.TryGetValue(animalRace, out float cachedFactor)",
            "MaximumAquaticCommonality / baseCommonality",
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
        self.assertNotIn("HasEctothermicExtension", source)
        self.assertNotIn("ecoSystemWeight", source)

    def test_inland_water_calibration(self):
        self.assertAlmostEqual(habitat_factor([624, 624, 624, 624, 624, 624, 256], 62500, .2),
                               4624 / 4625)
        self.assertAlmostEqual(habitat_factor([495, 250, 255], 62500, .2), 1495 / 4625)
        self.assertEqual(habitat_factor([], 62500, .2), 0)

    def test_river_calibration(self):
        for body_size in (.15, .65, 1.5, 1.8, 3.25, 4.5):
            self.assertAlmostEqual(habitat_factor([1500], 62500, body_size, 1500), 9300 / 4625)
            self.assertEqual(habitat_factor([3500], 62500, body_size, 3500), 3)
        self.assertAlmostEqual(habitat_factor([1000], 62500, 3.25, 1000), 6200 / 4625)
        self.assertLess(habitat_factor([1000], 62500, 4.5, 1000),
                        habitat_factor([1000], 62500, 3.25, 1000))

    def test_large_animals_require_connected_water(self):
        self.assertEqual(habitat_factor([225] * 6, 62500, 4.5), 0)
        self.assertEqual(habitat_factor([400] * 12, 62500, 4.5), 0)
        self.assertGreater(habitat_factor([750], 62500, 4.5), 0)
        self.assertGreater(habitat_factor([3500], 62500, 4.5),
                           habitat_factor([1500], 62500, 4.5))

    def test_commonality_cap_preserves_group_correction(self):
        for base in (.025, .1, .15, .175, .275, .5, .8):
            for factor in (0, .3, 1, 2, 3):
                adjusted = commonality_factor(factor, base)
                self.assertAlmostEqual(base * adjusted, min(.5, factor * base))
                for group_size in (1, 5.5, 10):
                    self.assertAlmostEqual(base / group_size * adjusted,
                                           min(.5, base * factor) / group_size)
                self.assertEqual(commonality_factor(factor, base, aquatic=False), 1)

    def test_marginal_habitat_has_low_weight(self):
        for body_size in (.15, .2, 1, 1.5, 1.8, 4.5):
            requirement = 200 * body_size ** (2 if body_size <= 1 else 4 / 3)
            threshold = requirement / 3
            self.assertEqual(habitat_factor([threshold], 62500, body_size), 0)
            marginal = habitat_factor([threshold * 1.01] * 10, 62500, body_size)
            self.assertGreater(marginal, 0)
            self.assertLess(marginal, .03)

    def test_monotonic_and_bounded(self):
        for body_size in (.15, 1.8, 4.5, 6):
            for river_fraction in (0, .5, 1):
                factors = [habitat_factor([area], 62500, body_size, area * river_fraction)
                           for area in range(0, 10001, 25)]
                self.assertEqual(factors, sorted(factors))
                self.assertTrue(all(0 <= factor <= 3 for factor in factors))

    def test_equal_coverage_at_different_map_sizes(self):
        self.assertAlmostEqual(habitat_factor([100], 10000, .15),
                               habitat_factor([400], 40000, .15))


if __name__ == "__main__":
    unittest.main()
