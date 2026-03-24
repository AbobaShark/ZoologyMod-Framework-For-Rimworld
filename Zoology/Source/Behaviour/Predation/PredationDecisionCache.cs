using System.Collections.Generic;
using Verse;

namespace ZoologyMod
{
    internal static class PredationDecisionCache
    {
        private readonly struct AcceptablePreyCacheEntry
        {
            public AcceptablePreyCacheEntry(bool value, int tick)
            {
                Value = value;
                Tick = tick;
            }

            public bool Value { get; }

            public int Tick { get; }
        }

        private readonly struct PreyScoreCacheEntry
        {
            public PreyScoreCacheEntry(float value, int tick)
            {
                Value = value;
                Tick = tick;
            }

            public float Value { get; }

            public int Tick { get; }
        }

        private const int AcceptablePreyCacheDurationTicks = ZoologyTickLimiter.PredationDecision.AcceptablePreyCacheDurationTicks;
        private const int PreyScoreCacheDurationTicks = ZoologyTickLimiter.PredationDecision.PreyScoreCacheDurationTicks;
        private const int CacheCleanupIntervalTicks = ZoologyTickLimiter.PredationDecision.CacheCleanupIntervalTicks;

        private static readonly Dictionary<long, AcceptablePreyCacheEntry> acceptablePreyCacheByPairKey = new Dictionary<long, AcceptablePreyCacheEntry>(512);
        private static readonly Dictionary<long, PreyScoreCacheEntry> preyScoreCacheByPairKey = new Dictionary<long, PreyScoreCacheEntry>(256);
        private static int lastCleanupTick = -CacheCleanupIntervalTicks;
        private static Game runtimeCacheGame;
        private static int runtimeCacheLastTick = -1;

        public static void ClearAll()
        {
            acceptablePreyCacheByPairKey.Clear();
            preyScoreCacheByPairKey.Clear();
            lastCleanupTick = -CacheCleanupIntervalTicks;
            runtimeCacheGame = Current.Game;
            runtimeCacheLastTick = Find.TickManager?.TicksGame ?? 0;
        }

        public static bool TryGetAcceptablePrey(Pawn predator, Pawn prey, out bool value)
        {
            value = false;
            long pairKey = PairKey(predator, prey);
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            EnsureRuntimeState(currentTick);
            return pairKey != 0L
                && currentTick > 0
                && acceptablePreyCacheByPairKey.TryGetValue(pairKey, out AcceptablePreyCacheEntry cached)
                && currentTick - cached.Tick <= AcceptablePreyCacheDurationTicks
                && (value = cached.Value) == cached.Value;
        }

        public static void StoreAcceptablePrey(Pawn predator, Pawn prey, bool value)
        {
            long pairKey = PairKey(predator, prey);
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            EnsureRuntimeState(currentTick);
            if (pairKey == 0L || currentTick <= 0)
            {
                return;
            }

            acceptablePreyCacheByPairKey[pairKey] = new AcceptablePreyCacheEntry(value, currentTick);
            CleanupIfNeeded(currentTick);
        }

        public static bool TryGetPreyScore(Pawn predator, Pawn prey, out float value)
        {
            value = 0f;
            long pairKey = PairKey(predator, prey);
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            EnsureRuntimeState(currentTick);
            return pairKey != 0L
                && currentTick > 0
                && preyScoreCacheByPairKey.TryGetValue(pairKey, out PreyScoreCacheEntry cached)
                && currentTick - cached.Tick <= PreyScoreCacheDurationTicks
                && (value = cached.Value) == cached.Value;
        }

        public static void StorePreyScore(Pawn predator, Pawn prey, float value)
        {
            long pairKey = PairKey(predator, prey);
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            EnsureRuntimeState(currentTick);
            if (pairKey == 0L || currentTick <= 0)
            {
                return;
            }

            preyScoreCacheByPairKey[pairKey] = new PreyScoreCacheEntry(value, currentTick);
            CleanupIfNeeded(currentTick);
        }

        private static void EnsureRuntimeState(int currentTick)
        {
            Game currentGame = Current.Game;
            bool gameChanged = !ReferenceEquals(runtimeCacheGame, currentGame);
            bool tickRewound = currentTick > 0 && runtimeCacheLastTick > 0 && currentTick < runtimeCacheLastTick;
            if (gameChanged || tickRewound)
            {
                acceptablePreyCacheByPairKey.Clear();
                preyScoreCacheByPairKey.Clear();
                lastCleanupTick = -CacheCleanupIntervalTicks;
                runtimeCacheGame = currentGame;
            }

            if (currentTick > 0)
            {
                runtimeCacheLastTick = currentTick;
            }
        }

        private static void CleanupIfNeeded(int currentTick)
        {
            if (currentTick - lastCleanupTick < CacheCleanupIntervalTicks)
            {
                return;
            }

            lastCleanupTick = currentTick;
            CleanupAcceptablePreyCache(currentTick);
            CleanupPreyScoreCache(currentTick);
        }

        private static void CleanupAcceptablePreyCache(int currentTick)
        {
            List<long> staleKeys = null;
            foreach (KeyValuePair<long, AcceptablePreyCacheEntry> entry in acceptablePreyCacheByPairKey)
            {
                if (currentTick - entry.Value.Tick <= AcceptablePreyCacheDurationTicks)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<long>(64);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                acceptablePreyCacheByPairKey.Remove(staleKeys[i]);
            }
        }

        private static void CleanupPreyScoreCache(int currentTick)
        {
            List<long> staleKeys = null;
            foreach (KeyValuePair<long, PreyScoreCacheEntry> entry in preyScoreCacheByPairKey)
            {
                if (currentTick - entry.Value.Tick <= PreyScoreCacheDurationTicks)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<long>(64);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                preyScoreCacheByPairKey.Remove(staleKeys[i]);
            }
        }

        private static long PairKey(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return 0L;
            }

            uint predatorId = (uint)predator.thingIDNumber;
            uint preyId = (uint)prey.thingIDNumber;
            return ((long)predatorId << 32) | preyId;
        }
    }
}
