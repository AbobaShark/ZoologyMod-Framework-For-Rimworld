using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Only configured animal melee attacks use this stat. No global tick hook.
    [DefOf]
    internal static class ZoologyLifeStageStatDefOf
    {
        public static StatDef Zoology_MeleeCooldownFactor;

        static ZoologyLifeStageStatDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ZoologyLifeStageStatDefOf));
        }
    }

    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.AdjustedCooldown),
        new Type[] { typeof(Tool), typeof(Pawn), typeof(Thing) })]
    internal static class Patch_AnimalLifeStageMeleeCooldown
    {
        private static bool Prepare()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || !settings.DisableAllRuntimePatches;
        }

        private static void Postfix(Tool tool, Pawn attacker, ref float __result)
        {
            if (tool == null || attacker == null || !attacker.RaceProps.Animal)
            {
                return;
            }

            var stage = attacker.ageTracker?.CurLifeStage;
            if (stage == null)
            {
                return;
            }

            StatDef stat = ZoologyLifeStageStatDefOf.Zoology_MeleeCooldownFactor;
            if (stat != null)
            {
                // Common stage factor; individual tool corrections follow.
                float factor = attacker.GetStatValue(stat);
                if (factor > 0f && !float.IsInfinity(factor) && !float.IsNaN(factor))
                {
                    __result *= factor;
                }
            }

            ZoologyLifeStageToolFactor toolFactor;
            if (ZoologyLifeStageProfileInstaller.TryGetToolFactor(attacker, tool, out toolFactor))
            {
                __result *= CEChecker.IsCEInstalled()
                    ? toolFactor.ceCooldownFactor
                    : toolFactor.vanillaCooldownFactor;
            }
        }
    }


    // Only natural tools whose source Tool references match the profile are
    // modified. Equipped weapons and hediff verbs are untouched.
    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.AdjustedMeleeDamageAmount),
        new Type[] { typeof(Tool), typeof(Pawn), typeof(Thing), typeof(HediffComp_VerbGiver) })]
    internal static class Patch_AnimalLifeStageToolDamage
    {
        private static bool Prepare()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || !settings.DisableAllRuntimePatches;
        }

        private static void Postfix(Tool tool, Pawn attacker, ref float __result)
        {
            ZoologyLifeStageToolFactor factor;
            if (ZoologyLifeStageProfileInstaller.TryGetToolFactor(attacker, tool, out factor))
            {
                __result *= CEChecker.IsCEInstalled()
                    ? factor.ceDamageFactor
                    : factor.vanillaDamageFactor;
            }
        }
    }
}
