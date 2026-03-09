using System;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    
    
    
    [HarmonyPatch(typeof(FoodUtility), "FoodIsSuitable", new Type[] { typeof(Pawn), typeof(ThingDef) })]
    static class Patch_FoodUtility_FoodIsSuitable
    {
        private static readonly System.Reflection.PropertyInfo ThingDefIsCorpseProperty = AccessTools.Property(typeof(ThingDef), "IsCorpse");

        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(Pawn p, ThingDef food, ref bool __result)
        {
            try
            {
                
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !ZoologyModSettings.EnableMammalLactation)
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

                
                var curStage = p.ageTracker?.CurLifeStage;
                if (curStage == null)
                {
                    
                    return true;
                }

                
                
                
                if (AnimalChildcareUtility.IsAnimalBabyLifeStage(curStage))
                {
                    
                    bool ok = (food.ingestible != null && food.ingestible.babiesCanIngest) && p.RaceProps.CanEverEat(food);
                    __result = ok;
                    return false; 
                }

                
                return true;
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

                var curStage = pawn.ageTracker?.CurLifeStage;
                if (AnimalChildcareUtility.IsAnimalBabyLifeStage(curStage))
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
        private static readonly System.Reflection.PropertyInfo ThingDefIsCorpseProperty = AccessTools.Property(typeof(ThingDef), "IsCorpse");

        static bool Prepare() => ZoologyModSettings.EnableMammalLactation;

        static bool Prefix(Pawn p, Thing food, Pawn getter, bool careIfNotAcceptableForTitle, bool allowVenerated, ref bool __result)
        {
            try
            {
                
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !ZoologyModSettings.EnableMammalLactation)
                    return true; 

                if (p == null || food == null) return true;

                
                if (!p.IsMammal()) return true;

                
                if (p.needs?.food == null)
                {
                    __result = false;
                    return false;
                }

                
                var curStage = p.ageTracker?.CurLifeStage;
                if (curStage == null) return true;

                if (AnimalChildcareUtility.IsAnimalBabyLifeStage(curStage))
                {
                    
                    
                    if (food is Corpse)
                    {
                        __result = false;
                        return false; 
                    }

                    
                    
                    try
                    {
                        var td = food.def;
                        if (td != null)
                        {
                            
                            if (ThingDefIsCorpseProperty != null)
                            {
                                var val = ThingDefIsCorpseProperty.GetValue(td, null);
                                if (val is bool b && b)
                                {
                                    __result = false;
                                    return false;
                                }
                            }
                        }
                    }
                    catch { /* не критично, продолжаем ванильную логику */ }
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
