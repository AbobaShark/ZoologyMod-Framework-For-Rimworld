#if RIMWORLD17
using System;
using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public sealed partial class Building_AnimalBarn
    {
        private int foodPrioritySampleTick = -1;
        private float cachedFoodRestockPriority;
        private int edibleFoodCacheTick = -1;
        private readonly Dictionary<Thing, bool> edibleFoodCache =
            new Dictionary<Thing, bool>();

        // Recompute no more than once per 150 ticks, unless the barn's food
        // inventory or animal population changed. This is only queried while
        // a hauler searches for work, never by DrawAt or every animal tick.
        internal float FoodRestockPriority
        {
            get
            {
                if (!NeedsFeed) return -1f;

                int now = GenTicks.TicksGame;
                if (foodPrioritySampleTick >= 0 && now >= foodPrioritySampleTick
                    && now - foodPrioritySampleTick < 150)
                    return cachedFoodRestockPriority;

                cachedFoodRestockPriority = CalculateFoodRestockPriority();
                foodPrioritySampleTick = now;
                return cachedFoodRestockPriority;
            }
        }

        private void InvalidateFoodRestockPriority()
        {
            foodPrioritySampleTick = -1;
            edibleFoodCacheTick = -1;
            edibleFoodCache.Clear();
        }

        // Use the same actual edible-food tests as TickFeeding; a stock of
        // food that none of the residents can consume must not count as
        // nutrition protecting them against starvation.
        private static float NutritionForBarnResident(Pawn animal, Thing item)
        {
            if (animal == null || animal.Dead || animal.Destroyed
                || item == null || item.Destroyed || !item.IngestibleNow
                || !item.def.IsNutritionGivingIngestible
                || item.TryGetComp<CompHatcher>() != null)
                return 0f;

            try
            {
                if (!FoodUtility.WillEat(animal, item, animal, false, false))
                    return 0f;
                float nutrition = FoodUtility.NutritionForEater(animal, item);
                return nutrition > 0f && !float.IsNaN(nutrition)
                    && !float.IsInfinity(nutrition) ? nutrition : 0f;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Zoology] Unable to evaluate barn food: " + e,
                    item.thingIDNumber ^ animal.thingIDNumber ^ 0xB4A5);
                return 0f;
            }
        }

        internal bool HasResidentEater(Thing item)
        {
            if (item == null || item.Destroyed) return false;
            int now = GenTicks.TicksGame;
            if (edibleFoodCacheTick < 0 || now < edibleFoodCacheTick
                || now - edibleFoodCacheTick >= 150)
            {
                edibleFoodCache.Clear();
                edibleFoodCacheTick = now;
            }

            if (edibleFoodCache.TryGetValue(item, out bool canEat))
                return canEat;

            canEat = false;
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i] is Pawn pawn
                    && NutritionForBarnResident(pawn, item) > 0f)
                {
                    canEat = true;
                    break;
                }
            }
            edibleFoodCache[item] = canEat;
            return canEat;
        }

        private float CalculateFoodRestockPriority()
        {
            float dailyDemand = 0f;
            float minimumIndividualCoverage = float.PositiveInfinity;
            int consumers = 0;

            // The holder can contain hundreds of small animals but only
            // 18 food stacks. Separating food first avoids an O(pawns²)
            // owner scan when the barn is crowded with chickens or piglets.
            var foodItems = new List<Thing>(MaximumFoodStacks);
            for (int i = 0; i < contents.Count; i++)
            {
                Thing item = contents[i];
                if (item is not Pawn && !item.Destroyed
                    && item.def.IsNutritionGivingIngestible
                    && item.TryGetComp<CompHatcher>() == null)
                    foodItems.Add(item);
            }
            float[] maxNutritionPerItem = new float[foodItems.Count];

            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i] is not Pawn animal || animal.Dead
                    || animal.Destroyed || animal.needs?.food == null)
                    continue;

                float consumption = animal.needs.food.FoodFallPerTick * 60000f;
                if (!(consumption > 0f) || float.IsNaN(consumption)
                    || float.IsInfinity(consumption))
                    continue;

                consumers++;
                dailyDemand += consumption;
                // Suckling young consume their mother's real nutrition,
                // rather than the barn's feed directly. Count their demand
                // against the shared reserves, but do not treat an otherwise
                // well-fed nursing litter as having zero edible food.
                if (LactationSettingsGate.Enabled()
                    && AnimalLactationUtility.IsAnimalBaby(animal)
                    && HasSuitableLactatingFemale(animal))
                    continue;

                float available = 0f;

                for (int j = 0; j < foodItems.Count; j++)
                {
                    Thing item = foodItems[j];
                    float nutrition = NutritionForBarnResident(animal, item);
                    if (nutrition <= 0f) continue;

                    available += nutrition * item.stackCount;
                    if (nutrition > maxNutritionPerItem[j])
                        maxNutritionPerItem[j] = nutrition;
                }

                minimumIndividualCoverage = Math.Min(minimumIndividualCoverage,
                    available / consumption);
            }

            // Empty barns remain eligible for pre-stocking after occupied
            // barns are supplied, but never compete ahead of hungry herds.
            if (consumers == 0 || !(dailyDemand > 0f))
                return 0f;

            // Milk-dependent young contribute to total demand while their
            // lactating mothers contribute to edible stock coverage.
            float totalNutrition = 0f;
            for (int i = 0; i < foodItems.Count; i++)
                totalNutrition += maxNutritionPerItem[i] * foodItems[i].stackCount;

            // Total nutrition / total consumption prevents shared food from
            // being counted once per animal. The minimum individual coverage
            // also detects species that cannot eat the stored food. These are
            // relative rankings, not artificial stocking or feeding limits.
            float coverageDays = Math.Min(totalNutrition / dailyDemand,
                minimumIndividualCoverage);
            if (!(coverageDays >= 0f) || float.IsNaN(coverageDays))
                return 1f;
            return 1f / (1f + coverageDays);
        }
    }
}
#endif
