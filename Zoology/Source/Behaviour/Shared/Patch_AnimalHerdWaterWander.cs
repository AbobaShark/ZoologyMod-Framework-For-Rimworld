using HarmonyLib;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // A wet terrestrial herd skips vanilla's expensive nearest-herdmate scan
    // and receives a common reachable shore target instead.
    [HarmonyPatch(typeof(JobGiver_WanderHerd), "GetWanderRoot")]
    internal static class Patch_DryHerdWaterRoot
    {
        private static bool Prefix(Pawn pawn, ref IntVec3 __result)
        {
            if (!AnimalWaterHabitat.CanEscapeWater(pawn))
                return true;
            if (!pawn.Map.GetComponent<AnimalRefugeMapComponent>()
                .TryHerdWaterExit(pawn, out IntVec3 shore))
                return true;
            __result = shore;
            return false;
        }

        private static void Postfix(Pawn pawn, ref IntVec3 __result)
        {
            if (!AnimalWaterHabitat.IsDryHerd(pawn) || pawn.Map == null
                || !__result.IsValid || !__result.InBounds(pawn.Map)
                || !__result.GetTerrain(pawn.Map).IsWater
                || !ModConstants.WaterHabitatBehavior)
                return;
            if (!pawn.Position.GetTerrain(pawn.Map).IsWater)
                __result = pawn.Position;
        }
    }

    // Vanilla fallback search relaxes avoidWander. Check the FINAL destination.
    [HarmonyPatch(typeof(JobGiver_Wander), "GetExactWanderDest")]
    internal static class Patch_DryHerdWanderDestination
    {
        private static void Postfix(JobGiver_Wander __instance, Pawn pawn, ref IntVec3 __result)
        {
            if (!(__instance is JobGiver_WanderHerd)
                || !AnimalWaterHabitat.IsDryHerd(pawn)
                || pawn.Map == null || !__result.IsValid
                || !__result.InBounds(pawn.Map)
                || !__result.GetTerrain(pawn.Map).IsWater
                || !ModConstants.WaterHabitatBehavior)
                return;
            AnimalRefugeMapComponent index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (pawn.Position.GetTerrain(pawn.Map).IsWater
                && index.TryHerdWaterExit(pawn, out IntVec3 shore))
            {
                __result = shore;
                return;
            }
            __result = index.TryDryCellNear(pawn, pawn.Position, out IntVec3 dry)
                ? dry : IntVec3.Invalid;
        }
    }
}
