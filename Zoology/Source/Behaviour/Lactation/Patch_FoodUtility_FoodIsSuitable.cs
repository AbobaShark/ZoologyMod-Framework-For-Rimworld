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
        private static readonly Dictionary<int, bool> isBabyByPawnId = new Dictionary<int, bool>(128);

        public static bool IsMammalBaby(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick != lastTick)
            {
                lastTick = tick;
                isBabyByPawnId.Clear();
            }

            int id = pawn.thingIDNumber;
            if (isBabyByPawnId.TryGetValue(id, out bool cached))
            {
                return cached;
            }

            bool isBaby = AnimalLactationUtility.IsAnimalBabyLifeStage(pawn.ageTracker?.CurLifeStage);
            isBabyByPawnId[id] = isBaby;
            return isBaby;
        }
    }
    
    
    
    [HarmonyPatch(typeof(FoodUtility), "FoodIsSuitable", new Type[] { typeof(Pawn), typeof(ThingDef) })]
    static class Patch_FoodUtility_FoodIsSuitable
    {
        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(Pawn p, ThingDef food, ref bool __result)
        {
            try
            {
                
                if (ZoologyModSettings.Instance == null || !ZoologyModSettings.EnableMammalLactation)
                {
                    return true;
                }

                if (p == null || food == null) return true; 

                
                if (!p.IsMammal()) return true;

                if (p.needs?.food == null)
                {
                    __result = false;
                    return false;
                }

                bool isBaby = MammalBabyCache.IsMammalBaby(p);
                if (!isBaby)
                {
                    return true;
                }

                if (!ZoologyTickLimiter.TryConsumeFoodIsSuitable(ZoologyTickLimiter.FoodIsSuitableBudgetPerTick))
                {
                    return true;
                }

                bool ok = (food.ingestible != null && food.ingestible.babiesCanIngest) && p.RaceProps.CanEverEat(food);
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
        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(Pawn pawn, ref Job __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !ZoologyModSettings.EnableMammalLactation)
                {
                    
                    return true;
                }

                if (pawn == null) return true;

                
                if (!pawn.IsMammal()) return true;

                
                if (pawn.needs?.food == null)
                {
                    __result = null;
                    return false;
                }

                if (MammalBabyCache.IsMammalBaby(pawn))
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
        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(Pawn p, Thing food, Pawn getter, bool careIfNotAcceptableForTitle, bool allowVenerated, ref bool __result)
        {
            try
            {
                
                if (ZoologyModSettings.Instance == null || !ZoologyModSettings.EnableMammalLactation)
                    return true; 

                if (p == null || food == null) return true;
                if (p.RaceProps?.Animal != true) return true;

                
                if (!p.IsMammal()) return true;

                if (p.needs?.food == null)
                {
                    __result = false;
                    return false;
                }

                bool isBaby = MammalBabyCache.IsMammalBaby(p);
                if (!isBaby) return true;

                if (!ZoologyTickLimiter.TryConsumeWillEat(ZoologyTickLimiter.WillEatBudgetPerTick))
                {
                    return true;
                }

                
                if (food is Corpse)
                {
                    __result = false;
                    return false; 
                }

                
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_FoodUtility_WillEat_Thing Prefix failed: {ex}");
                return true; 
            }
        }
    }
}
