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
DORMANCIES = ("HIBERNATION", "WINTER_REST", "DENNING", "MATERNITY_DENNING")
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
            "WinterDormancy": "DENNING",
        })
        self.assertIsNone(parent)

    def test_each_valid_mode_roundtrips_in_both_generators(self):
        for column, choices, class_name in (
            ("OffspringShelter", SHELTERS, OFFSPRING_CLASS),
            ("WinterDormancy", DORMANCIES, DORMANCY_CLASS),
        ):
            for mode in choices:
                with self.subTest(column=column, mode=mode):
                    r = animal(mode if column == "OffspringShelter" else "",
                               mode if column == "WinterDormancy" else "")
                    generated = build_mod_extensions_for_child(r, {}, include_all=True)
                    extensions = generated.findall("li")
                    self.assertEqual(len(extensions), 1)
                    self.assertEqual(extensions[0].get("Class"), class_name)
                    self.assertEqual(extensions[0].findtext("mode"), mode)
                    ops = self.patch_generator.create_concrete_mod_extension_patches(
                        "TestAnimal", r)
                    added = [
                        op for op in ops
                        if op.get("Class") == "PatchOperationAddModExtension"
                    ]
                    self.assertEqual(len(added), 1)
                    self.assertEqual(added[0].xpath("./value/li/@Class"), [class_name])
                    self.assertEqual(added[0].xpath("./value/li/mode/text()"), [mode])

    def test_none_and_empty_clear_only_owned_modes(self):
        root = LET.fromstring("""<ThingDef><modExtensions>
          <li Class="Other.Custom, Other"/>
          <li Class="ZoologyMod.ModExtension_OffspringShelter, ZoologyMod">
            <mode>DEN</mode></li>
          <li Class="ZoologyMod.ModExtension_WinterDormancy, ZoologyMod">
            <mode>HIBERNATION</mode></li>
        </modExtensions></ThingDef>""")
        no_modes = animal("NONE", "")
        self.assertIsNone(build_mod_extensions_for_child(no_modes, {}, True))
        _merge_mod_extensions_node(root, None, manage_concrete_extensions=True)
        self.assertEqual([li.get("Class") for li in root.findall("./modExtensions/li")],
                         ["Other.Custom, Other"])

    def test_invalid_modes_fail_not_silently_ignored(self):
        for row in (animal("NEST"), animal("POUCHES"), animal("DEN", "HIBERNATE")):
            with self.subTest(row=row.to_dict()):
                with self.assertRaisesRegex(ValueError, "Unknown"):
                    build_mod_extensions_for_child(row, {}, True)
                with self.assertRaisesRegex(ValueError, "Unknown"):
                    self.patch_generator.create_concrete_mod_extension_patches(
                        "TestAnimal", row)

    def test_patch_repeated_generation_does_not_accumulate_modes(self):
        root = LET.fromstring("""<Defs><ThingDef><defName>TestAnimal</defName>
            <modExtensions><li Class="Other.Custom, Other"/></modExtensions>
            </ThingDef></Defs>""")
        applier = PatchApplier(record_missing=True)
        for mode in ("DEN", "POUCH", "NONE", "POUCH"):
            ops = self.patch_generator.create_concrete_mod_extension_patches(
                "TestAnimal", animal(mode, "WINTER_REST"))
            patch = LET.Element("Patch")
            patch.extend(ops)
            applier.apply_patch_root(patch, root)
        extensions = root.xpath("./ThingDef/modExtensions/li")
        self.assertEqual(sum(1 for x in extensions if x.get("Class") == OFFSPRING_CLASS), 1)
        self.assertEqual(sum(1 for x in extensions if x.get("Class") == DORMANCY_CLASS), 1)
        self.assertEqual(sum(1 for x in extensions if x.get("Class") == "Other.Custom, Other"), 1)


if __name__ == "__main__":
    unittest.main()
