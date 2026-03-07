using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class PredationSettingsGate
    {
        public static bool EnablePredatorDefendCorpse()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnablePredatorDefendCorpse;
        }
    }

    internal static class ProtectPreyState
    {
        public const string ProtectPreyDefName = "Zoology_ProtectPrey";

        public static bool IsProtectPreyJob(Pawn pawn)
        {
            if (pawn == null) return false;

            var curJob = pawn.CurJob;
            if (curJob == null) return false;

            var defName = curJob.def?.defName;
            if (!string.IsNullOrEmpty(defName) && defName.Equals(ProtectPreyDefName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var driver = pawn.jobs?.curDriver;
            if (driver == null) return false;

            var driverType = driver.GetType();
            if (driverType.Name == "JobDriver_ProtectPrey") return true;
            if (driverType.FullName != null && driverType.FullName.EndsWith(".JobDriver_ProtectPrey", StringComparison.Ordinal)) return true;

            try
            {
                return typeof(JobDriver_ProtectPrey).IsAssignableFrom(driverType);
            }
            catch
            {
                return false;
            }
        }

        public static Pawn GetProtectedPawn(Pawn predator)
        {
            try
            {
                var curJob = predator?.CurJob;
                if (curJob == null) return null;
                return curJob.GetTarget(TargetIndex.A).Thing as Pawn;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class ProtectPreyMapCache
    {
        internal readonly struct Entry
        {
            public Entry(Pawn predator, Pawn protectedPawn)
            {
                Predator = predator;
                ProtectedPawn = protectedPawn;
            }

            public Pawn Predator { get; }
            public Pawn ProtectedPawn { get; }
        }

        private sealed class CacheData
        {
            public long RefreshTick;
            public readonly List<Entry> Entries = new List<Entry>(8);
        }

        private const int REFRESH_INTERVAL_TICKS = 60;
        private static readonly Dictionary<int, CacheData> byMapId = new Dictionary<int, CacheData>();

        public static List<Entry> Get(Map map, long now)
        {
            if (map == null) return null;

            int mapId = map.uniqueID;
            if (!byMapId.TryGetValue(mapId, out var data))
            {
                data = new CacheData();
                byMapId[mapId] = data;
            }

            if (data.RefreshTick + REFRESH_INTERVAL_TICKS > now)
            {
                return data.Entries;
            }

            data.RefreshTick = now;
            data.Entries.Clear();

            var pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns == null || pawns.Count == 0) return data.Entries;

            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || !p.Spawned || p.Dead || p.Destroyed) continue;
                if (!p.RaceProps.predator) continue;
                if (!ProtectPreyState.IsProtectPreyJob(p)) continue;

                var protectedPawn = ProtectPreyState.GetProtectedPawn(p);
                if (protectedPawn == null || protectedPawn.Dead || protectedPawn.Destroyed || !protectedPawn.Spawned) continue;

                data.Entries.Add(new Entry(p, protectedPawn));
            }

            return data.Entries;
        }
    }

    [HarmonyPatch(typeof(Faction), "Notify_MemberTookDamage")]
    public static class Patch_Faction_Notify_MemberTookDamage
    {
        private static readonly MethodInfo TookDamageFromPredatorMethod =
            AccessTools.Method(typeof(Faction), "TookDamageFromPredator", new Type[] { typeof(Pawn) })
            ?? AccessTools.Method(typeof(Faction), "TookDamageFromPredator");

        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

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

                if (ProtectPreyState.IsProtectPreyJob(pawn))
                {
                    try
                    {
                        if (TookDamageFromPredatorMethod != null)
                        {
                            TookDamageFromPredatorMethod.Invoke(__instance, new object[] { pawn });
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
        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        static void Postfix(Pawn predator, Faction myFaction, ref Pawn __result)
        {
            try
            {
                if (__result != null) return;
                if (predator == null) return;
                var curJob = predator.CurJob;
                if (curJob == null) return;
                if (!ProtectPreyState.IsProtectPreyJob(predator)) return;

                
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
        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        
        static void Postfix(Pawn pawn, Pawn prey, ref bool __result)
        {
            try
            {
                if (__result) return; 
                if (pawn == null) return;
                if (ProtectPreyState.IsProtectPreyJob(pawn))
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
        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        static void Postfix(Pawn pawn, ref bool __result)
        {
            try
            {
                if (__result) return; 
                if (pawn == null || !ProtectPreyState.IsProtectPreyJob(pawn)) return;

                var targetPawn = ProtectPreyState.GetProtectedPawn(pawn);
                if (targetPawn != null && targetPawn.Spawned)
                {
                    const float THREAT_RADIUS = 20f;
                    if (pawn.Position.InHorDistOf(targetPawn.Position, THREAT_RADIUS))
                    {
                        __result = true;
                    }
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
        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        static void Postfix(Faction __instance, Pawn predator, ref bool __result)
        {
            try
            {
                if (__result) return; 

                if (predator == null || __instance == null) return;
                if (!ProtectPreyState.IsProtectPreyJob(predator)) return;

                
                
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
        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        static void Postfix(Pawn predator, Faction toFaction, ref bool __result)
        {
            try
            {
                if (__result) return; 

                if (predator == null || toFaction == null) return;

                var curJob = predator.CurJob;
                if (curJob == null) return;
                if (!ProtectPreyState.IsProtectPreyJob(predator)) return;

                
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
        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        
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

                if (searcherPawn == null || !searcherPawn.Spawned || searcherPawn.Map == null || searcherPawn.Faction == null) return;

                long now = Find.TickManager?.TicksGame ?? 0L;
                var protectors = ProtectPreyMapCache.Get(searcherPawn.Map, now);
                if (protectors == null || protectors.Count == 0) return;

                const float ADD_RADIUS = 30f;
                for (int i = 0; i < protectors.Count; i++)
                {
                    var p = protectors[i].Predator;
                    var protectedPawn = protectors[i].ProtectedPawn;
                    if (p == null || protectedPawn == null) continue;
                    if (!p.Spawned || p.Dead || p.Destroyed) continue;
                    if (protectedPawn.Faction != searcherPawn.Faction) continue;
                    if (!p.Position.InHorDistOf(searcherPawn.Position, ADD_RADIUS)) continue;

                    bool already = false;
                    for (int j = 0; j < __result.Count; j++)
                    {
                        if (__result[j]?.Thing == p) { already = true; break; }
                    }
                    if (already) continue;
                    __result.Add(p as IAttackTarget);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_AttackTargetsCache_GetPotentialTargetsFor Postfix error: {ex}");
            }
        }
    }
}
