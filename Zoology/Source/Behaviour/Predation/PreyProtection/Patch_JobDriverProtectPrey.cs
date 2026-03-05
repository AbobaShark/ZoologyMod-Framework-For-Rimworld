

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Faction), "Notify_MemberTookDamage")]
    public static class Patch_Faction_Notify_MemberTookDamage
    {
        static void Postfix(Faction __instance, Pawn member, DamageInfo dinfo)
        {
            try
            {
                
                if (dinfo.Instigator == null) return;
                var pawn = dinfo.Instigator as Pawn;
                if (pawn == null) return;

                bool alreadyHandled = false;
                try
                {
                    
                    
                    if (pawn.CurJob?.def == JobDefOf.PredatorHunt)
                        alreadyHandled = true;
                }
                catch { alreadyHandled = false; }

                if (alreadyHandled) return;

                
                var driver = pawn.jobs?.curDriver;
                bool isProtectPrey = false;
                if (driver != null)
                {
                    var t = driver.GetType();
                    if (t.Name == "JobDriver_ProtectPrey" || t.FullName?.EndsWith(".JobDriver_ProtectPrey") == true
                        || typeof(JobDriver_ProtectPrey).IsAssignableFrom(t))
                    {
                        isProtectPrey = true;
                    }
                }
                if (!isProtectPrey)
                {
                    if (!string.IsNullOrEmpty(pawn.CurJob?.def?.defName) && pawn.CurJob.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                        isProtectPrey = true;
                }

                if (isProtectPrey)
                {
                    
                    try
                    {
                        MethodInfo mi = AccessTools.Method(typeof(Faction), "TookDamageFromPredator", new Type[] { typeof(Pawn) });
                        if (mi == null) 
                            mi = AccessTools.Method(typeof(Faction), "TookDamageFromPredator");

                        if (mi != null)
                        {
                            mi.Invoke(__instance, new object[] { pawn });
                        }
                        else
                        {
                            Log.Warning("[ZoologyMod] TookDamageFromPredator method not found via reflection — predatorThreats не обновлён.");
                        }
                    }
                    catch (Exception ex2)
                    {
                        Log.Error($"[ZoologyMod] Error calling TookDamageFromPredator for ProtectPrey instigator {pawn}: {ex2}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_Faction_Notify_MemberTookDamage Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(GenHostility), "GetPreyOfMyFaction", new Type[] { typeof(Pawn), typeof(Faction) })]
    public static class Patch_GenHostility_GetPreyOfMyFaction
    {
        static void Postfix(Pawn predator, Faction myFaction, ref Pawn __result)
        {
            try
            {
                if (__result != null) return;
                if (predator == null) return;
                var curJob = predator.CurJob;
                if (curJob == null) return;

                
                var driver = predator.jobs?.curDriver;
                bool isProtectPrey = false;
                if (driver != null)
                {
                    var t = driver.GetType();
                    if (t.Name == "JobDriver_ProtectPrey" || t.FullName?.EndsWith(".JobDriver_ProtectPrey") == true
                        || typeof(JobDriver_ProtectPrey).IsAssignableFrom(t))
                    {
                        
                        var endedProp = t.GetProperty("ended");
                        bool ended = false;
                        try { if (endedProp != null) ended = (bool)endedProp.GetValue(driver); } catch { ended = false; }
                        if (!ended) isProtectPrey = true;
                    }
                }

                if (!isProtectPrey)
                {
                    
                    if (!string.IsNullOrEmpty(curJob.def?.defName) && curJob.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                    {
                        isProtectPrey = true;
                    }
                }

                if (!isProtectPrey) return;

                
                try
                {
                    var t = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                    if (t != null && !t.Dead && t.Faction == myFaction)
                    {
                        __result = t;
                    }
                }
                catch { /* безопасно игнорируем */ }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_GenHostility_GetPreyOfMyFaction Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_ReactToCloseMeleeThreat), "IsHunting", new Type[] { typeof(Pawn), typeof(Pawn) })]
    public static class Patch_ReactToCloseMeleeThreat_IsHunting
    {
        
        static void Postfix(Pawn pawn, Pawn prey, ref bool __result)
        {
            try
            {
                if (__result) return; 
                if (pawn?.jobs?.curDriver == null) return;

                var driver = pawn.jobs.curDriver;
                var driverType = driver.GetType();

                
                Type protectPreyType = Type.GetType("YourModNamespace.JobDriver_ProtectPrey, YourModAssemblyName");
                
                if (protectPreyType == null)
                {
                    
                    if (driverType.Name.Equals("JobDriver_ProtectPrey", StringComparison.Ordinal)
                        || (driverType.FullName != null && driverType.FullName.EndsWith(".JobDriver_ProtectPrey", StringComparison.Ordinal)))
                    {
                        __result = true;
                        return;
                    }
                }
                else
                {
                    
                    if (protectPreyType.IsAssignableFrom(driverType))
                    {
                        __result = true;
                        return;
                    }
                }

                
                var defName = pawn.CurJob?.def?.defName;
                if (!string.IsNullOrEmpty(defName) && defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                {
                    __result = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_ReactToCloseMeleeThreat_IsHunting Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PawnUtility), "IsFighting", new Type[] { typeof(Pawn) })]
    public static class Patch_PawnUtility_IsFighting
    {
        static void Postfix(Pawn pawn, ref bool __result)
        {
            try
            {
                if (__result) return; 
                if (pawn == null || pawn.CurJob == null) return;

                var curJob = pawn.CurJob;
                var driver = pawn.jobs?.curDriver;

                
                bool DriverIsProtectPrey(Type driverType)
                {
                    if (driverType == null) return false;
                    if (driverType.Name == "JobDriver_ProtectPrey") return true;
                    if (driverType.FullName != null && driverType.FullName.EndsWith(".JobDriver_ProtectPrey")) return true;
                    try
                    {
                        
                        var protectType = typeof(JobDriver_ProtectPrey);
                        if (protectType.IsAssignableFrom(driverType)) return true;
                    }
                    catch { }
                    return false;
                }

                
                if (driver != null)
                {
                    var dType = driver.GetType();
                    if (DriverIsProtectPrey(dType))
                    {
                        try
                        {
                            
                            LocalTargetInfo targInfo = curJob.GetTarget(TargetIndex.A);
                            var targetPawn = targInfo.Thing as Pawn;
                            if (targetPawn != null && targetPawn.Spawned)
                            {
                                
                                
                                const float THREAT_RADIUS = 20f;
                                if (pawn.Position.InHorDistOf(targetPawn.Position, THREAT_RADIUS))
                                {
                                    __result = true;
                                }
                            }
                        }
                        catch
                        {
                            
                        }
                        return;
                    }
                }

                
                var defName = curJob.def?.defName;
                if (!string.IsNullOrEmpty(defName) && defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        LocalTargetInfo targInfo = curJob.GetTarget(TargetIndex.A);
                        var targetPawn = targInfo.Thing as Pawn;
                        if (targetPawn != null && targetPawn.Spawned)
                        {
                            const float THREAT_RADIUS = 20f;
                            if (pawn.Position.InHorDistOf(targetPawn.Position, THREAT_RADIUS))
                            {
                                __result = true;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_PawnUtility_IsFighting Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Faction), "HasPredatorRecentlyAttackedAnyone", new Type[] { typeof(Pawn) })]
    public static class Patch_Faction_HasPredatorRecentlyAttackedAnyone
    {
        static void Postfix(Faction __instance, Pawn predator, ref bool __result)
        {
            try
            {
                if (__result) return; 

                if (predator == null || __instance == null) return;

                
                var driver = predator.jobs?.curDriver;
                bool isProtectPrey = false;
                if (driver != null)
                {
                    var t = driver.GetType();
                    if (t.Name == "JobDriver_ProtectPrey" || (t.FullName != null && t.FullName.EndsWith(".JobDriver_ProtectPrey")))
                        isProtectPrey = true;
                }

                
                if (!isProtectPrey)
                {
                    try
                    {
                        var cur = predator.CurJob;
                        if (cur != null && !string.IsNullOrEmpty(cur.def?.defName)
                            && cur.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                        {
                            isProtectPrey = true;
                        }
                    }
                    catch { /* ignore */ }
                }

                if (!isProtectPrey) return;

                
                
                try
                {
                    var curJob = predator.CurJob;
                    if (curJob == null) return;

                    var targ = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                    if (targ != null && !targ.Dead && targ.Faction == __instance)
                    {
                        __result = true;
                    }
                }
                catch
                {
                    
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_Faction_HasPredatorRecentlyAttackedAnyone Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(GenHostility), "IsPredatorHostileTo", new Type[] { typeof(Pawn), typeof(Faction) })]
    public static class Patch_GenHostility_IsPredatorHostileTo
    {
        static void Postfix(Pawn predator, Faction toFaction, ref bool __result)
        {
            try
            {
                if (__result) return; 

                if (predator == null || toFaction == null) return;

                var curJob = predator.CurJob;
                if (curJob == null) return;

                
                var driver = predator.jobs?.curDriver;
                bool isProtectPrey = false;
                if (driver != null)
                {
                    var t = driver.GetType();
                    if (t.Name == "JobDriver_ProtectPrey" || (t.FullName != null && t.FullName.EndsWith(".JobDriver_ProtectPrey")))
                        isProtectPrey = true;
                }
                if (!isProtectPrey)
                {
                    if (!string.IsNullOrEmpty(curJob.def?.defName) && curJob.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                        isProtectPrey = true;
                }

                if (!isProtectPrey) return;

                
                try
                {
                    var targ = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                    if (targ != null && !targ.Dead && targ.Faction == toFaction)
                    {
                        __result = true;
                    }
                }
                catch { /* игнорируем ошибки чтения таргета */ }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_GenHostility_IsPredatorHostileTo Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(AttackTargetsCache), "GetPotentialTargetsFor", new Type[] { typeof(IAttackTargetSearcher) })]
    public static class Patch_AttackTargetsCache_GetPotentialTargetsFor
    {
        
        static void Postfix(AttackTargetsCache __instance, IAttackTargetSearcher th, ref List<IAttackTarget> __result)
        {
            try
            {
                if (th == null) return;
                if (__result == null) __result = new List<IAttackTarget>();

                
                Pawn searcherPawn = null;
                try
                {
                    searcherPawn = th as Pawn;
                    if (searcherPawn == null)
                    {
                        var thingProp = th.GetType().GetProperty("Thing");
                        if (thingProp != null)
                        {
                            var thingVal = thingProp.GetValue(th, null) as Thing;
                            searcherPawn = thingVal as Pawn;
                        }
                    }
                }
                catch { /* ignore */ }

                if (searcherPawn == null || searcherPawn.Map == null) return;

                const float ADD_RADIUS = 30f;
                var pawns = searcherPawn.Map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p == null) continue;
                    if (!p.RaceProps.predator) continue;
                    if (!p.Spawned) continue;
                    if (!p.Position.InHorDistOf(searcherPawn.Position, ADD_RADIUS)) continue;

                    bool already = false;
                    for (int j = 0; j < __result.Count; j++)
                    {
                        if (__result[j]?.Thing == p) { already = true; break; }
                    }
                    if (already) continue;

                    bool isProtectPrey = false;
                    try
                    {
                        var driver = p.jobs?.curDriver;
                        if (driver != null)
                        {
                            var dt = driver.GetType();
                            if (dt.Name == "JobDriver_ProtectPrey" || (dt.FullName != null && dt.FullName.EndsWith(".JobDriver_ProtectPrey")))
                                isProtectPrey = true;
                            else
                            {
                                try { if (typeof(JobDriver_ProtectPrey).IsAssignableFrom(dt)) isProtectPrey = true; } catch { }
                            }
                        }

                        if (!isProtectPrey)
                        {
                            var cur = p.CurJob;
                            if (cur != null && !string.IsNullOrEmpty(cur.def?.defName)
                                && cur.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                            {
                                isProtectPrey = true;
                            }
                        }
                    }
                    catch { }

                    if (!isProtectPrey) continue;

                    Pawn protectedPawn = null;
                    try
                    {
                        var curJob = p.CurJob;
                        if (curJob != null)
                        {
                            var targ = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                            if (targ != null && !targ.Dead) protectedPawn = targ;
                        }
                    }
                    catch { }

                    if (protectedPawn == null) continue;

                    if (searcherPawn.Faction != null && protectedPawn.Faction == searcherPawn.Faction)
                    {
                        __result.Add(p as IAttackTarget);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_AttackTargetsCache_GetPotentialTargetsFor Postfix error: {ex}");
            }
        }
    }
}
