using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace ZoologyMod
{
    public class JobDriver_YoungSuckle : JobDriver
    {
        private const int suckleDurationTicks = ZoologyTickLimiter.Lactation.YoungSuckleDurationTicks;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (this.job != null)
            {
                this.job.checkOverrideOnExpire = false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil wait = Toils_General.Wait(suckleDurationTicks, TargetIndex.None);
            wait.tickAction = () =>
            {
                var pawn = this.pawn;
                if (pawn == null || pawn.Dead)
                {
                    pawn?.jobs?.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (pawn.Map == null)
                {
                    pawn.jobs?.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage >= 0.99f)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }

                Pawn availableMom = AnimalChildcareUtility.FindNearestAvailableMother(pawn);
                if (availableMom == null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }
            };
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            yield return wait;
        }
    }
}
