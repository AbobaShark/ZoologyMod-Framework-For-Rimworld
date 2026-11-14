using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Uses the vanilla forage-site finder and 240-tick action, but produces
    // a small wild-animal portion rather than the trained animal's 10-25 items.
    public sealed class JobDriver_WildAnimalForage : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => PredatorFoodChoiceUtility.WildForageEnabled
                && pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // The full eligibility test was performed when issuing the job.
            // Only cheap map/site checks run as an ongoing fail condition.
            this.FailOn(() => !PredatorFoodChoiceUtility.WildForageEnabled
                || pawn.Map == null || pawn.Faction != null
                || !job.targetA.Cell.GetTerrain(pawn.Map).natural);

            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil work = Toils_General.WaitWith(TargetIndex.A,
                PredatorFoodChoiceUtility.WildForageWorkTicks,
                useProgressBar: true, maintainPosture: true);
            work.WithEffect(EffecterDefOf.BuryPawn,
                () => job.GetTarget(TargetIndex.A));
            yield return work;
            yield return Toils_General.Do(() =>
            {
                if (!PredatorFoodChoiceUtility.CanWildForage(pawn))
                    return;
                ThingDef product = pawn.Map.Biome.foragedFood;
                Thing food = ThingMaker.MakeThing(product);
                food.stackCount = Rand.RangeInclusive(
                    PredatorFoodChoiceUtility.WildForageMinStack,
                    PredatorFoodChoiceUtility.WildForageMaxStack);
                GenPlace.TryPlaceThing(food, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            });
        }
    }
}
