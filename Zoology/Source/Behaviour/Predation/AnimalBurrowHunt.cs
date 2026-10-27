using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class AnimalBurrowHunt
    {
        [ThreadStatic] internal static Pawn EvaluatingOccupant;
        internal static bool Eligible(Pawn predator, Building_AnimalBurrow burrow)
            => FindPrey(predator, burrow) != null;
        internal static Pawn FindPrey(Pawn predator, Building_AnimalBurrow burrow)
            => burrow?.Occupants.FirstOrDefault(p => CanHuntOccupant(predator, burrow, p));
        internal static bool CanHuntOccupant(Pawn predator, Building_AnimalBurrow burrow, Pawn prey)
        {
            if (predator?.Spawned != true || !predator.RaceProps.predator || predator.Downed
                || !DefModExtensionCache<ModExtension_Burrower>.Has(predator)
                || burrow?.Spawned != true || burrow.Map != predator.Map || burrow.IsForbidden(predator)
                || prey == null || !burrow.Contains(prey) || prey.Dead || prey == predator
                || burrow.ProtectedByMotherFrom(predator, prey)) return false;
            if (predator.Faction != null && (prey.Faction != null || prey.HostFaction != null) && !predator.HostileTo(prey)) return false;
            // Reuse the complete ordinary prey rules (faction, species, size, combat power,
            // guarded young and blocked pursuits), without spawning or exposing the occupant.
            Pawn previous = EvaluatingOccupant;
            try { EvaluatingOccupant = prey; return FoodUtility.IsAcceptablePreyFor(predator, prey); }
            finally { EvaluatingOccupant = previous; }
        }
    }

    public class JobGiver_ExcavateAnimalBurrow : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn?.Spawned != true || !pawn.RaceProps.predator || pawn.Downed || pawn.InMentalState
                || !DefModExtensionCache<ModExtension_Burrower>.Has(pawn) || pawn.needs?.food?.CurLevelPercentage >= 0.3f
                || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)
                || PredatorPreyPairGameComponent.Instance?.GetPairedCorpse(pawn) != null) return null;
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            var index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (state == null || state.nextBurrowHuntPlan > GenTicks.TicksGame || !index.TryPlan()) return null;
            state.nextBurrowHuntPlan = GenTicks.TicksGame + 600;
            foreach (Building_AnimalBurrow burrow in index.Nearby(pawn.Position, (int)Building_AnimalBurrow.Settings.searchRadius)
                .OfType<Building_AnimalBurrow>().Where(b => AnimalBurrowHunt.Eligible(pawn, b))
                .OrderBy(b => (b.Position - pawn.Position).LengthHorizontalSquared).Take(ZoologyTickLimiter.Refuges.MaxPathCandidates))
                if (pawn.CanReserveAndReach(burrow, PathEndMode.Touch, Danger.Some))
                    return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_ExcavateBurrow"), burrow, AnimalBurrowHunt.FindPrey(pawn, burrow));
            return null;
        }
    }

    public class JobDriver_ExcavateAnimalBurrow : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Downed || pawn.InMentalState || pawn.stances.stunner.Stunned
                || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)
                || !(TargetA.Thing is Building_AnimalBurrow burrow) || !burrow.Contains(TargetB.Pawn)
                || !AnimalBurrowHunt.CanHuntOccupant(pawn, burrow, TargetB.Pawn));
            yield return Toils_General.Do(() => ((Building_AnimalBurrow)TargetA.Thing).SetThreat(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(((Building_AnimalBurrow)TargetA.Thing).OpenTicks, TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                Pawn prey = TargetB.Pawn;
                ((Building_AnimalBurrow)TargetA.Thing).Excavate(pawn);
                if (prey?.Spawned == true && !prey.Dead && FoodUtility.IsAcceptablePreyFor(pawn, prey))
                    pawn.jobs.jobQueue.EnqueueFirst(JobMaker.MakeJob(JobDefOf.PredatorHunt, prey));
            });
        }
        public override void Notify_DamageTaken(DamageInfo dinfo)
        {
            if (dinfo.Amount > 0f) EndJobWith(JobCondition.InterruptForced);
        }
    }
}
