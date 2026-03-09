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
        private static readonly Dictionary<Type, bool> protectDriverTypeCache = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, PropertyInfo> thingPropertyBySearcherType = new Dictionary<Type, PropertyInfo>();
        private readonly struct ProtectedPawnCacheEntry
        {
            public ProtectedPawnCacheEntry(Job job, Pawn protectedPawn, int mapId)
            {
                Job = job;
                ProtectedPawn = protectedPawn;
                MapId = mapId;
            }

            public Job Job { get; }
            public Pawn ProtectedPawn { get; }
            public int MapId { get; }
        }

        private static readonly Dictionary<int, ProtectedPawnCacheEntry> protectedPawnCacheByPredatorId =
            new Dictionary<int, ProtectedPawnCacheEntry>(64);
        private static readonly List<int> protectedPawnCacheCleanupBuffer = new List<int>(16);
        private const int ProtectedPawnCacheCleanupIntervalTicks = 600;
        private static int lastProtectedPawnCacheCleanupTick = -ProtectedPawnCacheCleanupIntervalTicks;

        private static bool IsProtectPreyDriverType(Type driverType)
        {
            if (driverType == null)
            {
                return false;
            }

            if (protectDriverTypeCache.TryGetValue(driverType, out bool cached))
            {
                return cached;
            }

            bool result = false;
            try
            {
                result = driverType == typeof(JobDriver_ProtectPrey)
                    || driverType.Name == "JobDriver_ProtectPrey"
                    || (driverType.FullName != null && driverType.FullName.EndsWith(".JobDriver_ProtectPrey", StringComparison.Ordinal))
                    || typeof(JobDriver_ProtectPrey).IsAssignableFrom(driverType);
            }
            catch
            {
                result = false;
            }

            protectDriverTypeCache[driverType] = result;
            return result;
        }

        internal static bool IsProtectPreyJob(Job curJob, JobDriver curDriver)
        {
            if (curJob == null)
            {
                return false;
            }

            var defName = curJob.def?.defName;
            if (!string.IsNullOrEmpty(defName) && defName.Equals(ProtectPreyDefName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsProtectPreyDriverType(curDriver?.GetType());
        }

        public static bool IsProtectPreyJob(Pawn pawn)
        {
            if (pawn == null) return false;

            var curJob = pawn.CurJob;
            var driver = pawn.jobs?.curDriver;
            return IsProtectPreyJob(curJob, driver);
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

        public static bool TryGetProtectedPawn(Pawn predator, out Pawn protectedPawn)
        {
            protectedPawn = null;
            if (predator == null)
            {
                return false;
            }

            var curJob = predator.CurJob;
            if (!IsProtectPreyJob(curJob, predator.jobs?.curDriver))
            {
                protectedPawnCacheByPredatorId.Remove(predator.thingIDNumber);
                return false;
            }

            int predatorId = predator.thingIDNumber;
            if (curJob != null
                && protectedPawnCacheByPredatorId.TryGetValue(predatorId, out ProtectedPawnCacheEntry cached)
                && ReferenceEquals(cached.Job, curJob)
                && cached.MapId == (predator.Map?.uniqueID ?? -1)
                && IsValidProtectedPawn(predator, cached.ProtectedPawn))
            {
                protectedPawn = cached.ProtectedPawn;
                return true;
            }

            try
            {
                protectedPawn = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                if (!IsValidProtectedPawn(predator, protectedPawn))
                {
                    protectedPawnCacheByPredatorId.Remove(predatorId);
                    return false;
                }

                protectedPawnCacheByPredatorId[predatorId] = new ProtectedPawnCacheEntry(
                    curJob,
                    protectedPawn,
                    predator.Map?.uniqueID ?? -1);
                CleanupProtectedPawnCacheIfNeeded(Find.TickManager?.TicksGame ?? 0);
                return true;
            }
            catch
            {
                protectedPawnCacheByPredatorId.Remove(predatorId);
                protectedPawn = null;
                return false;
            }
        }

        private static bool IsValidProtectedPawn(Pawn predator, Pawn protectedPawn)
        {
            return predator != null
                && protectedPawn != null
                && !protectedPawn.Dead
                && !protectedPawn.Destroyed
                && protectedPawn.Spawned
                && predator.Map != null
                && protectedPawn.Map == predator.Map;
        }

        public static bool IsActivelyProtectingNearbyPrey(Pawn predator, float threatRadius)
        {
            if (!TryGetProtectedPawn(predator, out Pawn protectedPawn) || !protectedPawn.Spawned)
            {
                return false;
            }

            return predator.Position.InHorDistOf(protectedPawn.Position, threatRadius);
        }

        private static void CleanupProtectedPawnCacheIfNeeded(int currentTick)
        {
            if (currentTick - lastProtectedPawnCacheCleanupTick < ProtectedPawnCacheCleanupIntervalTicks)
            {
                return;
            }

            lastProtectedPawnCacheCleanupTick = currentTick;
            protectedPawnCacheCleanupBuffer.Clear();
            foreach (KeyValuePair<int, ProtectedPawnCacheEntry> entry in protectedPawnCacheByPredatorId)
            {
                ProtectedPawnCacheEntry cached = entry.Value;
                if (cached.Job == null || cached.ProtectedPawn == null || cached.ProtectedPawn.Dead || cached.ProtectedPawn.Destroyed)
                {
                    protectedPawnCacheCleanupBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < protectedPawnCacheCleanupBuffer.Count; i++)
            {
                protectedPawnCacheByPredatorId.Remove(protectedPawnCacheCleanupBuffer[i]);
            }

            protectedPawnCacheCleanupBuffer.Clear();
        }

        public static Pawn TryGetSearcherPawn(IAttackTargetSearcher searcher)
        {
            if (searcher == null)
            {
                return null;
            }

            if (searcher is Pawn pawn)
            {
                return pawn;
            }

            try
            {
                var searcherType = searcher.GetType();
                if (!thingPropertyBySearcherType.TryGetValue(searcherType, out var thingProperty))
                {
                    thingProperty = searcherType.GetProperty("Thing");
                    thingPropertyBySearcherType[searcherType] = thingProperty;
                }

                return thingProperty?.GetValue(searcher, null) as Pawn;
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
                if (!ProtectPreyState.TryGetProtectedPawn(p, out var protectedPawn)) continue;
                if (protectedPawn.Dead || protectedPawn.Destroyed || !protectedPawn.Spawned) continue;

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
                if (!ProtectPreyState.TryGetProtectedPawn(predator, out var protectedPawn)) return;
                if (!protectedPawn.Dead && protectedPawn.Faction == myFaction)
                {
                    __result = protectedPawn;
                }
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
                if (!ProtectPreyState.TryGetProtectedPawn(predator, out var targetPawn)) return;
                if (!targetPawn.Dead && targetPawn.Faction == __instance)
                {
                    __result = true;
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
                if (!ProtectPreyState.TryGetProtectedPawn(predator, out var targetPawn)) return;
                if (!targetPawn.Dead && targetPawn.Faction == toFaction)
                {
                    __result = true;
                }
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

                
                Pawn searcherPawn = ProtectPreyState.TryGetSearcherPawn(th);
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
