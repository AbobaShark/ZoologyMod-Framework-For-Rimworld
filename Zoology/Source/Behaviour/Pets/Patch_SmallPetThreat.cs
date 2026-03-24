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

        private static Game cachedGame;
        private static Faction cachedPlayerFaction;
        private static float cachedSmallPetThreshold = -1f;
        private static bool cachedIgnoreSmallPetsEnabled = true;
        private static bool smallPetCandidateSetInitialized;
        private static int settingsSnapshotTick = int.MinValue;

        private readonly struct HostilityCacheEntry
        {
            public HostilityCacheEntry(bool hostile)
            {
                Hostile = hostile;
            }

            public bool Hostile { get; }
        }

        private static readonly Dictionary<int, HostilityCacheEntry> hostilityCacheByFactionId = new Dictionary<int, HostilityCacheEntry>(64);
        private static readonly HashSet<int> smallPetCandidatePawnIds = new HashSet<int>();

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
            smallPetCandidateSetInitialized = false;
            settingsSnapshotTick = int.MinValue;
            hostilityCacheByFactionId.Clear();
            smallPetCandidatePawnIds.Clear();
        }

        internal static void NotifySettingsChanged()
        {
            cachedSmallPetThreshold = -1f;
            smallPetCandidateSetInitialized = false;
            settingsSnapshotTick = int.MinValue;
            smallPetCandidatePawnIds.Clear();
            hostilityCacheByFactionId.Clear();
        }

        private static Faction GetPlayerFactionCached()
        {
            ResetCachesForCurrentGameIfNeeded();
            if (cachedGame == null)
            {
                return null;
            }

            cachedPlayerFaction ??= Faction.OfPlayerSilentFail;
            return cachedPlayerFaction;
        }

        private static bool IsHostileToPlayerFactionSafe(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            int factionId = faction.loadID;
            if (factionId >= 0 && hostilityCacheByFactionId.TryGetValue(factionId, out HostilityCacheEntry cached))
            {
                return cached.Hostile;
            }

            Faction playerFaction = GetPlayerFactionCached();
            if (playerFaction == null || ReferenceEquals(faction, playerFaction))
            {
                return false;
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

        private static void RefreshSettingsSnapshotIfNeeded()
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick > 0
                && settingsSnapshotTick > 0
                && tick - settingsSnapshotTick < SettingsSnapshotRefreshIntervalTicks)
            {
                return;
            }

            if (tick <= 0 && settingsSnapshotTick == 0)
            {
                return;
            }

            ZoologyModSettings settings = ZoologyModSettings.Instance;
            bool ignoreSmallPets = settings == null || settings.EnableIgnoreSmallPetsByRaiders;
            float threshold = settings?.SmallPetBodySizeThreshold ?? DefaultSmallPetThreshold;
            bool changed = ignoreSmallPets != cachedIgnoreSmallPetsEnabled
                || cachedSmallPetThreshold < 0f
                || threshold < cachedSmallPetThreshold - 0.0001f
                || threshold > cachedSmallPetThreshold + 0.0001f;

            if (changed)
            {
                cachedIgnoreSmallPetsEnabled = ignoreSmallPets;
                cachedSmallPetThreshold = threshold;
                smallPetCandidateSetInitialized = false;
                smallPetCandidatePawnIds.Clear();
            }

            settingsSnapshotTick = tick > 0 ? tick : 0;
        }

        private static void RebuildSmallPetCandidateSet()
        {
            smallPetCandidateSetInitialized = true;
            smallPetCandidatePawnIds.Clear();

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
                        smallPetCandidatePawnIds.Add(pawn.thingIDNumber);
                    }
                }
            }
        }

        private static bool EnsureSmallPetCandidateSetCached()
        {
            ResetCachesForCurrentGameIfNeeded();
            RefreshSettingsSnapshotIfNeeded();
            if (!cachedIgnoreSmallPetsEnabled)
            {
                return false;
            }

            if (!smallPetCandidateSetInitialized)
            {
                RebuildSmallPetCandidateSet();
            }

            return smallPetCandidatePawnIds.Count > 0;
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
                smallPetCandidatePawnIds.Remove(pawn?.thingIDNumber ?? -1);
                return;
            }

            Faction playerFaction = GetPlayerFactionCached();
            if (IsSmallPetCandidate(pawn, playerFaction, cachedSmallPetThreshold))
            {
                smallPetCandidatePawnIds.Add(pawn.thingIDNumber);
            }
            else if (pawn != null)
            {
                smallPetCandidatePawnIds.Remove(pawn.thingIDNumber);
            }
        }

        private static void RemoveCachedMembership(Pawn pawn)
        {
            ResetCachesForCurrentGameIfNeeded();
            if (!smallPetCandidateSetInitialized || pawn == null)
            {
                return;
            }

            smallPetCandidatePawnIds.Remove(pawn.thingIDNumber);
        }

        private static void InvalidateHostilityCache()
        {
            hostilityCacheByFactionId.Clear();
        }

        public static bool Prepare()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            return settings == null || settings.EnableIgnoreSmallPetsByRaiders;
        }

        public static bool Prefix(Pawn __instance, Pawn otherPawn, ref bool __result)
        {
            if (__instance == null || otherPawn == null)
            {
                return true;
            }

            RefreshSettingsSnapshotIfNeeded();
            if (!cachedIgnoreSmallPetsEnabled)
            {
                return true;
            }

            if (!smallPetCandidateSetInitialized && !EnsureSmallPetCandidateSetCached())
            {
                return true;
            }

            if (smallPetCandidatePawnIds.Count == 0 || !smallPetCandidatePawnIds.Contains(__instance.thingIDNumber))
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

            if (otherPawn.RaceProps?.Humanlike != true)
            {
                Faction otherFaction = otherPawn.Faction;
                if (otherFaction == null || !IsHostileToPlayerFactionSafe(otherFaction))
                {
                    return true;
                }
            }

            Lord lord = otherPawn.GetLord();
            if (lord != null && lord.CurLordToil != null && lord.CurLordToil.AllowAggressiveTargetingOfRoamers)
            {
                return true;
            }

            if (ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(otherPawn, __instance))
            {
                __result = false;
                return false;
            }

            __result = true;
            return false;
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
