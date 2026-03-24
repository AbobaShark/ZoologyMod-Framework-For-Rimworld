using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Caravan_NeedsTracker), "TrySatisfyFoodNeed")]
    internal static class Patch_Caravan_NeedsTracker_TrySatisfyFoodNeed_MammalBabies
    {
        private static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        private static void Prefix(Caravan_NeedsTracker __instance, Pawn pawn, Need_Food food, int delta)
        {
            try
            {
                if (!ZoologyModSettings.EnableMammalLactation)
                {
                    return;
                }

                Caravan caravan = __instance?.caravan;
                if (caravan == null || pawn == null || food == null)
                {
                    return;
                }

                if (!pawn.IsMammal() || !MammalBabyCache.IsMammalBaby(pawn))
                {
                    return;
                }

                if (!AnimalLactationUtility.ChildWantsSuckle(pawn))
                {
                    return;
                }

                Pawn feeder = FindBestFeederInCaravan(caravan, pawn);
                if (feeder == null)
                {
                    return;
                }

                SuckleInCaravan(pawn, feeder, delta);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Caravan mammal baby feeding prefix failed: {ex}");
            }
        }

        private static Pawn FindBestFeederInCaravan(Caravan caravan, Pawn baby)
        {
            Pawn best = null;
            float bestFood = -1f;
            var pawns = caravan.PawnsListForReading;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn candidate = pawns[i];
                if (candidate == null || candidate == baby)
                {
                    continue;
                }

                if (!AnimalLactationUtility.CanMotherFeed(candidate))
                {
                    continue;
                }

                if (!AnimalLactationUtility.IsCrossBreedCompatible(candidate, baby))
                {
                    continue;
                }

                if (!IsPupFactionCompatible(candidate, baby))
                {
                    continue;
                }

                float candidateFood = candidate.needs?.food?.CurLevelPercentage ?? -1f;
                if (candidateFood > bestFood)
                {
                    best = candidate;
                    bestFood = candidateFood;
                }
            }

            return best;
        }

        private static bool IsPupFactionCompatible(Pawn mom, Pawn pup)
        {
            if (mom == null || pup == null)
            {
                return false;
            }

            Faction pupFaction = pup.Faction;
            Faction pupHost = pup.HostFaction;

            if (pupFaction == null && pupHost == null)
            {
                return mom.Faction == null && mom.HostFaction == null;
            }

            return mom.Faction == pupFaction
                || mom.Faction == pupHost
                || mom.HostFaction == pupFaction
                || mom.HostFaction == pupHost;
        }

        private static bool SuckleInCaravan(Pawn pup, Pawn mom, int deltaTicks)
        {
            if (pup == null || mom == null || pup.Dead || mom.Dead || mom.Downed)
            {
                return false;
            }

            if (!AnimalLactationUtility.IsCrossBreedCompatible(mom, pup))
            {
                return false;
            }

            if (!AnimalLactationUtility.MotherHasSufficientNutrition(mom))
            {
                return false;
            }

            HediffDef lactDef = AnimalLactationUtility.LactatingHediffDef;
            if (lactDef == null)
            {
                return false;
            }

            Hediff lact = mom.health?.hediffSet?.GetFirstHediffOfDef(lactDef);
            if (lact == null)
            {
                return false;
            }

            Need_Food momFood = mom.needs?.food;
            Need_Food pupFood = pup.needs?.food;
            if (momFood == null || pupFood == null)
            {
                return false;
            }

            float nutritionWanted = pupFood.NutritionWanted;
            if (nutritionWanted <= 0f)
            {
                return true;
            }

            int safeDelta = Math.Max(1, deltaTicks);
            float perTick = pupFood.MaxLevel / AnimalLactationUtility.FullFeedSessionTicks;
            float providedRequested = perTick * safeDelta;
            float provided = Math.Min(nutritionWanted, providedRequested);
            float momDeduct = Math.Min(momFood.CurLevel, provided);
            if (momDeduct <= 0f)
            {
                return false;
            }

            momFood.CurLevel = Math.Max(0f, momFood.CurLevel - momDeduct);
            pupFood.CurLevel = Math.Min(pupFood.MaxLevel, pupFood.CurLevel + momDeduct);

            try
            {
                lact.Severity = 1f;
            }
            catch
            {
            }

            return pupFood.CurLevel >= pupFood.MaxLevel * 0.99f;
        }
    }

    [HarmonyPatch(typeof(FoodUtility), "FoodIsSuitable", new Type[] { typeof(Pawn), typeof(ThingDef) })]
    internal static class Patch_FoodUtility_FoodIsSuitable_MammalBabyCaravanCompatibility
    {
        private static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn p, ThingDef food, ref bool __result)
        {
            try
            {
                if (!ZoologyModSettings.EnableMammalLactation)
                {
                    return true;
                }

                if (p == null || food == null || !p.IsMammal() || !MammalBabyCache.IsMammalBaby(p))
                {
                    return true;
                }

                Need_Food needFood = p.needs?.food;
                if (needFood == null)
                {
                    __result = false;
                    return false;
                }

                IngestibleProperties ingestible = food.ingestible;
                if (ingestible == null)
                {
                    __result = false;
                    return false;
                }

                bool isNutritionFood = food.IsNutritionGivingIngestible;
                bool isDrug = food.IsDrug || ingestible.drugCategory != DrugCategory.None;
                bool isCorpse = typeof(Corpse).IsAssignableFrom(food.thingClass);

                __result = ingestible.babiesCanIngest && isNutritionFood && !isDrug && !isCorpse;
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Mammal baby FoodIsSuitable override failed: {ex}");
                return true;
            }
        }
    }
}
