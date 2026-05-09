using System;
using System.Collections.Generic;
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
        private const int EggFoodDeltaCacheDurationTicks = 10;
        private const int EggFoodDeltaHotCacheSize = 8192;
        private const int EggFoodDeltaHotCacheMask = EggFoodDeltaHotCacheSize - 1;
        private const int EggIncubationDurationTicks = 2500;
        private const int EggIncubationSearchFailCooldownTicks = 180;
        private const float EggIncubationSearchRadius = 20f;
        private const int EggIncubationTouchFreshnessTicks = 2;
        private const byte EggStateUnknown = 0;
        private const byte EggStateNo = 1;
        private const byte EggStateUnfertilized = 2;
        private const byte EggStateFertilized = 4;

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

        private static readonly Dictionary<long, int> recentEggProtectionTriggerByPairKey = new Dictionary<long, int>(128);
        private static readonly Dictionary<long, int> recentYoungProtectionTriggerByPairKey = new Dictionary<long, int>(128);
        private static readonly Dictionary<long, EggFoodStateCacheEntry> eggFoodStateCacheByPairKey = new Dictionary<long, EggFoodStateCacheEntry>(256);
        private static readonly Dictionary<int, int> incubatedEggTouchTickByEggId = new Dictionary<int, int>(64);
        private static readonly Dictionary<int, int> lastIncubationSearchFailureTickByPawnId = new Dictionary<int, int>(64);
        private static readonly List<Pawn> eggProtectorsScratch = new List<Pawn>(8);
        private static readonly List<long> eggTriggerCleanupScratch = new List<long>(64);
        private static readonly List<long> eggFoodStateCleanupScratch = new List<long>(64);
        private static readonly List<long> youngTriggerCleanupScratch = new List<long>(64);
        private static readonly EggFoodDeltaCacheEntry[] eggFoodDeltaHotCacheSlots = new EggFoodDeltaCacheEntry[EggFoodDeltaHotCacheSize];
        private static readonly ThingDef[] eggDefsByShortHash = new ThingDef[ushort.MaxValue + 1];
        private static readonly byte[] eggStatesByShortHash = new byte[ushort.MaxValue + 1];

        private static JobDef protectYoungJobDef;
        private static JobDef incubateEggJobDef;
        private static TickManager eggRuntimeTickManager;
        private static Game eggRuntimeGame;
        private static int eggRuntimeLastTick = -1;
        private static int eggRuntimeEnsureTick = -1;
        private static int lastEggProtectionCleanupTick = int.MinValue;
        private static int lastYoungProtectionCleanupTick = int.MinValue;
        private static int lastEggFoodStateCacheCleanupTick = -ZoologyTickLimiter.Childcare.EggFoodStateCacheCleanupIntervalTicks;
        private static int eggFoodStateBudgetTick = -1;
        private static int eggFoodStateBudgetRemaining;

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

        public static JobDef GetEggIncubationJobDef()
        {
            return incubateEggJobDef ?? (incubateEggJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_IncubateEggClutch"));
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

            EggClutchDefenseGameComponent component = EggClutchDefenseGameComponent.Instance;
            if (component == null)
            {
                return false;
            }

            int triggeredCount = 0;
            Pawn exemplar = null;
            bool anyTriggered = false;

            try
            {
                if (!component.TryGetProtectors(egg, eggProtectorsScratch))
                {
                    return false;
                }

                for (int i = 0; i < eggProtectorsScratch.Count; i++)
                {
                    Pawn protector = eggProtectorsScratch[i];
                    if (!CanProtectorGuardEgg(protector, aggressor, egg))
                    {
                        continue;
                    }

                    if (!TryTakeProtectionJob(protector, aggressor, egg))
                    {
                        continue;
                    }

                    anyTriggered = true;
                    triggeredCount++;
                    exemplar ??= protector;

                    try
                    {
                        protector.mindState?.Notify_PredatorHuntingPlayerNotification();
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                eggProtectorsScratch.Clear();
            }

            if (!anyTriggered)
            {
                return false;
            }

            RememberEggProtectionTrigger(aggressor, egg);
            TryNotifyPlayerAboutEggProtection(aggressor, egg, exemplar, triggeredCount);
            return true;
        }

        public static float GetFoodOptimalityDeltaForEgg(Pawn eater, Thing foodSource)
        {
            if (!IsEggProtectionEnabled || eater == null || foodSource == null || !IsFertilizedEgg(foodSource))
            {
                return 0f;
            }

            TickManager tickManager = Find.TickManager;
            int currentTick = tickManager?.TicksGame ?? 0;
            EnsureEggRuntimeCacheState(currentTick, tickManager);

            long pairKey = MakePairKey(eater, foodSource);
            if (TryGetEggFoodDeltaCached(currentTick, pairKey, out float cachedDelta))
            {
                return cachedDelta;
            }

            if (!TryGetEggProtectionAnchor(foodSource, eater, out Map anchorMap, out _)
                || eater.MapHeld == null
                || eater.MapHeld != anchorMap)
            {
                StoreEggFoodDelta(currentTick, pairKey, 0f);
                return 0f;
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
                || aggressor == protector
                || !IsProtectorEligibleForDefense(protector, protectedThing)
                || IsSameFactionBlocked(protector, aggressor)
                || IsAttackerTooStrong(aggressor, protector)
                || IsProtectorAcceptablePrey(aggressor, protector)
                || !CanProtectorEngage(protector, aggressor))
            {
                return false;
            }

            bool success = TryTakeProtectionJob(protector, aggressor, protectedThing);
            if (success)
            {
                RememberYoungProtectionTrigger(aggressor, protectedThing);
                TryNotifyPlayerAboutYoungProtection(aggressor, protectedThing, protector, 1);
            }
            return success;
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
            return component != null && component.ShouldBlockOwnFertilizedEggConsumption(eater, foodSource);
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
            if (protector == null
                || aggressor == null
                || egg == null
                || aggressor == protector
                || !IsProtectorEligibleForDefense(protector, egg)
                || IsSameFactionBlocked(protector, aggressor)
                || IsAttackerTooStrong(aggressor, protector)
                || IsProtectorAcceptablePrey(aggressor, protector)
                || !CanProtectorEngage(protector, aggressor)
                || !TryGetEggProtectionAnchor(egg, aggressor, out Map anchorMap, out IntVec3 anchorPosition))
            {
                return false;
            }

            return PreyProtectionUtility.IsPawnWithinProtectionRange(
                protector,
                anchorMap,
                anchorPosition,
                PreyProtectionUtility.GetProtectionRangeSquared());
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

                return protector.jobs?.TryTakeOrderedJob(job) ?? false;
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: failed to order childcare protection job: {ex}");
                return false;
            }
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

        private static void TryNotifyPlayerAboutEggProtection(Pawn aggressor, Thing egg, Pawn exemplar, int triggeredCount)
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
                    label = "LetterLabelAnimalProtectingEggsPack".Translate(exemplar.GetKindLabelPlural(), exemplar.Named("PARENT"));
                    text = "LetterAnimalProtectingEggsPack".Translate(exemplar.GetKindLabelPlural(), aggressor.LabelDefinite(), exemplar.Named("PARENT"), aggressor.Named("PREY"));
                    if (label.NullOrEmpty() || label.Contains("LetterLabelAnimalProtectingEggsPack"))
                    {
                        label = $"{exemplar.GetKindLabelPlural()} are protecting a clutch";
                    }

                    if (text.NullOrEmpty() || text.Contains("LetterAnimalProtectingEggsPack"))
                    {
                        text = $"{exemplar.GetKindLabelPlural()} are protecting a clutch and are attacking {aggressor.LabelDefinite()}.";
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

                if (aggressor.RaceProps?.Humanlike == true)
                {
                    Find.LetterStack.ReceiveLetter(label.CapitalizeFirst(), text.CapitalizeFirst(), LetterDefOf.ThreatBig, exemplar, null, null, null, null, 0, true);
                }
                else
                {
                    Messages.Message(text.CapitalizeFirst(), exemplar, MessageTypeDefOf.ThreatBig, true);
                }

                EggClutchDefenseGameComponent.MarkProtectionNotificationSentForEgg(egg.thingIDNumber);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: egg protection notification failed: {ex}");
            }
        }

        private static void TryNotifyPlayerAboutYoungProtection(Pawn aggressor, Thing protectedYoung, Pawn exemplar, int triggeredCount)
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
                    label = "LetterLabelMotherProtectingYoungPack".Translate(exemplar.GetKindLabelPlural(), exemplar.Named("PARENT"));
                    text = "LetterMotherProtectingYoungPack".Translate(exemplar.GetKindLabelPlural(), aggressor.LabelDefinite(), exemplar.Named("PARENT"), aggressor.Named("ATTACKER"));
                    if (label.NullOrEmpty() || label.Contains("LetterLabelMotherProtectingYoungPack"))
                    {
                        label = $"{exemplar.GetKindLabelPlural()} are protecting their young";
                    }

                    if (text.NullOrEmpty() || text.Contains("LetterMotherProtectingYoungPack"))
                    {
                        text = $"{exemplar.GetKindLabelPlural()} are protecting their young and are attacking {aggressor.LabelDefinite()}.";
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

                if (aggressor.RaceProps?.Humanlike == true)
                {
                    Find.LetterStack.ReceiveLetter(label.CapitalizeFirst(), text.CapitalizeFirst(), LetterDefOf.ThreatBig, exemplar, null, null, null, null, 0, true);
                }
                else
                {
                    Messages.Message(text.CapitalizeFirst(), exemplar, MessageTypeDefOf.ThreatBig, true);
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
                return state == EggStateUnfertilized || state == EggStateFertilized;
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
            if (def != null && TryGetCachedEggState(def, out byte state) && state == EggStateFertilized)
            {
                return true;
            }

            CompHatcher hatcher = egg.TryGetComp<CompHatcher>();
            return hatcher != null && hatcher.otherParent != null;
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

        private static bool CanProtectorEngage(Pawn protector, Pawn aggressor)
        {
            if (protector == null || aggressor == null)
            {
                return false;
            }

            Job curJob = protector.CurJob;
            if (curJob != null)
            {
                if (curJob.playerForced || curJob.def == JobDefOf.AttackMelee)
                {
                    return false;
                }

                if (ProtectPreyState.IsProtectPreyJob(protector) || ProtectYoungUtility.IsProtectYoungJob(protector))
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
                if (component.TryGetProtectors(egg, eggProtectorsScratch))
                {
                    for (int i = 0; i < eggProtectorsScratch.Count; i++)
                    {
                        if (!CanProtectorGuardEgg(eggProtectorsScratch[i], eater, egg))
                        {
                            continue;
                        }

                        isGuarded = true;
                        break;
                    }
                }
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
                incubatedEggTouchTickByEggId.Clear();
                lastIncubationSearchFailureTickByPawnId.Clear();
                Array.Clear(eggFoodDeltaHotCacheSlots, 0, eggFoodDeltaHotCacheSlots.Length);
                lastEggProtectionCleanupTick = int.MinValue;
                lastYoungProtectionCleanupTick = int.MinValue;
                lastEggFoodStateCacheCleanupTick = -ZoologyTickLimiter.Childcare.EggFoodStateCacheCleanupIntervalTicks;
                eggFoodStateBudgetTick = -1;
                eggFoodStateBudgetRemaining = 0;
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
