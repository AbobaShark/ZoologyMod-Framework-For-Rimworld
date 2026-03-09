using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    public static class PredationHarmonyPatches
    {
        private static readonly MethodInfo BestPawnToHuntForPredatorMethod =
            AccessTools.Method(typeof(FoodUtility), "BestPawnToHuntForPredator", new[] { typeof(Pawn), typeof(bool) });

        private const float OwnedCorpseBonus = 10000f;
        private const float UnpairedCorpseBonus = 300f;
        private const float GuardedCorpsePenalty = -10000f;
        private const float LiveAcceptablePreyBonus = 600f;

        static PredationHarmonyPatches()
        {
            var s = ZoologyModSettings.Instance;
            if (s != null && !s.EnablePredatorDefendCorpse)
            {
                return;
            }

            var harmony = new Harmony("com.abobashark.zoology.predatorpairs");
            try
            {
                
                var targetFoodOpt = AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality));
                var postfixFood = typeof(PredationHarmonyPatches).GetMethod(nameof(FoodOptimality_Postfix), BindingFlags.Static | BindingFlags.Public);
                var targetBestFoodSource = AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap));
                var postfixBestFoodSource = typeof(PredationHarmonyPatches).GetMethod(nameof(BestFoodSourceOnMap_Postfix), BindingFlags.Static | BindingFlags.Public);
                if (targetFoodOpt != null && postfixFood != null)
                {
                    harmony.Patch(targetFoodOpt, null, new HarmonyMethod(postfixFood));
                }
                else
                {
                    Log.Warning("Zoology: FoodUtility.FoodOptimality method or postfix not found; skipping patch.");
                }

                if (targetBestFoodSource != null && postfixBestFoodSource != null)
                {
                    harmony.Patch(targetBestFoodSource, null, new HarmonyMethod(postfixBestFoodSource));
                }
                else
                {
                    Log.Warning("Zoology: FoodUtility.BestFoodSourceOnMap method or postfix not found; skipping patch.");
                }

                
                MethodInfo pawnKillMethod = null;

                var killMethods = typeof(Pawn).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == "Kill")
                    .ToList();

                Type damageInfoType = typeof(DamageInfo);
                Type nullableDamageInfoType = typeof(Nullable<>).MakeGenericType(damageInfoType);

                foreach (var m in killMethods)
                {
                    var pars = m.GetParameters();
                    if (pars.Length >= 1)
                    {
                        var p0 = pars[0].ParameterType;
                        if (p0 == damageInfoType || p0 == nullableDamageInfoType)
                        {
                            pawnKillMethod = m;
                            break;
                        }
                        if (p0.IsGenericType && p0.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            var ga = p0.GetGenericArguments();
                            if (ga.Length == 1 && ga[0] == damageInfoType)
                            {
                                pawnKillMethod = m;
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(p0.FullName) && p0.FullName.IndexOf("DamageInfo", StringComparison.InvariantCultureIgnoreCase) >= 0)
                        {
                            pawnKillMethod = m;
                            break;
                        }
                    }
                }

                if (pawnKillMethod != null)
                {
                    var postfixKill = typeof(PredationHarmonyPatches).GetMethod(nameof(Pawn_Kill_Postfix), BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(pawnKillMethod, null, new HarmonyMethod(postfixKill));
                }
                else
                {
                    Log.Warning("Zoology: Pawn.Kill overload not found for patching.");
                }

                
                try
                {
                    Type carryType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a =>
                        {
                            try { return a.GetTypes(); } catch { return new Type[0]; }
                        })
                        .FirstOrDefault(t => t.Name == "Pawn_CarryTracker" || t.Name == "CarryTracker");

                    if (carryType != null)
                    {
                        
                        var method1 = carryType.GetMethod("TryStartCarry", new Type[] { typeof(Thing) });
                        if (method1 != null)
                        {
                            var postfix1 = typeof(PredationHarmonyPatches).GetMethod(nameof(PawnCarry_TryStartCarry_Postfix), BindingFlags.Static | BindingFlags.Public);
                            harmony.Patch(method1, null, new HarmonyMethod(postfix1));
                        }

                        
                        var method2 = carryType.GetMethod("TryStartCarry", new Type[] { typeof(Thing), typeof(int), typeof(bool) });
                        if (method2 != null)
                        {
                            var postfix2 = typeof(PredationHarmonyPatches).GetMethod(nameof(PawnCarry_TryStartCarry_Int_Postfix), BindingFlags.Static | BindingFlags.Public);
                            harmony.Patch(method2, null, new HarmonyMethod(postfix2));
                        }
                    }
                    else
                    {
                        Log.Warning("Zoology: Pawn_CarryTracker type not found in loaded assemblies; skipping TryStartCarry patches.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: Exception while trying to patch TryStartCarry: {ex}");
                }

                
                try
                {
                    var targetIngested = AccessTools.Method(typeof(Thing), "Ingested", new Type[] { typeof(Pawn), typeof(float) });
                    if (targetIngested == null)
                    {
                        targetIngested = typeof(Thing).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == "Ingested" && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(Pawn));
                    }

                    if (targetIngested != null)
                    {
                        var postfixIngested = typeof(PredationHarmonyPatches).GetMethod(nameof(Thing_Ingested_Postfix), BindingFlags.Static | BindingFlags.Public);
                        harmony.Patch(targetIngested, null, new HarmonyMethod(postfixIngested));
                    }
                    else
                    {
                        Log.Warning("Zoology: Thing.Ingested method not found; skipping ingest-time defend patch.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: Exception while trying to patch Thing.Ingested: {ex}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Harmony patches failed: {ex}");
            }
        }

        
        public static void FoodOptimality_Postfix(Pawn eater, Thing foodSource, ThingDef foodDef, float dist, bool takingToInventory, ref float __result)
        {
            if (eater == null || foodSource == null) return;

            var corpse = foodSource as Corpse;
            if (corpse == null)
            {
                var maybePawn = foodSource as Pawn;
                if (maybePawn != null && maybePawn.Dead)
                {
                    
                    corpse = maybePawn.Corpse; 
                }
            }
            if (corpse != null)
            {
                var comp = PredatorPreyPairGameComponent.Instance;
                try
                {
                    if (comp != null && eater != null && comp.IsPaired(eater, corpse))
                    {
                        __result += OwnedCorpseBonus; 
                        return;
                    }

                    bool effectivelyUnowned = true;
                    if (comp != null)
                    {
                        try
                        {
                            effectivelyUnowned = comp.IsCorpseEffectivelyUnownedFor(eater, corpse);
                        }
                        catch
                        {
                            
                            effectivelyUnowned = true;
                        }
                    }

                    if (effectivelyUnowned)
                    {
                        __result += UnpairedCorpseBonus;
                    }
                    else
                    {
                        __result += GuardedCorpsePenalty;
                    }
                    return;
                }
                catch (Exception exCorp)
                {
                    Log.Warning($"Zoology: FoodOptimality_Postfix: error handling corpse logic: {exCorp}");
                }
            }

            var livePawn = foodSource as Pawn;
            if (livePawn != null && !livePawn.Dead)
            {
                if (eater.RaceProps?.predator == true && FoodUtility.IsAcceptablePreyFor(eater, livePawn))
                {
                    __result += LiveAcceptablePreyBonus;
                }
                return;
            }

            if (!(foodSource is Pawn) && foodSource.def.IsNutritionGivingIngestible)
            {
                if (!foodSource.def.IsDrug)
                {
                    float prefBoost = (float)foodSource.def.ingestible.preferability * 4f;
                    __result += prefBoost;
                }
            }
        }

        public static void BestFoodSourceOnMap_Postfix(Pawn getter, Pawn eater, bool desperate, ref Thing __result, ref ThingDef foodDef)
        {
            try
            {
                Pawn predator = eater ?? getter;
                if (predator?.RaceProps?.predator != true || __result == null)
                {
                    return;
                }

                Corpse corpse = __result as Corpse;
                if (corpse == null && __result is Pawn deadPawn && deadPawn.Dead)
                {
                    corpse = deadPawn.Corpse;
                }

                if (corpse == null || !ShouldPreferLivePreyOverCorpse(predator, corpse))
                {
                    return;
                }

                Pawn huntTarget = TryGetVanillaHuntTarget(predator, desperate);
                if (huntTarget == null)
                {
                    return;
                }

                __result = null;
                foodDef = null;
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: BestFoodSourceOnMap_Postfix exception: {ex}");
            }
        }

        private static bool ShouldPreferLivePreyOverCorpse(Pawn predator, Corpse corpse)
        {
            if (predator == null || corpse == null)
            {
                return false;
            }

            var comp = PredatorPreyPairGameComponent.Instance;
            if (comp == null)
            {
                return false;
            }

            try
            {
                if (comp.IsPaired(predator, corpse))
                {
                    return false;
                }

                return !comp.IsCorpseEffectivelyUnownedFor(predator, corpse);
            }
            catch
            {
                return false;
            }
        }

        private static Pawn TryGetVanillaHuntTarget(Pawn predator, bool desperate)
        {
            if (BestPawnToHuntForPredatorMethod == null || predator == null)
            {
                return null;
            }

            try
            {
                return BestPawnToHuntForPredatorMethod.Invoke(null, new object[] { predator, desperate }) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        
        public static void Pawn_Kill_Postfix(Pawn __instance, object[] __args)
        {
            try
            {
                if (__instance == null) return;
                if (__args == null || __args.Length == 0) return;

                object dinfoObj = __args.Length > 0 ? __args[0] : null;
                if (dinfoObj == null) return;

                DamageInfo? dinfo = null;
                try
                {
                    if (dinfoObj is DamageInfo dd) dinfo = dd;
                    else
                    {
                        var t = dinfoObj.GetType();
                        var hasValProp = t.GetProperty("HasValue");
                        var valProp = t.GetProperty("Value");
                        if (hasValProp != null && valProp != null)
                        {
                            bool has = (bool)hasValProp.GetValue(dinfoObj, null);
                            if (has)
                            {
                                var val = valProp.GetValue(dinfoObj, null);
                                if (val is DamageInfo) dinfo = (DamageInfo)val;
                            }
                        }
                    }
                }
                catch
                {
                    dinfo = null;
                }

                if (dinfo == null) return;
                var inst = dinfo.Value.Instigator;
                if (!(inst is Pawn killer)) return;
                if (!killer.RaceProps.predator) return;

                Job kj = null;
                try { kj = killer.CurJob; } catch { kj = null; }
                bool isHuntDriver = false;
                if (kj != null && kj.def != null && kj.def.driverClass != null)
                {
                    var driver = kj.def.driverClass;
                    if (typeof(JobDriver_PredatorHunt).IsAssignableFrom(driver)) isHuntDriver = true;
                }
                if (!isHuntDriver)
                {
                    if (kj == null || kj.def != JobDefOf.AttackMelee)
                    {
                        return;
                    }
                }

                Corpse corpse = __instance.Corpse ?? PredationLookupUtility.FindSpawnedCorpseForInnerPawn(__instance);

                if (corpse == null)
                {
                    var compFallback = PredatorPreyPairGameComponent.Instance;
                    if (compFallback != null)
                    {
                        compFallback.RegisterPairFromKill(killer, __instance);
                    }
                    return;
                }

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp != null)
                {
                    comp.RegisterPairFromKill(killer, corpse);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Pawn_Kill_Postfix exception: {ex}");
            }
        }

        
        public static void PawnCarry_TryStartCarry_Postfix(object __instance, Thing item, bool __result)
        {
            try
            {
                if (!__result || item == null) return;
                var corpse = item as Corpse;
                if (corpse == null) return;

                Pawn carrier = PredationLookupUtility.TryGetCarrierPawn(__instance);
                if (carrier == null)
                {
                    carrier = PredationLookupUtility.FindPawnHoldingThing(item.thingIDNumber);
                }

                if (carrier == null) return;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp != null)
                {
                    comp.TryTriggerDefendFor(corpse, carrier);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: PawnCarry_TryStartCarry_Postfix exception: {ex}");
            }
        }

        
        public static void PawnCarry_TryStartCarry_Int_Postfix(object __instance, Thing item, int count, bool reserve, int __result)
        {
            try
            {
                if (__result <= 0 || item == null) return;
                var corpse = item as Corpse;
                if (corpse == null) return;

                Pawn carrier = PredationLookupUtility.TryGetCarrierPawn(__instance);
                if (carrier == null)
                {
                    carrier = PredationLookupUtility.FindPawnHoldingThing(item.thingIDNumber);
                }

                if (carrier == null) return;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp != null)
                {
                    comp.TryTriggerDefendFor(corpse, carrier);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: PawnCarry_TryStartCarry_Int_Postfix exception: {ex}");
            }
        }

        
        public static void Thing_Ingested_Postfix(Thing __instance, Pawn ingester, float nutritionWanted)
        {
            try
            {
                if (__instance == null || ingester == null) return;
                var corpse = __instance as Corpse;
                if (corpse == null) return;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp != null)
                {
                    comp.TryTriggerDefendFor(corpse, ingester);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: Thing_Ingested_Postfix exception: {ex}");
            }
        }
    }
}
