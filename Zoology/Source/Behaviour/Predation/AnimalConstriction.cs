using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(HuntJobUtility), nameof(HuntJobUtility.WasKilledByHunter))]
    internal static class Patch_ConstrictionHuntingKill
    {
        static void Postfix(Pawn __0, DamageInfo? __1, ref bool __result)
        {
            if (!__result && __1?.Instigator is Pawn hunter
                && hunter.jobs?.curDriver is JobDriver_Constrict
                && hunter.CurJob.count == 1
                && hunter.TryGetComp<CompAnimalStrategyState>()?.constrictedPrey == __0)
                __result = true;
        }
    }
    public class Hediff_AnimalConstriction : HediffWithComps, IDelayedPredationSource
    {
        public Pawn holder;
        public int sourceTick;
        public Pawn Hunter => Active ? holder : null;
        public int SourceTick => sourceTick;
        public bool Active => holder?.Spawned == true && pawn.Spawned && holder.Map == pawn.Map && !holder.Dead
            && holder.Position == pawn.Position
            && (holder.Downed ? holder.TryGetComp<CompAnimalStrategyState>()?.constrictedPrey == pawn
                : holder.CurJob?.targetA.Thing == pawn && holder.jobs.curDriver is JobDriver_Constrict);
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
        public static void Release(Pawn holder, bool endJob = true)
        {
            var state = holder?.TryGetComp<CompAnimalStrategyState>();
            Pawn prey = state?.constrictedPrey;
            if (state != null) state.constrictedPrey = null;
            if (prey?.health?.hediffSet != null)
            {
                var effect = prey.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>()
                    .FirstOrDefault(h => h.holder == holder);
                if (effect != null) prey.health.RemoveHediff(effect);
            }
            if (endJob && holder?.jobs?.curDriver is JobDriver_Constrict)
                holder.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
        public static bool Applicable(Pawn snake, Pawn prey)
        {
            var ext = DefModExtensionCache<ModExtension_Constrictor>.Get(snake.def);
            return ext != null && prey.Spawned && snake.Map == prey.Map && !prey.Dead && prey.RaceProps.IsFlesh
                && prey.BodySize <= snake.BodySize * ext.maxPreySizeRatio
                && prey.RaceProps.body.AllParts.Any(p => p.def.defName == "Heart");
        }
    }
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.PostApplyDamage))]
    internal static class Patch_AnimalCombatEvents
    {
        static void Postfix(Pawn ___pawn, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt <= 0f) return;
            if (___pawn.TryGetComp<CompAnimalStrategyState>()?.constrictedPrey != null
                && Rand.Chance(Mathf.Clamp01(totalDamageDealt / (10f * ___pawn.HealthScale))))
                AnimalConstriction.Release(___pawn);
            if (___pawn.Dead) return;
            var damagedAmbusher = ___pawn.TryGetComp<CompAnimalStrategyState>();
            if (damagedAmbusher?.ambushPrey != null) damagedAmbusher.revealed = true;
            if (___pawn.jobs?.curDriver is JobDriver_AnimalTreeRefuge tree && tree.AtHeight)
                tree.Notify_DamageTaken(dinfo);
            if (!(dinfo.Instigator is Pawn snake)) return;
            var state = snake.TryGetComp<CompAnimalStrategyState>();
            if (state?.ambushPrey == ___pawn) state.revealed = true;
            if (DefModExtensionCache<ModExtension_Constrictor>.Get(snake.def) == null) return;
            bool hunting = AnimalHuntStrategy.IsHunt(snake, ___pawn);
            if (dinfo.Tool == null || snake.Downed || snake.jobs == null || snake.jobs.curDriver is JobDriver_Constrict
                || state?.constrictedPrey != null
                || ___pawn.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>().Any()
                || !snake.Position.AdjacentTo8WayOrInside(___pawn.Position) || !AnimalConstriction.Applicable(snake, ___pawn)) return;
            float chance = snake.BodySize / (snake.BodySize + ___pawn.BodySize) * (hunting ? 1f : 0.1f);
            if (Rand.Chance(chance))
            {
                Job hold = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_Constrict"), ___pawn);
                hold.count = hunting ? 1 : 0;
                snake.jobs.StartJob(hold, JobCondition.InterruptForced);
            }
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
            this.FailOn(() => pawn.InMentalState
                || TargetA.Pawn.Dead || (effect != null && pawn.Position != TargetA.Cell)
                || !AnimalConstriction.Applicable(pawn, TargetA.Pawn));
            AddFinishAction(_ =>
            {
                if (!pawn.Downed) AnimalConstriction.Release(pawn, false);
            });
            Toil hold = ToilMaker.MakeToil();
            hold.initAction = () =>
            {
                pawn.pather.StopDead(); TargetA.Pawn.pather.StopDead();
                var state = pawn.TryGetComp<CompAnimalStrategyState>();
                if (state == null || (state.constrictedPrey != null && state.constrictedPrey != TargetA.Pawn)
                    || TargetA.Pawn.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>().Any(h => h.holder != pawn))
                { EndJobWith(JobCondition.Incompletable); return; }
                if (!TargetA.Cell.InBounds(pawn.Map) || !TargetA.Cell.Standable(pawn.Map)
                    || !pawn.Position.AdjacentTo8WayOrInside(TargetA.Cell))
                { EndJobWith(JobCondition.Incompletable); return; }
                IntVec3 oldPosition = pawn.Position;
                try
                {
                    pawn.Position = TargetA.Cell;
                    pawn.Notify_Teleported(false);
                    if (!pawn.Map.thingGrid.ThingsListAt(TargetA.Cell).Contains(pawn))
                        throw new System.InvalidOperationException("Constriction holder was not registered at the prey cell");
                }
                catch (System.Exception ex)
                {
                    pawn.Position = oldPosition; pawn.Notify_Teleported(false);
                    Log.Warning("[Zoology] Could not place constrictor: " + ex.Message);
                    EndJobWith(JobCondition.Incompletable); return;
                }
                if (effect == null)
                {
                    effect = TargetA.Pawn.health.hediffSet.hediffs.OfType<Hediff_AnimalConstriction>()
                        .FirstOrDefault(h => h.holder == pawn);
                    if (effect == null)
                    {
                        effect = (Hediff_AnimalConstriction)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Zoology_Constriction"), TargetA.Pawn);
                        effect.holder = pawn; effect.sourceTick = GenTicks.TicksGame;
                        TargetA.Pawn.health.AddHediff(effect);
                    }
                }
                state.constrictedPrey = TargetA.Pawn;
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
    }
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeUndowned")]
    internal static class Patch_ResumeConstrictAfterDowned
    {
        static void Postfix(Pawn ___pawn)
        {
            Pawn prey = ___pawn.TryGetComp<CompAnimalStrategyState>()?.constrictedPrey;
            if (prey == null) return;
            if (___pawn.Spawned && prey.Spawned && ___pawn.Map == prey.Map && ___pawn.Position == prey.Position
                && AnimalConstriction.Applicable(___pawn, prey))
                ___pawn.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_Constrict"), prey), JobCondition.InterruptForced);
            else AnimalConstriction.Release(___pawn);
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    internal static class Patch_ReleaseConstrictionOnDespawn
    {
        static void Prefix(Pawn __instance) => AnimalConstriction.Release(__instance);
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class Patch_ReleaseConstrictionOnDeath
    {
        static void Prefix(Pawn __instance) => AnimalConstriction.Release(__instance);
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
