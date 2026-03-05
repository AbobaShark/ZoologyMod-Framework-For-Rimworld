// Patch_ScavengerImmunity.cs

using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod.HarmonyPatches
{
    [HarmonyPatch]
    public static class Patch_ScavengerImmunity
    {
        // ---------------------------
        // 1) Block food poisoning when pawn (scavenger) eats a Corpse AND cause == Rotten
        // ---------------------------
        [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.AddFoodPoisoningHediff))]
        private static class Inner_AddFoodPoisoningHediff
        {
            static bool Prefix(Pawn pawn, Thing ingestible, FoodPoisonCause cause)
            {
                try
                {
                    // Respect settings: если опция выключена — не вмешиваемся (ваниль).
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return true;

                    if (pawn == null || ingestible == null) return true;

                    // Блокируем ТОЛЬКО пищевое отравление, вызванное гниением (Rotten) от трупа,
                    // и только для падальщиков (ModExtension IsScavenger).
                    if (cause != FoodPoisonCause.Rotten) return true;
                    if (!(ingestible is Corpse)) return true;

                    var scav = pawn.def?.GetModExtension<ModExtension_IsScavenger>();
                    if (scav == null) return true;

                    // Падальщик + источник — труп + причина Rotten => блокируем ванильное добавление FoodPoisoning.
                    return false;
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in AddFoodPoisoningHediff prefix: " + e);
                    return true;
                }
            }
        }

        // ---------------------------
        // 2) Remove LungRotExposure caused by RotStink (gas) for scavengers
        // ---------------------------
        [HarmonyPatch(typeof(GasUtility), nameof(GasUtility.PawnGasEffectsTickInterval))]
        private static class Inner_PawnGasEffectsTickInterval
        {
            static void Postfix(Pawn pawn, int delta)
            {
                try
                {
                    // Respect settings: если опция выключена — не вмешиваемся (ваниль).
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && !settings.EnableScavengering) return;

                    if (pawn == null) return;
                    var scav = pawn.def?.GetModExtension<ModExtension_IsScavenger>();
                    if (scav == null) return;
                    if (pawn.Map == null) return;
                    if (pawn.Position.GasDensity(pawn.Map, GasType.RotStink) <= 0) return;

                    var hediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.LungRotExposure, false);
                    if (hediff != null)
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("[Zoology] Error in PawnGasEffectsTickInterval postfix: " + e);
                }
            }
        }
    }
}