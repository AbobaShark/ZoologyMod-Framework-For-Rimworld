#if RIMWORLD17
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // The ordinary vanilla pen-balancing heuristic optimizes pen
    // populations. It can move the only mother away from a litter, or
    // reverse a deliberate nursery balancing job. Restrict only such
    // automatically balanced moves, leaving every other pen rope job,
    // manual command and barn handler unchanged.
    [HarmonyPatch(typeof(AnimalPenUtility),
        nameof(AnimalPenUtility.GetPenAnimalShouldBeTakenTo))]
    internal static class Patch_ProtectNursingMothersInPens
    {
        private static bool Prepare() =>
            AnimalNursingBalance.Enabled;

        private static void Postfix(Pawn animal, RopingPriority mode,
            ref CompAnimalPenMarker __result)
        {
            if (__result != null && mode == RopingPriority.Balanced
                && AnimalNursingBalance.HasDependentYoungInPen(animal))
                __result = null;
        }
    }
}
#endif
