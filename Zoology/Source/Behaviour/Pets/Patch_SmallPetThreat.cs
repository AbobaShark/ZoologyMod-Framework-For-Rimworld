using System.Collections.Generic;
using System;
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
        private const int SettingsSnapshotRefreshIntervalTicks = ZoologyTickLimiter.SmallPetThreat.SettingsSnapshotRefreshIntervalTicks;
        private const int SmallPetStateCacheDurationTicks = ZoologyTickLimiter.SmallPetThreat.StateCacheDurationTicks;
        private const int SmallPetStateCacheCleanupIntervalTicks = ZoologyTickLimiter.SmallPetThreat.StateCacheCleanupIntervalTicks;
        private const int SmallPetThreatBudgetPerTick = ZoologyTickLimiter.SmallPetThreat.BudgetPerTick;

        private static readonly Dictionary<ThingDef, bool> cachedEligibleSmallPetDefs = new Dictionary<ThingDef, bool>(64);
        private static readonly Dictionary<int, CachedSmallPetState> cachedSmallPetStateByPawnId = new Dictionary<int, CachedSmallPetState>(32);
        private static readonly Dictionary<long, CachedThreatState> cachedThreatStateByPairKey = new Dictionary<long, CachedThreatState>(64);
        private static readonly List<int> cachedSmallPetStateCleanupBuffer = new List<int>(32);
        private static readonly List<long> cachedThreatStateCleanupBuffer = new List<long>(32);
        private static ZoologyModSettings cachedSettings;
        private static bool cachedIgnoreSmallPetsByRaidersEnabled = true;
        private static float cachedSmallPetThresholdSnapshot = ModConstants.DefaultSmallPetBodySizeThreshold;
        private static int lastSmallPetStateCacheCleanupTick = -SmallPetStateCacheCleanupIntervalTicks;
        private static int lastSettingsSnapshotTick = -SettingsSnapshotRefreshIntervalTicks;
        private static float cachedSmallPetThreshold = -1f;
        private static int smallPetThreatBudgetTick = -1;
        private static int smallPetThreatBudgetRemaining = 0;
        [ThreadStatic]
        private static bool isInsidePrefix;

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

            if (isInsidePrefix)
            {
                return true;
            }

            Faction instanceFaction = __instance.Faction;
            Faction otherFaction = otherPawn.Faction;
            if (instanceFaction != Faction.OfPlayer && otherFaction != Faction.OfPlayer)
            {
                return true;
            }

            RaceProperties instanceRace = __instance.RaceProps;
            RaceProperties otherRace = otherPawn.RaceProps;
            bool instanceAnimal = instanceRace?.Animal == true;
            bool otherAnimal = otherRace?.Animal == true;
            if (!instanceAnimal && !otherAnimal)
            {
                return true;
            }

            bool instanceHumanlike = instanceRace?.Humanlike == true;
            bool otherHumanlike = otherRace?.Humanlike == true;
            if (!instanceHumanlike && !otherHumanlike)
            {
                return true;
            }

            isInsidePrefix = true;
            try
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!TryGetSettingsSnapshot(currentTick, out float smallPetThreshold))
                {
                    return true;
                }

                if (!TryConsumeSmallPetThreatBudget(currentTick))
                {
                    return true;
                }

                if (!TryResolveSmallPetAndThreat(__instance, otherPawn, out Pawn smallPet, out Pawn threatPawn))
                {
                    return true;
                }

                if (!IsEligibleSmallPetDef(smallPet.def, smallPetThreshold))
                {
                    return true;
                }

                if (!CanSuppressThreatForSmallPets(threatPawn, smallPet, currentTick))
                {
                    return true;
                }

                if (!CanIgnoreSmallPetThreat(smallPet, currentTick))
                {
                    return true;
                }

                __result = true;
                return false;
            }
            finally
            {
                isInsidePrefix = false;
            }
        }

        private static bool TryResolveSmallPetAndThreat(Pawn firstPawn, Pawn secondPawn, out Pawn smallPet, out Pawn threatPawn)
        {
            smallPet = null;
            threatPawn = null;

            if (IsPotentialSmallPetCandidate(firstPawn) && IsPotentialThreatPawn(secondPawn, firstPawn))
            {
                smallPet = firstPawn;
                threatPawn = secondPawn;
                return true;
            }

            if (IsPotentialSmallPetCandidate(secondPawn) && IsPotentialThreatPawn(firstPawn, secondPawn))
            {
                smallPet = secondPawn;
                threatPawn = firstPawn;
                return true;
            }

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

        private static bool TryConsumeSmallPetThreatBudget(int currentTick)
        {
            if (currentTick <= 0)
            {
                return true;
            }

            if (smallPetThreatBudgetTick != currentTick)
            {
                smallPetThreatBudgetTick = currentTick;
                smallPetThreatBudgetRemaining = SmallPetThreatBudgetPerTick;
            }

            if (smallPetThreatBudgetRemaining <= 0)
            {
                return false;
            }

            smallPetThreatBudgetRemaining--;
            return true;
        }

        private static bool CanSuppressThreatForSmallPets(Pawn otherPawn, Pawn smallPet, int currentTick)
        {
            if (otherPawn == null || smallPet == null)
            {
                return false;
            }

            long pairKey = MakeThreatPairKey(otherPawn, smallPet);
            if (currentTick > 0
                && pairKey != 0L
                && cachedThreatStateByPairKey.TryGetValue(pairKey, out CachedThreatState cached)
                && cached.ValidUntilTick >= currentTick)
            {
                return cached.CanSuppressThreat;
            }

            bool canSuppressThreat = otherPawn.Spawned
                && !otherPawn.Dead
                && !otherPawn.Destroyed
                && !otherPawn.Downed
                && otherPawn.RaceProps?.Humanlike == true
                && IsHostileToSmallPetFaction(otherPawn, smallPet);

            if (canSuppressThreat)
            {
                Lord lord = otherPawn.GetLord();
                canSuppressThreat = lord?.CurLordToil == null || !lord.CurLordToil.AllowAggressiveTargetingOfRoamers;
            }

            if (currentTick > 0)
            {
                if (pairKey != 0L)
                {
                    cachedThreatStateByPairKey[pairKey] = new CachedThreatState(currentTick + SmallPetStateCacheDurationTicks, canSuppressThreat);
                }
            }

            return canSuppressThreat;
        }

        private static bool IsHostileToSmallPetFaction(Pawn threatPawn, Pawn smallPet)
        {
            if (threatPawn == null || smallPet == null)
            {
                return false;
            }

            Faction smallPetFaction = smallPet.Faction;
            if (smallPetFaction == null)
            {
                return false;
            }

            Faction threatFaction = threatPawn.Faction;
            if (threatFaction != null)
            {
                return threatFaction.HostileTo(smallPetFaction);
            }

            return threatPawn.HostileTo(smallPetFaction);
        }

        private static long MakeThreatPairKey(Pawn threatPawn, Pawn smallPet)
        {
            if (threatPawn == null || smallPet == null)
            {
                return 0L;
            }

            uint threatId = (uint)threatPawn.thingIDNumber;
            uint smallPetId = (uint)smallPet.thingIDNumber;
            return ((long)threatId << 32) | smallPetId;
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
                && def.race.baseBodySize < threshold;
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

            cachedThreatStateCleanupBuffer.Clear();

            foreach (KeyValuePair<long, CachedThreatState> entry in cachedThreatStateByPairKey)
            {
                if (entry.Value.ValidUntilTick < currentTick)
                {
                    cachedThreatStateCleanupBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < cachedThreatStateCleanupBuffer.Count; i++)
            {
                cachedThreatStateByPairKey.Remove(cachedThreatStateCleanupBuffer[i]);
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
