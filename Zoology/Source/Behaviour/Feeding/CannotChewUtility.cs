using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class CannotChewUtility
    {
        private static readonly Dictionary<int, bool> hasCannotChewByPawnId = new Dictionary<int, bool>(128);
        private static readonly Dictionary<int, float> predationBodySizeLimitByPawnId = new Dictionary<int, float>(128);
        private static Game runtimeCacheGame;
        private static int runtimeCacheTick = int.MinValue;

        public static bool HasCannotChew(Pawn pawn)
        {
            if (pawn == null || !CannotChewSettingsGate.Enabled())
            {
                return false;
            }

            EnsureRuntimeCaches();

            int pawnId = pawn.thingIDNumber;
            if (hasCannotChewByPawnId.TryGetValue(pawnId, out bool cached))
            {
                return cached;
            }

            bool hasCannotChew = DefModExtensionCache<ModExtension_CannotChew>.Has(pawn);
            hasCannotChewByPawnId[pawnId] = hasCannotChew;
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

            int predatorId = predator.thingIDNumber;
            if (predationBodySizeLimitByPawnId.TryGetValue(predatorId, out float cachedLimit))
            {
                return cachedLimit;
            }

            float maxPreyBodySize = predator.RaceProps?.maxPreyBodySize ?? float.MaxValue;
            float result = maxPreyBodySize;
            if (!HasCannotChew(predator))
            {
                predationBodySizeLimitByPawnId[predatorId] = result;
                return result;
            }

            if (IsNonAdultGrowthStage(predator))
            {
                float currentBodySize = predator.BodySize;
                result = currentBodySize < maxPreyBodySize ? currentBodySize : maxPreyBodySize;
            }

            predationBodySizeLimitByPawnId[predatorId] = result;
            return result;
        }

        private static void EnsureRuntimeCaches()
        {
            Game currentGame = Current.Game;
            int currentTick = Find.TickManager?.TicksGame ?? int.MinValue;
            if (ReferenceEquals(runtimeCacheGame, currentGame) && runtimeCacheTick == currentTick)
            {
                return;
            }

            runtimeCacheGame = currentGame;
            runtimeCacheTick = currentTick;
            hasCannotChewByPawnId.Clear();
            predationBodySizeLimitByPawnId.Clear();
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
