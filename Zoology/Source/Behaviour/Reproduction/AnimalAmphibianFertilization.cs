using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // External fertilization of actual spawned underwater clutches.
    // The female's CompEggLayer supplies the original ThingDef mapping.
    // No species list and no scanning of every map Thing.
    public sealed class JobGiver_FertilizeAmphibianRoe : ThinkNode_JobGiver
    {
        private const int SearchRadius = 24;
        private const int SearchInterval = 600;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ModConstants.AdvancedReproduction) return null;
            if (pawn?.Spawned != true || !pawn.IsAnimal || pawn.gender != Gender.Male
                || pawn.Dead || pawn.Downed || pawn.InMentalState || pawn.Sterile()
                || AnimalOffspringShelter.For(pawn.def) != OffspringShelterMode.AQUATIC_CLUTCH
                || pawn.RaceProps?.disableMating == true)
                return null;

            CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null || state.nextRoeFertilizationPlan > GenTicks.TicksGame)
                return null;
            AnimalRefugeMapComponent index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (index == null || !index.TryPlan()) return null;
            state.nextRoeFertilizationPlan = GenTicks.TicksGame + SearchInterval;

            CompProperties_EggLayer eggs = pawn.def.GetCompProperties<CompProperties_EggLayer>();
            ThingDef unfertilizedDef = eggs?.eggUnfertilizedDef;
            if (unfertilizedDef == null) return null;
            List<Thing> clutches = pawn.Map.listerThings.ThingsOfDef(unfertilizedDef);
            Thing nearest = null;
            float bestDistance = SearchRadius * SearchRadius;
            for (int i = 0; i < clutches.Count; i++)
            {
                Thing item = clutches[i];
                float distance = (item.Position - pawn.Position).LengthHorizontalSquared;
                if (distance >= bestDistance || !item.Spawned
                    || item.TryGetComp<CompAmphibianRoe>() is not CompAmphibianRoe roe
                    || !roe.CanBeFertilizedBy(pawn))
                    continue;
                if (!pawn.CanReserveAndReach(item, PathEndMode.Touch, Danger.Some))
                    continue;
                nearest = item;
                bestDistance = distance;
            }
            return nearest == null ? null
                : JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Zoology_FertilizeAmphibianRoe"), nearest);
        }
    }

    public sealed class JobDriver_FertilizeAmphibianRoe : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !ModConstants.AdvancedReproduction || pawn.Downed || pawn.InMentalState
                || TargetA.Thing?.TryGetComp<CompAmphibianRoe>() is not CompAmphibianRoe roe
                || !roe.CanBeFertilizedBy(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil fertilize = Toils_General.Wait(250, TargetIndex.A);
            fertilize.WithProgressBarToilDelay(TargetIndex.A)
                .FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            // Same heart flecks used while fertilizing an ovum in Biotech.
            // Emit only during this toil, not while searching or walking.
            fertilize.tickIntervalAction = delta =>
            {
                if (pawn?.Map != null && pawn.IsHashIntervalTick(100, delta)
                    && !pawn.Position.Fogged(pawn.Map))
                    FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.Heart);
            };
            yield return fertilize;
            yield return Toils_General.Do(() =>
                TargetA.Thing?.TryGetComp<CompAmphibianRoe>()?.Fertilize(pawn));
        }
    }
}
