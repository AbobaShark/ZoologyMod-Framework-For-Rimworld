using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobGiver_AnimalLickWounds : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!AnimalWoundLickingUtility.CanUseWoundLicking(pawn))
            {
                return null;
            }

            if (!AnimalWoundLickingUtility.HasLickableWounds(pawn))
            {
                return null;
            }

            if (pawn.Map?.reservationManager == null || !pawn.Map.reservationManager.CanReserve(pawn, pawn))
            {
                return null;
            }

            JobDef lickJob = DefDatabase<JobDef>.GetNamedSilentFail(AnimalWoundLickingUtility.AnimalLickWoundsJobDefName);
            return lickJob == null ? null : JobMaker.MakeJob(lickJob, pawn);
        }
    }
}
