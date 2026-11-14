using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Egg boxes retain vanilla behaviour. Only natural bird nests reject
    // unfertilized eggs and have a species-correct developer fill action.
    public sealed class CompAnimalNestEggContainer : CompEggContainer
    {
        internal static bool IsFertilizedEgg(ThingDef eggDef) =>
            eggDef != null
            && eggDef.GetCompProperties<CompProperties_Hatcher>() != null;

        public override bool Accepts(ThingDef thingDef) =>
            IsFertilizedEgg(thingDef) && base.Accepts(thingDef);

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // CompEggContainer's built-in developer command creates
            // unfertilized chicken eggs, even for unrelated bird species.
            // Retain the genuine contained-egg selection gizmo but not that
            // hardcoded debug action.
            for (int i = 0; i < innerContainer.Count; i++)
            {
                Gizmo gizmo = Building.SelectContainedItemGizmo(parent, innerContainer[i]);
                if (gizmo != null) yield return gizmo;
            }

            if (!DebugSettings.ShowDevGizmos || !Empty
                || parent is not Building_AnimalNursery nest)
                yield break;

            Pawn mother = nest.Mother;
            CompEggLayer layer = mother?.TryGetComp<CompEggLayer>();
            ThingDef fertilizedDef = layer?.Props?.eggFertilizedDef;
            if (!Accepts(fertilizedDef)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "DEV: Fill with fertilized eggs",
                action = () =>
                {
                    if (!Empty || !Accepts(fertilizedDef) || !nest.Spawned)
                        return;
                    Thing egg = ThingMaker.MakeThing(fertilizedDef);
                    egg.stackCount = Props.stackLimit;
                    CompHatcher hatcher = egg.TryGetComp<CompHatcher>();
                    if (hatcher != null)
                    {
                        hatcher.hatcheeFaction = mother.Faction;
                        hatcher.hatcheeParent = mother;
                        hatcher.otherParent = nest.Father;
                    }
                    if (!innerContainer.TryAdd(egg))
                        egg.Destroy();
                }
            };
        }
    }
}
