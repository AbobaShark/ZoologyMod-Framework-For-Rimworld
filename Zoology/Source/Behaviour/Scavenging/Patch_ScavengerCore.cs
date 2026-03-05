// Patch_ScavengerCore.cs

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
        // --- Corpse.IngestibleNow getter: если для этого трупа у нас записан scavenger, и он реально ест этот труп,
        // то разрешаем гнилые/не-fresh стадии для него.
        [HarmonyPatch(typeof(Corpse))]
        [HarmonyPatch("IngestibleNow", MethodType.Getter)]
        private static class Inner_Corpse_IngestibleNow
        {
            static bool Prefix(Corpse __instance, ref bool __result)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    // Если опция выключена — позволяем ваниле полностью выполнять логику.
                    if (settings != null && !settings.EnableScavengering) return true;

                    var eater = ScavengerEatingContext.GetEatingPawnForCorpse(__instance);

                    // Если контекст пуст — ваниль
                    if (eater == null)
                    {
                        return true; // ваниль
                    }

                    // Теперь eater гарантированно тот pawn, который в CurJob Ingest с target == __instance
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

        // --- StatPart_IsCorpseFresh.TryGetIsFreshFactor
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
            static MethodBase TargetMethod() => TargetMethod_TryGetIsFreshFactor();
            static bool Prefix(StatRequest req, ref float factor) => Prefix_TryGetIsFreshFactor(req, ref factor);
        }

        // --- FoodUtility.GetBodyPartNutrition (только для текущего едящего падальщика)
        [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.GetBodyPartNutrition), new[] { typeof(Corpse), typeof(BodyPartRecord) })]
        private static class Inner_FoodUtility_GetBodyPartNutrition
        {
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
                        return true; // vanilla
                    }

                    float nutritionRaw = corpse.GetStatValue(StatDefOf.Nutrition, false, -1);

                    float adjusted;
                    if (rotStage == RotStage.Dessicated)
                        adjusted = nutritionRaw * 0.1f;
                    else
                        adjusted = nutritionRaw;

                    // вызов оригинальной логики с уже скорректированным nutrition
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

        // --- Toils_Ingest.FinalizeIngest обёртка: гарантированно выставить/очистить локальный контекст (только для scav)
        [HarmonyPatch(typeof(Toils_Ingest), "FinalizeIngest")]
        private static class Inner_ToilsIngest_FinalizeIngest
        {
            static void Postfix(ref Toil __result, Pawn ingester, TargetIndex ingestibleInd)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;
                    if (__result == null) return;
                    var scav = ingester?.def?.GetModExtension<ModExtension_IsScavenger>();
                    if (scav == null) return; // только для падальщиков

                    Action oldInit = __result.initAction;
                    __result.initAction = () =>
                    {
                        try
                        {
                            // Устанавливаем явную пару pawn -> target (взяли текущий target из CurJob, если есть)
                            try
                            {
                                Thing target = null;
                                if (ingester != null && ingester.CurJob != null)
                                {
                                    // CurJob.targetA — LocalTargetInfo (struct). Берём .Thing напрямую.
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

        // --- IngestedCalculateAmounts wrapper: для безопасности (контекст установлен в FinalizeIngest или Notify_Starting)
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
                // Do not create context here — it should already be created in FinalizeIngest.initAction or Notify_Starting.
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
                // Если по какой-то причине контекст остался — аккуратно сбросим именно этого pawn'а.
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
            static MethodBase TargetMethod() => TargetMethod_IngestedCalculateAmounts();
            static void Prefix(Pawn ingester) => Prefix_IngestedCalculateAmounts(ingester);
            static void Postfix(Pawn ingester) => Postfix_IngestedCalculateAmounts(ingester);
        }
    }
}
