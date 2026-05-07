using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobDriver_AnimalLickWounds : JobDriver
    {
        private Pawn Patient => job.GetTarget(TargetIndex.A).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Patient != pawn || !AnimalWoundLickingUtility.CanUseWoundLicking(pawn));
            AddEndCondition(() => AnimalWoundLickingUtility.HasLickableWounds(pawn) ? JobCondition.Ongoing : JobCondition.Succeeded);

            Toil wait = Toils_General.Wait(AnimalWoundLickingUtility.GetLickDurationTicks());
            wait.WithProgressBarToilDelay(TargetIndex.A).PlaySustainerOrSound(SoundDefOf.Interact_Tend);
            wait.tickIntervalAction = delegate (int delta)
            {
                if (pawn.IsHashIntervalTick(100, delta) && !pawn.Position.Fogged(pawn.Map))
                {
                    FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.HealingCross);
                }
            };
            yield return wait;

            yield return Toils_General.Do(delegate
            {
                AnimalWoundLickingUtility.TryApplyWoundLicking(pawn);
            });
        }
    }
}
