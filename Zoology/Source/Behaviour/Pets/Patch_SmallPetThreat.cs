

using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using System.Reflection;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn), "ThreatDisabledBecauseNonAggressiveRoamer")]
    public static class Patch_SmallPetThreatDisabled
    {
        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableIgnoreSmallPetsByRaiders;
        }

        public static void Postfix(Pawn __instance, Pawn otherPawn, ref bool __result)
        {
            
            if (!ModConstants.Settings.EnableIgnoreSmallPetsByRaiders)
                return;

            if (__result)
                return;

            
            bool isSmallPet = __instance.RaceProps.Animal
                           && __instance.Faction == Faction.OfPlayer
                           && __instance.RaceProps.baseBodySize < ModConstants.SmallPetBodySizeThreshold;

            if (!isSmallPet)
                return;

            
            Lord lord = otherPawn?.GetLord();

            
            bool followingMaster = ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(__instance);

            
            bool allowAggressive = lord != null && lord.CurLordToil.AllowAggressiveTargetingOfRoamers;

            
            bool notAggro = !__instance.InAggroMentalState;
            bool notFighting = !__instance.IsFighting();
            bool cooldownPassed = Find.TickManager.TicksGame >= __instance.mindState.lastEngageTargetTick + 360;

            if (!allowAggressive && !followingMaster && notAggro && notFighting && cooldownPassed)
            {
                __result = true;
            }
        }
    }
}
