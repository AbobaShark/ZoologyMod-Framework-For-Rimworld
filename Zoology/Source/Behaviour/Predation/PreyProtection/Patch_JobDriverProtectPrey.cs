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
            public ProtectedPawnCacheEntry(Pawn predator, Job job, Pawn protectedPawn, int mapId, int validUntilTick)
            {
                Predator = predator;
                Job = job;
                ProtectedPawn = protectedPawn;
                MapId = mapId;
                ValidUntilTick = validUntilTick;
            }

            public Pawn Predator { get; }
            public Job Job { get; }
            public Pawn ProtectedPawn { get; }
            public int MapId { get; }
            public int ValidUntilTick { get; }
        }

        private static readonly Dictionary<int, ProtectedPawnCacheEntry> protectedPawnCacheByPredatorId =
            new Dictionary<int, ProtectedPawnCacheEntry>(64);
        private static readonly Dictionary<int, int> activeProtectorsByMapId = new Dictionary<int, int>(4);
        private static int activeProtectorsTotal;
        private static readonly Dictionary<int, BackgroundResyncState> backgroundResyncByMapId = new Dictionary<int, BackgroundResyncState>(4);
        private static readonly List<int> protectedPawnCacheCleanupBuffer = new List<int>(16);
        private const int ProtectedPawnCacheCleanupIntervalTicks = ZoologyTickLimiter.PreyProtection.ProtectedPawnCacheCleanupIntervalTicks;
        private const int ProtectedPawnCacheDurationTicks = ZoologyTickLimiter.PreyProtection.ProtectedPawnCacheDurationTicks;
        private const int ProtectedPawnCacheRefreshMarginTicks = ZoologyTickLimiter.PreyProtection.ProtectedPawnCacheRefreshMarginTicks;
        private const int BackgroundResyncScanMin = 4;
        private const int BackgroundResyncScanMax = 8;
        private static int lastProtectedPawnCacheCleanupTick = -ProtectedPawnCacheCleanupIntervalTicks;
        private sealed class BackgroundResyncState
        {
            public int NextIndex;
            public long LastTick = -1;
        }

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

            Job curJob = predator.CurJob;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int mapId = predator.Map?.uniqueID ?? -1;
            int predatorId = predator.thingIDNumber;

            if (currentTick > 0
                && protectedPawnCacheByPredatorId.TryGetValue(predatorId, out ProtectedPawnCacheEntry cached)
                && ReferenceEquals(cached.Job, curJob)
                && cached.MapId == mapId
                && cached.ValidUntilTick >= currentTick)
            {
                if (IsValidProtectedPawn(predator, cached.ProtectedPawn))
                {
                    protectedPawn = cached.ProtectedPawn;
                    if (cached.ValidUntilTick - currentTick <= ProtectedPawnCacheRefreshMarginTicks)
                    {
                        protectedPawnCacheByPredatorId[predatorId] = new ProtectedPawnCacheEntry(
                            predator,
                            cached.Job,
                            cached.ProtectedPawn,
                            cached.MapId,
                            currentTick + ProtectedPawnCacheDurationTicks);
                    }
                    return true;
                }

                protectedPawnCacheByPredatorId.Remove(predatorId);
            }

            return false;
        }

        internal static bool TryGetProtectedPawnCachedNoTick(Pawn predator, out Pawn protectedPawn)
        {
            protectedPawn = null;
            if (predator == null)
            {
                return false;
            }

            int predatorId = predator.thingIDNumber;
            if (!protectedPawnCacheByPredatorId.TryGetValue(predatorId, out ProtectedPawnCacheEntry cached))
            {
                return false;
            }

            if (!ReferenceEquals(cached.Job, predator.CurJob))
            {
                RemoveProtectedPawnCacheEntry(predatorId);
                return false;
            }

            if (!IsValidProtectedPawn(predator, cached.ProtectedPawn))
            {
                RemoveProtectedPawnCacheEntry(predatorId);
                return false;
            }

            protectedPawn = cached.ProtectedPawn;
            return true;
        }

        internal static bool TryGetProtectedPawnFast(Pawn predator, out Pawn protectedPawn)
        {
            if (TryGetProtectedPawnCachedNoTick(predator, out protectedPawn))
            {
                return protectedPawn != null;
            }

            protectedPawn = null;
            if (predator == null)
            {
                return false;
            }

            Job curJob = predator.CurJob;
            if (!IsProtectPreyJobFast(curJob, predator.jobs?.curDriver))
            {
                return false;
            }

            Pawn candidate = null;
            try
            {
                candidate = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
            }
            catch
            {
                return false;
            }

            if (!IsValidProtectedPawn(predator, candidate))
            {
                return false;
            }

            NotifyProtectPreyJobStarted(predator, candidate, curJob);
            protectedPawn = candidate;
            return true;
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
                if (cached.ValidUntilTick < currentTick)
                {
                    protectedPawnCacheCleanupBuffer.Add(entry.Key);
                    continue;
                }

                Pawn predator = cached.Predator;
                Pawn pawn = cached.ProtectedPawn;
                if (predator == null || pawn == null || predator.Dead || predator.Destroyed || pawn.Dead || pawn.Destroyed)
                {
                    protectedPawnCacheCleanupBuffer.Add(entry.Key);
                    continue;
                }

                if (!ReferenceEquals(predator.CurJob, cached.Job))
                {
                    protectedPawnCacheCleanupBuffer.Add(entry.Key);
                }
            }

            for (int i = 0; i < protectedPawnCacheCleanupBuffer.Count; i++)
            {
                RemoveProtectedPawnCacheEntry(protectedPawnCacheCleanupBuffer[i]);
            }

            protectedPawnCacheCleanupBuffer.Clear();
        }

        private static void StoreProtectedPawnCache(
            Pawn predator,
            Job job,
            Pawn protectedPawn,
            int mapId,
            int currentTick)
        {
            if (predator == null || protectedPawn == null || currentTick <= 0)
            {
                return;
            }

            int predatorId = predator.thingIDNumber;
            if (protectedPawnCacheByPredatorId.TryGetValue(predatorId, out ProtectedPawnCacheEntry existing))
            {
                if (existing.MapId != mapId)
                {
                    DecrementMapCount(existing.MapId);
                    IncrementMapCount(mapId);
                }
            }
            else
            {
                IncrementMapCount(mapId);
            }

            protectedPawnCacheByPredatorId[predatorId] = new ProtectedPawnCacheEntry(
                predator,
                job,
                protectedPawn,
                mapId,
                currentTick + ProtectedPawnCacheDurationTicks);
            CleanupProtectedPawnCacheIfNeeded(currentTick);
        }

        private static void RemoveProtectedPawnCacheEntry(int predatorId)
        {
            if (protectedPawnCacheByPredatorId.TryGetValue(predatorId, out ProtectedPawnCacheEntry existing))
            {
                protectedPawnCacheByPredatorId.Remove(predatorId);
                DecrementMapCount(existing.MapId);
            }
        }

        private static void IncrementMapCount(int mapId)
        {
            if (mapId < 0)
            {
                return;
            }

            activeProtectorsTotal++;
            if (activeProtectorsByMapId.TryGetValue(mapId, out int count))
            {
                activeProtectorsByMapId[mapId] = count + 1;
            }
            else
            {
                activeProtectorsByMapId[mapId] = 1;
            }
        }

        private static void DecrementMapCount(int mapId)
        {
            if (mapId < 0)
            {
                return;
            }

            if (!activeProtectorsByMapId.TryGetValue(mapId, out int count))
            {
                return;
            }

            if (activeProtectorsTotal > 0)
            {
                activeProtectorsTotal--;
            }
            count--;
            if (count <= 0)
            {
                activeProtectorsByMapId.Remove(mapId);
            }
            else
            {
                activeProtectorsByMapId[mapId] = count;
            }
        }

        internal static bool HasActiveProtectorsForMap(Map map)
        {
            if (map == null)
            {
                return false;
            }

            return activeProtectorsByMapId.TryGetValue(map.uniqueID, out int count) && count > 0;
        }

        internal static bool HasAnyActiveProtectors => activeProtectorsTotal > 0;

        internal static void NotifyProtectPreyJobStarted(Pawn predator, Pawn protectedPawn, Job job)
        {
            if (predator == null || protectedPawn == null || job == null)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int mapId = predator.Map?.uniqueID ?? -1;
            StoreProtectedPawnCache(predator, job, protectedPawn, mapId, currentTick);
        }

        internal static void NotifyProtectPreyJobEnded(Pawn predator, Job job)
        {
            if (predator == null)
            {
                return;
            }

            int predatorId = predator.thingIDNumber;
            if (protectedPawnCacheByPredatorId.TryGetValue(predatorId, out ProtectedPawnCacheEntry cached))
            {
                if (job == null || ReferenceEquals(cached.Job, job))
                {
                    RemoveProtectedPawnCacheEntry(predatorId);
                }
            }
        }

        internal static bool TryFillActiveProtectorsForMap(Map map, List<ProtectPreyMapCache.Entry> entries)
        {
            if (map == null || entries == null)
            {
                return false;
            }

            int mapId = map.uniqueID;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            bool hadMapEntry = false;
            List<int> staleKeys = null;

            foreach (KeyValuePair<int, ProtectedPawnCacheEntry> entry in protectedPawnCacheByPredatorId)
            {
                ProtectedPawnCacheEntry cached = entry.Value;
                if (cached.MapId != mapId)
                {
                    continue;
                }

                hadMapEntry = true;
                if (currentTick > 0 && cached.ValidUntilTick < currentTick)
                {
                    if (staleKeys == null) staleKeys = new List<int>(8);
                    staleKeys.Add(entry.Key);
                    continue;
                }

                Pawn predator = cached.Predator;
                Pawn protectedPawn = cached.ProtectedPawn;
                if (!IsValidProtectedPawn(predator, protectedPawn))
                {
                    if (staleKeys == null) staleKeys = new List<int>(8);
                    staleKeys.Add(entry.Key);
                    continue;
                }

                entries.Add(new ProtectPreyMapCache.Entry(predator, protectedPawn));
            }

            if (staleKeys != null)
            {
                for (int i = 0; i < staleKeys.Count; i++)
                {
                    RemoveProtectedPawnCacheEntry(staleKeys[i]);
                }
            }

            return hadMapEntry;
        }

        internal static void TryBackgroundResync(Map map, long now, List<ProtectPreyMapCache.Entry> entries)
        {
            if (map == null || entries == null || now <= 0)
            {
                return;
            }

            if (HasActiveProtectorsForMap(map))
            {
                return;
            }

            int mapId = map.uniqueID;
            if (!backgroundResyncByMapId.TryGetValue(mapId, out BackgroundResyncState state))
            {
                state = new BackgroundResyncState();
                backgroundResyncByMapId[mapId] = state;
            }

            if (state.LastTick == now)
            {
                return;
            }

            state.LastTick = now;
            var pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns == null || pawns.Count == 0)
            {
                state.NextIndex = 0;
                return;
            }

            int budget = pawns.Count >= 200 ? BackgroundResyncScanMax : BackgroundResyncScanMin;
            int count = pawns.Count;
            int index = state.NextIndex;
            int scanned = 0;
            while (scanned < budget)
            {
                if (index >= count)
                {
                    index = 0;
                }

                Pawn p = pawns[index];
                scanned++;
                index++;

                if (p == null || !p.Spawned || p.Dead || p.Destroyed)
                {
                    continue;
                }

                Job curJob = p.CurJob;
                if (!IsProtectPreyJobFast(curJob, p.jobs?.curDriver))
                {
                    continue;
                }

                Pawn protectedPawn = null;
                try
                {
                    protectedPawn = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                }
                catch
                {
                    continue;
                }

                if (!IsValidProtectedPawn(p, protectedPawn))
                {
                    continue;
                }

                NotifyProtectPreyJobStarted(p, protectedPawn, curJob);
                entries.Add(new ProtectPreyMapCache.Entry(p, protectedPawn));
                break;
            }

            state.NextIndex = index;
        }

        private static bool IsProtectPreyJobFast(Job curJob, JobDriver curDriver)
        {
            if (curJob == null)
            {
                return false;
            }

            var defName = curJob.def?.defName;
            if (!string.IsNullOrEmpty(defName)
                && defName.Equals(ProtectPreyDefName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (curDriver is JobDriver_ProtectPrey)
            {
                return true;
            }

            Type driverClass = curJob.def?.driverClass;
            return driverClass != null && typeof(JobDriver_ProtectPrey).IsAssignableFrom(driverClass);
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

        private const int REFRESH_INTERVAL_TICKS = ZoologyTickLimiter.PreyProtection.ProtectPreyMapRefreshIntervalTicks;
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

            bool hadMapCache = ProtectPreyState.TryFillActiveProtectorsForMap(map, data.Entries);
            if (data.Entries.Count == 0 && !hadMapCache)
            {
                ProtectPreyState.TryBackgroundResync(map, now, data.Entries);
            }
            return data.Entries;
        }
    }

    [HarmonyPatch(typeof(Faction), "Notify_MemberTookDamage")]
    public static class Patch_Faction_Notify_MemberTookDamage
    {
        private static readonly Action<Faction, Pawn> TookDamageFromPredatorAction = CreateTookDamageFromPredatorAction();

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
                        if (TookDamageFromPredatorAction != null)
                        {
                            TookDamageFromPredatorAction(__instance, pawn);
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

        private static Action<Faction, Pawn> CreateTookDamageFromPredatorAction()
        {
            try
            {
                var method = AccessTools.Method(typeof(Faction), "TookDamageFromPredator", new Type[] { typeof(Pawn) })
                    ?? AccessTools.Method(typeof(Faction), "TookDamageFromPredator");
                if (method == null)
                {
                    return null;
                }

                return (Action<Faction, Pawn>)Delegate.CreateDelegate(typeof(Action<Faction, Pawn>), method);
            }
            catch
            {
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(GenHostility), "GetPreyOfMyFaction", new Type[] { typeof(Pawn), typeof(Faction) })]
    public static class Patch_GenHostility_GetPreyOfMyFaction
    {
        private static int lastHasAnyTick = int.MinValue;
        private static bool lastHasAnyValue;

        private static bool HasAnyActiveProtectorsFast(int currentTick)
        {
            if (currentTick <= 0)
            {
                return ProtectPreyState.HasAnyActiveProtectors;
            }

            if (lastHasAnyTick != currentTick)
            {
                lastHasAnyTick = currentTick;
                lastHasAnyValue = ProtectPreyState.HasAnyActiveProtectors;
            }

            return lastHasAnyValue;
        }

        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        static void Postfix(Pawn predator, Faction myFaction, ref Pawn __result)
        {
            try
            {
                if (__result != null) return;
                if (predator == null || myFaction == null) return;
                if (predator.Dead || predator.Destroyed || !predator.Spawned) return;

                Map map = predator.Map;
                if (map == null) return;

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!HasAnyActiveProtectorsFast(currentTick)) return;
                if (!ProtectPreyState.HasActiveProtectorsForMap(map)) return;

                if (!ProtectPreyState.TryGetProtectedPawnCachedNoTick(predator, out Pawn protectedPawn))
                {
                    return;
                }

                if (protectedPawn != null && protectedPawn.Faction == myFaction)
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
        private static int lastHasAnyTick = int.MinValue;
        private static bool lastHasAnyValue;

        private static bool HasAnyActiveProtectorsFast(int currentTick)
        {
            if (currentTick <= 0)
            {
                return ProtectPreyState.HasAnyActiveProtectors;
            }

            if (lastHasAnyTick != currentTick)
            {
                lastHasAnyTick = currentTick;
                lastHasAnyValue = ProtectPreyState.HasAnyActiveProtectors;
            }

            return lastHasAnyValue;
        }

        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        static void Postfix(Faction __instance, Pawn predator, ref bool __result)
        {
            try
            {
                if (__result) return; 

                if (predator == null || __instance == null) return;
                if (predator.Dead || predator.Destroyed || !predator.Spawned) return;

                Map map = predator.Map;
                if (map == null) return;

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!HasAnyActiveProtectorsFast(currentTick)) return;
                if (!ProtectPreyState.HasActiveProtectorsForMap(map)) return;

                if (!ProtectPreyState.TryGetProtectedPawnCachedNoTick(predator, out Pawn protectedPawn))
                {
                    return;
                }

                if (protectedPawn != null && protectedPawn.Faction == __instance)
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
        private static int lastHasAnyTick = int.MinValue;
        private static bool lastHasAnyValue;

        private static bool HasAnyActiveProtectorsFast(int currentTick)
        {
            if (currentTick <= 0)
            {
                return ProtectPreyState.HasAnyActiveProtectors;
            }

            if (lastHasAnyTick != currentTick)
            {
                lastHasAnyTick = currentTick;
                lastHasAnyValue = ProtectPreyState.HasAnyActiveProtectors;
            }

            return lastHasAnyValue;
        }

        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        static void Postfix(Pawn predator, Faction toFaction, ref bool __result)
        {
            try
            {
                if (__result) return; 

                if (predator == null || toFaction == null) return;
                if (predator.Dead || predator.Destroyed || !predator.Spawned) return;

                Map map = predator.Map;
                if (map == null) return;

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!HasAnyActiveProtectorsFast(currentTick)) return;
                if (!ProtectPreyState.HasActiveProtectorsForMap(map)) return;
                if (!ProtectPreyState.TryGetProtectedPawnCachedNoTick(predator, out var targetPawn)) return;
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
        private static int lastHasAnyTick = int.MinValue;
        private static bool lastHasAnyValue;

        private static bool HasAnyActiveProtectorsFast(int currentTick)
        {
            if (currentTick <= 0)
            {
                return ProtectPreyState.HasAnyActiveProtectors;
            }

            if (lastHasAnyTick != currentTick)
            {
                lastHasAnyTick = currentTick;
                lastHasAnyValue = ProtectPreyState.HasAnyActiveProtectors;
            }

            return lastHasAnyValue;
        }

        public static bool Prepare() => PredationSettingsGate.EnablePredatorDefendCorpse();

        
        static void Postfix(AttackTargetsCache __instance, IAttackTargetSearcher th, ref List<IAttackTarget> __result)
        {
            try
            {
                if (th == null) return;
                if (__result == null) __result = new List<IAttackTarget>();
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!HasAnyActiveProtectorsFast(currentTick)) return;

                
                Pawn searcherPawn = ProtectPreyState.TryGetSearcherPawn(th);
                if (searcherPawn == null || !searcherPawn.Spawned || searcherPawn.Map == null || searcherPawn.Faction == null) return;
                if (!ProtectPreyState.HasActiveProtectorsForMap(searcherPawn.Map)) return;

                long now = currentTick;
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
