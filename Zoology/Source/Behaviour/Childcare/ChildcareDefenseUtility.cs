using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class ChildcareDefenseUtility
    {
        private const int EggProtectionTriggerCooldownTicks = 180;
        private const int YoungProtectionTriggerCooldownTicks = 180;
        private const float GuardedEggPenalty = -10000f;
        private const float GuardedYoungPenalty = -10000f;
        private const float ProtectionHungerThreshold = 0.10f;
        private const int EggFoodDeltaCacheDurationTicks = 10;
        private const int EggFoodDeltaHotCacheSize = 8192;
        private const int EggFoodDeltaHotCacheMask = EggFoodDeltaHotCacheSize - 1;
        private const int YoungProtectionStateCacheDurationTicks = ZoologyTickLimiter.Childcare.YoungProtectionTargetStateCacheDurationTicks;
        private const int YoungProtectionStateHotCacheSize = 32768;
        private const int YoungProtectionStateHotCacheMask = YoungProtectionStateHotCacheSize - 1;
        private const int YoungProtectionTargetStateHotCacheSize = 8192;
        private const int YoungProtectionTargetStateHotCacheMask = YoungProtectionTargetStateHotCacheSize - 1;
        private const int DirectYoungProtectionMapCacheDurationTicks = 60;
        private const int EggIncubationDurationTicks = 2500;
        private const int EggIncubationSearchFailCooldownTicks = 180;
        private const float EggIncubationSearchRadius = 20f;
        private const int EggIncubationTouchFreshnessTicks = 2;
        private const byte EggStateUnknown = 0;
        private const byte EggStateNo = 1;
        private const byte EggStateUnfertilized = 2;
        private const byte EggStateFertilized = 4;
        private const byte HatcherCompStateUnknown = 0;
        private const byte HatcherCompStateNo = 1;
        private const byte HatcherCompStateYes = 2;

        private readonly struct EggFoodDeltaCacheEntry
        {
            public EggFoodDeltaCacheEntry(long key, float delta, int tick)
            {
                Key = key;
                Delta = delta;
                Tick = tick;
            }

            public long Key { get; }
            public float Delta { get; }
            public int Tick { get; }
        }

        private readonly struct EggFoodStateCacheEntry
        {
            public EggFoodStateCacheEntry(bool isGuarded, int tick)
            {
                IsGuarded = isGuarded;
                Tick = tick;
            }

            public bool IsGuarded { get; }
            public int Tick { get; }
        }

        private readonly struct YoungProtectionStateCacheEntry
        {
            public YoungProtectionStateCacheEntry(bool isGuarded, int tick)
            {
                IsGuarded = isGuarded;
                Tick = tick;
            }

            public bool IsGuarded { get; }
            public int Tick { get; }
        }

        private readonly struct YoungProtectionStateHotCacheEntry
        {
            public YoungProtectionStateHotCacheEntry(Pawn aggressor, Pawn young, YoungProtectionStateCacheEntry state)
            {
                YoungId = young?.thingIDNumber ?? 0;
                AggressorMap = aggressor?.MapHeld;
                AggressorDef = aggressor?.def;
                AggressorKind = aggressor?.kindDef;
                AggressorLifeStage = aggressor?.ageTracker?.CurLifeStage;
                AggressorFaction = aggressor?.Faction;
                State = state;
            }

            public int YoungId { get; }
            public Map AggressorMap { get; }
            public ThingDef AggressorDef { get; }
            public PawnKindDef AggressorKind { get; }
            public LifeStageDef AggressorLifeStage { get; }
            public Faction AggressorFaction { get; }
            public YoungProtectionStateCacheEntry State { get; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Matches(Pawn aggressor, Pawn young)
            {
                return YoungId > 0
                    && young != null
                    && young.thingIDNumber == YoungId
                    && ReferenceEquals(AggressorMap, aggressor?.MapHeld)
                    && ReferenceEquals(AggressorDef, aggressor?.def)
                    && ReferenceEquals(AggressorKind, aggressor?.kindDef)
                    && ReferenceEquals(AggressorLifeStage, aggressor?.ageTracker?.CurLifeStage)
                    && ReferenceEquals(AggressorFaction, aggressor?.Faction);
            }
        }

        private struct YoungProtectionTargetState
        {
            public int YoungId;
            public int Tick;
            public Map AnchorMap;
            public bool HasBestProtector;
            public float BestProtectorPower;
            public Faction BestProtectorBlockedFaction;
            public bool HasFallbackProtector;
            public float FallbackProtectorPower;
        }

        private readonly struct NearbyYoungCacheEntry
        {
            public NearbyYoungCacheEntry(Pawn young, int tick, bool hasYoung)
            {
                Young = young;
                Tick = tick;
                HasYoung = hasYoung;
            }

            public Pawn Young { get; }
            public int Tick { get; }
            public bool HasYoung { get; }
        }

        private static readonly Dictionary<long, int> recentEggProtectionTriggerByPairKey = new Dictionary<long, int>(128);
        private static readonly Dictionary<long, int> recentYoungProtectionTriggerByPairKey = new Dictionary<long, int>(128);
        private static readonly Dictionary<long, EggFoodStateCacheEntry> eggFoodStateCacheByPairKey = new Dictionary<long, EggFoodStateCacheEntry>(256);
        private static readonly Dictionary<int, NearbyYoungCacheEntry> nearbyYoungCacheByProtectorId = new Dictionary<int, NearbyYoungCacheEntry>(128);
        private static readonly Dictionary<int, YoungProtectionTargetState> directYoungProtectionStateByYoungId = new Dictionary<int, YoungProtectionTargetState>(256);
        private static readonly Dictionary<int, int> recentProtectYoungForcedFleeTickByAggressorId = new Dictionary<int, int>(128);
        private static readonly Dictionary<int, int> incubatedEggTouchTickByEggId = new Dictionary<int, int>(64);
        private static readonly Dictionary<int, int> lastIncubationSearchFailureTickByPawnId = new Dictionary<int, int>(64);
        private static readonly List<Pawn> eggProtectorsScratch = new List<Pawn>(8);
        private static readonly List<Pawn> youngProtectorsScratch = new List<Pawn>(8);
        private static readonly List<Pawn> childcareOwnerScratch = new List<Pawn>(8);
        private static readonly List<Pawn> triggeredProtectorsScratch = new List<Pawn>(8);
        private static readonly List<long> eggTriggerCleanupScratch = new List<long>(64);
        private static readonly List<long> eggFoodStateCleanupScratch = new List<long>(64);
        private static readonly List<long> youngTriggerCleanupScratch = new List<long>(64);
        private static readonly EggFoodDeltaCacheEntry[] eggFoodDeltaHotCacheSlots = new EggFoodDeltaCacheEntry[EggFoodDeltaHotCacheSize];
        private static readonly YoungProtectionStateHotCacheEntry[] youngProtectionStateHotCacheSlots = new YoungProtectionStateHotCacheEntry[YoungProtectionStateHotCacheSize];
        private static readonly YoungProtectionTargetState[] youngProtectionTargetStateHotCacheSlots = new YoungProtectionTargetState[YoungProtectionTargetStateHotCacheSize];
        private static readonly ThingDef[] eggDefsByShortHash = new ThingDef[ushort.MaxValue + 1];
        private static readonly byte[] eggStatesByShortHash = new byte[ushort.MaxValue + 1];
        private static readonly ThingDef[] hatcherCompDefsByShortHash = new ThingDef[ushort.MaxValue + 1];
        private static readonly byte[] hatcherCompStatesByShortHash = new byte[ushort.MaxValue + 1];
        private static readonly Dictionary<ThingDef, bool> hatcherCompPresenceByDefFallback = new Dictionary<ThingDef, bool>(64);

        private static JobDef protectYoungJobDef;
        private static JobDef incubateEggJobDef;
        private static TickManager eggRuntimeTickManager;
        private static Game eggRuntimeGame;
        private static int eggRuntimeLastTick = -1;
        private static int eggRuntimeEnsureTick = -1;
        private static int lastEggProtectionCleanupTick = int.MinValue;
        private static int lastYoungProtectionCleanupTick = int.MinValue;
        private static int lastProtectYoungForcedFleeCleanupTick = int.MinValue;
        private static int lastEggFoodStateCacheCleanupTick = -ZoologyTickLimiter.Childcare.EggFoodStateCacheCleanupIntervalTicks;
        private static int eggFoodStateBudgetTick = -1;
        private static int eggFoodStateBudgetRemaining;
        private static int youngProtectionTargetStateBudgetTick = -1;
        private static int youngProtectionTargetStateBudgetRemaining;
        private static Map directYoungProtectionMapCacheMap;
        private static int directYoungProtectionMapCacheMapId = -1;
        private static int directYoungProtectionMapCacheTick = -1;

        public static bool IsYoungProtectionEnabled
        {
            get
            {
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                if (settings == null)
                {
                    return true;
                }

                return !settings.DisableAllRuntimePatches && settings.EnableAnimalChildcare;
            }
        }

        public static bool IsEggProtectionEnabled
        {
            get
            {
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                if (settings == null)
                {
                    return true;
                }

                return !settings.DisableAllRuntimePatches
                    && settings.EnableAnimalChildcare
                    && settings.EnableAnimalEggProtection;
            }
        }

        public static int GetEggIncubationDurationTicks() => EggIncubationDurationTicks;
        public static int GetProtectionRange() => ModConstants.ChildcareProtectionRange;
        public static int GetProtectionRangeSquared()
        {
            int range = GetProtectionRange();
            return range * range;
        }

        public static JobDef GetEggIncubationJobDef()
        {
            return incubateEggJobDef ?? (incubateEggJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_IncubateEggClutch"));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetYoungPreyScoreDelta(Pawn predator, Pawn prey)
        {
            if (predator == null
                || prey == null
                || !prey.IsAnimal
                || !ChildcareUtility.IsAnimalChild(prey)
                || CanIgnoreProtectionBecauseOfHunger(predator))
            {
                return 0f;
            }

            return TryGetYoungProtectionStateForKnownYoung(predator, prey, out YoungProtectionStateCacheEntry state) && state.IsGuarded
                ? GuardedYoungPenalty
                : 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldBlockProtectedYoungPredation(Pawn predator, Pawn prey)
        {
            if (predator == null
                || prey == null
                || !prey.IsAnimal
                || !ChildcareUtility.IsAnimalChild(prey)
                || CanIgnoreProtectionBecauseOfHunger(predator))
            {
                return false;
            }

            return TryGetYoungProtectionStateForKnownYoung(predator, prey, out YoungProtectionStateCacheEntry state) && state.IsGuarded;
        }

        public static bool CouldAnyProtectorBlockYoungPredation(Pawn prey)
        {
            if (!IsYoungProtectionEnabled
                || prey == null
                || !prey.IsAnimal
                || !ChildcareUtility.IsAnimalChild(prey))
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            return TryGetDirectYoungProtectionTargetState(prey, currentTick, out YoungProtectionTargetState targetState)
                && targetState.HasBestProtector;
        }

        internal static bool TryGetActiveProtectionAnchor(Pawn protector, Thing protectedThing, Pawn preferredHolder, out Thing activeProtectedThing, out Map map, out IntVec3 position)
        {
            activeProtectedThing = null;
            if (TryResolveActiveProtectedThing(protector, protectedThing, out Thing resolvedProtectedThing)
                && TryGetProtectionAnchor(resolvedProtectedThing, preferredHolder, out map, out position))
            {
                activeProtectedThing = resolvedProtectedThing;
                return true;
            }

            map = null;
            position = IntVec3.Invalid;
            return false;
        }

        internal static bool ShouldIgnoreHumanlikeThreatForChildcareProtection(Pawn protector, Pawn threat)
        {
            if (protector == null
                || threat == null
                || !PawnThreatUtility.IsHumanlikeOrMechanoid(threat)
                || !ZoologyModSettings.CanChildcareDefendYoungFromHumansAndMechanoidsNow(protector))
            {
                return false;
            }

            return HasProtectableChildcareTargetForHumanDefense(protector);
        }

        internal static bool HasProtectableChildcareTargetForHumanDefense(Pawn protector)
        {
            bool preventFleeWhileProtectingEggClutches = ZoologyModSettings.ShouldPreventFleeFromHumansWhileProtectingEggClutchesStatic();
            bool preventFleeWhileProtectingYoung = ZoologyModSettings.ShouldPreventFleeFromHumansWhileProtectingYoungStatic();
            if ((!preventFleeWhileProtectingEggClutches && !preventFleeWhileProtectingYoung)
                || protector == null
                || protector.Dead
                || protector.Destroyed
                || !protector.Spawned
                || protector.Map == null
                || protector.Downed
                || protector.InMentalState
                || !ChildcareUtility.HasChildcareExtension(protector)
                || ChildcareUtility.IsAnimalChild(protector)
                || !ZoologyModSettings.CanChildcareDefendYoungFromHumansAndMechanoidsNow(protector))
            {
                return false;
            }

            bool canProtectEggClutches = preventFleeWhileProtectingEggClutches && IsEggProtectionEnabled;
            bool canProtectYoung = preventFleeWhileProtectingYoung && IsYoungProtectionEnabled;
            if (!canProtectEggClutches && !canProtectYoung)
            {
                return false;
            }

            if (canProtectEggClutches && HasProtectableEggClutchForHumanDefense(protector))
            {
                return true;
            }

            return canProtectYoung && HasProtectableYoungForHumanDefense(protector);
        }

        private static bool HasProtectableEggClutchForHumanDefense(Pawn protector)
        {
            Job currentJob = protector?.CurJob;
            Thing currentProtectedThing = currentJob?.GetTarget(TargetIndex.B).Thing;
            if (IsEggThing(currentProtectedThing) && IsProtectedThingValid(currentProtectedThing))
            {
                return true;
            }

            return IsEggProtectionEnabled
                && IsProtectedThingValid(EggClutchDefenseGameComponent.Instance?.TryGetPairedEggForProtector(protector));
        }

        private static bool HasProtectableYoungForHumanDefense(Pawn protector)
        {
            return TryFindProtectedYoungForProtector(protector, out Pawn young)
                && IsProtectedThingValid(young);
        }

        private static bool ShouldUseSharedProtectionGroup(Pawn protector)
        {
            return protector != null
                && protector.RaceProps?.herdAnimal == true
                && ChildcareUtility.HasChildcareExtension(protector)
                && !ChildcareUtility.IsAnimalChild(protector);
        }

        private static bool CanIgnoreProtectionBecauseOfHunger(Pawn eater)
        {
            return eater?.needs?.food != null
                && eater.needs.food.CurLevelPercentage <= ProtectionHungerThreshold;
        }

        private static bool CanProtectorDeterCurrentJobFast(Pawn protector, Thing protectedThing)
        {
            Job curJob = protector?.CurJob;
            if (curJob == null)
            {
                return true;
            }

            if (ProtectYoungUtility.IsProtectYoungJob(curJob, protector))
            {
                return ProtectYoungUtility.IsProtectYoungJobProtecting(protector, protectedThing)
                    || ProtectYoungUtility.CanRetargetProtectYoungJob(protector, curJob, null, protectedThing);
            }

            if (ProtectPreyState.IsProtectPreyJob(protector))
            {
                return false;
            }

            if (curJob.playerForced || curJob.def == JobDefOf.AttackMelee)
            {
                return false;
            }

            return true;
        }

        private static bool IsHumanlikeOrMechanoidThreat(Pawn aggressor)
        {
            return aggressor != null && PawnThreatUtility.IsHumanlikeOrMechanoid(aggressor);
        }

        private static bool CanProtectorMeetHumanlikeThreatThreshold(Pawn protector, Pawn aggressor)
        {
            return !IsHumanlikeOrMechanoidThreat(aggressor)
                || ZoologyModSettings.CanChildcareDefendYoungFromHumansAndMechanoidsNow(protector);
        }

        private static bool TryGetProtectionAnchor(Thing protectedThing, Pawn preferredHolder, out Map map, out IntVec3 position)
        {
            map = protectedThing?.MapHeld;
            position = protectedThing?.PositionHeld ?? IntVec3.Invalid;
            if (map != null && position.IsValid)
            {
                return true;
            }

            if (protectedThing != null && IsEggThing(protectedThing))
            {
                return TryGetEggProtectionAnchor(protectedThing, preferredHolder, out map, out position);
            }

            map = null;
            position = IntVec3.Invalid;
            return false;
        }

        private static bool TryResolveActiveProtectedThing(Pawn protector, Thing protectedThing, out Thing activeProtectedThing)
        {
            activeProtectedThing = null;
            if (IsProtectedThingValid(protectedThing))
            {
                activeProtectedThing = protectedThing;
                return true;
            }

            if (protector == null || protector.Map == null)
            {
                return false;
            }

            if (protectedThing is Pawn)
            {
                if (TryFindProtectedYoungForProtector(protector, out Pawn young) && IsProtectedThingValid(young))
                {
                    activeProtectedThing = young;
                    return true;
                }

                return false;
            }

            Thing egg = EggClutchDefenseGameComponent.Instance?.TryGetPairedEggForProtector(protector);
            if (IsProtectedThingValid(egg))
            {
                activeProtectedThing = egg;
                return true;
            }

            return false;
        }

        private static bool CanProtectorGuardThing(Pawn protector, Pawn aggressor, Thing protectedThing, Pawn preferredHolder)
        {
            if (!TryGetProtectionAnchor(protectedThing, preferredHolder, out Map anchorMap, out IntVec3 anchorPosition))
            {
                return false;
            }

            return CanProtectorGuardThing(protector, aggressor, protectedThing, preferredHolder, anchorMap, anchorPosition);
        }

        private static bool CanProtectorGuardThing(
            Pawn protector,
            Pawn aggressor,
            Thing protectedThing,
            Pawn preferredHolder,
            Map anchorMap,
            IntVec3 anchorPosition)
        {
            if (protector == null
                || aggressor == null
                || protectedThing == null
                || aggressor == protector
                || !IsProtectorEligibleForDefense(protector, protectedThing)
                || IsSameFactionBlocked(protector, aggressor)
                || !CanProtectorMeetHumanlikeThreatThreshold(protector, aggressor)
                || IsAttackerTooStrong(aggressor, protector)
                || IsProtectorAcceptablePrey(aggressor, protector)
                || !CanProtectorEngage(protector, aggressor, protectedThing))
            {
                return false;
            }

            if (protector.Map != anchorMap || !anchorPosition.IsValid)
            {
                return false;
            }

            try
            {
                if (!GenSight.LineOfSight(protector.Position, anchorPosition, anchorMap))
                {
                    return false;
                }

                if (preferredHolder != null && PreyProtectionUtility.IsThingHeldByPawn(preferredHolder, protectedThing))
                {
                    return protector.CanReach(preferredHolder, PathEndMode.Touch, Danger.Deadly);
                }

                return protector.CanReach(anchorPosition, PathEndMode.Touch, Danger.Deadly);
            }
            catch
            {
                return false;
            }
        }

        private static bool CanProtectorDeterPredationFast(
            Pawn protector,
            Pawn aggressor,
            Thing protectedThing,
            Map anchorMap,
            IntVec3 anchorPosition)
        {
            if (protector == null
                || aggressor == null
                || protectedThing == null
                || anchorMap == null
                || !anchorPosition.IsValid
                || aggressor == protector
                || aggressor.Dead
                || aggressor.Destroyed
                || aggressor.MapHeld != anchorMap
                || !IsProtectorEligibleForDefense(protector, protectedThing)
                || protector.Map != anchorMap
                || !PreyProtectionUtility.IsPawnWithinProtectionRange(protector, anchorMap, anchorPosition, GetProtectionRangeSquared())
                || IsSameFactionBlocked(protector, aggressor)
                || !CanProtectorMeetHumanlikeThreatThreshold(protector, aggressor)
                || IsAttackerTooStrong(aggressor, protector)
                || !CanProtectorInterruptCurrentJobFast(protector, aggressor, protectedThing))
            {
                return false;
            }

            return true;
        }

        private static bool CanProtectorDeterPredationTargetFast(
            Pawn protector,
            Thing protectedThing,
            Map anchorMap,
            IntVec3 anchorPosition)
        {
            if (protector == null
                || protectedThing == null
                || anchorMap == null
                || !anchorPosition.IsValid
                || !IsProtectorEligibleForDefense(protector, protectedThing)
                || protector.Map != anchorMap
                || !CanProtectorDeterCurrentJobFast(protector, protectedThing))
            {
                return false;
            }

            return true;
        }

        private static bool CanProtectorInterruptCurrentJobFast(Pawn protector, Pawn newAggressor, Thing protectedThing)
        {
            Job curJob = protector?.CurJob;
            if (curJob == null)
            {
                return true;
            }

            if (ProtectYoungUtility.IsProtectYoungJob(curJob, protector))
            {
                return ProtectYoungUtility.CanRetargetProtectYoungJob(protector, curJob, newAggressor, protectedThing);
            }

            if (ProtectPreyState.IsProtectPreyJob(protector))
            {
                return false;
            }

            if (curJob.playerForced || curJob.def == JobDefOf.AttackMelee)
            {
                return false;
            }

            return true;
        }

        private static bool TryAddUniqueProtector(List<Pawn> protectors, Pawn protector)
        {
            if (protectors == null || protector == null)
            {
                return false;
            }

            for (int i = 0; i < protectors.Count; i++)
            {
                if (ReferenceEquals(protectors[i], protector))
                {
                    return false;
                }
            }

            protectors.Add(protector);
            return true;
        }

        private static bool IsSharedProtectionCandidate(
            Pawn leader,
            Pawn candidate,
            Map anchorMap,
            IntVec3 anchorPosition)
        {
            if (leader == null
                || candidate == null
                || ReferenceEquals(leader, candidate)
                || candidate.Dead
                || candidate.Destroyed
                || candidate.Downed
                || !candidate.Spawned
                || candidate.InMentalState
                || candidate.Map != leader.Map
                || !ChildcareUtility.HasChildcareExtension(candidate)
                || ChildcareUtility.IsAnimalChild(candidate)
                || !ReferenceEquals(candidate.Faction, leader.Faction)
                || !SharesSpeciesLineage(candidate, leader))
            {
                return false;
            }

            int protectionRangeSq = GetProtectionRangeSquared();
            bool nearLeader = (candidate.Position - leader.Position).LengthHorizontalSquared <= protectionRangeSq;
            bool nearAnchor = anchorMap != null
                && anchorPosition.IsValid
                && PreyProtectionUtility.IsPawnWithinProtectionRange(candidate, anchorMap, anchorPosition, protectionRangeSq);
            return nearLeader || nearAnchor;
        }

        private static void AppendSharedProtectors(
            Pawn leader,
            Pawn aggressor,
            Thing protectedThing,
            Pawn preferredHolder,
            List<Pawn> protectors)
        {
            if (leader == null
                || aggressor == null
                || protectedThing == null
                || protectors == null
                || !TryGetProtectionAnchor(protectedThing, preferredHolder, out Map anchorMap, out IntVec3 anchorPosition))
            {
                return;
            }

            if (CanProtectorGuardThing(leader, aggressor, protectedThing, preferredHolder, anchorMap, anchorPosition))
            {
                TryAddUniqueProtector(protectors, leader);
            }

            if (!ShouldUseSharedProtectionGroup(leader))
            {
                return;
            }

            IReadOnlyList<Pawn> all = leader.Map?.mapPawns?.AllPawnsSpawned;
            if (all == null)
            {
                return;
            }

            for (int i = 0; i < all.Count; i++)
            {
                Pawn candidate = all[i];
                if (!IsSharedProtectionCandidate(leader, candidate, anchorMap, anchorPosition))
                {
                    continue;
                }

                if (!CanProtectorGuardThing(candidate, aggressor, protectedThing, preferredHolder, anchorMap, anchorPosition))
                {
                    continue;
                }

                TryAddUniqueProtector(protectors, candidate);
            }
        }

        private static bool TryFillEggProtectors(Thing egg, Pawn aggressor, List<Pawn> protectors)
        {
            protectors?.Clear();
            if (!IsEggProtectionEnabled || egg == null || aggressor == null || protectors == null)
            {
                return false;
            }

            EggClutchDefenseGameComponent component = EggClutchDefenseGameComponent.Instance;
            if (component == null)
            {
                return false;
            }

            try
            {
                if (!component.TryGetProtectors(egg, childcareOwnerScratch))
                {
                    return false;
                }

                for (int i = 0; i < childcareOwnerScratch.Count; i++)
                {
                    AppendSharedProtectors(childcareOwnerScratch[i], aggressor, egg, aggressor, protectors);
                }
            }
            finally
            {
                childcareOwnerScratch.Clear();
            }

            return protectors.Count > 0;
        }

        private static bool TryFillYoungProtectors(Pawn mother, Pawn aggressor, Thing protectedThing, List<Pawn> protectors)
        {
            protectors?.Clear();
            if (!IsYoungProtectionEnabled
                || mother == null
                || aggressor == null
                || protectedThing == null
                || protectors == null)
            {
                return false;
            }

            AppendSharedProtectors(mother, aggressor, protectedThing, null, protectors);
            return protectors.Count > 0;
        }

        private static bool HasAnyYoungProtector(Pawn mother, Pawn aggressor, Thing protectedThing)
        {
            if (!IsYoungProtectionEnabled
                || mother == null
                || aggressor == null
                || protectedThing == null
                || !TryGetProtectionAnchor(protectedThing, null, out Map anchorMap, out IntVec3 anchorPosition))
            {
                return false;
            }

            if (CanProtectorDeterPredationFast(mother, aggressor, protectedThing, anchorMap, anchorPosition))
            {
                return true;
            }

            if (!ShouldUseSharedProtectionGroup(mother))
            {
                return false;
            }

            return HasAnySharedYoungProtectorNearAnchor(mother, aggressor, protectedThing, anchorMap, anchorPosition);
        }

        private static bool HasAnySharedYoungProtectorNearAnchor(
            Pawn mother,
            Pawn aggressor,
            Thing protectedThing,
            Map anchorMap,
            IntVec3 anchorPosition)
        {
            if (mother?.Map != anchorMap || anchorMap == null || !anchorPosition.IsValid)
            {
                return false;
            }

            int cellCount = GenRadial.NumCellsInRadius(GetProtectionRange());
            for (int i = 0; i < cellCount; i++)
            {
                IntVec3 cell = anchorPosition + GenRadial.RadialPattern[i];
                if (!cell.InBounds(anchorMap))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(anchorMap);
                for (int j = 0; j < things.Count; j++)
                {
                    if (things[j] is not Pawn candidate)
                    {
                        continue;
                    }

                    if (!IsSharedProtectionCandidate(mother, candidate, anchorMap, anchorPosition))
                    {
                        continue;
                    }

                    if (CanProtectorDeterPredationFast(candidate, aggressor, protectedThing, anchorMap, anchorPosition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetDirectYoungProtectionTargetState(Pawn young, int currentTick, out YoungProtectionTargetState state)
        {
            state = default;
            if (young == null || young.thingIDNumber <= 0 || currentTick <= 0)
            {
                return false;
            }

            if (TryRefreshDirectYoungProtectionMapCache(young.MapHeld, currentTick))
            {
                if (directYoungProtectionStateByYoungId.TryGetValue(young.thingIDNumber, out state))
                {
                    return true;
                }

                state = new YoungProtectionTargetState
                {
                    YoungId = young.thingIDNumber,
                    Tick = directYoungProtectionMapCacheTick,
                    AnchorMap = young.MapHeld
                };
                return true;
            }

            int slotIndex = GetYoungProtectionTargetStateCacheSlotIndex(young.thingIDNumber);
            YoungProtectionTargetState cached = youngProtectionTargetStateHotCacheSlots[slotIndex];
            if (cached.YoungId == young.thingIDNumber
                && cached.Tick > 0
                && currentTick - cached.Tick <= ZoologyTickLimiter.Childcare.YoungProtectionTargetStateCacheDurationTicks + (young.thingIDNumber & 63))
            {
                state = cached;
                return true;
            }

            state = BuildDirectYoungProtectionTargetState(young, currentTick);
            youngProtectionTargetStateHotCacheSlots[slotIndex] = state;
            return true;
        }

        private static bool TryRefreshDirectYoungProtectionMapCache(Map map, int currentTick)
        {
            int mapId = map?.uniqueID ?? -1;
            if (map == null || mapId < 0 || currentTick <= 0)
            {
                return false;
            }

            if (ReferenceEquals(directYoungProtectionMapCacheMap, map)
                && directYoungProtectionMapCacheMapId == mapId
                && directYoungProtectionMapCacheTick > 0
                && currentTick - directYoungProtectionMapCacheTick <= DirectYoungProtectionMapCacheDurationTicks)
            {
                return true;
            }

            directYoungProtectionStateByYoungId.Clear();
            directYoungProtectionMapCacheMap = map;
            directYoungProtectionMapCacheMapId = mapId;
            directYoungProtectionMapCacheTick = currentTick;

            IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return true;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn young = pawns[i];
                if (young == null
                    || !young.IsAnimal
                    || !ChildcareUtility.IsAnimalChild(young))
                {
                    continue;
                }

                YoungProtectionTargetState state = BuildDirectYoungProtectionTargetState(young, currentTick);
                if (state.HasBestProtector)
                {
                    directYoungProtectionStateByYoungId[young.thingIDNumber] = state;
                }
            }

            return true;
        }

        private static YoungProtectionTargetState BuildDirectYoungProtectionTargetState(Pawn young, int currentTick)
        {
            YoungProtectionTargetState state = new YoungProtectionTargetState
            {
                YoungId = young?.thingIDNumber ?? 0,
                Tick = currentTick
            };

            if (young == null
                || !ChildcareUtility.TryGetBiologicalMother(young, out Pawn mother)
                || !ChildcareUtility.HasChildcareExtension(mother)
                || !TryGetProtectionAnchor(young, null, out Map anchorMap, out IntVec3 anchorPosition))
            {
                return state;
            }

            state.AnchorMap = anchorMap;
            TryAddYoungProtectionTargetCandidate(ref state, mother, young, anchorMap, anchorPosition);
            if (ShouldUseSharedProtectionGroup(mother))
            {
                AddSharedYoungProtectionTargetCandidates(ref state, mother, young, anchorMap, anchorPosition);
            }

            return state;
        }

        private static bool TryGetYoungProtectionTargetState(Pawn young, int currentTick, out YoungProtectionTargetState state)
        {
            state = default;
            if (young == null || young.thingIDNumber <= 0 || currentTick <= 0)
            {
                return false;
            }

            int slotIndex = GetYoungProtectionTargetStateCacheSlotIndex(young.thingIDNumber);
            YoungProtectionTargetState cached = youngProtectionTargetStateHotCacheSlots[slotIndex];
            bool hasCached = cached.YoungId == young.thingIDNumber && cached.Tick > 0;
            if (hasCached)
            {
                int age = currentTick - cached.Tick;
                int jitter = young.thingIDNumber & 63;
                if (age <= ZoologyTickLimiter.Childcare.YoungProtectionTargetStateCacheDurationTicks + jitter)
                {
                    state = cached;
                    return true;
                }

                if (!TryConsumeBudget(
                        ref youngProtectionTargetStateBudgetTick,
                        ref youngProtectionTargetStateBudgetRemaining,
                        ZoologyTickLimiter.Childcare.YoungProtectionTargetStateBudgetPerTick)
                    && age <= ZoologyTickLimiter.Childcare.YoungProtectionTargetStateMaxStaleTicks)
                {
                    state = cached;
                    return true;
                }
            }
            else if (!TryConsumeBudget(
                         ref youngProtectionTargetStateBudgetTick,
                         ref youngProtectionTargetStateBudgetRemaining,
                         ZoologyTickLimiter.Childcare.YoungProtectionTargetStateBudgetPerTick))
            {
                return false;
            }

            state = BuildYoungProtectionTargetState(young, currentTick);
            youngProtectionTargetStateHotCacheSlots[slotIndex] = state;
            return true;
        }

        private static YoungProtectionTargetState BuildYoungProtectionTargetState(Pawn young, int currentTick)
        {
            YoungProtectionTargetState state = new YoungProtectionTargetState
            {
                YoungId = young?.thingIDNumber ?? 0,
                Tick = currentTick
            };

            if (young == null
                || !ChildcareUtility.TryGetKnownBiologicalMother(young, out Pawn mother)
                || !ChildcareUtility.HasChildcareExtension(mother)
                || !TryGetProtectionAnchor(young, null, out Map anchorMap, out IntVec3 anchorPosition))
            {
                return state;
            }

            state.AnchorMap = anchorMap;
            TryAddYoungProtectionTargetCandidate(ref state, mother, young, anchorMap, anchorPosition);

            if (ShouldUseSharedProtectionGroup(mother))
            {
                AddSharedYoungProtectionTargetCandidates(ref state, mother, young, anchorMap, anchorPosition);
            }

            return state;
        }

        private static void AddSharedYoungProtectionTargetCandidates(
            ref YoungProtectionTargetState state,
            Pawn mother,
            Thing protectedThing,
            Map anchorMap,
            IntVec3 anchorPosition)
        {
            if (mother?.Map != anchorMap || anchorMap == null || !anchorPosition.IsValid)
            {
                return;
            }

            int cellCount = GenRadial.NumCellsInRadius(GetProtectionRange());
            for (int i = 0; i < cellCount; i++)
            {
                IntVec3 cell = anchorPosition + GenRadial.RadialPattern[i];
                if (!cell.InBounds(anchorMap))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(anchorMap);
                for (int j = 0; j < things.Count; j++)
                {
                    if (things[j] is not Pawn candidate)
                    {
                        continue;
                    }

                    if (!IsSharedProtectionCandidate(mother, candidate, anchorMap, anchorPosition))
                    {
                        continue;
                    }

                    TryAddYoungProtectionTargetCandidate(ref state, candidate, protectedThing, anchorMap, anchorPosition);
                }
            }
        }

        private static void TryAddYoungProtectionTargetCandidate(
            ref YoungProtectionTargetState state,
            Pawn protector,
            Thing protectedThing,
            Map anchorMap,
            IntVec3 anchorPosition)
        {
            if (!CanProtectorDeterPredationTargetFast(protector, protectedThing, anchorMap, anchorPosition))
            {
                return;
            }

            float power = AnimalCombatPowerUtility.GetAdjustedCombatPower(protector);
            Faction blockedFaction = GetSameFactionBlockedFaction(protector);
            if (!state.HasBestProtector || power > state.BestProtectorPower)
            {
                if (state.HasBestProtector && !ReferenceEquals(state.BestProtectorBlockedFaction, blockedFaction))
                {
                    state.HasFallbackProtector = true;
                    state.FallbackProtectorPower = state.BestProtectorPower;
                }

                state.HasBestProtector = true;
                state.BestProtectorPower = power;
                state.BestProtectorBlockedFaction = blockedFaction;
                return;
            }

            if (!ReferenceEquals(state.BestProtectorBlockedFaction, blockedFaction)
                && (!state.HasFallbackProtector || power > state.FallbackProtectorPower))
            {
                state.HasFallbackProtector = true;
                state.FallbackProtectorPower = power;
            }
        }

        private static bool TryGetYoungProtectionPowerAgainst(Pawn aggressor, YoungProtectionTargetState state, out float protectorPower)
        {
            protectorPower = 0f;
            if (aggressor == null || !state.HasBestProtector || aggressor.MapHeld != state.AnchorMap)
            {
                return false;
            }

            if (state.BestProtectorBlockedFaction == null || !ReferenceEquals(state.BestProtectorBlockedFaction, aggressor.Faction))
            {
                protectorPower = state.BestProtectorPower;
                return true;
            }

            if (!state.HasFallbackProtector)
            {
                return false;
            }

            protectorPower = state.FallbackProtectorPower;
            return true;
        }

        private static bool DoesYoungProtectionStateBlockAggressor(Pawn aggressor, YoungProtectionTargetState targetState)
        {
            if (!TryGetYoungProtectionPowerAgainst(aggressor, targetState, out float protectorPower))
            {
                return false;
            }

            if (IsHumanlikeOrMechanoidThreat(aggressor)
                && protectorPower < ModConstants.MinCombatPowerToDefendYoungFromHumans)
            {
                return false;
            }

            float aggressorPower = AnimalCombatPowerUtility.GetAdjustedCombatPower(aggressor);
            if (aggressorPower <= 0f || protectorPower <= 0f)
            {
                return true;
            }

            return aggressorPower < protectorPower * ModConstants.CombatPowerDominanceFactor;
        }

        private static Faction GetSameFactionBlockedFaction(Pawn protector)
        {
            Faction faction = protector?.Faction;
            if (faction == null)
            {
                return null;
            }

            FactionDef def = faction.def;
            if (def != null
                && def.defName != null
                && def.defName.Equals("Photonozoa", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return faction;
        }

        private static int GetYoungProtectionTargetStateCacheSlotIndex(int youngId)
        {
            uint mixed = (uint)youngId;
            mixed *= 2654435761u;
            return (int)(mixed & YoungProtectionTargetStateHotCacheMask);
        }

        private static bool TryFindProtectedYoungForProtector(Pawn protector, out Pawn young)
        {
            young = null;
            if (!IsYoungProtectionEnabled
                || protector == null
                || protector.Map?.mapPawns?.AllPawnsSpawned == null
                || protector.thingIDNumber <= 0)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int protectorId = protector.thingIDNumber;
            if (currentTick > 0
                && nearbyYoungCacheByProtectorId.TryGetValue(protectorId, out NearbyYoungCacheEntry cached)
                && currentTick - cached.Tick <= YoungProtectionStateCacheDurationTicks)
            {
                if (!cached.HasYoung)
                {
                    return false;
                }

                if (IsProtectedThingValid(cached.Young))
                {
                    young = cached.Young;
                    return true;
                }

                nearbyYoungCacheByProtectorId.Remove(protectorId);
            }

            Pawn bestYoung = null;
            int bestDistanceSq = int.MaxValue;
            IReadOnlyList<Pawn> all = protector.Map.mapPawns.AllPawnsSpawned;
            int sharedRangeSq = GetProtectionRangeSquared();

            for (int i = 0; i < all.Count; i++)
            {
                Pawn candidate = all[i];
                if (candidate == null
                    || candidate == protector
                    || !candidate.IsAnimal
                    || !ChildcareUtility.IsAnimalChild(candidate)
                    || !candidate.Spawned
                    || candidate.Dead
                    || candidate.Destroyed
                    || candidate.Map != protector.Map
                    || !ChildcareUtility.TryGetBiologicalMother(candidate, out Pawn mother))
                {
                    continue;
                }

                if (!ChildcareUtility.HasChildcareExtension(mother))
                {
                    continue;
                }

                bool matchesProtector = ReferenceEquals(mother, protector);
                if (!matchesProtector)
                {
                    matchesProtector = ShouldUseSharedProtectionGroup(mother)
                        && ReferenceEquals(mother.Faction, protector.Faction)
                        && SharesSpeciesLineage(mother, protector)
                        && mother.Spawned
                        && mother.Map == protector.Map
                        && (mother.Position - protector.Position).LengthHorizontalSquared <= sharedRangeSq;
                }

                if (!matchesProtector)
                {
                    continue;
                }

                int distanceSq = (candidate.Position - protector.Position).LengthHorizontalSquared;
                if (distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestYoung = candidate;
                bestDistanceSq = distanceSq;
            }

            if (currentTick > 0)
            {
                nearbyYoungCacheByProtectorId[protectorId] = new NearbyYoungCacheEntry(bestYoung, currentTick, bestYoung != null);
            }

            if (bestYoung == null)
            {
                return false;
            }

            young = bestYoung;
            return true;
        }

        public static bool TryTriggerEggProtection(Pawn aggressor, Thing egg)
        {
            if (!IsEggProtectionEnabled || aggressor == null || egg == null || !IsFertilizedEgg(egg))
            {
                return false;
            }

            if (!ShouldTriggerEggProtection(aggressor, egg))
            {
                return false;
            }

            int triggeredCount = 0;
            Pawn exemplar = null;
            bool anyTriggered = false;

            try
            {
                triggeredProtectorsScratch.Clear();
                if (!TryFillEggProtectors(egg, aggressor, eggProtectorsScratch))
                {
                    return false;
                }

                for (int i = 0; i < eggProtectorsScratch.Count; i++)
                {
                    Pawn protector = eggProtectorsScratch[i];
                    if (!TryTakeProtectionJob(protector, aggressor, egg))
                    {
                        continue;
                    }

                    anyTriggered = true;
                    triggeredCount++;
                    exemplar ??= protector;
                    TryAddUniqueProtector(triggeredProtectorsScratch, protector);
                }
            }
            finally
            {
                eggProtectorsScratch.Clear();
            }

            if (!anyTriggered)
            {
                triggeredProtectorsScratch.Clear();
                return false;
            }

            RememberEggProtectionTrigger(aggressor, egg);
            TryNotifyPlayerAboutEggProtection(aggressor, egg, exemplar, triggeredProtectorsScratch, triggeredCount);
            triggeredProtectorsScratch.Clear();
            return true;
        }

        internal static bool ShouldCheckEggFoodOptimality(Pawn eater, Thing foodSource)
        {
            if (!IsEggProtectionEnabled || eater == null || foodSource == null || !IsFertilizedEgg(foodSource))
            {
                return false;
            }

            if (!TryGetEggProtectionAnchor(foodSource, eater, out Map anchorMap, out _)
                || eater.MapHeld == null
                || eater.MapHeld != anchorMap)
            {
                return false;
            }

            EggClutchDefenseGameComponent component = EggClutchDefenseGameComponent.Instance;
            if (component == null)
            {
                return false;
            }

            return (!CanIgnoreProtectionBecauseOfHunger(eater)
                    && component.ShouldBlockOwnFertilizedEggConsumption(eater, foodSource))
                || component.HasPotentialFoodOptimalityProtector(foodSource, eater, anchorMap);
        }

        public static float GetFoodOptimalityDeltaForEgg(Pawn eater, Thing foodSource)
        {
            if (!ShouldCheckEggFoodOptimality(eater, foodSource))
            {
                return 0f;
            }

            return GetFoodOptimalityDeltaForCheckedEgg(eater, foodSource);
        }

        internal static float GetFoodOptimalityDeltaForCheckedEgg(Pawn eater, Thing foodSource)
        {
            TickManager tickManager = Find.TickManager;
            int currentTick = tickManager?.TicksGame ?? 0;
            EnsureEggRuntimeCacheState(currentTick, tickManager);

            long pairKey = MakePairKey(eater, foodSource);
            if (TryGetEggFoodDeltaCached(currentTick, pairKey, out float cachedDelta))
            {
                return cachedDelta;
            }

            if (!ZoologyTickLimiter.TryConsumeFoodOptimality(ZoologyTickLimiter.FoodOptimalityBudgetPerTick))
            {
                return 0f;
            }

            float delta = 0f;
            EggClutchDefenseGameComponent component = EggClutchDefenseGameComponent.Instance;
            if (ShouldBlockEggConsumption(eater, foodSource))
            {
                delta = GuardedEggPenalty;
            }
            else if (component != null && TryGetEggFoodState(component, eater, foodSource, out EggFoodStateCacheEntry state))
            {
                if (state.IsGuarded)
                {
                    delta = GuardedEggPenalty;
                }
            }

            StoreEggFoodDelta(currentTick, pairKey, delta);
            return delta;
        }

        public static bool TryOrderProtection(Pawn protector, Pawn aggressor, Thing protectedThing)
        {
            if (!IsYoungProtectionEnabled
                || protector == null
                || aggressor == null
                || protectedThing == null)
            {
                return false;
            }

            if (!ShouldTriggerYoungProtection(aggressor, protectedThing))
            {
                return false;
            }

            int triggeredCount = 0;
            Pawn exemplar = null;
            bool anyTriggered = false;

            try
            {
                triggeredProtectorsScratch.Clear();
                if (!TryFillYoungProtectors(protector, aggressor, protectedThing, youngProtectorsScratch))
                {
                    return false;
                }

                for (int i = 0; i < youngProtectorsScratch.Count; i++)
                {
                    Pawn youngProtector = youngProtectorsScratch[i];
                    if (!TryTakeProtectionJob(youngProtector, aggressor, protectedThing))
                    {
                        continue;
                    }

                    anyTriggered = true;
                    triggeredCount++;
                    exemplar ??= youngProtector;
                    TryAddUniqueProtector(triggeredProtectorsScratch, youngProtector);
                }
            }
            finally
            {
                youngProtectorsScratch.Clear();
            }

            if (!anyTriggered)
            {
                triggeredProtectorsScratch.Clear();
                return false;
            }

            RememberYoungProtectionTrigger(aggressor, protectedThing);
            TryNotifyPlayerAboutYoungProtection(aggressor, protectedThing, exemplar, triggeredProtectorsScratch, triggeredCount);
            triggeredProtectorsScratch.Clear();
            return true;
        }

        public static bool ShouldNullifyEggDeterioration(Thing thing)
        {
            if (!IsEggProtectionEnabled || thing == null || thing.Destroyed || !IsEggThing(thing))
            {
                return false;
            }

            Pawn mother = thing.TryGetComp<CompHatcher>()?.hatcheeParent;
            if (mother == null)
            {
                EggClutchDefenseGameComponent.Instance?.TryGetAnyMother(thing, out mother);
            }

            if (mother == null)
            {
                return false;
            }

            if (!ChildcareUtility.HasChildcareExtension(mother))
            {
                return true;
            }

            return IsFertilizedEgg(thing) && IsEggIncubatedNow(thing);
        }

        public static void RegisterNearbyLaidEggsForMother(Pawn mother)
        {
            if (!IsEggProtectionEnabled
                || mother == null
                || mother.Map == null
                || !mother.Spawned)
            {
                return;
            }

            EggClutchDefenseGameComponent component = EggClutchDefenseGameComponent.Instance;
            if (component == null)
            {
                return;
            }

            try
            {
                Map map = mother.Map;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(mother.Position, 1.9f, true))
                {
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    List<Thing> things = cell.GetThingList(map);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing thing = things[i];
                        if (!IsEggThing(thing) || !IsEggCompatibleWithMother(mother, thing))
                        {
                            continue;
                        }

                        component.RegisterOwnership(mother, thing);
                    }
                }
            }
            catch
            {
            }
        }

        public static Thing TryFindEggIncubationTarget(Pawn mother)
        {
            if (!CanUseEggIncubation(mother))
            {
                return null;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick > 0
                && lastIncubationSearchFailureTickByPawnId.TryGetValue(mother.thingIDNumber, out int lastFailTick)
                && currentTick - lastFailTick < EggIncubationSearchFailCooldownTicks)
            {
                return null;
            }

            Thing egg = GenClosest.ClosestThingReachable(
                mother.Position,
                mother.Map,
                ThingRequest.ForGroup(ThingRequestGroup.FoodSource),
                PathEndMode.Touch,
                TraverseParms.For(mother),
                EggIncubationSearchRadius,
                thing => IsEggIncubationTargetForMother(mother, thing));

            if (egg == null)
            {
                if (currentTick > 0)
                {
                    lastIncubationSearchFailureTickByPawnId[mother.thingIDNumber] = currentTick;
                }
            }
            else
            {
                lastIncubationSearchFailureTickByPawnId.Remove(mother.thingIDNumber);
            }

            return egg;
        }

        public static bool CanContinueEggIncubation(Pawn mother, Thing egg)
        {
            if (!CanUseEggIncubation(mother))
            {
                return false;
            }

            return IsEggIncubationTargetForMother(mother, egg, requireReservationAvailability: false);
        }

        public static void MarkEggIncubated(Thing egg)
        {
            if (egg == null)
            {
                return;
            }

            TickManager tickManager = Find.TickManager;
            int currentTick = tickManager?.TicksGame ?? 0;
            EnsureEggRuntimeCacheState(currentTick, tickManager);
            if (currentTick > 0)
            {
                incubatedEggTouchTickByEggId[egg.thingIDNumber] = currentTick;
            }
        }

        public static void StopEggIncubation(Thing egg)
        {
            if (egg == null)
            {
                return;
            }

            incubatedEggTouchTickByEggId.Remove(egg.thingIDNumber);
        }

        public static void FinishEggIncubation(Thing egg)
        {
            StopEggIncubation(egg);
            if (egg == null || egg.Destroyed)
            {
                return;
            }

            try
            {
                egg.HitPoints = egg.MaxHitPoints;
            }
            catch
            {
            }
        }

        internal static bool ShouldBlockEggConsumption(Pawn eater, Thing foodSource)
        {
            if (!IsEggProtectionEnabled || eater == null || foodSource == null || !IsFertilizedEgg(foodSource))
            {
                return false;
            }

            EggClutchDefenseGameComponent component = EggClutchDefenseGameComponent.Instance;
            if (component == null || CanIgnoreProtectionBecauseOfHunger(eater))
            {
                return false;
            }

            if (component.ShouldBlockOwnFertilizedEggConsumption(eater, foodSource))
            {
                return true;
            }

            return TryGetEggFoodState(component, eater, foodSource, out EggFoodStateCacheEntry state) && state.IsGuarded;
        }

        internal static void HandleEggSplit(Thing source, Thing piece)
        {
            if (!IsEggProtectionEnabled || source == null || piece == null)
            {
                return;
            }

            if (!IsEggThing(source) && !IsEggThing(piece))
            {
                return;
            }

            EggClutchDefenseGameComponent.Instance?.HandleEggSplit(source, piece);
        }

        internal static void HandleEggAbsorb(Thing target, Thing source, int countToTake)
        {
            if (!IsEggProtectionEnabled || target == null || source == null || countToTake <= 0)
            {
                return;
            }

            if (!IsEggThing(target) && !IsEggThing(source))
            {
                return;
            }

            EggClutchDefenseGameComponent.Instance?.HandleEggAbsorb(target, source, countToTake);
        }

        private static bool CanUseEggIncubation(Pawn mother)
        {
            if (!IsEggProtectionEnabled
                || mother == null
                || !mother.IsAnimal
                || mother.gender != Gender.Female
                || !mother.Spawned
                || mother.Map == null
                || mother.Dead
                || mother.Destroyed
                || mother.Downed
                || mother.InMentalState
                || !PreyProtectionUtility.IsPawnAwakeForProtection(mother)
                || !ChildcareUtility.HasChildcareExtension(mother))
            {
                return false;
            }

            return mother.TryGetComp<CompEggLayer>() != null;
        }

        private static bool IsEggIncubationTargetForMother(Pawn mother, Thing egg, bool requireReservationAvailability = true)
        {
            if (mother == null
                || egg == null
                || egg.Destroyed
                || !egg.Spawned
                || egg.Map != mother.Map
                || egg.IsForbidden(mother)
                || !IsFertilizedEgg(egg)
                || !NeedsEggIncubation(egg))
            {
                return false;
            }

            EggClutchDefenseGameComponent component = EggClutchDefenseGameComponent.Instance;
            if (component == null || !component.IsAssociatedWithMother(egg, mother))
            {
                return false;
            }

            if (requireReservationAvailability && !mother.CanReserve(egg))
            {
                return false;
            }

            return true;
        }

        private static bool NeedsEggIncubation(Thing egg)
        {
            if (egg == null || egg.Destroyed || !egg.Spawned || egg.Map == null || !egg.PositionHeld.IsValid)
            {
                return false;
            }

            return egg.MaxHitPoints > 0 && egg.HitPoints * 5 < egg.MaxHitPoints * 4;
        }

        private static bool IsEggCompatibleWithMother(Pawn mother, Thing egg)
        {
            if (mother == null || egg == null)
            {
                return false;
            }

            Pawn knownMother = egg.TryGetComp<CompHatcher>()?.hatcheeParent;
            if (knownMother == null)
            {
                EggClutchDefenseGameComponent.Instance?.TryGetAnyMother(egg, out knownMother);
            }

            return knownMother == null || SharesSpeciesLineage(mother, knownMother);
        }

        private static bool CanProtectorGuardEgg(Pawn protector, Pawn aggressor, Thing egg)
        {
            return CanProtectorGuardThing(protector, aggressor, egg, aggressor);
        }

        private static bool TryGetEggProtectionAnchor(Thing egg, Pawn preferredHolder, out Map map, out IntVec3 position)
        {
            map = egg?.MapHeld;
            position = egg?.PositionHeld ?? IntVec3.Invalid;
            if (map != null && position.IsValid)
            {
                return true;
            }

            map = null;
            position = IntVec3.Invalid;
            if (egg == null || preferredHolder == null || preferredHolder.Map == null)
            {
                return false;
            }

            if (!PreyProtectionUtility.IsThingHeldByPawn(preferredHolder, egg))
            {
                return false;
            }

            map = preferredHolder.Map;
            position = preferredHolder.Position;
            return position.IsValid;
        }

        private static bool TryTakeProtectionJob(Pawn protector, Pawn aggressor, Thing protectedThing)
        {
            try
            {
                JobDef jobDef = protectYoungJobDef ?? (protectYoungJobDef = DefDatabase<JobDef>.GetNamedSilentFail(ProtectYoungUtility.ProtectYoungDefName));
                Job job = jobDef != null
                    ? JobMaker.MakeJob(jobDef, aggressor, protectedThing)
                    : JobMaker.MakeJob(JobDefOf.AttackMelee, aggressor);

                if (protector.jobs == null)
                {
                    return false;
                }

                ProtectYoungUtility.InterruptRetargetableProtectYoungJob(protector, aggressor, protectedThing);
                protector.jobs.StartJob(
                    job,
                    JobCondition.InterruptForced,
                    null,
                    resumeCurJobAfterwards: false,
                    cancelBusyStances: true,
                    null,
                    JobTag.Misc);
                TryForceAggressorFleeFromProtectYoung(protector, aggressor);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: failed to order childcare protection job: {ex}");
                return false;
            }
        }

        private static void TryForceAggressorFleeFromProtectYoung(Pawn protector, Pawn aggressor)
        {
            try
            {
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                if (protector == null
                    || aggressor == null
                    || protector.Map == null
                    || aggressor.Map != protector.Map
                    || aggressor.jobs == null
                    || aggressor.CurJob?.def == JobDefOf.Flee
                    || HasRecentProtectYoungForcedFlee(aggressor, currentTick)
                    || !ZoologyFleeSafetyUtility.CanUseForcedThreatFlee(aggressor, ignoreNeverFleeFromEnemies: true)
                    || !ZoologyFleeSafetyUtility.IsValidThreatForFlee(protector, aggressor)
                    || ZoologyFleeSafetyUtility.IsThreatMeleeAttackingPawn(protector, aggressor))
                {
                    return;
                }

                int fleeDistance = ZoologyModSettings.Instance?.FleeDistanceTargetPredator ?? ModConstants.DefaultFleeDistanceTargetPredator;
                if (fleeDistance < 6)
                {
                    fleeDistance = 6;
                }
                else if (fleeDistance > 40)
                {
                    fleeDistance = 40;
                }

                Job fleeJob = FleeUtility.FleeJob(aggressor, protector, fleeDistance);
                if (fleeJob == null)
                {
                    return;
                }

                RememberProtectYoungForcedFlee(aggressor, currentTick);
                aggressor.jobs.StartJob(
                    fleeJob,
                    JobCondition.InterruptForced,
                    null,
                    resumeCurJobAfterwards: false,
                    cancelBusyStances: true,
                    null,
                    JobTag.Misc);
            }
            catch
            {
            }
        }

        private static bool HasRecentProtectYoungForcedFlee(Pawn aggressor, int currentTick)
        {
            if (aggressor == null || aggressor.thingIDNumber <= 0 || currentTick <= 0)
            {
                return false;
            }

            return recentProtectYoungForcedFleeTickByAggressorId.TryGetValue(aggressor.thingIDNumber, out int tick)
                && tick == currentTick;
        }

        private static void RememberProtectYoungForcedFlee(Pawn aggressor, int currentTick)
        {
            if (aggressor == null || aggressor.thingIDNumber <= 0 || currentTick <= 0)
            {
                return;
            }

            recentProtectYoungForcedFleeTickByAggressorId[aggressor.thingIDNumber] = currentTick;
            if (currentTick - lastProtectYoungForcedFleeCleanupTick < 600)
            {
                return;
            }

            lastProtectYoungForcedFleeCleanupTick = currentTick;
            youngTriggerCleanupScratch.Clear();
            foreach (KeyValuePair<int, int> entry in recentProtectYoungForcedFleeTickByAggressorId)
            {
                if (currentTick - entry.Value > 60)
                {
                    youngTriggerCleanupScratch.Add(entry.Key);
                }
            }

            for (int i = 0; i < youngTriggerCleanupScratch.Count; i++)
            {
                recentProtectYoungForcedFleeTickByAggressorId.Remove((int)youngTriggerCleanupScratch[i]);
            }

            youngTriggerCleanupScratch.Clear();
        }

        private static bool ShouldTriggerEggProtection(Pawn aggressor, Thing egg)
        {
            if (aggressor.Dead
                || aggressor.Destroyed
                || aggressor.MapHeld == null
                || !TryGetEggProtectionAnchor(egg, aggressor, out Map eggMap, out _)
                || aggressor.MapHeld != eggMap)
            {
                return false;
            }

            CleanupRecentEggProtectionTriggers();
            long pairKey = MakePairKey(aggressor, egg);
            if (pairKey == 0L)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (recentEggProtectionTriggerByPairKey.TryGetValue(pairKey, out int lastTriggerTick)
                && currentTick - lastTriggerTick < EggProtectionTriggerCooldownTicks)
            {
                return false;
            }

            return true;
        }

        private static bool ShouldTriggerYoungProtection(Pawn aggressor, Thing protectedYoung)
        {
            if (aggressor == null
                || protectedYoung == null
                || aggressor.Dead
                || aggressor.Destroyed
                || aggressor.MapHeld == null
                || protectedYoung.MapHeld == null
                || aggressor.MapHeld != protectedYoung.MapHeld)
            {
                return false;
            }

            CleanupRecentYoungProtectionTriggers();
            long pairKey = MakePairKey(aggressor, protectedYoung);
            if (pairKey == 0L)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (recentYoungProtectionTriggerByPairKey.TryGetValue(pairKey, out int lastTriggerTick)
                && currentTick - lastTriggerTick < YoungProtectionTriggerCooldownTicks)
            {
                return false;
            }

            return true;
        }

        private static void RememberEggProtectionTrigger(Pawn aggressor, Thing egg)
        {
            long pairKey = MakePairKey(aggressor, egg);
            if (pairKey == 0L)
            {
                return;
            }

            recentEggProtectionTriggerByPairKey[pairKey] = Find.TickManager?.TicksGame ?? 0;
        }

        private static void RememberYoungProtectionTrigger(Pawn aggressor, Thing protectedYoung)
        {
            long pairKey = MakePairKey(aggressor, protectedYoung);
            if (pairKey == 0L)
            {
                return;
            }

            recentYoungProtectionTriggerByPairKey[pairKey] = Find.TickManager?.TicksGame ?? 0;
        }

        private static void CleanupRecentYoungProtectionTriggers()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick == lastYoungProtectionCleanupTick || recentYoungProtectionTriggerByPairKey.Count == 0)
            {
                return;
            }

            lastYoungProtectionCleanupTick = currentTick;
            if (recentYoungProtectionTriggerByPairKey.Count < 64 || currentTick % 120 != 0)
            {
                return;
            }

            youngTriggerCleanupScratch.Clear();
            foreach (KeyValuePair<long, int> entry in recentYoungProtectionTriggerByPairKey)
            {
                if (currentTick - entry.Value >= YoungProtectionTriggerCooldownTicks)
                {
                    youngTriggerCleanupScratch.Add(entry.Key);
                }
            }

            for (int i = 0; i < youngTriggerCleanupScratch.Count; i++)
            {
                recentYoungProtectionTriggerByPairKey.Remove(youngTriggerCleanupScratch[i]);
            }

            youngTriggerCleanupScratch.Clear();
        }

        private static void CleanupRecentEggProtectionTriggers()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick == lastEggProtectionCleanupTick || recentEggProtectionTriggerByPairKey.Count == 0)
            {
                return;
            }

            lastEggProtectionCleanupTick = currentTick;
            if (recentEggProtectionTriggerByPairKey.Count < 64 || currentTick % 120 != 0)
            {
                return;
            }

            eggTriggerCleanupScratch.Clear();
            foreach (KeyValuePair<long, int> entry in recentEggProtectionTriggerByPairKey)
            {
                if (currentTick - entry.Value >= EggProtectionTriggerCooldownTicks)
                {
                    eggTriggerCleanupScratch.Add(entry.Key);
                }
            }

            for (int i = 0; i < eggTriggerCleanupScratch.Count; i++)
            {
                recentEggProtectionTriggerByPairKey.Remove(eggTriggerCleanupScratch[i]);
            }

            eggTriggerCleanupScratch.Clear();
        }

        private static void TryNotifyPlayerAboutEggProtection(Pawn aggressor, Thing egg, Pawn exemplar, IReadOnlyList<Pawn> triggeredProtectors, int triggeredCount)
        {
            if (aggressor == null
                || aggressor.Faction != Faction.OfPlayer
                || exemplar == null
                || egg == null
                || EggClutchDefenseGameComponent.IsProtectionNotificationSuppressedForEgg(egg.thingIDNumber))
            {
                return;
            }

            try
            {
                bool pack = triggeredCount > 1;
                string label;
                string text;

                if (pack)
                {
                    string collectiveLabel = ZoologyNotificationUtility.GetCollectiveAnimalLabel(exemplar, triggeredCount);
                    label = "LetterLabelAnimalProtectingEggsPack".Translate(collectiveLabel);
                    text = "LetterAnimalProtectingEggsPack".Translate(collectiveLabel, aggressor.LabelDefinite());
                    if (label.NullOrEmpty() || label.Contains("LetterLabelAnimalProtectingEggsPack"))
                    {
                        label = $"{collectiveLabel} are protecting a clutch";
                    }

                    if (text.NullOrEmpty() || text.Contains("LetterAnimalProtectingEggsPack"))
                    {
                        text = $"{collectiveLabel} are protecting a clutch and are attacking {aggressor.LabelDefinite()}.";
                    }
                }
                else
                {
                    label = "LetterLabelAnimalProtectingEggs".Translate(exemplar.LabelShort, aggressor.LabelDefinite(), exemplar.Named("PARENT"), aggressor.Named("PREY"));
                    text = "LetterAnimalProtectingEggs".Translate(exemplar.LabelIndefinite(), aggressor.LabelDefinite(), exemplar.Named("PARENT"), aggressor.Named("PREY"));
                    if (label.NullOrEmpty() || label.Contains("LetterLabelAnimalProtectingEggs"))
                    {
                        label = $"{exemplar.LabelShort} is protecting its clutch";
                    }

                    if (text.NullOrEmpty() || text.Contains("LetterAnimalProtectingEggs"))
                    {
                        text = $"{exemplar.LabelShort} is protecting its clutch and is attacking {aggressor.LabelDefinite()}.";
                    }
                }

                if (PawnThreatUtility.IsHumanlikeOrMechanoid(aggressor))
                {
                    Find.LetterStack.ReceiveLetter(
                        label.CapitalizeFirst(),
                        text.CapitalizeFirst(),
                        pack ? LetterDefOf.ThreatBig : LetterDefOf.ThreatSmall,
                        ZoologyNotificationUtility.CreateLookTargets(triggeredProtectors, egg));
                }
                else
                {
                    Messages.Message(
                        text.CapitalizeFirst(),
                        ZoologyNotificationUtility.CreateLookTargets(triggeredProtectors, egg),
                        pack ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.ThreatSmall,
                        true);
                }

                EggClutchDefenseGameComponent.MarkProtectionNotificationSentForEgg(egg.thingIDNumber);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: egg protection notification failed: {ex}");
            }
        }

        private static void TryNotifyPlayerAboutYoungProtection(Pawn aggressor, Thing protectedYoung, Pawn exemplar, IReadOnlyList<Pawn> triggeredProtectors, int triggeredCount)
        {
            if (aggressor == null
                || aggressor.Faction != Faction.OfPlayer
                || exemplar == null
                || protectedYoung == null)
            {
                return;
            }

            try
            {
                bool pack = triggeredCount > 1;
                string label;
                string text;

                if (pack)
                {
                    string collectiveLabel = ZoologyNotificationUtility.GetCollectiveAnimalLabel(exemplar, triggeredCount);
                    label = "LetterLabelMotherProtectingYoungPack".Translate(collectiveLabel);
                    text = "LetterMotherProtectingYoungPack".Translate(collectiveLabel, aggressor.LabelDefinite());
                    if (label.NullOrEmpty() || label.Contains("LetterLabelMotherProtectingYoungPack"))
                    {
                        label = $"{collectiveLabel} are protecting their young";
                    }

                    if (text.NullOrEmpty() || text.Contains("LetterMotherProtectingYoungPack"))
                    {
                        text = $"{collectiveLabel} are protecting their young and are attacking {aggressor.LabelDefinite()}.";
                    }
                }
                else
                {
                    label = "LetterLabelMotherProtectingYoung".Translate(exemplar.LabelShort, aggressor.LabelDefinite(), exemplar.Named("PARENT"), aggressor.Named("ATTACKER"));
                    text = "LetterMotherProtectingYoung".Translate(exemplar.LabelIndefinite(), aggressor.LabelDefinite(), exemplar.Named("PARENT"), aggressor.Named("ATTACKER"));
                    if (label.NullOrEmpty() || label.Contains("LetterLabelMotherProtectingYoung"))
                    {
                        label = $"{exemplar.LabelShort} is protecting its young";
                    }

                    if (text.NullOrEmpty() || text.Contains("LetterMotherProtectingYoung"))
                    {
                        text = $"{exemplar.LabelShort} is protecting its young and is attacking {aggressor.LabelDefinite()}.";
                    }
                }

                if (PawnThreatUtility.IsHumanlikeOrMechanoid(aggressor))
                {
                    Find.LetterStack.ReceiveLetter(
                        label.CapitalizeFirst(),
                        text.CapitalizeFirst(),
                        pack ? LetterDefOf.ThreatBig : LetterDefOf.ThreatSmall,
                        ZoologyNotificationUtility.CreateLookTargets(triggeredProtectors, protectedYoung));
                }
                else
                {
                    Messages.Message(
                        text.CapitalizeFirst(),
                        ZoologyNotificationUtility.CreateLookTargets(triggeredProtectors, protectedYoung),
                        pack ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.ThreatSmall,
                        true);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: young protection notification failed: {ex}");
            }
        }

        private static bool IsEggThing(Thing thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return false;
            }

            ThingDef def = thing.def;
            if (def != null && TryGetCachedEggState(def, out byte state))
            {
                if (state == EggStateUnfertilized || state == EggStateFertilized)
                {
                    return true;
                }

                if (!DefHasHatcherComp(def))
                {
                    return false;
                }
            }

            return thing.TryGetComp<CompHatcher>() != null;
        }

        private static bool IsFertilizedEgg(Thing egg)
        {
            if (egg == null || egg.Destroyed)
            {
                return false;
            }

            ThingDef def = egg.def;
            if (def != null && TryGetCachedEggState(def, out byte state))
            {
                if (state == EggStateFertilized)
                {
                    return true;
                }

                if (!DefHasHatcherComp(def))
                {
                    return false;
                }
            }

            CompHatcher hatcher = egg.TryGetComp<CompHatcher>();
            return hatcher != null && hatcher.otherParent != null;
        }

        internal static bool CouldBeFertilizedEggFoodSource(Thing thing)
        {
            if (thing == null || thing.Destroyed)
            {
                return false;
            }

            ThingDef def = thing.def;
            if (def == null)
            {
                return false;
            }

            if (TryGetCachedEggState(def, out byte state) && state == EggStateFertilized)
            {
                return true;
            }

            return DefHasHatcherComp(def);
        }

        private static bool TryGetCachedEggState(ThingDef def, out byte state)
        {
            state = EggStateNo;
            if (def == null)
            {
                return false;
            }

            ushort shortHash = def.shortHash;
            if (shortHash != 0 && ReferenceEquals(eggDefsByShortHash[shortHash], def))
            {
                state = eggStatesByShortHash[shortHash];
                return state != EggStateUnknown;
            }

            byte resolvedState = EggStateNo;
            List<ThingCategoryDef> categories = def.thingCategories;
            if (categories != null)
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    ThingCategoryDef category = categories[i];
                    if (category == ThingCategoryDefOf.EggsFertilized)
                    {
                        resolvedState = EggStateFertilized;
                        break;
                    }

                    if (category == ThingCategoryDefOf.EggsUnfertilized)
                    {
                        resolvedState = EggStateUnfertilized;
                    }
                }
            }

            if (shortHash != 0)
            {
                ThingDef cached = eggDefsByShortHash[shortHash];
                if (cached == null || ReferenceEquals(cached, def))
                {
                    eggDefsByShortHash[shortHash] = def;
                    eggStatesByShortHash[shortHash] = resolvedState;
                }
            }

            state = resolvedState;
            return resolvedState != EggStateUnknown;
        }

        private static bool DefHasHatcherComp(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            ushort shortHash = def.shortHash;
            if (shortHash != 0 && ReferenceEquals(hatcherCompDefsByShortHash[shortHash], def))
            {
                byte state = hatcherCompStatesByShortHash[shortHash];
                if (state != HatcherCompStateUnknown)
                {
                    return state == HatcherCompStateYes;
                }
            }

            if (hatcherCompPresenceByDefFallback.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool hasComp = false;
            try
            {
                hasComp = def.HasComp(typeof(CompHatcher));
            }
            catch
            {
                hasComp = false;
            }

            if (shortHash != 0)
            {
                ThingDef cachedDef = hatcherCompDefsByShortHash[shortHash];
                if (cachedDef == null || ReferenceEquals(cachedDef, def))
                {
                    hatcherCompDefsByShortHash[shortHash] = def;
                    hatcherCompStatesByShortHash[shortHash] = hasComp ? HatcherCompStateYes : HatcherCompStateNo;
                    return hasComp;
                }
            }

            hatcherCompPresenceByDefFallback[def] = hasComp;
            return hasComp;
        }

        private static bool IsProtectorEligibleForDefense(Pawn protector, Thing protectedThing)
        {
            if (protector == null || protectedThing == null)
            {
                return false;
            }

            if (protector.Dead || protector.Destroyed || !protector.Spawned || protector.Downed || protector.InMentalState)
            {
                return false;
            }

            if (!PreyProtectionUtility.IsPawnAwakeForProtection(protector))
            {
                return false;
            }

            if (!ChildcareUtility.HasChildcareExtension(protector))
            {
                return false;
            }

            if (!IsProtectedThingValid(protectedThing))
            {
                return false;
            }

            return protector.MapHeld == protectedThing.MapHeld;
        }

        private static bool IsProtectedThingValid(Thing protectedThing)
        {
            if (protectedThing == null || protectedThing.Destroyed || !protectedThing.SpawnedOrAnyParentSpawned || protectedThing.MapHeld == null)
            {
                return false;
            }

            if (protectedThing is Pawn pawn && pawn.Dead)
            {
                return false;
            }

            return true;
        }

        private static bool IsSameFactionBlocked(Pawn protector, Pawn aggressor)
        {
            if (protector?.Faction == null || aggressor?.Faction == null)
            {
                return false;
            }

            if (!ReferenceEquals(protector.Faction, aggressor.Faction))
            {
                return false;
            }

            FactionDef def = protector.Faction.def;
            if (def != null
                && def.defName != null
                && def.defName.Equals("Photonozoa", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool SharesSpeciesLineage(Pawn first, Pawn second)
        {
            if (first == null || second == null || first.def == null || second.def == null)
            {
                return false;
            }

            return first.def == second.def || ZoologyCacheUtility.AreCrossbreedRelated(first.def, second.def);
        }

        private static bool IsAttackerTooStrong(Pawn aggressor, Pawn protector)
        {
            float aggressorPower = AnimalCombatPowerUtility.GetAdjustedCombatPower(aggressor);
            float protectorPower = AnimalCombatPowerUtility.GetAdjustedCombatPower(protector);

            if (aggressorPower <= 0f || protectorPower <= 0f)
            {
                return false;
            }

            return aggressorPower >= protectorPower * ModConstants.CombatPowerDominanceFactor;
        }

        private static bool IsProtectorAcceptablePrey(Pawn aggressor, Pawn protector)
        {
            try
            {
                if (aggressor?.RaceProps?.predator != true || protector == null)
                {
                    return false;
                }

                return FoodUtility.IsAcceptablePreyFor(aggressor, protector);
            }
            catch
            {
                return false;
            }
        }

        private static bool CanProtectorEngage(Pawn protector, Pawn aggressor, Thing protectedThing)
        {
            if (protector == null || aggressor == null)
            {
                return false;
            }

            Job curJob = protector.CurJob;
            if (curJob != null)
            {
                if (ProtectYoungUtility.IsProtectYoungJob(curJob, protector))
                {
                    return ProtectYoungUtility.CanRetargetProtectYoungJob(protector, curJob, aggressor, protectedThing);
                }

                if (ProtectPreyState.IsProtectPreyJob(protector))
                {
                    return false;
                }

                if (curJob.playerForced || curJob.def == JobDefOf.AttackMelee)
                {
                    return false;
                }
            }

            try
            {
                if (!protector.CanReach(aggressor, PathEndMode.Touch, Danger.Deadly))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static bool IsEggIncubatedNow(Thing egg)
        {
            if (egg == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick <= 0)
            {
                return false;
            }

            return incubatedEggTouchTickByEggId.TryGetValue(egg.thingIDNumber, out int lastTouchTick)
                && currentTick - lastTouchTick <= EggIncubationTouchFreshnessTicks;
        }

        private static bool TryGetEggFoodDeltaCached(int currentTick, long pairKey, out float delta)
        {
            delta = 0f;
            if (pairKey == 0L || currentTick <= 0)
            {
                return false;
            }

            int slotIndex = (int)((ulong)pairKey & EggFoodDeltaHotCacheMask);
            EggFoodDeltaCacheEntry cached = eggFoodDeltaHotCacheSlots[slotIndex];
            if (cached.Key != pairKey || currentTick - cached.Tick > EggFoodDeltaCacheDurationTicks)
            {
                return false;
            }

            delta = cached.Delta;
            return true;
        }

        private static void StoreEggFoodDelta(int currentTick, long pairKey, float delta)
        {
            if (pairKey == 0L || currentTick <= 0)
            {
                return;
            }

            int slotIndex = (int)((ulong)pairKey & EggFoodDeltaHotCacheMask);
            eggFoodDeltaHotCacheSlots[slotIndex] = new EggFoodDeltaCacheEntry(pairKey, delta, currentTick);
        }

        private static bool TryGetEggFoodState(EggClutchDefenseGameComponent component, Pawn eater, Thing egg, out EggFoodStateCacheEntry state)
        {
            state = default;
            if (component == null || eater == null || egg == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            long pairKey = MakePairKey(eater, egg);
            if (pairKey != 0L
                && currentTick > 0
                && eggFoodStateCacheByPairKey.TryGetValue(pairKey, out EggFoodStateCacheEntry cached)
                && currentTick - cached.Tick <= ZoologyTickLimiter.Childcare.EggFoodStateCacheDurationTicks)
            {
                state = cached;
                return true;
            }

            if (!TryConsumeBudget(ref eggFoodStateBudgetTick, ref eggFoodStateBudgetRemaining, ZoologyTickLimiter.Childcare.EggFoodStateBudgetPerTick))
            {
                return false;
            }

            bool isGuarded = false;
            try
            {
                isGuarded = TryFillEggProtectors(egg, eater, eggProtectorsScratch);
            }
            finally
            {
                eggProtectorsScratch.Clear();
            }

            state = new EggFoodStateCacheEntry(isGuarded, currentTick);
            if (pairKey != 0L && currentTick > 0)
            {
                eggFoodStateCacheByPairKey[pairKey] = state;
                CleanupEggFoodStateCacheIfNeeded(currentTick);
            }

            return true;
        }

        private static bool TryGetYoungProtectionState(Pawn aggressor, Pawn protectedYoung, out YoungProtectionStateCacheEntry state)
        {
            state = default;
            if (aggressor == null
                || protectedYoung == null
                || !protectedYoung.IsAnimal
                || !ChildcareUtility.IsAnimalChild(protectedYoung))
            {
                return false;
            }

            return TryGetYoungProtectionStateForKnownYoung(aggressor, protectedYoung, out state);
        }

        private static bool TryGetYoungProtectionStateForKnownYoung(Pawn aggressor, Pawn youngPawn, out YoungProtectionStateCacheEntry state)
        {
            state = default;
            if (!IsYoungProtectionEnabled
                || aggressor == null
                || youngPawn == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (TryGetYoungProtectionStateCached(currentTick, aggressor, youngPawn, out YoungProtectionStateCacheEntry cached))
            {
                state = cached;
                return true;
            }

            if (!TryGetDirectYoungProtectionTargetState(youngPawn, currentTick, out YoungProtectionTargetState targetState))
            {
                state = new YoungProtectionStateCacheEntry(isGuarded: false, currentTick);
                return true;
            }

            if (!targetState.HasBestProtector)
            {
                state = new YoungProtectionStateCacheEntry(isGuarded: false, currentTick);
                if (currentTick > 0)
                {
                    StoreYoungProtectionStateCached(currentTick, aggressor, youngPawn, state);
                }

                return true;
            }

            bool isGuarded = DoesYoungProtectionStateBlockAggressor(aggressor, targetState);

            state = new YoungProtectionStateCacheEntry(isGuarded, currentTick);
            if (currentTick > 0)
            {
                StoreYoungProtectionStateCached(currentTick, aggressor, youngPawn, state);
            }

            return true;
        }

        private static bool TryGetYoungProtectionStateCached(int currentTick, Pawn aggressor, Pawn young, out YoungProtectionStateCacheEntry state)
        {
            state = default;
            if (currentTick <= 0 || aggressor == null || young == null || young.thingIDNumber <= 0)
            {
                return false;
            }

            int slotIndex = GetYoungProtectionStateCacheSlotIndex(aggressor, young);
            YoungProtectionStateHotCacheEntry cached = youngProtectionStateHotCacheSlots[slotIndex];
            if (!cached.Matches(aggressor, young)
                || currentTick - cached.State.Tick > YoungProtectionStateCacheDurationTicks)
            {
                return false;
            }

            state = cached.State;
            return true;
        }

        private static void StoreYoungProtectionStateCached(int currentTick, Pawn aggressor, Pawn young, YoungProtectionStateCacheEntry state)
        {
            if (currentTick <= 0 || aggressor == null || young == null || young.thingIDNumber <= 0)
            {
                return;
            }

            int slotIndex = GetYoungProtectionStateCacheSlotIndex(aggressor, young);
            youngProtectionStateHotCacheSlots[slotIndex] = new YoungProtectionStateHotCacheEntry(aggressor, young, state);
        }

        private static int GetYoungProtectionStateCacheSlotIndex(Pawn aggressor, Pawn young)
        {
            unchecked
            {
                Faction faction = aggressor?.Faction;
                uint mixed = (uint)(young?.thingIDNumber ?? 0);
                mixed ^= (uint)(aggressor?.MapHeld?.uniqueID ?? 0) * 2246822519u;
                mixed ^= (uint)(aggressor?.def?.shortHash ?? 0) * 3266489917u;
                mixed ^= (uint)(aggressor?.kindDef?.shortHash ?? 0) * 668265263u;
                mixed ^= (uint)(aggressor?.ageTracker?.CurLifeStage?.shortHash ?? 0) * 374761393u;
                mixed ^= (uint)(faction != null ? RuntimeHelpers.GetHashCode(faction) : 0) * 2654435761u;
                mixed ^= mixed >> 16;
                mixed *= 2654435761u;
                mixed ^= mixed >> 13;
                return (int)(mixed & YoungProtectionStateHotCacheMask);
            }
        }

        private static bool TryConsumeBudget(ref int tick, ref int remaining, int perTick)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick <= 0)
            {
                return false;
            }

            if (tick != currentTick)
            {
                tick = currentTick;
                remaining = perTick;
            }

            if (remaining <= 0)
            {
                return false;
            }

            remaining--;
            return true;
        }

        private static void CleanupEggFoodStateCacheIfNeeded(int currentTick)
        {
            if (currentTick - lastEggFoodStateCacheCleanupTick < ZoologyTickLimiter.Childcare.EggFoodStateCacheCleanupIntervalTicks)
            {
                return;
            }

            lastEggFoodStateCacheCleanupTick = currentTick;
            eggFoodStateCleanupScratch.Clear();
            foreach (KeyValuePair<long, EggFoodStateCacheEntry> entry in eggFoodStateCacheByPairKey)
            {
                if (currentTick - entry.Value.Tick > ZoologyTickLimiter.Childcare.EggFoodStateCacheDurationTicks)
                {
                    eggFoodStateCleanupScratch.Add(entry.Key);
                }
            }

            for (int i = 0; i < eggFoodStateCleanupScratch.Count; i++)
            {
                eggFoodStateCacheByPairKey.Remove(eggFoodStateCleanupScratch[i]);
            }

            eggFoodStateCleanupScratch.Clear();
        }

        private static void EnsureEggRuntimeCacheState(int currentTick, TickManager tickManager)
        {
            if (currentTick > 0
                && eggRuntimeEnsureTick == currentTick
                && ReferenceEquals(eggRuntimeTickManager, tickManager))
            {
                return;
            }

            Game currentGame = Current.Game;
            bool tickManagerChanged = !ReferenceEquals(eggRuntimeTickManager, tickManager);
            bool gameChanged = !ReferenceEquals(eggRuntimeGame, currentGame);
            bool tickRewound = currentTick > 0 && eggRuntimeLastTick > 0 && currentTick < eggRuntimeLastTick;
            if (tickManagerChanged || gameChanged || tickRewound)
            {
                recentEggProtectionTriggerByPairKey.Clear();
                recentYoungProtectionTriggerByPairKey.Clear();
                eggFoodStateCacheByPairKey.Clear();
                nearbyYoungCacheByProtectorId.Clear();
                directYoungProtectionStateByYoungId.Clear();
                incubatedEggTouchTickByEggId.Clear();
                lastIncubationSearchFailureTickByPawnId.Clear();
                Array.Clear(eggFoodDeltaHotCacheSlots, 0, eggFoodDeltaHotCacheSlots.Length);
                Array.Clear(youngProtectionStateHotCacheSlots, 0, youngProtectionStateHotCacheSlots.Length);
                Array.Clear(youngProtectionTargetStateHotCacheSlots, 0, youngProtectionTargetStateHotCacheSlots.Length);
                lastEggProtectionCleanupTick = int.MinValue;
                lastYoungProtectionCleanupTick = int.MinValue;
                lastEggFoodStateCacheCleanupTick = -ZoologyTickLimiter.Childcare.EggFoodStateCacheCleanupIntervalTicks;
                eggFoodStateBudgetTick = -1;
                eggFoodStateBudgetRemaining = 0;
                youngProtectionTargetStateBudgetTick = -1;
                youngProtectionTargetStateBudgetRemaining = 0;
                directYoungProtectionMapCacheMap = null;
                directYoungProtectionMapCacheMapId = -1;
                directYoungProtectionMapCacheTick = -1;
                eggRuntimeTickManager = tickManager;
                eggRuntimeGame = currentGame;
                eggRuntimeLastTick = -1;
            }

            if (currentTick > 0)
            {
                eggRuntimeLastTick = currentTick;
                eggRuntimeEnsureTick = currentTick;
            }
            else
            {
                eggRuntimeEnsureTick = -1;
            }
        }

        private static long MakePairKey(Pawn aggressor, Thing egg)
        {
            if (aggressor == null || egg == null)
            {
                return 0L;
            }

            return ((long)(uint)aggressor.thingIDNumber << 32) | (uint)egg.thingIDNumber;
        }
    }
}
