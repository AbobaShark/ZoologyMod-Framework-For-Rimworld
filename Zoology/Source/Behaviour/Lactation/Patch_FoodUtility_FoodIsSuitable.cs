using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    internal static class MammalBabyCache
    {
        private static int lastTick = -1;
        private static readonly Dictionary<int, bool> shouldUseBabyFoodRulesByPawnId = new Dictionary<int, bool>(128);

        public static bool ShouldUseBabyFoodRules(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick != lastTick)
            {
                lastTick = tick;
                shouldUseBabyFoodRulesByPawnId.Clear();
            }

            int id = pawn.thingIDNumber;
            if (shouldUseBabyFoodRulesByPawnId.TryGetValue(id, out bool cached))
            {
                return cached;
            }

            bool shouldUse = ComputeShouldUseBabyFoodRules(pawn);
            shouldUseBabyFoodRulesByPawnId[id] = shouldUse;
            return shouldUse;
        }

        public static bool IsMammalBaby(Pawn pawn)
        {
            return ShouldUseBabyFoodRules(pawn);
        }

        private static bool ComputeShouldUseBabyFoodRules(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.RaceProps?.Animal != true) return false;
            if (!ZoologyCacheUtility.HasMammalExtension(pawn.def)
                && !ZoologyCacheUtility.HasMammalExtension(pawn.kindDef))
            {
                return false;
            }

            var stage = pawn.ageTracker?.CurLifeStage;
            if (AnimalLactationUtility.IsAnimalBabyLifeStage(stage))
            {
                return true;
            }

            if (stage != null && stage.developmentalStage == DevelopmentalStage.Baby)
            {
                return true;
            }

            try
            {
                var ages = pawn.RaceProps?.lifeStageAges;
                if (ages != null && ages.Count > 1 && pawn.ageTracker != null)
                {
                    return pawn.ageTracker.CurLifeStageIndex == 0;
                }
            }
            catch
            {
            }

            return false;
        }
    }
    
    
    
    [HarmonyPatch(typeof(FoodUtility), "FoodIsSuitable", new Type[] { typeof(Pawn), typeof(ThingDef) })]
    static class Patch_FoodUtility_FoodIsSuitable
    {
        static bool Prepare() => LactationSettingsGate.Enabled();

        static bool Prefix(Pawn p, ThingDef food, ref bool __result)
        {
            try
            {
                
                if (!LactationSettingsGate.Enabled())
                {
                    return true;
                }

                if (p == null || food == null) return true; 

                if (p.needs?.food == null)
                {
                    __result = false;
                    return false;
                }

                if (!MammalBabyCache.ShouldUseBabyFoodRules(p))
                {
                    return true;
                }

                IngestibleProperties ingestible = food.ingestible;
                if (ingestible == null)
                {
                    __result = false;
                    return false;
                }

                bool ok = ingestible.babiesCanIngest && p.RaceProps.CanEverEat(food);
                if (p.MapHeld == null)
                {
                    bool isDrug = food.IsDrug || ingestible.drugCategory != DrugCategory.None;
                    bool isCorpse = typeof(Corpse).IsAssignableFrom(food.thingClass);
                    ok = ok && food.IsNutritionGivingIngestible && !isDrug && !isCorpse;
                }

                __result = ok;
                return false; 
            }
            catch (Exception ex)
            {
                Log.Error("ZoologyMod: Patch_FoodUtility_FoodIsSuitable Prefix error: " + ex);
                return true; 
            }
        }
    }

    
    
    
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryFindFishJob", new Type[] { typeof(Pawn) })]
    static class Patch_JobGiver_GetFood_TryFindFishJob_BlockForMammalBabies
    {
        static bool Prepare() => LactationSettingsGate.Enabled();

        static bool Prefix(Pawn pawn, ref Job __result)
        {
            try
            {
                if (!LactationSettingsGate.Enabled())
                {
                    return true;
                }

                if (pawn == null) return true;

                
                if (pawn.needs?.food == null)
                {
                    __result = null;
                    return false;
                }

                if (MammalBabyCache.ShouldUseBabyFoodRules(pawn))
                {
                    __result = null;
                    return false; 
                }

                
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_TryFindFishJob Prefix failed: {ex}");
                return true;
            }
        }
    }

    
    
    
    
    
    [HarmonyPatch(typeof(FoodUtility), "WillEat", new Type[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    static class Patch_FoodUtility_WillEat_Thing_CorpseBlockForMammalBabies
    {
        static bool Prepare() => LactationSettingsGate.Enabled();

        static bool Prefix(Pawn p, Thing food, Pawn getter, bool careIfNotAcceptableForTitle, bool allowVenerated, ref bool __result)
        {
            try
            {
                
                if (!LactationSettingsGate.Enabled())
                    return true; 

                if (p == null || !(food is Corpse)) return true;

                if (!MammalBabyCache.ShouldUseBabyFoodRules(p))
                {
                    return true;
                }

                if (p.needs?.food == null)
                {
                    __result = false;
                    return false;
                }

                __result = false;
                return false; 
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_FoodUtility_WillEat_Thing Prefix failed: {ex}");
                return true; 
            }
        }
    }
}
