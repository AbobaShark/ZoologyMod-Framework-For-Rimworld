using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobDriver_IncubateEggClutch : JobDriver
    {
        private Thing EggTarget => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing egg = EggTarget;
            return egg != null && pawn.Reserve(egg, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !ChildcareDefenseUtility.CanContinueEggIncubation(pawn, EggTarget));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil wait = Toils_General.Wait(ChildcareDefenseUtility.GetEggIncubationDurationTicks(), TargetIndex.A);
            wait.WithProgressBarToilDelay(TargetIndex.A);
            System.Action originalInit = wait.initAction;
            wait.initAction = delegate
            {
                originalInit?.Invoke();
                ChildcareDefenseUtility.MarkEggIncubated(EggTarget);
            };
            wait.tickAction = delegate
            {
                Thing egg = EggTarget;
                if (!ChildcareDefenseUtility.CanContinueEggIncubation(pawn, egg))
                {
                    pawn.jobs?.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }

                ChildcareDefenseUtility.MarkEggIncubated(egg);
            };
            wait.AddFinishAction(() =>
            {
                ChildcareDefenseUtility.StopEggIncubation(EggTarget);
            });
            yield return wait;

            yield return Toils_General.Do(delegate
            {
                ChildcareDefenseUtility.FinishEggIncubation(EggTarget);
            });
        }
    }
}
