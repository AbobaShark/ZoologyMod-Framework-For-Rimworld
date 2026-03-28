using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace ZoologyMod
{
    public class JobDriver_ProtectPrey : JobDriver
    {
        private const int MinimumProtectDurationTicks = ZoologyTickLimiter.PreyProtection.MinimumProtectDurationTicks;
        private static int MAX_DISTANCE_SQ => PreyProtectionUtility.GetProtectionRangeSquared();

        private int startTickLocal = -1;
        private bool notifiedPlayer = false;

        public Pawn TargetPawn => this.job.GetTarget(TargetIndex.A).Thing as Pawn;
        public Corpse PreyCorpse => this.job.GetTarget(TargetIndex.B).Thing as Corpse;
        public Pawn ProtectedPawn => this.job.GetTarget(TargetIndex.B).Thing as Pawn;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startTickLocal, "startTickLocal", -1, false);
            Scribe_Values.Look(ref notifiedPlayer, "notifiedPlayer", false, false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                try
                {
                    if (pawn != null && TargetPawn != null)
                    {
                        ProtectPreyState.NotifyProtectPreyJobStarted(pawn, TargetPawn, this.job);
                    }
                }
                catch { }
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            
            return true;
        }

        private bool IsInMinimumProtectDuration()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            int startedAt = startTickLocal;
            if (startedAt < 0)
            {
                startTickLocal = now;
                return true;
            }

            return now - startedAt < MinimumProtectDurationTicks;
        }

        private bool IsValidPursuitTarget(Pawn target)
        {
            if (target == null)
            {
                return false;
            }

            if (!target.Spawned || target.Destroyed || target.Dead || target.Downed)
            {
                return false;
            }

            if (pawn == null || pawn.Map == null)
            {
                return target.Map != null;
            }

            return target.Map == pawn.Map;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            
            Pawn actorPawn = this.pawn;

            
            base.AddFinishAction(delegate (JobCondition cond)
            {
                try
                {
                    ProtectPreyState.NotifyProtectPreyJobEnded(actorPawn, this.job);
                    if (actorPawn != null && actorPawn.Map != null)
                        actorPawn.Map.attackTargetsCache.UpdateTarget(actorPawn);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: JobDriver_ProtectPrey FinishAction exception: {ex}");
                }
            });

            
            Toil initToil = new Toil();
            initToil.initAction = delegate
            {
                try
                {
                    Pawn actor = initToil.actor;
                    Corpse corpse = this.PreyCorpse;
                    if (corpse != null)
                    {
                        try
                        {
                            
                            if (actor != null && actor.Faction == Faction.OfPlayer)
                                corpse.SetForbidden(false, false);
                            else
                                corpse.SetForbidden(true, false);
                        }
                        catch { /* non-fatal: ignore SetForbidden issues */ }

                        try
                        {
                            actor?.CurJob?.SetTarget(TargetIndex.B, corpse);
                        }
                        catch { /* non-fatal */ }
                    }

                    if (corpse != null)
                    {
                        TryWarnPlayer();
                    }
                    startTickLocal = Find.TickManager?.TicksGame ?? 0;
                    try
                    {
                        ProtectPreyState.NotifyProtectPreyJobStarted(actor, this.TargetPawn, actor?.CurJob);
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Zoology: JobDriver_ProtectPrey initToil exception: {ex}");
                }
            };
            initToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return initToil;

            
            Action hitAction = delegate ()
            {
                try
                {
                    Pawn targ = this.TargetPawn;
                    if (targ == null) return;
                    bool surprise = false;
                    this.pawn.meleeVerbs.TryMeleeAttack(targ, null, surprise);
                }
                catch (Exception ex)
                {
                    
                    Log.Warning($"Zoology: JobDriver_ProtectPrey hitAction exception: {ex}");
                }
            };

            Toil attackToil = Toils_Combat.FollowAndMeleeAttack(TargetIndex.A, hitAction)
                .FailOnDespawnedOrNull(TargetIndex.A)
                .FailOn(() =>
                {
                    try
                    {
                        var corp = this.PreyCorpse;
                        var targ = this.TargetPawn;
                        var protectedPawn = this.ProtectedPawn;
                        
                        if (corp == null && protectedPawn == null) return true;
                        if (IsInMinimumProtectDuration()) return false;

                        if (corp != null)
                        {
                            if (!corp.Spawned && targ != null)
                            {
                                try
                                {
                                    if (PreyProtectionUtility.IsThingHeldByPawn(targ, corp)) return false;
                                }
                                catch { /* ignore inventory anomalies */ }

                                return true;
                            }

                            return false;
                        }

                        if (protectedPawn == null) return true;
                        if (!protectedPawn.Spawned || protectedPawn.Destroyed || protectedPawn.Dead) return true;
                        return false;
                    }
                    catch
                    {
                        
                        return false;
                    }
                });

            
            attackToil.AddPreTickIntervalAction(delegate (int _)
            {
                try
                {
                    Corpse corpse = this.PreyCorpse;
                    Pawn protectedPawn = this.ProtectedPawn;
                    Pawn targ = this.TargetPawn;

                    
                    if (corpse == null && protectedPawn == null)
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (!IsValidPursuitTarget(targ))
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    int currentTick = Find.TickManager?.TicksGame ?? 0;
                    if (currentTick > 0
                        && currentTick % ZoologyTickLimiter.PreyProtection.ProtectPreyMapRefreshIntervalTicks == 0)
                    {
                        ProtectPreyState.NotifyPredatorThreatForFaction(actorPawn, targ, force: false);
                    }

                    if (IsInMinimumProtectDuration())
                    {
                        if (actorPawn?.Map != null) actorPawn.Map.attackTargetsCache.UpdateTarget(actorPawn);
                        return;
                    }

                    Map anchorMap = null;
                    IntVec3 anchorPos = IntVec3.Invalid;
                    if (corpse != null)
                    {
                        if (!PreyProtectionUtility.TryGetProtectionAnchor(corpse, targ, out anchorMap, out anchorPos))
                        {
                            actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                            return;
                        }
                    }
                    else
                    {
                        if (protectedPawn == null || !protectedPawn.Spawned || protectedPawn.Destroyed || protectedPawn.Dead)
                        {
                            actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                            return;
                        }

                        anchorMap = protectedPawn.Map;
                        anchorPos = protectedPawn.Position;
                        if (anchorMap == null || !anchorPos.IsValid)
                        {
                            actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                            return;
                        }
                    }

                    if (!PreyProtectionUtility.IsPawnWithinProtectionRange(targ, anchorMap, anchorPos, MAX_DISTANCE_SQ))
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (!PreyProtectionUtility.IsPawnWithinProtectionRange(actorPawn, anchorMap, anchorPos, MAX_DISTANCE_SQ))
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    
                    if (actorPawn?.Map != null) actorPawn.Map.attackTargetsCache.UpdateTarget(actorPawn);
                }
                catch (Exception ex)
                {
                    
                    Log.Warning($"Zoology: JobDriver_ProtectPrey attackToil Tick exception: {ex}");
                }
            });

            yield return attackToil;

            yield break;
        }

        private void TryWarnPlayer()
        {
            try
            {
                if (this.notifiedPlayer) return;
                Pawn targ = this.TargetPawn;
                Corpse corpse = this.PreyCorpse;
                if (targ == null || corpse == null) return;

                
                
                try
                {
                    if (PredatorPreyPairGameComponent.IsProtectionNotificationSuppressedForCorpse(corpse.thingIDNumber))
                    {
                        this.notifiedPlayer = true;
                        return;
                    }
                }
                catch { /* ignore suppression errors */ }
                

                
                if (!targ.Spawned) return;
                if (targ.Faction != null && targ.Faction == Faction.OfPlayer)
                {
                    
                    string label = "LetterLabelPredatorProtectingPrey".Translate(this.pawn.LabelShort, targ.LabelDefinite(), this.pawn.Named("PREDATOR"), targ.Named("PREY"));
                    string text = "LetterPredatorProtectingPrey".Translate(this.pawn.LabelIndefinite(), targ.LabelDefinite(), this.pawn.Named("PREDATOR"), targ.Named("PREY"));
                    
                    if (label.NullOrEmpty() || label.Contains("LetterLabelPredatorProtectingPrey"))
                        label = $"{this.pawn.LabelShort} is protecting its prey";
                    if (text.NullOrEmpty() || text.Contains("LetterPredatorProtectingPrey"))
                        text = $"{this.pawn.LabelShort} is protecting its prey and is attacking {targ.LabelDefinite()}.";

                    if (targ.RaceProps.Humanlike)
                    {
                        Find.LetterStack.ReceiveLetter(label.CapitalizeFirst(), text.CapitalizeFirst(), LetterDefOf.ThreatBig, this.pawn, null, null, null, null, 0, true);
                    }
                    else
                    {
                        Messages.Message(text.CapitalizeFirst(), this.pawn, MessageTypeDefOf.ThreatBig, true);
                    }

                    this.pawn.mindState.Notify_PredatorHuntingPlayerNotification();
                    this.notifiedPlayer = true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: JobDriver_ProtectPrey TryWarnPlayer exception: {ex}");
            }
        }
    }
}
