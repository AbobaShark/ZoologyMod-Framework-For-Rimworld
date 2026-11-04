using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Job evaluation can happen repeatedly while the child is idle. Reuse a
    // short-lived reachability result instead of running a region search for
    // every evaluation (and every rare sleeping-child check).
    internal static class ChildcareMotherReachability
    {
        private const int RecheckIntervalTicks = 300;
        private const int CleanupIntervalTicks = 6000;

        private readonly struct CacheEntry
        {
            public CacheEntry(int mapId, int motherId, int tick, bool reachable)
            {
                MapId = mapId;
                MotherId = motherId;
                Tick = tick;
                Reachable = reachable;
            }

            public int MapId { get; }
            public int MotherId { get; }
            public int Tick { get; }
            public bool Reachable { get; }
        }

        private static readonly Dictionary<int, CacheEntry> resultsByChildId =
            new Dictionary<int, CacheEntry>(128);
        private static readonly List<int> expiredChildIds = new List<int>(32);
        private static Game cachedGame;
        private static int lastCleanupTick;

        public static bool CanFollow(Pawn child, Pawn mother)
        {
            if (child == null || mother == null || !child.Spawned || !mother.Spawned
                || child.Map == null || child.Map != mother.Map)
            {
                return false;
            }

            // Area restrictions may exclude a mother even when the map regions
            // are connected. Avoid using her location as a wander root in that case.
            Area allowedArea = child.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap;
            if (allowedArea != null && !allowedArea[mother.Position])
            {
                return false;
            }

            Game game = Current.Game;
            if (!object.ReferenceEquals(game, cachedGame))
            {
                cachedGame = game;
                resultsByChildId.Clear();
                expiredChildIds.Clear();
                lastCleanupTick = 0;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int childId = child.thingIDNumber;
            int motherId = mother.thingIDNumber;
            int mapId = child.Map.uniqueID;

            if (tick > 0 && childId > 0
                && resultsByChildId.TryGetValue(childId, out CacheEntry cached)
                && cached.MapId == mapId && cached.MotherId == motherId
                && tick >= cached.Tick && tick - cached.Tick < RecheckIntervalTicks)
            {
                return cached.Reachable;
            }

            // ByPawn uses the child's actual traversal rules: a pen roamer
            // cannot open a colonist-only door simply because its mother can.
            bool reachable = child.CanReach(mother.Position, PathEndMode.OnCell,
                Danger.Deadly, false, false, TraverseMode.ByPawn);

            if (tick > 0 && childId > 0)
            {
                resultsByChildId[childId] = new CacheEntry(mapId, motherId, tick, reachable);
                if (tick - lastCleanupTick >= CleanupIntervalTicks)
                {
                    RemoveExpiredEntries(tick);
                }
            }

            return reachable;
        }

        private static void RemoveExpiredEntries(int tick)
        {
            lastCleanupTick = tick;
            expiredChildIds.Clear();
            foreach (KeyValuePair<int, CacheEntry> pair in resultsByChildId)
            {
                if (pair.Value.Tick > tick
                    || tick - pair.Value.Tick >= CleanupIntervalTicks)
                {
                    expiredChildIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < expiredChildIds.Count; i++)
            {
                resultsByChildId.Remove(expiredChildIds[i]);
            }
            expiredChildIds.Clear();
        }
    }
}
