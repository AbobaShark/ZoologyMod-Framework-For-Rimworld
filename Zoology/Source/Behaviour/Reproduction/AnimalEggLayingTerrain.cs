using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class AnimalEggLayingTerrain
    {
        private static JobDef jobDef;
        internal static JobDef JobDef => jobDef ??= DefDatabase<JobDef>.GetNamed("Zoology_LayEggOnTerrain");

        internal static bool Aquatic(Pawn pawn) =>
            AnimalOffspringShelter.For(pawn?.def) == OffspringShelterMode.AQUATIC_CLUTCH;

        internal static bool CellSuitable(Pawn pawn, IntVec3 cell, bool aquatic)
        {
            Map map = pawn?.Map;
            return map != null && cell.InBounds(map) && cell.Standable(map)
                && cell.GetEdifice(map) == null && !cell.IsForbidden(pawn)
                && cell.GetTerrain(map).IsWater == aquatic;
        }

        internal static bool TryDryCell(Pawn pawn, out IntVec3 cell)
        {
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(pawn.Position, 14f, true))
                if (CellSuitable(pawn, candidate, false)
                    && pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Some))
                {
                    cell = candidate;
                    return true;
                }
            cell = IntVec3.Invalid;
            return false;
        }
    }

    // Preserve vanilla egg boxes and Zoology nursery jobs. For open-ground
    // laying only, validate the substrate and use an exact placement job.
    [HarmonyPatch(typeof(JobGiver_LayEgg), "TryGiveJob")]
    internal static class Patch_LayEgg_RespectTerrain
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Pawn pawn, ref Job __result)
        {
            if (!ModConstants.AdvancedReproduction || __result?.def != JobDefOf.LayEgg
                || pawn?.Map == null || AnimalEggLayingTerrain.Aquatic(pawn))
                return;
            // A real dry container keeps vanilla behavior; a ground nest
            // or egg box placed on flooded terrain must not attract dry eggs.
            if (__result.targetA.HasThing
                && !__result.targetA.Cell.GetTerrain(pawn.Map).IsWater)
                return;
            IntVec3 cell = __result.targetA.Cell;
            if (!AnimalEggLayingTerrain.CellSuitable(pawn, cell, false)
                && !AnimalEggLayingTerrain.TryDryCell(pawn, out cell))
            {
                __result = null;
                return;
            }
            __result = JobMaker.MakeJob(AnimalEggLayingTerrain.JobDef, cell);
        }
    }

    public sealed class JobDriver_LayEggOnTerrain : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Remember the selected substrate through walking and waiting.
            // A setting or terrain change invalidates the job before production.
            bool aquatic = TargetA.Cell.InBounds(pawn.Map)
                && TargetA.Cell.GetTerrain(pawn.Map).IsWater;
            this.FailOn(() => !ModConstants.AdvancedReproduction
                || AnimalEggLayingTerrain.Aquatic(pawn) != aquatic
                || !AnimalEggLayingTerrain.CellSuitable(pawn, TargetA.Cell, aquatic));
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(500);
            yield return Toils_General.Do(() =>
            {
                CompEggLayer layer = pawn.TryGetComp<CompEggLayer>();
                if (layer?.CanLayNow != true || AnimalEggLayingTerrain.Aquatic(pawn) != aquatic
                    || !AnimalEggLayingTerrain.CellSuitable(pawn, TargetA.Cell, aquatic))
                    return;
                Thing egg = layer.ProduceEgg();
                if (egg == null) return;
                // Never let near-placement choose a cell of the other terrain.
                void OnPlaced(Thing placed, int count)
                {
                    if (pawn.Faction != Faction.OfPlayer) placed.SetForbidden(true);
                    if (ChildcareDefenseUtility.IsEggProtectionEnabled
                        && placed.TryGetComp<CompAmphibianRoe>() == null)
                        EggClutchDefenseGameComponent.Instance?.RegisterOwnership(pawn, placed);
                }
                if (!GenPlace.TryPlaceThing(egg, TargetA.Cell, pawn.Map, ThingPlaceMode.Direct, OnPlaced)
                    && !GenPlace.TryPlaceThing(egg, TargetA.Cell, pawn.Map, ThingPlaceMode.Near, OnPlaced,
                        cell => AnimalEggLayingTerrain.CellSuitable(pawn, cell, aquatic)))
                {
                    Thing placed = GenSpawn.Spawn(egg, TargetA.Cell, pawn.Map);
                    if (placed != null) OnPlaced(placed, placed.stackCount);
                }
            });
        }
    }
}
