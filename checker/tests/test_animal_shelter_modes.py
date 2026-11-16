"""Data-driven offspring shelter and winter dormancy XML contracts."""
import sys
import unittest
from pathlib import Path

import pandas as pd
from lxml import etree as LET

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from rimworld_xml_generator import (
    build_mod_extensions_for_child, build_mod_extensions_for_parent,
    _merge_mod_extensions_node,
)
from core.patch_engine import PatchGenerator
from core.rimworld_patch_apply import PatchApplier


SHELTERS = (
    "POUCH", "DEN", "GROUND_NEST_ALTRICIAL", "TREE_NEST_ALTRICIAL",
    "GROUND_NEST_PRECOCIAL", "TREE_NEST_PRECOCIAL", "AQUATIC_CLUTCH",
)
OFFSPRING_CLASS = "ZoologyMod.ModExtension_OffspringShelter, ZoologyMod"
DORMANCY_CLASS = "ZoologyMod.ModExtension_WinterDormancy, ZoologyMod"


def animal(shelter="", dormancy=""):
    return pd.Series({
        "XML name": "<li>TestAnimal</li>",
        "Common name": "Test Animal",
        "Parrent abstract": "AnimalThingBase",
        "OffspringShelter": shelter,
        "WinterDormancy": dormancy,
    })


class DataDrivenShelterTests(unittest.TestCase):
    def setUp(self):
        self.patch_generator = PatchGenerator.__new__(PatchGenerator)

    def test_both_columns_are_concrete_only(self):
        parent = build_mod_extensions_for_parent({
            "OffspringShelter": "POUCH",
            "WinterDormancy": "True",
        })
        self.assertIsNone(parent)

    def assert_profile_roundtrip(self, row, class_name, fields):
        generated = build_mod_extensions_for_child(row, {}, include_all=True)
        extensions = generated.findall("li")
        self.assertEqual(len(extensions), 1)
        self.assertEqual(extensions[0].get("Class"), class_name)
        self.assertEqual({child.tag: child.text for child in extensions[0]}, fields)
        ops = self.patch_generator.create_concrete_mod_extension_patches("TestAnimal", row)
        added = [op for op in ops if op.get("Class") == "PatchOperationAddModExtension"]
        self.assertEqual(len(added), 1)
        item = added[0].find("./value/li")
        self.assertEqual(item.get("Class"), class_name)
        self.assertEqual({child.tag: child.text for child in item}, fields)

    def test_reduced_wandering_is_concrete_and_independent_of_ectothermy(self):
        marker = "ZoologyMod.ModExtension_ReducedWandering, ZoologyMod"
        ecto = "ZoologyMod.ModExtension_Ectothermic, ZoologyMod"
        self.assertIsNone(build_mod_extensions_for_parent({"ReducedWandering": "True"}))
        for ectothermic, reduced in (("True", "True"), ("True", "False"),
                                     ("False", "True"), ("False", "False")):
            with self.subTest(ectothermic=ectothermic, reduced=reduced):
                row = pd.Series({"Ectothermic": ectothermic, "ReducedWandering": reduced})
                node = build_mod_extensions_for_child(row, {}, include_all=True)
                classes = [] if node is None else [li.get("Class") for li in node]
                self.assertEqual(marker in classes, reduced == "True")
                self.assertEqual(ecto in classes, ectothermic == "True")
                parent = {"ReducedWandering": reduced}
                child = build_mod_extensions_for_child(row, parent, include_all=False)
                child_classes = [] if child is None else [li.get("Class") for li in child]
                self.assertEqual(marker in child_classes, reduced == "True")
                ops = self.patch_generator.create_concrete_mod_extension_patches("TestAnimal", row)
                added = [op.find("./value/li").get("Class") for op in ops
                         if op.get("Class") == "PatchOperationAddModExtension"]
                self.assertEqual(added, [marker] if reduced == "True" else [])
        root = LET.fromstring('<Defs><ThingDef><defName>TestAnimal</defName><modExtensions>'
                              '<li Class="' + ecto + '"/></modExtensions></ThingDef></Defs>')
        for reduced in ("True", "False"):
            patch = LET.Element("Patch")
            patch.extend(self.patch_generator.create_concrete_mod_extension_patches(
                "TestAnimal", pd.Series({"ReducedWandering": reduced})))
            PatchApplier(record_missing=True).apply_patch_root(patch, root)
            classes = [li.get("Class") for li in root.xpath("./ThingDef/modExtensions/li")]
            self.assertEqual(marker in classes, reduced == "True")
            self.assertIn(ecto, classes)

    def test_offspring_shelter_modes_roundtrip(self):
        for mode in SHELTERS:
            with self.subTest(mode=mode):
                self.assert_profile_roundtrip(animal(mode), OFFSPRING_CLASS, {"mode": mode})

    def test_winter_eligibility_roundtrip(self):
        self.assert_profile_roundtrip(animal(dormancy="True"), DORMANCY_CLASS,
                                      {"ordinaryDormancy": "true"})
        for flag in ("True", "False"):
            row = animal(dormancy=flag)
            row["Maternity denning season"] = "Autumn, Winter"
            self.assert_profile_roundtrip(row, DORMANCY_CLASS,
                {"ordinaryDormancy": flag.lower(), "maternitySeasonMask": "12"})
        for flag in ("", "False"):
            row = animal(dormancy=flag)
            self.assertIsNone(build_mod_extensions_for_child(row, {}, True))
            ops = self.patch_generator.create_concrete_mod_extension_patches("TestAnimal", row)
            self.assertFalse(any(op.get("Class") == "PatchOperationAddModExtension" for op in ops))

    def test_none_and_empty_clear_only_owned_modes(self):
        root = LET.fromstring("""<ThingDef><modExtensions>
          <li Class="Other.Custom, Other"/>
          <li Class="ZoologyMod.ModExtension_OffspringShelter, ZoologyMod">
            <mode>DEN</mode></li>
          <li Class="ZoologyMod.ModExtension_WinterDormancy, ZoologyMod">
            <ordinaryDormancy>true</ordinaryDormancy></li>
        </modExtensions></ThingDef>""")
        no_modes = animal("NONE", "")
        self.assertIsNone(build_mod_extensions_for_child(no_modes, {}, True))
        _merge_mod_extensions_node(root, None, manage_concrete_extensions=True)
        self.assertEqual([li.get("Class") for li in root.findall("./modExtensions/li")],
                         ["Other.Custom, Other"])

    def test_invalid_modes_fail_not_silently_ignored(self):
        for row in (animal("NEST"), animal("POUCHES"), animal("DEN", "INVALID")):
            with self.subTest(row=row.to_dict()):
                with self.assertRaisesRegex(ValueError, "Unknown|Invalid"):
                    build_mod_extensions_for_child(row, {}, True)
                with self.assertRaisesRegex(ValueError, "Unknown|Invalid"):
                    self.patch_generator.create_concrete_mod_extension_patches(
                        "TestAnimal", row)

    def test_patch_repeated_generation_does_not_accumulate_modes(self):
        root = LET.fromstring("""<Defs><ThingDef><defName>TestAnimal</defName>
            <modExtensions><li Class="Other.Custom, Other"/></modExtensions>
            </ThingDef></Defs>""")
        applier = PatchApplier(record_missing=True)
        for mode in ("DEN", "POUCH", "NONE", "POUCH"):
            ops = self.patch_generator.create_concrete_mod_extension_patches(
                "TestAnimal", animal(mode, "True"))
            patch = LET.Element("Patch")
            patch.extend(ops)
            applier.apply_patch_root(patch, root)
        extensions = root.xpath("./ThingDef/modExtensions/li")
        self.assertEqual(sum(1 for x in extensions if x.get("Class") == OFFSPRING_CLASS), 1)
        self.assertEqual(sum(1 for x in extensions if x.get("Class") == DORMANCY_CLASS), 1)
        self.assertEqual(sum(1 for x in extensions if x.get("Class") == "Other.Custom, Other"), 1)


if __name__ == "__main__":
    unittest.main()
