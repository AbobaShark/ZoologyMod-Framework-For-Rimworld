using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobDriver_ProtectYoung : JobDriver
    {
        private const int StopDistanceTiles = 10;
        private const int StopDistanceSquared = StopDistanceTiles * StopDistanceTiles;

        private Pawn TargetPawn => this.job.GetTarget(TargetIndex.A).Thing as Pawn;
        private Pawn ProtectedPawn => this.job.GetTarget(TargetIndex.B).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil attackToil = Toils_Combat.FollowAndMeleeAttack(TargetIndex.A, HitAction)
                .FailOn(() => !IsValidPawn(TargetPawn))
                .FailOn(() => !IsValidPawn(ProtectedPawn));

            attackToil.AddPreTickIntervalAction(delegate (int _)
            {
                try
                {
                    Pawn target = TargetPawn;
                    Pawn child = ProtectedPawn;
                    if (!IsValidPawn(target) || !IsValidPawn(child))
                    {
                        pawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (target.Map != child.Map)
                    {
                        pawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    int distSq = (target.Position - child.Position).LengthHorizontalSquared;
                    if (distSq >= StopDistanceSquared)
                    {
                        pawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: JobDriver_ProtectYoung tick exception: {ex}");
                }
            });

            yield return attackToil;
        }

        private void HitAction()
        {
            try
            {
                Pawn target = TargetPawn;
                if (target == null) return;
                pawn.meleeVerbs.TryMeleeAttack(target, null, false);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: JobDriver_ProtectYoung HitAction exception: {ex}");
            }
        }

        private bool IsValidPawn(Pawn p)
        {
            if (p == null) return false;
            if (!p.Spawned || p.Destroyed || p.Dead) return false;
            return true;
        }

    }
}
