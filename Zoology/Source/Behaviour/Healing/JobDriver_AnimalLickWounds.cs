using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobDriver_AnimalLickWounds : JobDriver
    {
        private Pawn Patient => job == null ? null : job.GetTarget(TargetIndex.A).Pawn;

        private bool HasValidPatient => pawn != null && Patient == pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return HasValidPatient;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !HasValidPatient || !AnimalWoundLickingUtility.CanUseWoundLicking(pawn));
            AddEndCondition(() => !HasValidPatient || !AnimalWoundLickingUtility.HasLickableWounds(pawn) ? JobCondition.Succeeded : JobCondition.Ongoing);

            Toil wait = Toils_General.Wait(AnimalWoundLickingUtility.GetLickDurationTicks());
            wait.WithProgressBarToilDelay(TargetIndex.A).PlaySustainerOrSound(SoundDefOf.Interact_Tend);
            wait.tickIntervalAction = delegate (int delta)
            {
                if (pawn?.Map != null && pawn.IsHashIntervalTick(100, delta) && !pawn.Position.Fogged(pawn.Map))
                {
                    FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.HealingCross);
                }
            };
            yield return wait;

            yield return Toils_General.Do(delegate
            {
                if (HasValidPatient)
                {
                    AnimalWoundLickingUtility.TryApplyWoundLicking(pawn);
                }
            });
        }
    }
}
