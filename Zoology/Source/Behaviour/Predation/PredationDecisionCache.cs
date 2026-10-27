using System.Collections.Generic;
using Verse;

namespace ZoologyMod
{
    internal static class PredationDecisionCache
    {
        private readonly struct AcceptablePreyCacheEntry
        {
            public AcceptablePreyCacheEntry(long pairKey, bool value, int tick)
            {
                PairKey = pairKey;
                Value = value;
                Tick = tick;
            }

            public long PairKey { get; }

            public bool Value { get; }

            public int Tick { get; }
        }

        private readonly struct PreyScoreCacheEntry
        {
            public PreyScoreCacheEntry(long pairKey, float value, int tick)
            {
                PairKey = pairKey;
                Value = value;
                Tick = tick;
            }

            public long PairKey { get; }

            public float Value { get; }

            public int Tick { get; }
        }

        private const int AcceptablePreyCacheDurationTicks = ZoologyTickLimiter.PredationDecision.AcceptablePreyCacheDurationTicks;
        private const int PreyScoreCacheDurationTicks = ZoologyTickLimiter.PredationDecision.PreyScoreCacheDurationTicks;
        private const int AcceptablePreyHotCacheSize = 32768;
        private const int AcceptablePreyHotCacheMask = AcceptablePreyHotCacheSize - 1;
        private const int PreyScoreHotCacheSize = 16384;
        private const int PreyScoreHotCacheMask = PreyScoreHotCacheSize - 1;

        private static readonly AcceptablePreyCacheEntry[] acceptablePreyHotCacheSlots = new AcceptablePreyCacheEntry[AcceptablePreyHotCacheSize];
        private static readonly PreyScoreCacheEntry[] preyScoreHotCacheSlots = new PreyScoreCacheEntry[PreyScoreHotCacheSize];
        private static Game runtimeCacheGame;
        private static int runtimeCacheLastTick = -1;

        public static void ClearAll()
        {
            System.Array.Clear(acceptablePreyHotCacheSlots, 0, acceptablePreyHotCacheSlots.Length);
            System.Array.Clear(preyScoreHotCacheSlots, 0, preyScoreHotCacheSlots.Length);
            runtimeCacheGame = Current.Game;
            runtimeCacheLastTick = Find.TickManager?.TicksGame ?? 0;
        }

        public static void InvalidatePrey(Pawn prey)
        {
            if (prey == null) return;
            uint id = (uint)prey.thingIDNumber;
            for (int i = 0; i < acceptablePreyHotCacheSlots.Length; i++)
                if ((uint)acceptablePreyHotCacheSlots[i].PairKey == id) acceptablePreyHotCacheSlots[i] = default;
            for (int i = 0; i < preyScoreHotCacheSlots.Length; i++)
                if ((uint)preyScoreHotCacheSlots[i].PairKey == id) preyScoreHotCacheSlots[i] = default;
        }

        // PairKey's low half contains only prey ID. Masking it directly made every predator
        // evaluating that prey overwrite the same slot, defeating the existing pair cache.
        private static int Slot(long key, int mask) => (int)(unchecked((uint)key * 2654435761u
            ^ (uint)((ulong)key >> 32) * 2246822519u) & (uint)mask);

        public static bool TryGetAcceptablePrey(Pawn predator, Pawn prey, out bool value)
        {
            value = false;
            long pairKey = PairKey(predator, prey);
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            EnsureRuntimeState(currentTick);
            if (pairKey == 0L || currentTick <= 0)
            {
                return false;
            }

            int slotIndex = Slot(pairKey, AcceptablePreyHotCacheMask);
            AcceptablePreyCacheEntry cached = acceptablePreyHotCacheSlots[slotIndex];
            if (cached.PairKey != pairKey
                || currentTick - cached.Tick > AcceptablePreyCacheDurationTicks)
            {
                return false;
            }

            value = cached.Value;
            return true;
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

            int slotIndex = Slot(pairKey, AcceptablePreyHotCacheMask);
            acceptablePreyHotCacheSlots[slotIndex] = new AcceptablePreyCacheEntry(pairKey, value, currentTick);
        }

        public static bool TryGetPreyScore(Pawn predator, Pawn prey, out float value)
        {
            value = 0f;
            long pairKey = PairKey(predator, prey);
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            EnsureRuntimeState(currentTick);
            if (pairKey == 0L || currentTick <= 0)
            {
                return false;
            }

            int slotIndex = Slot(pairKey, PreyScoreHotCacheMask);
            PreyScoreCacheEntry cached = preyScoreHotCacheSlots[slotIndex];
            if (cached.PairKey != pairKey
                || currentTick - cached.Tick > PreyScoreCacheDurationTicks)
            {
                return false;
            }

            value = cached.Value;
            return true;
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

            int slotIndex = Slot(pairKey, PreyScoreHotCacheMask);
            preyScoreHotCacheSlots[slotIndex] = new PreyScoreCacheEntry(pairKey, value, currentTick);
        }

        private static void EnsureRuntimeState(int currentTick)
        {
            Game currentGame = Current.Game;
            bool gameChanged = !ReferenceEquals(runtimeCacheGame, currentGame);
            bool tickRewound = currentTick > 0 && runtimeCacheLastTick > 0 && currentTick < runtimeCacheLastTick;
            if (gameChanged || tickRewound)
            {
                System.Array.Clear(acceptablePreyHotCacheSlots, 0, acceptablePreyHotCacheSlots.Length);
                System.Array.Clear(preyScoreHotCacheSlots, 0, preyScoreHotCacheSlots.Length);
                runtimeCacheGame = currentGame;
            }

            if (currentTick > 0)
            {
                runtimeCacheLastTick = currentTick;
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
