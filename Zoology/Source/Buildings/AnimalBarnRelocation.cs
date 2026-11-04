#if RIMWORLD17
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // A contained pawn is not a spawned work target. The handler must first
    // reach the barn and release that pawn, then hand off to the actual
    // vanilla rope job (or the existing barn rope job).
    internal static class AnimalBarnRelocation
    {
        public static bool TryFind(Pawn handler, Building_AnimalBarn source,
            out Pawn resident, out Thing destination)
        {
            resident = null;
            destination = null;
            if (!Building_AnimalBarn.Enabled || handler?.Map == null
                || source?.Spawned != true || source.Map != handler.Map
                || source.Faction != Faction.OfPlayer || source.IsForbidden(handler)
                || !handler.CanReserveAndReach(source, PathEndMode.Touch, Danger.Some))
            {
                return false;
            }

            foreach (Pawn candidate in source.Animals)
            {
                if (candidate == null || candidate.Destroyed || candidate.Dead
                    || candidate.Downed || candidate.Faction != Faction.OfPlayer
                    || source.AllowsResident(candidate)
                    || !AnimalPenUtility.IsRopeManagedAnimalDef(candidate.def)
                    || !candidate.Roamer)
                {
                    continue;
                }

                Thing target = FindDestination(handler, source, candidate);
                if (target != null)
                {
                    resident = candidate;
                    destination = target;
                    return true;
                }
            }

            return false;
        }

        private static bool Reachable(Pawn handler, Building_AnimalBarn source,
            Pawn animal, Thing target, PathEndMode mode)
        {
            return target != null && target.Spawned && !target.IsForbidden(handler)
                && handler.CanReach(target, PathEndMode.Touch, Danger.Some)
                && source.Map.reachability.CanReach(source.InteractionCell, target,
                    mode, TraverseParms.For(handler).WithFenceblockedOf(animal));
        }

        // First try enclosed vanilla pens and other barns by proximity.
        // If none is eligible, use a hitching post, then an unenclosed pen,
        // matching the vanilla pen work giver's fallback categories.
        private static Thing FindDestination(Pawn handler, Building_AnimalBarn source, Pawn animal)
        {
            Map map = source.Map;
            Thing best = null;
            int bestDistance = int.MaxValue;
            foreach (Building building in map.listerBuildings.allBuildingsAnimalPenMarkers)
            {
                CompAnimalPenMarker marker = building.TryGetComp<CompAnimalPenMarker>();
                if (marker?.PenState == null || !marker.PenState.Enclosed
                    || !marker.AcceptsToPen(animal)
                    || !Reachable(handler, source, animal, building, PathEndMode.Touch))
                {
                    continue;
                }

                // Reuse the vanilla pen job's cell placement check.
                if (!AnimalPenUtility.FindPlaceInPenToStand(marker, handler).IsValid)
                {
                    continue;
                }

                int distance = source.InteractionCell.DistanceToSquared(building.Position);
                if (distance < bestDistance)
                {
                    best = building;
                    bestDistance = distance;
                }
            }

            foreach (Building_AnimalBarn barn in AnimalBarnUtility.BarnsOnMap(map))
            {
                if (barn == source || !barn.CanAcceptTransferredResident(animal)
                    || !Reachable(handler, source, animal, barn, PathEndMode.Touch)
                    || !map.reachability.CanReach(source.InteractionCell,
                        barn.InteractionCell, PathEndMode.OnCell,
                        TraverseParms.For(handler).WithFenceblockedOf(animal)))
                {
                    continue;
                }

                int distance = source.InteractionCell.DistanceToSquared(barn.InteractionCell);
                if (distance < bestDistance)
                {
                    best = barn;
                    bestDistance = distance;
                }
            }

            if (best != null)
            {
                return best;
            }

            foreach (Building hitch in map.listerBuildings.allBuildingsHitchingPosts)
            {
                if (!Reachable(handler, source, animal, hitch, PathEndMode.Touch))
                {
                    continue;
                }

                int distance = source.InteractionCell.DistanceToSquared(hitch.Position);
                if (distance < bestDistance)
                {
                    best = hitch;
                    bestDistance = distance;
                }
            }

            if (best != null)
            {
                return best;
            }

            // Some vanilla handling configurations allow an unenclosed pen
            // as a last resort. It remains a native pen-rope destination.
            foreach (Building building in map.listerBuildings.allBuildingsAnimalPenMarkers)
            {
                CompAnimalPenMarker marker = building.TryGetComp<CompAnimalPenMarker>();
                if (marker?.PenState == null || !marker.PenState.Unenclosed
                    || !marker.AcceptsToPen(animal)
                    || !Reachable(handler, source, animal, building, PathEndMode.Touch)
                    || !AnimalPenUtility.FindPlaceInPenToStand(marker, handler).IsValid)
                {
                    continue;
                }

                int distance = source.InteractionCell.DistanceToSquared(building.Position);
                if (distance < bestDistance)
                {
                    best = building;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static Job MakeRopeJob(Pawn handler, Pawn animal, Thing destination)
        {
            if (animal?.Spawned != true || destination?.Spawned != true
                || animal.Map != destination.Map)
            {
                return null;
            }

            if (destination is Building_AnimalBarn barn)
            {
                return barn.CanAcceptAnimal(animal) && !barn.IsForbidden(handler)
                    && AnimalBarnUtility.CanReachBarn(animal, barn, handler)
                    ? JobMaker.MakeJob(ZoologyBarnJobDefOf.Zoology_BarnAdmit,
                        animal, barn.InteractionCell, barn)
                    : null;
            }

            CompAnimalPenMarker marker = destination.TryGetComp<CompAnimalPenMarker>();
            if (marker != null)
            {
                if (marker.PenState == null || !marker.AcceptsToPen(animal)
                    || destination.IsForbidden(handler))
                {
                    return null;
                }

                return WorkGiver_TakeToPen.MakeJob(handler, animal, marker,
                    marker.PenState.Unenclosed, RopingPriority.Closest, out _);
            }

            return handler.Map.listerBuildings.allBuildingsHitchingPosts.Contains(destination as Building)
                && !destination.IsForbidden(handler)
                ? JobMaker.MakeJob(JobDefOf.RopeRoamerToHitchingPost, animal, destination)
                : null;
        }

        public static bool TryReleaseAndQueueNursing(Pawn handler,
            Building_AnimalBarn source, Pawn resident, Thing destination)
        {
            Map map = source?.Map;
            if (handler == null || map == null || resident == null
                || destination == null || !source.ContainsAnimal(resident)
                || source.Faction != Faction.OfPlayer
                || !AnimalNursingBalance.StillNeeded(source, resident, destination)
                || destination.IsForbidden(handler)
                || !handler.CanReach(destination, PathEndMode.Touch, Danger.Some)
                || !map.reachability.CanReach(source.InteractionCell,
                    destination, PathEndMode.Touch,
                    TraverseParms.For(handler).WithFenceblockedOf(resident)))
            {
                AnimalNursingBalance.Cancel(resident, map);
                return false;
            }

            if (!source.TryReleaseAnimal(resident))
            {
                AnimalNursingBalance.Cancel(resident, map);
                return false;
            }

            Job ropeJob = MakeRopeJob(handler, resident, destination);
            if (ropeJob == null)
            {
                // A real pawn is now outside. Its next handler can use the
                // usual pen/barn jobs; never teleport it into storage.
                AnimalNursingBalance.Cancel(resident, map);
                return false;
            }

            handler.jobs.jobQueue.EnqueueFirst(ropeJob);
            return true;
        }

        public static bool TryReleaseAndQueue(Pawn handler, Building_AnimalBarn source,
            Pawn resident, Thing plannedDestination)
        {
            if (handler == null || source == null || resident == null
                || !source.ContainsAnimal(resident) || source.AllowsResident(resident))
            {
                return false;
            }

            // Recheck just before unloading: another worker may have filled
            // the destination, or the animal filter may have changed again.
            Thing destination = FindDestination(handler, source, resident);
            if (destination == null)
            {
                return false;
            }

            if (!source.TryReleaseAnimal(resident))
            {
                return false;
            }

            Job nextJob = MakeRopeJob(handler, resident, destination);
            if (nextJob == null && plannedDestination != destination)
            {
                nextJob = MakeRopeJob(handler, resident, plannedDestination);
            }
            if (nextJob == null)
            {
                // The animal is now a normal spawned roamer. Vanilla and
                // Zoology work givers can pick it up on a subsequent scan.
                return false;
            }

            handler.jobs.jobQueue.EnqueueFirst(nextJob);
            return true;
        }
    }

    public sealed class WorkGiver_BarnRelocate : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !Building_AnimalBarn.Enabled || pawn?.Map == null
                || pawn.Map.listerThings.ThingsOfDef(Building_AnimalBarn.BarnDef).Count == 0;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerThings.ThingsOfDef(Building_AnimalBarn.BarnDef);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return t is Building_AnimalBarn barn
                && AnimalBarnRelocation.TryFind(pawn, barn, out _, out _);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_AnimalBarn barn)
                || !AnimalBarnRelocation.TryFind(pawn, barn, out Pawn animal,
                    out Thing destination))
            {
                return null;
            }

            return JobMaker.MakeJob(ZoologyBarnJobDefOf.Zoology_BarnRelocate,
                barn, animal, destination);
        }
    }

    public sealed class JobDriver_BarnRelocate : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // The contained animal cannot be reserved until it is spawned.
            // Exclusive access to the source barn prevents two handlers from
            // trying to extract the same animal at once.
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1,
                null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                var source = job.GetTarget(TargetIndex.A).Thing as Building_AnimalBarn;
                var resident = job.GetTarget(TargetIndex.B).Thing as Pawn;
                Thing destination = job.GetTarget(TargetIndex.C).Thing;
                bool nursing = job.def ==
                    ZoologyBarnJobDefOf.Zoology_NursingRelocateFromBarn;
                bool success = nursing
                    ? AnimalBarnRelocation.TryReleaseAndQueueNursing(
                        pawn, source, resident, destination)
                    : AnimalBarnRelocation.TryReleaseAndQueue(
                        pawn, source, resident, destination);
                if (!success)
                    EndJobWith(JobCondition.Incompletable);
            });
        }
    }
}
#endif
