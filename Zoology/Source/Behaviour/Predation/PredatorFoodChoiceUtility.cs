using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Scores use vanilla FoodOptimality for items and Zoology's existing
    // GetPreyScoreFor for prey; neither adds a second distance or speed cost.
    internal static class PredatorFoodChoiceUtility
    {
        private const float FoodBaseline = 300f;
        internal const int WildForageMinStack = 2;
        internal const int WildForageMaxStack = 5;
        internal const int WildForageWorkTicks = 240;
        private const int WorkTicksPerScore = 10;
        private const float HarvestRadius = 35f;
        private const int HarvestSearchIntervalTicks = 600;
        private const int WildForageIntervalTicks = 2500;
        private const int NearbyFoodSearchIntervalTicks = 600;
        private const int NearbyFoodMaxRegions = 8;
        private const float NearbyFoodMaxDistance = 35f;
        private static readonly FieldInfo skillBaseValueField =
            AccessTools.Field(typeof(SkillNeed_BaseBonus), "baseValue");
        private static readonly FieldInfo skillBonusPerLevelField =
            AccessTools.Field(typeof(SkillNeed_BaseBonus), "bonusPerLevel");
        private static readonly Dictionary<SkillNeed, float> plantFiveSkillFactors =
            new Dictionary<SkillNeed, float>(4);
        private static readonly Dictionary<StatDef, float> plantsFiveStatValues =
            new Dictionary<StatDef, float>(2);
        private static readonly JobGiver_WildForageSite forageSiteGiver = new JobGiver_WildForageSite();

        // All new feeding decisions, including existing jobs loaded from
        // saves, share this master-aware feature gate.
        internal static bool FoodChoiceEnabled
        {
            get
            {
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                return settings == null ||
                    (!settings.DisableAllRuntimePatches && settings.EnableAnimalFoodChoice);
            }
        }

        // Wild Forage has no dedicated Harmony patch. Toggling it never
        // requires rebuilding the other runtime patch sets.
        internal static bool WildForageEnabled
        {
            get
            {
                if (!FoodChoiceEnabled || !ModsConfig.OdysseyActive)
                    return false;
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                return settings == null || settings.EnableWildAnimalForage;
            }
        }

        internal static bool EnabledFor(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.RaceProps?.predator != true
                || pawn.Downed || pawn.InMentalState)
                return false;
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            return FoodChoiceEnabled
                && (settings == null || settings.EnableAdvancedPredationLogic);
        }

        internal static float ScoreLivePrey(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
                return float.NegativeInfinity;
            // The vanilla hunting search and this food chooser use exactly
            // the same prey score, including the one existing speed adjustment.
            // The dynamic reward for body size and constriction is contributed
            // to GetPreyScoreFor by the existing Zoology postfix.
            return FoodBaseline + FoodUtility.GetPreyScoreFor(predator, prey);
        }

        // Vanilla's animal search selects the closest food, not the highest
        // FoodOptimality. This uses two existing spatially indexed categories,
        // never a map-wide Thing list or a second per-tick search.
        internal static bool TryPreferNearbyFood(Pawn eater, Thing selected,
            ThingDef selectedDef, bool allowCorpse, bool allowForbidden,
            bool ignoreReservations, out Thing preferred, out ThingDef preferredDef)
        {
            preferred = null;
            preferredDef = null;
            if (eater?.Spawned != true || selected?.Spawned != true
                || selectedDef?.ingestible == null)
                return false;

            bool checkRaw = (selectedDef.ingestible.foodType & FoodTypeFlags.Meal) != 0;
            bool checkCorpse = eater.RaceProps?.predator == true && allowCorpse
                && selected is not Corpse;
            if (!checkRaw && !checkCorpse)
                return false;

            CompAnimalStrategyState state = eater.TryGetComp<CompAnimalStrategyState>();
            int now = GenTicks.TicksGame;
            if (state != null)
            {
                if (state.nextNaturalFoodScan > now)
                    return false;
                state.nextNaturalFoodScan = now + NearbyFoodSearchIntervalTicks;
            }

            float bestScore = ScorePreparedFood(eater, selected, selectedDef,
                selected.Position);
            float maxDistance = NearbyFoodMaxDistance;
            TraverseParms traverse = TraverseParms.For(eater);

            if (checkRaw)
            {
                // Meals have a -12 modifier; raw foods have +12. The nearest
                // suitable raw item is sufficient to test a better score.
                Thing raw = GenClosest.ClosestThingReachable(
                    eater.Position, eater.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                    PathEndMode.OnCell, traverse, maxDistance,
                    thing => thing != null && thing != selected && thing.Spawned
                        && thing.def?.ingestible != null && !thing.def.IsDrug
                        && (thing.def.ingestible.preferability == FoodPreferability.RawBad
                            || thing.def.ingestible.preferability == FoodPreferability.RawTasty)
                        && thing.IngestibleNow
                        && (allowForbidden || !thing.IsForbidden(eater))
                        && eater.WillEat(thing, eater, true, false)
                        && (ignoreReservations || eater.CanReserve(thing)),
                    searchRegionsMax: NearbyFoodMaxRegions);
                if (raw != null)
                {
                    ThingDef rawDef = FoodUtility.GetFinalIngestibleDef(raw);
                    float rawScore = ScorePreparedFood(eater, raw, rawDef, raw.Position);
                    if (rawScore > bestScore)
                    {
                        preferred = raw;
                        preferredDef = rawDef;
                        bestScore = rawScore;
                    }
                }
            }

            if (checkCorpse)
            {
                // A free corpse has a large existing benefit over ordinary
                // items. The Corpse request group is region-indexed, so only
                // a bounded set of nearby regions is visited.
                Thing corpse = GenClosest.ClosestThingReachable(
                    eater.Position, eater.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.Corpse),
                    PathEndMode.OnCell, traverse, maxDistance,
                    thing => thing is Corpse && thing.Spawned
                        && thing != selected && !thing.Destroyed
                        && thing.IngestibleNow
                        && (allowForbidden || !thing.IsForbidden(eater))
                        && eater.WillEat(thing, eater, true, false)
                        && (ignoreReservations || eater.CanReserve(thing))
                        && FoodUtility.FoodOptimality(eater, thing,
                            FoodUtility.GetFinalIngestibleDef(thing),
                            (eater.Position - thing.Position).LengthHorizontal) > bestScore,
                    searchRegionsMax: NearbyFoodMaxRegions);
                if (corpse != null)
                {
                    ThingDef corpseDef = FoodUtility.GetFinalIngestibleDef(corpse);
                    float corpseScore = ScorePreparedFood(eater, corpse, corpseDef,
                        corpse.Position);
                    if (corpseScore > bestScore)
                    {
                        preferred = corpse;
                        preferredDef = corpseDef;
                    }
                }
            }

            return preferred != null;
        }

        internal static float ScorePreparedFood(Pawn predator, Thing source, ThingDef foodDef,
            IntVec3 foodPosition)
        {
            if (predator == null || source == null)
                return float.NegativeInfinity;
            if (source is Pawn live && !live.Dead)
                return ScoreLivePrey(predator, live);
            foodDef ??= FoodUtility.GetFinalIngestibleDef(source);
            if (foodDef == null)
                return float.NegativeInfinity;
            return FoodUtility.FoodOptimality(predator, source, foodDef,
                (predator.Position - foodPosition).LengthHorizontal);
        }

        internal static float ScoreShelterFood(Pawn predator, Thing shelter, Thing contents)
        {
            if (predator == null || shelter == null || contents == null)
                return float.NegativeInfinity;
            float distance = (predator.Position - shelter.Position).LengthHorizontal;
            float score;
            if (contents is Pawn prey && !prey.Dead)
            {
                // A sheltered pawn has no spawned position. The size, threat
                // and vulnerability component can still be evaluated without
                // inventing an on-map position or moving the pawn.
                // Held prey has no map position. Its biology and physical
                // suitability are still known. The shelter's distance is
                // subtracted once, separately.
                score = FoodBaseline
                    + LargeMammalPredationUtility.GetPreySelectionScore(predator, prey)
                    + GetPreyOpportunityAdjustment(predator, prey, held: true)
                    - distance;
            }
            else
            {
                score = ScorePreparedFood(predator, contents,
                    FoodUtility.GetFinalIngestibleDef(contents), shelter.Position);
                // Multiple eggs are an actual larger food resource, not five
                // independent search targets.
                if (contents.stackCount > 1 && contents.def.ingestible != null)
                    score += NutritionReward(predator,
                        contents.stackCount * contents.def.ingestible.CachedNutrition);
            }
            // Only the actual work duration matters: 10 ticks = 1 point.
            // There is no climbing penalty.
            score -= (float)AnimalBurrowHunt.OpeningTicks(shelter) / WorkTicksPerScore;
            return score;
        }

        internal static float ScoreFishingJob(Pawn predator, Job job)
        {
            if (predator == null || job == null || job.def != JobDefOf.FishAnimal)
                return float.NegativeInfinity;
            float fishingSpeed = predator.GetStatValue(StatDefOf.FishingSpeed);
            if (fishingSpeed <= 0f)
                return float.NegativeInfinity;
            float distance = (predator.Position - job.targetB.Cell).LengthHorizontal;
            return FoodBaseline - distance - 1500f / fishingSpeed / WorkTicksPerScore;
        }

        internal static float ScoreFoodJob(Pawn predator, Job job)
        {
            if (job == null)
                return float.NegativeInfinity;
            if (job.def == JobDefOf.PredatorHunt && job.targetA.Thing is Pawn prey)
                return ScoreLivePrey(predator, prey);
            if (job.def == JobDefOf.FishAnimal)
                return ScoreFishingJob(predator, job);
            if ((job.def == JobDefOf.Harvest
                || job.def == DefDatabase<JobDef>.GetNamedSilentFail("Zoology_AnimalHarvest"))
                && job.targetA.Thing is Plant plant)
                return ScoreHarvestPlant(predator, plant);
            if (job.def == DefDatabase<JobDef>.GetNamedSilentFail("Zoology_WildForage"))
                return ScoreWildForage(predator, job.targetA.Cell);
            if (job.def != JobDefOf.Ingest)
                return float.NegativeInfinity;
            Thing food = job.targetA.Thing;
            return food != null
                ? ScorePreparedFood(predator, food, FoodUtility.GetFinalIngestibleDef(food),
                    food.PositionHeld)
                : float.NegativeInfinity;
        }

        // This correction is part of the existing GetPreyScoreFor evaluation,
        // so vanilla's prey selector and the unified chooser rank the same
        // live candidates. No new per-prey Harmony hook or map scan.
        internal static float GetPreyOpportunityAdjustment(Pawn predator, Pawn prey, bool held = false)
        {
            if (!FoodChoiceEnabled || predator == null || prey == null)
                return 0f;
            float adjustment = 0f;
            if (LargeMammalPredationUtility.UsesLargeMammalSizing(predator))
            {
                // A small carcass cannot sustain a large mammal as well as a
                // similarly sized prey. Preserve existing large-mammal fit
                // and combat scores; add only the obtainable-body-size term.
                float ratio = predator.BodySize > 0f ? prey.BodySize / predator.BodySize : 0f;
                adjustment += FoodBaseline * (UnityEngine.Mathf.Clamp01(ratio) - 1f);
            }

            ModExtension_Constrictor constrictor =
                DefModExtensionCache<ModExtension_Constrictor>.Get(predator.def);
            if (constrictor != null && prey.BodySize <= predator.BodySize * constrictor.maxPreySizeRatio
                && prey.RaceProps?.IsFlesh == true && !prey.Dead
                && (held || prey.Spawned && predator.Map == prey.Map)
                && AnimalConstriction.PreyHasConstrictibleAnatomy(prey))
            {
                // This decisive, capability-specific advantage outweighs size
                // and combat-score differences between otherwise valid prey.
                adjustment += 2f * FoodBaseline;
            }
            return adjustment;
        }

        internal static bool CanHarvest(Pawn pawn)
        {
            if (!FoodChoiceEnabled || pawn?.RaceProps?.Animal != true || pawn.Downed || pawn.InMentalState
                || MammalBabyCache.ShouldUseBabyFoodRules(pawn))
                return false;
            FoodTypeFlags diet = pawn.RaceProps.foodType;
            return (diet & FoodTypeFlags.Meat) != 0
                && (diet & FoodTypeFlags.VegetableOrFruit) != 0;
        }

        internal static bool CanWildForage(Pawn pawn)
        {
            if (!WildForageEnabled || pawn?.Spawned != true
                || pawn.Faction != null || pawn.Downed || pawn.InMentalState
                || MammalBabyCache.ShouldUseBabyFoodRules(pawn)
                || pawn.Map?.Biome?.foragedFood == null
                || pawn.Map.Biome.forageability <= 0f
                || TrainableDefOf.Forage == null
                || pawn.RaceProps.specialTrainables == null
                || !pawn.RaceProps.specialTrainables.Contains(TrainableDefOf.Forage))
                return false;
            ThingDef foraged = pawn.Map.Biome.foragedFood;
            return foraged.ingestible != null && foraged.IsNutritionGivingIngestible
                && pawn.WillEat(foraged, pawn, true, false);
        }

        private static float NutritionReward(Pawn eater, float nutrition)
        {
            float capacity = eater?.needs?.food?.MaxLevel ?? 0f;
            return capacity > 0f ? FoodBaseline * UnityEngine.Mathf.Clamp01(nutrition / capacity) : 0f;
        }

        internal static float ScoreWildForage(Pawn eater, IntVec3 location)
        {
            ThingDef product = eater?.Map?.Biome?.foragedFood;
            if (product?.ingestible == null)
                return float.NegativeInfinity;
            float expectedCount = (WildForageMinStack + WildForageMaxStack) * 0.5f;
            float nutrition = expectedCount * product.ingestible.CachedNutrition;
            // Foraging is valued by its own edible yield, duration and travel
            // distance. FishingSpeed is unrelated to gathering land food.
            return NutritionReward(eater, nutrition)
                - WildForageWorkTicks / (float)WorkTicksPerScore
                - (eater.Position - location).LengthHorizontal;
        }

        private static float ExpectedPlantYield(Plant plant)
        {
            if (plant?.def?.plant == null)
                return 0f;
            float fraction = UnityEngine.Mathf.InverseLerp(
                plant.def.plant.harvestMinGrowth, 1f, plant.Growth);
            float expected = plant.def.plant.harvestYield * (0.5f + 0.5f * fraction);
            if (plant.MaxHitPoints > 0)
                expected *= UnityEngine.Mathf.Lerp(0.5f, 1f,
                    (float)plant.HitPoints / plant.MaxHitPoints);
            if (plant.def.plant.harvestYieldAffectedByDifficulty)
                expected *= Find.Storyteller.difficulty.cropYieldFactor;
            return expected;
        }

        // Use the actual game's Plants skill curves at level 5. Animals do
        // not have a human skill tracker; this computes the counterfactual
        // stat from the existing animal stat without modifying its state.
        private static float SkillNeedAtLevelFive(SkillNeed need)
        {
            if (need is SkillNeed_Curve curve)
                return curve.curve.Evaluate(5f);
            if (need is SkillNeed_Direct direct)
            {
                var values = direct.valuesPerLevel;
                return values.Count > 0
                    ? values[UnityEngine.Mathf.Min(5, values.Count - 1)] : 1f;
            }
            if (need is SkillNeed_BaseBonus)
            {
                if (plantFiveSkillFactors.TryGetValue(need, out float cached))
                    return cached;

                // SkillNeed_BaseBonus has private baseValue/bonusPerLevel
                // fields in vanilla. Read each def once, never per tick.
                if (skillBaseValueField == null || skillBonusPerLevelField == null)
                {
                    Log.ErrorOnce("[Zoology] Unable to resolve vanilla Plants-5 skill factor.", 99173112);
                    return 0f;
                }
                float value = (float)skillBaseValueField.GetValue(need)
                    + 5f * (float)skillBonusPerLevelField.GetValue(need);
                plantFiveSkillFactors.Add(need, value);
                return value;
            }
            return 1f;
        }

        internal static float AnimalStatAtPlantsFive(Pawn eater, StatDef stat)
        {
            // Skill-five productivity depends only on the stat definition.
            // Reuse it across all plants and animals without per-candidate
            // skill evaluation or reflection.
            if (plantsFiveStatValues.TryGetValue(stat, out float cached))
                return cached;
            // Animals often have no Manipulation capacity or skill tracker.
            // Reconstruct the normal Plants-5 colonist productivity from the
            // game's own stat def, instead of inheriting the animal's
            // unskilled (possibly minimum-clamped) work stat.
            float value = stat.defaultBaseValue;
            var offsets = stat.skillNeedOffsets;
            if (offsets != null)
                for (int i = 0; i < offsets.Count; i++)
                    if (offsets[i].skill == SkillDefOf.Plants)
                        value += SkillNeedAtLevelFive(offsets[i]);
            var factors = stat.skillNeedFactors;
            if (factors != null)
                for (int i = 0; i < factors.Count; i++)
                    if (factors[i].skill == SkillDefOf.Plants)
                        value *= SkillNeedAtLevelFive(factors[i]);
            value = UnityEngine.Mathf.Clamp(value, stat.minValue, stat.maxValue);
            plantsFiveStatValues.Add(stat, value);
            return value;
        }

        internal static float HarvestWorkRate(Pawn eater, Plant plant)
        {
            return AnimalStatAtPlantsFive(eater, StatDefOf.PlantWorkSpeed)
                * UnityEngine.Mathf.Lerp(3.3f, 1f, plant.Growth);
        }

        internal static float ScoreHarvestPlant(Pawn eater, Plant plant)
        {
            if (eater == null || plant?.Spawned != true
                || plant.def.plant?.harvestedThingDef?.ingestible == null)
                return float.NegativeInfinity;
            ThingDef product = plant.def.plant.harvestedThingDef;
            float workRate = HarvestWorkRate(eater, plant);
            if (workRate <= 0f)
                return float.NegativeInfinity;
            float workTicks = plant.def.plant.harvestWork / workRate;
            float nutrition = ExpectedPlantYield(plant) * product.ingestible.CachedNutrition;
            float yield = AnimalStatAtPlantsFive(eater, StatDefOf.PlantHarvestYield);
            if (plant.def.plant.harvestFailable)
                nutrition *= UnityEngine.Mathf.Clamp01(yield);
            if (yield > 1f)
                nutrition *= yield;

            float foodCapacity = eater.needs?.food?.MaxLevel ?? 0f;
            if (nutrition <= 0f || foodCapacity <= 0f)
                return float.NegativeInfinity;

            // Rate harvesting by the work needed to obtain a common
            // nutrition amount (one full food capacity), like fishing's
            // 300-point baseline minus work time. A small crop needs more
            // harvests, but is not additionally penalized for each one.
            float nutritionFraction = UnityEngine.Mathf.Min(1f, nutrition / foodCapacity);
            float equivalentWorkTicks = workTicks / nutritionFraction;
            return FoodBaseline - equivalentWorkTicks / WorkTicksPerScore
                - (eater.Position - plant.Position).LengthHorizontal;
        }

        private static bool ValidHarvestPlant(Pawn eater, Thing source)
        {
            if (source is not Plant plant || !plant.Spawned || plant.Destroyed
                || plant.IsForbidden(eater) || !plant.HarvestableNow || !plant.CanYieldNow()
                || plant.def.plant?.harvestedThingDef is not ThingDef food
                || !food.IsNutritionGivingIngestible || food.IsDrug
                || !eater.WillEat(food, eater, true, false))
                return false;
            return HarvestWorkRate(eater, plant) > 0f;
        }

        internal static Job TryFindHarvestJob(Pawn eater, out float score)
        {
            score = float.NegativeInfinity;
            if (!CanHarvest(eater))
                return null;
            Thing target = GenClosest.ClosestThingReachable(
                eater.Position, eater.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HarvestablePlant),
                PathEndMode.Touch, TraverseParms.For(eater),
                HarvestRadius, thing => ValidHarvestPlant(eater, thing));
            if (target is not Plant plant)
                return null;
            score = ScoreHarvestPlant(eater, plant);
            JobDef harvest = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_AnimalHarvest");
            return harvest != null ? JobMaker.MakeJob(harvest, plant) : null;
        }

        internal static Job TryFindWildForageJob(Pawn eater, out float score)
        {
            score = float.NegativeInfinity;
            if (!CanWildForage(eater))
                return null;
            // A successful vanilla site search is performed only after the
            // no-pathfinding yield score can beat the current food choice.
            Job vanilla = forageSiteGiver.FindSite(eater);
            if (vanilla == null)
                return null;
            score = ScoreWildForage(eater, vanilla.targetA.Cell);
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_WildForage");
            return def != null ? JobMaker.MakeJob(def, vanilla.targetA.Cell) : null;
        }
    }

    // The vanilla Odyssey job giver uses the same natural-site rules. The
    // wild variant is invoked only from a hungry animal's food decision.
    internal sealed class JobGiver_WildForageSite : JobGiver_Forage
    {
        internal Job FindSite(Pawn pawn) => base.TryGiveJob(pawn);
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.TryFindBestFoodSourceFor))]
    internal static class Patch_UnifiedPredatorFoodSource
    {
        static void Postfix(Pawn getter, Pawn eater, bool forceScanWholeMap,
            bool allowCorpse, bool allowForbidden, bool ignoreReservations,
            ref bool __result, ref Thing foodSource, ref ThingDef foodDef)
        {
            if (getter != eater || eater?.RaceProps?.Animal != true)
                return;
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            if (settings?.DisableAllRuntimePatches == true || !PredatorFoodChoiceUtility.FoodChoiceEnabled)
                return;
            if (PredatorFoodChoiceUtility.TryPreferNearbyFood(eater, foodSource,
                foodDef, allowCorpse, allowForbidden, ignoreReservations,
                out Thing better, out ThingDef betterDef))
            {
                foodSource = better;
                foodDef = betterDef;
                __result = true;
            }
            if (!PredatorFoodChoiceUtility.EnabledFor(eater)
                || foodSource is Pawn || PredationHarmonyPatches.ShouldBlockLivePreyHunt(eater))
                return;

            // A paired own corpse wins by the existing +10000 score. Skip
            // the live-prey search altogether in this common post-kill state.
            if (foodSource is Corpse corpse
                && PredatorPreyPairGameComponent.Instance?.IsPaired(eater, corpse) == true)
                return;

            float foodScore = foodSource?.Spawned == true
                ? PredatorFoodChoiceUtility.ScorePreparedFood(eater, foodSource, foodDef,
                    foodSource.Position)
                : float.NegativeInfinity;
            Pawn prey = PredationHarmonyPatches.TryGetVanillaHuntTarget(eater, forceScanWholeMap);
            if (prey == null || !FoodUtility.IsAcceptablePreyFor(eater, prey)
                || !eater.CanReach(prey, PathEndMode.ClosestTouch, Danger.Deadly))
                return;

            float preyScore = PredatorFoodChoiceUtility.ScoreLivePrey(eater, prey);
            if (preyScore <= foodScore)
                return;
            foodSource = prey;
            foodDef = FoodUtility.GetFinalIngestibleDef(prey);
            __result = true;
        }
    }

    // In the same rare hunger decision also compare embedded refuge food and
    // Odyssey fishing. The usual expensive searches remain map-index-budgeted;
    // fishing cannot outrank ready food with score >= the vanilla baseline.
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    internal static class Patch_UnifiedPredatorFoodJobs
    {
        [HarmonyPriority(Priority.First)]
        static void Postfix(Pawn pawn, ref Job __result)
        {
            if (pawn?.Spawned != true || pawn.RaceProps?.Animal != true
                || pawn.needs?.food == null || pawn.Downed || pawn.InMentalState
                || pawn.needs.food.CurLevelPercentage >= pawn.RaceProps.FoodLevelPercentageWantEat
                || MammalBabyCache.ShouldUseBabyFoodRules(pawn))
                return;
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            if (settings?.DisableAllRuntimePatches == true || !PredatorFoodChoiceUtility.FoodChoiceEnabled)
                return;
            if (__result != null && __result.def != JobDefOf.Ingest
                && __result.def != JobDefOf.PredatorHunt
                && __result.def != JobDefOf.FishAnimal
                && __result.def != JobDefOf.Harvest)
                return;

            bool predator = PredatorFoodChoiceUtility.EnabledFor(pawn);
            if (__result?.def == JobDefOf.Harvest
                && __result.targetA.Thing is Plant vanillaPlant
                && PredatorFoodChoiceUtility.CanHarvest(pawn))
            {
                JobDef animalHarvest =
                    DefDatabase<JobDef>.GetNamedSilentFail("Zoology_AnimalHarvest");
                if (animalHarvest != null)
                    __result = JobMaker.MakeJob(animalHarvest, vanillaPlant);
            }

            float bestScore = PredatorFoodChoiceUtility.ScoreFoodJob(pawn, __result);
            if (predator && pawn.needs.food.CurLevelPercentage < 0.3f && bestScore < 1000f)
            {
                Job shelterJob = JobGiver_ExcavateAnimalBurrow.TryPlanFoodJob(pawn,
                    out float shelterScore);
                if (shelterJob != null && shelterScore > bestScore)
                {
                    __result = shelterJob;
                    bestScore = shelterScore;
                }
            }

            if (predator && ModsConfig.OdysseyActive && pawn.RaceProps.canFishForFood
                && pawn.Map?.waterBodyTracker?.AnyBodyContainsFish == true
                && bestScore < 300f)
            {
                // Fishing has a 1500/FishingSpeed tick work cost. Do not run
                // the allocation-heavy vanilla search unless it can beat the
                // current alternative even before travel distance.
                float fishingSpeed = pawn.GetStatValue(StatDefOf.FishingSpeed);
                if (fishingSpeed > 0f && 300f - 150f / fishingSpeed > bestScore)
                {
                    Job fish = JobGiver_GetFood.TryFindFishJob(pawn);
                    if (fish != null)
                    {
                        float fishScore = PredatorFoodChoiceUtility.ScoreFishingJob(pawn, fish);
                        if (fishScore > bestScore)
                        {
                            __result = fish;
                            bestScore = fishScore;
                        }
                    }
                }
            }

            CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null)
                return;
            int now = GenTicks.TicksGame;
            if (bestScore < 300f && PredatorFoodChoiceUtility.CanHarvest(pawn)
                && state.nextWildHarvestPlan <= now)
            {
                state.nextWildHarvestPlan = now + 600;
                Job harvest = PredatorFoodChoiceUtility.TryFindHarvestJob(pawn, out float harvestScore);
                if (harvest != null && harvestScore > bestScore)
                {
                    __result = harvest;
                    bestScore = harvestScore;
                }
            }

            if (pawn.Faction == null && bestScore < 300f
                && state.nextWildForagePlan <= now
                && PredatorFoodChoiceUtility.CanWildForage(pawn))
            {
                state.nextWildForagePlan = now + 2500;
                Job forage = PredatorFoodChoiceUtility.TryFindWildForageJob(pawn,
                    out float forageScore);
                if (forage != null && forageScore > bestScore)
                    __result = forage;
            }
        }
    }
}
