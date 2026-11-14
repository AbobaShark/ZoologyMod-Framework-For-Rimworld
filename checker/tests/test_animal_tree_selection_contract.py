"""Tree/nest/perched-pawn selection must be bidirectional and UI-only."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
BEHAVIOUR = ROOT / "Zoology/Source/Behaviour"
TREE_SELECTION = BEHAVIOUR / "Flee/AnimalTreeSelection.cs"
TREE_INDEX = BEHAVIOUR / "Flee/AnimalTreeCanopy.cs"
REFUGES = BEHAVIOUR / "Flee/AnimalRefuges.cs"
STRATEGY = BEHAVIOUR / "Predation/AnimalStrategies.cs"
NURSERY = BEHAVIOUR / "Childcare/AnimalNursery.cs"


class TreeSelectionContracts(unittest.TestCase):
    def test_uses_native_select_gizmos_not_an_extra_simulation_comp(self):
        selection = TREE_SELECTION.read_text(encoding="utf-8")
        self.assertIn("ContainingSelectionUtility.CreateSelectStorageGizmo(", selection)
        self.assertIn('"CommandSelectContainerThing".Translate(tree)', selection)
        self.assertIn('"CommandSelectContainedPawn"', selection)
        self.assertIn('"CommandSelectContainedThing"', selection)
        self.assertIn("groupable: false", selection)
        self.assertNotIn("ThingOwner", selection)
        self.assertNotIn("ComponentTick", selection)
        self.assertNotIn("CompTick", selection)
        # The only new Harmony integration is the cold UI gizmo getter.
        self.assertIn("[HarmonyPatch(typeof(Plant), nameof(Plant.GetGizmos))]",
                      selection)
        self.assertNotIn("Thing.SpawnSetup", selection)
        self.assertNotIn("Pawn.Tick", selection)

    def test_pawn_to_tree_only_while_actually_perched(self):
        selection = TREE_SELECTION.read_text(encoding="utf-8")
        strategy = STRATEGY.read_text(encoding="utf-8")
        self.assertIn("animal.jobs?.curDriver is not JobDriver_AnimalTreeRefuge driver",
                      selection)
        self.assertIn("!driver.AtHeight", selection)
        self.assertIn("animal.CurJob?.targetA.Thing as Plant", selection)
        self.assertIn("tree?.Spawned == true && tree.Map == animal.Map", selection)
        self.assertIn("public override IEnumerable<Gizmo> CompGetGizmosExtra()",
                      strategy)
        self.assertIn("AnimalTreeSelection.OccupiedTree(parent as Pawn)", strategy)
        self.assertIn("AnimalTreeSelection.SelectTree(tree)", strategy)

    def test_nest_to_tree_uses_existing_anchor_and_dies_with_anchor(self):
        source = NURSERY.read_text(encoding="utf-8")
        self.assertIn("if (TreeNest && anchorTree?.Spawned == true", source)
        self.assertIn("anchorTree.Map == Map", source)
        self.assertIn("AnimalTreeSelection.SelectTree(anchorTree)", source)
        self.assertIn("if (TreeNest && (anchorTree == null || !anchorTree.Spawned))",
                      source)
        self.assertIn("Scribe_References.Look(ref anchorTree", source)

    def test_tree_lists_only_present_nests_and_animals_at_height(self):
        index = TREE_INDEX.read_text(encoding="utf-8")
        self.assertIn("internal IEnumerable<Thing> SelectableTreeOccupants(Plant tree)",
                      index)
        start = index.index("internal IEnumerable<Thing> SelectableTreeOccupants(")
        end = index.index("internal bool TreeHasRoom(", start)
        method = index[start:end]
        self.assertIn("GetCurrentTreeClaims(tree)", method)
        self.assertIn("nest.Spawned && nest.Map == map", method)
        self.assertIn("nest.TreeNest && nest.AnchorTree == tree", method)
        self.assertIn("driver.AtHeight", method)
        self.assertIn("occupant.CurJob?.targetA.Thing == tree", method)
        self.assertIn("yield return nest;", method)
        self.assertIn("yield return occupant;", method)
        self.assertNotIn("Find.Maps", method)
        self.assertNotIn("AllPawnsSpawned", method)
        self.assertIn("index.SelectableTreeOccupants(tree)", TREE_SELECTION.read_text())

    def test_tree_patch_does_nothing_for_unselected_trees(self):
        selection = TREE_SELECTION.read_text(encoding="utf-8")
        self.assertIn("Find.Selector.SingleSelectedThing != __instance", selection)
        self.assertIn("__instance.def.plant?.IsTree != true", selection)
        self.assertIn("__instance?.Spawned != true", selection)
        self.assertIn("if (index != null)", selection)
        self.assertIn("if (occupant?.Spawned != true", selection)
        # Claims are maintained by the existing jobs, not a new ticker.
        refuges = REFUGES.read_text(encoding="utf-8")
        self.assertIn("public bool AtHeight;", refuges)
        self.assertIn("ReleaseTreeClaim(TargetA.Thing as Plant, pawn)", refuges)
        self.assertIn("AtHeight = false;", refuges)


if __name__ == "__main__":
    unittest.main()
