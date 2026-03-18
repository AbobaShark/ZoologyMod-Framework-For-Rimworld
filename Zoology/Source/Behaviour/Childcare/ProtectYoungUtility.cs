using System;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class ProtectYoungUtility
    {
        public const string ProtectYoungDefName = "Zoology_ProtectYoung";

        public static bool IsProtectYoungJob(Pawn pawn)
        {
            if (pawn == null) return false;

            Job curJob = pawn.CurJob;
            if (curJob == null) return false;

            var defName = curJob.def?.defName;
            if (!string.IsNullOrEmpty(defName)
                && defName.Equals(ProtectYoungDefName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (pawn.jobs?.curDriver is JobDriver_ProtectYoung)
            {
                return true;
            }

            Type driverClass = curJob.def?.driverClass;
            return driverClass != null && typeof(JobDriver_ProtectYoung).IsAssignableFrom(driverClass);
        }
    }
}
