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

            return corpse.InnerPawn.BodySize > eater.BodySize;
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
