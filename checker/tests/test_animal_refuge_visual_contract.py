"""Static contracts for natural animal refuges' damage and selection UI."""
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
DEFS = ROOT / "Zoology/Core/Defs/ThingDefs"
BEHAVIOUR = ROOT / "Zoology/Source/Behaviour"


class AnimalRefugeVisualContracts(unittest.TestCase):
    def test_shared_burrow_parent_disables_building_crack_overlays(self):
        xml = ET.parse(DEFS / "AnimalBurrow.xml").getroot()
        base = xml.find("./ThingDef[@Name='Zoology_SharedBurrowBase']")
        self.assertIsNotNone(base)
        self.assertEqual(base.get("Abstract"), "True")
        self.assertEqual(base.findtext("useHitPoints"), "true")
        self.assertEqual(base.findtext("drawDamagedOverlay"), "false")
        actual = [node for node in xml.findall("./ThingDef")
                  if node.get("Abstract") != "True"]
        self.assertEqual(len(actual), 6)
        for node in actual:
            with self.subTest(defName=node.findtext("defName")):
                self.assertEqual(
                    node.get("ParentName"), "Zoology_SharedBurrowBase")
                self.assertIsNone(node.find("drawDamagedOverlay"))

    def test_both_nest_defs_hide_cracks_but_keep_hit_points(self):
        xml = ET.parse(DEFS / "AnimalNursery.xml").getroot()
        nests = [node for node in xml.findall("./ThingDef")]
        self.assertEqual({
            node.findtext("defName") for node in nests
        }, {"Zoology_AnimalTreeNest", "Zoology_AnimalGroundNest"})
        for node in nests:
            with self.subTest(defName=node.findtext("defName")):
                self.assertEqual(node.findtext("useHitPoints"), "true")
                self.assertEqual(node.findtext("drawDamagedOverlay"), "false")

    def test_no_disabled_reverse_deconstruct_gizmo_on_nests(self):
        xml = ET.parse(DEFS / "AnimalNursery.xml").getroot()
        for node in xml.findall("./ThingDef"):
            with self.subTest(defName=node.findtext("defName")):
                building = node.find("building")
                self.assertIsNotNone(building)
                self.assertEqual(building.findtext("claimable"), "false")
                # RimWorld classifies deconstructible=false artificial
                # buildings as attack-to-remove objects and adds a disabled
                # reverse deconstruction command. Let the instance reject
                # deconstruction without generating the unwanted gizmo.
                self.assertIsNone(building.find("deconstructible"))
        nursery = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(
            encoding="utf-8")
        self.assertIn(
            "public override AcceptanceReport DeconstructibleBy(Faction fac) => false;",
            nursery)
        burrow = (BEHAVIOUR / "Flee/AnimalBurrow.cs").read_text(
            encoding="utf-8")
        self.assertIn(
            "public override AcceptanceReport DeconstructibleBy(Faction fac) => false;",
            burrow)

    def test_normal_decay_and_destruction_preserved(self):
        burrow = (BEHAVIOUR / "Flee/AnimalBurrow.cs").read_text(
            encoding="utf-8")
        nest = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(
            encoding="utf-8")
        self.assertIn("else HitPoints--;", burrow)
        self.assertIn("else Destroy();", nest)
        self.assertIn("EjectEggContents();", nest)
        self.assertIn("EjectContents();", burrow)


if __name__ == "__main__":
    unittest.main()
