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
        private static Game cachedGame;
        private static Faction cachedPlayerFaction;
        private static int nextPlayerFactionRefreshTick = int.MinValue;
        private static int lastHostilityCleanupTick = int.MinValue;
        private static int lastCandidateCleanupTick = int.MinValue;

        private const int PlayerFactionRefreshIntervalTicks = 300;
        private const int HostilityCacheDurationTicks = 300;
        private const int SmallPetCandidateCacheDurationTicks = 120;
        private const int CacheCleanupIntervalTicks = 1200;
        private const int MaxHostilityCacheSize = 256;
        private const int MaxCandidateCacheSize = 2048;

        private readonly struct HostilityCacheEntry
        {
            public HostilityCacheEntry(bool hostile, int validUntilTick)
            {
                Hostile = hostile;
                ValidUntilTick = validUntilTick;
            }

            public bool Hostile { get; }
            public int ValidUntilTick { get; }
        }

        private readonly struct SmallPetCandidateCacheEntry
        {
            public SmallPetCandidateCacheEntry(bool isCandidate, int validUntilTick)
            {
                IsCandidate = isCandidate;
                ValidUntilTick = validUntilTick;
            }

            public bool IsCandidate { get; }
            public int ValidUntilTick { get; }
        }

        private static readonly Dictionary<int, HostilityCacheEntry> hostilityCacheByFactionId = new Dictionary<int, HostilityCacheEntry>(64);
        private static readonly Dictionary<int, SmallPetCandidateCacheEntry> smallPetCandidateCacheByPawnId = new Dictionary<int, SmallPetCandidateCacheEntry>(256);

        private static Faction GetPlayerFactionCached()
        {
            int currentTick = Find.TickManager?.TicksGame ?? -1;
            Game currentGame = Current.Game;
            if (!ReferenceEquals(cachedGame, currentGame))
            {
                cachedGame = currentGame;
                cachedPlayerFaction = null;
                nextPlayerFactionRefreshTick = int.MinValue;
                hostilityCacheByFactionId.Clear();
                smallPetCandidateCacheByPawnId.Clear();
            }

            if (currentGame == null)
            {
                return null;
            }

            if (cachedPlayerFaction != null)
            {
                return cachedPlayerFaction;
            }

            if (currentTick < 0 || currentTick >= nextPlayerFactionRefreshTick)
            {
                cachedPlayerFaction = Faction.OfPlayerSilentFail;
                nextPlayerFactionRefreshTick = (currentTick < 0)
                    ? PlayerFactionRefreshIntervalTicks
                    : currentTick + PlayerFactionRefreshIntervalTicks;
            }

            return cachedPlayerFaction;
        }

        private static bool IsHostileToPlayerFactionSafe(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            if (currentTick >= 0)
            {
                if (lastHostilityCleanupTick == int.MinValue || currentTick - lastHostilityCleanupTick >= CacheCleanupIntervalTicks)
                {
                    CleanupExpiredHostilityCache(currentTick);
                    lastHostilityCleanupTick = currentTick;
                }

                int factionId = faction.loadID;
                if (factionId >= 0
                    && hostilityCacheByFactionId.TryGetValue(factionId, out HostilityCacheEntry cached)
                    && cached.ValidUntilTick >= currentTick)
                {
                    return cached.Hostile;
                }
            }

            Faction playerFaction = GetPlayerFactionCached();
            if (playerFaction == null || ReferenceEquals(faction, playerFaction))
            {
                return false;
            }

            FactionRelation relation = faction.RelationWith(playerFaction, allowNull: true);
            bool hostile = relation != null && relation.kind == FactionRelationKind.Hostile;
            if (currentTick >= 0)
            {
                int factionId = faction.loadID;
                if (factionId >= 0)
                {
                    if (hostilityCacheByFactionId.Count >= MaxHostilityCacheSize)
                    {
                        CleanupExpiredHostilityCache(currentTick);
                    }

                    hostilityCacheByFactionId[factionId] = new HostilityCacheEntry(
                        hostile,
                        currentTick + HostilityCacheDurationTicks);
                }
            }

            return hostile;
        }

        private static bool IsPlayerSmallPetCandidateCached(Pawn pawn, ZoologyModSettings settings)
        {
            if (pawn == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? -1;
            int pawnId = pawn.thingIDNumber;
            if (currentTick >= 0
                && smallPetCandidateCacheByPawnId.TryGetValue(pawnId, out SmallPetCandidateCacheEntry cached)
                && cached.ValidUntilTick >= currentTick)
            {
                return cached.IsCandidate;
            }

            bool candidate = false;
            RaceProperties raceProps = pawn.RaceProps;
            if (raceProps?.Animal == true && !pawn.Roamer)
            {
                float smallPetThreshold = settings?.SmallPetBodySizeThreshold ?? 0.35f;
                if (raceProps.baseBodySize < smallPetThreshold)
                {
                    Faction playerFaction = GetPlayerFactionCached();
                    if (playerFaction != null && ReferenceEquals(pawn.Faction, playerFaction))
                    {
                        candidate = true;
                    }
                }
            }

            if (currentTick >= 0)
            {
                if (lastCandidateCleanupTick == int.MinValue || currentTick - lastCandidateCleanupTick >= CacheCleanupIntervalTicks)
                {
                    CleanupExpiredSmallPetCandidateCache(currentTick);
                    lastCandidateCleanupTick = currentTick;
                }

                if (smallPetCandidateCacheByPawnId.Count >= MaxCandidateCacheSize)
                {
                    CleanupExpiredSmallPetCandidateCache(currentTick);
                }

                smallPetCandidateCacheByPawnId[pawnId] = new SmallPetCandidateCacheEntry(
                    candidate,
                    currentTick + SmallPetCandidateCacheDurationTicks);
            }

            return candidate;
        }

        private static void CleanupExpiredHostilityCache(int currentTick)
        {
            if (hostilityCacheByFactionId.Count == 0)
            {
                return;
            }

            List<int> keysToRemove = null;
            foreach (KeyValuePair<int, HostilityCacheEntry> entry in hostilityCacheByFactionId)
            {
                if (entry.Value.ValidUntilTick >= currentTick)
                {
                    continue;
                }

                keysToRemove ??= new List<int>(16);
                keysToRemove.Add(entry.Key);
            }

            if (keysToRemove == null)
            {
                return;
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                hostilityCacheByFactionId.Remove(keysToRemove[i]);
            }
        }

        private static void CleanupExpiredSmallPetCandidateCache(int currentTick)
        {
            if (smallPetCandidateCacheByPawnId.Count == 0)
            {
                return;
            }

            List<int> keysToRemove = null;
            foreach (KeyValuePair<int, SmallPetCandidateCacheEntry> entry in smallPetCandidateCacheByPawnId)
            {
                if (entry.Value.ValidUntilTick >= currentTick)
                {
                    continue;
                }

                keysToRemove ??= new List<int>(32);
                keysToRemove.Add(entry.Key);
            }

            if (keysToRemove == null)
            {
                return;
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                smallPetCandidateCacheByPawnId.Remove(keysToRemove[i]);
            }
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

            if (otherPawn == null)
            {
                return true;
            }

            if (!IsPlayerSmallPetCandidateCached(__instance, settings))
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
                // If this small pet is actively being hit in melee, do not suppress threat response.
                __result = false;
                return false;
            }

            __result = true;
            return false;
        }
    }
}
