using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class CannotChewUtility
    {
        private readonly struct CannotChewCacheEntry
        {
            public CannotChewCacheEntry(Pawn pawn, ThingDef def, PawnKindDef kindDef, bool hasCannotChew)
            {
                Pawn = pawn;
                Def = def;
                KindDef = kindDef;
                HasCannotChew = hasCannotChew;
            }

            public Pawn Pawn { get; }
            public ThingDef Def { get; }
            public PawnKindDef KindDef { get; }
            public bool HasCannotChew { get; }
        }

        private readonly struct BodySizeLimitCacheEntry
        {
            public BodySizeLimitCacheEntry(Pawn pawn, ThingDef def, PawnKindDef kindDef, int lifeStageIndex, float bodySize, float limit)
            {
                Pawn = pawn;
                Def = def;
                KindDef = kindDef;
                LifeStageIndex = lifeStageIndex;
                BodySize = bodySize;
                Limit = limit;
            }

            public Pawn Pawn { get; }
            public ThingDef Def { get; }
            public PawnKindDef KindDef { get; }
            public int LifeStageIndex { get; }
            public float BodySize { get; }
            public float Limit { get; }
        }

        private static Pawn lastCannotChewPawn;
        private static ThingDef lastCannotChewDef;
        private static PawnKindDef lastCannotChewKindDef;
        private static bool lastCannotChew;
        private static Pawn lastBodySizeLimitPawn;
        private static ThingDef lastBodySizeLimitDef;
        private static PawnKindDef lastBodySizeLimitKindDef;
        private static int lastBodySizeLimitLifeStageIndex = -1;
        private static float lastBodySizeLimitBodySize = -1f;
        private static float lastBodySizeLimit;
        private static readonly Dictionary<int, CannotChewCacheEntry> hasCannotChewByPawnId = new Dictionary<int, CannotChewCacheEntry>(128);
        private static readonly Dictionary<int, BodySizeLimitCacheEntry> predationBodySizeLimitByPawnId = new Dictionary<int, BodySizeLimitCacheEntry>(128);
        private static Game runtimeCacheGame;

        public static bool HasCannotChew(Pawn pawn)
        {
            if (pawn == null || !CannotChewSettingsGate.Enabled())
            {
                return false;
            }

            EnsureRuntimeCaches();

            ThingDef def = pawn.def;
            PawnKindDef kindDef = pawn.kindDef;
            if (ReferenceEquals(lastCannotChewPawn, pawn)
                && ReferenceEquals(lastCannotChewDef, def)
                && ReferenceEquals(lastCannotChewKindDef, kindDef))
            {
                return lastCannotChew;
            }

            int pawnId = pawn.thingIDNumber;
            if (pawnId > 0
                && hasCannotChewByPawnId.TryGetValue(pawnId, out CannotChewCacheEntry cached)
                && ReferenceEquals(cached.Pawn, pawn)
                && ReferenceEquals(cached.Def, def)
                && ReferenceEquals(cached.KindDef, kindDef))
            {
                RememberLastCannotChew(pawn, cached.HasCannotChew);
                return cached.HasCannotChew;
            }

            bool hasCannotChew = DefModExtensionCache<ModExtension_CannotChew>.Has(pawn);
            if (pawnId > 0)
            {
                hasCannotChewByPawnId[pawnId] = new CannotChewCacheEntry(pawn, def, kindDef, hasCannotChew);
            }
            RememberLastCannotChew(pawn, hasCannotChew);
            return hasCannotChew;
        }

        public static bool IsCorpseTooLarge(Pawn eater, Corpse corpse)
        {
            if (eater == null || corpse?.InnerPawn == null)
            {
                return false;
            }

            return corpse.InnerPawn.BodySize > GetPredationBodySizeLimit(eater);
        }

        public static bool IsPreyTooLargeForPredator(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            return prey.BodySize > GetPredationBodySizeLimit(predator);
        }

        public static float GetPredationBodySizeLimit(Pawn predator)
        {
            if (predator == null)
            {
                return 0f;
            }

            EnsureRuntimeCaches();

            ThingDef def = predator.def;
            PawnKindDef kindDef = predator.kindDef;
            int lifeStageIndex = GetLifeStageIndex(predator);
            float bodySize = predator.BodySize;
            if (ReferenceEquals(lastBodySizeLimitPawn, predator)
                && ReferenceEquals(lastBodySizeLimitDef, def)
                && ReferenceEquals(lastBodySizeLimitKindDef, kindDef)
                && lastBodySizeLimitLifeStageIndex == lifeStageIndex
                && lastBodySizeLimitBodySize == bodySize)
            {
                return lastBodySizeLimit;
            }

            int predatorId = predator.thingIDNumber;
            if (predatorId > 0
                && predationBodySizeLimitByPawnId.TryGetValue(predatorId, out BodySizeLimitCacheEntry cachedLimit)
                && ReferenceEquals(cachedLimit.Pawn, predator)
                && ReferenceEquals(cachedLimit.Def, def)
                && ReferenceEquals(cachedLimit.KindDef, kindDef)
                && cachedLimit.LifeStageIndex == lifeStageIndex
                && cachedLimit.BodySize == bodySize)
            {
                RememberLastBodySizeLimit(predator, lifeStageIndex, bodySize, cachedLimit.Limit);
                return cachedLimit.Limit;
            }

            float maxPreyBodySize = predator.RaceProps?.maxPreyBodySize ?? float.MaxValue;
            float result = maxPreyBodySize;
            if (!HasCannotChew(predator))
            {
                StoreBodySizeLimit(predatorId, predator, lifeStageIndex, bodySize, result);
                return result;
            }

            if (IsNonAdultGrowthStage(predator))
            {
                float currentBodySize = bodySize;
                result = currentBodySize < maxPreyBodySize ? currentBodySize : maxPreyBodySize;
            }

            StoreBodySizeLimit(predatorId, predator, lifeStageIndex, bodySize, result);
            return result;
        }

        public static void ClearRuntimeCaches()
        {
            runtimeCacheGame = Current.Game;
            lastCannotChewPawn = null;
            lastCannotChewDef = null;
            lastCannotChewKindDef = null;
            lastCannotChew = false;
            lastBodySizeLimitPawn = null;
            lastBodySizeLimitDef = null;
            lastBodySizeLimitKindDef = null;
            lastBodySizeLimitLifeStageIndex = -1;
            lastBodySizeLimitBodySize = -1f;
            lastBodySizeLimit = 0f;
            hasCannotChewByPawnId.Clear();
            predationBodySizeLimitByPawnId.Clear();
        }

        private static void EnsureRuntimeCaches()
        {
            Game currentGame = Current.Game;
            if (ReferenceEquals(runtimeCacheGame, currentGame))
            {
                return;
            }

            ClearRuntimeCaches();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetLifeStageIndex(Pawn pawn)
        {
            try
            {
                return pawn?.ageTracker?.CurLifeStageIndex ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RememberLastCannotChew(Pawn pawn, bool hasCannotChew)
        {
            lastCannotChewPawn = pawn;
            lastCannotChewDef = pawn?.def;
            lastCannotChewKindDef = pawn?.kindDef;
            lastCannotChew = hasCannotChew;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RememberLastBodySizeLimit(Pawn pawn, int lifeStageIndex, float bodySize, float limit)
        {
            lastBodySizeLimitPawn = pawn;
            lastBodySizeLimitDef = pawn?.def;
            lastBodySizeLimitKindDef = pawn?.kindDef;
            lastBodySizeLimitLifeStageIndex = lifeStageIndex;
            lastBodySizeLimitBodySize = bodySize;
            lastBodySizeLimit = limit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreBodySizeLimit(int pawnId, Pawn pawn, int lifeStageIndex, float bodySize, float limit)
        {
            if (pawnId > 0)
            {
                predationBodySizeLimitByPawnId[pawnId] = new BodySizeLimitCacheEntry(pawn, pawn?.def, pawn?.kindDef, lifeStageIndex, bodySize, limit);
            }

            RememberLastBodySizeLimit(pawn, lifeStageIndex, bodySize, limit);
        }

        private static bool IsNonAdultGrowthStage(Pawn pawn)
        {
            if (pawn?.ageTracker == null)
            {
                return false;
            }

            var lifeStageAges = pawn.RaceProps?.lifeStageAges;
            if (lifeStageAges != null && lifeStageAges.Count > 0)
            {
                int curIndex = pawn.ageTracker.CurLifeStageIndex;
                if (curIndex >= 0 && curIndex < lifeStageAges.Count - 1)
                {
                    return true;
                }
            }

            return AnimalLifeStageUtility.IsAnimalChildLifeStage(pawn.ageTracker.CurLifeStage);
        }

        public static float GetRemainingCorpseNutrition(Corpse corpse, Pawn ingester)
        {
            if (corpse == null)
            {
                return 0f;
            }

            Pawn inner = corpse.InnerPawn;
            if (inner?.health?.hediffSet == null)
            {
                return 0f;
            }

            ScavengerEatingContext.SetEating(ingester, corpse);
            try
            {
                float total = 0f;
                var parts = inner.health.hediffSet.GetNotMissingParts();
                foreach (var part in parts)
                {
                    total += FoodUtility.GetBodyPartNutrition(corpse, part);
                }
                return total;
            }
            finally
            {
                ScavengerEatingContext.Clear(ingester);
            }
        }
    }
}
