using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public abstract class JobDriver_PetPlayBase : JobDriver
    {
        private const int FullValidationIntervalTicks = 60;

        private int interactivePetJobId = -1;
        private int nextFullValidationTick;
        private bool bondAttemptMade;

        protected Pawn Pet => job?.GetTarget(TargetIndex.A).Pawn;

        protected abstract bool RequiresCanine { get; }

        protected abstract bool RequiresOutdoors { get; }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref interactivePetJobId, "interactivePetJobId", -1);
            Scribe_Values.Look(ref nextFullValidationTick, "nextFullValidationTick", 0);
            Scribe_Values.Look(ref bondAttemptMade, "bondAttemptMade", false);
        }

        protected void RegisterPetCleanup()
        {
            AddFinishAction(delegate
            {
                StopPetJob();
            });
        }

        protected bool WaitForJoyDuration()
        {
            return Find.TickManager.TicksGame < startTick + job.def.joyDuration;
        }

        protected bool PetStillDoingInteractiveJob()
        {
            if (interactivePetJobId < 0)
            {
                return false;
            }

            Job currentJob = Pet?.CurJob;
            if (currentJob == null || currentJob.loadID != interactivePetJobId)
            {
                interactivePetJobId = -1;
                return false;
            }

            return true;
        }

        protected Toil StartPetJob(
            JobDef petJobDef,
            LocomotionUrgency urgency,
            Func<LocalTargetInfo> targetGetter = null)
        {
            return new Toil
            {
                initAction = delegate
                {
                    Pawn pet = Pet;
                    if (pet?.jobs == null)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    LocalTargetInfo target = targetGetter == null
                        ? new LocalTargetInfo(pawn)
                        : targetGetter();
                    if (!target.IsValid)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    pet.jobs.StopAll();
                    Job petJob = targetGetter == null
                        ? JobMaker.MakeJob(petJobDef, pawn)
                        : JobMaker.MakeJob(petJobDef, target, pawn);
                    petJob.locomotionUrgency = urgency;
                    petJob.expiryInterval = Math.Max(job.def.joyDuration, 600);
                    interactivePetJobId = petJob.loadID;
                    pet.jobs.StartJob(petJob);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        protected Toil EndPetJob()
        {
            return new Toil
            {
                initAction = StopPetJob,
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        protected Toil GoToPet(LocomotionUrgency urgency)
        {
            Toil toil = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            toil.AddPreInitAction(delegate
            {
                job.locomotionUrgency = urgency;
            });
            return MakeRecreational(toil, RandomSocialMode.Quiet);
        }

        protected Toil TalkToPet()
        {
            Toil toil = new Toil
            {
                initAction = delegate
                {
                    Pawn pet = Pet;
                    if (pet != null)
                    {
                        pawn.rotationTracker.FaceTarget(pet);
                        pawn.interactions.TryInteractWith(pet, InteractionDefOf.AnimalChat);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = 90
            };
            return MakeRecreational(toil);
        }

        protected Toil TryDevelopBondAtPlayStart()
        {
            return new Toil
            {
                initAction = delegate
                {
                    if (bondAttemptMade)
                    {
                        return;
                    }

                    bondAttemptMade = true;
                    PetPlayUtility.TryDevelopBondOnPlayStart(pawn, Pet);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        protected Toil HoldPosition(int ticks)
        {
            return MakeRecreational(new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = ticks
            });
        }

        protected Toil RepeatToilOnCondition(Toil destination, params Func<bool>[] conditions)
        {
            return new Toil
            {
                initAction = delegate
                {
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        if (!conditions[i]())
                        {
                            return;
                        }
                    }

                    JumpToToil(destination);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        protected Toil MakeRecreational(Toil toil, RandomSocialMode socialMode = RandomSocialMode.SuperActive)
        {
            toil.tickIntervalAction = delegate (int delta)
            {
                JoyUtility.JoyTickCheckEnd(pawn, delta);
            };
            toil.socialMode = socialMode;
            toil.FailOn(ShouldFailCurrentPlay);
            toil.FailOnDespawnedOrNull(TargetIndex.A);
            return toil;
        }

        private bool ShouldFailCurrentPlay()
        {
            Pawn pet = Pet;
            if (pet == null
                || pet.Destroyed
                || pet.Dead
                || !pet.Spawned
                || pet.Map != pawn?.Map)
            {
                StopPetJob();
                return true;
            }

            int now = Find.TickManager.TicksGame;
            if (now < nextFullValidationTick)
            {
                return false;
            }

            nextFullValidationTick = now + FullValidationIntervalTicks;
            if (PetPlayUtility.CanContinuePlaying(pawn, pet, RequiresCanine, RequiresOutdoors))
            {
                return false;
            }

            StopPetJob();
            return true;
        }

        private void StopPetJob()
        {
            if (interactivePetJobId < 0)
            {
                return;
            }

            Pawn pet = Pet;
            Job currentJob = pet?.CurJob;
            if (currentJob != null && currentJob.loadID == interactivePetJobId)
            {
                pet.jobs.EndCurrentJob(JobCondition.Succeeded);
            }

            interactivePetJobId = -1;
        }
    }

    public abstract class JobDriver_CanineOutdoorPlayBase : JobDriver_PetPlayBase
    {
        private List<LocalTargetInfo> path;
        private int nextPathIndex;

        protected override bool RequiresCanine => true;

        protected override bool RequiresOutdoors => true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextPathIndex, "nextPathIndex", 0);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            path = job.targetQueueB;
            return path != null
                && path.Count > 0
                && pawn.Reserve(Pet, job, errorOnFailed: errorOnFailed);
        }

        protected LocalTargetInfo NextWaypoint(bool preserve)
        {
            path = path ?? job.targetQueueB;
            if (path == null || nextPathIndex >= path.Count)
            {
                return new LocalTargetInfo(IntVec3.Invalid);
            }

            LocalTargetInfo target = path[nextPathIndex];
            if (!preserve)
            {
                nextPathIndex++;
            }

            return target;
        }

        protected Toil WalkToNextWaypoint()
        {
            Toil toil = new Toil
            {
                initAction = delegate
                {
                    LocalTargetInfo waypoint = NextWaypoint(false);
                    if (!waypoint.IsValid)
                    {
                        EndJobWith(JobCondition.Succeeded);
                        return;
                    }

                    if (!pawn.CanReach(waypoint, PathEndMode.OnCell, Danger.None))
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    job.locomotionUrgency = LocomotionUrgency.Walk;
                    pawn.pather.StartPath(waypoint.Cell, PathEndMode.OnCell);
                },
                defaultCompleteMode = ToilCompleteMode.PatherArrival
            };
            return MakeRecreational(toil);
        }

        protected Toil ThrowFetchToy()
        {
            Toil toil = new Toil
            {
                initAction = delegate
                {
                    LocalTargetInfo target = NextWaypoint(true);
                    if (!target.IsValid || !pawn.CanReach(target, PathEndMode.OnCell, Danger.None))
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    job.targetB = target;
                    pawn.rotationTracker.FaceTarget(target);
                    FleckMaker.ThrowStone(pawn, target.Cell);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            return MakeRecreational(toil);
        }
    }

    public sealed class JobDriver_WalkCanine : JobDriver_CanineOutdoorPlayBase
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            RegisterPetCleanup();

            yield return StartPetJob(ZoologyPetPlayDefOf.Zoology_PetWait, LocomotionUrgency.None);
            yield return GoToPet(LocomotionUrgency.Jog);
            yield return TryDevelopBondAtPlayStart();
            yield return TalkToPet();
            yield return StartPetJob(JobDefOf.Follow, LocomotionUrgency.Walk);

            Toil walk = WalkToNextWaypoint();
            yield return walk;
            yield return RepeatToilOnCondition(walk, WaitForJoyDuration, PetStillDoingInteractiveJob);

            yield return GoToPet(LocomotionUrgency.Jog);
            yield return EndPetJob();
            yield return TalkToPet();
        }
    }

    public sealed class JobDriver_PlayFetchCanine : JobDriver_CanineOutdoorPlayBase
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            RegisterPetCleanup();

            yield return StartPetJob(ZoologyPetPlayDefOf.Zoology_PetWait, LocomotionUrgency.None);
            yield return GoToPet(LocomotionUrgency.Jog);
            yield return TryDevelopBondAtPlayStart();
            yield return TalkToPet();

            Toil follow = StartPetJob(JobDefOf.Follow, LocomotionUrgency.Walk);
            yield return follow;
            yield return WalkToNextWaypoint();
            yield return ThrowFetchToy();
            yield return StartPetJob(
                ZoologyPetPlayDefOf.Zoology_PetFetch,
                LocomotionUrgency.Sprint,
                delegate { return NextWaypoint(false); });

            Toil wait = HoldPosition(30);
            yield return wait;
            yield return RepeatToilOnCondition(wait, WaitForJoyDuration, PetStillDoingInteractiveJob);
            yield return RepeatToilOnCondition(follow, WaitForJoyDuration);

            yield return GoToPet(LocomotionUrgency.Jog);
            yield return EndPetJob();
            yield return TalkToPet();
        }
    }

    public sealed class JobDriver_PlayWithPet : JobDriver_PetPlayBase
    {
        protected override bool RequiresCanine => false;

        protected override bool RequiresOutdoors => false;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Pet, job, errorOnFailed: errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            RegisterPetCleanup();

            yield return StartPetJob(ZoologyPetPlayDefOf.Zoology_PetWait, LocomotionUrgency.None);
            yield return GoToPet(LocomotionUrgency.Jog);
            yield return TryDevelopBondAtPlayStart();
            yield return TalkToPet();

            Toil chooseToyCell = new Toil
            {
                initAction = delegate
                {
                    if (!PetPlayUtility.TryFindToyCell(pawn, Pet, out IntVec3 cell))
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    job.targetB = cell;
                    pawn.rotationTracker.FaceTarget(cell);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return chooseToyCell;

            Toil teaseWithToy = MakeRecreational(new Toil
            {
                initAction = delegate
                {
                    pawn.rotationTracker.FaceTarget(job.GetTarget(TargetIndex.B));
                },
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = 45
            });
            yield return teaseWithToy;

            yield return StartPetJob(
                ZoologyPetPlayDefOf.Zoology_PetChaseToy,
                LocomotionUrgency.Sprint,
                delegate { return job.GetTarget(TargetIndex.B); });

            Toil wait = HoldPosition(30);
            yield return wait;
            yield return RepeatToilOnCondition(wait, WaitForJoyDuration, PetStillDoingInteractiveJob);
            yield return RepeatToilOnCondition(chooseToyCell, WaitForJoyDuration);

            yield return EndPetJob();
            yield return GoToPet(LocomotionUrgency.Jog);
            yield return TalkToPet();
        }
    }

    public sealed class JobDriver_PetWait : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil wait = Toils_General.Wait(job.expiryInterval > 0 ? job.expiryInterval : 8000);
            wait.AddPreInitAction(delegate
            {
                if (job.GetTarget(TargetIndex.A).IsValid)
                {
                    pawn.rotationTracker.FaceTarget(job.GetTarget(TargetIndex.A));
                }
            });
            yield return wait;
        }
    }

    public sealed class JobDriver_PetFetch : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil watch = Toils_General.Wait(120);
            watch.AddPreInitAction(delegate
            {
                pawn.rotationTracker.FaceTarget(job.GetTarget(TargetIndex.A));
            });
            yield return watch;

            Toil sprint = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            sprint.AddPreInitAction(delegate { job.locomotionUrgency = LocomotionUrgency.Sprint; });
            yield return sprint;

            Toil pause = Toils_General.Wait(90);
            pause.AddPreInitAction(delegate
            {
                pawn.rotationTracker.FaceTarget(job.GetTarget(TargetIndex.B));
            });
            yield return pause;

            Toil returnToPawn = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            returnToPawn.AddPreInitAction(delegate { job.locomotionUrgency = LocomotionUrgency.Jog; });
            yield return returnToPawn;
            yield return Toils_General.Wait(90);
        }
    }

    public sealed class JobDriver_PetChaseToy : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil watch = Toils_General.Wait(45);
            watch.AddPreInitAction(delegate
            {
                pawn.rotationTracker.FaceTarget(job.GetTarget(TargetIndex.A));
            });
            yield return watch;

            Toil sprint = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            sprint.AddPreInitAction(delegate { job.locomotionUrgency = LocomotionUrgency.Sprint; });
            yield return sprint;

            Toil pounce = Toils_General.Wait(90);
            pounce.AddPreInitAction(delegate
            {
                pawn.rotationTracker.FaceTarget(job.GetTarget(TargetIndex.B));
            });
            yield return pounce;
        }
    }
}
