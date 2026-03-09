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

        private static readonly Dictionary<ThingDef, bool> cachedEligibleSmallPetDefs = new Dictionary<ThingDef, bool>(64);
        private static readonly Dictionary<int, CachedSmallPetState> cachedSmallPetStateByPawnId = new Dictionary<int, CachedSmallPetState>(32);
        private static readonly Dictionary<int, CachedThreatState> cachedThreatStateByPawnId = new Dictionary<int, CachedThreatState>(32);
        private static readonly List<int> cachedSmallPetStateCleanupBuffer = new List<int>(32);
        private static ZoologyModSettings cachedSettings;
        private static int lastSmallPetStateCacheCleanupTick = -SmallPetStateCacheCleanupIntervalTicks;
        private static float cachedSmallPetThreshold = -1f;

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableIgnoreSmallPetsByRaiders;
        }

        public static bool Prefix(Pawn __instance, Pawn otherPawn, ref bool __result)
        {
            var settings = GetSettingsFast();
            if (settings == null || !settings.EnableIgnoreSmallPetsByRaiders)
            {
                return true;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            float smallPetThreshold = settings.SmallPetBodySizeThreshold;
            if (!CanEverUseSmallPetRaiderIgnore(__instance, smallPetThreshold))
            {
                return true;
            }

            if (!CanSuppressThreatForSmallPets(otherPawn, currentTick))
            {
                return true;
            }

            if (!CanIgnoreSmallPetThreat(__instance, currentTick))
            {
                return true;
            }

            __result = true;
            return false;
        }

        private static ZoologyModSettings GetSettingsFast()
        {
            if (cachedSettings != null)
            {
                return cachedSettings;
            }

            cachedSettings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
            return cachedSettings;
        }

        private static bool CanEverUseSmallPetRaiderIgnore(Pawn pawn, float smallPetThreshold)
        {
            if (pawn == null || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            return IsEligibleSmallPetDef(pawn.def, smallPetThreshold);
        }

        private static bool CanSuppressThreatForSmallPets(Pawn otherPawn, int currentTick)
        {
            if (otherPawn == null)
            {
                return false;
            }

            int pawnId = otherPawn.thingIDNumber;
            if (currentTick > 0
                && cachedThreatStateByPawnId.TryGetValue(pawnId, out CachedThreatState cached)
                && cached.ValidUntilTick >= currentTick)
            {
                return cached.CanSuppressThreat;
            }

            bool canSuppressThreat = otherPawn.Spawned
                && !otherPawn.Dead
                && !otherPawn.Destroyed
                && !otherPawn.Downed
                && otherPawn.RaceProps?.Humanlike == true
                && otherPawn.HostileTo(Faction.OfPlayer);

            if (canSuppressThreat)
            {
                Lord lord = otherPawn.GetLord();
                canSuppressThreat = lord?.CurLordToil == null || !lord.CurLordToil.AllowAggressiveTargetingOfRoamers;
            }

            if (currentTick > 0)
            {
                cachedThreatStateByPawnId[pawnId] = new CachedThreatState(currentTick + SmallPetStateCacheDurationTicks, canSuppressThreat);
            }

            return canSuppressThreat;
        }

        private static bool CanIgnoreSmallPetThreat(Pawn pawn, int currentTick)
        {
            if (pawn == null)
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

        private static bool IsEligibleSmallPetDef(ThingDef def, float threshold)
        {
            if (def?.race == null)
            {
                return false;
            }

            if (cachedSmallPetThreshold != threshold)
            {
                cachedEligibleSmallPetDefs.Clear();
                cachedSmallPetThreshold = threshold;
            }

            if (cachedEligibleSmallPetDefs.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = def.race.Animal && def.race.baseBodySize < threshold;
            cachedEligibleSmallPetDefs[def] = result;
            return result;
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

            cachedSmallPetStateCleanupBuffer.Clear();

            foreach (KeyValuePair<int, CachedThreatState> entry in cachedThreatStateByPawnId)
            {
                if (entry.Value.ValidUntilTick < currentTick)
                {
                    cachedSmallPetStateCleanupBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < cachedSmallPetStateCleanupBuffer.Count; i++)
            {
                cachedThreatStateByPawnId.Remove(cachedSmallPetStateCleanupBuffer[i]);
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

        private readonly struct CachedThreatState
        {
            public CachedThreatState(int validUntilTick, bool canSuppressThreat)
            {
                ValidUntilTick = validUntilTick;
                CanSuppressThreat = canSuppressThreat;
            }

            public int ValidUntilTick { get; }

            public bool CanSuppressThreat { get; }
        }
    }
}
