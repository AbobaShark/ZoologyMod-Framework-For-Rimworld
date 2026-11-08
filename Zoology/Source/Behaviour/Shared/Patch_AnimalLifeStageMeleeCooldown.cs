using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.AdjustedCooldown),
        new Type[] { typeof(Tool), typeof(Pawn), typeof(Thing) })]
    internal static class Patch_AnimalLifeStageMeleeCooldown
    {
        private static bool Prepare()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || !settings.DisableAllRuntimePatches;
        }

        private static void Postfix(Tool tool, Pawn attacker, Thing equipment, ref float __result)
        {
            if (tool == null || attacker?.RaceProps?.Animal != true
                || (equipment != null && equipment != attacker))
            {
                return;
            }

            // Never apply a blanket cooldown to all natural or equipped tools.
            // Species/tool coefficients are absolute stage-to-adult ratios,
            // not residual multipliers on a second generic age factor.
            bool ce = CEChecker.IsCEInstalled();
            float interpolated;
            if (ZoologyAnimalGrowth.TryGetToolFactor(attacker, tool,
                    ce ? ZoologyAnimalGrowth.GrowthToolFactor.CeCooldown
                       : ZoologyAnimalGrowth.GrowthToolFactor.VanillaCooldown,
                    out interpolated))
            {
                __result *= interpolated;
                return;
            }

            ZoologyLifeStageToolFactor toolFactor;
            if (ZoologyLifeStageProfileInstaller.TryGetToolFactor(attacker, tool, out toolFactor))
                __result *= ce ? toolFactor.ceCooldownFactor : toolFactor.vanillaCooldownFactor;
        }
    }


    // Apply only to natural attacks, never equipped weapons or hediff verbs.
    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.AdjustedMeleeDamageAmount),
        new Type[] { typeof(Tool), typeof(Pawn), typeof(Thing), typeof(HediffComp_VerbGiver) })]
    internal static class Patch_AnimalLifeStageToolDamage
    {
        private static bool Prepare()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || !settings.DisableAllRuntimePatches;
        }

        private static void Postfix(Tool tool, Pawn attacker, Thing equipment,
            HediffComp_VerbGiver hediffCompSource, ref float __result)
        {
            if (attacker?.RaceProps?.Animal != true || hediffCompSource != null
                || (equipment != null && equipment != attacker))
                return;

            bool ce = CEChecker.IsCEInstalled();
            float interpolated;
            if (ZoologyAnimalGrowth.TryGetToolFactor(attacker, tool,
                    ce ? ZoologyAnimalGrowth.GrowthToolFactor.CeDamage
                       : ZoologyAnimalGrowth.GrowthToolFactor.VanillaDamage,
                    out interpolated))
            {
                __result *= interpolated;
                return;
            }

            ZoologyLifeStageToolFactor factor;
            if (ZoologyLifeStageProfileInstaller.TryGetToolFactor(attacker, tool, out factor))
                __result *= ce ? factor.ceDamageFactor : factor.vanillaDamageFactor;
        }
    }
}
