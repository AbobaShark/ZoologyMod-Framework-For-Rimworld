#if RIMWORLD17
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [DefOf]
    public static class ZoologyBarnJobDefOf
    {
        public static JobDef Zoology_BarnAdmit;
        public static JobDef Zoology_BarnFeed;
        public static JobDef Zoology_BarnRelocate;
        public static JobDef Zoology_NursingRelocateFromBarn;

        static ZoologyBarnJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ZoologyBarnJobDefOf));
        }
    }

    internal static class AnimalBarnWorkPriority
    {
        public static void MatchVanillaPens()
        {
            WorkGiverDef own = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Zoology_BarnAdmit");
            if (own == null)
            {
                return;
            }

            WorkGiverDef pen = null;
            foreach (WorkGiverDef candidate in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (candidate == own || candidate.workType != WorkTypeDefOf.Handling)
                {
                    continue;
                }

                string name = candidate.defName ?? string.Empty;
                string type = candidate.giverClass?.Name ?? string.Empty;
                if (name.IndexOf("Rope", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.IndexOf("Pen", StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf("Rope", StringComparison.OrdinalIgnoreCase) >= 0
                    && type.IndexOf("Pen", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    pen = candidate;
                    break;
                }
            }

            if (pen != null)
            {
                own.priorityInType = pen.priorityInType;
                WorkGiverDef requested = DefDatabase<WorkGiverDef>.GetNamedSilentFail(
                    "Zoology_BarnManualAdmit");
                if (requested != null)
                {
                    // Explicit player loading orders take precedence, while
                    // automatic barn/pen handling remains on equal footing.
                    requested.priorityInType = pen.priorityInType + 1;
                }

                WorkGiverDef relocation = DefDatabase<WorkGiverDef>.GetNamedSilentFail(
                    "Zoology_BarnRelocate");
                if (relocation != null)
                {
                    // Filter eviction comes before ordinary pen/barn intake.
                    relocation.priorityInType = pen.priorityInType + 2;
                }
                WorkGiverDef nursing = DefDatabase<WorkGiverDef>
                    .GetNamedSilentFail("Zoology_NursingBalance");
                if (nursing != null)
                    nursing.priorityInType = pen.priorityInType + 3;
            }
        }
    }

    public class WorkGiver_BarnAdmit : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !Building_AnimalBarn.Enabled || pawn?.Map == null
                || pawn.Map.listerThings.ThingsOfDef(Building_AnimalBarn.BarnDef).Count == 0;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Pawn> candidates = pawn.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn animal = candidates[i];
                if (animal.RaceProps?.Animal == true && animal != pawn)
                {
                    yield return animal;
                }
            }
        }

        internal static Building_AnimalBarn FindBarn(Pawn worker, Pawn animal,
            bool manualOnly = false, bool forced = false)
        {
            if (worker?.Map == null || animal?.Map != worker.Map
                || !AnimalPenUtility.NeedsToBeManagedByRope(animal)
                || animal.roping == null || !forced && animal.roping.IsRopedByPawn
                || !WorkGiver_InteractAnimal.CanInteractWithAnimal(
                    worker, animal, out _, forced,
                    canInteractWhileSleeping: true, ignoreSkillRequirements: true,
                    canInteractWhileRoaming: true)
                || !worker.CanReserveAndReach(animal, PathEndMode.Touch, Danger.Some))
            {
                return null;
            }

            List<Thing> things = worker.Map.listerThings.ThingsOfDef(Building_AnimalBarn.BarnDef);
            Building_AnimalBarn requested = null;
            int requestedDistance = int.MaxValue;
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Building_AnimalBarn barn)
                    || !barn.IsManualIntakeRequested(animal)
                    || !barn.CanAcceptAnimal(animal, ignoreAutomaticRules: true)
                    || barn.IsForbidden(worker)
                    || !worker.CanReserveAndReach(barn, PathEndMode.Touch, Danger.Some)
                    || !AnimalBarnUtility.CanReachBarn(animal, barn, worker))
                {
                    continue;
                }

                int distance = (animal.Position - barn.InteractionCell).LengthHorizontalSquared;
                if (distance < requestedDistance)
                {
                    requested = barn;
                    requestedDistance = distance;
                }
            }

            // A player-issued loading request wins over automatic pen
            // preference and the barn's species filter.
            if (requested != null || manualOnly)
            {
                return requested;
            }

            // Animals released for normal gathering should be tended before
            // another worker moves them back into storage.
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Building_AnimalBarn releasing
                    && releasing.IsAwaitingService(animal))
                {
                    return null;
                }
            }

            // For automatic handling, remain at the same priority as pens
            // and leave animals in an already suitable enclosed pen.
            if (AnimalPenUtility.GetCurrentPenOf(animal, allowUnenclosedPens: false) != null)
            {
                return null;
            }

            CompAnimalPenMarker pen = AnimalPenUtility.ClosestSuitablePen(animal, allowUnenclosedPens: false);
            int penDistance = pen != null
                ? (animal.Position - pen.parent.Position).LengthHorizontalSquared
                : int.MaxValue;

            Building_AnimalBarn selected = null;
            int bestDist = int.MaxValue;
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Building_AnimalBarn barn)
                    || !barn.CanAcceptAnimal(animal)
                    || barn.IsForbidden(worker)
                    || !worker.CanReserveAndReach(barn, PathEndMode.Touch, Danger.Some)
                    || !AnimalBarnUtility.CanReachBarn(animal, barn, worker))
                {
                    continue;
                }

                int distance = (animal.Position - barn.InteractionCell).LengthHorizontalSquared;
                if (distance >= penDistance || distance >= bestDist)
                {
                    continue;
                }

                selected = barn;
                bestDist = distance;
            }

            return selected;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return t is Pawn animal && FindBarn(pawn, animal, forced: forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn animal))
            {
                return null;
            }

            Building_AnimalBarn barn = FindBarn(pawn, animal, forced: forced);
            return barn == null ? null : JobMaker.MakeJob(ZoologyBarnJobDefOf.Zoology_BarnAdmit,
                animal, barn.InteractionCell, barn);
        }
    }

    // The vanilla right-click 'Lead to pen' work giver knows only pen
    // markers. When no ordinary pen can take the animal, allow its existing
    // manual command to use the same barn rope job as autonomous handling.
    // This is a targeted fallback, not a replacement for vanilla pen logic.
    [HarmonyPatch]
    internal static class Patch_BarnAsVanillaLeadToPenDestination
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(WorkGiver_TakeToPen),
                nameof(WorkGiver_TakeToPen.JobOnThing),
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
        }

        private static bool Prepare()
        {
            return Building_AnimalBarn.Enabled && TargetMethod() != null;
        }

        private static void Postfix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            if (!forced || __result != null || !Building_AnimalBarn.Enabled
                || !(t is Pawn animal))
            {
                return;
            }

            Building_AnimalBarn barn = WorkGiver_BarnAdmit.FindBarn(
                pawn, animal, forced: true);
            if (barn != null)
            {
                __result = JobMaker.MakeJob(ZoologyBarnJobDefOf.Zoology_BarnAdmit,
                    animal, barn.InteractionCell, barn);
            }
        }
    }

    // Explicit player orders take precedence over automatic pen assignment
    // without changing the ordinary barn handling job's priority.
    public sealed class WorkGiver_BarnManualAdmit : WorkGiver_BarnAdmit
    {
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return t is Pawn animal && FindBarn(pawn, animal, manualOnly: true) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn animal))
            {
                return null;
            }

            Building_AnimalBarn barn = FindBarn(pawn, animal, manualOnly: true);
            return barn == null ? null : JobMaker.MakeJob(
                ZoologyBarnJobDefOf.Zoology_BarnAdmit, animal, barn.InteractionCell, barn);
        }
    }

    // The same vanilla roping driver used for pens; no artificial Goto
    // orders or teleporting animals into the barn.
    public sealed class JobDriver_BarnAdmit : JobDriver_RopeToDestination
    {
        private Building_AnimalBarn Barn => job.GetTarget(TargetIndex.C).Thing as Building_AnimalBarn;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.C);
            return base.MakeNewToils();
        }

        protected override bool HasRopeeArrived(Pawn ropee, bool roperWaitingAtDest)
        {
            Building_AnimalBarn barn = Barn;
            return roperWaitingAtDest && barn != null && barn.Spawned
                && ropee.Spawned && ropee.Map == barn.Map
                && ropee.Position.InHorDistOf(barn.InteractionCell, 2f);
        }

        protected override void ProcessArrivedRopee(Pawn ropee)
        {
            Building_AnimalBarn barn = Barn;
            if (barn != null)
            {
                barn.TryStoreAnimal(ropee, ignoreAutomaticRules: barn.IsManualIntakeRequested(ropee));
            }
        }

        protected override bool ShouldOpportunisticallyRopeAnimal(Pawn animal)
        {
            // Capacity and filters may change during transport; only take the
            // one animal that has already been validated for this destination.
            return false;
        }

        protected override bool UpdateDestination()
        {
            Building_AnimalBarn barn = Barn;
            if (barn == null || !barn.Spawned)
            {
                return false;
            }

            IntVec3 position = barn.InteractionCell;
            if (job.GetTarget(TargetIndex.B).Cell == position)
            {
                return false;
            }

            job.SetTarget(TargetIndex.B, position);
            return true;
        }
    }

    public sealed class WorkGiver_BarnFeed : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool Prioritized => true;

        // The vanilla prioritized scanner selects the most under-supplied
        // eligible barn first. Distance breaks ties instead of making the
        // nearest barn absorb the entire map's feed inventory.
        public override float GetPriority(Pawn pawn, TargetInfo t)
        {
            return t.Thing is Building_AnimalBarn barn
                ? barn.FoodRestockPriority : -1f;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !Building_AnimalBarn.Enabled || pawn?.Map == null;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Thing> things = pawn.Map.listerThings.ThingsOfDef(Building_AnimalBarn.BarnDef);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Building_AnimalBarn barn && barn.NeedsFeed)
                {
                    yield return barn;
                }
            }
        }

        private static Thing FindFood(Pawn worker, Building_AnimalBarn barn)
        {
            if (worker?.Map == null || barn?.Map != worker.Map || !barn.NeedsFeed
                || !worker.CanReserveAndReach(barn, PathEndMode.Touch, Danger.Some))
            {
                return null;
            }

            List<Thing> foods = worker.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSource);
            Thing closest = null;
            Thing fallback = null;
            int closestDistance = int.MaxValue;
            int fallbackDistance = int.MaxValue;
            bool hasResidents = barn.AnimalCount > 0;
            for (int i = 0; i < foods.Count; i++)
            {
                Thing item = foods[i];
                if (!barn.CanAcceptFood(item) || item.IsForbidden(worker)
                    || !worker.CanReserveAndReach(item, PathEndMode.ClosestTouch, Danger.Some))
                {
                    continue;
                }

                int dist = (worker.Position - item.Position).LengthHorizontalSquared;
                // Food an existing resident can actually eat takes priority
                // over merely filter-allowed food. Still allow pre-stocking
                // empty barns or stockpiling another allowed food if no
                // compatible food is reachable.
                if (!hasResidents || barn.HasResidentEater(item))
                {
                    if (dist < closestDistance)
                    {
                        closest = item;
                        closestDistance = dist;
                    }
                }
                else if (dist < fallbackDistance)
                {
                    fallback = item;
                    fallbackDistance = dist;
                }
            }

            return closest ?? fallback;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return t is Building_AnimalBarn barn && FindFood(pawn, barn) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_AnimalBarn barn))
            {
                return null;
            }

            Thing food = FindFood(pawn, barn);
            if (food == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(ZoologyBarnJobDefOf.Zoology_BarnFeed, food, barn);
            job.count = barn.FoodStacksToAccept(food);
            return job.count > 0 ? job : null;
        }
    }

    public sealed class JobDriver_BarnFeed : JobDriver
    {
        private Building_AnimalBarn Barn => job.GetTarget(TargetIndex.B).Thing as Building_AnimalBarn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, job.count, null, errorOnFailed)
                && pawn.Reserve(job.GetTarget(TargetIndex.B), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return new Toil
            {
                initAction = () =>
                {
                    if (Barn == null || !Barn.TryAcceptCarriedFood(pawn))
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
#endif
