using System.Collections.Generic;
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
        private const int SmallPetStateCacheDurationTicks = 15;
        private const int SmallPetStateCacheCleanupIntervalTicks = 600;

        private static readonly Dictionary<int, CachedSmallPetState> cachedSmallPetStateByPawnId = new Dictionary<int, CachedSmallPetState>(32);
        private static readonly List<int> cachedSmallPetStateCleanupBuffer = new List<int>(32);
        private static int lastSmallPetStateCacheCleanupTick = -SmallPetStateCacheCleanupIntervalTicks;

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableIgnoreSmallPetsByRaiders;
        }

        public static bool Prefix(Pawn __instance, Pawn otherPawn, ref bool __result)
        {
            var settings = ModConstants.Settings;
            if (settings == null || !settings.EnableIgnoreSmallPetsByRaiders)
            {
                return true;
            }

            if (!IsPotentialRaiderIgnoringThreat(otherPawn))
            {
                return true;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!CanIgnoreSmallPetThreat(__instance, settings, currentTick))
            {
                return true;
            }

            Lord lord = otherPawn.GetLord();
            bool allowAggressive = lord != null && lord.CurLordToil.AllowAggressiveTargetingOfRoamers;
            if (allowAggressive)
            {
                return true;
            }

            __result = true;
            return false;
        }

        private static bool IsPotentialRaiderIgnoringThreat(Pawn otherPawn)
        {
            return otherPawn != null
                && otherPawn.Spawned
                && !otherPawn.Dead
                && !otherPawn.Destroyed
                && !otherPawn.Downed
                && otherPawn.RaceProps?.Humanlike == true
                && otherPawn.HostileTo(Faction.OfPlayer);
        }

        private static bool CanIgnoreSmallPetThreat(Pawn pawn, ZoologyModSettings settings, int currentTick)
        {
            if (pawn == null
                || settings == null
                || !pawn.RaceProps.Animal
                || pawn.Faction != Faction.OfPlayer
                || pawn.RaceProps.baseBodySize >= settings.SmallPetBodySizeThreshold)
            {
                return false;
            }

            if (currentTick > 0
                && cachedSmallPetStateByPawnId.TryGetValue(pawn.thingIDNumber, out CachedSmallPetState cached)
                && cached.ValidUntilTick >= currentTick)
            {
                return cached.CanIgnoreThreat;
            }

            bool canIgnoreThreat = !ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn)
                && !pawn.InAggroMentalState
                && !pawn.IsFighting()
                && currentTick >= pawn.mindState.lastEngageTargetTick + 360;

            if (currentTick > 0)
            {
                cachedSmallPetStateByPawnId[pawn.thingIDNumber] = new CachedSmallPetState(currentTick + SmallPetStateCacheDurationTicks, canIgnoreThreat);
                CleanupCacheIfNeeded(currentTick);
            }

            return canIgnoreThreat;
        }

        private static void CleanupCacheIfNeeded(int currentTick)
        {
            if (currentTick <= 0
                || cachedSmallPetStateByPawnId.Count == 0
                || currentTick - lastSmallPetStateCacheCleanupTick < SmallPetStateCacheCleanupIntervalTicks)
            {
                return;
            }

            lastSmallPetStateCacheCleanupTick = currentTick;
            cachedSmallPetStateCleanupBuffer.Clear();

            foreach (KeyValuePair<int, CachedSmallPetState> entry in cachedSmallPetStateByPawnId)
            {
                if (entry.Value.ValidUntilTick < currentTick)
                {
                    cachedSmallPetStateCleanupBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < cachedSmallPetStateCleanupBuffer.Count; i++)
            {
                cachedSmallPetStateByPawnId.Remove(cachedSmallPetStateCleanupBuffer[i]);
            }
        }

        private readonly struct CachedSmallPetState
        {
            public CachedSmallPetState(int validUntilTick, bool canIgnoreThreat)
            {
                ValidUntilTick = validUntilTick;
                CanIgnoreThreat = canIgnoreThreat;
            }

            public int ValidUntilTick { get; }

            public bool CanIgnoreThreat { get; }
        }
    }
}
