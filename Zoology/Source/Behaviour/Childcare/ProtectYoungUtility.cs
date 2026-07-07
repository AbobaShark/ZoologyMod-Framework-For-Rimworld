using System;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class ProtectYoungUtility
    {
        public const string ProtectYoungDefName = "Zoology_ProtectYoung";
        private static JobDef protectYoungJobDef;
        private static bool protectYoungJobDefResolved;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsProtectYoungJob(Pawn pawn)
        {
            Job curJob = pawn?.CurJob;
            return curJob != null && IsProtectYoungJob(curJob, pawn);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsProtectYoungJob(Job curJob, Pawn pawn = null)
        {
            if (curJob == null)
            {
                return false;
            }

            if (IsProtectYoungJobDef(curJob.def))
            {
                return true;
            }

            if (pawn?.jobs?.curDriver is JobDriver_ProtectYoung)
            {
                return true;
            }

            Type driverClass = curJob.def?.driverClass;
            return driverClass == typeof(JobDriver_ProtectYoung)
                || (driverClass != null && typeof(JobDriver_ProtectYoung).IsAssignableFrom(driverClass));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsProtectYoungJobDef(JobDef jobDef)
        {
            if (jobDef == null)
            {
                return false;
            }

            JobDef cached = GetProtectYoungJobDef();
            if (cached != null && ReferenceEquals(jobDef, cached))
            {
                return true;
            }

            string defName = jobDef.defName;
            return defName == ProtectYoungDefName
                || (!string.IsNullOrEmpty(defName)
                    && defName.Equals(ProtectYoungDefName, StringComparison.OrdinalIgnoreCase));
        }

        private static JobDef GetProtectYoungJobDef()
        {
            if (protectYoungJobDefResolved)
            {
                return protectYoungJobDef;
            }

            protectYoungJobDefResolved = true;
            protectYoungJobDef = DefDatabase<JobDef>.GetNamedSilentFail(ProtectYoungDefName);
            return protectYoungJobDef;
        }

        public static bool CanRetargetProtectYoungJob(Pawn pawn, Pawn newAggressor, Thing protectedThing)
        {
            return CanRetargetProtectYoungJob(pawn, pawn?.CurJob, newAggressor, protectedThing);
        }

        public static bool CanRetargetProtectYoungJob(Pawn pawn, Job curJob, Pawn newAggressor, Thing protectedThing)
        {
            if (pawn == null || curJob == null || !IsProtectYoungJob(curJob, pawn))
            {
                return false;
            }

            if (!TryGetProtectYoungTargets(curJob, out Pawn currentAggressor, out Thing currentProtectedThing))
            {
                return true;
            }

            if (!IsActiveThreat(currentAggressor) || !IsValidProtectedThing(currentProtectedThing))
            {
                return true;
            }

            return protectedThing != null
                && ReferenceEquals(currentProtectedThing, protectedThing)
                && newAggressor != null
                && !ReferenceEquals(currentAggressor, newAggressor);
        }

        public static bool IsProtectYoungJobProtecting(Pawn pawn, Thing protectedThing)
        {
            Job curJob = pawn?.CurJob;
            if (protectedThing == null || curJob == null || !IsProtectYoungJob(curJob, pawn))
            {
                return false;
            }

            return TryGetProtectYoungTargets(curJob, out Pawn currentAggressor, out Thing currentProtectedThing)
                && ReferenceEquals(currentProtectedThing, protectedThing)
                && IsActiveThreat(currentAggressor)
                && IsValidProtectedThing(currentProtectedThing);
        }

        public static void InterruptRetargetableProtectYoungJob(Pawn pawn, Pawn newAggressor, Thing protectedThing)
        {
            if (!CanRetargetProtectYoungJob(pawn, newAggressor, protectedThing))
            {
                return;
            }

            try
            {
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true, true);
            }
            catch
            {
            }
        }

        private static bool IsActiveThreat(Pawn pawn)
        {
            return pawn != null
                && pawn.Spawned
                && !pawn.Destroyed
                && !pawn.Dead
                && !pawn.Downed;
        }

        private static bool IsValidProtectedThing(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.SpawnedOrAnyParentSpawned || thing.MapHeld == null)
            {
                return false;
            }

            return !(thing is Pawn pawnThing) || !pawnThing.Dead;
        }

        private static bool TryGetProtectYoungTargets(Job curJob, out Pawn aggressor, out Thing protectedThing)
        {
            aggressor = null;
            protectedThing = null;
            if (curJob == null)
            {
                return false;
            }

            try
            {
                aggressor = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                protectedThing = curJob.GetTarget(TargetIndex.B).Thing;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
