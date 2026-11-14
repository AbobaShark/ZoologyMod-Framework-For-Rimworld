using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // The stock developer overlay should show the current FoodUtility result.
    // Only skip it for an unspawned or invalid selected pawn, which can occur
    // when selecting an animal held inside a barn or other container.
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.DebugDrawPredatorFoodSource))]
    internal static class Patch_DebugDrawPredatorFoodSource_ValidSelection
    {
        private static bool Prefix()
        {
            Pawn pawn = Find.Selector?.SingleSelectedThing as Pawn;
            if (pawn == null)
                return false;

            return pawn.Spawned
                && !pawn.Destroyed
                && !pawn.Dead
                && pawn.Map != null
                && pawn.Map == Find.CurrentMap
                && pawn.RaceProps != null
                && pawn.health?.capacities != null
                && pawn.needs?.food != null;
        }
    }
}
