using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

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

        private static readonly MethodInfo AggregateAnimalDensityFactorMethod =
            AccessTools.Method(typeof(GameConditionManager), "AggregateAnimalDensityFactor", new[] { typeof(Map) });

        public static bool ShouldBlockWildWildMating(Pawn first, Pawn second, ZoologyModSettings settings = null)
        {
            settings ??= ModConstants.Settings;
            if (settings == null
                || !settings.LimitWildAnimalReproductionByEcosystem
                || !BothWildAnimals(first, second))
            {
                return false;
            }

            Map map = first.Map;
            if (map == null || second.Map != map)
            {
                return false;
            }

            return IsOverAllowedEcosystemWeight(map, settings.WildAnimalReproductionEcosystemLimitFactor);
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
            if (map?.mapPawns == null)
            {
                return 0f;
            }

            float total = 0f;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (IsWildAnimal(pawn) && pawn.kindDef != null)
                {
                    total += pawn.kindDef.ecoSystemWeight;
                }
            }

            return total;
        }

        private static EcosystemStatus GetEcosystemStatus(Map map)
        {
            if (map == null)
            {
                return default;
            }

            int tick = GenTicks.TicksGame;
            int mapId = map.uniqueID;
            if (EcosystemStatusCache.TryGetValue(mapId, out EcosystemStatus cached)
                && tick - cached.Tick <= EcosystemStatusCacheTicks)
            {
                return cached;
            }

            EcosystemStatus status = new EcosystemStatus
            {
                Tick = tick,
                DesiredWeight = DesiredTotalAnimalWeight(map),
                CurrentWeight = CurrentTotalEcosystemWeight(map)
            };
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
                && pawn.HostFaction == null;
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

        private struct EcosystemStatus
        {
            public int Tick;
            public float DesiredWeight;
            public float CurrentWeight;
        }
    }
}
