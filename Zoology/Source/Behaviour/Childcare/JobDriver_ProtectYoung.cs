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
        private Thing ProtectedThing => this.job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil attackToil = Toils_Combat.FollowAndMeleeAttack(TargetIndex.A, HitAction)
                .FailOn(() => !IsValidPawn(TargetPawn))
                .FailOn(() => !IsValidProtectedThing(ProtectedThing));

            attackToil.AddPreTickIntervalAction(delegate (int _)
            {
                try
                {
                    Pawn target = TargetPawn;
                    Thing protectedThing = ProtectedThing;
                    if (!IsValidPawn(target) || !IsValidProtectedThing(protectedThing))
                    {
                        pawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (target.MapHeld != protectedThing.MapHeld)
                    {
                        pawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    int distSq = (target.PositionHeld - protectedThing.PositionHeld).LengthHorizontalSquared;
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

        private bool IsValidProtectedThing(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.SpawnedOrAnyParentSpawned || thing.MapHeld == null)
            {
                return false;
            }

            if (thing is Pawn pawnThing && pawnThing.Dead)
            {
                return false;
            }

            return true;
        }
    }
}
