using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobGiver_MotherRespondToSuckleRequests : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!LactationSettingsGate.Enabled())
            {
                return null;
            }

            if (!AnimalLactationUtility.CanMotherFeed(pawn))
            {
                return null;
            }

            if (!AnimalLactationUtility.CanAttemptFeedNow(pawn))
            {
                return null;
            }

            if (!LactationRequestUtility.TryGetRequestingPup(pawn, out Pawn pup) || pup == null)
            {
                return null;
            }

            JobDef youngSuckleDef = AnimalLactationUtility.YoungSuckleJobDef;
            if (youngSuckleDef != null
                && pup.CurJob != null
                && pup.CurJob.def == youngSuckleDef
                && pup.CurJob.targetA.HasThing
                && ReferenceEquals(pup.CurJob.GetTarget(TargetIndex.A).Thing, pawn))
            {
                return null;
            }

            if (ShouldLetBabyComeToMom(pawn))
            {
                if (AnimalLactationUtility.TryStartYoungSuckleJob(pup, pawn))
                {
                    AnimalLactationUtility.RecordFeedAttempt(pawn);
                    LactationRequestUtility.ClearRequest(pup);
                }
                return null;
            }

            if (!pawn.CanReach(pup, PathEndMode.Touch, Danger.Deadly, false, false, TraverseMode.ByPawn))
            {
                return null;
            }

            Job job = AnimalLactationUtility.MakeAnimalBreastfeedJob(pup, pawn);
            if (job == null)
            {
                return null;
            }

            try
            {
                if (!pawn.Reserve(pup, job, 1, -1, null, false))
                {
                    return null;
                }

                if (!pawn.Reserve(pawn, job, 1, -1, null, false))
                {
                    try
                    {
                        var rm = pup.Map?.reservationManager;
                        if (rm != null) rm.Release(new LocalTargetInfo(pup), pawn, job);
                    }
                    catch
                    {
                    }
                    return null;
                }
            }
            catch
            {
                return null;
            }

            AnimalLactationUtility.RecordFeedAttempt(pawn);
            LactationRequestUtility.ClearRequest(pup);
            return job;
        }

        private static bool ShouldLetBabyComeToMom(Pawn mom)
        {
            if (mom == null)
            {
                return false;
            }

            if (mom.Downed)
            {
                return true;
            }

            if (mom.CurJobDef == JobDefOf.LayDown && mom.InBed())
            {
                return true;
            }

            return false;
        }
    }
}
