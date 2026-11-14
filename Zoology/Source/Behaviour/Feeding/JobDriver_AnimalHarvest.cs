using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Animal collection of edible crops, berries and agave. Uses vanilla plant
    // yield and PlantCollected; only work speed and yield skill effects are
    // evaluated at the fixed Plants-5 competence.
    public sealed class JobDriver_AnimalHarvest : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => PredatorFoodChoiceUtility.FoodChoiceEnabled
                && pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            Plant plant = TargetA.Thing as Plant;
            if (plant == null)
                yield break;
            this.FailOn(() => !PredatorFoodChoiceUtility.FoodChoiceEnabled
                || !plant.HarvestableNow || !plant.CanYieldNow()
                || plant.IsForbidden(pawn));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            float workRate = PredatorFoodChoiceUtility.HarvestWorkRate(pawn, plant);
            if (workRate <= 0f)
                yield break;
            int ticks = Mathf.Max(1, Mathf.CeilToInt(plant.def.plant.harvestWork / workRate));
            Toil work = Toils_General.WaitWith(TargetIndex.A, ticks,
                useProgressBar: true, maintainPosture: true);
            work.WithEffect(plant.def.plant.IsTree
                ? EffecterDefOf.Harvest_Tree : EffecterDefOf.Harvest_Plant,
                TargetIndex.A);
            yield return work;

            yield return Toils_General.Do(() =>
            {
                if (!plant.Spawned || !plant.CanYieldNow()
                    || !PredatorFoodChoiceUtility.CanHarvest(pawn))
                    return;
                ThingDef product = plant.def.plant.harvestedThingDef;
                if (product == null || !pawn.WillEat(product, pawn, true, false))
                    return;

                float harvestYield = PredatorFoodChoiceUtility.AnimalStatAtPlantsFive(
                    pawn, StatDefOf.PlantHarvestYield);
                bool failed = plant.def.plant.harvestFailable
                    && Rand.Value > Mathf.Clamp01(harvestYield);
                if (!failed)
                {
                    int count = plant.YieldNow();
                    if (harvestYield > 1f)
                        count = GenMath.RoundRandom(count * harvestYield);
                    if (count > 0)
                    {
                        Thing produce = ThingMaker.MakeThing(product);
                        produce.stackCount = count;
                        if (pawn.Faction != Faction.OfPlayer)
                            produce.SetForbidden(true);
                        GenPlace.TryPlaceThing(produce, pawn.Position, pawn.Map,
                            ThingPlaceMode.Near);
                    }
                }
                plant.PlantCollected(pawn, PlantDestructionMode.Chop);
            });
        }
    }
}
