

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace ZoologyMod.HarmonyPatches
{
    [HarmonyPatch]
    public static class Patch_ScavengeringCore
    {
        
        
        [HarmonyPatch(typeof(Corpse))]
        [HarmonyPatch("IngestibleNow", MethodType.Getter)]
        private static class Inner_Corpse_IngestibleNow
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static bool Prefix(Corpse __instance, ref bool __result)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    
                    if (settings != null && !settings.EnableScavengering) return true;

                    var eater = ScavengerEatingContext.GetEatingPawnForCorpse(__instance);

                    
                    if (eater == null)
                    {
                        return true; 
                    }

                    
                    var scav = eater.def.GetModExtension<ModExtension_IsScavenger>();
                    if (scav == null)
                    {
                        return true;
                    }

                    if (__instance == null)
                    {
                        __result = false;
                        return false;
                    }

                    if (__instance.Bugged)
                    {
                        __result = false;
                        return false;
                    }

                    bool defAllows = __instance.def.IsNutritionGivingIngestible;
                    bool isFlesh = __instance.InnerPawn != null && __instance.InnerPawn.RaceProps.IsFlesh;
                    if (!defAllows || !isFlesh)
                    {
                        __result = false;
                        return false;
                    }

                    var rotComp = __instance.TryGetComp<CompRottable>();
                    RotStage rotStage = rotComp != null ? rotComp.Stage : __instance.GetRotStage();

                    if (rotStage == RotStage.Dessicated && !scav.allowVeryRotten)
                    {
                        __result = false;
                        return false;
                    }

                    __result = true;
                    return false;
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Corpse.IngestibleNow prefix: " + e);
                    return true;
                }
            }
        }

        
        static MethodBase TargetMethod_TryGetIsFreshFactor()
        {
            try
            {
                return AccessTools.Method(
                    typeof(StatPart_IsCorpseFresh),
                    "TryGetIsFreshFactor",
                    new Type[] { typeof(StatRequest), typeof(float).MakeByRefType() }
                );
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Failed resolving TryGetIsFreshFactor: " + e);
                return null;
            }
        }

        static bool Prefix_TryGetIsFreshFactor(StatRequest req, ref float factor)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableScavengering) return true;

                if (!req.HasThing) return true;
                var corpse = req.Thing as Corpse;
                if (corpse == null) return true;

                var eater = ScavengerEatingContext.GetEatingPawnForCorpse(corpse);
                if (eater == null) return true;

                var scav = eater.def.GetModExtension<ModExtension_IsScavenger>();
                if (scav == null) return true;

                var rotComp = corpse.TryGetComp<CompRottable>();
                RotStage stage = rotComp != null ? rotComp.Stage : corpse.GetRotStage();

                if (stage == RotStage.Fresh || stage == RotStage.Rotting)
                {
                    factor = 1f;
                    return false;
                }

                if (stage == RotStage.Dessicated)
                {
                    factor = scav.allowVeryRotten ? 0.1f : 0f;
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in TryGetIsFreshFactor prefix: " + e);
                return true;
            }
        }

        [HarmonyPatch]
        private static class Inner_TryGetIsFreshFactor
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static MethodBase TargetMethod() => TargetMethod_TryGetIsFreshFactor();
            static bool Prefix(StatRequest req, ref float factor) => Prefix_TryGetIsFreshFactor(req, ref factor);
        }

        
        [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.GetBodyPartNutrition), new[] { typeof(Corpse), typeof(BodyPartRecord) })]
        private static class Inner_FoodUtility_GetBodyPartNutrition
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static bool Prefix(Corpse corpse, BodyPartRecord part, ref float __result)
            {
                try
                {
                    if (ZoologyModSettings.Instance != null && !ZoologyModSettings.Instance.EnableScavengering) return true;

                    if (corpse == null || part == null) return true;

                    var eater = ScavengerEatingContext.GetEatingPawnForCorpse(corpse);
                    if (eater == null) return true;

                    var scav = eater.def.GetModExtension<ModExtension_IsScavenger>();
                    if (scav == null) return true;

                    var rotComp = corpse.TryGetComp<CompRottable>();
                    RotStage rotStage = rotComp != null ? rotComp.Stage : corpse.GetRotStage();

                    if (rotStage == RotStage.Dessicated && !scav.allowVeryRotten)
                    {
                        return true; 
                    }

                    float nutritionRaw = corpse.GetStatValue(StatDefOf.Nutrition, false, -1);

                    float adjusted;
                    if (rotStage == RotStage.Dessicated)
                        adjusted = nutritionRaw * 0.1f;
                    else
                        adjusted = nutritionRaw;

                    
                    __result = FoodUtility.GetBodyPartNutrition(adjusted, corpse.InnerPawn, part);
                    return false;
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in GetBodyPartNutrition prefix: " + e);
                    return true;
                }
            }
        }

        
        [HarmonyPatch(typeof(Toils_Ingest), "FinalizeIngest")]
        private static class Inner_ToilsIngest_FinalizeIngest
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Postfix(ref Toil __result, Pawn ingester, TargetIndex ingestibleInd)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;
                    if (__result == null) return;
                    var scav = ingester?.def?.GetModExtension<ModExtension_IsScavenger>();
                    if (scav == null) return; 

                    Action oldInit = __result.initAction;
                    __result.initAction = () =>
                    {
                        try
                        {
                            
                            try
                            {
                                Thing target = null;
                                if (ingester != null && ingester.CurJob != null)
                                {
                                    
                                    target = ingester.CurJob.targetA.Thing;
                                }
                                ScavengerEatingContext.SetEating(ingester, target);
                            }
                            catch (Exception ex) 
                            { 
                                Log.Error("[Zoology] Exception in FinalizeIngest init wrapper (SetEating): " + ex); 
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Zoology] Exception in FinalizeIngest init wrapper (outer): " + ex);
                        }

                        try { oldInit?.Invoke(); }
                        catch (Exception ex) { Log.Error("[Zoology] Exception in FinalizeIngest oldInit: " + ex); }
                    };

                    __result.AddFinishAction(() =>
                    {
                        try
                        {
                            ScavengerEatingContext.Clear(ingester);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Zoology] Exception clearing ScavengerEatingContext in finalize finish: " + ex);
                        }
                    });
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error wrapping FinalizeIngest: " + e);
                }
            }
        }

        
        static MethodBase TargetMethod_IngestedCalculateAmounts()
        {
            try
            {
                return AccessTools.Method(
                    typeof(Corpse),
                    "IngestedCalculateAmounts",
                    new Type[] {
                        typeof(Pawn),
                        typeof(float),
                        typeof(int).MakeByRefType(),
                        typeof(float).MakeByRefType()
                    }
                );
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Failed resolving Corpse.IngestedCalculateAmounts: " + e);
                return null;
            }
        }

        static void Prefix_IngestedCalculateAmounts(Pawn ingester)
        {
            try
            {
                
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in Prefix(IngestedCalculateAmounts): " + e);
            }
        }

        static void Postfix_IngestedCalculateAmounts(Pawn ingester)
        {
            try
            {
                
                ScavengerEatingContext.Clear(ingester);
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in Postfix(IngestedCalculateAmounts): " + e);
            }
        }

        [HarmonyPatch]
        private static class Inner_IngestedCalculateAmounts
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static MethodBase TargetMethod() => TargetMethod_IngestedCalculateAmounts();
            static void Prefix(Pawn ingester) => Prefix_IngestedCalculateAmounts(ingester);
            static void Postfix(Pawn ingester) => Postfix_IngestedCalculateAmounts(ingester);
        }
    }
}
