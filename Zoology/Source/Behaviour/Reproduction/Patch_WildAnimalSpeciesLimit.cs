using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Vanilla calls CommonalityOfAnimalNow only when choosing a random
    // wild kind for a natural map spawn. Filtering its weight preserves the
    // original biome, pollution, coastal, mutator and group-size selection.
    // Explicitly supplied kinds (incidents, quests, etc.) are not changed.
    [HarmonyPatch]
    internal static class Patch_NaturalWildAnimalSpawnCommonalitySpeciesLimit
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(WildAnimalSpawner),
                "CommonalityOfAnimalNow",
                new[] { typeof(PawnKindDef), typeof(IntVec3) });
            return method?.ReturnType == typeof(float) ? method : null;
        }

        private static bool Prepare()
        {
            // Do not install this Harmony hook at all when the user has
            // disabled the species cap. Settings changes rebuild the patches.
            if (!WildAnimalEcosystemUtility.IsSpeciesLimitActive(ModConstants.Settings))
            {
                return false;
            }

            if (TargetMethod() != null)
            {
                return true;
            }

            Log.WarningOnce(
                "[Zoology] Cannot find WildAnimalSpawner.CommonalityOfAnimalNow(PawnKindDef, IntVec3). " +
                "The wild species cap will not filter natural spawn weights on this RimWorld version.",
                192751483);
            return false;
        }

        private static void Postfix(Map ___map, PawnKindDef def, ref float __result)
        {
            ZoologyModSettings settings = ModConstants.Settings;
            if (__result <= 0f || def?.race == null || ___map == null
                || !WildAnimalEcosystemUtility.IsSpeciesLimitActive(settings))
            {
                return;
            }

            var counts = WildAnimalEcosystemUtility.GetLiveWildSpeciesCounts(___map);
            if (counts.TryGetValue(def.race, out int count)
                && count >= settings.WildAnimalSpeciesLimit)
            {
                __result = 0f;
            }
        }
    }

    // Covers vanilla mating as well as jobs from mods which call PawnUtility.Mated
    // without using Zoology's custom wild-mate selection.
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.Mated))]
    internal static class Patch_WildAnimalSpeciesLimit_Mated
    {
        private static bool Prepare()
        {
            ZoologyModSettings settings = ModConstants.Settings;
            return settings != null && !settings.DisableAllRuntimePatches
                && (settings.LimitWildAnimalsPerSpecies
                    || settings.EnableWildAnimalReproduction
                        && settings.LimitWildAnimalReproductionByEcosystem);
        }

        private static bool Prefix(Pawn male, Pawn female)
        {
            return !WildAnimalEcosystemUtility.ShouldBlockWildWildMating(male, female);
        }
    }
}
