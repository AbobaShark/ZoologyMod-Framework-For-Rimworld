using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace ZoologyMod
{
    public static class WildAnimalEcosystemUtility
    {
        private const int EcosystemStatusCacheTicks = 250;

        private static readonly SimpleCurve PollutionToAnimalDensityFactorCurve = new SimpleCurve
        {
            new CurvePoint(0.1f, 1f),
            new CurvePoint(1f, 0.25f)
        };

        private static readonly Dictionary<int, EcosystemStatus> EcosystemStatusCache = new Dictionary<int, EcosystemStatus>();
        private static Game cachedGame;

        private static readonly MethodInfo AggregateAnimalDensityFactorMethod =
            AccessTools.Method(typeof(GameConditionManager), "AggregateAnimalDensityFactor", new[] { typeof(Map) });

        public static bool ShouldBlockWildWildMating(Pawn first, Pawn second, ZoologyModSettings settings = null)
        {
            settings ??= ModConstants.Settings;
            if (settings == null || settings.DisableAllRuntimePatches || !BothWildAnimals(first, second))
            {
                return false;
            }

            Map map = first.Map;
            if (map == null || second.Map != map)
            {
                return false;
            }

            if (settings.LimitWildAnimalsPerSpecies)
            {
                Dictionary<ThingDef, int> counts = GetEcosystemStatus(map).SpeciesCounts;
                if (counts.TryGetValue(first.def, out int firstCount) && firstCount >= settings.WildAnimalSpeciesLimit
                    || counts.TryGetValue(second.def, out int secondCount) && secondCount >= settings.WildAnimalSpeciesLimit)
                {
                    return true;
                }
            }

            return settings.EnableWildAnimalReproduction
                && settings.LimitWildAnimalReproductionByEcosystem
                && IsOverAllowedEcosystemWeight(map, settings.WildAnimalReproductionEcosystemLimitFactor);
        }

        public static bool IsSpeciesLimitActive(ZoologyModSettings settings)
        {
            return settings != null && !settings.DisableAllRuntimePatches && settings.LimitWildAnimalsPerSpecies;
        }

        public static Dictionary<ThingDef, int> GetLiveWildSpeciesCounts(Map map)
        {
            var counts = new Dictionary<ThingDef, int>();
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return counts;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!IsWildAnimal(pawn) || pawn.Dead || pawn.def == null)
                {
                    continue;
                }

                counts.TryGetValue(pawn.def, out int count);
                counts[pawn.def] = count + 1;
            }

            return counts;
        }

        public static Dictionary<ThingDef, float> GetExpectedSpeciesWeights(Map map, float allowedWeight)
        {
            var weights = new Dictionary<ThingDef, float>();
            if (map?.TileInfo == null || allowedWeight <= 0f)
            {
                return weights;
            }

            float total = 0f;
            foreach (BiomeDef biome in map.Biomes)
            {
                if (biome == null)
                {
                    continue;
                }

                foreach (PawnKindDef kind in biome.AllWildAnimals)
                {
                    if (kind?.race == null || !map.mapTemperature.SeasonAcceptableFor(kind.race))
                    {
                        continue;
                    }

                    float commonality = Mathf.Max(0f, biome.CommonalityOfAnimal(kind)
                        + (map.TileInfo.IsCoastal ? biome.CommonalityOfCoastalAnimal(kind) : 0f));
                    if (commonality <= 0f)
                    {
                        continue;
                    }

                    float expectedWeight = commonality * Mathf.Max(0f, kind.ecoSystemWeight);
                    weights.TryGetValue(kind.race, out float current);
                    weights[kind.race] = current + expectedWeight;
                    total += expectedWeight;
                }
            }

            if (total > 0f)
            {
                var species = new List<ThingDef>(weights.Keys);
                for (int i = 0; i < species.Count; i++)
                {
                    ThingDef race = species[i];
                    weights[race] = allowedWeight * weights[race] / total;
                }
            }

            return weights;
        }

        public static float AllowedEcosystemWeight(Map map, float limitFactor)
        {
            return GetEcosystemStatus(map).DesiredWeight * Mathf.Clamp(
                limitFactor,
                ModConstants.MinWildAnimalReproductionEcosystemLimitFactor,
                ModConstants.MaxWildAnimalReproductionEcosystemLimitFactor);
        }

        public static bool IsOverAllowedEcosystemWeight(Map map, float limitFactor)
        {
            return TryGetOverloadStatus(map, limitFactor, out _, out _, out _);
        }

        public static bool TryGetOverloadStatus(Map map, float limitFactor, out float currentWeight, out float allowedWeight, out float overloadRatio)
        {
            EcosystemStatus status = GetEcosystemStatus(map);
            currentWeight = status.CurrentWeight;
            allowedWeight = status.DesiredWeight * Mathf.Clamp(
                limitFactor,
                ModConstants.MinWildAnimalReproductionEcosystemLimitFactor,
                ModConstants.MaxWildAnimalReproductionEcosystemLimitFactor);
            overloadRatio = allowedWeight > 0f ? currentWeight / allowedWeight : 0f;

            return allowedWeight > 0f && currentWeight >= allowedWeight;
        }

        public static float DesiredTotalAnimalWeight(Map map)
        {
            float desiredAnimalDensity = DesiredAnimalDensity(map);
            if (desiredAnimalDensity <= 0f || float.IsNaN(desiredAnimalDensity))
            {
                return 0f;
            }

            return map.Area / (10000f / desiredAnimalDensity);
        }

        public static float CurrentTotalEcosystemWeight(Map map)
        {
            return GetEcosystemStatus(map).CurrentWeight;
        }

        private static EcosystemStatus GetEcosystemStatus(Map map)
        {
            if (map == null)
            {
                return new EcosystemStatus();
            }

            if (cachedGame != Current.Game)
            {
                cachedGame = Current.Game;
                EcosystemStatusCache.Clear();
            }

            int tick = GenTicks.TicksGame;
            int mapId = map.uniqueID;
            if (EcosystemStatusCache.TryGetValue(mapId, out EcosystemStatus cached)
                && tick - cached.Tick >= 0
                && tick - cached.Tick <= EcosystemStatusCacheTicks)
            {
                return cached;
            }

            var status = new EcosystemStatus
            {
                Tick = tick,
                DesiredWeight = DesiredTotalAnimalWeight(map)
            };

            IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (!IsWildAnimal(pawn) || pawn.Dead || pawn.def == null)
                    {
                        continue;
                    }

                    status.SpeciesCounts.TryGetValue(pawn.def, out int count);
                    status.SpeciesCounts[pawn.def] = count + 1;
                    if (pawn.kindDef != null)
                    {
                        status.CurrentWeight += pawn.kindDef.ecoSystemWeight;
                    }
                }
            }

            EcosystemStatusCache[mapId] = status;
            return status;
        }

        private static bool BothWildAnimals(Pawn first, Pawn second)
        {
            return IsWildAnimal(first) && IsWildAnimal(second);
        }

        public static bool IsWildAnimal(Pawn pawn)
        {
            return pawn != null
                && pawn.IsAnimal
                && pawn.Faction == null
                && pawn.HostFaction == null
                && !pawn.IsQuestLodger()
                && pawn.GetLord() == null;
        }

        private static float DesiredAnimalDensity(Map map)
        {
            if (map?.TileInfo == null)
            {
                return 0f;
            }

            float animalDensity = map.TileInfo.AnimalDensity;
            float seasonalCommonality = 0f;
            float totalCommonality = 0f;

            foreach (BiomeDef biome in map.Biomes)
            {
                if (biome == null)
                {
                    continue;
                }

                foreach (PawnKindDef animal in biome.AllWildAnimals)
                {
                    if (animal?.race == null)
                    {
                        continue;
                    }

                    float commonality = biome.CommonalityOfAnimal(animal);
                    if (map.TileInfo.IsCoastal)
                    {
                        commonality += biome.CommonalityOfCoastalAnimal(animal);
                    }

                    totalCommonality += commonality;
                    if (map.mapTemperature.SeasonAcceptableFor(animal.race))
                    {
                        seasonalCommonality += commonality;
                    }
                }
            }

            if (totalCommonality <= 0f)
            {
                return 0f;
            }

            animalDensity *= seasonalCommonality / totalCommonality;
            animalDensity *= AggregateAnimalDensityFactor(map);

            if (ModsConfig.BiotechActive)
            {
                animalDensity *= PollutionToAnimalDensityFactorCurve.Evaluate(map.TileInfo.pollution);
            }

            return animalDensity;
        }

        private static float AggregateAnimalDensityFactor(Map map)
        {
            if (map?.gameConditionManager == null || AggregateAnimalDensityFactorMethod == null)
            {
                return 1f;
            }

            try
            {
                object value = AggregateAnimalDensityFactorMethod.Invoke(map.gameConditionManager, new object[] { map });
                return value is float factor ? factor : 1f;
            }
            catch (Exception ex)
            {
                Log.WarningOnce($"[Zoology] Failed to read vanilla animal density factor. Wild reproduction ecosystem limit will ignore game condition density changes. Exception: {ex}", 196613021);
                return 1f;
            }
        }

        private sealed class EcosystemStatus
        {
            public int Tick;
            public float DesiredWeight;
            public float CurrentWeight;
            public readonly Dictionary<ThingDef, int> SpeciesCounts = new Dictionary<ThingDef, int>();
        }
    }
}
