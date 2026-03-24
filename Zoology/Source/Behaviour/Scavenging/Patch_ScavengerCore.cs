using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using ZoologyMod;

namespace ZoologyMod.HarmonyPatches
{
    [HarmonyPatch]
    public static class Patch_ScavengeringCore
    {
        private static Pawn TryGetScavengerEater(Corpse corpse)
        {
            if (corpse == null) return null;

            Pawn eater = ScavengerEatingContext.GetEatingPawnForCorpse(corpse);
            if (eater != null) return eater;

            try
            {
                Map map = corpse.Map;
                if (map == null || !corpse.Spawned) return null;

                IntVec3 pos = corpse.Position;
                for (int i = 0; i < GenAdj.AdjacentCellsAndInside.Length; i++)
                {
                    IntVec3 c = pos + GenAdj.AdjacentCellsAndInside[i];
                    if (!c.InBounds(map)) continue;
                    var list = map.thingGrid.ThingsListAtFast(c);
                    for (int j = 0; j < list.Count; j++)
                    {
                        if (list[j] is Pawn p)
                        {
                            if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(p, out _)) continue;
                            Job cj = p.CurJob;
                            if (cj == null || cj.def != JobDefOf.Ingest) continue;
                            if (cj.targetA.Thing == corpse || cj.targetB.Thing == corpse || cj.targetC.Thing == corpse)
                            {
                                ScavengerEatingContext.SetEating(p, corpse);
                                return p;
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }
        
        
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

                    var eater = TryGetScavengerEater(__instance);

                    
                    if (eater == null)
                    {
                        return true; 
                    }

                    
                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(eater, out ModExtension_IsScavenger scav))
                    {
                        return true;
                    }
                    if (scav == null)
                    {
                        return true;
                    }

                    if (CannotChewUtility.HasCannotChew(eater)
                        && CannotChewUtility.IsCorpseTooLarge(eater, __instance))
                    {
                        __result = false;
                        return false;
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

                var eater = TryGetScavengerEater(corpse);
                if (eater == null) return true;

                if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(eater, out ModExtension_IsScavenger scav)) return true;

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

                    var eater = TryGetScavengerEater(corpse);
                    if (eater == null) return true;

                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(eater, out ModExtension_IsScavenger scav)) return true;

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
                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(ingester, out ModExtension_IsScavenger scav)) return; 

                    Toil toil = __result;
                    Action oldInit = toil.initAction;
                    toil.initAction = () =>
                    {
                        try
                        {
                            
                            try
                            {
                                Thing target = null;
                                Pawn actor = toil.actor;
                                Job actorJob = actor?.CurJob;
                                target = TryGetCorpseTarget(actorJob, ingestibleInd);

                                if (target == null && ingester != null)
                                {
                                    Job ingesterJob = ingester.CurJob;
                                    target = TryGetCorpseTarget(ingesterJob, ingestibleInd);
                                }

                                ScavengerEatingContext.SetEating(ingester, target);
                                if (!ReferenceEquals(actor, ingester))
                                {
                                    ScavengerEatingContext.SetHandFeeding(ingester, target);
                                }
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

                    toil.AddFinishAction(() =>
                    {
                        try
                        {
                            ScavengerEatingContext.Clear(ingester);
                            ScavengerEatingContext.ClearHandFeeding(ingester);
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
            static void Postfix(Pawn ingester) => Postfix_IngestedCalculateAmounts(ingester);
        }

        
        [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.ChewIngestible))]
        private static class Inner_ToilsIngest_ChewIngestible
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Postfix(ref Toil __result, Pawn chewer, float durationMultiplier, TargetIndex ingestibleInd, TargetIndex eatSurfaceInd)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;
                    if (__result == null || chewer == null) return;

                    Toil toil = __result;
                    Action oldInit = toil.initAction;
                    toil.initAction = () =>
                    {
                        try
                        {
                            Thing target = null;
                            Pawn actor = toil.actor;
                            Job actorJob = actor?.CurJob;
                            target = TryGetCorpseTarget(actorJob, ingestibleInd);

                            if (target == null && chewer != null)
                            {
                                Job chewerJob = chewer.CurJob;
                                target = TryGetCorpseTarget(chewerJob, ingestibleInd);
                            }

                            ScavengerEatingContext.SetEating(chewer, target);
                            if (!ReferenceEquals(actor, chewer))
                            {
                                ScavengerEatingContext.SetHandFeeding(chewer, target);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Zoology] Exception in ChewIngestible init wrapper (SetEating): " + ex);
                        }

                        try { oldInit?.Invoke(); }
                        catch (Exception ex) { Log.Error("[Zoology] Exception in ChewIngestible oldInit: " + ex); }
                    };

                    toil.AddFinishAction(() =>
                    {
                        try
                        {
                            ScavengerEatingContext.Clear(chewer);
                            ScavengerEatingContext.ClearHandFeeding(chewer);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[Zoology] Exception clearing ScavengerEatingContext in chew finish: " + ex);
                        }
                    });
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error wrapping ChewIngestible: " + e);
                }
            }
        }

        
        [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested), new[] { typeof(Pawn), typeof(float) })]
        private static class Inner_Thing_Ingested
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Prefix(Thing __instance, Pawn ingester, float nutritionWanted)
            {
                try
                {
                    if (ingester == null) return;
                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(ingester, out ModExtension_IsScavenger scav)) return;

                    ScavengerEatingContext.SetForceIngestible(ingester, __instance);
                    ScavengerEatingContext.SetEating(ingester, __instance);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Thing.Ingested prefix: " + e);
                }
            }

            static void Postfix(Thing __instance, Pawn ingester, float nutritionWanted)
            {
                try
                {
                    ScavengerEatingContext.Clear(ingester);
                    ScavengerEatingContext.ClearForceIngestible(ingester);
                    ScavengerEatingContext.ClearHandFeeding(ingester);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Thing.Ingested postfix: " + e);
                }
            }
        }

        private static Thing TryGetCorpseTarget(Job job, TargetIndex ingestibleInd)
        {
            if (job == null) return null;

            Thing target = null;
            try
            {
                target = job.GetTarget(ingestibleInd).Thing;
            }
            catch
            {
                target = null;
            }

            if (target is Corpse) return target;

            if (job.targetA.Thing is Corpse) return job.targetA.Thing;
            if (job.targetB.Thing is Corpse) return job.targetB.Thing;
            if (job.targetC.Thing is Corpse) return job.targetC.Thing;

            return target;
        }
    }
}
