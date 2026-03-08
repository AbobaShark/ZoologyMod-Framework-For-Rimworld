using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace ZoologyMod
{
    public class JobDriver_ProtectPrey : JobDriver
    {
        
        

        private static int MAX_DISTANCE_SQ => PreyProtectionUtility.GetProtectionRangeSquared();

        private int startTickLocal = -1;
        private bool notifiedPlayer = false;

        public Pawn TargetPawn => this.job.GetTarget(TargetIndex.A).Thing as Pawn;
        public Corpse PreyCorpse => this.job.GetTarget(TargetIndex.B).Thing as Corpse;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startTickLocal, "startTickLocal", -1, false);
            Scribe_Values.Look(ref notifiedPlayer, "notifiedPlayer", false, false);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            
            Pawn actorPawn = this.pawn;

            
            base.AddFinishAction(delegate (JobCondition cond)
            {
                try
                {
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

                    TryWarnPlayer();
                    startTickLocal = Find.TickManager?.TicksGame ?? 0;
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
                        
                        if (corp == null) return true;

                        
                        if (!corp.Spawned && targ != null)
                        {
                            try
                            {
                                var ct = targ.carryTracker;
                                if (ct != null && ct.CarriedThing == corp) return false;
                                var inv = targ.inventory?.innerContainer;
                                if (inv != null && inv.Contains(corp)) return false;
                            }
                            catch { /* ignore inventory anomalies */ }
                            
                            return true;
                        }

                        
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
                    Pawn targ = this.TargetPawn;

                    
                    if (corpse == null)
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (targ == null || !targ.Spawned || targ.Dead)
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    if (!PreyProtectionUtility.TryGetProtectionAnchor(corpse, targ, out Map anchorMap, out IntVec3 anchorPos))
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
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
