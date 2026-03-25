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
        private const float DefaultSmallPetThreshold = ModConstants.DefaultSmallPetBodySizeThreshold;
        private const int SettingsSnapshotRefreshIntervalTicks = ZoologyTickLimiter.SmallPetThreat.SettingsSnapshotRefreshIntervalTicks;
        private const int StateCacheDurationTicks = ZoologyTickLimiter.SmallPetThreat.StateCacheDurationTicks;
        private const int StateCacheCleanupIntervalTicks = ZoologyTickLimiter.SmallPetThreat.StateCacheCleanupIntervalTicks;

        private static Game cachedGame;
        private static Faction cachedPlayerFaction;
        private static float cachedSmallPetThreshold = -1f;
        private static bool cachedIgnoreSmallPetsEnabled = true;
        private static bool settingsSnapshotInitialized;
        private static bool settingsSnapshotDirty = true;
        private static int settingsSnapshotLastRefreshTick = int.MinValue;
        private static bool smallPetCandidateSetInitialized;
        private static int smallPetCandidateCount;
        private static bool hasAnySmallPetCandidates;
        private static int lastStateCacheCleanupTick = int.MinValue;

        private readonly struct HostilityCacheEntry
        {
            public HostilityCacheEntry(bool hostile)
            {
                Hostile = hostile;
            }

            public bool Hostile { get; }
        }

        private readonly struct SourceEligibilityEntry
        {
            public SourceEligibilityEntry(Pawn pawn, int tick, bool eligible)
            {
                Pawn = pawn;
                Tick = tick;
                Eligible = eligible;
            }

            public Pawn Pawn { get; }
            public int Tick { get; }
            public bool Eligible { get; }
        }

        private readonly struct OtherThreatEntry
        {
            public OtherThreatEntry(Pawn pawn, int tick, bool canThreat)
            {
                Pawn = pawn;
                Tick = tick;
                CanThreat = canThreat;
            }

            public Pawn Pawn { get; }
            public int Tick { get; }
            public bool CanThreat { get; }
        }

        private readonly struct MeleeThreatEntry
        {
            public MeleeThreatEntry(int tick, bool attacking)
            {
                Tick = tick;
                Attacking = attacking;
            }

            public int Tick { get; }
            public bool Attacking { get; }
        }

        private static readonly Dictionary<int, HostilityCacheEntry> hostilityCacheByFactionId = new Dictionary<int, HostilityCacheEntry>(64);
        private static readonly HashSet<int> smallPetCandidatePawnIds = new HashSet<int>();
        private static readonly Dictionary<int, SourceEligibilityEntry> sourceEligibilityCacheByPawnId = new Dictionary<int, SourceEligibilityEntry>(128);
        private static readonly Dictionary<int, OtherThreatEntry> otherThreatCacheByPawnId = new Dictionary<int, OtherThreatEntry>(128);
        private static readonly Dictionary<long, MeleeThreatEntry> meleeThreatCacheByPair = new Dictionary<long, MeleeThreatEntry>(256);
        private static readonly List<int> intCleanupBuffer = new List<int>(128);
        private static readonly List<long> longCleanupBuffer = new List<long>(128);

        private static long MakePairKey(Pawn source, Pawn other)
        {
            uint sourceId = source != null ? (uint)source.thingIDNumber : 0u;
            uint otherId = other != null ? (uint)other.thingIDNumber : 0u;
            return ((long)sourceId << 32) | otherId;
        }

        private static void ResetCachesForCurrentGameIfNeeded()
        {
            Game currentGame = Current.Game;
            if (ReferenceEquals(cachedGame, currentGame))
            {
                return;
            }

            cachedGame = currentGame;
            cachedPlayerFaction = null;
            cachedSmallPetThreshold = -1f;
            cachedIgnoreSmallPetsEnabled = true;
            settingsSnapshotInitialized = false;
            settingsSnapshotDirty = true;
            settingsSnapshotLastRefreshTick = int.MinValue;
            smallPetCandidateSetInitialized = false;
            smallPetCandidateCount = 0;
            hasAnySmallPetCandidates = false;
            lastStateCacheCleanupTick = int.MinValue;
            hostilityCacheByFactionId.Clear();
            smallPetCandidatePawnIds.Clear();
            sourceEligibilityCacheByPawnId.Clear();
            otherThreatCacheByPawnId.Clear();
            meleeThreatCacheByPair.Clear();
        }

        internal static void NotifySettingsChanged()
        {
            cachedSmallPetThreshold = -1f;
            settingsSnapshotInitialized = false;
            settingsSnapshotDirty = true;
            settingsSnapshotLastRefreshTick = int.MinValue;
            smallPetCandidateSetInitialized = false;
            ClearSmallPetCandidates();
            hostilityCacheByFactionId.Clear();
            sourceEligibilityCacheByPawnId.Clear();
            otherThreatCacheByPawnId.Clear();
            meleeThreatCacheByPair.Clear();
        }

        private static Faction GetPlayerFactionCached()
        {
            if (cachedGame == null)
            {
                return null;
            }

            cachedPlayerFaction ??= Faction.OfPlayerSilentFail;
            return cachedPlayerFaction;
        }

        private static bool IsHostileToPlayerFactionSafe(Faction faction, Faction playerFaction)
        {
            if (faction == null || playerFaction == null || ReferenceEquals(faction, playerFaction))
            {
                return false;
            }

            int factionId = faction.loadID;
            if (factionId >= 0 && hostilityCacheByFactionId.TryGetValue(factionId, out HostilityCacheEntry cached))
            {
                return cached.Hostile;
            }

            FactionRelation relation = faction.RelationWith(playerFaction, allowNull: true);
            bool hostile = relation != null && relation.kind == FactionRelationKind.Hostile;
            if (factionId >= 0)
            {
                hostilityCacheByFactionId[factionId] = new HostilityCacheEntry(hostile);
            }

            return hostile;
        }

        private static bool IsSmallPetCandidate(Pawn pawn, Faction playerFaction, float smallPetThreshold)
        {
            if (pawn == null || playerFaction == null)
            {
                return false;
            }

            if (pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (!ReferenceEquals(pawn.Faction, playerFaction))
            {
                return false;
            }

            RaceProperties raceProps = pawn.RaceProps;
            return raceProps?.Animal == true
                && !pawn.Roamer
                && raceProps.baseBodySize < smallPetThreshold;
        }

        private static void ClearSmallPetCandidates()
        {
            smallPetCandidatePawnIds.Clear();
            smallPetCandidateCount = 0;
            hasAnySmallPetCandidates = false;
        }

        private static void AddSmallPetCandidateId(int pawnId)
        {
            if (pawnId < 0 || !smallPetCandidatePawnIds.Add(pawnId))
            {
                return;
            }

            smallPetCandidateCount++;
            hasAnySmallPetCandidates = true;
        }

        private static void RemoveSmallPetCandidateId(int pawnId)
        {
            if (pawnId < 0 || !smallPetCandidatePawnIds.Remove(pawnId))
            {
                return;
            }

            if (smallPetCandidateCount > 0)
            {
                smallPetCandidateCount--;
            }

            hasAnySmallPetCandidates = smallPetCandidateCount > 0;
        }

        private static void EnsureSettingsSnapshot()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (settingsSnapshotInitialized
                && !settingsSnapshotDirty
                && currentTick - settingsSnapshotLastRefreshTick < SettingsSnapshotRefreshIntervalTicks)
            {
                return;
            }

            ZoologyModSettings settings = ZoologyModSettings.Instance;
            bool ignoreSmallPets = settings == null || settings.EnableIgnoreSmallPetsByRaiders;
            float threshold = settings?.SmallPetBodySizeThreshold ?? DefaultSmallPetThreshold;
            bool changed = !settingsSnapshotInitialized
                || ignoreSmallPets != cachedIgnoreSmallPetsEnabled
                || cachedSmallPetThreshold < 0f
                || threshold < cachedSmallPetThreshold - 0.0001f
                || threshold > cachedSmallPetThreshold + 0.0001f;

            cachedIgnoreSmallPetsEnabled = ignoreSmallPets;
            cachedSmallPetThreshold = threshold;
            settingsSnapshotInitialized = true;
            settingsSnapshotDirty = false;
            settingsSnapshotLastRefreshTick = currentTick;

            if (changed)
            {
                smallPetCandidateSetInitialized = false;
                ClearSmallPetCandidates();
                sourceEligibilityCacheByPawnId.Clear();
                otherThreatCacheByPawnId.Clear();
                meleeThreatCacheByPair.Clear();
            }
        }

        private static void RebuildSmallPetCandidateSet()
        {
            smallPetCandidateSetInitialized = true;
            ClearSmallPetCandidates();

            if (!cachedIgnoreSmallPetsEnabled)
            {
                return;
            }

            Faction playerFaction = GetPlayerFactionCached();
            List<Map> maps = Find.Maps;
            if (playerFaction == null || maps == null)
            {
                return;
            }

            for (int mi = 0; mi < maps.Count; mi++)
            {
                List<Pawn> playerPawns = maps[mi]?.mapPawns?.SpawnedPawnsInFaction(playerFaction);
                if (playerPawns == null || playerPawns.Count == 0)
                {
                    continue;
                }

                for (int i = 0; i < playerPawns.Count; i++)
                {
                    Pawn pawn = playerPawns[i];
                    if (IsSmallPetCandidate(pawn, playerFaction, cachedSmallPetThreshold))
                    {
                        AddSmallPetCandidateId(pawn.thingIDNumber);
                    }
                }
            }
        }

        private static bool EnsureSmallPetCandidateSetCached()
        {
            ResetCachesForCurrentGameIfNeeded();
            EnsureSettingsSnapshot();
            if (!cachedIgnoreSmallPetsEnabled)
            {
                return false;
            }

            if (!smallPetCandidateSetInitialized)
            {
                RebuildSmallPetCandidateSet();
            }

            return hasAnySmallPetCandidates;
        }

        private static void UpdateCachedMembership(Pawn pawn)
        {
            ResetCachesForCurrentGameIfNeeded();
            if (!smallPetCandidateSetInitialized)
            {
                return;
            }

            if (!cachedIgnoreSmallPetsEnabled)
            {
                RemoveSmallPetCandidateId(pawn?.thingIDNumber ?? -1);
                sourceEligibilityCacheByPawnId.Clear();
                otherThreatCacheByPawnId.Clear();
                meleeThreatCacheByPair.Clear();
                return;
            }

            Faction playerFaction = GetPlayerFactionCached();
            if (IsSmallPetCandidate(pawn, playerFaction, cachedSmallPetThreshold))
            {
                AddSmallPetCandidateId(pawn.thingIDNumber);
            }
            else if (pawn != null)
            {
                RemoveSmallPetCandidateId(pawn.thingIDNumber);
            }

            if (pawn != null)
            {
                int pawnId = pawn.thingIDNumber;
                sourceEligibilityCacheByPawnId.Remove(pawnId);
                otherThreatCacheByPawnId.Remove(pawnId);
            }
            meleeThreatCacheByPair.Clear();
        }

        private static void RemoveCachedMembership(Pawn pawn)
        {
            ResetCachesForCurrentGameIfNeeded();
            if (pawn != null)
            {
                int pawnId = pawn.thingIDNumber;
                sourceEligibilityCacheByPawnId.Remove(pawnId);
                otherThreatCacheByPawnId.Remove(pawnId);
            }
            meleeThreatCacheByPair.Clear();

            if (!smallPetCandidateSetInitialized || pawn == null)
            {
                return;
            }

            RemoveSmallPetCandidateId(pawn.thingIDNumber);
        }

        private static void InvalidateHostilityCache()
        {
            hostilityCacheByFactionId.Clear();
            otherThreatCacheByPawnId.Clear();
            meleeThreatCacheByPair.Clear();
        }

        private static void EnsureStateCacheCleanup(int currentTick)
        {
            if (currentTick <= 0)
            {
                return;
            }

            if (currentTick - lastStateCacheCleanupTick < StateCacheCleanupIntervalTicks)
            {
                return;
            }

            lastStateCacheCleanupTick = currentTick;

            intCleanupBuffer.Clear();
            foreach (KeyValuePair<int, SourceEligibilityEntry> kv in sourceEligibilityCacheByPawnId)
            {
                SourceEligibilityEntry entry = kv.Value;
                Pawn pawn = entry.Pawn;
                if (currentTick - entry.Tick > StateCacheDurationTicks
                    || pawn == null
                    || pawn.Destroyed
                    || pawn.Dead)
                {
                    intCleanupBuffer.Add(kv.Key);
                }
            }
            for (int i = 0; i < intCleanupBuffer.Count; i++)
            {
                sourceEligibilityCacheByPawnId.Remove(intCleanupBuffer[i]);
            }

            intCleanupBuffer.Clear();
            foreach (KeyValuePair<int, OtherThreatEntry> kv in otherThreatCacheByPawnId)
            {
                OtherThreatEntry entry = kv.Value;
                Pawn pawn = entry.Pawn;
                if (currentTick - entry.Tick > StateCacheDurationTicks
                    || pawn == null
                    || pawn.Destroyed
                    || pawn.Dead)
                {
                    intCleanupBuffer.Add(kv.Key);
                }
            }
            for (int i = 0; i < intCleanupBuffer.Count; i++)
            {
                otherThreatCacheByPawnId.Remove(intCleanupBuffer[i]);
            }

            longCleanupBuffer.Clear();
            foreach (KeyValuePair<long, MeleeThreatEntry> kv in meleeThreatCacheByPair)
            {
                if (currentTick - kv.Value.Tick > StateCacheDurationTicks)
                {
                    longCleanupBuffer.Add(kv.Key);
                }
            }
            for (int i = 0; i < longCleanupBuffer.Count; i++)
            {
                meleeThreatCacheByPair.Remove(longCleanupBuffer[i]);
            }
        }

        private static bool IsSourceEligibleCached(Pawn pawn, int currentTick)
        {
            if (pawn == null)
            {
                return false;
            }

            int pawnId = pawn.thingIDNumber;
            if (sourceEligibilityCacheByPawnId.TryGetValue(pawnId, out SourceEligibilityEntry cached)
                && ReferenceEquals(cached.Pawn, pawn)
                && currentTick - cached.Tick <= StateCacheDurationTicks)
            {
                return cached.Eligible;
            }

            bool eligible = hasAnySmallPetCandidates && smallPetCandidatePawnIds.Contains(pawnId);
            if (eligible)
            {
                if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn))
                {
                    eligible = false;
                }
                else if (pawn.InAggroMentalState || pawn.IsFighting())
                {
                    eligible = false;
                }
                else
                {
                    int lastEngageTick = pawn.mindState?.lastEngageTargetTick ?? int.MinValue;
                    if (currentTick < lastEngageTick + 360)
                    {
                        eligible = false;
                    }
                }
            }

            sourceEligibilityCacheByPawnId[pawnId] = new SourceEligibilityEntry(pawn, currentTick, eligible);
            return eligible;
        }

        private static bool CanOtherPawnThreatSmallPetCached(Pawn otherPawn, Faction playerFaction, int currentTick)
        {
            if (otherPawn == null || playerFaction == null)
            {
                return false;
            }

            int pawnId = otherPawn.thingIDNumber;
            if (otherThreatCacheByPawnId.TryGetValue(pawnId, out OtherThreatEntry cached)
                && ReferenceEquals(cached.Pawn, otherPawn)
                && currentTick - cached.Tick <= StateCacheDurationTicks)
            {
                return cached.CanThreat;
            }

            bool canThreat = true;
            if (otherPawn.RaceProps?.Humanlike != true)
            {
                Faction otherFaction = otherPawn.Faction;
                canThreat = otherFaction != null && IsHostileToPlayerFactionSafe(otherFaction, playerFaction);
            }

            if (canThreat)
            {
                Lord lord = otherPawn.GetLord();
                if (lord != null
                    && lord.CurLordToil != null
                    && lord.CurLordToil.AllowAggressiveTargetingOfRoamers)
                {
                    canThreat = false;
                }
            }

            otherThreatCacheByPawnId[pawnId] = new OtherThreatEntry(otherPawn, currentTick, canThreat);
            return canThreat;
        }

        private static bool IsThreatMeleeAttackingPawnCached(Pawn otherPawn, Pawn pawn, int currentTick)
        {
            if (otherPawn == null || pawn == null)
            {
                return false;
            }

            long pairKey = MakePairKey(pawn, otherPawn);
            if (meleeThreatCacheByPair.TryGetValue(pairKey, out MeleeThreatEntry cached)
                && currentTick - cached.Tick <= StateCacheDurationTicks)
            {
                return cached.Attacking;
            }

            bool attacking = ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(otherPawn, pawn);
            meleeThreatCacheByPair[pairKey] = new MeleeThreatEntry(currentTick, attacking);
            return attacking;
        }

        public static bool Prepare()
        {
            return true;
        }

        public static void Postfix(Pawn __instance, Pawn otherPawn, ref bool __result)
        {
            if (__instance == null || otherPawn == null)
            {
                return;
            }

            ResetCachesForCurrentGameIfNeeded();
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            EnsureStateCacheCleanup(currentTick);

            // Vanilla already disabled threat for a non-aggressive roamer;
            // keep threat enabled only while the "threat" is actively melee-attacking this pawn.
            if (__result)
            {
                if (IsThreatMeleeAttackingPawnCached(otherPawn, __instance, currentTick))
                {
                    __result = false;
                }
                return;
            }

            EnsureSettingsSnapshot();
            if (!cachedIgnoreSmallPetsEnabled)
            {
                return;
            }

            if (!smallPetCandidateSetInitialized && !EnsureSmallPetCandidateSetCached())
            {
                return;
            }

            if (!hasAnySmallPetCandidates)
            {
                return;
            }

            if (!IsSourceEligibleCached(__instance, currentTick))
            {
                return;
            }

            Faction playerFaction = GetPlayerFactionCached();
            if (playerFaction == null)
            {
                return;
            }

            if (!CanOtherPawnThreatSmallPetCached(otherPawn, playerFaction, currentTick))
            {
                return;
            }

            if (IsThreatMeleeAttackingPawnCached(otherPawn, __instance, currentTick))
            {
                __result = false;
                return;
            }

            __result = true;
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
        private static class Patch_Pawn_SpawnSetup_SmallPetCache
        {
            private static void Postfix(Pawn __instance)
            {
                UpdateCachedMembership(__instance);
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
        private static class Patch_Pawn_SetFaction_SmallPetCache
        {
            private static void Postfix(Pawn __instance)
            {
                UpdateCachedMembership(__instance);
                if (__instance?.Faction != null)
                {
                    hostilityCacheByFactionId.Remove(__instance.Faction.loadID);
                }
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
        private static class Patch_Pawn_DeSpawn_SmallPetCache
        {
            private static void Prefix(Pawn __instance)
            {
                RemoveCachedMembership(__instance);
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
        private static class Patch_Pawn_Kill_SmallPetCache
        {
            private static void Prefix(Pawn __instance)
            {
                RemoveCachedMembership(__instance);
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
        private static class Patch_Pawn_Destroy_SmallPetCache
        {
            private static void Prefix(Pawn __instance)
            {
                RemoveCachedMembership(__instance);
            }
        }

        [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
        private static class Patch_Faction_NotifyRelationKindChanged_SmallPetCache
        {
            private static void Postfix()
            {
                InvalidateHostilityCache();
            }
        }
    }
}
