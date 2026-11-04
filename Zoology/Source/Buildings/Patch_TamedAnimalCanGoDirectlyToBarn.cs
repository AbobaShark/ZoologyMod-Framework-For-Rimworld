#if RIMWORLD17
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // JobDriver_Tame ends with an instant toil that checks only vanilla
    // pen markers. If no pen is found it sends MessageTameNoSuitablePens,
    // even when Zoology's normal handling worker can use a nearby barn.
    // Keep all vanilla tame/pen logic; intercept only that final toil,
    // immediately before it would choose a pen or display the warning.
    [HarmonyPatch]
    internal static class Patch_TamedAnimalCanGoDirectlyToBarn
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(JobDriver_Tame), "MakeNewToils");

        private static bool Prepare() =>
            Building_AnimalBarn.Enabled && TargetMethod() != null;

        private static void Postfix(JobDriver_Tame __instance,
            ref IEnumerable<Toil> __result)
        {
            if (__result != null)
                __result = WithBarnFallback(__result, __instance);
        }

        internal static IEnumerable<Toil> WithBarnFallback(
            IEnumerable<Toil> toils, JobDriver_Tame driver)
        {
            foreach (Toil toil in toils)
            {
                // The final vanilla instant toil is explicitly created
                // with this name. Do not intercept the taming interaction,
                // food collection or any other jobs.
                if (toil != null && toil.debugName == "MakeNewToils"
                    && toil.initAction != null)
                {
                    Action vanilla = toil.initAction;
                    toil.initAction = () =>
                    {
                        if (!TryStartBarnJob(driver))
                            vanilla();
                    };
                }

                yield return toil;
            }
        }

        private static bool TryStartBarnJob(JobDriver_Tame driver)
        {
            if (!Building_AnimalBarn.Enabled || driver?.pawn == null
                || driver.job == null)
                return false;

            Pawn handler = driver.pawn;
            Pawn animal = driver.job.GetTarget(TargetIndex.A).Thing as Pawn;
            if (animal?.Spawned != true || animal.Faction != Faction.OfPlayer
                || animal.Map != handler.Map
                || !AnimalPenUtility.NeedsToBeManagedByRope(animal))
                return false;

            // This is the same admission/filter/capacity/path policy used
            // when the ordinary Zoology handler selects a destination. It
            // also respects a closer suitable vanilla enclosed pen.
            Building_AnimalBarn barn = WorkGiver_BarnAdmit.FindBarn(
                handler, animal);
            if (barn == null)
                return false;

            Job job = JobMaker.MakeJob(ZoologyBarnJobDefOf.Zoology_BarnAdmit,
                animal, barn.InteractionCell, barn);
            handler.jobs.StartJob(job, JobCondition.Succeeded);
            return true;
        }
    }
}
#endif
