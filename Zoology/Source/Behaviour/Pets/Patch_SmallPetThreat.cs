// Patch_SmallPetThreat.cs

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
        public static void Postfix(Pawn __instance, Pawn otherPawn, ref bool __result)
        {
            // Если функция отключена, ничего не делаем
            if (!ModConstants.Settings.EnableIgnoreSmallPetsByRaiders)
                return;

            if (__result)
                return;

            // Проверяем, является ли __instance мелким питомцем
            bool isSmallPet = __instance.RaceProps.Animal
                           && __instance.Faction == Faction.OfPlayer
                           && __instance.RaceProps.baseBodySize < ModConstants.SmallPetBodySizeThreshold;

            if (!isSmallPet)
                return;

            // Получаем лорда otherPawn
            Lord lord = otherPawn?.GetLord();

            // Проверяем, следует ли питомец за хозяином
            bool followingMaster = ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(__instance);

            // Разрешает ли лорд агрессивное таргетинг roamers (аналогично для small pets)
            bool allowAggressive = lord != null && lord.CurLordToil.AllowAggressiveTargetingOfRoamers;

            // Аналогичные условия как для roamers
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