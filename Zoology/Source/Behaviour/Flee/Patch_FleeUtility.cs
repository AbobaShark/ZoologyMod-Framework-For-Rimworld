using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(FleeUtility), nameof(FleeUtility.ShouldAnimalFleeDanger))]
    public static class Patch_ShouldAnimalFleeDanger_PrefixReplace
    {
        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableCustomFleeDanger;
        }

        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (!ModConstants.Settings.EnableCustomFleeDanger)
                return true;

            if (pawn == null)
            {
                __result = false;
                return false;
            }

            bool isAnimal = pawn.IsAnimal;
            bool notInMental = !pawn.InMentalState;
            bool notFighting = !pawn.IsFighting();
            bool notDowned = !pawn.Downed;
            bool notDead = !pawn.Dead;
            bool notFollowMaster = !ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn);
            bool noLord = pawn.GetLord() == null;

            bool factionClause;
            if (pawn.Faction != Faction.OfPlayer || pawn.Map == null || !pawn.Map.IsPlayerHome)
            {
                factionClause = true;
            }
            else
            {
                bool predator = pawn.RaceProps?.predator ?? false;
                float baseSize = pawn.RaceProps?.baseBodySize ?? pawn.BodySize;

                bool safeOnHome =
                    (predator && baseSize >= ModConstants.SafePredatorBodySizeThreshold)
                    || (!predator && baseSize > ModConstants.SafeNonPredatorBodySizeThreshold);

                factionClause = !safeOnHome;
            }

            bool factionAllows = (pawn.Faction == null || pawn.Faction.def.animalsFleeDanger);

            bool jobAllows = true;
            if (pawn.CurJob != null && pawn.CurJobDef != null && pawn.CurJobDef.neverFleeFromEnemies)
            {
                jobAllows = false;
            }
            if (pawn.jobs != null && pawn.jobs.curJob != null && pawn.jobs.curJob.def == JobDefOf.Flee && pawn.jobs.curJob.startTick == Find.TickManager.TicksGame)
            {
                jobAllows = false;
            }

            bool final = isAnimal && notInMental && notFighting && notDowned && notDead && notFollowMaster && noLord && factionClause && factionAllows && jobAllows;

            __result = final;
            return false;
        }
    }
}
