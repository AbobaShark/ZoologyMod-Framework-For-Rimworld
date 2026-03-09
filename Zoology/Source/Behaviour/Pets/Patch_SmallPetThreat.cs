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
        private const int SettingsSnapshotRefreshIntervalTicks = 60;
        private const int SmallPetStateCacheDurationTicks = 15;
        private const int SmallPetStateCacheCleanupIntervalTicks = 600;

        private static readonly Dictionary<ThingDef, bool> cachedEligibleSmallPetDefs = new Dictionary<ThingDef, bool>(64);
        private static readonly Dictionary<int, CachedSmallPetState> cachedSmallPetStateByPawnId = new Dictionary<int, CachedSmallPetState>(32);
        private static readonly Dictionary<int, CachedThreatState> cachedThreatStateByPawnId = new Dictionary<int, CachedThreatState>(32);
        private static readonly List<int> cachedSmallPetStateCleanupBuffer = new List<int>(32);
        private static ZoologyModSettings cachedSettings;
        private static bool cachedIgnoreSmallPetsByRaidersEnabled = true;
        private static float cachedSmallPetThresholdSnapshot = ModConstants.DefaultSmallPetBodySizeThreshold;
        private static int lastSmallPetStateCacheCleanupTick = -SmallPetStateCacheCleanupIntervalTicks;
        private static int lastSettingsSnapshotTick = -SettingsSnapshotRefreshIntervalTicks;
        private static float cachedSmallPetThreshold = -1f;

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableIgnoreSmallPetsByRaiders;
        }

        public static bool Prefix(Pawn __instance, Pawn otherPawn, ref bool __result)
        {
            if (__instance == null
                || otherPawn == null
                || Current.ProgramState != ProgramState.Playing
                || Current.Game == null)
            {
                return true;
            }

            if (!IsPotentialThreatPawn(otherPawn, __instance))
            {
                return true;
            }

            if (!IsPotentialSmallPetCandidate(__instance))
            {
                return true;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!TryGetSettingsSnapshot(currentTick, out float smallPetThreshold)
                || !IsEligibleSmallPetDef(__instance.def, smallPetThreshold))
            {
                return true;
            }

            if (!CanSuppressThreatForSmallPets(otherPawn, __instance, currentTick))
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

        private static bool IsPotentialThreatPawn(Pawn otherPawn, Pawn smallPet)
        {
            return otherPawn != null
                && smallPet != null
                && otherPawn != smallPet
                && otherPawn.Spawned
                && !otherPawn.Dead
                && !otherPawn.Destroyed
                && !otherPawn.Downed
                && otherPawn.Map != null
                && otherPawn.Map == smallPet.Map
                && otherPawn.RaceProps?.Humanlike == true;
        }

        private static bool IsPotentialSmallPetCandidate(Pawn pawn)
        {
            Faction pawnFaction = pawn?.Faction;
            if (pawn == null
                || !pawn.Spawned
                || pawn.Dead
                || pawn.Destroyed
                || pawn.Downed
                || pawn.Roamer
                || pawnFaction == null
                || !pawnFaction.IsPlayer)
            {
                return false;
            }

            return pawn.RaceProps?.Animal == true;
        }

        private static bool TryGetSettingsSnapshot(int currentTick, out float smallPetThreshold)
        {
            smallPetThreshold = cachedSmallPetThresholdSnapshot;
            bool needsRefresh = cachedSettings == null
                || currentTick <= 0
                || currentTick - lastSettingsSnapshotTick >= SettingsSnapshotRefreshIntervalTicks;

            if (needsRefresh)
            {
                cachedSettings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
                if (cachedSettings == null)
                {
                    return false;
                }

                cachedIgnoreSmallPetsByRaidersEnabled = cachedSettings.EnableIgnoreSmallPetsByRaiders;
                cachedSmallPetThresholdSnapshot = cachedSettings.SmallPetBodySizeThreshold;
                lastSettingsSnapshotTick = currentTick > 0 ? currentTick : 0;
            }

            smallPetThreshold = cachedSmallPetThresholdSnapshot;
            return cachedIgnoreSmallPetsByRaidersEnabled;
        }

        private static bool CanSuppressThreatForSmallPets(Pawn otherPawn, Pawn smallPet, int currentTick)
        {
            if (otherPawn == null || smallPet == null)
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
                && otherPawn.HostileTo(smallPet);

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

            bool result = def.race.Animal
                && def.race.baseBodySize < threshold
                && def.race.roamMtbDays <= 0f;
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
