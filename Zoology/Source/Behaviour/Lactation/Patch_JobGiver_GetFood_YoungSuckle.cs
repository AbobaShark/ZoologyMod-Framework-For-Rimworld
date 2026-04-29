using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class Patch_JobGiver_GetFood_YoungSuckle
    {
        private static bool Prepare() => LactationSettingsGate.Enabled();

        private static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (!LactationSettingsGate.Enabled())
                {
                    return;
                }

                if (!AnimalLactationUtility.ChildWantsSuckle(pawn))
                {
                    return;
                }

                Pawn mom = AnimalLactationUtility.FindNearestReachableMotherForPup(pawn);
                if (mom == null)
                {
                    return;
                }

                LactationRequestUtility.TryRequestSuckle(pawn, mom);

                if (!AnimalLactationUtility.CanAttemptFeedNow(mom))
                {
                    return;
                }
                if (!pawn.CanReserve(mom))
                {
                    return;
                }

                JobDef jd = AnimalLactationUtility.YoungSuckleJobDef;
                if (jd == null || jd.driverClass == null)
                {
                    return;
                }

                Job suckleJob = JobMaker.MakeJob(jd, mom);
                suckleJob.checkOverrideOnExpire = false;
                suckleJob.expiryInterval = ZoologyTickLimiter.Lactation.FullFeedSessionTicks;

                AnimalLactationUtility.RecordFeedAttempt(mom);
                __result = suckleJob;
            }
            catch
            {
            }
        }
    }
}
