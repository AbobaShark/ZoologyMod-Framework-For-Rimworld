using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobGiver_WildCompatibleMate : ThinkNode_JobGiver
    {
        private const float MaxMateSearchRadius = 30f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            if (settings != null && (settings.DisableAllRuntimePatches || !settings.EnableWildAnimalReproduction))
            {
                return null;
            }

            if (pawn == null
                || !pawn.IsAnimal
                || pawn.Map == null
                || pawn.gender != Gender.Male
                || pawn.Sterile()
                || pawn.RaceProps?.disableMating == true
                || pawn.Dead
                || pawn.Destroyed
                || pawn.Downed
                || pawn.InMentalState)
            {
                return null;
            }

            Pawn mate = FindCompatibleMate(pawn, pawn.def);
            if (mate == null)
            {
                List<ThingDef> crossbreedDefs = pawn.RaceProps?.canCrossBreedWith;
                if (crossbreedDefs != null)
                {
                    for (int i = 0; i < crossbreedDefs.Count; i++)
                    {
                        ThingDef targetDef = crossbreedDefs[i];
                        if (targetDef == null || targetDef == pawn.def)
                        {
                            continue;
                        }

                        mate = FindCompatibleMate(pawn, targetDef);
                        if (mate != null)
                        {
                            break;
                        }
                    }
                }
            }

            return mate == null ? null : JobMaker.MakeJob(JobDefOf.Mate, mate);
        }

        private static Pawn FindCompatibleMate(Pawn pawn, ThingDef targetDef)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(targetDef),
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                MaxMateSearchRadius,
                thing => IsValidMateTarget(pawn, thing as Pawn)) as Pawn;
        }

        private static bool IsValidMateTarget(Pawn male, Pawn female)
        {
            if (female == null
                || female == male
                || female.Downed
                || female.Dead
                || female.Destroyed
                || female.Map != male.Map
                || !female.CanCasuallyInteractNow()
                || female.IsForbidden(male))
            {
                return false;
            }

            if (!CanUseWildCompatibilityRule(male, female))
            {
                return false;
            }

            return PawnUtility.FertileMateTarget(male, female);
        }

        private static bool CanUseWildCompatibilityRule(Pawn first, Pawn second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            Faction firstFaction = first.Faction;
            Faction secondFaction = second.Faction;

            if (firstFaction == null || secondFaction == null)
            {
                return true;
            }

            return false;
        }
    }
}
