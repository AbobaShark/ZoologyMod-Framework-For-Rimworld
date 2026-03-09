using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod.HarmonyPatches
{
    [HarmonyPatch]
    public static class Patch_ScavengerImmunity
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> HealthTrackerPawnRef =
            AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        private static bool IsScavenger(Pawn pawn)
        {
            return pawn?.RaceProps?.Animal == true
                && pawn.def != null
                && ZoologyCacheUtility.HasScavengerExtension(pawn.def);
        }

        [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.AddFoodPoisoningHediff))]
        private static class Inner_AddFoodPoisoningHediff
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static bool Prefix(Pawn pawn, Thing ingestible, FoodPoisonCause cause)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering)
                    {
                        return true;
                    }

                    if (pawn == null || ingestible == null)
                    {
                        return true;
                    }

                    if (cause != FoodPoisonCause.Rotten || !(ingestible is Corpse))
                    {
                        return true;
                    }

                    return !IsScavenger(pawn);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in AddFoodPoisoningHediff prefix: " + e);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(HediffDef), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
        private static class Inner_PawnHealthTracker_AddHediffDef
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static bool Prefix(Pawn_HealthTracker __instance, HediffDef def)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering)
                    {
                        return true;
                    }

                    if (def != HediffDefOf.LungRotExposure)
                    {
                        return true;
                    }

                    Pawn pawn = HealthTrackerPawnRef(__instance);
                    return !IsScavenger(pawn);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Pawn_HealthTracker.AddHediff(HediffDef) prefix: " + e);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
        private static class Inner_PawnHealthTracker_AddHediff
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static bool Prefix(Pawn_HealthTracker __instance, Hediff hediff)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering)
                    {
                        return true;
                    }

                    if (hediff?.def != HediffDefOf.LungRotExposure)
                    {
                        return true;
                    }

                    Pawn pawn = HealthTrackerPawnRef(__instance);
                    return !IsScavenger(pawn);
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Pawn_HealthTracker.AddHediff(Hediff) prefix: " + e);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup), new[] { typeof(Map), typeof(bool) })]
        private static class Inner_Pawn_SpawnSetup
        {
            static bool Prepare()
            {
                var s = ZoologyModSettings.Instance;
                return s == null || s.EnableScavengering;
            }

            static void Postfix(Pawn __instance)
            {
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering)
                    {
                        return;
                    }

                    if (!IsScavenger(__instance))
                    {
                        return;
                    }

                    Hediff hediff = __instance.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.LungRotExposure, false);
                    if (hediff != null)
                    {
                        __instance.health.RemoveHediff(hediff);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in Pawn.SpawnSetup postfix: " + e);
                }
            }
        }
    }
}
