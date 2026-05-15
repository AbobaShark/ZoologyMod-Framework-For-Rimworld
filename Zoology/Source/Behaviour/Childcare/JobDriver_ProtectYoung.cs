using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobDriver_ProtectYoung : JobDriver
    {
        private const int MinimumProtectDurationTicks = ZoologyTickLimiter.PreyProtection.MinimumProtectDurationTicks;
        private static int MaxDistanceSq => ChildcareDefenseUtility.GetProtectionRangeSquared();

        private int startTickLocal = -1;

        private Pawn TargetPawn => job.GetTarget(TargetIndex.A).Thing as Pawn;
        private Thing ProtectedThing => job.GetTarget(TargetIndex.B).Thing;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startTickLocal, "startTickLocal", -1);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Pawn actorPawn = pawn;

            AddFinishAction(delegate
            {
                try
                {
                    ZoologyNotificationUtility.TryNotifyProtectionEnded(actorPawn, TargetPawn, ProtectedThing);
                    actorPawn?.Map?.attackTargetsCache?.UpdateTarget(actorPawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: JobDriver_ProtectYoung FinishAction exception: {ex}");
                }
            });

            Toil attackToil = Toils_Combat.FollowAndMeleeAttack(TargetIndex.A, HitAction)
                .FailOnDespawnedOrNull(TargetIndex.A)
                .FailOn(() =>
                {
                    try
                    {
                        Pawn target = TargetPawn;
                        if (target == null)
                        {
                            return true;
                        }

                        if (IsInMinimumProtectDuration())
                        {
                            return false;
                        }

                        return !ChildcareDefenseUtility.TryGetActiveProtectionAnchor(actorPawn, ProtectedThing, target, out _, out Map anchorMap, out _)
                            || target.MapHeld != anchorMap;
                    }
                    catch
                    {
                        return false;
                    }
                });

            attackToil.AddPreTickIntervalAction(delegate (int _)
            {
                try
                {
                    Pawn target = TargetPawn;
                    if (!IsValidPawn(target))
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (!ChildcareDefenseUtility.TryGetActiveProtectionAnchor(actorPawn, ProtectedThing, target, out Thing activeProtectedThing, out Map anchorMap, out IntVec3 anchorPosition))
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (!ReferenceEquals(activeProtectedThing, ProtectedThing))
                    {
                        actorPawn?.CurJob?.SetTarget(TargetIndex.B, activeProtectedThing);
                    }

                    if (target.MapHeld != anchorMap)
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (IsInMinimumProtectDuration())
                    {
                        actorPawn?.Map?.attackTargetsCache?.UpdateTarget(actorPawn);
                        return;
                    }

                    if (!PreyProtectionUtility.IsPawnWithinProtectionRange(target, anchorMap, anchorPosition, MaxDistanceSq))
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (!PreyProtectionUtility.IsPawnWithinProtectionRange(actorPawn, anchorMap, anchorPosition, MaxDistanceSq))
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    actorPawn?.Map?.attackTargetsCache?.UpdateTarget(actorPawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: JobDriver_ProtectYoung tick exception: {ex}");
                }
            });

            yield return attackToil;
        }

        private bool IsInMinimumProtectDuration()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int startedAt = startTickLocal;
            if (startedAt < 0)
            {
                startTickLocal = now;
                return true;
            }

            return now - startedAt < MinimumProtectDurationTicks;
        }

        private void HitAction()
        {
            try
            {
                Pawn target = TargetPawn;
                if (target == null)
                {
                    return;
                }

                pawn.meleeVerbs.TryMeleeAttack(target, null, false);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: JobDriver_ProtectYoung HitAction exception: {ex}");
            }
        }

        private static bool IsValidPawn(Pawn p)
        {
            return p != null
                && p.Spawned
                && !p.Destroyed
                && !p.Dead
                && !p.Downed;
        }

        private static bool IsValidProtectedThing(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.SpawnedOrAnyParentSpawned || thing.MapHeld == null)
            {
                return false;
            }

            return !(thing is Pawn pawnThing) || !pawnThing.Dead;
        }
    }
}
