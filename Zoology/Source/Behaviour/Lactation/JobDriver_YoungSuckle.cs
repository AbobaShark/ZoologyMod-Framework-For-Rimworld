using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace ZoologyMod
{
    public class JobDriver_YoungSuckle : JobDriver
    {
        private const TargetIndex MomInd = TargetIndex.A;
        private const int suckleDurationTicks = ZoologyTickLimiter.Lactation.YoungSuckleDurationTicks;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (this.job != null)
            {
                this.job.checkOverrideOnExpire = false;
            }

            Pawn mom = this.job?.GetTarget(MomInd).Thing as Pawn;
            if (mom != null)
            {
                try
                {
                    if (!this.pawn.CanReserve(mom))
                    {
                        return false;
                    }

                    this.pawn.Reserve(mom, this.job, 1, -1, null, false);
                }
                catch
                {
                }
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Pawn momTarget = this.job?.GetTarget(MomInd).Thing as Pawn;
            if (momTarget != null)
            {
                this.FailOnDespawnedNullOrForbidden(MomInd);

                yield return Toils_Goto.GotoThing(MomInd, PathEndMode.Touch);

                Toil suckle = new Toil();
                suckle.tickAction = delegate
                {
                    try
                    {
                        Pawn baby = this.pawn;
                        Pawn mom = this.job.GetTarget(MomInd).Thing as Pawn;

                        if (baby == null || baby.Dead || baby.Downed)
                        {
                            baby?.jobs?.EndCurrentJob(JobCondition.Incompletable);
                            return;
                        }

                        if (baby.InMentalState)
                        {
                            baby.jobs?.EndCurrentJob(JobCondition.Incompletable);
                            return;
                        }

                        if (mom == null || mom.Dead || !mom.Spawned || mom.InMentalState)
                        {
                            baby.jobs?.EndCurrentJob(JobCondition.Incompletable);
                            return;
                        }

                        if (!AnimalLactationUtility.CanPupSelfSuckleFromMother(mom))
                        {
                            baby.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                            return;
                        }

                        if (baby.Map == null || mom.Map != baby.Map)
                        {
                            baby.jobs?.EndCurrentJob(JobCondition.Incompletable);
                            return;
                        }

                        if (baby.needs?.food != null && baby.needs.food.CurLevelPercentage >= 0.99f)
                        {
                            baby.jobs.EndCurrentJob(JobCondition.Succeeded);
                            return;
                        }

                        if ((baby.Position - mom.Position).LengthHorizontalSquared > 2f)
                        {
                            baby.jobs.EndCurrentJob(JobCondition.InterruptForced);
                            return;
                        }

                        if (!AnimalLactationUtility.MotherHasSufficientNutrition(mom))
                        {
                            baby.jobs.EndCurrentJob(JobCondition.Incompletable);
                            return;
                        }

                        bool babyFull = AnimalLactationUtility.SuckleFromLactatingPawn(baby, mom, 1);
                        if (babyFull)
                        {
                            baby.jobs.EndCurrentJob(JobCondition.Succeeded);
                            return;
                        }

                        var lactDef = AnimalLactationUtility.LactatingHediffDef;
                        if (lactDef == null)
                        {
                            Log.Warning("ZoologyMod: youngSuckle.tickAction: HediffDef 'Zoology_Lactating' not found.");
                            return;
                        }
                        if (!mom.health.hediffSet.HasHediff(lactDef))
                        {
                            baby.jobs.EndCurrentJob(JobCondition.Incompletable);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ZoologyMod: Exception in youngSuckle.tickAction: " + ex);
                    }
                };

                suckle.defaultCompleteMode = ToilCompleteMode.Never;
                suckle.WithProgressBar(MomInd, () =>
                {
                    Pawn baby = this.pawn;
                    if (baby?.needs?.food == null) return 0f;
                    return baby.needs.food.CurLevelPercentage;
                });
                suckle.AddFinishAction(() =>
                {
                    try
                    {
                        LactationRequestUtility.ClearRequest(this.pawn);
                    }
                    catch
                    {
                    }
                });

                yield return suckle;
                yield break;
            }

            Toil wait = Toils_General.Wait(suckleDurationTicks, TargetIndex.None);
            wait.tickAction = () =>
            {
                var pawn = this.pawn;
                if (pawn == null || pawn.Dead)
                {
                    pawn?.jobs?.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (pawn.Map == null)
                {
                    pawn.jobs?.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage >= 0.99f)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }

                Pawn availableMom = AnimalLactationUtility.FindNearestAvailableMother(pawn);
                if (availableMom == null)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }
            };
            wait.defaultCompleteMode = ToilCompleteMode.Delay;
            wait.AddFinishAction(() =>
            {
                try
                {
                    LactationRequestUtility.ClearRequest(this.pawn);
                }
                catch
                {
                }
            });
            yield return wait;
        }
    }
}
