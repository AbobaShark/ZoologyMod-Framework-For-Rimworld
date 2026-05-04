using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

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

    [HarmonyPatch(typeof(JobGiver_ReactToCloseMeleeThreat), "TryGiveJob")]
    internal static class Patch_ReactToCloseMeleeThreat_Fallback
    {
        private static bool Prepare() => Patch_AnimalFleeFromPredators.Prepare();

        private static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result?.def != JobDefOf.AttackMelee)
            {
                return;
            }

            Pawn threat = __result.targetA.Thing as Pawn;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (Patch_SmallPetThreatDisabled.ShouldPreventSmallPetMeleeRetaliation(pawn, threat, currentTick))
            {
                if (pawn?.mindState != null)
                {
                    pawn.mindState.meleeThreat = null;
                }

                __result = null;
            }
        }
    }

    internal sealed class JobGiver_AnimalFlee_Zoology : JobGiver_AnimalFlee
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            Job result = base.TryGiveJob(pawn);
            Patch_AnimalFleeFromPredators.ApplyZoologyFleeLogic(this, pawn, ref result);
            return result;
        }
    }

    [StaticConstructorOnStartup]
    internal static class ZoologyAnimalFleeThinkTreeInjector
    {
        private static readonly FieldInfo ThinkNodeSubNodesField = AccessTools.Field(typeof(ThinkNode), "subNodes");
        private static readonly FieldInfo ThinkNodeParentField = AccessTools.Field(typeof(ThinkNode), "parent");

        static ZoologyAnimalFleeThinkTreeInjector()
        {
            TryReplaceJobGivers();
        }

        private static void TryReplaceJobGivers()
        {
            try
            {
                int replaced = 0;
                List<ThinkTreeDef> defs = DefDatabase<ThinkTreeDef>.AllDefsListForReading;
                if (defs != null)
                {
                    for (int i = 0; i < defs.Count; i++)
                    {
                        ThinkTreeDef def = defs[i];
                        if (def?.thinkRoot == null)
                        {
                            continue;
                        }

                        replaced += ReplaceInNode(def.thinkRoot, null);
                    }
                }

                if (Prefs.DevMode && replaced > 0)
                {
                    Log.Message($"[Zoology] Replaced {replaced} JobGiver_AnimalFlee nodes with Zoology version.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] ThinkTree replacement failed: {ex}");
            }
        }

        private static int ReplaceInNode(ThinkNode node, ThinkNode parent)
        {
            if (node == null)
            {
                return 0;
            }

            ThinkNode current = node;
            int replaced = 0;

            if (node is JobGiver_AnimalFlee && !(node is JobGiver_AnimalFlee_Zoology))
            {
                JobGiver_AnimalFlee_Zoology replacement = new JobGiver_AnimalFlee_Zoology();
                CopyInstanceFields(node, replacement);

                if (parent != null)
                {
                    List<ThinkNode> parentSubNodes = GetSubNodes(parent);
                    if (parentSubNodes != null)
                    {
                        int index = parentSubNodes.IndexOf(node);
                        if (index >= 0)
                        {
                            parentSubNodes[index] = replacement;
                        }
                    }

                    SetParent(replacement, parent);
                }

                current = replacement;
                replaced++;
            }

            List<ThinkNode> subNodes = GetSubNodes(current);
            if (subNodes == null || subNodes.Count == 0)
            {
                return replaced;
            }

            for (int i = 0; i < subNodes.Count; i++)
            {
                ThinkNode child = subNodes[i];
                if (child == null)
                {
                    continue;
                }

                SetParent(child, current);
                replaced += ReplaceInNode(child, current);
            }

            return replaced;
        }

        private static List<ThinkNode> GetSubNodes(ThinkNode node)
        {
            return ThinkNodeSubNodesField?.GetValue(node) as List<ThinkNode>;
        }

        private static void SetParent(ThinkNode node, ThinkNode parent)
        {
            if (ThinkNodeParentField != null)
            {
                ThinkNodeParentField.SetValue(node, parent);
            }
        }

        private static void CopyInstanceFields(object source, object destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            Type type = source.GetType();
            while (type != null && type != typeof(object))
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic)
                    {
                        continue;
                    }

                    field.SetValue(destination, field.GetValue(source));
                }

                type = type.BaseType;
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
                int tick,
                int expireTick)
            {
                MapId = mapId;
                MapRefreshTick = mapRefreshTick;
                BucketX = bucketX;
                BucketZ = bucketZ;
                AllowNonHostilePredators = allowNonHostilePredators;
                Threat = threat;
                HasThreat = hasThreat;
                Tick = tick;
                ExpireTick = expireTick;
            }

            public int MapId { get; }
            public int MapRefreshTick { get; }
            public int BucketX { get; }
            public int BucketZ { get; }
            public bool AllowNonHostilePredators { get; }
            public Pawn Threat { get; }
            public bool HasThreat { get; }
            public int Tick { get; }
            public int ExpireTick { get; }
        }

        private readonly struct PawnFleeDecisionCacheEntry
        {
            public PawnFleeDecisionCacheEntry(int tick, int mapId, bool hasJob)
            {
                Tick = tick;
                MapId = mapId;
                HasJob = hasJob;
            }

            public int Tick { get; }
            public int MapId { get; }
            public bool HasJob { get; }
        }

        private readonly struct TargetedPredatorEntry
        {
            public TargetedPredatorEntry(Pawn predator, Pawn prey)
            {
                Predator = predator;
                Prey = prey;
            }

            public Pawn Predator { get; }
            public Pawn Prey { get; }
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
            public readonly Dictionary<int, List<Pawn>> SmallPetRaiderThreatsByBucket = new Dictionary<int, List<Pawn>>(32);
            public readonly Dictionary<int, List<Pawn>> ActivePredatorThreatsByBucket = new Dictionary<int, List<Pawn>>(32);
            public readonly Dictionary<int, List<Pawn>> PassivePredatorThreatsByBucket = new Dictionary<int, List<Pawn>>(32);
            public readonly Dictionary<int, List<Pawn>> CarrierThreatsByBucket = new Dictionary<int, List<Pawn>>(16);
            public readonly Dictionary<int, Pawn> CloseMeleeHumanlikeThreatByPreyId = new Dictionary<int, Pawn>(16);
            public readonly Dictionary<int, Pawn> CloseMeleePredatorThreatByPreyId = new Dictionary<int, Pawn>(16);
            public float MaxCarrierThreatRadius;
            public int AnimalCount;
            public int ScanIntervalTicks = 1;
            public int RefreshIntervalTicks = ThreatMapRefreshIntervalTicks;
            public bool HasHumanlikeThreats;
            public bool HasSmallPetRaiderThreats;
            public bool HasActivePredatorThreats;
            public bool HasPassivePredatorThreats;
            public bool HasCarrierThreats;
            public bool HasCloseMeleeHumanlikeThreats;
            public bool HasCloseMeleePredatorThreats;

            public void Clear()
            {
                ThreatMapCache.ReturnBucketLists(HumanlikeThreatsByBucket);
                ThreatMapCache.ReturnBucketLists(SmallPetRaiderThreatsByBucket);
                ThreatMapCache.ReturnBucketLists(ActivePredatorThreatsByBucket);
                ThreatMapCache.ReturnBucketLists(PassivePredatorThreatsByBucket);
                ThreatMapCache.ReturnBucketLists(CarrierThreatsByBucket);
                CloseMeleeHumanlikeThreatByPreyId.Clear();
                CloseMeleePredatorThreatByPreyId.Clear();
                MaxCarrierThreatRadius = 0f;
                AnimalCount = 0;
                ScanIntervalTicks = 1;
                RefreshIntervalTicks = ThreatMapRefreshIntervalTicks;
                HasHumanlikeThreats = false;
                HasSmallPetRaiderThreats = false;
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

            public static void ClearAll()
            {
                foreach (KeyValuePair<int, ThreatMapCacheData> entry in byMapId)
                {
                    entry.Value?.Clear();
                }

                byMapId.Clear();
            }

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

                return currentTick > 0 && currentTick - data.RefreshTick < data.RefreshIntervalTicks;
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

                if (currentTick > 0 && currentTick - data.RefreshTick < data.RefreshIntervalTicks)
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

                    if (IsPotentialSmallPetRaiderThreatSource(threat))
                    {
                        data.HasSmallPetRaiderThreats = true;
                        AddSmallPetRaiderThreatInfluence(data, threat);
                    }

                    if (TryGetCarrierExtension(threat, out ModExtension_FleeFromCarrier carrierExtension)
                        && carrierExtension.fleeRadius > 0f)
                    {
                        data.HasCarrierThreats = true;
                        AddCarrierThreatInfluence(data, threat, carrierExtension);
                    }

                    if (!IsPotentialPredatorThreatSource(threat))
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
                data.RefreshIntervalTicks = CalculateThreatMapRefreshIntervalTicks(data.AnimalCount);
            }

            private static void AddHumanlikeThreatInfluence(ThreatMapCacheData data, Pawn threat)
            {
                AddThreat(data.HumanlikeThreatsByBucket, threat);

                Pawn targetPawn = GetThreatTargetPawnOrNull(threat);
                if (ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, targetPawn)
                    && MarkCloseMeleeThreat(data.CloseMeleeHumanlikeThreatByPreyId, targetPawn, threat))
                {
                    data.HasCloseMeleeHumanlikeThreats = true;
                }
            }

            private static void AddSmallPetRaiderThreatInfluence(ThreatMapCacheData data, Pawn threat)
            {
                AddThreat(data.SmallPetRaiderThreatsByBucket, threat);
            }

            private static void AddActivePredatorThreatInfluence(ThreatMapCacheData data, Pawn threat, Pawn targetPawn)
            {
                AddThreat(data.ActivePredatorThreatsByBucket, threat);

                if (targetPawn != null
                    && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, targetPawn)
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

            private static int CalculateThreatMapRefreshIntervalTicks(int animalCount)
            {
                if (animalCount >= 1200) return 360;
                if (animalCount >= 640) return 300;
                if (animalCount >= 320) return 240;
                if (animalCount >= 160) return 180;
                if (animalCount >= 80) return 120;
                return ThreatMapRefreshIntervalTicks;
            }
        }

        private static readonly Dictionary<int, NoThreatScanCacheEntry> noThreatScanCacheByPawnId = new Dictionary<int, NoThreatScanCacheEntry>(128);
        private static readonly Dictionary<long, ThreatVisibilityCacheEntry> lineOfSightAndReachCacheByPairKey = new Dictionary<long, ThreatVisibilityCacheEntry>(512);
        private static readonly Dictionary<long, ThreatVisibilityCacheEntry> reachabilityCacheByPairKey = new Dictionary<long, ThreatVisibilityCacheEntry>(256);
        private static readonly Dictionary<NearbyThreatBucketCacheKey, NearbyThreatBucketsCacheEntry> nearbyThreatBucketsCacheByBucketKey = new Dictionary<NearbyThreatBucketCacheKey, NearbyThreatBucketsCacheEntry>(256);
        private static readonly Dictionary<int, NearestPredatorThreatCacheEntry> nearestPredatorThreatCacheByPawnId = new Dictionary<int, NearestPredatorThreatCacheEntry>(256);
        private static readonly Dictionary<int, ThreatScanBudgetEntry> threatScanBudgetByMapId = new Dictionary<int, ThreatScanBudgetEntry>(8);
        private static readonly Dictionary<int, PawnFleeDecisionCacheEntry> pawnFleeDecisionCacheByPawnId = new Dictionary<int, PawnFleeDecisionCacheEntry>(512);
        private static readonly Dictionary<JobDef, bool> foodJobByDefCache = new Dictionary<JobDef, bool>(64);
        private static readonly Dictionary<Type, bool> foodJobDriverByTypeCache = new Dictionary<Type, bool>(64);
        private static readonly Dictionary<JobDef, bool> tamingJobByDefCache = new Dictionary<JobDef, bool>(64);
        private static readonly Dictionary<Type, bool> tamingJobDriverByTypeCache = new Dictionary<Type, bool>(32);
        private static readonly Dictionary<Type, bool> predatorHuntDriverByTypeCache = new Dictionary<Type, bool>(32);
        private static readonly List<ProtectPreyMapCache.Entry> protectPreyThreatEntriesScratch = new List<ProtectPreyMapCache.Entry>(16);
        private static int protectPreyThreatEntriesTick = -1;
        private static int protectPreyThreatEntriesMapId = -1;
        private static readonly List<TargetedPredatorEntry> targetedPredatorEntriesScratch = new List<TargetedPredatorEntry>(16);
        private static int targetedPredatorEntriesTick = -1;
        private static int targetedPredatorEntriesMapId = -1;
        private static readonly List<TargetedPredatorEntry> targetedProtectYoungEntriesScratch = new List<TargetedPredatorEntry>(16);
        private static int targetedProtectYoungEntriesTick = -1;
        private static int targetedProtectYoungEntriesMapId = -1;

        private const float VanillaThreatSearchRadius = 18f;
        private const int VanillaFleeDistance = 24;
        private const int ConfigurableSearchRadiusMin = 6;
        private const int ConfigurableSearchRadiusMax = 24;
        private const int ConfigurableFleeDistanceMin = 6;
        private const int ConfigurableFleeDistanceMax = 40;
        private static readonly int CarrierExtensionDefaultFleeDistance = new ModExtension_FleeFromCarrier().fleeDistance ?? 16;
        private const int NoThreatScanCooldownTicks = ZoologyTickLimiter.FleeThreat.NoThreatScanCooldownTicks;
        private const int ThreatMapRefreshIntervalTicks = ZoologyTickLimiter.FleeThreat.ThreatMapRefreshIntervalTicks;
        private const int ThreatVisibilityCacheDurationTicks = ZoologyTickLimiter.FleeThreat.ThreatVisibilityCacheDurationTicks;
        private const int NearbyThreatBucketsCacheDurationTicks = ZoologyTickLimiter.FleeThreat.NearbyThreatBucketsCacheDurationTicks;
        private const int NearestPredatorThreatCacheDurationTicks = ZoologyTickLimiter.FleeThreat.NearestPredatorThreatCacheDurationTicks;
        private const int ThreatCacheCleanupIntervalTicks = ZoologyTickLimiter.FleeThreat.ThreatCacheCleanupIntervalTicks;
        private const int ThreatBucketSize = 16;
        private const int MinThreatScanIntervalTicks = ZoologyTickLimiter.FleeThreat.MinThreatScanIntervalTicks;
        private const int ThreatScanBudgetMin = 8;
        private const int ThreatScanBudgetBase = 12;
        private const int ThreatScanBudgetPer50Animals = 4;
        private const int ThreatScanBudgetMax = 96;
        private const int ThreatScanBudgetCooldownTicks = ZoologyTickLimiter.FleeThreat.ThreatScanBudgetCooldownTicks;
        private const int FallbackThreatScanBudgetPerTick = ZoologyTickLimiter.FleeThreat.FallbackThreatScanBudgetPerTick;
        private const string PhotonozoaFactionDefName = "Photonozoa";

        private static int lastThreatCacheCleanupTick = -ThreatCacheCleanupIntervalTicks;
        private static int lastFreshThreatCacheTick = int.MinValue;
        private static int lastFreshThreatCacheMapId = int.MinValue;
        private static bool lastFreshThreatCacheWasFresh;
        private static ThreatMapCacheData lastFreshThreatCacheData;
        private static int fallbackThreatScanBudgetTick = -1;
        private static int fallbackThreatScanBudgetRemaining = 0;
        private static Game runtimeCacheGame;
        private static int runtimeCacheLastTick = -1;
        private static FactionDef photonozoaFactionDefCached = DefDatabase<FactionDef>.GetNamedSilentFail(PhotonozoaFactionDefName);

        private static float ClampSearchRadius(int radius)
        {
            if (radius < ConfigurableSearchRadiusMin)
            {
                return ConfigurableSearchRadiusMin;
            }

            if (radius > ConfigurableSearchRadiusMax)
            {
                return ConfigurableSearchRadiusMax;
            }

            return radius;
        }

        private static int ClampFleeDistance(int distance)
        {
            if (distance < ConfigurableFleeDistanceMin)
            {
                return ConfigurableFleeDistanceMin;
            }

            if (distance > ConfigurableFleeDistanceMax)
            {
                return ConfigurableFleeDistanceMax;
            }

            return distance;
        }

        private static float GetPredatorSearchRadius()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            int radius = settings?.PredatorSearchRadius ?? 18;
            return ClampSearchRadius(radius);
        }

        private static float GetNonHostilePredatorSearchRadius()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            int radius = settings?.NonHostilePredatorSearchRadius ?? 12;
            return ClampSearchRadius(radius);
        }

        private static float GetHumanSearchRadius()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            int radius = settings?.HumanSearchRadius ?? 12;
            return ClampSearchRadius(radius);
        }

        private static float GetSmallPetRaiderThreatSearchRadius()
        {
            return VanillaThreatSearchRadius;
        }

        private static int GetFleeDistancePredator()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            int distance = settings?.FleeDistancePredator ?? 16;
            return ClampFleeDistance(distance);
        }

        private static int GetFleeDistanceTargetPredator()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            int distance = settings?.FleeDistanceTargetPredator ?? 24;
            return ClampFleeDistance(distance);
        }

        private static int GetFleeDistanceHuman()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            int distance = settings?.FleeDistanceHuman ?? 16;
            return ClampFleeDistance(distance);
        }

        private static int GetSmallPetRaiderFleeDistance()
        {
            return VanillaFleeDistance;
        }

        private static int GetCarrierFleeDistance(ModExtension_FleeFromCarrier extension)
        {
            int configured = extension?.fleeDistance ?? CarrierExtensionDefaultFleeDistance;
            if (configured <= 0)
            {
                configured = CarrierExtensionDefaultFleeDistance;
            }

            return configured > 0 ? configured : VanillaFleeDistance;
        }

        private static int GetPredatorFleeDistanceForThreat(Pawn pawn, Pawn threat)
        {
            if (pawn == null || threat == null)
            {
                return GetFleeDistanceTargetPredator();
            }

            float activeRadius = GetPredatorSearchRadius();
            if (IsTrackedActivePredatorThreatStillValid(threat, pawn, activeRadius))
            {
                return GetFleeDistanceTargetPredator();
            }

            float passiveRadius = GetNonHostilePredatorSearchRadius();
            if (IsTrackedPassivePredatorThreatStillValid(threat, pawn, passiveRadius))
            {
                return GetFleeDistancePredator();
            }

            return GetFleeDistanceTargetPredator();
        }

        private static bool IsPhotonozoaFaction(Faction faction)
        {
            FactionDef factionDef = faction?.def;
            if (factionDef == null)
            {
                return false;
            }

            if (photonozoaFactionDefCached == null)
            {
                photonozoaFactionDefCached = DefDatabase<FactionDef>.GetNamedSilentFail(PhotonozoaFactionDefName);
            }

            return factionDef == photonozoaFactionDefCached
                || string.Equals(factionDef.defName, PhotonozoaFactionDefName, StringComparison.Ordinal);
        }

        private static bool IsFactionNullOrPhotonozoa(Faction faction)
        {
            if (faction == null)
            {
                return true;
            }

            return IsPhotonozoaFaction(faction);
        }

        private static bool CanUsePhotonozoaPredatorException(Pawn prey, Pawn threat)
        {
            if (prey == null || threat == null)
            {
                return false;
            }

            bool preyPhotonozoa = ZoologyCacheUtility.IsPhotonozoa(prey.def) || IsPhotonozoaFaction(prey.Faction);
            bool threatPhotonozoa = ZoologyCacheUtility.IsPhotonozoa(threat.def) || IsPhotonozoaFaction(threat.Faction);
            if (!preyPhotonozoa || !threatPhotonozoa)
            {
                return false;
            }

            if (!IsFactionNullOrPhotonozoa(prey.Faction) || !IsFactionNullOrPhotonozoa(threat.Faction))
            {
                return false;
            }

            return threat.RaceProps?.predator == true;
        }

        private static bool CanPhotonozoaPreyReactToPredatorFlee(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Destroyed || pawn.Downed)
            {
                return false;
            }

            if (pawn.InMentalState)
            {
                return false;
            }

            if (pawn.GetLord() != null)
            {
                return false;
            }

            return !ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn);
        }

        private static bool IsFactionNullOrHostileToPrey(Pawn threat, Pawn prey)
        {
            if (threat == null || prey == null)
            {
                return false;
            }

            Faction threatFaction = threat.Faction;
            if (threatFaction == null)
            {
                return true;
            }

            Faction preyFaction = prey.Faction;
            if (preyFaction == null)
            {
                return true;
            }

            if (ReferenceEquals(threatFaction, preyFaction))
            {
                return false;
            }

            try
            {
                FactionRelation relation = threatFaction.RelationWith(preyFaction, allowNull: true);
                return relation == null || relation.kind == FactionRelationKind.Hostile;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidNonTargetPredatorFactionContext(Pawn predator, Pawn prey)
        {
            return IsFactionNullOrHostileToPrey(predator, prey)
                || CanUsePhotonozoaPredatorException(prey, predator);
        }

        private static bool ShouldBlockFleeInMeleeForPawn(Pawn pawn)
        {
            return pawn != null;
        }

        private static bool IsPotentialPredatorThreatSource(Pawn threat)
        {
            return threat?.RaceProps?.predator == true;
        }

        private static bool IsPredatorThreatSourceForPrey(Pawn predator, Pawn prey)
        {
            return predator != null
                && prey != null
                && predator.RaceProps?.predator == true;
        }

        public static bool Prepare()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null
                || settings.EnablePreyFleeFromPredators
                || settings.AnimalsFreeFromHumans
                || settings.EnableFleeFromCarrier
                || settings.EnablePredatorDefendCorpse
                || settings.EnableAnimalChildcare
                || settings.EnableIgnoreSmallPetsByRaiders;
        }

        public static void Postfix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            if (__instance is JobGiver_AnimalFlee_Zoology)
            {
                return;
            }

            ApplyZoologyFleeLogic(__instance, pawn, ref __result);
        }

        internal static void ApplyZoologyFleeLogic(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                bool fleeFromPredatorsEnabled = settings == null || settings.EnablePreyFleeFromPredators;
                bool fleeFromHumansEnabled = settings == null || settings.AnimalsFreeFromHumans;
                bool fleeFromCarriersEnabled = settings == null || settings.EnableFleeFromCarrier;
                bool fleeFromRaidersForSmallPetsEnabled = settings != null
                    && settings.EnableIgnoreSmallPetsByRaiders;
                bool fleeFromNonTargetPredatorsEnabled = settings == null
                    || (settings.EnablePreyFleeFromPredators && settings.AnimalsFleeFromNonHostlePredators);
                bool fleeFromDefendCorpseEnabled = settings == null || settings.EnablePredatorDefendCorpse;
                bool fleeFromProtectYoungEnabled = settings == null || settings.EnableAnimalChildcare;
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                EnsureRuntimeCacheState(currentTick);

                RaceProperties raceProps = pawn?.RaceProps;
                if (pawn == null || pawn.Map == null || raceProps == null || !raceProps.Animal || pawn.Dead || pawn.Destroyed)
                {
                    return;
                }

                bool shouldBlockFleeInMelee = ShouldBlockFleeInMeleeForPawn(pawn);
                Pawn meleeAttacker = null;
                bool underMeleeAttack = shouldBlockFleeInMelee
                    && ZoologyFleeSafetyUtility.TryGetMeleeAttackerOnPawn(pawn, out meleeAttacker);
                if (!underMeleeAttack && shouldBlockFleeInMelee)
                {
                    underMeleeAttack = IsFleeJobThreatMeleeAttackingPawn(pawn, __result)
                        || IsFleeJobThreatMeleeAttackingPawn(pawn, pawn.jobs?.curJob);
                }
                if (underMeleeAttack)
                {
                    if (__result?.def == JobDefOf.Flee || pawn.jobs?.curJob?.def == JobDefOf.Flee)
                    {
                        __result = null;
                        if (pawn.jobs?.curJob?.def == JobDefOf.Flee)
                        {
                            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        }
                    }

                    ClearNoThreatScanCache(pawn);
                    StorePawnFleeDecisionCache(pawn, currentTick, null);
                    return;
                }

                if (__result != null || pawn.jobs?.curJob?.def == JobDefOf.Flee)
                {
                    ClearNoThreatScanCache(pawn);
                    return;
                }

                if (TryGetPawnFleeDecisionCache(pawn, currentTick, out bool hasCachedJob, out Job cachedJob))
                {
                    if (hasCachedJob)
                    {
                        __result = cachedJob;
                    }
                    return;
                }

                if (!underMeleeAttack
                    && fleeFromDefendCorpseEnabled
                    && TryHandleImmediateProtectPreyThreat(pawn, out Job protectPreyFleeJob))
                {
                    ClearNoThreatScanCache(pawn);
                    __result = protectPreyFleeJob;
                    StorePawnFleeDecisionCache(pawn, currentTick, __result);
                    return;
                }

                if (!underMeleeAttack
                    && fleeFromPredatorsEnabled
                    && TryHandleImmediateTargetedPredatorThreat(pawn, out Job targetedPredatorFleeJob))
                {
                    ClearNoThreatScanCache(pawn);
                    __result = targetedPredatorFleeJob;
                    StorePawnFleeDecisionCache(pawn, currentTick, __result);
                    return;
                }

                if (!underMeleeAttack
                    && fleeFromProtectYoungEnabled
                    && TryHandleImmediateProtectYoungThreat(pawn, out Job protectYoungFleeJob))
                {
                    ClearNoThreatScanCache(pawn);
                    __result = protectYoungFleeJob;
                    StorePawnFleeDecisionCache(pawn, currentTick, __result);
                    return;
                }

                bool anyStandardFleeFeatureEnabled = fleeFromNonTargetPredatorsEnabled
                    || fleeFromHumansEnabled
                    || fleeFromCarriersEnabled
                    || fleeFromRaidersForSmallPetsEnabled;
                if (!anyStandardFleeFeatureEnabled)
                {
                    RememberNoThreatScan(
                        pawn,
                        currentTick,
                        fleeFromNonTargetPredatorsEnabled,
                        fleeFromHumansEnabled,
                        fleeFromCarriersEnabled,
                        fleeFromRaidersForSmallPetsEnabled);
                    StorePawnFleeDecisionCache(pawn, currentTick, null);
                    return;
                }

                if (ShouldSkipThreatScan(
                    pawn,
                    currentTick,
                    fleeFromNonTargetPredatorsEnabled,
                    fleeFromNonTargetPredatorsEnabled,
                    fleeFromHumansEnabled,
                    fleeFromCarriersEnabled,
                    fleeFromRaidersForSmallPetsEnabled))
                {
                    StorePawnFleeDecisionCache(pawn, currentTick, null);
                    return;
                }

                if (!underMeleeAttack
                    && fleeFromNonTargetPredatorsEnabled
                    && TryHandlePredatorThreat(pawn, allowNonHostilePredators: true, out Job predatorFleeJob))
                {
                    ClearNoThreatScanCache(pawn);
                    if (predatorFleeJob != null)
                    {
                        __result = predatorFleeJob;
                    }
                    StorePawnFleeDecisionCache(pawn, currentTick, __result);
                    return;
                }

                if (!underMeleeAttack && fleeFromCarriersEnabled)
                {
                    Job carrierJob = TryCreateCarrierFleeJob(pawn);
                    if (carrierJob != null)
                    {
                        ClearNoThreatScanCache(pawn);
                        __result = carrierJob;
                        StorePawnFleeDecisionCache(pawn, currentTick, __result);
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
                        StorePawnFleeDecisionCache(pawn, currentTick, __result);
                        return;
                    }
                }

                if (!fleeFromHumansEnabled || __result != null)
                {
                    if (__result == null)
                    {
                        if (!underMeleeAttack)
                        {
                            RememberNoThreatScan(
                                pawn,
                                currentTick,
                                fleeFromNonTargetPredatorsEnabled,
                                fleeFromHumansEnabled,
                                fleeFromCarriersEnabled,
                                fleeFromRaidersForSmallPetsEnabled);
                        }
                    }
                    if (!underMeleeAttack)
                    {
                        StorePawnFleeDecisionCache(pawn, currentTick, __result);
                    }
                    return;
                }

                Job humanJob = underMeleeAttack ? null : TryCreateHumanFleeJob(pawn, settings);
                if (humanJob != null)
                {
                    ClearNoThreatScanCache(pawn);
                    __result = humanJob;
                    StorePawnFleeDecisionCache(pawn, currentTick, __result);
                    return;
                }

                if (__result == null)
                {
                    if (!underMeleeAttack)
                    {
                        RememberNoThreatScan(
                            pawn,
                            currentTick,
                            fleeFromNonTargetPredatorsEnabled,
                            fleeFromHumansEnabled,
                            fleeFromCarriersEnabled,
                            fleeFromRaidersForSmallPetsEnabled);
                    }
                }
                if (!underMeleeAttack)
                {
                    StorePawnFleeDecisionCache(pawn, currentTick, __result);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[ZoologyMod] Patch_AnimalFleeFromPredators failed: {e}");
            }
        }

        private static bool TryHandleImmediateProtectPreyThreat(Pawn pawn, out Job fleeJob)
        {
            fleeJob = null;

            if (pawn?.Map == null)
            {
                return false;
            }

            if (!ProtectPreyState.HasAnyActiveProtectors || !ProtectPreyState.HasActiveProtectorsForMap(pawn.Map))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!TryGetProtectPreyThreatForPawn(pawn, currentTick, out Pawn threat))
            {
                return false;
            }

            return TryBuildImmediateThreatFleeJob(pawn, threat, GetFleeDistanceTargetPredator(), out fleeJob);
        }

        private static bool TryHandleImmediateTargetedPredatorThreat(Pawn pawn, out Job fleeJob)
        {
            fleeJob = null;
            if (pawn?.Map == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            var entries = GetTargetedPredatorEntriesForMap(pawn.Map, currentTick);
            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            float predatorRadius = GetPredatorSearchRadius();
            float maxDistanceSq = predatorRadius * predatorRadius;
            IntVec3 pawnPos = pawn.Position;
            Pawn best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!ReferenceEquals(entry.Prey, pawn))
                {
                    continue;
                }

                Pawn predator = entry.Predator;
                if (predator == null || predator.Dead || predator.Destroyed || predator.Downed || !predator.Spawned)
                {
                    continue;
                }

                if (predator.Map != pawn.Map)
                {
                    continue;
                }

                float dist = (predator.Position - pawnPos).LengthHorizontalSquared;
                if (dist > maxDistanceSq)
                {
                    continue;
                }

                if (!CanThreatTargetPawnForFlee(predator, pawn))
                {
                    continue;
                }

                if (!HasReachability(predator, pawn))
                {
                    continue;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = predator;
                }
            }

            if (best == null)
            {
                if (ZoologyCacheUtility.IsPhotonozoa(pawn.def))
                {
                    best = FindNearestTargetedPredatorThreatFallback(pawn, predatorRadius);
                }

                if (best == null)
                {
                    return false;
                }
            }

            bool created = TryBuildImmediateThreatFleeJob(pawn, best, GetFleeDistanceTargetPredator(), out fleeJob);
            return created;
        }

        private static bool TryHandleImmediateProtectYoungThreat(Pawn pawn, out Job fleeJob)
        {
            fleeJob = null;
            if (pawn?.Map == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            var entries = GetProtectYoungEntriesForMap(pawn.Map, currentTick);
            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            float predatorRadius = GetPredatorSearchRadius();
            float maxDistanceSq = predatorRadius * predatorRadius;
            IntVec3 pawnPos = pawn.Position;
            Pawn best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!ReferenceEquals(entry.Prey, pawn))
                {
                    continue;
                }

                Pawn threat = entry.Predator;
                if (!ZoologyFleeSafetyUtility.IsValidThreatForFlee(threat, pawn))
                {
                    continue;
                }

                float dist = (threat.Position - pawnPos).LengthHorizontalSquared;
                if (dist > maxDistanceSq)
                {
                    continue;
                }

                if (!HasReachability(threat, pawn))
                {
                    continue;
                }

                if (!CanThreatTargetPawnForFlee(threat, pawn))
                {
                    continue;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = threat;
                }
            }

            if (best == null)
            {
                return false;
            }

            return TryBuildImmediateThreatFleeJob(pawn, best, GetFleeDistanceTargetPredator(), out fleeJob);
        }

        private static List<TargetedPredatorEntry> GetTargetedPredatorEntriesForMap(Map map, int currentTick)
        {
            if (map == null)
            {
                return null;
            }

            int mapId = map.uniqueID;
            if (targetedPredatorEntriesMapId == mapId
                && targetedPredatorEntriesTick == currentTick)
            {
                return targetedPredatorEntriesScratch;
            }

            targetedPredatorEntriesTick = currentTick;
            targetedPredatorEntriesMapId = mapId;
            targetedPredatorEntriesScratch.Clear();

            IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns == null || pawns.Count == 0)
            {
                return targetedPredatorEntriesScratch;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn predator = pawns[i];
                if (predator == null || predator.Dead || predator.Destroyed || predator.Downed || !predator.Spawned)
                {
                    continue;
                }

                if (!TryGetThreatTargetPawn(predator, out Pawn prey))
                {
                    continue;
                }

                if (!IsPredatorThreatSourceForPrey(predator, prey))
                {
                    continue;
                }

                if (!IsTargetedPredatorThreatJob(predator, prey))
                {
                    continue;
                }

                if (prey == null || prey.Dead || prey.Destroyed || prey.Downed || !prey.Spawned)
                {
                    continue;
                }

                if (prey.Map != map)
                {
                    continue;
                }

                targetedPredatorEntriesScratch.Add(new TargetedPredatorEntry(predator, prey));
            }

            return targetedPredatorEntriesScratch;
        }

        private static List<TargetedPredatorEntry> GetProtectYoungEntriesForMap(Map map, int currentTick)
        {
            if (map == null)
            {
                return null;
            }

            int mapId = map.uniqueID;
            if (targetedProtectYoungEntriesMapId == mapId
                && targetedProtectYoungEntriesTick == currentTick)
            {
                return targetedProtectYoungEntriesScratch;
            }

            targetedProtectYoungEntriesTick = currentTick;
            targetedProtectYoungEntriesMapId = mapId;
            targetedProtectYoungEntriesScratch.Clear();

            IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns == null || pawns.Count == 0)
            {
                return targetedProtectYoungEntriesScratch;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn protector = pawns[i];
                if (protector == null || protector.Dead || protector.Destroyed || protector.Downed || !protector.Spawned)
                {
                    continue;
                }

                if (!ProtectYoungUtility.IsProtectYoungJob(protector))
                {
                    continue;
                }

                if (!TryGetThreatTargetPawn(protector, out Pawn prey))
                {
                    continue;
                }

                if (prey == null || prey.Dead || prey.Destroyed || prey.Downed || !prey.Spawned || prey.Map != map)
                {
                    continue;
                }

                targetedProtectYoungEntriesScratch.Add(new TargetedPredatorEntry(protector, prey));
            }

            return targetedProtectYoungEntriesScratch;
        }

        private static Pawn FindNearestTargetedPredatorThreatFallback(Pawn prey, float radius)
        {
            if (prey?.Map?.mapPawns?.AllPawnsSpawned == null)
            {
                return null;
            }

            float radiusSquared = radius * radius;
            float bestDistanceSquared = radiusSquared;
            Pawn nearestThreat = null;
            IReadOnlyList<Pawn> pawns = prey.Map.mapPawns.AllPawnsSpawned;
            IntVec3 preyPosition = prey.Position;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn predator = pawns[i];
                if (!ZoologyFleeSafetyUtility.IsValidThreatForFlee(predator, prey)
                    || !IsPredatorThreatSourceForPrey(predator, prey))
                {
                    continue;
                }

                float distanceSquared = (predator.Position - preyPosition).LengthHorizontalSquared;
                if (distanceSquared > radiusSquared || distanceSquared >= bestDistanceSquared)
                {
                    continue;
                }

                if (!IsTargetedPredatorThreatJob(predator, prey)
                    || !CanThreatTargetPawnForFlee(predator, prey)
                    || !HasReachability(predator, prey))
                {
                    continue;
                }

                nearestThreat = predator;
                bestDistanceSquared = distanceSquared;
            }

            return nearestThreat;
        }

        private static bool TryGetProtectPreyThreatForPawn(Pawn pawn, int currentTick, out Pawn threat)
        {
            threat = null;

            if (pawn?.Map == null)
            {
                return false;
            }

            var entries = GetProtectPreyEntriesForMap(pawn.Map, currentTick);
            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            int searchRadius = Math.Max(GetFleeDistanceTargetPredator(), PreyProtectionUtility.GetProtectionRange());
            float maxDistanceSq = searchRadius * searchRadius;
            IntVec3 pawnPos = pawn.Position;
            Pawn best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!ReferenceEquals(entry.ProtectedPawn, pawn))
                {
                    continue;
                }

                Pawn protector = entry.Predator;
                if (protector == null || protector.Dead || protector.Destroyed || protector.Downed || !protector.Spawned)
                {
                    continue;
                }

                if (protector.Map != pawn.Map)
                {
                    continue;
                }

                float dist = (protector.Position - pawnPos).LengthHorizontalSquared;
                if (dist > maxDistanceSq)
                {
                    continue;
                }

                if (!HasReachability(protector, pawn))
                {
                    continue;
                }

                if (!CanThreatTargetPawnForFlee(protector, pawn))
                {
                    continue;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = protector;
                }
            }

            if (best == null)
            {
                return false;
            }

            threat = best;
            return true;
        }

        private static List<ProtectPreyMapCache.Entry> GetProtectPreyEntriesForMap(Map map, int currentTick)
        {
            if (map == null)
            {
                return null;
            }

            int mapId = map.uniqueID;
            if (protectPreyThreatEntriesTick == currentTick && protectPreyThreatEntriesMapId == mapId)
            {
                return protectPreyThreatEntriesScratch;
            }

            protectPreyThreatEntriesTick = currentTick;
            protectPreyThreatEntriesMapId = mapId;
            protectPreyThreatEntriesScratch.Clear();
            ProtectPreyState.TryFillActiveProtectorsForMap(map, protectPreyThreatEntriesScratch);
            return protectPreyThreatEntriesScratch;
        }

        private static bool TryBuildImmediateThreatFleeJob(Pawn pawn, Pawn threat, int fleeDistance, out Job fleeJob)
        {
            fleeJob = null;
            if (!ZoologyFleeSafetyUtility.CanUseForcedThreatFlee(pawn))
            {
                return false;
            }

            if (!ZoologyFleeSafetyUtility.IsValidThreatForFlee(threat, pawn))
            {
                return false;
            }

            if (ShouldBlockFleeInMeleeForPawn(pawn)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, pawn))
            {
                return false;
            }

            if (!HasReachability(threat, pawn))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (HasFreshFleeJob(pawn, currentTick))
            {
                return false;
            }

            HandlePursuitAllowanceIfNeeded(threat, pawn);
            fleeJob = FleeUtility.FleeJob(pawn, threat, fleeDistance);
            return fleeJob != null;
        }

        private static bool TryHandlePredatorThreat(Pawn pawn, bool allowNonHostilePredators, out Job fleeJob)
        {
            fleeJob = null;

            if (pawn?.Map == null)
            {
                return false;
            }

            bool preyIsPhotonozoa = ZoologyCacheUtility.IsPhotonozoa(pawn.def);
            bool foodJobActive = false;
            bool foodJobActiveKnown = false;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!TryGetThreatCache(pawn, refreshIfNeeded: true, out ThreatMapCacheData threatCache))
            {
                if (TryConsumeFallbackThreatScanBudget(currentTick))
                {
                    Pawn fallbackThreat = FindNearestPredatorThreatFallback(pawn, GetPredatorSearchRadius(), allowNonHostilePredators);
                    if (fallbackThreat == null)
                    {
                        return false;
                    }

                    foodJobActive = IsFoodSeekingOrEatingJob(pawn);
                    foodJobActiveKnown = true;
                    return TryBuildPredatorFleeJob(pawn, fallbackThreat, preyIsPhotonozoa, foodJobActive, out fleeJob);
                }

                return false;
            }

            if (threatCache.HasCloseMeleePredatorThreats
                && TryGetCloseMeleeThreat(pawn, isPredatorThreat: true, threatCache, out Pawn meleeThreat)
                && ShouldBlockFleeInMeleeForPawn(pawn)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(meleeThreat, pawn))
            {
                return true;
            }

            if (!threatCache.HasActivePredatorThreats
                && (!allowNonHostilePredators || !threatCache.HasPassivePredatorThreats))
            {
                if (TryConsumeFallbackThreatScanBudget(currentTick))
                {
                    Pawn fallbackThreat = FindNearestPredatorThreatFallback(pawn, GetPredatorSearchRadius(), allowNonHostilePredators);
                    if (fallbackThreat == null)
                    {
                        return false;
                    }

                    if (!foodJobActiveKnown)
                    {
                        foodJobActive = IsFoodSeekingOrEatingJob(pawn);
                        foodJobActiveKnown = true;
                    }

                    return TryBuildPredatorFleeJob(pawn, fallbackThreat, preyIsPhotonozoa, foodJobActive, out fleeJob);
                }

                return false;
            }

            if (!threatCache.HasActivePredatorThreats && allowNonHostilePredators)
            {
                foodJobActive = IsFoodSeekingOrEatingJob(pawn);
                foodJobActiveKnown = true;
                if (foodJobActive)
                {
                    return false;
                }
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
                threat = FindNearestPredatorThreat(pawn, threatCache, GetPredatorSearchRadius(), allowNonHostilePredators);
                StoreNearestPredatorThreatCache(pawn, threatCache, allowNonHostilePredators, threat, threat != null, currentTick);
            }
            else if (!hasCachedThreat)
            {
                return false;
            }

            if (threat == null)
            {
                if (preyIsPhotonozoa
                    && allowNonHostilePredators
                    && TryConsumeFallbackThreatScanBudget(currentTick))
                {
                    threat = FindNearestPredatorThreatFallback(pawn, GetPredatorSearchRadius(), allowNonHostilePredators);
                }

                if (threat == null)
                {
                    return false;
                }
            }

            if (!foodJobActiveKnown)
            {
                foodJobActive = IsFoodSeekingOrEatingJob(pawn);
                foodJobActiveKnown = true;
            }

            bool created = TryBuildPredatorFleeJob(pawn, threat, preyIsPhotonozoa, foodJobActive, out fleeJob);
            return created;
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

            bool photonozoaPredatorException = CanUsePhotonozoaPredatorException(pawn, threat);
            if (!photonozoaPredatorException && ZoologyFleeSafetyUtility.IsStandardFleeBlockedByExtensions(pawn))
            {
                return false;
            }

            bool shouldAnimalFleeDanger = photonozoaPredatorException || FleeUtility.ShouldAnimalFleeDanger(pawn);
            bool threatAimingAtPawn = CanThreatTargetPawnForFlee(threat, pawn);
            if (threatAimingAtPawn)
            {
                return false;
            }

            if (ShouldBlockFleeInMeleeForPawn(pawn)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, pawn))
            {
                return false;
            }

            if (!shouldAnimalFleeDanger && !photonozoaPredatorException)
            {
                return false;
            }

            if (!IsValidNonTargetPredatorFactionContext(threat, pawn)
                || !IsAcceptablePreyForFlee(threat, pawn))
            {
                return false;
            }

            if (foodJobActive)
            {
                return false;
            }

            int fleeDistance = GetPredatorFleeDistanceForThreat(pawn, threat);

            bool bothPhotonozoaInTheirFaction = IsPhotonozoaPairInTheirFaction(threat, pawn);
            if (photonozoaPredatorException)
            {
                if (!CanPhotonozoaPreyReactToPredatorFlee(pawn))
                {
                    return false;
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (HasFreshFleeJob(pawn, currentTick))
                {
                    return false;
                }
            }
            else if (!bothPhotonozoaInTheirFaction)
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

                Faction playerFaction = Faction.OfPlayerSilentFail;
                if (playerFaction != null && pawn.Faction == playerFaction && pawn.Map.IsPlayerHome)
                {
                    return false;
                }

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (HasFreshFleeJob(pawn, currentTick))
                {
                    return false;
                }
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

                float humanRadius = GetHumanSearchRadius();
                Pawn fallbackThreat = FindNearestHumanlikeThreatFallback(pawn, humanRadius);
                if (fallbackThreat == null
                    || (ShouldBlockFleeInMeleeForPawn(pawn)
                        && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(fallbackThreat, pawn)))
                {
                    return null;
                }

                return FleeUtility.FleeJob(pawn, fallbackThreat, GetFleeDistanceHuman());
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

                float humanRadius = GetHumanSearchRadius();
                Pawn fallbackThreat = FindNearestHumanlikeThreatFallback(pawn, humanRadius);
                if (fallbackThreat == null
                    || (ShouldBlockFleeInMeleeForPawn(pawn)
                        && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(fallbackThreat, pawn)))
                {
                    return null;
                }

                return FleeUtility.FleeJob(pawn, fallbackThreat, GetFleeDistanceHuman());
            }

            if (!CanAnimalFleeFromHumans(pawn, settings, threatCache))
            {
                return null;
            }

            Pawn threat = FindNearestHumanlikeThreat(pawn, threatCache, GetHumanSearchRadius());
            if (threat == null)
            {
                return null;
            }

            if (ShouldBlockFleeInMeleeForPawn(pawn)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, pawn))
            {
                return null;
            }

            return FleeUtility.FleeJob(pawn, threat, GetFleeDistanceHuman());
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

            if (ShouldBlockFleeInMeleeForPawn(pawn)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, pawn))
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

            if (!ZoologyFleeSafetyUtility.IsValidThreatForFlee(threat, pawn))
            {
                return null;
            }

            if (!FleeUtility.ShouldAnimalFleeDanger(pawn))
            {
                return null;
            }

            if (ShouldBlockFleeInMeleeForPawn(pawn)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, pawn))
            {
                return null;
            }

            return FleeUtility.FleeJob(pawn, threat, GetSmallPetRaiderFleeDistance());
        }

        private static bool CanAnimalFleeFromHumans(Pawn pawn, ZoologyModSettings settings, ThreatMapCacheData threatCache = null)
        {
            if (pawn == null || (settings != null && !settings.AnimalsFreeFromHumans))
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

            if (ZoologyFleeSafetyUtility.IsStandardFleeBlockedByExtensions(pawn))
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

            if (settings != null && !settings.GetAnimalsFreeFromHumansFor(pawn.def))
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

            if (ZoologyFleeSafetyUtility.IsStandardFleeBlockedByExtensions(pawn))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (HasFreshFleeJob(pawn, currentTick))
            {
                return false;
            }

            return true;
        }

        private static bool CanSmallPetFleeFromRaiders(Pawn pawn, ZoologyModSettings settings)
        {
            if (pawn == null
                || settings == null
                || !pawn.RaceProps.Animal
                || pawn.Faction != Faction.OfPlayerSilentFail
                || pawn.Map == null
                || pawn.Roamer)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Dead || pawn.Destroyed || pawn.Downed || pawn.InMentalState || pawn.IsFighting())
            {
                return false;
            }

            if (ZoologyFleeSafetyUtility.IsStandardFleeBlockedByExtensions(pawn))
            {
                return false;
            }

            if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn))
            {
                return false;
            }

            if (pawn.GetLord() != null)
            {
                return false;
            }

            JobDef curJobDef = pawn.CurJob?.def;
            if (curJobDef != null && curJobDef.neverFleeFromEnemies)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (HasFreshFleeJob(pawn, currentTick))
            {
                return false;
            }

            return pawn.RaceProps.baseBodySize < settings.SmallPetBodySizeThreshold;
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

            if (TryGetNearbyThreatBucketsCache(
                pawn,
                freshThreatCache,
                predatorsEnabled,
                allowNonHostilePredators,
                humansEnabled,
                carriersEnabled,
                smallPetRaidersEnabled,
                currentTick,
                out bool hasNearbyThreatBuckets))
            {
                if (!hasNearbyThreatBuckets)
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
            }
            else
            {
                hasNearbyThreatBuckets = HasNearbyRelevantThreatBuckets(
                    freshThreatCache,
                    pawn,
                    predatorsEnabled,
                    allowNonHostilePredators,
                    humansEnabled,
                    carriersEnabled,
                    smallPetRaidersEnabled);
                StoreNearbyThreatBucketsCache(
                    pawn,
                    freshThreatCache,
                    predatorsEnabled,
                    allowNonHostilePredators,
                    humansEnabled,
                    carriersEnabled,
                    smallPetRaidersEnabled,
                    hasNearbyThreatBuckets,
                    currentTick);
                if (!hasNearbyThreatBuckets)
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
            }

            ClearNoThreatScanCache(pawn);
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
                && currentTick - lastFreshThreatCacheData.RefreshTick < lastFreshThreatCacheData.RefreshIntervalTicks)
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

            if (humansEnabled && cache.HasHumanlikeThreats)
            {
                return true;
            }

            if (smallPetRaidersEnabled && cache.HasSmallPetRaiderThreats)
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

            float predatorRadius = GetPredatorSearchRadius();
            if (predatorsEnabled && HasThreatBucketsInRange(cache.ActivePredatorThreatsByBucket, pawn.Position, predatorRadius))
            {
                return true;
            }

            float nonHostilePredatorRadius = GetNonHostilePredatorSearchRadius();
            if (predatorsEnabled
                && allowNonHostilePredators
                && HasThreatBucketsInRange(
                    cache.PassivePredatorThreatsByBucket,
                    pawn.Position,
                    nonHostilePredatorRadius))
            {
                return true;
            }

            float humanRadius = 0f;
            if (humansEnabled)
            {
                float configuredHumanRadius = GetHumanSearchRadius();
                if (HasThreatBucketsInRange(cache.HumanlikeThreatsByBucket, pawn.Position, configuredHumanRadius))
                {
                    return true;
                }

                humanRadius = configuredHumanRadius;
            }

            float smallPetRaiderRadius = GetSmallPetRaiderThreatSearchRadius();
            if (smallPetRaidersEnabled && smallPetRaiderRadius > humanRadius)
            {
                humanRadius = smallPetRaiderRadius;
            }

            if (smallPetRaidersEnabled
                && HasThreatBucketsInRange(cache.SmallPetRaiderThreatsByBucket, pawn.Position, smallPetRaiderRadius))
            {
                return true;
            }

            if (!smallPetRaidersEnabled
                && humanRadius > 0f
                && HasThreatBucketsInRange(cache.HumanlikeThreatsByBucket, pawn.Position, humanRadius))
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
                || currentTick > cached.ExpireTick)
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

            float predatorRadius = GetPredatorSearchRadius();
            float nonHostilePredatorRadius = GetNonHostilePredatorSearchRadius();
            bool isValidThreat = IsTrackedActivePredatorThreatStillValid(cachedThreat, pawn, predatorRadius)
                || (allowNonHostilePredators
                    && IsTrackedPassivePredatorThreatStillValid(
                        cachedThreat,
                        pawn,
                        nonHostilePredatorRadius));

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
            int cacheDurationTicks = CalculateNearestPredatorThreatCacheDurationTicks(cache.AnimalCount);
            if (cacheDurationTicks < 1)
            {
                cacheDurationTicks = 1;
            }

            nearestPredatorThreatCacheByPawnId[pawn.thingIDNumber] = new NearestPredatorThreatCacheEntry(
                pawn.Map.uniqueID,
                cache.RefreshTick,
                position.x / ThreatBucketSize,
                position.z / ThreatBucketSize,
                allowNonHostilePredators,
                threat,
                hasThreat,
                currentTick,
                currentTick + cacheDurationTicks);
        }

        private static int CalculateNearestPredatorThreatCacheDurationTicks(int animalCount)
        {
            if (animalCount >= 1200) return 90;
            if (animalCount >= 640) return 75;
            if (animalCount >= 320) return 60;
            if (animalCount >= 160) return 45;
            if (animalCount >= 80) return 30;
            return NearestPredatorThreatCacheDurationTicks;
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

        private static bool TryGetPawnFleeDecisionCache(
            Pawn pawn,
            int currentTick,
            out bool hasJob,
            out Job job)
        {
            hasJob = false;
            job = null;

            if (pawn == null || currentTick <= 0)
            {
                return false;
            }

            int pawnId = pawn.thingIDNumber;
            if (!pawnFleeDecisionCacheByPawnId.TryGetValue(pawnId, out PawnFleeDecisionCacheEntry cached))
            {
                return false;
            }

            int mapId = pawn.Map?.uniqueID ?? -1;
            if (cached.Tick != currentTick || cached.MapId != mapId)
            {
                pawnFleeDecisionCacheByPawnId.Remove(pawnId);
                return false;
            }

            hasJob = cached.HasJob;
            return true;
        }

        private static void StorePawnFleeDecisionCache(Pawn pawn, int currentTick, Job job)
        {
            if (pawn?.Map == null || currentTick <= 0)
            {
                return;
            }

            int pawnId = pawn.thingIDNumber;

            // RimWorld pools Job instances aggressively. Keeping a live Job in a static cache
            // can hand the same pooled object to a different pawn later in the same tick and
            // corrupt cachedDriver ownership. Cache only negative decisions here.
            if (job != null)
            {
                pawnFleeDecisionCacheByPawnId.Remove(pawnId);
                return;
            }

            pawnFleeDecisionCacheByPawnId[pawnId] = new PawnFleeDecisionCacheEntry(
                currentTick,
                pawn.Map.uniqueID,
                false);
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
            JobDef jobDef = pawn?.CurJob?.def;
            return jobDef != null && IsFoodSeekingOrEatingJob(jobDef);
        }

        private static bool IsFoodSeekingOrEatingJob(JobDef jobDef)
        {
            if (jobDef == null)
            {
                return false;
            }

            if (jobDef == JobDefOf.Ingest)
            {
                return true;
            }

            if (foodJobByDefCache.TryGetValue(jobDef, out bool cached))
            {
                return cached;
            }

            bool isFoodJob = false;
            Type driverClass = jobDef.driverClass;
            if (driverClass != null)
            {
                if (!foodJobDriverByTypeCache.TryGetValue(driverClass, out bool driverIsFood))
                {
                    driverIsFood = typeof(JobDriver_Ingest).IsAssignableFrom(driverClass)
                        || NameContainsIngest(driverClass.Name)
                        || NameContainsIngest(driverClass.FullName);
                    foodJobDriverByTypeCache[driverClass] = driverIsFood;
                }

                if (driverIsFood)
                {
                    isFoodJob = true;
                }
            }

            if (!isFoodJob && NameContainsIngest(jobDef.defName))
            {
                isFoodJob = true;
            }

            foodJobByDefCache[jobDef] = isFoodJob;
            return isFoodJob;
        }

        private static bool NameContainsIngest(string name)
        {
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("Ingest", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPredatorGuardingCorpseAgainstHumans(Pawn pawn, ZoologyModSettings settings)
        {
            if (pawn == null
                || (settings != null && !settings.AnimalsFreeFromHumans)
                || (settings != null && !settings.EnablePredatorDefendCorpse)
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

        private static bool HasActiveCloseMeleeThreatFromHumanlike(Pawn pawn)
        {
            return HasActiveCloseMeleeThreatFromHumanlike(pawn, refreshIfNeeded: true);
        }

        private static bool HasActiveCloseMeleeThreatFromHumanlike(Pawn pawn, bool refreshIfNeeded)
        {
            return TryGetCloseMeleeThreat(pawn, isPredatorThreat: false, refreshIfNeeded, out Pawn threat)
                && ShouldBlockFleeInMeleeForPawn(pawn)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, pawn);
        }

        private static bool HasActiveCloseMeleeThreatFromHumanlike(Pawn pawn, ThreatMapCacheData cache)
        {
            return TryGetCloseMeleeThreat(pawn, isPredatorThreat: false, cache, out Pawn threat)
                && ShouldBlockFleeInMeleeForPawn(pawn)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, pawn);
        }

        private static bool IsHumanDoingAnimalTamingJob(Pawn human)
        {
            Job curJob = human?.CurJob;
            JobDef jobDef = curJob?.def;
            if (jobDef == null)
            {
                return false;
            }

            if (jobDef == JobDefOf.Tame || jobDef == JobDefOf.Train)
            {
                return true;
            }

            if (!DoesJobTargetAnimal(curJob))
            {
                return false;
            }

            if (tamingJobByDefCache.TryGetValue(jobDef, out bool cached))
            {
                return cached;
            }

            bool isTamingJob = false;
            Type driverClass = jobDef.driverClass;
            if (driverClass != null)
            {
                if (!tamingJobDriverByTypeCache.TryGetValue(driverClass, out bool driverIsTaming))
                {
                    driverIsTaming = typeof(JobDriver_InteractAnimal).IsAssignableFrom(driverClass)
                        || JobNameMatchesTaming(driverClass.Name)
                        || JobNameMatchesTaming(driverClass.FullName);
                    tamingJobDriverByTypeCache[driverClass] = driverIsTaming;
                }

                isTamingJob = driverIsTaming;
            }

            if (!isTamingJob)
            {
                isTamingJob = JobNameMatchesTaming(jobDef.defName);
            }

            tamingJobByDefCache[jobDef] = isTamingJob;
            return isTamingJob;
        }

        private static bool DoesJobTargetAnimal(Job job)
        {
            if (job == null)
            {
                return false;
            }

            return JobTargetIsAnimal(job.targetA)
                || JobTargetIsAnimal(job.targetB)
                || JobTargetIsAnimal(job.targetC)
                || JobTargetQueueContainsAnimal(job.targetQueueA)
                || JobTargetQueueContainsAnimal(job.targetQueueB);
        }

        private static bool JobTargetQueueContainsAnimal(List<LocalTargetInfo> targetQueue)
        {
            if (targetQueue == null || targetQueue.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < targetQueue.Count; i++)
            {
                if (JobTargetIsAnimal(targetQueue[i]))
                {
                    return true;
                }
            }

            return false;
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

        private static bool JobNameMatchesPredatorHunt(string jobName)
        {
            return !string.IsNullOrEmpty(jobName)
                && jobName.IndexOf("PredatorHunt", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPredatorHuntThreatJob(Job curJob, JobDriver curDriver)
        {
            if (curJob?.def == null)
            {
                return false;
            }

            if (curDriver is JobDriver_PredatorHunt || curJob.def == JobDefOf.PredatorHunt)
            {
                return true;
            }

            Type driverClass = curJob.def.driverClass;
            return (driverClass != null && typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverClass))
                || JobNameMatchesPredatorHunt(curJob.def.defName)
                || JobNameMatchesPredatorHunt(driverClass?.Name)
                || JobNameMatchesPredatorHunt(driverClass?.FullName);
        }

        private static bool IsTargetedPredatorThreatJob(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            Job curJob = predator.CurJob;
            JobDriver curDriver = predator.jobs?.curDriver;
            if (curJob?.def == null)
            {
                return false;
            }

            if (IsPredatorHuntThreatJob(curJob, curDriver))
            {
                return true;
            }

            return curJob.def == JobDefOf.AttackMelee && CanThreatTargetPawnForFlee(predator, prey);
        }

        private static bool IsProtectYoungThreatJob(Job curJob, JobDriver curDriver)
        {
            if (curJob?.def == null)
            {
                return false;
            }

            if (curDriver is JobDriver_ProtectYoung)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(curJob.def.defName)
                && curJob.def.defName.Equals(ProtectYoungUtility.ProtectYoungDefName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Type driverClass = curJob.def.driverClass;
            return driverClass != null && typeof(JobDriver_ProtectYoung).IsAssignableFrom(driverClass);
        }

        private static bool TryGetJobTargetPawn(Job job, out Pawn targetPawn)
        {
            targetPawn = null;
            if (job == null)
            {
                return false;
            }

            if (TryGetPawnFromTarget(job.targetA, out targetPawn))
            {
                return true;
            }

            if (TryGetPawnFromTarget(job.targetB, out targetPawn))
            {
                return true;
            }

            return TryGetPawnFromTarget(job.targetC, out targetPawn);
        }

        private static bool TryGetPawnFromTarget(LocalTargetInfo target, out Pawn pawn)
        {
            pawn = null;
            if (!target.HasThing)
            {
                return false;
            }

            pawn = target.Thing as Pawn;
            return pawn != null;
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
                float searchRadius = GetSmallPetRaiderThreatSearchRadius();
                if (!TryGetThreatCache(pawn, refreshIfNeeded: true, out ThreatMapCacheData cache))
                {
                    return FindNearestSmallPetRaiderThreatFallback(pawn, searchRadius);
                }

                Pawn threatFromCache = FindNearestThreatInRange(
                    cache.SmallPetRaiderThreatsByBucket,
                    pawn,
                    searchRadius,
                    ThreatSearchMode.SmallPetRaider,
                    0f,
                    out _);

                return threatFromCache ?? FindNearestSmallPetRaiderThreatFallback(pawn, searchRadius);
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] FindNearestSmallPetRaiderThreat failed: {ex}");
                return FindNearestSmallPetRaiderThreatFallback(pawn, GetSmallPetRaiderThreatSearchRadius());
            }
        }

        private static Pawn FindNearestSmallPetRaiderThreatFallback(Pawn pawn, float radius)
        {
            if (pawn?.Map?.mapPawns?.AllPawnsSpawned == null)
            {
                return null;
            }

            float radiusSquared = radius * radius;
            float bestDistanceSquared = radiusSquared;
            Pawn nearestThreat = null;
            IReadOnlyList<Pawn> pawns = pawn.Map.mapPawns.AllPawnsSpawned;
            IntVec3 pawnPosition = pawn.Position;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn threat = pawns[i];
                if (threat == null || !IsSmallPetRaiderThreatCandidate(threat, pawn))
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

        private static Pawn FindNearestCarrierThreat(Pawn pawn, out int fleeDistance)
        {
            fleeDistance = CarrierExtensionDefaultFleeDistance > 0 ? CarrierExtensionDefaultFleeDistance : VanillaFleeDistance;
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

                fleeDistance = GetCarrierFleeDistance(extension);
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
                && IsPotentialSmallPetRaiderThreatSource(threat)
                && HasLineOfSightAndReach(threat, pawn);
        }

        private static bool IsPotentialSmallPetRaiderThreatSource(Pawn threat)
        {
            if (threat == null)
            {
                return false;
            }

            Faction threatFaction = threat.Faction;
            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (threatFaction == null || playerFaction == null || ReferenceEquals(threatFaction, playerFaction))
            {
                return false;
            }

            return IsHostileToPlayerSafe(threat);
        }

        private static bool IsHostileToPlayerSafe(Pawn threat)
        {
            try
            {
                if (threat == null) return false;
                Faction playerFaction = Faction.OfPlayerSilentFail;
                Faction threatFaction = threat.Faction;
                if (playerFaction == null || threatFaction == null) return false;
                if (ReferenceEquals(playerFaction, threatFaction)) return false;

                FactionRelation rel = threatFaction.RelationWith(playerFaction, allowNull: true);
                return rel == null || rel.kind == FactionRelationKind.Hostile;
            }
            catch
            {
                return false;
            }
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

            if (!IsFactionNullOrHostileToPrey(carrier, prey))
            {
                return false;
            }

            if (extension.fleeBodySizeLimit > 0f && preyBodySize > extension.fleeBodySizeLimit)
            {
                return false;
            }

            return HasLineOfSightAndReach(carrier, prey);
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

            StoreThreatVisibility(lineOfSightAndReachCacheByPairKey, threat, pawn, true);
            return true;
        }

        private static bool HasReachability(Pawn threat, Pawn pawn)
        {
            if (threat == null || pawn == null || threat.Map == null || threat.Map != pawn.Map)
            {
                return false;
            }

            if (threat.Position == pawn.Position)
            {
                return true;
            }

            if (TryGetThreatVisibility(reachabilityCacheByPairKey, threat, pawn, out bool cachedResult))
            {
                return cachedResult;
            }

            bool canReach;
            try
            {
                canReach = threat.CanReach(pawn, PathEndMode.Touch, Danger.Deadly);
            }
            catch
            {
                canReach = false;
            }

            if (!canReach)
            {
                try
                {
                    canReach = pawn.CanReach(threat, PathEndMode.Touch, Danger.Deadly);
                }
                catch
                {
                    canReach = false;
                }
            }

            StoreThreatVisibility(reachabilityCacheByPairKey, threat, pawn, canReach);
            return canReach;
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

                float passiveRadius = GetNonHostilePredatorSearchRadius();
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

            float passiveRadius = GetNonHostilePredatorSearchRadius();
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
                    || !IsPotentialPredatorThreatSource(threat))
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
                && IsPredatorThreatSourceForPrey(predator, prey)
                && TryGetThreatTargetPawn(predator, out Pawn targetedPawn)
                && distanceSquared <= radiusSquared
                && (ReferenceEquals(targetedPawn, prey)
                    ? HasReachability(predator, prey)
                    : HasLineOfSightAndReach(predator, prey))
                && (ReferenceEquals(targetedPawn, prey)
                    || (IsValidNonTargetPredatorFactionContext(predator, prey) && IsAcceptablePreyForFlee(predator, prey)));
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
                && IsPredatorThreatSourceForPrey(predator, prey)
                && HasLineOfSightAndReach(predator, prey)
                && distanceSquared <= radiusSquared
                && IsValidNonTargetPredatorFactionContext(predator, prey)
                && IsAcceptablePreyForFlee(predator, prey);
        }

        private static void CleanupThreatCachesIfNeeded(int currentTick)
        {
            if (currentTick - lastThreatCacheCleanupTick < ThreatCacheCleanupIntervalTicks)
            {
                return;
            }

            lastThreatCacheCleanupTick = currentTick;
            CleanupNoThreatScanCache(currentTick);
            CleanupPawnFleeDecisionCache(currentTick);
            CleanupThreatVisibilityCache(lineOfSightAndReachCacheByPairKey, currentTick);
            CleanupThreatVisibilityCache(reachabilityCacheByPairKey, currentTick);
            CleanupNearbyThreatBucketsCache(currentTick);
            CleanupNearestPredatorThreatCache(currentTick);
            ThreatMapCache.CleanupStaleMaps();
            CleanupThreatScanBudget();
        }

        private static void EnsureRuntimeCacheState(int currentTick)
        {
            Game currentGame = Current.Game;
            bool gameChanged = !ReferenceEquals(runtimeCacheGame, currentGame);
            bool tickRewound = currentTick > 0 && runtimeCacheLastTick > 0 && currentTick < runtimeCacheLastTick;
            if (gameChanged || tickRewound)
            {
                ResetRuntimeCaches();
                runtimeCacheGame = currentGame;
            }

            if (currentTick > 0)
            {
                runtimeCacheLastTick = currentTick;
            }
        }

        private static void ResetRuntimeCaches()
        {
            noThreatScanCacheByPawnId.Clear();
            lineOfSightAndReachCacheByPairKey.Clear();
            reachabilityCacheByPairKey.Clear();
            nearbyThreatBucketsCacheByBucketKey.Clear();
            nearestPredatorThreatCacheByPawnId.Clear();
            threatScanBudgetByMapId.Clear();
            pawnFleeDecisionCacheByPawnId.Clear();
            protectPreyThreatEntriesScratch.Clear();
            targetedPredatorEntriesScratch.Clear();
            targetedProtectYoungEntriesScratch.Clear();
            protectPreyThreatEntriesTick = -1;
            protectPreyThreatEntriesMapId = -1;
            targetedPredatorEntriesTick = -1;
            targetedPredatorEntriesMapId = -1;
            targetedProtectYoungEntriesTick = -1;
            targetedProtectYoungEntriesMapId = -1;
            lastThreatCacheCleanupTick = -ThreatCacheCleanupIntervalTicks;
            lastFreshThreatCacheTick = int.MinValue;
            lastFreshThreatCacheMapId = int.MinValue;
            lastFreshThreatCacheWasFresh = false;
            lastFreshThreatCacheData = null;
            fallbackThreatScanBudgetTick = -1;
            fallbackThreatScanBudgetRemaining = 0;
            ThreatMapCache.ClearAll();
            PredationDecisionCache.ClearAll();
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

        private static void CleanupPawnFleeDecisionCache(int currentTick)
        {
            if (currentTick <= 0 || pawnFleeDecisionCacheByPawnId.Count == 0)
            {
                return;
            }

            List<int> staleKeys = null;
            foreach (KeyValuePair<int, PawnFleeDecisionCacheEntry> entry in pawnFleeDecisionCacheByPawnId)
            {
                if (currentTick - entry.Value.Tick <= ThreatCacheCleanupIntervalTicks)
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
                pawnFleeDecisionCacheByPawnId.Remove(staleKeys[i]);
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
                if (currentTick <= entry.Value.ExpireTick)
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

        private static bool CanThreatTargetPawnForFlee(Pawn threat, Pawn prey)
        {
            return IsThreatTargetingPawn(threat, prey)
                && (AnimalCombatPowerUtility.CanAnimalThreatTriggerTargetedFlee(threat, prey)
                    || IsAcceptablePreyForFlee(threat, prey));
        }

        private static bool TryGetThreatTargetPawn(Pawn predator, out Pawn targetPawn)
        {
            targetPawn = null;

            Job curJob = predator?.CurJob;
            if (curJob == null)
            {
                return false;
            }

            if (predator.jobs?.curDriver is JobDriver_PredatorHunt huntDriver)
            {
                Pawn prey = huntDriver.Prey;
                if (prey != null && !prey.Dead)
                {
                    targetPawn = prey;
                    return true;
                }
            }

            if (!TryGetJobTargetPawn(curJob, out Pawn candidate) || candidate.Dead)
            {
                return false;
            }

            if (!IsThreatJob(curJob, predator.jobs?.curDriver))
            {
                return false;
            }

            targetPawn = candidate;
            return true;
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

            if (curJob.def == JobDefOf.PredatorHunt)
            {
                return true;
            }

            Type driverClass = curJob.def.driverClass;
            if (driverClass != null)
            {
                if (!predatorHuntDriverByTypeCache.TryGetValue(driverClass, out bool driverIsHunt))
                {
                    driverIsHunt = typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverClass)
                        || JobNameMatchesPredatorHunt(driverClass.Name)
                        || JobNameMatchesPredatorHunt(driverClass.FullName);
                    predatorHuntDriverByTypeCache[driverClass] = driverIsHunt;
                }

                if (driverIsHunt)
                {
                    return true;
                }
            }

            if (JobNameMatchesPredatorHunt(curJob.def.defName))
            {
                return true;
            }

            return ProtectPreyState.IsProtectPreyJob(curJob, curDriver)
                || IsProtectYoungThreatJob(curJob, curDriver);
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
                return false;
            }
        }

        private static bool IsAcceptablePreyForFlee(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return false;
            }

            if (CanUsePhotonozoaPredatorException(prey, predator))
            {
                if (IsAcceptablePrey(predator, prey))
                {
                    return true;
                }

                return Patch_IsAcceptablePreyForPredator.IsAcceptablePhotonozoaPreyForFlee(predator, prey);
            }

            return IsAcceptablePrey(predator, prey);
        }

        private static bool IsFleeJobThreatMeleeAttackingPawn(Pawn pawn, Job fleeJob)
        {
            if (pawn == null || fleeJob?.def != JobDefOf.Flee)
            {
                return false;
            }

            return TryGetJobTargetPawn(fleeJob, out Pawn threat)
                && ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(threat, pawn);
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
