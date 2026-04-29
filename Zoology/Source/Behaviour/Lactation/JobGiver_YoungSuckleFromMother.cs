using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobGiver_YoungSuckleFromMother : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!LactationSettingsGate.Enabled()) return null;
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned) return null;
            if (pawn.Map == null) return null;
            if (pawn.InMentalState) return null;

            if (!pawn.IsMammal()) return null;
            if (!AnimalLactationUtility.IsAnimalBabyLifeStage(pawn.ageTracker?.CurLifeStage)) return null;

            var foodNeed = pawn.needs?.food;
            if (foodNeed == null || foodNeed.CurLevelPercentage >= AnimalLactationUtility.feedingThreshold) return null;

            Pawn mom = AnimalLactationUtility.FindNearestReachableMotherForPup(pawn);
            if (mom == null)
            {
                LactationRequestUtility.ClearRequest(pawn);
                return null;
            }

            LactationRequestUtility.TryRequestSuckle(pawn, mom);

            if (!AnimalLactationUtility.CanAttemptFeedNow(mom)) return null;
            if (!pawn.CanReserve(mom)) return null;

            JobDef jd = AnimalLactationUtility.YoungSuckleJobDef;
            if (jd == null || jd.driverClass == null) return null;

            Job job = JobMaker.MakeJob(jd, mom);
            job.checkOverrideOnExpire = false;
            job.expiryInterval = ZoologyTickLimiter.Lactation.FullFeedSessionTicks;

            AnimalLactationUtility.RecordFeedAttempt(mom);
            return job;
        }
    }
}
