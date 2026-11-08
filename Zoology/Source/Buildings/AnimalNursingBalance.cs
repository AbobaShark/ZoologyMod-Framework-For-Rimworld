#if RIMWORLD17
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Shared, map-local planning for vanilla pens and Zoology barns. Nursing
    // itself is NOT simulated here; actual milk transfer and maternal
    // nutrition remain in AnimalLactationUtility/vanilla jobs.
    internal static class AnimalNursingBalance
    {
        internal sealed class Place
        {
            internal Building_AnimalBarn Barn;
            internal CompAnimalPenMarker Pen;
            internal readonly List<CompAnimalPenMarker> PenMarkers =
                new List<CompAnimalPenMarker>();
            internal readonly List<Pawn> Young = new List<Pawn>();
            internal readonly List<Pawn> Nurses = new List<Pawn>();
            internal Thing Building => Barn ?? Pen?.parent;
        }

        internal sealed class Transfer
        {
            internal Place From;
            internal Place To;
            internal Pawn Nurse;
            internal CompAnimalPenMarker DestinationPen;
            internal float RecoveredDemand;
            internal float ReducedLoad;
            internal Thing Source => From.Building is Building_AnimalBarn
                ? From.Building : Nurse;
            internal Thing Destination => To.Barn ?? DestinationPen?.parent;
        }

        private struct Coverage
        {
            internal float Unserved;
            internal float Load;
        }

        private sealed class Snapshot
        {
            internal int BuiltAt = -1;
            internal readonly List<Place> Places = new List<Place>();
            internal readonly List<Transfer> Transfers = new List<Transfer>();
            internal Thing[] Sources = Array.Empty<Thing>();
            internal bool BarnsEnabledAtBuild;
            internal Transfer Pending;
            internal Pawn Handler;
            internal int PendingAt;
        }

        private const int RefreshTicks = 250;
        private const int PendingStartGraceTicks = 600;
        private const int MaximumTransferTicks = 15000;
        private static Game cachedGame;
        private static readonly Dictionary<Map, Snapshot> snapshots =
            new Dictionary<Map, Snapshot>();

        internal static bool Enabled => LactationSettingsGate.Enabled()
            && ModConstants.Settings?.DisableAllRuntimePatches != true;

        private static bool IsNurse(Pawn p) => p != null
            && !p.Dead && !p.Destroyed && p.Faction == Faction.OfPlayer
            && AnimalNursingFilterUtility.IsLactatingFemale(p)
            && AnimalLactationUtility.CanMotherFeed(p);

        private static bool IsYoung(Pawn p) => p != null
            && !p.Dead && !p.Destroyed && p.Faction == Faction.OfPlayer
            && AnimalNursingFilterUtility.IsMilkDependent(p)
            && p.needs?.food != null;

        private static bool Compatible(Pawn nurse, Pawn young) =>
            AnimalLactationUtility.IsCrossBreedCompatible(nurse, young);

        private static Coverage Measure(Place place, Pawn without = null,
            Pawn with = null)
        {
            var result = new Coverage();
            for (int i = 0; i < place.Young.Count; i++)
            {
                Pawn young = place.Young[i];
                float daily = young.needs.food.FoodFallPerTick * 60000f;
                if (!(daily > 0f) || float.IsInfinity(daily)
                    || float.IsNaN(daily)) continue;

                int count = 0;
                for (int j = 0; j < place.Nurses.Count; j++)
                {
                    Pawn nurse = place.Nurses[j];
                    if (nurse != without && Compatible(nurse, young))
                        count++;
                }
                if (with != null && !place.Nurses.Contains(with)
                    && Compatible(with, young))
                    count++;

                if (count == 0) result.Unserved += daily;
                else result.Load += daily / count;
            }
            return result;
        }

        // Each infant's real food drain is apportioned over compatible
        // milk-providing mothers. Prioritize reducing uncovered demand,
        // then minimizing total demand per available compatible mother.
        // Never rescue one nursery by orphaning another.
        private static bool Evaluate(Place source, Place destination,
            Pawn nurse, out float rescued, out float loadImprovement)
        {
            rescued = 0f;
            loadImprovement = 0f;
            if (source == destination || destination.Young.Count == 0)
                return false;

            bool feedsTarget = false;
            foreach (Pawn baby in destination.Young)
                if (Compatible(nurse, baby))
                {
                    feedsTarget = true;
                    break;
                }
            if (!feedsTarget) return false;

            Coverage sourceBefore = Measure(source);
            Coverage targetBefore = Measure(destination);
            Coverage sourceAfter = Measure(source, without: nurse);
            Coverage targetAfter = Measure(destination, with: nurse);

            // A transfer must not make an orphaned cohort at its origin.
            if (sourceAfter.Unserved > sourceBefore.Unserved + .0001f)
                return false;

            rescued = sourceBefore.Unserved + targetBefore.Unserved
                - sourceAfter.Unserved - targetAfter.Unserved;
            loadImprovement = sourceBefore.Load + targetBefore.Load
                - sourceAfter.Load - targetAfter.Load;

            if (rescued < -.0001f) return false;
            return rescued > .0001f
                || Math.Abs(rescued) <= .0001f && loadImprovement > .0001f;
        }

        private static Snapshot Get(Map map, bool refresh = false)
        {
            if (cachedGame != Current.Game)
            {
                cachedGame = Current.Game;
                snapshots.Clear();
            }

            if (!snapshots.TryGetValue(map, out Snapshot snapshot))
            {
                snapshot = new Snapshot();
                snapshots.Add(map, snapshot);
            }

            int now = GenTicks.TicksGame;
            if (refresh || snapshot.BuiltAt < 0 || now < snapshot.BuiltAt
                || now - snapshot.BuiltAt >= RefreshTicks
                || snapshot.BarnsEnabledAtBuild != Building_AnimalBarn.Enabled)
            {
                Build(map, snapshot);
                snapshot.BuiltAt = now;
            }
            return snapshot;
        }

        private static void Build(Map map, Snapshot cache)
        {
            cache.Places.Clear();
            cache.Transfers.Clear();
            cache.Sources = Array.Empty<Thing>();
            cache.BarnsEnabledAtBuild = Building_AnimalBarn.Enabled;
            if (!Enabled || map == null || !map.IsPlayerHome)
                return;

            if (Building_AnimalBarn.Enabled)
                foreach (Building_AnimalBarn barn in AnimalBarnUtility.BarnsOnMap(map))
                {
                    if (barn.Faction != Faction.OfPlayer || !barn.Spawned)
                        continue;
                    var place = new Place { Barn = barn };
                    foreach (Pawn resident in barn.Animals)
                    {
                        if (IsYoung(resident)) place.Young.Add(resident);
                        if (IsNurse(resident)) place.Nurses.Add(resident);
                    }
                    cache.Places.Add(place);
                }

            // Multiple markers can describe the same physical pen. Count
            // each connected enclosure only once, using the first marker.
            foreach (Building building in map.listerBuildings.allBuildingsAnimalPenMarkers)
            {
                CompAnimalPenMarker marker = building.TryGetComp<CompAnimalPenMarker>();
                if (marker?.parent?.Faction != Faction.OfPlayer
                    || marker.PenState == null || !marker.PenState.Enclosed)
                    continue;
                HashSet<Region> regions = marker.PenState.ConnectedRegions;
                if (regions == null || regions.Count == 0)
                    continue;
                Place existing = null;
                foreach (Place place in cache.Places)
                {
                    if (place.Pen == null) continue;
                    foreach (Region region in regions)
                        if (place.Pen.PenState.ContainsConnectedRegion(region))
                        {
                            existing = place;
                            break;
                        }
                    if (existing != null) break;
                }
                if (existing != null)
                {
                    // Multiple markers can share a pen but have different
                    // animal filters. Any accepting marker can receive her.
                    existing.PenMarkers.Add(marker);
                    continue;
                }
                var penPlace = new Place { Pen = marker };
                penPlace.PenMarkers.Add(marker);
                cache.Places.Add(penPlace);
            }

            List<Pawn> spawned = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn animal = spawned[i];
                if (!IsYoung(animal) && !IsNurse(animal)) continue;
                if (animal.roping?.IsRopedByPawn == true) continue;
                Region region = animal.GetRegion();
                if (region == null) continue;
                foreach (Place place in cache.Places)
                    if (place.Pen != null
                        && place.Pen.PenState.ContainsConnectedRegion(region))
                    {
                        if (IsYoung(animal)) place.Young.Add(animal);
                        if (IsNurse(animal)) place.Nurses.Add(animal);
                        break;
                    }
            }

            for (int i = 0; i < cache.Places.Count; i++)
            {
                Place source = cache.Places[i];
                for (int m = 0; m < source.Nurses.Count; m++)
                {
                    Pawn nurse = source.Nurses[m];
                    if (!AnimalPenUtility.IsRopeManagedAnimalDef(nurse.def))
                        continue;

                    for (int j = 0; j < cache.Places.Count; j++)
                    {
                        Place target = cache.Places[j];
                        if (target == source || target.Young.Count == 0
                            || !DestinationAccepts(target, nurse,
                                out CompAnimalPenMarker targetPen))
                            continue;

                        if (!Evaluate(source, target, nurse,
                            out float rescued, out float reducedLoad))
                            continue;

                        cache.Transfers.Add(new Transfer {
                            From = source, To = target, Nurse = nurse,
                            DestinationPen = targetPen,
                            RecoveredDemand = rescued, ReducedLoad = reducedLoad
                        });
                    }
                }
            }

            cache.Transfers.Sort((a, b) =>
            {
                int byRescue = b.RecoveredDemand.CompareTo(a.RecoveredDemand);
                if (byRescue != 0) return byRescue;
                return b.ReducedLoad.CompareTo(a.ReducedLoad);
            });
            var seen = new HashSet<Thing>();
            var sources = new List<Thing>();
            foreach (Transfer transfer in cache.Transfers)
                if (transfer.Source != null && seen.Add(transfer.Source))
                    sources.Add(transfer.Source);
            // Replace rather than mutate: a vanilla work scanner can still
            // be enumerating the previous snapshot when a job is issued.
            cache.Sources = sources.ToArray();
        }

        private static bool DestinationAccepts(Place target, Pawn nurse,
            out CompAnimalPenMarker selectedMarker)
        {
            selectedMarker = null;
            if (target.Barn != null)
                return target.Barn.CanAcceptTransferredResident(nurse);

            foreach (CompAnimalPenMarker pen in target.PenMarkers)
                if (pen?.parent?.Spawned == true
                    && pen.PenState.Enclosed && pen.AcceptsToPen(nurse))
                {
                    selectedMarker = pen;
                    return true;
                }
            return false;
        }

        private static bool AtDestination(Transfer transfer)
        {
            Pawn mother = transfer.Nurse;
            if (transfer.To.Barn != null)
                return transfer.To.Barn.ContainsAnimal(mother);
            return mother?.Spawned == true
                && mother.GetRegion() is Region region
                && transfer.To.Pen?.PenState?.ContainsConnectedRegion(region) == true;
        }

        private static bool PendingMoveActive(Map map, Snapshot cache)
        {
            Transfer pending = cache.Pending;
            if (pending == null) return false;

            int elapsed = GenTicks.TicksGame - cache.PendingAt;
            Pawn mother = pending.Nurse;
            if (elapsed < 0 || mother == null || mother.Dead || mother.Destroyed
                || AtDestination(pending)
                || !pending.Destination.Spawned || !pending.From.Building.Spawned)
            {
                cache.Pending = null;
                cache.BuiltAt = -1;
                return false;
            }

            Job activeJob = cache.Handler?.CurJob;
            bool handling = activeJob != null
                && (activeJob.targetA.Thing == pending.Source
                    || activeJob.targetA.Thing == mother);
            if (elapsed < MaximumTransferTicks
                && (elapsed < PendingStartGraceTicks || handling))
                return true;

            cache.Pending = null;
            cache.BuiltAt = -1;
            return false;
        }

        internal static IEnumerable<Thing> WorkSources(Map map)
        {
            if (!Enabled || map == null) return Array.Empty<Thing>();
            Snapshot cache = Get(map);
            if (PendingMoveActive(map, cache))
                return Array.Empty<Thing>();
            // Arrival can invalidate the snapshot during the pending check.
            return Get(map).Sources;
        }

        // Vanilla pen balancing may optimize head counts without noticing
        // milk-dependent offspring. Let the dedicated nursing work giver
        // decide where these mothers belong instead of immediately undoing
        // a milk-critical move. Pens without dependent young are unchanged.
        internal static bool HasDependentYoungInPen(Pawn mother)
        {
            if (!Enabled || mother?.Spawned != true || mother.Map == null
                || !IsNurse(mother)) return false;
            Snapshot cache = Get(mother.Map);
            // The just-delivered mother may have moved since the latest
            // 250-tick snapshot. Refresh on confirmed arrival immediately.
            PendingMoveActive(mother.Map, cache);
            cache = Get(mother.Map);
            Region actualRegion = mother.GetRegion();
            foreach (Place place in cache.Places)
                if (place.Pen != null && place.Nurses.Contains(mother)
                    && actualRegion != null
                    && place.Pen.PenState.ContainsConnectedRegion(actualRegion))
                    foreach (Pawn young in place.Young)
                        if (Compatible(mother, young)) return true;
            return false;
        }

        private static bool Reachable(Pawn handler, Transfer transfer)
        {
            Pawn mother = transfer.Nurse;
            Thing source = transfer.Source;
            Thing target = transfer.Destination;
            if (source == null || !source.Spawned || target == null
                || !target.Spawned || target.IsForbidden(handler)
                || source.IsForbidden(handler) || !IsNurse(mother)
                || !handler.CanReserveAndReach(source, PathEndMode.Touch, Danger.Some)
                || !handler.CanReach(target, PathEndMode.Touch, Danger.Some))
                return false;

            if (transfer.To.Barn != null)
            {
                IntVec3 entrance = transfer.To.Barn.InteractionCell;
                if (!handler.Map.reachability.CanReach(handler.Position,
                        entrance, PathEndMode.OnCell,
                        TraverseParms.For(handler)))
                    return false;

                IntVec3 origin = transfer.From.Barn != null
                    ? transfer.From.Barn.InteractionCell : mother.Position;
                if (!handler.Map.reachability.CanReach(origin, entrance,
                        PathEndMode.OnCell,
                        TraverseParms.For(handler).WithFenceblockedOf(mother)))
                    return false;
            }

            if (transfer.From.Barn != null)
            {
                if (!transfer.From.Barn.ContainsAnimal(mother))
                    return false;
                return handler.Map.reachability.CanReach(
                    transfer.From.Barn.InteractionCell, target,
                    PathEndMode.Touch,
                    TraverseParms.For(handler).WithFenceblockedOf(mother));
            }

            if (mother?.Spawned != true || mother.Map != handler.Map
                || mother.roping == null || mother.roping.IsRopedByPawn)
                return false;
            if (!WorkGiver_InteractAnimal.CanInteractWithAnimal(
                handler, mother, out _, forced: false,
                canInteractWhileSleeping: true, ignoreSkillRequirements: true,
                canInteractWhileRoaming: true))
                return false;

            return handler.Map.reachability.CanReach(mother.Position, target,
                PathEndMode.Touch,
                TraverseParms.For(handler).WithFenceblockedOf(mother));
        }

        internal static Transfer FindFor(Pawn handler, Thing source)
        {
            if (!Enabled || handler?.Map == null || source == null)
                return null;
            Snapshot cache = Get(handler.Map);
            if (PendingMoveActive(handler.Map, cache))
                return null;
            cache = Get(handler.Map);
            foreach (Transfer transfer in cache.Transfers)
                if (transfer.Source == source && Reachable(handler, transfer))
                    return transfer;
            return null;
        }

        internal static Job TryAssign(Pawn handler, Thing source)
        {
            Transfer transfer = FindFor(handler, source);
            if (transfer == null) return null;

            // Re-evaluate after other jobs changed barn contents or pens.
            Snapshot cache = Get(handler.Map, refresh: true);
            if (PendingMoveActive(handler.Map, cache)) return null;
            Transfer fresh = null;
            foreach (Transfer candidate in cache.Transfers)
                if (candidate.Nurse == transfer.Nurse
                    && candidate.Source == transfer.Source
                    && candidate.Destination == transfer.Destination
                    && Reachable(handler, candidate))
                {
                    fresh = candidate;
                    break;
                }
            if (fresh == null) return null;

            Job job;
            if (fresh.From.Barn != null)
                job = JobMaker.MakeJob(
                    ZoologyBarnJobDefOf.Zoology_NursingRelocateFromBarn,
                    fresh.From.Barn, fresh.Nurse, fresh.Destination);
            else if (fresh.To.Barn != null)
                job = JobMaker.MakeJob(ZoologyBarnJobDefOf.Zoology_BarnAdmit,
                    fresh.Nurse, fresh.To.Barn.InteractionCell, fresh.To.Barn);
            else
                job = WorkGiver_TakeToPen.MakeJob(handler, fresh.Nurse,
                    fresh.DestinationPen, false, RopingPriority.Closest, out _);

            if (job != null)
            {
                cache.Pending = fresh;
                cache.Handler = handler;
                cache.PendingAt = GenTicks.TicksGame;
            }
            return job;
        }

        internal static void Cancel(Pawn nurse, Map map)
        {
            if (map != null && snapshots.TryGetValue(map, out Snapshot cache)
                && cache.Pending?.Nurse == nurse)
            {
                cache.Pending = null;
                cache.BuiltAt = -1;
            }
        }

        internal static bool StillNeeded(Building_AnimalBarn source,
            Pawn mother, Thing destination)
        {
            if (source?.Map == null || mother == null || destination == null
                || !source.ContainsAnimal(mother)) return false;

            Snapshot cache = Get(source.Map, refresh: true);
            foreach (Transfer transfer in cache.Transfers)
                if (transfer.Nurse == mother && transfer.From.Barn == source
                    && transfer.Destination == destination)
                    return true;
            return false;
        }

        internal static bool BalancingImproves(
            IList<Pawn> sourceYoung, IList<Pawn> sourceNurses,
            IList<Pawn> targetYoung, IList<Pawn> targetNurses, Pawn mother)
        {
            var source = new Place();
            var target = new Place();
            source.Young.AddRange(sourceYoung);
            source.Nurses.AddRange(sourceNurses);
            target.Young.AddRange(targetYoung);
            target.Nurses.AddRange(targetNurses);
            return Evaluate(source, target, mother, out _, out _);
        }
    }

    // One cached candidate map-wide; the work giver is not a per-tick
    // simulator and hands actual transportation to vanilla rope drivers.
    public sealed class WorkGiver_NursingBalance : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            !AnimalNursingBalance.Enabled || pawn?.Map == null
            || !pawn.Map.IsPlayerHome;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            AnimalNursingBalance.WorkSources(pawn.Map);

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            AnimalNursingBalance.FindFor(pawn, t) != null;

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            AnimalNursingBalance.TryAssign(pawn, t);
    }
}
#endif
