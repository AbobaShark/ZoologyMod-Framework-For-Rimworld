using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Only the membership list lives in XML. Physiological parameters are
    // read from the fully patched race Defs in memory.
    public sealed class WaterBodySpawnRequirementDef : Def
    {
        public List<string> animals = new List<string>();
    }

    internal static class WaterBodySpawnRequirement
    {
        private const int CacheLifetimeTicks = 15000;
        private const string RequirementDefName = "Zoology_WaterBodySpawnRequirements";
        private const float AreaPerBodySizeSquared = 150f;
        private const float EndothermAreaMultiplier = 2f;
        private const float FullHabitatWetness = 0.048f;
        private const float MaximumAquaticFactor = 2f;
        private const float MarshClimateMinimum = 15f;

        private sealed class MapWaterCache
        {
            internal int checkedAt;
            internal bool initialized;
            internal bool[] visited;
            internal int[] queue;
            internal readonly List<int> waterBodyAreas = new List<int>(64);
            internal readonly Dictionary<ThingDef, float> aquaticFactors =
                new Dictionary<ThingDef, float>(32);
        }

        private static readonly ConditionalWeakTable<Map, MapWaterCache> MapCaches =
            new ConditionalWeakTable<Map, MapWaterCache>();
        private static readonly HashSet<ThingDef> RequiredRaces = new HashSet<ThingDef>();
        private static bool requirementsLoaded;

        internal static float NaturalSpawnCommonalityFactor(Map map, ThingDef animalRace)
        {
            if (animalRace == null || map?.terrainGrid == null)
                return 1f;

            EnsureRequirementsLoaded();
            if (!RequiredRaces.Contains(animalRace))
                return 1f;

            MapWaterCache cache = MapCaches.GetValue(map, _ => new MapWaterCache());
            int now = GenTicks.TicksGame;
            if (!cache.initialized || now < cache.checkedAt
                || now - cache.checkedAt >= CacheLifetimeTicks)
            {
                RebuildMapFactors(map, cache);
                cache.checkedAt = now;
                cache.initialized = true;
            }

            return cache.aquaticFactors.TryGetValue(animalRace, out float factor)
                ? factor : 1f;
        }

        private static void EnsureRequirementsLoaded()
        {
            if (requirementsLoaded)
                return;

            requirementsLoaded = true;
            WaterBodySpawnRequirementDef rules =
                DefDatabase<WaterBodySpawnRequirementDef>.GetNamedSilentFail(RequirementDefName);
            if (rules?.animals == null)
                return;

            for (int i = 0; i < rules.animals.Count; i++)
            {
                string name = rules.animals[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                ThingDef race = DefDatabase<ThingDef>.GetNamedSilentFail(name.Trim());
                if (race != null)
                    RequiredRaces.Add(race);
            }
        }

        // One flood fill (four-way adjacency) per cached map refresh.
        // Freshwater habitats include marsh terrain in warm climates.
        private static void RebuildMapFactors(Map map, MapWaterCache cache)
        {
            TerrainGrid terrain = map.terrainGrid;
            bool includeMarsh = false;
            if (map.TileInfo != null)
            {
                float annualTemperature = 0f;
                for (int i = 0; i < 12; i++)
                    annualTemperature += GenTemperature.AverageTemperatureAtTileForTwelfth(map.Tile, (Twelfth)i);
                includeMarsh = annualTemperature / 12f >= MarshClimateMinimum;
            }
            int width = map.Size.x;
            int count = map.cellIndices.NumGridCells;
            if (cache.visited == null || cache.visited.Length != count)
            {
                cache.visited = new bool[count];
                cache.queue = new int[count];
            }
            else
            {
                Array.Clear(cache.visited, 0, count);
            }

            List<int> areas = cache.waterBodyAreas;
            areas.Clear();
            int oceanCells = 0;
            int riverCells = 0;
            int totalWater = 0;
            int largestWater = 0;
            bool[] visited = cache.visited;
            int[] queue = cache.queue;

            if (width > 0 && count > 0 && terrain.AnyWaterCells)
            {
                for (int start = 0; start < count; start++)
                {
                    TerrainDef startTerrain = terrain.TerrainAt(start);
                    // Count ocean and river terrain once per cell.
                    if (startTerrain != null && startTerrain.IsOcean)
                        oceanCells++;
                    else if (IsHabitatWater(startTerrain, includeMarsh)
                        && startTerrain.tags?.Contains("River") == true)
                        riverCells++;
                    if (visited[start])
                        continue;

                    visited[start] = true;
                    if (!IsHabitatWater(startTerrain, includeMarsh))
                        continue;

                    int first = 0;
                    int next = 1;
                    queue[0] = start;
                    while (first < next)
                    {
                        int cell = queue[first++];
                        int x = cell % width;
                        if (x > 0)
                            EnqueueWater(terrain, cell - 1, visited, queue, ref next, includeMarsh);
                        if (x + 1 < width)
                            EnqueueWater(terrain, cell + 1, visited, queue, ref next, includeMarsh);
                        if (cell >= width)
                            EnqueueWater(terrain, cell - width, visited, queue, ref next, includeMarsh);
                        if (cell + width < count)
                            EnqueueWater(terrain, cell + width, visited, queue, ref next, includeMarsh);
                    }

                    areas.Add(next);
                    totalWater += next;
                    if (next > largestWater)
                        largestWater = next;
                }
            }

            int eligibleCells = count - oceanCells;
            float wetness = eligibleCells > 0
                ? (totalWater + largestWater + 2f * riverCells) / (2f * eligibleCells) : 0f;
            // Flowing freshwater contributes additional habitat productivity
            // while connected-area requirements use actual water cells.
            float aquaticBoost = MaximumAquaticFactor * Math.Min(1f, wetness / FullHabitatWetness);

            // Species scores and Def extension lookups are outside the
            // candidate-selection hot path.
            cache.aquaticFactors.Clear();
            foreach (ThingDef race in RequiredRaces)
            {
                float size = race.race?.baseBodySize ?? 0f;
                if (size <= 0f)
                {
                    cache.aquaticFactors[race] = 0f;
                    continue;
                }

                bool ectothermic = ZoologyCacheUtility.HasEctothermicExtension(race);
                float requirement = AreaPerBodySizeSquared * size * size
                    * (ectothermic ? 1f : EndothermAreaMultiplier);
                float minimumConnectedArea = requirement / 3f;
                if (largestWater <= minimumConnectedArea)
                {
                    cache.aquaticFactors[race] = 0f;
                    continue;
                }

                float effectiveArea = 0f;
                for (int i = 0; i < areas.Count; i++)
                {
                    float area = areas[i];
                    effectiveArea += area * Math.Min(1f, area / requirement);
                }

                float connectedSuitability = (largestWater - minimumConnectedArea)
                    / (requirement - minimumConnectedArea);
                float habitatSuitability = Math.Min(1f,
                    Math.Min(connectedSuitability, effectiveArea / requirement));
                cache.aquaticFactors[race] = aquaticBoost * habitatSuitability;
            }
        }

        private static bool IsHabitatWater(TerrainDef terrain, bool includeMarsh)
        {
            return terrain != null && terrain.IsWater && !terrain.IsOcean
                && (includeMarsh || terrain.defName != "Marsh");
        }

        private static void EnqueueWater(
            TerrainGrid terrain, int index, bool[] visited, int[] queue, ref int next, bool includeMarsh)
        {
            if (visited[index])
                return;

            visited[index] = true;
            if (IsHabitatWater(terrain.TerrainAt(index), includeMarsh))
                queue[next++] = index;
        }
    }

    // Modifies the existing natural-wildlife weight only; explicitly chosen
    // PawnKinds and species in coastal spawn pools remain untouched.
    [HarmonyPatch]
    internal static class Patch_WildAnimalSpawner_WaterBodyRequirement
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(WildAnimalSpawner),
                "CommonalityOfAnimalNow",
                new[] { typeof(PawnKindDef), typeof(IntVec3) });
            return method?.ReturnType == typeof(float) ? method : null;
        }

        private static void Postfix(Map ___map, PawnKindDef def, IntVec3 loc, ref float __result)
        {
            if (__result <= 0f || def?.race == null || ___map == null)
                return;

            BiomeDef biome = ___map.BiomeAt(loc);
            if (biome == null || biome.CommonalityOfCoastalAnimal(def) > 0f)
                return;

            __result *= WaterBodySpawnRequirement.NaturalSpawnCommonalityFactor(
                ___map, def.race);
        }
    }
}
