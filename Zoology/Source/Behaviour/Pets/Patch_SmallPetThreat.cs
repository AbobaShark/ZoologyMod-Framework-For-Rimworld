using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ThreatDisabled))]
    public static class Patch_SmallPetThreatDisabled
    {
        private const float DefaultSmallPetThreshold = ModConstants.DefaultSmallPetBodySizeThreshold;
        private const int SettingsSnapshotRefreshIntervalTicks = ZoologyTickLimiter.SmallPetThreat.SettingsSnapshotRefreshIntervalTicks;
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

        private static readonly Dictionary<int, HostilityCacheEntry> hostilityCacheByFactionId = new Dictionary<int, HostilityCacheEntry>(64);
        private static readonly HashSet<int> smallPetCandidateOverflowIds = new HashSet<int>();

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
            hostilityCacheByFactionId.Clear();
            ClearCandidateMembershipLookup();
        }

        private static void ResetCachesForCurrentGameIfNeeded()
        {
            Game currentGame = Current.Game;
            if (!ReferenceEquals(cachedGame, currentGame))
            {
                ResetCachesForGame(currentGame);
            }
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

            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                List<Pawn> playerPawns = maps[mapIndex]?.mapPawns?.SpawnedPawnsInFaction(playerFaction);
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
            ResetCachesForCurrentGameIfNeeded();
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

        private static bool IsCurrentSmallPetCandidate(Pawn pawn, int currentTick)
        {
            Faction playerFaction = GetPlayerFactionCached();
            if (playerFaction == null)
            {
                return false;
            }

            EnsureSettingsSnapshot(currentTick);
            if (!cachedIgnoreSmallPetsEnabled)
            {
                return false;
            }

            if (smallPetCandidateSetInitialized)
            {
                return hasAnySmallPetCandidates && pawn != null && CandidateMembershipContains(pawn.thingIDNumber);
            }

            return IsSmallPetCandidate(pawn, playerFaction, cachedSmallPetThreshold);
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
        }

        private static void RemoveCachedMembership(Pawn pawn)
        {
            ResetCachesForCurrentGameIfNeeded();
            if (!smallPetCandidateSetInitialized || pawn == null)
            {
                return;
            }

            RemoveSmallPetCandidateId(pawn.thingIDNumber);
        }

        private static void InvalidateHostilityCache()
        {
            hostilityCacheByFactionId.Clear();
        }

        private static bool IsCandidateSourceEligible(Pawn pawn, int currentTick)
        {
            if (pawn == null)
            {
                return false;
            }

            if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn))
            {
                return false;
            }

            if (pawn.InAggroMentalState || pawn.IsFighting())
            {
                return false;
            }

            int lastEngageTick = pawn.mindState?.lastEngageTargetTick ?? int.MinValue;
            return currentTick >= lastEngageTick + 360;
        }

        private static bool CanIgnoreThreatForSmallPet(Pawn smallPet, Pawn otherPawn, Faction playerFaction)
        {
            if (smallPet == null || playerFaction == null)
            {
                return false;
            }

            if (otherPawn == null)
            {
                return true;
            }

            if (HasAggressiveTargetingOverride(otherPawn, smallPet))
            {
                return false;
            }

            bool hostileThreat = true;
            if (otherPawn.RaceProps?.Humanlike != true)
            {
                Faction otherFaction = otherPawn.Faction;
                hostileThreat = otherFaction != null && IsHostileToPlayerFactionSafe(otherFaction, playerFaction);
            }

            if (!hostileThreat)
            {
                return false;
            }

            Lord lord = otherPawn.GetLord();
            if (lord != null
                && lord.CurLordToil != null
                && lord.CurLordToil.AllowAggressiveTargetingOfRoamers)
            {
                return false;
            }

            return !ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(otherPawn, smallPet);
        }

        private static bool ShouldDisableThreatForSmallPet(Pawn targetPawn, IAttackTargetSearcher disabledFor, int currentTick)
        {
            if (targetPawn == null || !EnsureSmallPetCandidateSetCached(currentTick))
            {
                return false;
            }

            Faction playerFaction = GetPlayerFactionCached();
            if (playerFaction == null)
            {
                return false;
            }

            Pawn searcherPawn = disabledFor?.Thing as Pawn;

            if (IsCurrentSmallPetCandidate(targetPawn, currentTick)
                && IsCandidateSourceEligible(targetPawn, currentTick))
            {
                return CanIgnoreThreatForSmallPet(targetPawn, searcherPawn, playerFaction);
            }

            if (searcherPawn == null
                || !IsCurrentSmallPetCandidate(searcherPawn, currentTick)
                || !IsCandidateSourceEligible(searcherPawn, currentTick))
            {
                return false;
            }

            return CanIgnoreThreatForSmallPet(searcherPawn, targetPawn, playerFaction);
        }

        private static bool IsFeatureEnabledNow()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            return settings == null || (!settings.DisableAllRuntimePatches && settings.EnableIgnoreSmallPetsByRaiders);
        }

        private static bool IsThreatPatchRuntimeEnabledNow()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            return settings == null || !settings.DisableAllRuntimePatches;
        }

        private static bool IsPlayerFactionRoamer(Pawn pawn)
        {
            return pawn != null
                && pawn.Roamer
                && ReferenceEquals(pawn.Faction, Faction.OfPlayerSilentFail);
        }

        private static bool ShouldAllowThreatForDirectRoamerMeleeRetaliation(Pawn threat, Pawn threatenedRoamer)
        {
            return threat != null
                && IsPlayerFactionRoamer(threatenedRoamer)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, threatenedRoamer);
        }

        private static bool ShouldForceMutualHostilityForDirectRoamerMeleeAttack(Pawn firstPawn, Pawn secondPawn)
        {
            return ShouldAllowThreatForDirectRoamerMeleeRetaliation(firstPawn, secondPawn)
                || ShouldAllowThreatForDirectRoamerMeleeRetaliation(secondPawn, firstPawn);
        }

        private static bool HasAggressiveTargetingOverride(Pawn threat, Pawn target)
        {
            if (threat == null || target == null)
            {
                return false;
            }

            if (threat.kindDef?.hostileToAll == true || target.kindDef?.hostileToAll == true)
            {
                return true;
            }

            MentalState threatMentalState = threat.MentalState;
            if (threatMentalState != null && threatMentalState.ForceHostileTo(target))
            {
                return true;
            }

            MentalState targetMentalState = target.MentalState;
            if (targetMentalState != null && targetMentalState.ForceHostileTo(threat))
            {
                return true;
            }

            if (threat.IsPrisoner
                && ReferenceEquals(threat.HostFaction, target.Faction)
                && PrisonBreakUtility.IsPrisonBreaking(threat))
            {
                return true;
            }

            if (threat.IsSlave
                && ReferenceEquals(threat.Faction, target.Faction)
                && SlaveRebellionUtility.IsRebelling(threat))
            {
                return true;
            }

            if (threat.InAggroMentalState || target.InAggroMentalState)
            {
                return true;
            }

            if (ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, target))
            {
                return true;
            }

            Lord lord = threat.GetLord();
            return lord?.CurLordToil != null
                && lord.CurLordToil.AllowAggressiveTargetingOfRoamers;
        }

        public static bool Prepare()
        {
            return IsThreatPatchRuntimeEnabledNow();
        }

        public static void Postfix(Pawn __instance, IAttackTargetSearcher disabledFor, ref bool __result)
        {
            if (__instance == null)
            {
                return;
            }

            Pawn disabledForPawn = disabledFor?.Thing as Pawn;
            if (__result)
            {
                if (ShouldAllowThreatForDirectRoamerMeleeRetaliation(__instance, disabledForPawn))
                {
                    __result = false;
                }

                return;
            }

            if (!IsFeatureEnabledNow())
            {
                return;
            }

            ResetCachesForCurrentGameIfNeeded();
            if (cachedGame == null)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (ShouldDisableThreatForSmallPet(__instance, disabledFor, currentTick))
            {
                __result = true;
            }
        }

        internal static bool ShouldPreventSmallPetMeleeRetaliation(Pawn smallPet, Pawn threat, int currentTick)
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            if (settings != null
                && (!settings.EnableIgnoreSmallPetsByRaiders || !settings.EnableSmallPetNoMeleeRetaliation))
            {
                return false;
            }

            if (smallPet == null || threat == null || !EnsureSmallPetCandidateSetCached(currentTick))
            {
                return false;
            }

            Faction playerFaction = GetPlayerFactionCached();
            if (playerFaction == null
                || !IsCurrentSmallPetCandidate(smallPet, currentTick)
                || !IsCandidateSourceEligible(smallPet, currentTick))
            {
                return false;
            }

            return CanIgnoreThreatForSmallPet(smallPet, threat, playerFaction);
        }

        private static bool IsNoMeleeRetaliationEnabled()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            return settings == null
                || (!settings.DisableAllRuntimePatches
                    && settings.EnableIgnoreSmallPetsByRaiders
                    && settings.EnableSmallPetNoMeleeRetaliation);
        }

        private static bool ShouldSuppressMutualHostility(Pawn firstPawn, Pawn secondPawn, int currentTick)
        {
            if (!IsNoMeleeRetaliationEnabled() || firstPawn == null || secondPawn == null)
            {
                return false;
            }

            return ShouldDisableThreatForSmallPet(firstPawn, secondPawn, currentTick)
                || ShouldDisableThreatForSmallPet(secondPawn, firstPawn, currentTick);
        }

        [HarmonyPatch(typeof(Pawn_MindState), "get_MeleeThreatStillThreat")]
        private static class Patch_Pawn_MindState_MeleeThreatStillThreat_SmallPetIgnore
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Postfix(Pawn_MindState __instance, ref bool __result)
            {
                if (!__result || __instance == null)
                {
                    return;
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                Pawn smallPet = __instance.pawn;
                Pawn threat = __instance.meleeThreat;
                if (!ShouldPreventSmallPetMeleeRetaliation(smallPet, threat, currentTick))
                {
                    return;
                }

                __instance.meleeThreat = null;
                __result = false;
            }
        }

        [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
        private static class Patch_JobGiver_AIFightEnemy_TryGiveJob_SmallPetIgnore
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Postfix(Pawn pawn, ref Job __result)
            {
                if (__result?.def != JobDefOf.AttackMelee)
                {
                    return;
                }

                Pawn threat = __result.targetA.Thing as Pawn;
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!ShouldPreventSmallPetMeleeRetaliation(pawn, threat, currentTick))
                {
                    return;
                }

                if (pawn?.mindState != null && ReferenceEquals(pawn.mindState.enemyTarget, threat))
                {
                    pawn.mindState.enemyTarget = null;
                }

                __result = null;
            }
        }

        [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
        private static class Patch_GenHostility_HostileTo_ThingThing_SmallPetIgnore
        {
            private static bool Prepare() => IsThreatPatchRuntimeEnabledNow();

            private static void Postfix(Thing a, Thing b, ref bool __result)
            {
                if (a is not Pawn pawnA || b is not Pawn pawnB)
                {
                    return;
                }

                if (ShouldForceMutualHostilityForDirectRoamerMeleeAttack(pawnA, pawnB))
                {
                    __result = true;
                    return;
                }

                if (!IsNoMeleeRetaliationEnabled() || !__result)
                {
                    return;
                }

                ResetCachesForCurrentGameIfNeeded();
                if (cachedGame == null)
                {
                    return;
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (ShouldSuppressMutualHostility(pawnA, pawnB, currentTick))
                {
                    __result = false;
                }
            }
        }

        [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) })]
        private static class Patch_GenHostility_HostileTo_ThingFaction_SmallPetIgnore
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Postfix(Thing t, Faction fac, ref bool __result)
            {
                if (!IsNoMeleeRetaliationEnabled() || !__result || t is not Pawn pawn)
                {
                    return;
                }

                ResetCachesForCurrentGameIfNeeded();
                if (cachedGame == null)
                {
                    return;
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!EnsureSmallPetCandidateSetCached(currentTick))
                {
                    return;
                }

                Faction playerFaction = GetPlayerFactionCached();
                if (playerFaction == null
                    || !IsCurrentSmallPetCandidate(pawn, currentTick)
                    || !IsCandidateSourceEligible(pawn, currentTick)
                    || !IsHostileToPlayerFactionSafe(fac, playerFaction))
                {
                    return;
                }

                __result = false;
            }
        }

        [HarmonyPatch(typeof(AttackTargetsCache), nameof(AttackTargetsCache.GetPotentialTargetsFor))]
        private static class Patch_AttackTargetsCache_GetPotentialTargetsFor_SmallPetFilter
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Postfix(IAttackTargetSearcher th, ref List<IAttackTarget> __result)
            {
                if (__result == null || __result.Count == 0)
                {
                    return;
                }

                ResetCachesForCurrentGameIfNeeded();
                if (cachedGame == null)
                {
                    return;
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!EnsureSmallPetCandidateSetCached(currentTick))
                {
                    return;
                }

                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    IAttackTarget target = __result[i];
                    if (target?.Thing is not Pawn targetPawn || !IsCurrentSmallPetCandidate(targetPawn, currentTick))
                    {
                        continue;
                    }

                    if (target.ThreatDisabled(th))
                    {
                        __result.RemoveAt(i);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
        private static class Patch_Pawn_SpawnSetup_SmallPetCache
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Postfix(Pawn __instance)
            {
                UpdateCachedMembership(__instance);
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
        private static class Patch_Pawn_SetFaction_SmallPetCache
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Prefix(Pawn __instance)
            {
                RemoveCachedMembership(__instance);
            }

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
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Prefix(Pawn __instance)
            {
                RemoveCachedMembership(__instance);
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
        private static class Patch_Pawn_Kill_SmallPetCache
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Prefix(Pawn __instance)
            {
                RemoveCachedMembership(__instance);
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
        private static class Patch_Pawn_Destroy_SmallPetCache
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Prefix(Pawn __instance)
            {
                RemoveCachedMembership(__instance);
            }
        }

        [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
        private static class Patch_Faction_NotifyRelationKindChanged_SmallPetCache
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Postfix()
            {
                InvalidateHostilityCache();
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
        private static class Patch_Game_FinalizeInit_SmallPetCache
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Postfix(Game __instance)
            {
                ResetCachesForGame(__instance);
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
        private static class Patch_Game_Dispose_SmallPetCache
        {
            private static bool Prepare() => IsFeatureEnabledNow();

            private static void Prefix(Game __instance)
            {
                if (ReferenceEquals(cachedGame, __instance))
                {
                    ResetCachesForGame(null);
                }
            }
        }
    }
}
