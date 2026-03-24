using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class CannotChewUtility
    {
        public static bool HasCannotChew(Pawn pawn)
        {
            if (pawn == null || !CannotChewSettingsGate.Enabled())
            {
                return false;
            }

            return DefModExtensionCache<ModExtension_CannotChew>.Has(pawn);
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

            float maxPreyBodySize = predator.RaceProps?.maxPreyBodySize ?? float.MaxValue;
            if (!HasCannotChew(predator))
            {
                return maxPreyBodySize;
            }

            if (IsNonAdultGrowthStage(predator))
            {
                float currentBodySize = predator.BodySize;
                return currentBodySize < maxPreyBodySize ? currentBodySize : maxPreyBodySize;
            }

            return maxPreyBodySize;
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
