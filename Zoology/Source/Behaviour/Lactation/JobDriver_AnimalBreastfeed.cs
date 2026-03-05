

using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace ZoologyMod
{
    public class JobDriver_AnimalBreastfeed : JobDriver
    {
        private const TargetIndex BabyInd = TargetIndex.A;
        private const TargetIndex MomInd = TargetIndex.B;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn mom = this.job.GetTarget(MomInd).Thing as Pawn;
            Pawn baby = this.job.GetTarget(BabyInd).Thing as Pawn;
            if (mom == null || baby == null) return false;
            if (mom.Dead || baby.Dead || mom.Downed || !mom.Spawned || !baby.Spawned) return false;

            if (!this.pawn.Reserve(baby, this.job, 1, -1, null, errorOnFailed))
            {
                return false;
            }
            if (!this.pawn.Reserve(mom, this.job, 1, -1, null, errorOnFailed))
            {
                try
                {
                    var rm = baby.Map?.reservationManager;
                    if (rm != null) rm.Release(new LocalTargetInfo(baby), this.pawn, this.job);
                }
                catch { }
                return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(BabyInd);
            this.FailOnDespawnedNullOrForbidden(MomInd);

            yield return Toils_Goto.GotoThing(BabyInd, PathEndMode.Touch);

            Toil suckle = new Toil();
            suckle.initAction = delegate
            {
                try
                {
                    Pawn baby = this.job.GetTarget(BabyInd).Thing as Pawn;
                    if (baby == null || baby.Dead)
                    {
                        return;
                    }

                    JobDef jd = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_YoungSuckle");
                    if (jd == null)
                    {
                        Job fallback = JobMaker.MakeJob(JobDefOf.Wait, 2000);
                        fallback.checkOverrideOnExpire = false;
                        baby.jobs.StartJob(fallback, JobCondition.InterruptForced, null, false);
                    }
                    else if (jd.driverClass == null)
                    {
                        Job fallback = JobMaker.MakeJob(JobDefOf.Wait, 2000);
                        fallback.checkOverrideOnExpire = false;
                        baby.jobs.StartJob(fallback, JobCondition.InterruptForced, null, false);
                    }
                    else
                    {
                        try
                        {
                            Job babyJob = JobMaker.MakeJob(jd);
                            babyJob.checkOverrideOnExpire = false;
                            baby.jobs.StartJob(babyJob, JobCondition.InterruptForced, null, false);
                        }
                        catch (Exception exJob)
                        {
                            Log.Error($"ZoologyMod: Failed to StartJob for child jobDef {jd?.defName ?? "<null>"}: {exJob}. Falling back to Wait job.");
                            Job fallback = JobMaker.MakeJob(JobDefOf.Wait, 2000);
                            fallback.checkOverrideOnExpire = false;
                            baby.jobs.StartJob(fallback, JobCondition.InterruptForced, null, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("ZoologyMod: Exception in suckle.initAction: " + ex);
                }
            };

            suckle.tickAction = delegate
            {
                try
                {
                    Pawn baby = this.job.GetTarget(BabyInd).Thing as Pawn;
                    Pawn mum = this.job.GetTarget(MomInd).Thing as Pawn;
                    if (baby == null || mum == null || baby.Dead || mum.Dead || mum.Downed)
                    {
                        if (baby != null && !baby.Dead) EndBabySuckleJob(baby);
                        this.pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                        return;
                    }

                    if ((mum.Position - baby.Position).LengthHorizontalSquared > 2f)
                    {
                        EndBabySuckleJob(baby);
                        this.pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        return;
                    }

                    if (!AnimalChildcareUtility.MotherHasSufficientNutrition(mum))
                    {
                        EndBabySuckleJob(baby);
                        this.pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                        return;
                    }

                    bool babyFull = AnimalChildcareUtility.SuckleFromLactatingPawn(baby, mum, 1);
                    if (babyFull)
                    {
                        EndBabySuckleJob(baby);
                        this.pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                        return;
                    }

                    var lactDef = DefDatabase<HediffDef>.GetNamedSilentFail("Zoology_Lactating");
                    if (lactDef == null)
                    {
                        Log.Warning("ZoologyMod: suckle.tickAction: HediffDef 'Zoology_Lactating' not found.");
                        return;
                    }
                    if (!mum.health.hediffSet.HasHediff(lactDef))
                    {
                        EndBabySuckleJob(baby);
                        this.pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("ZoologyMod: Exception in suckle.tickAction: " + ex);
                }
            };

            suckle.defaultCompleteMode = ToilCompleteMode.Never;
            suckle.WithProgressBar(BabyInd, () =>
            {
                Pawn baby = this.job.GetTarget(BabyInd).Thing as Pawn;
                if (baby?.needs?.food == null) return 0f;
                return baby.needs.food.CurLevelPercentage;
            });

            yield return suckle;
        }
        private void EndBabySuckleJob(Pawn baby)
        {
            if (baby == null || baby.jobs == null || baby.CurJob == null || baby.Dead) return;
            JobDef suckleDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_YoungSuckle");
            if ((suckleDef != null && baby.CurJob.def == suckleDef) || baby.CurJob.def == JobDefOf.Wait)
            {
                baby.jobs.EndCurrentJob(JobCondition.Succeeded);
            }
        }
    }
}