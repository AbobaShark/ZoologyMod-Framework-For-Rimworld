using Verse;

namespace ZoologyMod
{
    internal static class ZoologyTickLimiter
    {
        // Per-tick call budgets (global hard caps per tick).
        public const int FoodOptimalityBudgetPerTick = 8000;
        public const int FoodIsSuitableBudgetPerTick = 6000;
        public const int WillEatBudgetPerTick = 6000;
        public const int GetPreyBudgetPerTick = 4000;
        public const int HasPredatorAttackedBudgetPerTick = 4000;

        // Predation food selection cache timing.
        public static class PredationFood
        {
            public const int CorpseFoodStateCacheDurationTicks = 300;
            public const int CorpseFoodStateCacheCleanupIntervalTicks = 1200;
            public const int CorpseFoodStateBudgetPerTick = 24;
            public const int LivePreyAcceptableBudgetPerTick = 24;
            public const int FoodOptimalityDeltaCacheDurationTicks = 10;
            public const int FoodOptimalityDeltaCacheCleanupIntervalTicks = 600;
        }

        // Predation decision cache timing.
        public static class PredationDecision
        {
            public const int AcceptablePreyCacheDurationTicks = 90;
            public const int PreyScoreCacheDurationTicks = 90;
            public const int CacheCleanupIntervalTicks = 600;
        }

        // Prey protection / defend-corpse timing.
        public static class PreyProtection
        {
            public const int DefaultPairTicks = 60 * 60 * 2;
            public const int TickCheckInterval = 250;
            public const int NotificationSuppressionTicks = 600;
            public const int TriggerCooldownTicks = 250;
            public const long InaccessibleRemoveTicks = 3600;
            public const long UnsuitableRemoveTicks = 1200;
            public const int PresenceCheckInterval = 250;
            public const int ProtectedPawnCacheCleanupIntervalTicks = 600;
            public const int ProtectedPawnCacheDurationTicks = 600;
            public const int ProtectedPawnCacheRefreshMarginTicks = 120;
            public const int ProtectPreyMapRefreshIntervalTicks = 60;
            public const int MinimumProtectDurationTicks = 360;
            public const int WanderNearPreyMinTicks = 125;
            public const int WanderNearPreyMaxTicks = 200;
            public const int PresenceLogCooldownTicks = 2000;
        }

        // Lactation-related timing.
        public static class Lactation
        {
            public const int FullFeedSessionTicks = 2000;
            public const int FullLactatingSeverityTicks = 10000;
            public const int FeedAttemptCooldownTicks = 100;
            public const int YoungSuckleDurationTicks = 2000;
            public const int AutoSlaughterCacheIntervalTicks = 120;
            public const int BackfillTickInterval = 600;
            public const int BreastfeedFallbackWaitTicks = 2000;
            public const int SuckleRequestDurationTicks = 600;
            public const int SuckleRequestCooldownTicks = 60;
            public const int SuckleRequestCacheDurationTicks = 30;
        }

        // Childcare-related timing.
        public static class Childcare
        {
            public const int MotherCacheDurationTicks = 120;
            public const int WanderNearMotherMinTicks = 125;
            public const int WanderNearMotherMaxTicks = 200;
        }

        // Scavenging AI timing.
        public static class Scavenging
        {
            public const int CorpseCleanupIntervalTicks = 60;
            public const int SearchCooldownTicks = 120;
            public const int SearchCooldownFailTicks = 240;
            public const int CachedResultTicks = 90;
            public const int MaxScansPerTickPerMap = 2;
        }

        // Small pets threat cache timing.
        public static class SmallPetThreat
        {
            public const int SettingsSnapshotRefreshIntervalTicks = 60;
            public const int StateCacheDurationTicks = 60;
            public const int StateCacheCleanupIntervalTicks = 600;
            public const int BudgetPerTick = 64;
        }

        // Animal flee threat scanning timing.
        public static class FleeThreat
        {
            public const int NoThreatScanCooldownTicks = 60;
            public const int ThreatMapRefreshIntervalTicks = 90;
            public const int ThreatVisibilityCacheDurationTicks = 60;
            public const int NearbyThreatBucketsCacheDurationTicks = 120;
            public const int NearestPredatorThreatCacheDurationTicks = 15;
            public const int ThreatCacheCleanupIntervalTicks = 600;
            public const int MinThreatScanIntervalTicks = 2;
            public const int ThreatScanBudgetCooldownTicks = 8;
            public const int FallbackThreatScanBudgetPerTick = 4;
        }

        // Pursuit block logic timing.
        public static class Pursuit
        {
            public const int ChaseTimeoutTicks = 2500;
            public const int ScanIntervalTicks = 120;
            public const int CleanIntervalTicks = 10000;
            public const int TicksPerLongTick = ChaseTimeoutTicks;
            public const int StopPredatorWaitTicks = 250;
        }

        private static bool TryConsume(ref int tickField, ref int remainingField, int perTick)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick <= 0)
            {
                return false;
            }

            if (tickField != tick)
            {
                tickField = tick;
                remainingField = perTick;
            }

            if (remainingField <= 0)
            {
                return false;
            }

            remainingField--;
            return true;
        }

        private static int foodOptimalityTick = -1;
        private static int foodOptimalityRemaining;
        private static int foodIsSuitableTick = -1;
        private static int foodIsSuitableRemaining;
        private static int willEatTick = -1;
        private static int willEatRemaining;
        private static int getPreyTick = -1;
        private static int getPreyRemaining;
        private static int hasPredatorAttackedTick = -1;
        private static int hasPredatorAttackedRemaining;

        public static bool TryConsumeFoodOptimality(int perTick) =>
            TryConsume(ref foodOptimalityTick, ref foodOptimalityRemaining, perTick);

        public static bool TryConsumeFoodIsSuitable(int perTick) =>
            TryConsume(ref foodIsSuitableTick, ref foodIsSuitableRemaining, perTick);

        public static bool TryConsumeWillEat(int perTick) =>
            TryConsume(ref willEatTick, ref willEatRemaining, perTick);

        public static bool TryConsumeGetPreyOfFaction(int perTick) =>
            TryConsume(ref getPreyTick, ref getPreyRemaining, perTick);

        public static bool TryConsumeHasPredatorAttackedAnyone(int perTick) =>
            TryConsume(ref hasPredatorAttackedTick, ref hasPredatorAttackedRemaining, perTick);
    }
}
