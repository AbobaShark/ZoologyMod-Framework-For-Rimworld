// JobDriver_ProtectPrey.cs

using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace ZoologyMod
{
    public class JobDriver_ProtectPrey : JobDriver
    {
        // TargetIndex.A: цель атаки (тот, кто поднял/начал есть добычу)
        // TargetIndex.B: corpse (добыча), может быть null

        private static int MAX_DISTANCE_FROM_PREY => (ZoologyModSettings.Instance != null && ZoologyModSettings.Instance.EnablePredatorDefendCorpse) ? ZoologyModSettings.Instance.PreyProtectionRange : 20;
        private static readonly float MAX_DISTANCE_SQ = MAX_DISTANCE_FROM_PREY * MAX_DISTANCE_FROM_PREY;

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
            // permissive: не резервируем добычу/цель — ванильная логика атаки справится
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // закэшируем pawn для замыканий (уменьшает количество обращений к this.pawn)
            Pawn actorPawn = this.pawn;

            // Finish action — обновить кэш атакующих целей (как у PredatorHunt)
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

            // init toil: set forbidden flags & notify player
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
                            // если игрок — оставляем доступным, иначе запрещаем
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

            // Attack-toil: follow and melee attack the target (с периодическими проверками)
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
                    // Не критично, но логируем, чтобы выявлять редкие ошибки в атакующих функциях
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
                        // если труп полностью отсутствует -> фэйл
                        if (corp == null) return true;

                        // если труп не спавнен, но его несёт цель -> разрешаем (не фэлим)
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
                            // если не несёт — считаем недоступным и завершаем
                            return true;
                        }

                        // в остальных случаях (corp != null и либо spawned == true) — не завершаем
                        return false;
                    }
                    catch
                    {
                        // в случае ошибок в проверке — не фэлим, чтобы не прерывать работу из-за багов
                        return false;
                    }
                });

            // Добавим периодическую проверку (как раньше), но учитывающую несение трупа:
            attackToil.AddPreTickIntervalAction(delegate (int _)
            {
                try
                {
                    Corpse corpse = this.PreyCorpse;
                    Pawn targ = this.TargetPawn;

                    // если труп исчез окончательно -> finish
                    if (corpse == null)
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    // если труп не спавнен и не находится у цели -> finish
                    if (!corpse.Spawned)
                    {
                        bool carriedByTarget = false;
                        if (targ != null)
                        {
                            try
                            {
                                var ct = targ.carryTracker;
                                if (ct != null && ct.CarriedThing == corpse) carriedByTarget = true;
                                else
                                {
                                    var inv = targ.inventory?.innerContainer;
                                    if (inv != null && inv.Contains(corpse)) carriedByTarget = true;
                                }
                            }
                            catch { /* ignore */ }
                        }
                        if (!carriedByTarget)
                        {
                            actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                            return;
                        }
                    }

                    // если цель умерла/пропала -> finish
                    if (targ == null || !targ.Spawned || targ.Dead)
                    {
                        actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                        return;
                    }

                    // если цель отошла слишком далеко от добычи -> finish
                    if (corpse.Spawned)
                    {
                        // используем сравнение по квадрату для скорости
                        if (!targ.Position.InHorDistOf(corpse.Position, MAX_DISTANCE_FROM_PREY))
                        {
                            actorPawn?.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                            return;
                        }
                    }
                    else
                    {
                        // если не спавн — проверяем расстояние до цели (чтобы хищник тоже не бегал бесконечно)
                        if (actorPawn != null && targ != null &&
                            actorPawn.Position.DistanceToSquared(targ.Position) > MAX_DISTANCE_SQ)
                        {
                            actorPawn.jobs?.EndCurrentJob(JobCondition.Succeeded, true, true);
                            return;
                        }
                    }

                    // обновляем кэш атакующих целей
                    if (actorPawn?.Map != null) actorPawn.Map.attackTargetsCache.UpdateTarget(actorPawn);
                }
                catch (Exception ex)
                {
                    // Логируем неожиданные исключения в проверке: они редки, но помогают отладке
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

                // --- новый кусок: если для этого трупа уже было агрегированное уведомление, 
                // то не шлём своё письмо (помечаем notifiedPlayer = true)
                try
                {
                    if (PredatorPreyPairGameComponent.IsProtectionNotificationSuppressedForCorpse(corpse.thingIDNumber))
                    {
                        this.notifiedPlayer = true;
                        return;
                    }
                }
                catch { /* ignore suppression errors */ }
                // --- конец нового куска

                // only warn if target is player pawn/animal, similar to PredatorHunt logic
                if (!targ.Spawned) return;
                if (targ.Faction != null && targ.Faction == Faction.OfPlayer)
                {
                    // localization keys (add translations in your mod if needed)
                    string label = "LetterLabelPredatorProtectingPrey".Translate(this.pawn.LabelShort, targ.LabelDefinite(), this.pawn.Named("PREDATOR"), targ.Named("PREY"));
                    string text = "LetterPredatorProtectingPrey".Translate(this.pawn.LabelIndefinite(), targ.LabelDefinite(), this.pawn.Named("PREDATOR"), targ.Named("PREY"));
                    // Fallback if translation missing
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