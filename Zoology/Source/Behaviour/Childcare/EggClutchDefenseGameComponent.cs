using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public class EggClutchDefenseGameComponent : GameComponent
    {
        private sealed class EggClutchOwnershipRecord : IExposable
        {
            public Thing Egg;
            public int EggThingId = -1;
            public List<Pawn> Mothers = new List<Pawn>(2);

            public void ExposeData()
            {
                Scribe_Values.Look(ref EggThingId, "eggThingId", -1);
                Scribe_Collections.Look(ref Mothers, "mothers", LookMode.Reference);
                Mothers ??= new List<Pawn>(2);
            }
        }

        private static readonly Dictionary<int, long> notificationSuppressedUntil = new Dictionary<int, long>(64);
        private static readonly List<Pawn> ownerScratch = new List<Pawn>(4);

        private static EggClutchDefenseGameComponent singleton;

        private List<EggClutchOwnershipRecord> ownershipRecords = new List<EggClutchOwnershipRecord>(64);
        private readonly Dictionary<int, EggClutchOwnershipRecord> recordByEggId = new Dictionary<int, EggClutchOwnershipRecord>(64);
        private readonly Dictionary<int, HashSet<int>> eggIdsByMotherId = new Dictionary<int, HashSet<int>>(64);

        public static EggClutchDefenseGameComponent Instance
        {
            get
            {
                if (singleton == null && Current.Game != null)
                {
                    singleton = Current.Game.GetComponent<EggClutchDefenseGameComponent>();
                }

                return singleton;
            }
        }

        public EggClutchDefenseGameComponent(Game game)
        {
            singleton = this;
        }

        public EggClutchDefenseGameComponent()
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            singleton = this;
            RebuildRuntimeIndex();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            singleton = this;
            ownershipRecords.Clear();
            recordByEggId.Clear();
            eggIdsByMotherId.Clear();
            notificationSuppressedUntil.Clear();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            singleton = this;
            RebuildRuntimeIndex();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref ownershipRecords, "Zoology_eggClutchOwnershipRecords", LookMode.Deep);
            ownershipRecords ??= new List<EggClutchOwnershipRecord>(64);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                singleton = this;
                RebuildRuntimeIndex();
            }
        }

        public void RegisterOwnership(Pawn mother, Thing egg)
        {
            if (!IsValidMother(mother) || egg == null || egg.Destroyed)
            {
                return;
            }

            EggClutchOwnershipRecord record = GetOrCreateRecord(egg);
            bool hadMother = ContainsMother(record, mother);
            AddMother(record, mother);
            if (!hadMother)
            {
                IndexRecordMothers(record);
            }
        }

        public void HandleEggSplit(Thing source, Thing piece)
        {
            if (source == null || piece == null || ReferenceEquals(source, piece))
            {
                return;
            }

            try
            {
                FillOwners(source, ownerScratch);
                if (ownerScratch.Count == 0)
                {
                    return;
                }

                EggClutchOwnershipRecord record = GetOrCreateRecord(piece);
                bool changed = false;
                for (int i = 0; i < ownerScratch.Count; i++)
                {
                    Pawn mother = ownerScratch[i];
                    if (ContainsMother(record, mother))
                    {
                        continue;
                    }

                    AddMother(record, mother);
                    changed = true;
                }

                if (changed)
                {
                    IndexRecordMothers(record);
                }
            }
            finally
            {
                ownerScratch.Clear();
            }
        }

        public void HandleEggAbsorb(Thing target, Thing source, int countToTake)
        {
            if (target == null || source == null || countToTake <= 0)
            {
                return;
            }

            try
            {
                FillOwners(target, ownerScratch);
                FillOwners(source, ownerScratch);
                if (ownerScratch.Count == 0)
                {
                    return;
                }

                EggClutchOwnershipRecord record = GetOrCreateRecord(target);
                bool changed = false;
                for (int i = 0; i < ownerScratch.Count; i++)
                {
                    Pawn mother = ownerScratch[i];
                    if (ContainsMother(record, mother))
                    {
                        continue;
                    }

                    AddMother(record, mother);
                    changed = true;
                }

                if (changed)
                {
                    IndexRecordMothers(record);
                }

                if (countToTake >= source.stackCount)
                {
                    RemoveRecord(source.thingIDNumber);
                }
            }
            finally
            {
                ownerScratch.Clear();
            }
        }

        public bool TryGetProtectors(Thing egg, List<Pawn> output)
        {
            if (output == null)
            {
                return false;
            }

            output.Clear();
            FillOwners(egg, output);
            return output.Count > 0;
        }

        public bool IsAssociatedWithMother(Thing egg, Pawn mother)
        {
            if (egg == null || mother == null)
            {
                return false;
            }

            CompHatcher hatcher = egg.TryGetComp<CompHatcher>();
            if (ReferenceEquals(hatcher?.hatcheeParent, mother))
            {
                return true;
            }

            if (!TryGetRecord(egg, out EggClutchOwnershipRecord record))
            {
                return false;
            }

            return ContainsMother(record, mother);
        }

        public bool TryGetAnyMother(Thing egg, out Pawn mother)
        {
            mother = null;
            if (egg == null)
            {
                return false;
            }

            CompHatcher hatcher = egg.TryGetComp<CompHatcher>();
            if (IsValidMother(hatcher?.hatcheeParent))
            {
                mother = hatcher.hatcheeParent;
                return true;
            }

            if (!TryGetRecord(egg, out EggClutchOwnershipRecord record))
            {
                return false;
            }

            for (int i = 0; i < record.Mothers.Count; i++)
            {
                Pawn candidate = record.Mothers[i];
                if (!IsValidMother(candidate))
                {
                    continue;
                }

                mother = candidate;
                return true;
            }

            return false;
        }

        public bool ShouldBlockOwnFertilizedEggConsumption(Pawn eater, Thing egg)
        {
            if (eater == null
                || egg == null
                || !eater.IsAnimal
                || !ChildcareUtility.HasChildcareExtension(eater))
            {
                return false;
            }

            try
            {
                FillOwners(egg, ownerScratch);
                for (int i = 0; i < ownerScratch.Count; i++)
                {
                    Pawn mother = ownerScratch[i];
                    if (!ChildcareUtility.HasChildcareExtension(mother))
                    {
                        continue;
                    }

                    if (SharesSpeciesLineage(mother, eater))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                ownerScratch.Clear();
            }

            return false;
        }

        public Thing TryGetPairedEggForMother(Pawn mother)
        {
            if (!IsValidMother(mother) || mother.Map == null)
            {
                return null;
            }

            int motherId = mother.thingIDNumber;
            if (motherId > 0 && eggIdsByMotherId.TryGetValue(motherId, out HashSet<int> eggIds) && eggIds.Count > 0)
            {
                Thing bestEgg = null;
                int bestDistanceSq = int.MaxValue;

                foreach (int eggId in eggIds)
                {
                    if (!recordByEggId.TryGetValue(eggId, out EggClutchOwnershipRecord record))
                    {
                        continue;
                    }

                    Thing egg = record.Egg;
                    if (egg == null
                        || egg.Destroyed
                        || !egg.Spawned
                        || egg.Map != mother.Map
                        || !egg.Position.IsValid)
                    {
                        continue;
                    }

                    int distanceSq = (mother.Position - egg.Position).LengthHorizontalSquared;
                    if (distanceSq >= bestDistanceSq)
                    {
                        continue;
                    }

                    bestEgg = egg;
                    bestDistanceSq = distanceSq;
                }

                if (bestEgg != null)
                {
                    return bestEgg;
                }
            }

            Thing fallbackEgg = null;
            int fallbackDistanceSq = int.MaxValue;
            for (int i = 0; i < ownershipRecords.Count; i++)
            {
                EggClutchOwnershipRecord record = ownershipRecords[i];
                Thing egg = record?.Egg;
                if (egg == null
                    || egg.Destroyed
                    || !egg.Spawned
                    || egg.Map != mother.Map
                    || !egg.Position.IsValid
                    || !RecordContainsMother(record, mother))
                {
                    continue;
                }

                int distanceSq = (mother.Position - egg.Position).LengthHorizontalSquared;
                if (distanceSq >= fallbackDistanceSq)
                {
                    continue;
                }

                fallbackEgg = egg;
                fallbackDistanceSq = distanceSq;
            }

            return fallbackEgg;
        }

        public Thing TryGetPairedEggForProtector(Pawn protector)
        {
            if (!IsValidMother(protector) || protector.Map == null || !protector.Spawned)
            {
                return null;
            }

            Thing directEgg = TryGetPairedEggForMother(protector);
            if (directEgg != null || protector.RaceProps?.herdAnimal != true || !ChildcareUtility.HasChildcareExtension(protector))
            {
                return directEgg;
            }

            Thing bestEgg = null;
            int bestDistanceSq = int.MaxValue;

            for (int i = 0; i < ownershipRecords.Count; i++)
            {
                EggClutchOwnershipRecord record = ownershipRecords[i];
                Thing egg = record?.Egg;
                if (egg == null
                    || egg.Destroyed
                    || !egg.Spawned
                    || egg.Map != protector.Map
                    || !egg.Position.IsValid)
                {
                    continue;
                }

                if (!RecordSharesProtectorLineage(record, protector))
                {
                    continue;
                }

                int distanceSq = (protector.Position - egg.Position).LengthHorizontalSquared;
                if (distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestEgg = egg;
                bestDistanceSq = distanceSq;
            }

            return bestEgg;
        }

        public static void MarkProtectionNotificationSentForEgg(int eggThingId)
        {
            if (eggThingId <= 0)
            {
                return;
            }

            try
            {
                long now = Find.TickManager?.TicksGame ?? 0L;
                notificationSuppressedUntil[eggThingId] = now + ZoologyTickLimiter.PreyProtection.NotificationSuppressionTicks;
            }
            catch
            {
            }
        }

        public static bool IsProtectionNotificationSuppressedForEgg(int eggThingId)
        {
            if (eggThingId <= 0)
            {
                return false;
            }

            try
            {
                long now = Find.TickManager?.TicksGame ?? 0L;
                if (notificationSuppressedUntil.TryGetValue(eggThingId, out long until))
                {
                    if (until >= now)
                    {
                        return true;
                    }

                    notificationSuppressedUntil.Remove(eggThingId);
                }
            }
            catch
            {
            }

            return false;
        }

        private void RebuildRuntimeIndex()
        {
            recordByEggId.Clear();
            eggIdsByMotherId.Clear();
            if (ownershipRecords.Count == 0)
            {
                return;
            }

            for (int i = ownershipRecords.Count - 1; i >= 0; i--)
            {
                EggClutchOwnershipRecord record = ownershipRecords[i];
                if ((record?.Egg == null || record.Egg.Destroyed) && record != null && record.EggThingId > 0)
                {
                    record.Egg = ResolveThingById(record.EggThingId);
                }

                if (!IsRecordUsable(record))
                {
                    ownershipRecords.RemoveAt(i);
                    continue;
                }

                int eggId = record.EggThingId > 0 ? record.EggThingId : record.Egg.thingIDNumber;
                if (eggId <= 0)
                {
                    ownershipRecords.RemoveAt(i);
                    continue;
                }

                record.EggThingId = eggId;

                if (recordByEggId.TryGetValue(eggId, out EggClutchOwnershipRecord existing))
                {
                    MergeRecordOwners(existing, record);
                    ownershipRecords.RemoveAt(i);
                    continue;
                }

                recordByEggId[eggId] = record;
                TrimInvalidMothers(record);
                if (record.Mothers.Count == 0 && !IsValidMother(record.Egg.TryGetComp<CompHatcher>()?.hatcheeParent))
                {
                    ownershipRecords.RemoveAt(i);
                    recordByEggId.Remove(eggId);
                    continue;
                }

                IndexRecordMothers(record);
            }
        }

        private bool TryGetRecord(Thing egg, out EggClutchOwnershipRecord record)
        {
            record = null;
            if (egg == null)
            {
                return false;
            }

            int eggId = egg.thingIDNumber;
            if (eggId <= 0)
            {
                return false;
            }

            if (!recordByEggId.TryGetValue(eggId, out record))
            {
                return false;
            }

            if (!IsRecordUsable(record) || !ReferenceEquals(record.Egg, egg))
            {
                RemoveRecord(eggId);
                record = null;
                return false;
            }

            TrimInvalidMothers(record);
            if (record.Mothers.Count == 0 && !IsValidMother(record.Egg.TryGetComp<CompHatcher>()?.hatcheeParent))
            {
                RemoveRecord(eggId);
                record = null;
                return false;
            }

            return true;
        }

        private EggClutchOwnershipRecord GetOrCreateRecord(Thing egg)
        {
            if (TryGetRecord(egg, out EggClutchOwnershipRecord existing))
            {
                return existing;
            }

            EggClutchOwnershipRecord record = new EggClutchOwnershipRecord
            {
                Egg = egg,
                EggThingId = egg?.thingIDNumber ?? -1
            };
            ownershipRecords.Add(record);
            if (record.EggThingId > 0)
            {
                recordByEggId[record.EggThingId] = record;
            }

            return record;
        }

        private void RemoveRecord(int eggId)
        {
            if (eggId <= 0 || !recordByEggId.TryGetValue(eggId, out EggClutchOwnershipRecord record))
            {
                return;
            }

            UnindexRecordMothers(record);
            recordByEggId.Remove(eggId);
            ownershipRecords.Remove(record);
        }

        private static void FillOwners(Thing egg, List<Pawn> output)
        {
            if (egg == null || output == null)
            {
                return;
            }

            CompHatcher hatcher = egg.TryGetComp<CompHatcher>();
            AppendUniqueMother(output, hatcher?.hatcheeParent);

            EggClutchDefenseGameComponent component = Instance;
            if (component == null || !component.TryGetRecord(egg, out EggClutchOwnershipRecord record))
            {
                return;
            }

            for (int i = 0; i < record.Mothers.Count; i++)
            {
                AppendUniqueMother(output, record.Mothers[i]);
            }
        }

        private static void MergeRecordOwners(EggClutchOwnershipRecord target, EggClutchOwnershipRecord source)
        {
            if (target == null || source == null || ReferenceEquals(target, source))
            {
                return;
            }

            for (int i = 0; i < source.Mothers.Count; i++)
            {
                Pawn mother = source.Mothers[i];
                if (IsValidMother(mother) && !ContainsMother(target, mother))
                {
                    target.Mothers.Add(mother);
                }
            }
        }

        private static bool IsRecordUsable(EggClutchOwnershipRecord record)
        {
            return record != null && record.Egg != null && !record.Egg.Destroyed && record.EggThingId > 0;
        }

        private static void AddMother(EggClutchOwnershipRecord record, Pawn mother)
        {
            if (record == null || !IsValidMother(mother) || ContainsMother(record, mother))
            {
                return;
            }

            record.EggThingId = record.Egg?.thingIDNumber ?? record.EggThingId;
            record.Mothers.Add(mother);
        }

        private static bool ContainsMother(EggClutchOwnershipRecord record, Pawn mother)
        {
            if (record == null || mother == null)
            {
                return false;
            }

            for (int i = 0; i < record.Mothers.Count; i++)
            {
                if (ReferenceEquals(record.Mothers[i], mother))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TrimInvalidMothers(EggClutchOwnershipRecord record)
        {
            if (record == null)
            {
                return;
            }

            for (int i = record.Mothers.Count - 1; i >= 0; i--)
            {
                if (!IsValidMother(record.Mothers[i]))
                {
                    record.Mothers.RemoveAt(i);
                }
            }
        }

        private static void AppendUniqueMother(List<Pawn> output, Pawn mother)
        {
            if (!IsValidMother(mother))
            {
                return;
            }

            for (int i = 0; i < output.Count; i++)
            {
                if (ReferenceEquals(output[i], mother))
                {
                    return;
                }
            }

            output.Add(mother);
        }

        private static bool SharesSpeciesLineage(Pawn first, Pawn second)
        {
            if (first == null || second == null || first.def == null || second.def == null)
            {
                return false;
            }

            return first.def == second.def || ZoologyCacheUtility.AreCrossbreedRelated(first.def, second.def);
        }

        private static bool RecordSharesProtectorLineage(EggClutchOwnershipRecord record, Pawn protector)
        {
            if (record == null || protector == null)
            {
                return false;
            }

            CompHatcher hatcher = record.Egg?.TryGetComp<CompHatcher>();
            if (SharesSpeciesLineage(hatcher?.hatcheeParent, protector))
            {
                return true;
            }

            for (int i = 0; i < record.Mothers.Count; i++)
            {
                if (SharesSpeciesLineage(record.Mothers[i], protector))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RecordContainsMother(EggClutchOwnershipRecord record, Pawn mother)
        {
            if (record == null || mother == null)
            {
                return false;
            }

            if (ReferenceEquals(record.Egg?.TryGetComp<CompHatcher>()?.hatcheeParent, mother))
            {
                return true;
            }

            return ContainsMother(record, mother);
        }

        private static bool IsValidMother(Pawn mother)
        {
            return mother != null && !mother.Destroyed && !mother.Dead;
        }

        private static Thing ResolveThingById(int thingId)
        {
            if (thingId <= 0)
            {
                return null;
            }

            List<Map> maps = Find.Maps;
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                Map map = maps[mapIndex];
                List<Thing> allThings = map?.listerThings?.AllThings;
                if (allThings != null)
                {
                    for (int thingIndex = 0; thingIndex < allThings.Count; thingIndex++)
                    {
                        Thing thing = allThings[thingIndex];
                        if (thing != null && thing.thingIDNumber == thingId)
                        {
                            return thing;
                        }
                    }
                }

                IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                {
                    continue;
                }

                for (int pawnIndex = 0; pawnIndex < pawns.Count; pawnIndex++)
                {
                    Pawn pawn = pawns[pawnIndex];
                    if (pawn == null)
                    {
                        continue;
                    }

                    Thing carriedThing = pawn.carryTracker?.CarriedThing;
                    if (carriedThing != null && carriedThing.thingIDNumber == thingId)
                    {
                        return carriedThing;
                    }

                    ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
                    if (inventory == null)
                    {
                        continue;
                    }

                    for (int inventoryIndex = 0; inventoryIndex < inventory.Count; inventoryIndex++)
                    {
                        Thing item = inventory[inventoryIndex];
                        if (item != null && item.thingIDNumber == thingId)
                        {
                            return item;
                        }
                    }
                }
            }

            return null;
        }

        private void IndexRecordMothers(EggClutchOwnershipRecord record)
        {
            if (record?.Egg == null)
            {
                return;
            }

            int eggId = record.Egg.thingIDNumber;
            if (eggId <= 0)
            {
                return;
            }

            for (int i = 0; i < record.Mothers.Count; i++)
            {
                Pawn mother = record.Mothers[i];
                if (!IsValidMother(mother))
                {
                    continue;
                }

                int motherId = mother.thingIDNumber;
                if (motherId <= 0)
                {
                    continue;
                }

                if (!eggIdsByMotherId.TryGetValue(motherId, out HashSet<int> eggIds))
                {
                    eggIds = new HashSet<int>();
                    eggIdsByMotherId[motherId] = eggIds;
                }

                eggIds.Add(eggId);
            }
        }

        private void UnindexRecordMothers(EggClutchOwnershipRecord record)
        {
            if (record?.Egg == null)
            {
                return;
            }

            int eggId = record.Egg.thingIDNumber;
            if (eggId <= 0)
            {
                return;
            }

            for (int i = 0; i < record.Mothers.Count; i++)
            {
                Pawn mother = record.Mothers[i];
                int motherId = mother?.thingIDNumber ?? 0;
                if (motherId <= 0 || !eggIdsByMotherId.TryGetValue(motherId, out HashSet<int> eggIds))
                {
                    continue;
                }

                eggIds.Remove(eggId);
                if (eggIds.Count == 0)
                {
                    eggIdsByMotherId.Remove(motherId);
                }
            }
        }
    }
}
