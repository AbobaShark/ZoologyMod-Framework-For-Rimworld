using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Caravan_NeedsTracker), nameof(Caravan_NeedsTracker.TrySatisfyPawnsNeeds))]
    internal static class Patch_Caravan_NeedsTracker_TrySatisfyPawnsNeeds_MammalBabies
    {
        private static bool Prepare() => LactationSettingsGate.Enabled();

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Caravan_NeedsTracker __instance, int delta)
        {
            try
            {
                Patch_Caravan_NeedsTracker_TrySatisfyFoodNeed_MammalBabies.PrepareScanCache(__instance);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Caravan mammal baby cache preparation failed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Caravan_NeedsTracker), "TrySatisfyFoodNeed")]
    internal static class Patch_Caravan_NeedsTracker_TrySatisfyFoodNeed_MammalBabies
    {
        private sealed class CaravanLactationScanCache
        {
            public int Tick = int.MinValue;
            public readonly HashSet<int> HandledBabyIds = new HashSet<int>();
            public readonly HashSet<int> HungryBabyIds = new HashSet<int>();
            public readonly List<Pawn> FeederCandidates = new List<Pawn>(8);
        }

        private static readonly Dictionary<Caravan, CaravanLactationScanCache> scanCacheByCaravan = new Dictionary<Caravan, CaravanLactationScanCache>();
        private static Game cachedGame;

        private static bool Prepare() => LactationSettingsGate.Enabled();

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Caravan_NeedsTracker __instance, Pawn pawn, Need_Food food, int delta)
        {
            if (!LactationSettingsGate.Enabled())
            {
                return true;
            }

            Caravan caravan = __instance?.caravan;
            if (caravan == null || pawn == null || food == null)
            {
                return true;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!TryGetPreparedScanCache(caravan, currentTick, out CaravanLactationScanCache cache))
            {
                cache = GetOrRefreshScanCache(caravan, currentTick);
            }

            if (!cache.HandledBabyIds.Contains(pawn.thingIDNumber))
            {
                return true;
            }

            try
            {
                HandleMammalBabyFoodNeed(caravan, pawn, food, delta, cache);
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Caravan mammal baby feeding prefix failed: {ex}");
                return true;
            }
        }

        internal static void PrepareScanCache(Caravan_NeedsTracker tracker)
        {
            if (!LactationSettingsGate.Enabled())
            {
                return;
            }

            Caravan caravan = tracker?.caravan;
            if (caravan == null)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            GetOrRefreshScanCache(caravan, currentTick);
        }

        private static void HandleMammalBabyFoodNeed(Caravan caravan, Pawn pawn, Need_Food food, int delta, CaravanLactationScanCache cache)
        {
            EnsureBiologicalMotherLactationInCaravan(caravan, pawn);

            if (cache.HungryBabyIds.Contains(pawn.thingIDNumber))
            {
                Pawn feeder = FindBestFeederInCaravan(cache.FeederCandidates, pawn);
                if (feeder != null)
                {
                    SuckleInCaravan(pawn, feeder, delta);
                }
            }

            if (!food.Starving)
            {
                food.lastNonStarvingTick = Find.TickManager.TicksGame;
            }

            if ((int)food.CurCategory < 1)
            {
                return;
            }

            if (!CaravanInventoryUtility.TryGetBestFood(caravan, pawn, out Thing foodThing, out Pawn owner))
            {
                return;
            }

            food.CurLevel += foodThing.Ingested(pawn, food.NutritionWanted);
            if (!foodThing.Destroyed)
            {
                return;
            }

            if (owner != null)
            {
                owner.inventory.innerContainer.Remove(foodThing);
                caravan.RecacheInventory();
            }

            if (!caravan.notifiedOutOfFood
                && !CaravanInventoryUtility.TryGetBestFood(caravan, pawn, out foodThing, out owner))
            {
                Messages.Message("MessageCaravanRanOutOfFood".Translate(caravan.LabelCap), caravan, MessageTypeDefOf.ThreatBig);
                caravan.notifiedOutOfFood = true;
            }
        }

        private static void ResetCacheForGame(Game currentGame)
        {
            cachedGame = currentGame;
            scanCacheByCaravan.Clear();
        }

        private static void ResetCacheForCurrentGameIfNeeded()
        {
            Game currentGame = Current.Game;
            if (!ReferenceEquals(cachedGame, currentGame))
            {
                ResetCacheForGame(currentGame);
            }
        }

        private static bool TryGetPreparedScanCache(Caravan caravan, int currentTick, out CaravanLactationScanCache cache)
        {
            ResetCacheForCurrentGameIfNeeded();

            if (caravan != null
                && scanCacheByCaravan.TryGetValue(caravan, out cache)
                && cache.Tick == currentTick)
            {
                return true;
            }

            cache = null;
            return false;
        }

        private static CaravanLactationScanCache GetOrRefreshScanCache(Caravan caravan, int currentTick)
        {
            ResetCacheForCurrentGameIfNeeded();

            if (!scanCacheByCaravan.TryGetValue(caravan, out CaravanLactationScanCache cache))
            {
                cache = new CaravanLactationScanCache();
                scanCacheByCaravan[caravan] = cache;
            }

            if (cache.Tick == currentTick)
            {
                return cache;
            }

            cache.Tick = currentTick;
            cache.HandledBabyIds.Clear();
            cache.HungryBabyIds.Clear();
            cache.FeederCandidates.Clear();

            List<Pawn> pawns = caravan?.PawnsListForReading;
            if (pawns == null || pawns.Count == 0)
            {
                return cache;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn caravanPawn = pawns[i];
                if (caravanPawn == null || caravanPawn.Dead || caravanPawn.Destroyed)
                {
                    continue;
                }

                if (caravanPawn.IsMammal() && MammalBabyCache.IsMammalBaby(caravanPawn))
                {
                    cache.HandledBabyIds.Add(caravanPawn.thingIDNumber);

                    if (AnimalLactationUtility.ChildWantsSuckle(caravanPawn))
                    {
                        EnsureBiologicalMotherLactationInCaravan(caravan, caravanPawn);
                        cache.HungryBabyIds.Add(caravanPawn.thingIDNumber);
                    }
                }
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn caravanPawn = pawns[i];
                if (caravanPawn == null || caravanPawn.Dead || caravanPawn.Destroyed)
                {
                    continue;
                }

                if (AnimalLactationUtility.CanMotherFeed(caravanPawn))
                {
                    cache.FeederCandidates.Add(caravanPawn);
                }
            }

            return cache;
        }

        private static void EnsureBiologicalMotherLactationInCaravan(Caravan caravan, Pawn baby)
        {
            Pawn mother = TryGetBiologicalMother(baby);
            if (mother == null
                || mother.Dead
                || mother.Destroyed
                || mother.gender != Gender.Female
                || !mother.IsMammal()
                || !AnimalLactationUtility.IsCrossBreedCompatible(mother, baby))
            {
                return;
            }

            if (!ReferenceEquals(mother.GetCaravan(), caravan) || !ReferenceEquals(baby.GetCaravan(), caravan))
            {
                return;
            }

            AnimalLactationUtility.EnsureLactatingHediff(mother);
        }

        private static Pawn TryGetBiologicalMother(Pawn baby)
        {
            List<DirectPawnRelation> relations = baby?.relations?.DirectRelations;
            if (relations == null)
            {
                return null;
            }

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation.def == PawnRelationDefOf.Parent && relation.otherPawn?.gender == Gender.Female)
                {
                    return relation.otherPawn;
                }
            }

            return null;
        }

        private static Pawn FindBestFeederInCaravan(List<Pawn> pawns, Pawn baby)
        {
            Pawn best = null;
            float bestFood = -1f;
            if (pawns == null || pawns.Count == 0)
            {
                return null;
            }

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
}
