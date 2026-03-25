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
        private const int MaxSourceEligibilityCacheEntries = 1024;
        private const int MaxOtherThreatCacheEntries = 4096;
        private const int CandidateMarkInitialSize = 4096;
        private const int CandidateMarkMaxDirectId = 1_000_000;

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
        private static int cachedTicksGame;
        private static bool cachedTicksGameValid;
        private static int[] smallPetCandidateMarks = new int[CandidateMarkInitialSize];
        private static int smallPetCandidateMarkVersion = 1;

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

        private static readonly Dictionary<int, HostilityCacheEntry> hostilityCacheByFactionId = new Dictionary<int, HostilityCacheEntry>(64);
        private static readonly HashSet<int> smallPetCandidateOverflowIds = new HashSet<int>();
        private static readonly Dictionary<int, SourceEligibilityEntry> sourceEligibilityCacheByPawnId = new Dictionary<int, SourceEligibilityEntry>(128);
        private static readonly Dictionary<int, OtherThreatEntry> otherThreatCacheByPawnId = new Dictionary<int, OtherThreatEntry>(128);

        private static void ResetCachesForGame(Game currentGame)
        {
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
            cachedTicksGame = 0;
            cachedTicksGameValid = false;
            hostilityCacheByFactionId.Clear();
            ClearCandidateMembershipLookup();
            sourceEligibilityCacheByPawnId.Clear();
            otherThreatCacheByPawnId.Clear();
        }

        private static void ResetCachesForCurrentGameIfNeeded()
        {
            Game currentGame = Current.Game;
            if (ReferenceEquals(cachedGame, currentGame))
            {
                return;
            }

            ResetCachesForGame(currentGame);
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
        }

        private static bool EnsureCandidateMarkCapacity(int pawnId)
        {
            if (pawnId < 0)
            {
                return false;
            }

            int[] marks = smallPetCandidateMarks;
            if (pawnId < marks.Length)
            {
                return true;
            }

            if (pawnId > CandidateMarkMaxDirectId)
            {
                return false;
            }

            int newSize = marks.Length;
            while (newSize <= pawnId && newSize < CandidateMarkMaxDirectId)
            {
                int next = newSize << 1;
                if (next <= 0 || next > CandidateMarkMaxDirectId)
                {
                    newSize = CandidateMarkMaxDirectId;
                    break;
                }

                newSize = next;
            }

            if (newSize <= pawnId)
            {
                return false;
            }

            System.Array.Resize(ref smallPetCandidateMarks, newSize);
            return true;
        }

        private static void ClearCandidateMembershipLookup()
        {
            if (smallPetCandidateMarkVersion == int.MaxValue)
            {
                System.Array.Clear(smallPetCandidateMarks, 0, smallPetCandidateMarks.Length);
                smallPetCandidateMarkVersion = 1;
            }
            else
            {
                smallPetCandidateMarkVersion++;
            }

            smallPetCandidateOverflowIds.Clear();
        }

        private static bool CandidateMembershipContains(int pawnId)
        {
            if (pawnId < 0)
            {
                return false;
            }

            int[] marks = smallPetCandidateMarks;
            if (pawnId < marks.Length)
            {
                return marks[pawnId] == smallPetCandidateMarkVersion;
            }

            return smallPetCandidateOverflowIds.Contains(pawnId);
        }

        private static bool CandidateMembershipAdd(int pawnId)
        {
            if (pawnId < 0)
            {
                return false;
            }

            if (EnsureCandidateMarkCapacity(pawnId))
            {
                int[] marks = smallPetCandidateMarks;
                if (marks[pawnId] == smallPetCandidateMarkVersion)
                {
                    return false;
                }

                marks[pawnId] = smallPetCandidateMarkVersion;
                return true;
            }

            return smallPetCandidateOverflowIds.Add(pawnId);
        }

        private static bool CandidateMembershipRemove(int pawnId)
        {
            if (pawnId < 0)
            {
                return false;
            }

            int[] marks = smallPetCandidateMarks;
            if (pawnId < marks.Length)
            {
                if (marks[pawnId] != smallPetCandidateMarkVersion)
                {
                    return false;
                }

                marks[pawnId] = 0;
                return true;
            }

            return smallPetCandidateOverflowIds.Remove(pawnId);
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
            ClearCandidateMembershipLookup();
            smallPetCandidateCount = 0;
            hasAnySmallPetCandidates = false;
        }

        private static void AddSmallPetCandidateId(int pawnId)
        {
            if (pawnId < 0 || !CandidateMembershipAdd(pawnId))
            {
                return;
            }

            smallPetCandidateCount++;
            hasAnySmallPetCandidates = true;
        }

        private static void RemoveSmallPetCandidateId(int pawnId)
        {
            if (pawnId < 0 || !CandidateMembershipRemove(pawnId))
            {
                return;
            }

            if (smallPetCandidateCount > 0)
            {
                smallPetCandidateCount--;
            }

            hasAnySmallPetCandidates = smallPetCandidateCount > 0;
        }

        private static int GetCurrentTickFast()
        {
            if (cachedTicksGameValid)
            {
                return cachedTicksGame;
            }

            TickManager tickManager = cachedGame?.tickManager;
            if (tickManager != null)
            {
                cachedTicksGame = tickManager.TicksGame;
                cachedTicksGameValid = true;
                return cachedTicksGame;
            }

            return 0;
        }

        private static void EnsureSettingsSnapshot(int currentTick)
        {
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

        private static bool EnsureSmallPetCandidateSetCached(int currentTick)
        {
            EnsureSettingsSnapshot(currentTick);
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
        }

        private static bool IsCandidateSourceEligibleCached(Pawn pawn, int pawnId, int currentTick)
        {
            if (pawn == null)
            {
                return false;
            }

            if (sourceEligibilityCacheByPawnId.TryGetValue(pawnId, out SourceEligibilityEntry cached)
                && ReferenceEquals(cached.Pawn, pawn)
                && currentTick - cached.Tick <= StateCacheDurationTicks)
            {
                return cached.Eligible;
            }

            if (!CandidateMembershipContains(pawnId))
            {
                return false;
            }

            bool eligible = true;
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

            if (!sourceEligibilityCacheByPawnId.ContainsKey(pawnId)
                && sourceEligibilityCacheByPawnId.Count >= MaxSourceEligibilityCacheEntries)
            {
                sourceEligibilityCacheByPawnId.Clear();
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

            if (!otherThreatCacheByPawnId.ContainsKey(pawnId)
                && otherThreatCacheByPawnId.Count >= MaxOtherThreatCacheEntries)
            {
                otherThreatCacheByPawnId.Clear();
            }

            otherThreatCacheByPawnId[pawnId] = new OtherThreatEntry(otherPawn, currentTick, canThreat);
            return canThreat;
        }

        private static bool CouldBeMeleeThreat(Pawn otherPawn, Pawn pawn)
        {
            if (otherPawn == null || pawn == null)
            {
                return false;
            }

            if (otherPawn.Destroyed || otherPawn.Dead || otherPawn.Downed || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (!otherPawn.Spawned || !pawn.Spawned || otherPawn.Map != pawn.Map)
            {
                return false;
            }

            return GenAdj.AdjacentTo8WayOrInside(otherPawn.Position, pawn.Position);
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

            if (cachedGame == null)
            {
                Game currentGame = Current.Game;
                if (!ReferenceEquals(cachedGame, currentGame))
                {
                    ResetCachesForGame(currentGame);
                }

                if (cachedGame == null)
                {
                    return;
                }
            }

            int currentTick = GetCurrentTickFast();

            // Vanilla already disabled threat for a non-aggressive roamer;
            // keep threat enabled only while the "threat" is actively melee-attacking this pawn.
            if (__result)
            {
                if (CouldBeMeleeThreat(otherPawn, __instance)
                    && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(otherPawn, __instance))
                {
                    __result = false;
                }
                return;
            }

            EnsureSettingsSnapshot(currentTick);
            if (!cachedIgnoreSmallPetsEnabled)
            {
                return;
            }

            if (!smallPetCandidateSetInitialized && !EnsureSmallPetCandidateSetCached(currentTick))
            {
                return;
            }

            if (!hasAnySmallPetCandidates)
            {
                return;
            }

            int sourceId = __instance.thingIDNumber;
            if (!CandidateMembershipContains(sourceId))
            {
                return;
            }

            if (!IsCandidateSourceEligibleCached(__instance, sourceId, currentTick))
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

            if (CouldBeMeleeThreat(otherPawn, __instance)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(otherPawn, __instance))
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

        [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
        private static class Patch_Game_FinalizeInit_SmallPetCache
        {
            private static void Postfix(Game __instance)
            {
                ResetCachesForGame(__instance);
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
        private static class Patch_Game_Dispose_SmallPetCache
        {
            private static void Prefix(Game __instance)
            {
                if (ReferenceEquals(cachedGame, __instance))
                {
                    ResetCachesForGame(null);
                }
            }
        }

        [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
        private static class Patch_TickManager_DoSingleTick_SmallPetCache
        {
            private static void Prefix(TickManager __instance)
            {
                int increment = DebugSettings.fastEcology ? 2000 : 1;
                cachedTicksGame = __instance.TicksGame + increment;
                cachedTicksGameValid = true;
            }
        }
    }
}
