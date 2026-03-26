using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    /// <summary>
    /// Stabilizes auto-slaughter execution when the dynamic candidate list changes mid-job.
    /// This prevents handlers from repeatedly dropping one target and switching to another.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_Slaughter), nameof(WorkGiver_Slaughter.JobOnThing))]
    internal static class Patch_WorkGiver_Slaughter_JobOnThing_AutoStability
    {
        private static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            try
            {
                if (forced || __result == null || __result.def != JobDefOf.Slaughter)
                {
                    return;
                }

                if (pawn?.Map == null || t is not Pawn animal || animal.Map != pawn.Map)
                {
                    return;
                }

                AutoSlaughterManager manager = pawn.Map.autoSlaughterManager;
                if (manager == null)
                {
                    return;
                }

                var autoTargets = manager.AnimalsToSlaughter;
                if (autoTargets == null || !autoTargets.Contains(animal))
                {
                    return;
                }

                // Keep the current slaughter job from failing if autoslaughter priorities
                // are recalculated while the handler is already walking/executing.
                __result.ignoreDesignations = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_WorkGiver_Slaughter_JobOnThing_AutoStability failed: {ex}");
            }
        }
    }
}
