using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Selection is a UI-only operation. These are the vanilla container-style
    // select commands, not a fabricated ThingOwner relationship: neither the
    // tree nor its occupants are despawned or transferred into containers.
    internal static class AnimalTreeSelection
    {
        internal static Plant OccupiedTree(Pawn animal)
        {
            if (animal?.Spawned != true || animal.Dead || animal.Destroyed
                || animal.jobs?.curDriver is not JobDriver_AnimalTreeRefuge driver
                || !driver.AtHeight)
                return null;

            Plant tree = animal.CurJob?.targetA.Thing as Plant;
            return tree?.Spawned == true && tree.Map == animal.Map
                ? tree : null;
        }

        internal static Gizmo SelectTree(Plant tree)
        {
            if (tree?.Spawned != true || !tree.def.selectable)
                return null;

            return ContainingSelectionUtility.CreateSelectStorageGizmo(
                "CommandSelectContainerThing".Translate(tree),
                "CommandSelectContainerThingDesc".Translate(),
                tree, tree, groupable: false);
        }

        internal static Gizmo SelectOccupant(Thing occupant)
        {
            if (occupant?.Spawned != true || !occupant.def.selectable)
                return null;

            bool pawn = occupant is Pawn;
            return ContainingSelectionUtility.CreateSelectStorageGizmo(
                (pawn ? "CommandSelectContainedPawn" : "CommandSelectContainedThing")
                    .Translate(occupant),
                (pawn ? "CommandSelectContainedPawnDesc" : "CommandSelectContainedThingDesc")
                    .Translate(),
                occupant, occupant, groupable: false);
        }
    }

    // Plant.GetGizmos runs only to build the selected tree's UI. Unlike a
    // ThingComp added to every tree or a pawn-tick Harmony patch, this has no
    // work in the simulation hot path and allocates nothing for other plants.
    [HarmonyPatch(typeof(Plant), nameof(Plant.GetGizmos))]
    internal static class Patch_Plant_AnimalTreeSelection
    {
        private static void Postfix(Plant __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.Spawned != true || __instance.def.plant?.IsTree != true
                || Find.Selector.SingleSelectedThing != __instance)
                return;

            AnimalRefugeMapComponent index =
                __instance.Map?.GetComponent<AnimalRefugeMapComponent>();
            if (index != null)
                __result = AppendTreeSelection(__result, index, __instance);
        }

        private static IEnumerable<Gizmo> AppendTreeSelection(
            IEnumerable<Gizmo> original, AnimalRefugeMapComponent index, Plant tree)
        {
            if (original != null)
                foreach (Gizmo gizmo in original)
                    yield return gizmo;

            // Revalidate every time the menu is drawn: abandoned perches and
            // destroyed nests immediately disappear from the choices.
            foreach (Thing occupant in index.SelectableTreeOccupants(tree))
            {
                Gizmo select = AnimalTreeSelection.SelectOccupant(occupant);
                if (select != null)
                    yield return select;
            }
        }
    }
}
