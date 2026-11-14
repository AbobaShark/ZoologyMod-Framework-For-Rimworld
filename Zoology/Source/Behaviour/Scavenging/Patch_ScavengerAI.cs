using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using ZoologyMod;

namespace ZoologyMod.HarmonyPatches
{
    public static class Patch_ScavengeringAI
    {
        private const int ScavengerSearchCooldownTicks = ZoologyTickLimiter.Scavenging.SearchCooldownTicks;
        private const int ScavengerSearchCooldownFailTicks = ZoologyTickLimiter.Scavenging.SearchCooldownFailTicks;
        private const int ScavengerMaxScansPerTickPerMap = ZoologyTickLimiter.Scavenging.MaxScansPerTickPerMap;

        private struct ScavengerSearchState
        {
            public int LastSearchTick;
            public Thing LastFound;
            public ThingDef LastFoodDef;
            public bool LastSearchHadResult;
        }

        private static readonly Dictionary<int, ScavengerSearchState> scavengerSearchStates = new Dictionary<int, ScavengerSearchState>(256);
        private static readonly Dictionary<int, int> mapScansThisTick = new Dictionary<int, int>(8);
        private static int mapScansTick = -1;
        private static Game runtimeStateGame;
        private static int runtimeStateLastTick = -1;

        private static void EnsureRuntimeState(int currentTick)
        {
            Game currentGame = Current.Game;
            bool gameChanged = !ReferenceEquals(runtimeStateGame, currentGame);
            bool tickRewound = currentTick > 0 && runtimeStateLastTick > 0 && currentTick < runtimeStateLastTick;
            if (gameChanged || tickRewound)
            {
                scavengerSearchStates.Clear();
                mapScansThisTick.Clear();
                mapScansTick = -1;
                runtimeStateGame = currentGame;
            }

            if (currentTick > 0)
            {
                runtimeStateLastTick = currentTick;
            }
        }

        
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
                    if (eater == null || getter == null || !allowCorpse) return;

                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(eater, out ModExtension_IsScavenger scav)) return; 
                    int currentTick = Find.TickManager?.TicksGame ?? 0;
                    EnsureRuntimeState(currentTick);

                    int pawnId = eater.thingIDNumber;
                    if (!scavengerSearchStates.TryGetValue(pawnId, out ScavengerSearchState state))
                    {
                        state = new ScavengerSearchState
                        {
                            LastSearchTick = -999999,
                            LastFound = null,
                            LastFoodDef = null,
                            LastSearchHadResult = false
                        };
                    }

                    // A cached corpse is authoritative only while it is still
                    // edible, permitted and reachable. Refreshing the index
                    // must never introduce a gap between the positive-cache
                    // lifetime and the successful-search cooldown.
                    Thing previous = state.LastFound;
                    bool hasCached = TryUseCachedResult(getter, eater, allowForbidden,
                        allowVenerated, ignoreReservations, minNutrition, scav, ref state,
                        out Thing cached, out ThingDef cachedDef);
                    if (!hasCached && previous != null && state.LastFound == null)
                    {
                        // A previously usable corpse became unavailable.
                        // Search for an alternative on the next budgeted pass.
                        state.LastSearchTick = -999999;
                    }

                    int cooldown = state.LastSearchHadResult
                        ? ScavengerSearchCooldownTicks : ScavengerSearchCooldownFailTicks;
                    if (currentTick - state.LastSearchTick < cooldown)
                    {
                        if (hasCached)
                        {
                            __result = cached;
                            foodDef = cachedDef;
                        }
                        scavengerSearchStates[pawnId] = state;
                        return;
                    }

                    var corpseLister = getter.Map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                    if (corpseLister == null || corpseLister.Count == 0)
                    {
                        state.LastSearchTick = currentTick;
                        state.LastSearchHadResult = false;
                        state.LastFound = null;
                        state.LastFoodDef = null;
                        scavengerSearchStates[pawnId] = state;
                        return;
                    }

                    if (!TryConsumeMapScanBudget(getter.Map, currentTick))
                    {
                        // The 2-scans-per-map budget is not an indication that
                        // a previously validated corpse has stopped being food.
                        if (hasCached)
                        {
                            __result = cached;
                            foodDef = cachedDef;
                        }
                        scavengerSearchStates[pawnId] = state;
                        return;
                    }

                    state.LastSearchTick = currentTick;
                    Thing found = FindClosestCorpseByDistance(getter, eater,
                        allowForbidden, allowVenerated, ignoreReservations,
                        minNutrition, scav, corpseLister, out ThingDef foundDef);

                    if (found != null)
                    {
                        __result = found;
                        foodDef = foundDef;
                        state.LastFound = found;
                        state.LastFoodDef = foundDef;
                        state.LastSearchHadResult = true;
                        scavengerSearchStates[pawnId] = state;
                        return;
                    }

                    // A failed refresh does not invalidate an independently
                    // revalidated cached corpse (e.g. a transient path query).
                    if (hasCached)
                    {
                        __result = cached;
                        foodDef = cachedDef;
                        state.LastFound = cached;
                        state.LastFoodDef = cachedDef;
                        state.LastSearchHadResult = true;
                    }
                    else
                    {
                        state.LastSearchHadResult = false;
                        state.LastFound = null;
                        state.LastFoodDef = null;
                    }
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

            // Keep pathfinding bounded: only the closest plausible corpses
            // reach the expensive reservation/path check.
            private const int MaxReachabilityCandidates = 6;

            private static bool TryGetScavengerCorpseFoodDef(
                Pawn getter, Pawn eater, Corpse corpse,
                bool allowForbidden, bool allowVenerated, float? minNutrition,
                ModExtension_IsScavenger scav, bool cannotChew,
                out ThingDef finalDef)
            {
                finalDef = null;
                if (corpse == null || !corpse.Spawned || corpse.Destroyed
                    || corpse.Bugged || corpse.Map != getter.Map
                    || corpse.InnerPawn?.RaceProps?.IsFlesh != true
                    || (cannotChew && CannotChewUtility.IsCorpseTooLarge(eater, corpse))
                    || (!allowForbidden && corpse.IsForbidden(getter)))
                    return false;

                if (!scav.allowVeryRotten)
                {
                    CompRottable rot = corpse.TryGetComp<CompRottable>();
                    if (rot != null ? rot.Stage == RotStage.Dessicated : corpse.IsDessicated())
                        return false;
                }

                try
                {
                    finalDef = FoodUtility.GetFinalIngestibleDef(corpse, false);
                    if (finalDef == null || !finalDef.IsNutritionGivingIngestible)
                        return false;
                    if (minNutrition.HasValue
                        && FoodUtility.NutritionForEater(eater, corpse) < minNutrition.Value)
                        return false;
                    // The Thing overload enforces ownership and all other object-specific
                    // food restrictions, which are absent from the ThingDef overload.
                    if (PredationHarmonyPatches.ShouldBlockGuardedCorpseConsumptionForFoodPatch(eater, corpse))
                        return false;
                    return eater.WillEat(corpse, getter, true, allowVenerated);
                }
                catch
                {
                    finalDef = null;
                    return false;
                }
            }

            private static bool CanUseCorpse(Pawn getter, Corpse corpse, bool ignoreReservations)
            {
                return ignoreReservations
                    ? getter.CanReach(corpse, PathEndMode.OnCell, Danger.Some)
                    : getter.CanReserveAndReach(corpse, PathEndMode.OnCell,
                        Danger.Some, 1, -1, null, false);
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
                if (corpseLister == null || corpseLister.Count == 0
                    || getter == null || eater == null || scav == null)
                    return null;

                // One bounded search, ranked by exactly the same FoodOptimality
                // used by the general predator food chooser. This ensures that
                // starvation does not make guarded corpses outrank free ones.
                Corpse[] candidates = new Corpse[MaxReachabilityCandidates];
                ThingDef[] foodDefs = new ThingDef[MaxReachabilityCandidates];
                float[] scores = new float[MaxReachabilityCandidates];
                int count = 0;
                bool cannotChew = CannotChewUtility.HasCannotChew(eater);
                IntVec3 root = getter.Position;

                for (int i = 0; i < corpseLister.Count; i++)
                {
                    if (corpseLister[i] is not Corpse corpse)
                        continue;
                    if (!TryGetScavengerCorpseFoodDef(getter, eater, corpse,
                        allowForbidden, allowVenerated, minNutrition, scav,
                        cannotChew, out ThingDef candidateDef))
                        continue;

                    float distance = (root - corpse.Position).LengthHorizontal;
                    float score = FoodUtility.FoodOptimality(eater, corpse, candidateDef, distance);
                    if (float.IsNaN(score) || (count == MaxReachabilityCandidates && score <= scores[count - 1]))
                        continue;

                    int position = count < MaxReachabilityCandidates
                        ? count : MaxReachabilityCandidates - 1;
                    while (position > 0 && score > scores[position - 1])
                    {
                        candidates[position] = candidates[position - 1];
                        foodDefs[position] = foodDefs[position - 1];
                        scores[position] = scores[position - 1];
                        position--;
                    }
                    candidates[position] = corpse;
                    foodDefs[position] = candidateDef;
                    scores[position] = score;
                    if (count < MaxReachabilityCandidates) count++;
                }

                for (int i = 0; i < count; i++)
                {
                    if (!CanUseCorpse(getter, candidates[i], ignoreReservations))
                        continue;
                    foodDef = foodDefs[i];
                    return candidates[i];
                }
                return null;
            }

            private static bool TryUseCachedResult(
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

                if (getter == null || eater == null || scav == null
                    || state.LastFound is not Corpse corpse
                    || !TryGetScavengerCorpseFoodDef(getter, eater, corpse,
                        allowForbidden, allowVenerated, minNutrition, scav,
                        CannotChewUtility.HasCannotChew(eater), out ThingDef currentDef)
                    || !CanUseCorpse(getter, corpse, ignoreReservations))
                {
                    state.LastFound = null;
                    state.LastFoodDef = null;
                    return false;
                }

                state.LastFoodDef = currentDef;
                result = corpse;
                foodDef = currentDef;
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
                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out ModExtension_IsScavenger scav)) return;

                    
                    try
                    {
                        Thing targetThing = null;
                        if (__instance.job != null)
                        {
                            
                            targetThing = __instance.job.targetA.Thing;
                        }
                        ScavengerEatingContext.SetEating(pawn, targetThing);
                        ScavengerEatingContext.SetForceIngestible(pawn, targetThing);
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
                    ScavengerEatingContext.ClearHandFeeding(__instance.pawn);
                    ScavengerEatingContext.ClearForceIngestible(__instance.pawn);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in JobDriver.EndJobWith postfix: " + e);
                }
            }
        }
    }
}
