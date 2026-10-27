using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class ModExtension_ConstrictibleBody : DefModExtension { }
    public class Hediff_AnimalConstriction : HediffWithComps, IDelayedPredationSource
    {
        public Pawn holder;
        public int sourceTick;
        public Pawn Hunter => Active ? holder : null;
        public int SourceTick => sourceTick;
        public bool Active => holder?.Spawned == true && pawn.Spawned && holder.Map == pawn.Map && !holder.Dead
            && !holder.Downed && !holder.stances.stunner.Stunned && holder.CurJob?.targetA.Thing == pawn
            && holder.jobs.curDriver is JobDriver_Constrict && holder.Position.AdjacentTo8WayOrInside(pawn.Position);
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref holder, "constrictor"); Scribe_Values.Look(ref sourceTick, "constrictionSourceTick");
        }
        public override void PostTick()
        {
            base.PostTick();
            if (!Active) pawn.health.RemoveHediff(this);
        }
    }
    internal static class AnimalConstriction
    {
        public static bool Applicable(Pawn snake, Pawn prey)
        {
            var ext = DefModExtensionCache<ModExtension_Constrictor>.Get(snake.def);
            return ext != null && prey.Spawned && snake.Map == prey.Map && !prey.Dead && prey.RaceProps.IsFlesh
                && prey.BodySize <= snake.BodySize * ext.maxPreySizeRatio
                && prey.RaceProps.body.HasModExtension<ModExtension_ConstrictibleBody>()
                && prey.health.hediffSet.GetNotMissingParts().Any(p => p.def.defName == "Heart"
                    && PawnCapacityUtility.CalculatePartEfficiency(prey.health.hediffSet, p) > 0f);
        }
    }
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    internal static class Patch_AnimalCombatEvents
    {
        static void Postfix(Pawn ___pawn, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt <= 0f || ___pawn.Dead) return;
            var damagedAmbusher = ___pawn.TryGetComp<CompAnimalStrategyState>();
            if (damagedAmbusher?.ambushPrey != null) damagedAmbusher.revealed = true;
            if (___pawn.jobs?.curDriver is JobDriver_AnimalTreeRefuge tree && tree.AtHeight)
                tree.Notify_DamageTaken(dinfo);
            if (!(dinfo.Instigator is Pawn snake)) return;
            var state = snake.TryGetComp<CompAnimalStrategyState>();
            if (state?.ambushPrey == ___pawn) state.revealed = true;
            if (!AnimalHuntStrategy.IsHunt(snake, ___pawn) || snake.jobs.curDriver is JobDriver_Constrict
                || !snake.Position.AdjacentTo8WayOrInside(___pawn.Position) || !AnimalConstriction.Applicable(snake, ___pawn)) return;
            if (Rand.Chance(snake.BodySize / (snake.BodySize + ___pawn.BodySize)))
                snake.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_Constrict"), ___pawn), JobCondition.InterruptForced);
        }
    }
    public class JobDriver_Constrict : JobDriver
    {
        private Hediff_AnimalConstriction effect;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_References.Look(ref effect, "constrictionEffect");
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Downed || pawn.stances.stunner.Stunned || pawn.InMentalState
                || TargetA.Pawn.Dead || !pawn.Position.AdjacentTo8WayOrInside(TargetA.Cell)
                || !AnimalConstriction.Applicable(pawn, TargetA.Pawn));
            AddFinishAction(_ =>
            {
                if (effect != null && !effect.pawn.Dead && effect.pawn.health.hediffSet.hediffs.Contains(effect)) effect.pawn.health.RemoveHediff(effect);
            });
            Toil hold = ToilMaker.MakeToil();
            hold.initAction = () =>
            {
                pawn.pather.StopDead(); TargetA.Pawn.pather.StopDead();
                if (effect == null)
                {
                    effect = (Hediff_AnimalConstriction)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Zoology_Constriction"), TargetA.Pawn);
                    effect.holder = pawn; effect.sourceTick = GenTicks.TicksGame;
                    TargetA.Pawn.health.AddHediff(effect);
                }
            };
            hold.tickAction = () =>
            {
                if (effect == null) { EndJobWith(JobCondition.Incompletable); return; }
                var ext = DefModExtensionCache<ModExtension_Constrictor>.Get(pawn.def);
                float factor = DefModExtensionCache<ModExtension_Ectothermic>.Has(TargetA.Pawn) ? ext.ectothermFactor : 1f;
                effect.Severity += ext.severityPerSecond * factor / 60f;
                if (pawn.IsHashIntervalTick(250) && !TargetA.Pawn.Downed
                    && Rand.Chance(0.1f * Mathf.Clamp01(TargetA.Pawn.BodySize / pawn.BodySize)
                        * TargetA.Pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness)))
                    EndJobWith(JobCondition.InterruptForced);
            };
            hold.defaultCompleteMode = ToilCompleteMode.Never;
            yield return hold;
        }
        public override void Notify_DamageTaken(DamageInfo dinfo)
        {
            if (dinfo.Amount >= 10f * pawn.HealthScale) EndJobWith(JobCondition.InterruptForced);
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    internal static class Patch_ConstrictedMovement
    {
        private static HediffDef constriction;
        static bool Prefix(Pawn ___pawn)
        {
            if (___pawn.health.hediffSet.hediffs.Count == 0) return true;
            HediffDef def = constriction ?? (constriction = DefDatabase<HediffDef>.GetNamedSilentFail("Zoology_Constriction"));
            return def == null || !(___pawn.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_AnimalConstriction effect) || !effect.Active;
        }
    }
}
