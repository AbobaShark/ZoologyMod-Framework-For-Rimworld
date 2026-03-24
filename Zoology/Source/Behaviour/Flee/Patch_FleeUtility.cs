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
            var settings = ModConstants.Settings;
            if (settings != null && !settings.EnableCustomFleeDanger)
                return true;

            if (pawn == null)
            {
                __result = false;
                return false;
            }

            if (!pawn.IsAnimal || pawn.InMentalState || pawn.IsFighting() || pawn.Downed || pawn.Dead)
            {
                __result = false;
                return false;
            }

            if (ZoologyFleeSafetyUtility.IsStandardFleeBlockedByExtensions(pawn))
            {
                __result = false;
                return false;
            }

            if (pawn.GetLord() != null)
            {
                __result = false;
                return false;
            }

            if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn))
            {
                __result = false;
                return false;
            }

            var faction = pawn.Faction;
            if (faction != null && !faction.def.animalsFleeDanger)
            {
                __result = false;
                return false;
            }

            var map = pawn.Map;
            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction != null && ReferenceEquals(faction, playerFaction) && map != null && map.IsPlayerHome)
            {
                var raceProps = pawn.RaceProps;
                bool predator = raceProps?.predator ?? false;
                float baseSize = raceProps?.baseBodySize ?? pawn.BodySize;
                bool safeOnHome =
                    (predator && baseSize >= settings.SafePredatorBodySizeThreshold)
                    || (!predator && baseSize > settings.SafeNonPredatorBodySizeThreshold);

                if (safeOnHome)
                {
                    __result = false;
                    return false;
                }
            }

            var curJob = pawn.CurJob;
            var curJobDef = curJob?.def;
            if (curJobDef != null && curJobDef.neverFleeFromEnemies)
            {
                __result = false;
                return false;
            }

            if (curJobDef == JobDefOf.Flee && curJob != null && curJob.startTick == Find.TickManager.TicksGame)
            {
                __result = false;
                return false;
            }

            __result = true;
            return false;
        }
    }
}
