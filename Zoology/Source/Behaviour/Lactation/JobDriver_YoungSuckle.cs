// JobDriver_YoungSuckle.cs

using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace ZoologyMod
{
    public class JobDriver_YoungSuckle : JobDriver
    {
        private const int suckleDurationTicks = 2000;

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
                if (this.pawn == null || this.pawn.Dead)
                {
                    this.pawn?.jobs?.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (this.pawn.needs?.food != null && this.pawn.needs.food.CurLevelPercentage >= 0.99f)
                {
                    this.pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }

                Pawn availableMom = AnimalChildcareUtility.FindNearestAvailableMother(this.pawn);
                if (availableMom == null)
                {
                    this.pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }
            };
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            yield return wait;
        }
    }
}