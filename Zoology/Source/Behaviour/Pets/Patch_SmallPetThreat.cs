using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn), "ThreatDisabledBecauseNonAggressiveRoamer")]
    public static class Patch_SmallPetThreatDisabled
    {
        private static bool IsHostileToPlayerFactionSafe(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null || ReferenceEquals(faction, playerFaction))
            {
                return false;
            }

            FactionRelation relation = faction.RelationWith(playerFaction, allowNull: true);
            return relation != null && relation.kind == FactionRelationKind.Hostile;
        }

        public static bool Prepare()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || settings.EnableIgnoreSmallPetsByRaiders;
        }

        public static bool Prefix(Pawn __instance, Pawn otherPawn, ref bool __result)
        {
            if (__instance == null)
            {
                return true;
            }

            ZoologyModSettings settings = ZoologyModSettings.Instance;
            if (settings != null && !settings.EnableIgnoreSmallPetsByRaiders)
            {
                return true;
            }

            RaceProperties raceProps = __instance.RaceProps;
            if (raceProps?.Animal != true)
            {
                return true;
            }

            Faction faction = __instance.Faction;
            if (faction == null || !faction.IsPlayer)
            {
                return true;
            }

            if (__instance.Roamer)
            {
                return true;
            }

            if (raceProps.baseBodySize >= settings.SmallPetBodySizeThreshold)
            {
                return true;
            }

            if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(__instance))
            {
                return true;
            }

            if (__instance.InAggroMentalState || __instance.IsFighting())
            {
                return true;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick < __instance.mindState.lastEngageTargetTick + 360)
            {
                return true;
            }

            if (otherPawn != null && otherPawn.RaceProps?.Humanlike != true)
            {
                // Allow the same "ignore small pets" behavior for hostile faction animals (e.g. raider animals, Photonozoa).
                if (otherPawn.Faction == null || !IsHostileToPlayerFactionSafe(otherPawn.Faction))
                {
                    return true;
                }
            }

            Lord lord = otherPawn?.GetLord();
            if (lord != null && lord.CurLordToil != null && lord.CurLordToil.AllowAggressiveTargetingOfRoamers)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
