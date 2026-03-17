using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod.HarmonyPatches
{
    public static class Patch_ScavengeringAI
    {
        private const int ScavengerSearchCooldownTicks = ZoologyTickLimiter.Scavenging.SearchCooldownTicks;
        private const int ScavengerSearchCooldownFailTicks = ZoologyTickLimiter.Scavenging.SearchCooldownFailTicks;
        private const int ScavengerCachedResultTicks = ZoologyTickLimiter.Scavenging.CachedResultTicks;
        private const int ScavengerMaxScansPerTickPerMap = ZoologyTickLimiter.Scavenging.MaxScansPerTickPerMap;

        private struct ScavengerSearchState
        {
            public int LastSearchTick;
            public int LastFoundTick;
            public Thing LastFound;
            public ThingDef LastFoodDef;
            public bool LastSearchHadResult;
        }

        private static readonly Dictionary<int, ScavengerSearchState> scavengerSearchStates = new Dictionary<int, ScavengerSearchState>(256);
        private static readonly Dictionary<int, int> mapScansThisTick = new Dictionary<int, int>(8);
        private static int mapScansTick = -1;

        
        [HarmonyPatch(typeof(FoodUtility))]
        [HarmonyPatch("BestFoodSourceOnMap")]
        private static class Inner_FoodUtility_BestFoodSourceOnMap
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Postfix(Pawn getter, Pawn eater, bool desperate, ref Thing __result, ref ThingDef foodDef,
                FoodPreferability maxPref = FoodPreferability.MealLavish, bool allowPlant = true, bool allowDrug = true,
                bool allowCorpse = true, bool allowDispenserFull = true, bool allowDispenserEmpty = true,
                bool allowForbidden = false, bool allowSociallyImproper = false, bool allowHarvest = false,
                bool forceScanWholeMap = false, bool ignoreReservations = false, bool calculateWantedStackCount = false,
                FoodPreferability minPrefOverride = FoodPreferability.Undefined, float? minNutrition = null,
                bool allowVenerated = false)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;
                    if (__result != null) return;
                    if (eater == null || getter == null) return;

                    var scav = DefModExtensionCache<ModExtension_IsScavenger>.Get(eater.def);
                    if (scav == null) return; 
                    int currentTick = Find.TickManager?.TicksGame ?? 0;

                    int pawnId = eater.thingIDNumber;
                    if (!scavengerSearchStates.TryGetValue(pawnId, out ScavengerSearchState state))
                    {
                        state = new ScavengerSearchState
                        {
                            LastSearchTick = -999999,
                            LastFoundTick = -999999,
                            LastFound = null,
                            LastFoodDef = null,
                            LastSearchHadResult = false
                        };
                    }

                    if (TryUseCachedResult(currentTick, getter, eater, allowForbidden, allowVenerated, ignoreReservations, minNutrition, scav, ref state, out Thing cached, out ThingDef cachedDef))
                    {
                        __result = cached;
                        foodDef = cachedDef;
                        scavengerSearchStates[pawnId] = state;
                        return;
                    }

                    var corpseLister = getter.Map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                    if (corpseLister == null || corpseLister.Count == 0)
                    {
                        state.LastSearchTick = currentTick;
                        state.LastSearchHadResult = false;
                        scavengerSearchStates[pawnId] = state;
                        return;
                    }

                    int cooldown = state.LastSearchHadResult ? ScavengerSearchCooldownTicks : ScavengerSearchCooldownFailTicks;
                    if (currentTick - state.LastSearchTick < cooldown)
                    {
                        scavengerSearchStates[pawnId] = state;
                        return;
                    }

                    if (!TryConsumeMapScanBudget(getter.Map, currentTick))
                    {
                        return;
                    }

                    state.LastSearchTick = currentTick;
                    ThingDef foundDef;
                    Thing found = FindClosestCorpseByDistance(getter, eater, allowForbidden, allowVenerated,
                        ignoreReservations, minNutrition, scav, corpseLister, out foundDef);

                    if (found != null)
                    {
                        __result = found;
                        foodDef = foundDef;

                        state.LastFound = found;
                        state.LastFoodDef = foundDef;
                        state.LastFoundTick = currentTick;
                        state.LastSearchHadResult = true;
                        scavengerSearchStates[pawnId] = state;
                        return;
                    }

                    state.LastSearchHadResult = false;
                    state.LastFound = null;
                    state.LastFoodDef = null;
                    scavengerSearchStates[pawnId] = state;
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in BestFoodSourceOnMap postfix: " + e);
                }
            }

            private static bool TryConsumeMapScanBudget(Map map, int currentTick)
            {
                if (map == null) return false;
                if (mapScansTick != currentTick)
                {
                    mapScansTick = currentTick;
                    mapScansThisTick.Clear();
                }

                int mapId = map.uniqueID;
                mapScansThisTick.TryGetValue(mapId, out int count);
                if (count >= ScavengerMaxScansPerTickPerMap)
                    return false;

                mapScansThisTick[mapId] = count + 1;
                return true;
            }

            private static Thing FindClosestCorpseByDistance(
                Pawn getter,
                Pawn eater,
                bool allowForbidden,
                bool allowVenerated,
                bool ignoreReservations,
                float? minNutrition,
                ModExtension_IsScavenger scav,
                List<Thing> corpseLister,
                out ThingDef foodDef)
            {
                foodDef = null;
                if (corpseLister == null || corpseLister.Count == 0 || getter == null || eater == null) return null;

                IntVec3 root = getter.Position;
                Corpse best = null;
                int bestDistSq = int.MaxValue;

                for (int i = 0; i < corpseLister.Count; i++)
                {
                    var corpse = corpseLister[i] as Corpse;
                    if (corpse == null) continue;
                    if (corpse.Destroyed || !corpse.Spawned) continue;
                    if (corpse.Bugged) continue;
                    if (corpse.Map != getter.Map) continue;
                    if (corpse.InnerPawn == null) continue;
                    if (!corpse.InnerPawn.RaceProps.IsFlesh) continue;
                    if (!allowForbidden && corpse.IsForbidden(getter)) continue;

                    if (!scav.allowVeryRotten)
                    {
                        var rotComp = corpse.TryGetComp<CompRottable>();
                        if (rotComp != null)
                        {
                            if (rotComp.Stage == RotStage.Dessicated) continue;
                        }
                        else if (corpse.IsDessicated()) continue;
                    }

                    int dx = root.x - corpse.Position.x;
                    int dz = root.z - corpse.Position.z;
                    int distSq = dx * dx + dz * dz;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = corpse;
                    }
                }

                if (best == null) return null;

                ThingDef finalDef;
                try { finalDef = FoodUtility.GetFinalIngestibleDef(best, false); }
                catch { return null; }
                if (finalDef == null || !finalDef.IsNutritionGivingIngestible) return null;

                if (minNutrition.HasValue)
                {
                    float nut = FoodUtility.NutritionForEater(eater, best);
                    if (nut < minNutrition.Value) return null;
                }

                if (!eater.WillEat(finalDef, getter, true, allowVenerated)) return null;

                if (ignoreReservations)
                {
                    if (!getter.CanReach(best, PathEndMode.OnCell, Danger.Some))
                    {
                        return null;
                    }
                }
                else
                {
                    if (!getter.CanReserveAndReach(best, PathEndMode.OnCell, Danger.Some, 1, -1, null, false))
                    {
                        return null;
                    }
                }

                foodDef = finalDef;
                return best;
            }

            private static bool TryUseCachedResult(
                int currentTick,
                Pawn getter,
                Pawn eater,
                bool allowForbidden,
                bool allowVenerated,
                bool ignoreReservations,
                float? minNutrition,
                ModExtension_IsScavenger scav,
                ref ScavengerSearchState state,
                out Thing result,
                out ThingDef foodDef)
            {
                result = null;
                foodDef = null;

                if (state.LastFound == null)
                    return false;
                if (currentTick - state.LastFoundTick > ScavengerCachedResultTicks)
                    return false;

                var corpse = state.LastFound as Corpse;
                if (corpse == null || corpse.Destroyed || !corpse.Spawned)
                {
                    state.LastFound = null;
                    state.LastFoodDef = null;
                    return false;
                }

                if (getter == null || eater == null || corpse.Map != getter.Map)
                {
                    state.LastFound = null;
                    state.LastFoodDef = null;
                    return false;
                }

                if (!allowForbidden && corpse.IsForbidden(getter))
                {
                    return false;
                }

                if (!scav.allowVeryRotten)
                {
                    var rotComp = corpse.TryGetComp<CompRottable>();
                    if (rotComp != null)
                    {
                        if (rotComp.Stage == RotStage.Dessicated) return false;
                    }
                    else if (corpse.IsDessicated()) return false;
                }

                ThingDef finalDef = state.LastFoodDef;
                if (finalDef == null)
                {
                    try { finalDef = FoodUtility.GetFinalIngestibleDef(corpse, false); }
                    catch { finalDef = null; }
                }
                if (finalDef == null || !finalDef.IsNutritionGivingIngestible)
                {
                    state.LastFound = null;
                    state.LastFoodDef = null;
                    return false;
                }

                if (minNutrition.HasValue)
                {
                    float nut = FoodUtility.NutritionForEater(eater, corpse);
                    if (nut < minNutrition.Value) return false;
                }

                if (!eater.WillEat(finalDef, getter, true, allowVenerated)) return false;

                if (!ignoreReservations && !getter.CanReserveAndReach(corpse, PathEndMode.OnCell, Danger.Some, 1, -1, null, false))
                {
                    return false;
                }

                state.LastFoodDef = finalDef;
                result = corpse;
                foodDef = finalDef;
                return true;
            }
        }

        
        [HarmonyPatch(typeof(JobDriver_Ingest), "Notify_Starting")]
        private static class Inner_JobDriver_Ingest_NotifyStarting
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Postfix(JobDriver_Ingest __instance)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;
                    if (__instance == null) return;
                    Pawn pawn = __instance.pawn;
                    if (pawn == null) return;
                    var scav = DefModExtensionCache<ModExtension_IsScavenger>.Get(pawn.def);
                    if (scav == null) return;

                    
                    try
                    {
                        Thing targetThing = null;
                        if (__instance.job != null)
                        {
                            
                            targetThing = __instance.job.targetA.Thing;
                        }
                        ScavengerEatingContext.SetEating(pawn, targetThing);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[Zoology] Error in Notify_Starting SetEating: " + ex);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in JobDriver_Ingest.Notify_Starting postfix: " + e);
                }
            }
        }

        
        [HarmonyPatch(typeof(JobDriver), "EndJobWith")]
        private static class Inner_JobDriver_EndJobWith_ClearEatingContext
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Postfix(JobDriver __instance, JobCondition condition)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;
                    if (__instance == null) return;
                    Job job = __instance.job;
                    if (job == null) return;
                    if (job.def != JobDefOf.Ingest) return;

                    ScavengerEatingContext.Clear(__instance.pawn);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in JobDriver.EndJobWith postfix: " + e);
                }
            }
        }
    }
}
