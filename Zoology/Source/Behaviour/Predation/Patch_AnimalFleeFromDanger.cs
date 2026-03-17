using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class PawnThreatUtility
    {
        private static readonly Func<RaceProperties, bool> RacePropsIsMechanoidGetter = CreateRacePropsIsMechanoidGetter();

        private static Func<RaceProperties, bool> CreateRacePropsIsMechanoidGetter()
        {
            try
            {
                var getter = AccessTools.PropertyGetter(typeof(RaceProperties), "IsMechanoid");
                if (getter == null)
                {
                    return null;
                }

                return (Func<RaceProperties, bool>)Delegate.CreateDelegate(typeof(Func<RaceProperties, bool>), getter);
            }
            catch
            {
                return null;
            }
        }

        public static bool IsHumanlikeOrMechanoid(Pawn pawn)
        {
            if (pawn?.RaceProps == null)
            {
                return false;
            }

            if (pawn.RaceProps.Humanlike)
            {
                return true;
            }

            if (RacePropsIsMechanoidGetter == null)
            {
                return false;
            }

            try
            {
                return RacePropsIsMechanoidGetter(pawn.RaceProps);
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_AnimalFleeFromPredators
    {
        private readonly struct NoThreatScanCacheEntry
        {
            public NoThreatScanCacheEntry(int nextScanTick, int mapId, bool predatorsEnabled, bool humansEnabled, bool carriersEnabled, bool smallPetRaidersEnabled)
            {
                NextScanTick = nextScanTick;
                MapId = mapId;
                PredatorsEnabled = predatorsEnabled;
                HumansEnabled = humansEnabled;
                CarriersEnabled = carriersEnabled;
                SmallPetRaidersEnabled = smallPetRaidersEnabled;
            }

            public int NextScanTick { get; }
            public int MapId { get; }
            public bool PredatorsEnabled { get; }
            public bool HumansEnabled { get; }
            public bool CarriersEnabled { get; }
            public bool SmallPetRaidersEnabled { get; }
        }

        private readonly struct ThreatVisibilityCacheEntry
        {
            public ThreatVisibilityCacheEntry(bool isVisible, int tick)
            {
                IsVisible = isVisible;
                Tick = tick;
            }

            public bool IsVisible { get; }
            public int Tick { get; }
        }

        private readonly struct NearbyThreatBucketCacheKey : IEquatable<NearbyThreatBucketCacheKey>
        {
            public NearbyThreatBucketCacheKey(
                int mapId,
                int mapRefreshTick,
                int bucketX,
                int bucketZ,
                int flagsMask)
            {
                MapId = mapId;
                MapRefreshTick = mapRefreshTick;
                BucketX = bucketX;
                BucketZ = bucketZ;
                FlagsMask = flagsMask;
            }

            public int MapId { get; }
            public int MapRefreshTick { get; }
            public int BucketX { get; }
            public int BucketZ { get; }
            public int FlagsMask { get; }
            public bool Equals(NearbyThreatBucketCacheKey other)
            {
                return MapId == other.MapId
                    && MapRefreshTick == other.MapRefreshTick
                    && BucketX == other.BucketX
                    && BucketZ == other.BucketZ
                    && FlagsMask == other.FlagsMask;
            }

            public override bool Equals(object obj)
            {
                return obj is NearbyThreatBucketCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = MapId;
                    hash = (hash * 397) ^ MapRefreshTick;
                    hash = (hash * 397) ^ BucketX;
                    hash = (hash * 397) ^ BucketZ;
                    hash = (hash * 397) ^ FlagsMask;
                    return hash;
                }
            }
        }

        private readonly struct NearbyThreatBucketsCacheEntry
        {
            public NearbyThreatBucketsCacheEntry(bool hasNearbyThreatBuckets, int tick)
            {
                HasNearbyThreatBuckets = hasNearbyThreatBuckets;
                Tick = tick;
            }

            public bool HasNearbyThreatBuckets { get; }
            public int Tick { get; }
        }

        private readonly struct NearestPredatorThreatCacheEntry
        {
            public NearestPredatorThreatCacheEntry(
                int mapId,
                int mapRefreshTick,
                int bucketX,
                int bucketZ,
                bool allowNonHostilePredators,
                Pawn threat,
                bool hasThreat,
                int tick)
            {
                MapId = mapId;
                MapRefreshTick = mapRefreshTick;
                BucketX = bucketX;
                BucketZ = bucketZ;
                AllowNonHostilePredators = allowNonHostilePredators;
                Threat = threat;
                HasThreat = hasThreat;
                Tick = tick;
            }

            public int MapId { get; }
            public int MapRefreshTick { get; }
            public int BucketX { get; }
            public int BucketZ { get; }
            public bool AllowNonHostilePredators { get; }
            public Pawn Threat { get; }
            public bool HasThreat { get; }
            public int Tick { get; }
        }

        private sealed class ThreatScanBudgetEntry
        {
            public int Tick;
            public int Remaining;
        }

        private sealed class ThreatMapCacheData
        {
            public int RefreshTick = -ThreatMapRefreshIntervalTicks;
            public readonly Dictionary<int, List<Pawn>> HumanlikeThreatsByBucket = new Dictionary<int, List<Pawn>>(32);
            public readonly Dictionary<int, List<Pawn>> ActivePredatorThreatsByBucket = new Dictionary<int, List<Pawn>>(32);
            public readonly Dictionary<int, List<Pawn>> PassivePredatorThreatsByBucket = new Dictionary<int, List<Pawn>>(32);
            public readonly Dictionary<int, List<Pawn>> CarrierThreatsByBucket = new Dictionary<int, List<Pawn>>(16);
            public readonly Dictionary<int, Pawn> CloseMeleeHumanlikeThreatByPreyId = new Dictionary<int, Pawn>(16);
            public readonly Dictionary<int, Pawn> CloseMeleePredatorThreatByPreyId = new Dictionary<int, Pawn>(16);
            public float MaxCarrierThreatRadius;
            public int AnimalCount;
            public int ScanIntervalTicks = 1;
            public bool HasHumanlikeThreats;
            public bool HasActivePredatorThreats;
            public bool HasPassivePredatorThreats;
            public bool HasCarrierThreats;
            public bool HasCloseMeleeHumanlikeThreats;
            public bool HasCloseMeleePredatorThreats;

            public void Clear()
            {
                ThreatMapCache.ReturnBucketLists(HumanlikeThreatsByBucket);
                ThreatMapCache.ReturnBucketLists(ActivePredatorThreatsByBucket);
                ThreatMapCache.ReturnBucketLists(PassivePredatorThreatsByBucket);
                ThreatMapCache.ReturnBucketLists(CarrierThreatsByBucket);
                CloseMeleeHumanlikeThreatByPreyId.Clear();
                CloseMeleePredatorThreatByPreyId.Clear();
                MaxCarrierThreatRadius = 0f;
                AnimalCount = 0;
                ScanIntervalTicks = 1;
                HasHumanlikeThreats = false;
                HasActivePredatorThreats = false;
                HasPassivePredatorThreats = false;
                HasCarrierThreats = false;
                HasCloseMeleeHumanlikeThreats = false;
                HasCloseMeleePredatorThreats = false;
            }
        }

        private enum ThreatSearchMode
        {
            Humanlike,
            SmallPetRaider,
            Carrier,
            PredatorActive,
            PredatorPassive
        }

        private static class ThreatMapCache
        {
            private static readonly Dictionary<int, ThreatMapCacheData> byMapId = new Dictionary<int, ThreatMapCacheData>();
            private static readonly Stack<List<Pawn>> pawnListPool = new Stack<List<Pawn>>(32);

            public static bool TryGetFresh(Map map, int currentTick, out ThreatMapCacheData data)
            {
                data = null;
                if (map == null)
                {
                    return false;
                }

                if (!byMapId.TryGetValue(map.uniqueID, out data))
                {
                    return false;
                }

                return currentTick > 0 && currentTick - data.RefreshTick < ThreatMapRefreshIntervalTicks;
            }

            public static bool TryGetLastKnown(Map map, out ThreatMapCacheData data)
            {
                data = null;
                return map != null && byMapId.TryGetValue(map.uniqueID, out data);
            }

            public static ThreatMapCacheData GetOrRefresh(Map map, int currentTick)
            {
                if (map == null)
                {
                    return null;
                }

                int mapId = map.uniqueID;
                if (!byMapId.TryGetValue(mapId, out ThreatMapCacheData data))
                {
                    data = new ThreatMapCacheData();
                    byMapId[mapId] = data;
                }

                if (currentTick > 0 && currentTick - data.RefreshTick < ThreatMapRefreshIntervalTicks)
                {
                    return data;
                }

                RefreshMap(map, data, currentTick);
                return data;
            }

            public static void CleanupStaleMaps()
            {
                List<Map> maps = Find.Maps;
                HashSet<int> activeMapIds = new HashSet<int>();
                if (maps != null)
                {
                    for (int i = 0; i < maps.Count; i++)
                    {
                        Map map = maps[i];
                        if (map != null)
                        {
                            activeMapIds.Add(map.uniqueID);
                        }
                    }
                }

                List<int> staleMapIds = null;
                foreach (KeyValuePair<int, ThreatMapCacheData> entry in byMapId)
                {
                    if (activeMapIds.Contains(entry.Key))
                    {
                        continue;
                    }

                    if (staleMapIds == null)
                    {
                        staleMapIds = new List<int>(4);
                    }

                    entry.Value.Clear();
                    staleMapIds.Add(entry.Key);
                }

                if (staleMapIds == null)
                {
                    return;
                }

                for (int i = 0; i < staleMapIds.Count; i++)
                {
                    byMapId.Remove(staleMapIds[i]);
                }
            }

            private static void RefreshMap(Map map, ThreatMapCacheData data, int currentTick)
            {
                if (map == null || data == null)
                {
                    return;
                }

                data.RefreshTick = currentTick;
                data.Clear();

                IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null || pawns.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn threat = pawns[i];
                    if (threat?.RaceProps?.Animal == true)
                    {
                        data.AnimalCount++;
                    }

                    if (!IsValidThreatSource(threat))
                    {
                        continue;
                    }

                    if (PawnThreatUtility.IsHumanlikeOrMechanoid(threat) && !IsHumanDoingAnimalTamingJob(threat))
                    {
                        data.HasHumanlikeThreats = true;
                        AddHumanlikeThreatInfluence(data, threat);
                    }

                    if (TryGetCarrierExtension(threat, out ModExtension_FleeFromCarrier carrierExtension)
                        && carrierExtension.fleeRadius > 0f)
                    {
                        data.HasCarrierThreats = true;
                        AddCarrierThreatInfluence(data, threat, carrierExtension);
                    }

                    if (threat.RaceProps?.predator != true)
                    {
                        continue;
                    }

                    if (TryGetThreatTargetPawn(threat, out Pawn targetPawn))
                    {
                        data.HasActivePredatorThreats = true;
                        AddActivePredatorThreatInfluence(data, threat, targetPawn);
                    }
                    else
                    {
                        data.HasPassivePredatorThreats = true;
                        AddPassivePredatorThreatInfluence(data, threat);
                    }
                }

                data.ScanIntervalTicks = CalculateScanIntervalTicks(data.AnimalCount);
            }

            private static void AddHumanlikeThreatInfluence(ThreatMapCacheData data, Pawn threat)
            {
                AddThreat(data.HumanlikeThreatsByBucket, threat);

                Pawn targetPawn = GetThreatTargetPawnOrNull(threat);
                if (IsThreatMeleeAttackingPawn(threat, targetPawn)
                    && MarkCloseMeleeThreat(data.CloseMeleeHumanlikeThreatByPreyId, targetPawn, threat))
                {
                    data.HasCloseMeleeHumanlikeThreats = true;
                }
            }

            private static void AddActivePredatorThreatInfluence(ThreatMapCacheData data, Pawn threat, Pawn targetPawn)
            {
                AddThreat(data.ActivePredatorThreatsByBucket, threat);

                if (targetPawn != null
                    && IsThreatMeleeAttackingPawn(threat, targetPawn)
                    && MarkCloseMeleeThreat(data.CloseMeleePredatorThreatByPreyId, targetPawn, threat))
                {
                    data.HasCloseMeleePredatorThreats = true;
                }
            }

            private static void AddPassivePredatorThreatInfluence(ThreatMapCacheData data, Pawn threat)
            {
                AddThreat(data.PassivePredatorThreatsByBucket, threat);
            }

            private static void AddCarrierThreatInfluence(ThreatMapCacheData data, Pawn threat, ModExtension_FleeFromCarrier extension)
            {
                AddThreat(data.CarrierThreatsByBucket, threat);
                if (extension != null && extension.fleeRadius > data.MaxCarrierThreatRadius)
                {
                    data.MaxCarrierThreatRadius = extension.fleeRadius;
                }
            }

            private static void AddThreat(Dictionary<int, List<Pawn>> buckets, Pawn threat)
            {
                if (buckets == null || threat == null || !threat.Spawned)
                {
                    return;
                }

                int key = MakeBucketKey(threat.Position);
                if (!buckets.TryGetValue(key, out List<Pawn> list))
                {
                    list = RentPawnList();
                    buckets[key] = list;
                }

                list.Add(threat);
            }

            private static bool MarkCloseMeleeThreat(Dictionary<int, Pawn> threatsByPreyId, Pawn prey, Pawn threat)
            {
                if (threatsByPreyId == null || prey == null || threat == null || !IsValidNearbyPrey(prey, threat))
                {
                    return false;
                }

                threatsByPreyId[prey.thingIDNumber] = threat;
                return true;
            }

            public static void ReturnBucketLists(Dictionary<int, List<Pawn>> buckets)
            {
                if (buckets == null)
                {
                    return;
                }

                foreach (List<Pawn> list in buckets.Values)
                {
                    if (list == null)
                    {
                        continue;
                    }

                    list.Clear();
                    pawnListPool.Push(list);
                }

                buckets.Clear();
            }

            private static List<Pawn> RentPawnList()
            {
                if (pawnListPool.Count > 0)
                {
                    return pawnListPool.Pop();
                }

                return new List<Pawn>(4);
            }

            private static bool IsValidThreatSource(Pawn threat)
            {
                return threat != null
                    && threat.Spawned
                    && !threat.Dead
                    && !threat.Destroyed
                    && !threat.Downed;
            }

            private static bool IsValidNearbyPrey(Pawn prey, Pawn threat)
            {
                return prey != null
                    && threat != null
                    && prey != threat
                    && prey.RaceProps?.Animal == true
                    && prey.Spawned
                    && !prey.Dead
                    && !prey.Destroyed
                    && prey.Map == threat.Map;
            }

            private static Pawn GetThreatTargetPawnOrNull(Pawn threat)
            {
                return threat?.CurJob?.GetTarget(TargetIndex.A).Thing as Pawn;
            }

            private static int MakeBucketKey(IntVec3 cell)
            {
                int bucketX = cell.x / ThreatBucketSize;
                int bucketZ = cell.z / ThreatBucketSize;
                return ((bucketZ & 0xFFFF) << 16) | (bucketX & 0xFFFF);
            }

            private static int CalculateScanIntervalTicks(int animalCount)
            {
                if (animalCount >= 1200) return 16;
                if (animalCount >= 640) return 12;
                if (animalCount >= 320) return 8;
                if (animalCount >= 160) return 4;
                if (animalCount >= 80) return 2;
                return 1;
            }
        }

        private static readonly Dictionary<int, NoThreatScanCacheEntry> noThreatScanCacheByPawnId = new Dictionary<int, NoThreatScanCacheEntry>(128);
        private static readonly Dictionary<long, ThreatVisibilityCacheEntry> lineOfSightAndReachCacheByPairKey = new Dictionary<long, ThreatVisibilityCacheEntry>(512);
        private static readonly Dictionary<long, ThreatVisibilityCacheEntry> lineOfSightOrReachCacheByPairKey = new Dictionary<long, ThreatVisibilityCacheEntry>(256);
        private static readonly Dictionary<NearbyThreatBucketCacheKey, NearbyThreatBucketsCacheEntry> nearbyThreatBucketsCacheByBucketKey = new Dictionary<NearbyThreatBucketCacheKey, NearbyThreatBucketsCacheEntry>(256);
        private static readonly Dictionary<int, NearestPredatorThreatCacheEntry> nearestPredatorThreatCacheByPawnId = new Dictionary<int, NearestPredatorThreatCacheEntry>(256);
        private static readonly Dictionary<int, ThreatScanBudgetEntry> threatScanBudgetByMapId = new Dictionary<int, ThreatScanBudgetEntry>(8);

        private const float PredatorSearchRadius = 12f;
        private const float NonHostilePredatorSearchRadiusFactor = 0.5f;
        private const float HumanSearchRadius = 6f;
        private const float SmallPetRaiderThreatSearchRadius = 18f;
        private const int CarrierFleeDistanceDefault = 24;
        private const int SmallPetFleeDistanceDefault = 24;
        private const int FleeDistanceDefault = 12;
        private const int FleeDistanceTarget = 16;
        private const int NoThreatScanCooldownTicks = ZoologyTickLimiter.FleeThreat.NoThreatScanCooldownTicks;
        private const int ThreatMapRefreshIntervalTicks = ZoologyTickLimiter.FleeThreat.ThreatMapRefreshIntervalTicks;
        private const int ThreatVisibilityCacheDurationTicks = ZoologyTickLimiter.FleeThreat.ThreatVisibilityCacheDurationTicks;
        private const int NearbyThreatBucketsCacheDurationTicks = ZoologyTickLimiter.FleeThreat.NearbyThreatBucketsCacheDurationTicks;
        private const int NearestPredatorThreatCacheDurationTicks = ZoologyTickLimiter.FleeThreat.NearestPredatorThreatCacheDurationTicks;
        private const int ThreatCacheCleanupIntervalTicks = ZoologyTickLimiter.FleeThreat.ThreatCacheCleanupIntervalTicks;
        private const int ThreatBucketSize = 8;
        private const int MinThreatScanIntervalTicks = ZoologyTickLimiter.FleeThreat.MinThreatScanIntervalTicks;
        private const int ThreatScanBudgetMin = 8;
        private const int ThreatScanBudgetBase = 12;
        private const int ThreatScanBudgetPer50Animals = 4;
        private const int ThreatScanBudgetMax = 96;
        private const int ThreatScanBudgetCooldownTicks = ZoologyTickLimiter.FleeThreat.ThreatScanBudgetCooldownTicks;
        private const int FallbackThreatScanBudgetPerTick = ZoologyTickLimiter.FleeThreat.FallbackThreatScanBudgetPerTick;

        private static int lastThreatCacheCleanupTick = -ThreatCacheCleanupIntervalTicks;
        private static int lastFreshThreatCacheTick = int.MinValue;
        private static int lastFreshThreatCacheMapId = int.MinValue;
        private static bool lastFreshThreatCacheWasFresh;
        private static ThreatMapCacheData lastFreshThreatCacheData;
        private static int fallbackThreatScanBudgetTick = -1;
        private static int fallbackThreatScanBudgetRemaining = 0;

        public static bool Prepare()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null
                || settings.EnablePreyFleeFromPredators
                || settings.AnimalsFreeFromHumans
                || settings.EnableFleeFromCarrier
                || (settings.EnableIgnoreSmallPetsByRaiders && settings.EnableSmallPetFleeFromRaiders);
        }

        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                bool fleeFromPredatorsEnabled = settings == null || settings.EnablePreyFleeFromPredators;
                bool fleeFromHumansEnabled = settings != null && settings.AnimalsFreeFromHumans;
                bool fleeFromCarriersEnabled = settings == null || settings.EnableFleeFromCarrier;
                bool fleeFromRaidersForSmallPetsEnabled = settings != null
                    && settings.EnableIgnoreSmallPetsByRaiders
                    && settings.EnableSmallPetFleeFromRaiders;
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                RaceProperties raceProps = pawn?.RaceProps;
                if (pawn == null || pawn.Map == null || raceProps == null || !raceProps.Animal || pawn.Dead || pawn.Destroyed)
                {
                    return;
                }

                if (NoFleeUtil.IsNoFlee(pawn))
                {
                    return;
                }

                if (__result != null || pawn.jobs?.curJob?.def == JobDefOf.Flee)
                {
                    ClearNoThreatScanCache(pawn);
                    return;
                }

                bool allowNonHostilePredators = settings != null
                    && settings.EnablePreyFleeFromPredators
                    && settings.AnimalsFleeFromNonHostlePredators;

                if (ShouldSkipThreatScan(
                    pawn,
                    currentTick,
                    fleeFromPredatorsEnabled,
                    allowNonHostilePredators,
                    fleeFromHumansEnabled,
                    fleeFromCarriersEnabled,
                    fleeFromRaidersForSmallPetsEnabled))
                {
                    return;
                }

                if (!fleeFromPredatorsEnabled
                    && !fleeFromCarriersEnabled
                    && !fleeFromHumansEnabled
                    && !fleeFromRaidersForSmallPetsEnabled)
                {
                    RememberNoThreatScan(
                        pawn,
                        currentTick,
                        fleeFromPredatorsEnabled,
                        fleeFromHumansEnabled,
                        fleeFromCarriersEnabled,
                        fleeFromRaidersForSmallPetsEnabled);
                    return;
                }

                if (fleeFromPredatorsEnabled && TryHandlePredatorThreat(pawn, allowNonHostilePredators, out Job predatorFleeJob))
                {
                    ClearNoThreatScanCache(pawn);
                    if (predatorFleeJob != null)
                    {
                        __result = predatorFleeJob;
                    }
                    return;
                }

                if (fleeFromCarriersEnabled)
                {
                    Job carrierJob = TryCreateCarrierFleeJob(pawn);
                    if (carrierJob != null)
                    {
                        ClearNoThreatScanCache(pawn);
                        __result = carrierJob;
                        return;
                    }
                }

                if (fleeFromRaidersForSmallPetsEnabled)
                {
                    Job smallPetRaiderFleeJob = TryCreateSmallPetRaiderFleeJob(pawn, settings);
                    if (smallPetRaiderFleeJob != null)
                    {
                        ClearNoThreatScanCache(pawn);
                        __result = smallPetRaiderFleeJob;
                        return;
                    }
                }

                if (!fleeFromHumansEnabled || __result != null)
                {
                    if (__result == null)
                    {
                        RememberNoThreatScan(
                            pawn,
                            currentTick,
                            fleeFromPredatorsEnabled,
                            fleeFromHumansEnabled,
                            fleeFromCarriersEnabled,
                            fleeFromRaidersForSmallPetsEnabled);
                    }
                    return;
                }

                Job humanJob = TryCreateHumanFleeJob(pawn, settings);
                if (humanJob != null)
                {
                    ClearNoThreatScanCache(pawn);
                    __result = humanJob;
                    return;
                }

                if (__result == null)
                {
                    RememberNoThreatScan(
                        pawn,
                        currentTick,
                        fleeFromPredatorsEnabled,
                        fleeFromHumansEnabled,
                        fleeFromCarriersEnabled,
                        fleeFromRaidersForSmallPetsEnabled);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[ZoologyMod] Patch_AnimalFleeFromPredators failed: {e}");
            }
        }

        private static bool TryHandlePredatorThreat(Pawn pawn, bool allowNonHostilePredators, out Job fleeJob)
        {
            fleeJob = null;

            if (pawn?.Map == null)
            {
                return false;
            }

            bool preyIsPhotonozoa = ZoologyCacheUtility.IsPhotonozoa(pawn.def);
            bool foodJobActive = IsFoodSeekingOrEatingJob(pawn);
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!TryGetThreatCache(pawn, refreshIfNeeded: true, out ThreatMapCacheData threatCache))
            {
                if (TryConsumeFallbackThreatScanBudget(currentTick))
                {
                    Pawn fallbackThreat = FindNearestPredatorThreatFallback(pawn, PredatorSearchRadius, allowNonHostilePredators);
                    return TryBuildPredatorFleeJob(pawn, fallbackThreat, preyIsPhotonozoa, foodJobActive, out fleeJob);
                }

                return false;
            }

            if (threatCache.HasCloseMeleePredatorThreats
                && HasActiveCloseMeleeThreatFromPredator(pawn, threatCache))
            {
                return true;
            }

            if (!threatCache.HasActivePredatorThreats
                && (!allowNonHostilePredators || !threatCache.HasPassivePredatorThreats))
            {
                if (TryConsumeFallbackThreatScanBudget(currentTick))
                {
                    Pawn fallbackThreat = FindNearestPredatorThreatFallback(pawn, PredatorSearchRadius, allowNonHostilePredators);
                    return TryBuildPredatorFleeJob(pawn, fallbackThreat, preyIsPhotonozoa, foodJobActive, out fleeJob);
                }

                return false;
            }

            if (foodJobActive && !threatCache.HasActivePredatorThreats)
            {
                return false;
            }

            Pawn threat;
            if (!TryGetCachedNearestPredatorThreat(
                pawn,
                threatCache,
                allowNonHostilePredators,
                currentTick,
                out bool hasCachedThreat,
                out threat))
            {
                threat = FindNearestPredatorThreat(pawn, threatCache, PredatorSearchRadius, allowNonHostilePredators);
                StoreNearestPredatorThreatCache(pawn, threatCache, allowNonHostilePredators, threat, threat != null, currentTick);
            }
            else if (!hasCachedThreat)
            {
                return false;
            }

            return TryBuildPredatorFleeJob(pawn, threat, preyIsPhotonozoa, foodJobActive, out fleeJob);
        }

        private static bool TryBuildPredatorFleeJob(
            Pawn pawn,
            Pawn threat,
            bool preyIsPhotonozoa,
            bool foodJobActive,
            out Job fleeJob)
        {
            fleeJob = null;
            if (pawn == null || threat == null)
            {
                return false;
            }

            bool shouldAnimalFleeDanger = FleeUtility.ShouldAnimalFleeDanger(pawn);
            if (!preyIsPhotonozoa && !shouldAnimalFleeDanger)
            {
                return false;
            }

            bool threatAimingAtPawn = IsThreatTargetingPawn(threat, pawn);
            if (foodJobActive && !threatAimingAtPawn)
            {
                return false;
            }

            int fleeDistance = threatAimingAtPawn ? FleeDistanceTarget : FleeDistanceDefault;

            bool bothPhotonozoaInTheirFaction = IsPhotonozoaPairInTheirFaction(threat, pawn);
            if (!bothPhotonozoaInTheirFaction)
            {
                if (preyIsPhotonozoa && !shouldAnimalFleeDanger)
                {
                    return false;
                }
            }
            else
            {
                if (pawn.InMentalState || pawn.IsFighting() || pawn.Downed)
                {
                    return false;
                }

                if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn))
                {
                    return false;
                }

                if (pawn.Faction == Faction.OfPlayer && pawn.Map.IsPlayerHome)
                {
                    return false;
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (HasFreshFleeJob(pawn, currentTick))
                {
                    return false;
                }
            }

            if (threatAimingAtPawn)
            {
                HandlePursuitAllowanceIfNeeded(threat, pawn);
            }

            fleeJob = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            return fleeJob != null;
        }

        private static Job TryCreateHumanFleeJob(Pawn pawn, ZoologyModSettings settings)
        {
            if (!TryGetThreatCache(pawn, refreshIfNeeded: true, out ThreatMapCacheData threatCache))
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!TryConsumeFallbackThreatScanBudget(currentTick))
                {
                    return null;
                }

                if (!CanAnimalFleeFromHumans(pawn, settings, null))
                {
                    return null;
                }

                Pawn fallbackThreat = FindNearestHumanlikeThreatFallback(pawn, HumanSearchRadius);
                return fallbackThreat != null ? FleeUtility.FleeJob(pawn, fallbackThreat, FleeDistanceDefault) : null;
            }

            if (!threatCache.HasHumanlikeThreats)
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (!TryConsumeFallbackThreatScanBudget(currentTick))
                {
                    return null;
                }

                if (!CanAnimalFleeFromHumans(pawn, settings, threatCache))
                {
                    return null;
                }

                Pawn fallbackThreat = FindNearestHumanlikeThreatFallback(pawn, HumanSearchRadius);
                return fallbackThreat != null ? FleeUtility.FleeJob(pawn, fallbackThreat, FleeDistanceDefault) : null;
            }

            if (!CanAnimalFleeFromHumans(pawn, settings, threatCache))
            {
                return null;
            }

            Pawn threat = FindNearestHumanlikeThreat(pawn, threatCache, HumanSearchRadius);
            if (threat == null)
            {
                return null;
            }

            return FleeUtility.FleeJob(pawn, threat, FleeDistanceDefault);
        }

        private static Job TryCreateCarrierFleeJob(Pawn pawn)
        {
            if (!CanAnimalFleeFromCarriers(pawn))
            {
                return null;
            }

            Pawn threat = FindNearestCarrierThreat(pawn, out int fleeDistance);
            if (threat == null)
            {
                return null;
            }

            return FleeUtility.FleeJob(pawn, threat, fleeDistance);
        }

        private static Job TryCreateSmallPetRaiderFleeJob(Pawn pawn, ZoologyModSettings settings)
        {
            if (!CanSmallPetFleeFromRaiders(pawn, settings))
            {
                return null;
            }

            Pawn threat = FindNearestSmallPetRaiderThreat(pawn);
            if (threat == null)
            {
                return null;
            }

            return FleeUtility.FleeJob(pawn, threat, SmallPetFleeDistanceDefault);
        }

        private static bool CanAnimalFleeFromHumans(Pawn pawn, ZoologyModSettings settings, ThreatMapCacheData threatCache = null)
        {
            if (pawn == null || settings == null || !settings.AnimalsFreeFromHumans)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Map == null || pawn.Faction != null || pawn.RaceProps.Humanlike)
            {
                return false;
            }

            if (pawn.Downed || pawn.InMentalState || pawn.IsFighting())
            {
                return false;
            }

            if (IsFoodSeekingOrEatingJob(pawn))
            {
                return false;
            }

            if (threatCache == null)
            {
                if (HasActiveCloseMeleeThreatFromHumanlike(pawn))
                {
                    return false;
                }
            }
            else if (HasActiveCloseMeleeThreatFromHumanlike(pawn, threatCache))
            {
                return false;
            }

            if (!settings.GetAnimalsFreeFromHumansFor(pawn.def))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (HasFreshFleeJob(pawn, currentTick))
            {
                return false;
            }

            if (HasAggressiveJob(pawn))
            {
                return false;
            }

            if (IsPredatorGuardingCorpseAgainstHumans(pawn, settings))
            {
                return false;
            }

            return FleeUtility.ShouldAnimalFleeDanger(pawn);
        }

        private static bool CanAnimalFleeFromCarriers(Pawn pawn)
        {
            if (pawn == null || !pawn.RaceProps.Animal || !pawn.Spawned || pawn.Map == null)
            {
                return false;
            }

            if (pawn.Dead || pawn.Downed || pawn.Destroyed || pawn.InMentalState || pawn.IsFighting())
            {
                return false;
            }

            if (TryGetCarrierExtension(pawn, out _))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (HasFreshFleeJob(pawn, currentTick))
            {
                return false;
            }

            return FleeUtility.ShouldAnimalFleeDanger(pawn);
        }

        private static bool CanSmallPetFleeFromRaiders(Pawn pawn, ZoologyModSettings settings)
        {
            if (pawn == null
                || settings == null
                || !pawn.RaceProps.Animal
                || pawn.Faction != Faction.OfPlayer
                || pawn.Map == null
                || pawn.Roamer)
            {
                return false;
            }

            return pawn.RaceProps.baseBodySize < settings.SmallPetBodySizeThreshold
                && FleeUtility.ShouldAnimalFleeDanger(pawn);
        }

        private static bool ShouldSkipThreatScan(
            Pawn pawn,
            int currentTick,
            bool predatorsEnabled,
            bool allowNonHostilePredators,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled)
        {
            if (pawn == null || pawn.Map == null || currentTick <= 0)
            {
                return false;
            }

            Map map = pawn.Map;
            int pawnId = pawn.thingIDNumber;
            int mapId = map.uniqueID;

            if (TryUseNoThreatScanCache(
                pawnId,
                mapId,
                currentTick,
                predatorsEnabled,
                humansEnabled,
                carriersEnabled,
                smallPetRaidersEnabled))
            {
                return true;
            }

            if (!TryGetFreshThreatCacheFast(map, currentTick, out ThreatMapCacheData freshThreatCache))
            {
                return false;
            }

            if (!HasRelevantThreats(freshThreatCache, predatorsEnabled, humansEnabled, carriersEnabled, smallPetRaidersEnabled))
            {
                RememberNoThreatScan(
                    pawn,
                    currentTick,
                    predatorsEnabled,
                    humansEnabled,
                    carriersEnabled,
                    smallPetRaidersEnabled);
                return true;
            }

            int scanIntervalTicks = freshThreatCache.ScanIntervalTicks;
            if (scanIntervalTicks < MinThreatScanIntervalTicks)
            {
                scanIntervalTicks = MinThreatScanIntervalTicks;
            }

            if (scanIntervalTicks > 1 && ((currentTick + pawnId) % scanIntervalTicks) != 0)
            {
                return true;
            }

            if (!TryConsumeThreatScanBudget(map, currentTick))
            {
                RememberNoThreatScan(
                    pawn,
                    currentTick,
                    predatorsEnabled,
                    humansEnabled,
                    carriersEnabled,
                    smallPetRaidersEnabled,
                    ThreatScanBudgetCooldownTicks);
                return true;
            }

            return false;
        }

        private static bool TryUseNoThreatScanCache(
            int pawnId,
            int mapId,
            int currentTick,
            bool predatorsEnabled,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled)
        {
            if (!noThreatScanCacheByPawnId.TryGetValue(pawnId, out NoThreatScanCacheEntry cached))
            {
                return false;
            }

            if (cached.MapId != mapId
                || cached.PredatorsEnabled != predatorsEnabled
                || cached.HumansEnabled != humansEnabled
                || cached.CarriersEnabled != carriersEnabled
                || cached.SmallPetRaidersEnabled != smallPetRaidersEnabled
                || currentTick >= cached.NextScanTick)
            {
                noThreatScanCacheByPawnId.Remove(pawnId);
                return false;
            }

            return true;
        }

        private static bool TryGetFreshThreatCacheFast(Map map, int currentTick, out ThreatMapCacheData cache)
        {
            cache = null;
            if (map == null || currentTick <= 0)
            {
                return false;
            }

            int mapId = map.uniqueID;
            if (lastFreshThreatCacheWasFresh
                && lastFreshThreatCacheTick == currentTick
                && lastFreshThreatCacheMapId == mapId
                && lastFreshThreatCacheData != null
                && currentTick - lastFreshThreatCacheData.RefreshTick < ThreatMapRefreshIntervalTicks)
            {
                cache = lastFreshThreatCacheData;
                return true;
            }

            bool hasFresh = ThreatMapCache.TryGetFresh(map, currentTick, out ThreatMapCacheData freshCache);
            if (hasFresh)
            {
                lastFreshThreatCacheTick = currentTick;
                lastFreshThreatCacheMapId = mapId;
                lastFreshThreatCacheWasFresh = true;
                lastFreshThreatCacheData = freshCache;
                cache = freshCache;
                return true;
            }

            return false;
        }

        private static bool HasRelevantThreats(
            ThreatMapCacheData cache,
            bool predatorsEnabled,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled)
        {
            if (cache == null)
            {
                return true;
            }

            if (predatorsEnabled && (cache.HasActivePredatorThreats || cache.HasPassivePredatorThreats))
            {
                return true;
            }

            if ((humansEnabled || smallPetRaidersEnabled) && cache.HasHumanlikeThreats)
            {
                return true;
            }

            return carriersEnabled && cache.HasCarrierThreats;
        }

        private static bool HasNearbyRelevantThreatBuckets(
            ThreatMapCacheData cache,
            Pawn pawn,
            bool predatorsEnabled,
            bool allowNonHostilePredators,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled)
        {
            if (cache == null || pawn?.Map == null)
            {
                return true;
            }

            if (predatorsEnabled && HasThreatBucketsInRange(cache.ActivePredatorThreatsByBucket, pawn.Position, PredatorSearchRadius))
            {
                return true;
            }

            if (predatorsEnabled
                && allowNonHostilePredators
                && HasThreatBucketsInRange(
                    cache.PassivePredatorThreatsByBucket,
                    pawn.Position,
                    PredatorSearchRadius * NonHostilePredatorSearchRadiusFactor))
            {
                return true;
            }

            float humanRadius = 0f;
            if (humansEnabled)
            {
                humanRadius = HumanSearchRadius;
            }

            if (smallPetRaidersEnabled && SmallPetRaiderThreatSearchRadius > humanRadius)
            {
                humanRadius = SmallPetRaiderThreatSearchRadius;
            }

            if (humanRadius > 0f && HasThreatBucketsInRange(cache.HumanlikeThreatsByBucket, pawn.Position, humanRadius))
            {
                return true;
            }

            return carriersEnabled
                && cache.MaxCarrierThreatRadius > 0f
                && HasThreatBucketsInRange(cache.CarrierThreatsByBucket, pawn.Position, cache.MaxCarrierThreatRadius);
        }

        private static bool TryGetNearbyThreatBucketsCache(
            Pawn pawn,
            ThreatMapCacheData cache,
            bool predatorsEnabled,
            bool allowNonHostilePredators,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled,
            int currentTick,
            out bool hasNearbyThreatBuckets)
        {
            hasNearbyThreatBuckets = false;
            if (pawn?.Map == null || cache == null || currentTick <= 0)
            {
                return false;
            }

            IntVec3 position = pawn.Position;
            int bucketX = position.x / ThreatBucketSize;
            int bucketZ = position.z / ThreatBucketSize;
            int flagsMask = MakeThreatOptionFlagsMask(
                predatorsEnabled,
                allowNonHostilePredators,
                humansEnabled,
                carriersEnabled,
                smallPetRaidersEnabled);
            var key = new NearbyThreatBucketCacheKey(
                pawn.Map.uniqueID,
                cache.RefreshTick,
                bucketX,
                bucketZ,
                flagsMask);
            if (!nearbyThreatBucketsCacheByBucketKey.TryGetValue(key, out NearbyThreatBucketsCacheEntry cached))
            {
                return false;
            }

            if (currentTick - cached.Tick > NearbyThreatBucketsCacheDurationTicks)
            {
                nearbyThreatBucketsCacheByBucketKey.Remove(key);
                return false;
            }

            hasNearbyThreatBuckets = cached.HasNearbyThreatBuckets;
            return true;
        }

        private static void StoreNearbyThreatBucketsCache(
            Pawn pawn,
            ThreatMapCacheData cache,
            bool predatorsEnabled,
            bool allowNonHostilePredators,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled,
            bool hasNearbyThreatBuckets,
            int currentTick)
        {
            if (pawn?.Map == null || cache == null || currentTick <= 0)
            {
                return;
            }

            IntVec3 position = pawn.Position;
            int flagsMask = MakeThreatOptionFlagsMask(
                predatorsEnabled,
                allowNonHostilePredators,
                humansEnabled,
                carriersEnabled,
                smallPetRaidersEnabled);
            var key = new NearbyThreatBucketCacheKey(
                pawn.Map.uniqueID,
                cache.RefreshTick,
                position.x / ThreatBucketSize,
                position.z / ThreatBucketSize,
                flagsMask);
            nearbyThreatBucketsCacheByBucketKey[key] = new NearbyThreatBucketsCacheEntry(hasNearbyThreatBuckets, currentTick);
        }

        private static bool TryGetCachedNearestPredatorThreat(
            Pawn pawn,
            ThreatMapCacheData cache,
            bool allowNonHostilePredators,
            int currentTick,
            out bool hasThreat,
            out Pawn threat)
        {
            hasThreat = false;
            threat = null;

            if (pawn?.Map == null || cache == null || currentTick <= 0)
            {
                return false;
            }

            int pawnId = pawn.thingIDNumber;
            if (!nearestPredatorThreatCacheByPawnId.TryGetValue(pawnId, out NearestPredatorThreatCacheEntry cached))
            {
                return false;
            }

            IntVec3 position = pawn.Position;
            int bucketX = position.x / ThreatBucketSize;
            int bucketZ = position.z / ThreatBucketSize;

            if (cached.MapId != pawn.Map.uniqueID
                || cached.MapRefreshTick != cache.RefreshTick
                || cached.BucketX != bucketX
                || cached.BucketZ != bucketZ
                || cached.AllowNonHostilePredators != allowNonHostilePredators
                || currentTick - cached.Tick > NearestPredatorThreatCacheDurationTicks)
            {
                nearestPredatorThreatCacheByPawnId.Remove(pawnId);
                return false;
            }

            if (!cached.HasThreat)
            {
                hasThreat = false;
                return true;
            }

            Pawn cachedThreat = cached.Threat;
            if (cachedThreat == null)
            {
                nearestPredatorThreatCacheByPawnId.Remove(pawnId);
                return false;
            }

            bool isValidThreat = IsTrackedActivePredatorThreatStillValid(cachedThreat, pawn, PredatorSearchRadius)
                || (allowNonHostilePredators
                    && IsTrackedPassivePredatorThreatStillValid(
                        cachedThreat,
                        pawn,
                        PredatorSearchRadius * NonHostilePredatorSearchRadiusFactor));

            if (!isValidThreat)
            {
                nearestPredatorThreatCacheByPawnId.Remove(pawnId);
                return false;
            }

            hasThreat = true;
            threat = cachedThreat;
            return true;
        }

        private static void StoreNearestPredatorThreatCache(
            Pawn pawn,
            ThreatMapCacheData cache,
            bool allowNonHostilePredators,
            Pawn threat,
            bool hasThreat,
            int currentTick)
        {
            if (pawn?.Map == null || cache == null || currentTick <= 0)
            {
                return;
            }

            IntVec3 position = pawn.Position;
            nearestPredatorThreatCacheByPawnId[pawn.thingIDNumber] = new NearestPredatorThreatCacheEntry(
                pawn.Map.uniqueID,
                cache.RefreshTick,
                position.x / ThreatBucketSize,
                position.z / ThreatBucketSize,
                allowNonHostilePredators,
                threat,
                hasThreat,
                currentTick);
        }

        private static int MakeThreatOptionFlagsMask(
            bool predatorsEnabled,
            bool allowNonHostilePredators,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled)
        {
            int mask = 0;
            if (predatorsEnabled) mask |= 1;
            if (allowNonHostilePredators) mask |= 2;
            if (humansEnabled) mask |= 4;
            if (carriersEnabled) mask |= 8;
            if (smallPetRaidersEnabled) mask |= 16;
            return mask;
        }

        private static void RememberNoThreatScan(
            Pawn pawn,
            int currentTick,
            bool predatorsEnabled,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled)
        {
            RememberNoThreatScan(
                pawn,
                currentTick,
                predatorsEnabled,
                humansEnabled,
                carriersEnabled,
                smallPetRaidersEnabled,
                NoThreatScanCooldownTicks);
        }

        private static void RememberNoThreatScan(
            Pawn pawn,
            int currentTick,
            bool predatorsEnabled,
            bool humansEnabled,
            bool carriersEnabled,
            bool smallPetRaidersEnabled,
            int cooldownTicks)
        {
            if (pawn?.Map == null || currentTick <= 0 || cooldownTicks <= 0)
            {
                return;
            }

            noThreatScanCacheByPawnId[pawn.thingIDNumber] = new NoThreatScanCacheEntry(
                currentTick + cooldownTicks,
                pawn.Map.uniqueID,
                predatorsEnabled,
                humansEnabled,
                carriersEnabled,
                smallPetRaidersEnabled);
            CleanupThreatCachesIfNeeded(currentTick);
        }

        private static void ClearNoThreatScanCache(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            noThreatScanCacheByPawnId.Remove(pawn.thingIDNumber);
        }

        private static bool HasAggressiveJob(Pawn pawn)
        {
            Job curJob = pawn?.CurJob;
            if (curJob == null)
            {
                return false;
            }

            if (curJob.def == JobDefOf.AttackMelee)
            {
                return true;
            }

            if (ProtectPreyState.IsProtectPreyJob(curJob, pawn.jobs?.curDriver))
            {
                return true;
            }

            Type driverClass = curJob.def?.driverClass;
            return driverClass != null && typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverClass);
        }

        private static bool IsFoodSeekingOrEatingJob(Pawn pawn)
        {
            Job curJob = pawn?.CurJob;
            if (curJob?.def == null)
            {
                return false;
            }

            if (curJob.def == JobDefOf.Ingest)
            {
                return true;
            }

            Type driverClass = curJob.def.driverClass;
            if (driverClass != null && typeof(JobDriver_Ingest).IsAssignableFrom(driverClass))
            {
                return true;
            }

            string defName = curJob.def.defName;
            if (!string.IsNullOrEmpty(defName) && defName.IndexOf("Ingest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string driverName = driverClass?.Name;
            return !string.IsNullOrEmpty(driverName)
                && driverName.IndexOf("Ingest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPredatorGuardingCorpseAgainstHumans(Pawn pawn, ZoologyModSettings settings)
        {
            if (pawn == null
                || settings == null
                || !settings.AnimalsFreeFromHumans
                || !settings.EnablePredatorDefendCorpse
                || !ZoologyModSettings.CanPredatorDefendPreyFromHumansAndMechanoidsNow(pawn))
            {
                return false;
            }

            try
            {
                Corpse pairedCorpse = PredatorPreyPairGameComponent.Instance?.GetPairedCorpse(pawn);
                return pairedCorpse != null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[ZoologyMod] Failed to check paired corpse for {pawn?.LabelShort}: {ex}");
                return false;
            }
        }

        private static bool HasFreshFleeJob(Pawn pawn, int currentTick)
        {
            Job curJob = pawn?.jobs?.curJob;
            return curJob != null
                && curJob.def == JobDefOf.Flee
                && curJob.startTick == currentTick;
        }

        private static bool HasActiveCloseMeleeThreatFromPredator(Pawn pawn)
        {
            return HasActiveCloseMeleeThreatFromPredator(pawn, refreshIfNeeded: true);
        }

        private static bool HasActiveCloseMeleeThreatFromPredator(Pawn pawn, bool refreshIfNeeded)
        {
            return TryGetCloseMeleeThreat(pawn, isPredatorThreat: true, refreshIfNeeded, out Pawn threat)
                && IsThreatMeleeAttackingPawn(threat, pawn);
        }

        private static bool HasActiveCloseMeleeThreatFromPredator(Pawn pawn, ThreatMapCacheData cache)
        {
            return TryGetCloseMeleeThreat(pawn, isPredatorThreat: true, cache, out Pawn threat)
                && IsThreatMeleeAttackingPawn(threat, pawn);
        }

        private static bool HasActiveCloseMeleeThreatFromHumanlike(Pawn pawn)
        {
            return HasActiveCloseMeleeThreatFromHumanlike(pawn, refreshIfNeeded: true);
        }

        private static bool HasActiveCloseMeleeThreatFromHumanlike(Pawn pawn, bool refreshIfNeeded)
        {
            return TryGetCloseMeleeThreat(pawn, isPredatorThreat: false, refreshIfNeeded, out Pawn threat)
                && IsThreatMeleeAttackingPawn(threat, pawn);
        }

        private static bool HasActiveCloseMeleeThreatFromHumanlike(Pawn pawn, ThreatMapCacheData cache)
        {
            return TryGetCloseMeleeThreat(pawn, isPredatorThreat: false, cache, out Pawn threat)
                && IsThreatMeleeAttackingPawn(threat, pawn);
        }

        private static bool IsThreatMeleeAttackingPawn(Pawn threat, Pawn pawn)
        {
            if (threat == null
                || pawn == null
                || threat == pawn
                || !threat.Spawned
                || !pawn.Spawned
                || threat.Dead
                || threat.Destroyed
                || threat.Downed
                || threat.Map != pawn.Map
                || !threat.Position.AdjacentTo8WayOrInside(pawn.Position))
            {
                return false;
            }

            Job curJob = threat.CurJob;
            if (curJob?.def != JobDefOf.AttackMelee || !curJob.targetA.HasThing)
            {
                return false;
            }

            return ReferenceEquals(curJob.GetTarget(TargetIndex.A).Thing, pawn);
        }

        private static bool IsHumanlikeThreat(Pawn pawn)
        {
            return PawnThreatUtility.IsHumanlikeOrMechanoid(pawn);
        }

        private static bool IsPredatorLikeThreat(Pawn pawn)
        {
            return pawn != null
                && ((pawn.RaceProps?.predator ?? false) || ProtectPreyState.IsProtectPreyJob(pawn));
        }

        private static bool IsHumanDoingAnimalTamingJob(Pawn human)
        {
            Job curJob = human?.CurJob;
            JobDef jobDef = curJob?.def;
            if (jobDef == null || !DoesJobTargetAnimal(curJob))
            {
                return false;
            }

            if (jobDef == JobDefOf.Tame || jobDef == JobDefOf.Train)
            {
                return true;
            }

            string defName = jobDef.defName;
            if (JobNameMatchesTaming(defName))
            {
                return true;
            }

            Type driverClass = jobDef.driverClass;
            return JobNameMatchesTaming(driverClass?.Name) || JobNameMatchesTaming(driverClass?.FullName);
        }

        private static bool DoesJobTargetAnimal(Job job)
        {
            if (job == null)
            {
                return false;
            }

            return JobTargetIsAnimal(job.targetA)
                || JobTargetIsAnimal(job.targetB)
                || JobTargetIsAnimal(job.targetC);
        }

        private static bool JobTargetIsAnimal(LocalTargetInfo target)
        {
            return target.HasThing && target.Thing is Pawn targetPawn && targetPawn.RaceProps?.Animal == true;
        }

        private static bool JobNameMatchesTaming(string jobName)
        {
            return !string.IsNullOrEmpty(jobName)
                && (jobName.IndexOf("Tame", StringComparison.OrdinalIgnoreCase) >= 0
                    || jobName.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TryGetThreatCache(Pawn pawn, bool refreshIfNeeded, out ThreatMapCacheData cache)
        {
            cache = null;
            if (pawn?.Map == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            cache = refreshIfNeeded
                ? ThreatMapCache.GetOrRefresh(pawn.Map, currentTick)
                : TryGetFreshThreatCacheFast(pawn.Map, currentTick, out ThreatMapCacheData freshCache) ? freshCache : null;

            if (refreshIfNeeded && cache != null && currentTick > 0)
            {
                lastFreshThreatCacheTick = currentTick;
                lastFreshThreatCacheMapId = pawn.Map.uniqueID;
                lastFreshThreatCacheWasFresh = true;
                lastFreshThreatCacheData = cache;
            }

            return cache != null;
        }

        private static bool TryGetCloseMeleeThreat(Pawn pawn, bool isPredatorThreat, bool refreshIfNeeded, out Pawn threat)
        {
            threat = null;
            if (!TryGetThreatCache(pawn, refreshIfNeeded, out ThreatMapCacheData cache))
            {
                return false;
            }

            return TryGetCloseMeleeThreat(pawn, isPredatorThreat, cache, out threat);
        }

        private static bool TryGetCloseMeleeThreat(Pawn pawn, bool isPredatorThreat, ThreatMapCacheData cache, out Pawn threat)
        {
            threat = null;
            if (pawn == null || cache == null)
            {
                return false;
            }

            Dictionary<int, Pawn> threatsByPreyId = isPredatorThreat
                ? cache.CloseMeleePredatorThreatByPreyId
                : cache.CloseMeleeHumanlikeThreatByPreyId;
            return threatsByPreyId.TryGetValue(pawn.thingIDNumber, out threat) && threat != null;
        }

        private static bool TryGetCarrierExtension(Pawn pawn, out ModExtension_FleeFromCarrier extension)
        {
            return DefModExtensionCache<ModExtension_FleeFromCarrier>.TryGet(pawn, out extension);
        }

        private static Pawn FindNearestHumanlikeThreat(Pawn pawn, float radius)
        {
            if (pawn?.Map == null)
            {
                return null;
            }

            if (!TryGetThreatCache(pawn, refreshIfNeeded: true, out ThreatMapCacheData cache))
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                return TryConsumeFallbackThreatScanBudget(currentTick)
                    ? FindNearestHumanlikeThreatFallback(pawn, radius)
                    : null;
            }

            return FindNearestHumanlikeThreat(pawn, cache, radius);
        }

        private static Pawn FindNearestHumanlikeThreat(Pawn pawn, ThreatMapCacheData cache, float radius)
        {
            if (pawn?.Map == null || cache == null || !cache.HasHumanlikeThreats)
            {
                return null;
            }

            try
            {
                return FindNearestThreatInRange(
                    cache.HumanlikeThreatsByBucket,
                    pawn,
                    radius,
                    ThreatSearchMode.Humanlike,
                    0f,
                    out _);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestHumanlikeThreat failed: {ex}");
                return null;
            }
        }

        private static Pawn FindNearestHumanlikeThreatFallback(Pawn pawn, float radius)
        {
            if (pawn?.Map?.mapPawns?.AllPawnsSpawned == null)
            {
                return null;
            }

            Pawn nearestThreat = null;
            float bestDistanceSquared = radius * radius;
            IReadOnlyList<Pawn> pawns = pawn.Map.mapPawns.AllPawnsSpawned;
            IntVec3 pawnPosition = pawn.Position;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn threat = pawns[i];
                if (threat == null || !IsHumanlikeThreatCandidate(threat, pawn))
                {
                    continue;
                }

                float distanceSquared = (threat.Position - pawnPosition).LengthHorizontalSquared;
                if (distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                nearestThreat = threat;
                bestDistanceSquared = distanceSquared;
            }

            return nearestThreat;
        }

        private static Pawn FindNearestSmallPetRaiderThreat(Pawn pawn)
        {
            if (pawn?.Map == null)
            {
                return null;
            }

            try
            {
                if (!TryGetThreatCache(pawn, refreshIfNeeded: true, out ThreatMapCacheData cache))
                {
                    return null;
                }

                return FindNearestThreatInRange(
                    cache.HumanlikeThreatsByBucket,
                    pawn,
                    SmallPetRaiderThreatSearchRadius,
                    ThreatSearchMode.SmallPetRaider,
                    0f,
                    out _);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestSmallPetRaiderThreat failed: {ex}");
                return null;
            }
        }

        private static Pawn FindNearestCarrierThreat(Pawn pawn, out int fleeDistance)
        {
            fleeDistance = CarrierFleeDistanceDefault;
            if (pawn?.Map == null)
            {
                return null;
            }

            try
            {
                if (!TryGetThreatCache(pawn, refreshIfNeeded: true, out ThreatMapCacheData cache)
                    || cache.MaxCarrierThreatRadius <= 0f)
                {
                    return null;
                }

                float preyBodySize = pawn.BodySize;
                Pawn nearestThreat = FindNearestThreatInRange(
                    cache.CarrierThreatsByBucket,
                    pawn,
                    cache.MaxCarrierThreatRadius,
                    ThreatSearchMode.Carrier,
                    preyBodySize,
                    out ModExtension_FleeFromCarrier extension);

                if (nearestThreat == null)
                {
                    return null;
                }

                fleeDistance = extension?.fleeDistance ?? CarrierFleeDistanceDefault;
                return nearestThreat;
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestCarrierThreat failed: {ex}");
                return null;
            }
        }

        private static bool IsHumanlikeThreatCandidate(Pawn human, Pawn animal)
        {
            if (human == null || animal == null)
            {
                return false;
            }

            if (human == animal || !human.Spawned || human.Dead || human.Destroyed || human.Downed)
            {
                return false;
            }

            if (human.Map != animal.Map || !PawnThreatUtility.IsHumanlikeOrMechanoid(human))
            {
                return false;
            }

            if (human.RaceProps?.Humanlike == true && IsHumanDoingAnimalTamingJob(human))
            {
                return false;
            }

            return HasLineOfSightAndReach(human, animal);
        }

        private static bool IsSmallPetRaiderThreatCandidate(Pawn threat, Pawn pawn)
        {
            return threat != null
                && pawn != null
                && threat != pawn
                && threat.Spawned
                && !threat.Dead
                && !threat.Destroyed
                && !threat.Downed
                && threat.Map == pawn.Map
                && threat.RaceProps?.Humanlike == true
                && threat.HostileTo(Faction.OfPlayer)
                && HasLineOfSightAndReach(threat, pawn);
        }

        private static bool IsCarrierThreatCandidate(
            Pawn carrier,
            Pawn prey,
            float preyBodySize,
            float distanceSquared,
            out ModExtension_FleeFromCarrier extension)
        {
            extension = null;
            if (carrier == null || prey == null || carrier == prey)
            {
                return false;
            }

            if (!carrier.Spawned || carrier.Dead || carrier.Destroyed || carrier.Downed || carrier.Map != prey.Map)
            {
                return false;
            }

            if (!TryGetCarrierExtension(carrier, out extension) || extension.fleeRadius <= 0f)
            {
                return false;
            }

            if (distanceSquared > extension.fleeRadius * extension.fleeRadius)
            {
                return false;
            }

            if (SharesNonNullFaction(prey, carrier))
            {
                return false;
            }

            if (extension.fleeBodySizeLimit > 0f && preyBodySize > extension.fleeBodySizeLimit)
            {
                return false;
            }

            return HasLineOfSightOrReach(prey, carrier);
        }

        private static bool HasLineOfSightAndReach(Pawn threat, Pawn pawn)
        {
            if (threat == null || pawn == null || threat.Map == null || threat.Map != pawn.Map)
            {
                return false;
            }

            if (threat.Position == pawn.Position)
            {
                return true;
            }

            if (TryGetThreatVisibility(lineOfSightAndReachCacheByPairKey, threat, pawn, out bool cachedResult))
            {
                return cachedResult;
            }

            bool hasLineOfSight;
            try
            {
                hasLineOfSight = GenSight.LineOfSight(pawn.Position, threat.Position, pawn.Map);
            }
            catch
            {
                hasLineOfSight = false;
            }

            if (!hasLineOfSight)
            {
                StoreThreatVisibility(lineOfSightAndReachCacheByPairKey, threat, pawn, false);
                return false;
            }

            try
            {
                bool result = threat.CanReach(pawn, PathEndMode.Touch, Danger.Deadly);
                StoreThreatVisibility(lineOfSightAndReachCacheByPairKey, threat, pawn, result);
                return result;
            }
            catch
            {
                StoreThreatVisibility(lineOfSightAndReachCacheByPairKey, threat, pawn, false);
                return false;
            }
        }

        private static bool HasLineOfSightOrReach(Pawn pawn, Pawn threat)
        {
            if (threat == null || pawn == null || threat.Map == null || threat.Map != pawn.Map)
            {
                return false;
            }

            if (threat.Position == pawn.Position)
            {
                return true;
            }

            if (TryGetThreatVisibility(lineOfSightOrReachCacheByPairKey, threat, pawn, out bool cachedResult))
            {
                return cachedResult;
            }

            try
            {
                if (GenSight.LineOfSight(pawn.Position, threat.Position, pawn.Map))
                {
                    StoreThreatVisibility(lineOfSightOrReachCacheByPairKey, threat, pawn, true);
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                bool result = threat.CanReach(pawn, PathEndMode.Touch, Danger.Deadly);
                StoreThreatVisibility(lineOfSightOrReachCacheByPairKey, threat, pawn, result);
                return result;
            }
            catch
            {
                StoreThreatVisibility(lineOfSightOrReachCacheByPairKey, threat, pawn, false);
                return false;
            }
        }

        private static bool TryGetThreatVisibility(
            Dictionary<long, ThreatVisibilityCacheEntry> cache,
            Pawn threat,
            Pawn prey,
            out bool result)
        {
            result = false;
            if (cache == null || threat == null || prey == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            long pairKey = PairKey(threat, prey);
            return pairKey != 0L
                && currentTick > 0
                && cache.TryGetValue(pairKey, out ThreatVisibilityCacheEntry cached)
                && currentTick - cached.Tick <= ThreatVisibilityCacheDurationTicks
                && (result = cached.IsVisible) == cached.IsVisible;
        }

        private static void StoreThreatVisibility(
            Dictionary<long, ThreatVisibilityCacheEntry> cache,
            Pawn threat,
            Pawn prey,
            bool result)
        {
            if (cache == null || threat == null || prey == null)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            long pairKey = PairKey(threat, prey);
            if (pairKey == 0L || currentTick <= 0)
            {
                return;
            }

            cache[pairKey] = new ThreatVisibilityCacheEntry(result, currentTick);
            CleanupThreatCachesIfNeeded(currentTick);
        }

        private static bool SharesNonNullFaction(Pawn a, Pawn b)
        {
            return a?.Faction != null && b?.Faction != null && ReferenceEquals(a.Faction, b.Faction);
        }

        private static Pawn FindNearestPredatorThreat(Pawn pawn, float radius, bool allowNonHostilePredators)
        {
            if (pawn?.Map == null)
            {
                return null;
            }

            if (!TryGetThreatCache(pawn, refreshIfNeeded: true, out ThreatMapCacheData cache))
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                return TryConsumeFallbackThreatScanBudget(currentTick)
                    ? FindNearestPredatorThreatFallback(pawn, radius, allowNonHostilePredators)
                    : null;
            }

            return FindNearestPredatorThreat(pawn, cache, radius, allowNonHostilePredators);
        }

        private static Pawn FindNearestPredatorThreat(Pawn pawn, ThreatMapCacheData cache, float radius, bool allowNonHostilePredators)
        {
            if (pawn?.Map == null || cache == null)
            {
                return null;
            }

            try
            {
                if (!cache.HasActivePredatorThreats
                    && (!allowNonHostilePredators || !cache.HasPassivePredatorThreats))
                {
                    return null;
                }

                Pawn nearestActiveThreat = FindNearestThreatInRange(
                    cache.ActivePredatorThreatsByBucket,
                    pawn,
                    radius,
                    ThreatSearchMode.PredatorActive,
                    0f,
                    out _);

                if (nearestActiveThreat != null || !allowNonHostilePredators)
                {
                    return nearestActiveThreat;
                }

                float passiveRadius = radius * NonHostilePredatorSearchRadiusFactor;
                return FindNearestThreatInRange(
                    cache.PassivePredatorThreatsByBucket,
                    pawn,
                    passiveRadius,
                    ThreatSearchMode.PredatorPassive,
                    0f,
                    out _);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestPredatorThreat failed: {ex}");
                return null;
            }
        }

        private static Pawn FindNearestPredatorThreatFallback(Pawn pawn, float radius, bool allowNonHostilePredators)
        {
            if (pawn?.Map?.mapPawns?.AllPawnsSpawned == null)
            {
                return null;
            }

            float radiusSquared = radius * radius;
            float bestActiveDistanceSquared = radiusSquared;
            Pawn nearestActiveThreat = null;

            float passiveRadius = radius * NonHostilePredatorSearchRadiusFactor;
            float passiveRadiusSquared = passiveRadius * passiveRadius;
            float bestPassiveDistanceSquared = passiveRadiusSquared;
            Pawn nearestPassiveThreat = null;

            IReadOnlyList<Pawn> pawns = pawn.Map.mapPawns.AllPawnsSpawned;
            IntVec3 pawnPosition = pawn.Position;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn threat = pawns[i];
                if (threat == null
                    || !threat.Spawned
                    || threat.Dead
                    || threat.Destroyed
                    || threat.Downed
                    || threat.RaceProps?.predator != true)
                {
                    continue;
                }

                float distanceSquared = (threat.Position - pawnPosition).LengthHorizontalSquared;
                if (distanceSquared <= bestActiveDistanceSquared
                    && IsTrackedActivePredatorThreatStillValid(threat, pawn, radiusSquared, distanceSquared))
                {
                    nearestActiveThreat = threat;
                    bestActiveDistanceSquared = distanceSquared;
                    continue;
                }

                if (!allowNonHostilePredators || distanceSquared > bestPassiveDistanceSquared)
                {
                    continue;
                }

                if (IsTrackedPassivePredatorThreatStillValid(threat, pawn, passiveRadiusSquared, distanceSquared))
                {
                    nearestPassiveThreat = threat;
                    bestPassiveDistanceSquared = distanceSquared;
                }
            }

            if (nearestActiveThreat != null || !allowNonHostilePredators)
            {
                return nearestActiveThreat;
            }

            return nearestPassiveThreat;
        }

        private static Pawn FindNearestThreatInRange(
            Dictionary<int, List<Pawn>> threatsByBucket,
            Pawn prey,
            float radius,
            ThreatSearchMode mode,
            float preyBodySize,
            out ModExtension_FleeFromCarrier carrierExtension)
        {
            carrierExtension = null;
            if (threatsByBucket == null || prey?.Map == null || radius < 0f)
            {
                return null;
            }

            int radiusCeiling = (int)Math.Ceiling(radius);
            IntVec3 preyPosition = prey.Position;
            int minBucketX = Math.Max(0, (preyPosition.x - radiusCeiling) / ThreatBucketSize);
            int maxBucketX = Math.Max(0, (preyPosition.x + radiusCeiling) / ThreatBucketSize);
            int minBucketZ = Math.Max(0, (preyPosition.z - radiusCeiling) / ThreatBucketSize);
            int maxBucketZ = Math.Max(0, (preyPosition.z + radiusCeiling) / ThreatBucketSize);
            float radiusSquared = radius * radius;
            float bestDistanceSquared = radiusSquared;
            Pawn nearestThreat = null;

            for (int bucketZ = minBucketZ; bucketZ <= maxBucketZ; bucketZ++)
            {
                for (int bucketX = minBucketX; bucketX <= maxBucketX; bucketX++)
                {
                    if (!threatsByBucket.TryGetValue(MakeThreatBucketKey(bucketX, bucketZ), out List<Pawn> threats))
                    {
                        continue;
                    }

                    for (int i = 0; i < threats.Count; i++)
                    {
                        Pawn threat = threats[i];
                        if (threat == null)
                        {
                            continue;
                        }

                        float distanceSquared = (threat.Position - preyPosition).LengthHorizontalSquared;
                        if (distanceSquared > radiusSquared || distanceSquared >= bestDistanceSquared)
                        {
                            continue;
                        }

                        switch (mode)
                        {
                            case ThreatSearchMode.Humanlike:
                                if (!IsHumanlikeThreatCandidate(threat, prey))
                                {
                                    continue;
                                }
                                break;
                            case ThreatSearchMode.SmallPetRaider:
                                if (!IsSmallPetRaiderThreatCandidate(threat, prey))
                                {
                                    continue;
                                }
                                break;
                            case ThreatSearchMode.Carrier:
                                if (!IsCarrierThreatCandidate(threat, prey, preyBodySize, distanceSquared, out ModExtension_FleeFromCarrier extension))
                                {
                                    continue;
                                }
                                carrierExtension = extension;
                                break;
                            case ThreatSearchMode.PredatorActive:
                                if (!IsTrackedActivePredatorThreatStillValid(threat, prey, radiusSquared, distanceSquared))
                                {
                                    continue;
                                }
                                break;
                            case ThreatSearchMode.PredatorPassive:
                                if (!IsTrackedPassivePredatorThreatStillValid(threat, prey, radiusSquared, distanceSquared))
                                {
                                    continue;
                                }
                                break;
                        }

                        nearestThreat = threat;
                        bestDistanceSquared = distanceSquared;
                    }
                }
            }

            return nearestThreat;
        }

        private static bool HasThreatBucketsInRange(
            Dictionary<int, List<Pawn>> threatsByBucket,
            IntVec3 preyPosition,
            float radius)
        {
            if (threatsByBucket == null || threatsByBucket.Count == 0 || radius <= 0f)
            {
                return false;
            }

            int radiusCeiling = (int)Math.Ceiling(radius);
            int minBucketX = Math.Max(0, (preyPosition.x - radiusCeiling) / ThreatBucketSize);
            int maxBucketX = Math.Max(0, (preyPosition.x + radiusCeiling) / ThreatBucketSize);
            int minBucketZ = Math.Max(0, (preyPosition.z - radiusCeiling) / ThreatBucketSize);
            int maxBucketZ = Math.Max(0, (preyPosition.z + radiusCeiling) / ThreatBucketSize);

            for (int bucketZ = minBucketZ; bucketZ <= maxBucketZ; bucketZ++)
            {
                for (int bucketX = minBucketX; bucketX <= maxBucketX; bucketX++)
                {
                    if (threatsByBucket.ContainsKey(MakeThreatBucketKey(bucketX, bucketZ)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int MakeThreatBucketKey(int bucketX, int bucketZ)
        {
            return ((bucketZ & 0xFFFF) << 16) | (bucketX & 0xFFFF);
        }

        private static bool IsTrackedActivePredatorThreatStillValid(Pawn predator, Pawn prey, float radius)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            float distanceSquared = (predator.Position - prey.Position).LengthHorizontalSquared;
            return IsTrackedActivePredatorThreatStillValid(predator, prey, radius * radius, distanceSquared);
        }

        private static bool IsTrackedActivePredatorThreatStillValid(Pawn predator, Pawn prey, float radiusSquared, float distanceSquared)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            return predator.Spawned
                && !predator.Dead
                && !predator.Destroyed
                && !predator.Downed
                && predator.Map == prey.Map
                && predator.RaceProps?.predator == true
                && TryGetThreatTargetPawn(predator, out _)
                && distanceSquared <= radiusSquared;
        }

        private static bool IsTrackedPassivePredatorThreatStillValid(Pawn predator, Pawn prey, float radius)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            float distanceSquared = (predator.Position - prey.Position).LengthHorizontalSquared;
            return IsTrackedPassivePredatorThreatStillValid(predator, prey, radius * radius, distanceSquared);
        }

        private static bool IsTrackedPassivePredatorThreatStillValid(Pawn predator, Pawn prey, float radiusSquared, float distanceSquared)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            return predator.Spawned
                && !predator.Dead
                && !predator.Destroyed
                && !predator.Downed
                && predator.Map == prey.Map
                && predator.RaceProps?.predator == true
                && distanceSquared <= radiusSquared
                && IsAcceptablePrey(predator, prey);
        }

        private static void CleanupThreatCachesIfNeeded(int currentTick)
        {
            if (currentTick - lastThreatCacheCleanupTick < ThreatCacheCleanupIntervalTicks)
            {
                return;
            }

            lastThreatCacheCleanupTick = currentTick;
            CleanupNoThreatScanCache(currentTick);
            CleanupThreatVisibilityCache(lineOfSightAndReachCacheByPairKey, currentTick);
            CleanupThreatVisibilityCache(lineOfSightOrReachCacheByPairKey, currentTick);
            CleanupNearbyThreatBucketsCache(currentTick);
            CleanupNearestPredatorThreatCache(currentTick);
            ThreatMapCache.CleanupStaleMaps();
            CleanupThreatScanBudget();
        }

        private static void CleanupNoThreatScanCache(int currentTick)
        {
            List<int> staleKeys = null;
            foreach (KeyValuePair<int, NoThreatScanCacheEntry> entry in noThreatScanCacheByPawnId)
            {
                if (currentTick < entry.Value.NextScanTick)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<int>(32);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                noThreatScanCacheByPawnId.Remove(staleKeys[i]);
            }
        }

        private static void CleanupThreatVisibilityCache(Dictionary<long, ThreatVisibilityCacheEntry> cache, int currentTick)
        {
            List<long> staleKeys = null;
            foreach (KeyValuePair<long, ThreatVisibilityCacheEntry> entry in cache)
            {
                if (currentTick - entry.Value.Tick <= ThreatVisibilityCacheDurationTicks)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<long>(64);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                cache.Remove(staleKeys[i]);
            }
        }

        private static void CleanupNearbyThreatBucketsCache(int currentTick)
        {
            if (nearbyThreatBucketsCacheByBucketKey.Count == 0)
            {
                return;
            }

            List<NearbyThreatBucketCacheKey> staleKeys = null;
            foreach (KeyValuePair<NearbyThreatBucketCacheKey, NearbyThreatBucketsCacheEntry> entry in nearbyThreatBucketsCacheByBucketKey)
            {
                if (currentTick - entry.Value.Tick <= NearbyThreatBucketsCacheDurationTicks)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<NearbyThreatBucketCacheKey>(64);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                nearbyThreatBucketsCacheByBucketKey.Remove(staleKeys[i]);
            }
        }

        private static void CleanupNearestPredatorThreatCache(int currentTick)
        {
            if (nearestPredatorThreatCacheByPawnId.Count == 0)
            {
                return;
            }

            List<int> staleKeys = null;
            foreach (KeyValuePair<int, NearestPredatorThreatCacheEntry> entry in nearestPredatorThreatCacheByPawnId)
            {
                if (currentTick - entry.Value.Tick <= NearestPredatorThreatCacheDurationTicks)
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<int>(64);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                nearestPredatorThreatCacheByPawnId.Remove(staleKeys[i]);
            }
        }

        private static void CleanupThreatScanBudget()
        {
            if (threatScanBudgetByMapId.Count == 0)
            {
                return;
            }

            List<Map> maps = Find.Maps;
            if (maps == null || maps.Count == 0)
            {
                threatScanBudgetByMapId.Clear();
                return;
            }

            HashSet<int> activeMapIds = new HashSet<int>();
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map != null)
                {
                    activeMapIds.Add(map.uniqueID);
                }
            }

            List<int> staleKeys = null;
            foreach (KeyValuePair<int, ThreatScanBudgetEntry> entry in threatScanBudgetByMapId)
            {
                if (activeMapIds.Contains(entry.Key))
                {
                    continue;
                }

                if (staleKeys == null)
                {
                    staleKeys = new List<int>(4);
                }

                staleKeys.Add(entry.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                threatScanBudgetByMapId.Remove(staleKeys[i]);
            }
        }

        private static bool TryConsumeThreatScanBudget(Map map, int currentTick)
        {
            if (map == null || currentTick <= 0)
            {
                return true;
            }

            int mapId = map.uniqueID;
            if (!threatScanBudgetByMapId.TryGetValue(mapId, out ThreatScanBudgetEntry budget))
            {
                budget = new ThreatScanBudgetEntry();
                threatScanBudgetByMapId[mapId] = budget;
            }

            if (budget.Tick != currentTick)
            {
                budget.Tick = currentTick;
                budget.Remaining = CalculateThreatScanBudget(map);
            }

            if (budget.Remaining <= 0)
            {
                return false;
            }

            budget.Remaining--;
            return true;
        }

        private static bool TryConsumeFallbackThreatScanBudget(int currentTick)
        {
            if (currentTick <= 0)
            {
                return false;
            }

            if (fallbackThreatScanBudgetTick != currentTick)
            {
                fallbackThreatScanBudgetTick = currentTick;
                fallbackThreatScanBudgetRemaining = FallbackThreatScanBudgetPerTick;
            }

            if (fallbackThreatScanBudgetRemaining <= 0)
            {
                return false;
            }

            fallbackThreatScanBudgetRemaining--;
            return true;
        }

        private static int CalculateThreatScanBudget(Map map)
        {
            int animalCount = 0;
            if (map != null && ThreatMapCache.TryGetLastKnown(map, out ThreatMapCacheData data) && data != null)
            {
                animalCount = data.AnimalCount;
            }
            else if (map?.mapPawns?.AllPawnsSpawned != null)
            {
                animalCount = map.mapPawns.AllPawnsSpawned.Count;
            }

            int budget = ThreatScanBudgetBase + (animalCount / 50) * ThreatScanBudgetPer50Animals;
            if (budget < ThreatScanBudgetMin)
            {
                budget = ThreatScanBudgetMin;
            }

            if (budget > ThreatScanBudgetMax)
            {
                budget = ThreatScanBudgetMax;
            }

            return budget;
        }

        private static bool IsActivePredatorThreat(Pawn predator, Pawn prey, out bool targetsPreyDirectly)
        {
            targetsPreyDirectly = false;
            if (!TryGetThreatTargetPawn(predator, out Pawn targetedPawn))
            {
                return false;
            }

            targetsPreyDirectly = ReferenceEquals(targetedPawn, prey);
            return true;
        }

        private static bool IsThreatTargetingPawn(Pawn predator, Pawn prey)
        {
            return IsActivePredatorThreat(predator, prey, out bool targetsPreyDirectly) && targetsPreyDirectly;
        }

        private static bool TryGetThreatTargetPawn(Pawn predator, out Pawn targetPawn)
        {
            targetPawn = null;

            Job curJob = predator?.CurJob;
            if (curJob == null || !curJob.targetA.HasThing || !IsThreatJob(curJob, predator.jobs?.curDriver))
            {
                return false;
            }

            targetPawn = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
            return targetPawn != null && !targetPawn.Dead;
        }

        private static bool IsThreatJob(Job curJob, JobDriver curDriver)
        {
            if (curJob == null || curJob.def == null)
            {
                return false;
            }

            if (curJob.def == JobDefOf.AttackMelee)
            {
                return true;
            }

            Type driverClass = curJob.def.driverClass;
            if (driverClass != null && typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverClass))
            {
                return true;
            }

            return ProtectPreyState.IsProtectPreyJob(curJob, curDriver);
        }

        private static bool IsAcceptablePrey(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            if (PredationDecisionCache.TryGetAcceptablePrey(predator, prey, out bool cachedAcceptable))
            {
                return cachedAcceptable;
            }

            try
            {
                return FoodUtility.IsAcceptablePreyFor(predator, prey);
            }
            catch
            {
                return true;
            }
        }

        private static long PairKey(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return 0L;
            }

            uint predatorId = (uint)predator.thingIDNumber;
            uint preyId = (uint)prey.thingIDNumber;
            return ((long)predatorId << 32) | preyId;
        }

        private static void HandlePursuitAllowanceIfNeeded(Pawn predator, Pawn prey)
        {
            try
            {
                var comp = ZoologyPursuitGameComponent.Instance;
                if (comp == null)
                {
                    TryEnsurePursuitComponentRegistered();
                    comp = ZoologyPursuitGameComponent.Instance;
                    if (comp == null)
                    {
                        if (Prefs.DevMode)
                        {
                            Log.Message("[Zoology] WARNING: ZoologyPursuitGameComponent.Instance is null - cannot AllowPursuit.");
                        }
                        return;
                    }
                }

                if (comp.IsPairAllowedNow(predator, prey) || comp.IsPairBlockedNow(predator, prey))
                {
                    return;
                }

                float predatorSpeed = predator.GetStatValue(StatDefOf.MoveSpeed, true);
                float preySpeed = prey.GetStatValue(StatDefOf.MoveSpeed, true);
                if (preySpeed <= predatorSpeed)
                {
                    return;
                }

                comp.AllowPursuit(predator, prey, 2);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] HandlePursuitAllowanceIfNeeded error: {ex}");
            }
        }

        private static void TryEnsurePursuitComponentRegistered()
        {
            try
            {
                if (ZoologyPursuitGameComponent.Instance != null)
                {
                    return;
                }

                Game game = Current.Game;
                if (game?.components == null)
                {
                    return;
                }

                bool exists = false;
                for (int i = 0; i < game.components.Count; i++)
                {
                    if (game.components[i] is ZoologyPursuitGameComponent)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    game.components.Add(new ZoologyPursuitGameComponent(game));
                    Log.Message("[Zoology] Dynamically added ZoologyPursuitGameComponent to Current.Game.components.");
                }

                var _ = ZoologyPursuitGameComponent.Instance;
            }
            catch (Exception exAdd)
            {
                Log.Error($"[Zoology] Failed to dynamically add ZoologyPursuitGameComponent: {exAdd}");
            }
        }

        private static bool IsPhotonozoaPairInTheirFaction(Pawn a, Pawn b)
        {
            try
            {
                if (a == null || b == null)
                {
                    return false;
                }

                return ZoologyCacheUtility.IsPhotonozoaPairInTheirFaction(a, b);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] IsPhotonozoaPairInTheirFaction error: {ex}");
                return false;
            }
        }
    }

}
