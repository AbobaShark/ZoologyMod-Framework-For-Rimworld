using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace ZoologyMod.HarmonyPatches
{
    public static class Patch_ScavengeringAI
    {
        private static readonly ThingRequest FoodSourceRequest = ThingRequest.ForGroup(ThingRequestGroup.FoodSource);

        
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
                    if (!allowCorpse) return;

                    var scav = DefModExtensionCache<ModExtension_IsScavenger>.Get(eater.def);
                    if (scav == null) return; 

                    Predicate<Thing> validator = t =>
                    {
                        var corpse = t as Corpse;
                        if (corpse == null) return false;
                        if (corpse.InnerPawn == null) return false;
                        if (corpse.Map == null) return false;

                        ThingDef finalDef;
                        try { finalDef = FoodUtility.GetFinalIngestibleDef(corpse, false); }
                        catch { return false; }
                        if (finalDef == null) return false;

                        if (minNutrition.HasValue)
                        {
                            float nut = FoodUtility.NutritionForEater(eater, corpse);
                            if (nut < minNutrition.Value) return false;
                        }

                        if (!eater.WillEat(finalDef, getter, true, allowVenerated)) return false;
                        if (!finalDef.IsNutritionGivingIngestible) return false;
                        if (!allowForbidden && t.IsForbidden(getter)) return false;

                        if (!scav.allowVeryRotten)
                        {
                            var rotComp = t.TryGetComp<CompRottable>();
                            if (rotComp != null)
                            {
                                if (rotComp.Stage == RotStage.Dessicated) return false;
                            }
                            else if (t.IsDessicated()) return false;
                        }

                        if (!ignoreReservations)
                        {
                            
                            if (!getter.CanReserveAndReach(t, PathEndMode.OnCell, Danger.Some, 1, -1, null, false)) return false;
                        }

                        if (!getter.Map.reachability.CanReachNonLocal(getter.Position, new TargetInfo(t.PositionHeld, t.Map, false),
                            PathEndMode.OnCell, TraverseParms.For(getter, Danger.Some, TraverseMode.ByPawn, false, false, false, true)))
                        {
                            return false;
                        }

                        return true;
                    };

                    int maxRegionsToScan = GetMaxRegionsToScan_Local(getter, forceScanWholeMap);

                    Thing found = GenClosest.ClosestThingReachable(getter.Position, getter.Map, FoodSourceRequest,
                        PathEndMode.OnCell, TraverseParms.For(getter, Danger.Deadly, TraverseMode.ByPawn, false, false, false, true),
                        9999f, validator, null, 0, maxRegionsToScan, false, RegionType.Set_Passable, false, false);

                    if (found != null)
                    {
                        if (!ignoreReservations && !getter.CanReserveAndReach(found, PathEndMode.OnCell, Danger.Some, 1, -1, null, false))
                        {
                            return;
                        }

                        var corp = found as Corpse;
                        var fd = FoodUtility.GetFinalIngestibleDef(found, false);

                        
                        __result = found;
                        foodDef = fd;
                    }
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in BestFoodSourceOnMap postfix: " + e);
                }
            }

            private static int GetMaxRegionsToScan_Local(Pawn getter, bool forceScanWholeMap)
            {
                if (getter.RaceProps.Humanlike) return -1;
                if (forceScanWholeMap) return -1;
                if (getter.Faction == Faction.OfPlayer)
                {
                    if (getter.Roamer && AnimalPenUtility.GetFixedAnimalFilter().Allows(getter))
                    {
                        CompAnimalPenMarker currentPenOf = AnimalPenUtility.GetCurrentPenOf(getter, false);
                        if (currentPenOf != null)
                            return Mathf.Min(currentPenOf.PenState.ConnectedRegions.Count, 100);
                    }
                    return 100;
                }
                return 30;
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
