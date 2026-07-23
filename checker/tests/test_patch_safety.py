import sys
import tempfile
import unittest
from pathlib import Path

import pandas as pd
from lxml import etree as LET


CHECKER_DIR = Path(__file__).resolve().parents[1]
if str(CHECKER_DIR) not in sys.path:
    sys.path.insert(0, str(CHECKER_DIR))

from rimworld_original_xml import OriginalXmlIndex
from rimworld_patch_apply import PatchApplier
from rimworld_patch_fixer import PatchGenerator
from rimworld_patch_optimizer import PatchOptimizer
from rimworld_xml_generator import build_ce_patch, update_ce_on_thing_inplace


class PatchOptimizerRuntimeGuardTests(unittest.TestCase):
    def _index(self):
        temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(temp_dir.cleanup)
        xml_path = Path(temp_dir.name) / "Races.xml"
        xml_path.write_text(
            """<?xml version="1.0" encoding="utf-8"?>
<Defs>
  <ThingDef Name="BigCatThingBase" Abstract="True">
    <race>
      <lifeStageAges>
        <li><def>AnimalAdult</def><minAge>0.5</minAge></li>
      </lifeStageAges>
    </race>
  </ThingDef>
  <ThingDef ParentName="BigCatThingBase">
    <defName>Panther</defName>
  </ThingDef>
</Defs>
""",
            encoding="utf-8",
        )
        return OriginalXmlIndex(temp_dir.name).load()

    def test_missing_field_add_precondition_is_kept_by_default(self):
        patch = LET.fromstring(
            b"""<Patch>
  <Operation Class="PatchOperationConditional">
    <xpath>/Defs/ThingDef[defName = "Panther"]/race</xpath>
    <nomatch Class="PatchOperationAdd">
      <xpath>/Defs/ThingDef[defName = "Panther"]</xpath>
      <value><race/></value>
    </nomatch>
  </Operation>
</Patch>"""
        )

        optimizer = PatchOptimizer(self._index())
        optimized = optimizer.optimize(patch)

        self.assertEqual(optimized[0].get("Class"), "PatchOperationConditional")
        self.assertEqual(optimizer.stats["conditionals_kept_missing_target_guard"], 1)

    def test_closed_world_mode_can_still_resolve_missing_field_guard(self):
        patch = LET.fromstring(
            b"""<Patch>
  <Operation Class="PatchOperationConditional">
    <xpath>/Defs/ThingDef[defName = "Panther"]/race</xpath>
    <nomatch Class="PatchOperationAdd">
      <xpath>/Defs/ThingDef[defName = "Panther"]</xpath>
      <value><race/></value>
    </nomatch>
  </Operation>
</Patch>"""
        )

        optimized = PatchOptimizer(
            self._index(),
            preserve_missing_target_guards=False,
        ).optimize(patch)

        self.assertEqual(optimized[0].get("Class"), "PatchOperationAdd")

    def test_missing_defensive_remove_is_not_discarded(self):
        patch = LET.fromstring(
            b"""<Patch>
  <Operation Class="PatchOperationConditional">
    <xpath>/Defs/ThingDef[@Name = "BigCatThingBase"]/race/litterSizeCurve</xpath>
    <match Class="PatchOperationRemove">
      <xpath>/Defs/ThingDef[@Name = "BigCatThingBase"]/race/litterSizeCurve</xpath>
    </match>
  </Operation>
</Patch>"""
        )

        optimized = PatchOptimizer(self._index()).optimize(patch)

        self.assertEqual(len(optimized), 1)
        self.assertEqual(optimized[0].get("Class"), "PatchOperationConditional")

    def test_panther_fields_replace_earlier_mod_values_without_duplicates(self):
        generator = PatchGenerator.__new__(PatchGenerator)
        patch = LET.Element("Patch")
        ensure_race = LET.fromstring(
            b"""<Operation Class="PatchOperationConditional">
  <xpath>/Defs/ThingDef[defName = "Panther"]/race</xpath>
  <nomatch Class="PatchOperationAdd">
    <xpath>/Defs/ThingDef[defName = "Panther"]</xpath>
    <value><race/></value>
  </nomatch>
</Operation>"""
        )
        patch.append(ensure_race)
        patch.append(
            generator.create_life_stage_full_replace_or_add(
                "Panther",
                "0.2",
                "0.4",
            )
        )
        patch.append(
            generator.create_litter_curve_replace(
                "Panther",
                [(0.4, 0), (1.6, 1)],
            )
        )

        index = self._index()
        optimized = PatchOptimizer(index).optimize(patch)
        runtime_root = index.clone_root()
        panther = runtime_root.xpath('./ThingDef[defName="Panther"]')[0]
        prior_race = LET.SubElement(panther, "race")
        prior_stages = LET.SubElement(prior_race, "lifeStageAges")
        prior_stage = LET.SubElement(prior_stages, "li")
        LET.SubElement(prior_stage, "def").text = "PriorModStage"
        prior_litter = LET.SubElement(prior_race, "litterSizeCurve")
        prior_points = LET.SubElement(prior_litter, "points")
        LET.SubElement(prior_points, "li").text = "(99, 1)"

        PatchApplier(record_missing=True).apply_patch_root(optimized, runtime_root)

        self.assertEqual(len(panther.findall("race")), 1)
        self.assertEqual(len(panther.findall("race/lifeStageAges")), 1)
        self.assertEqual(len(panther.findall("race/litterSizeCurve")), 1)
        self.assertEqual(
            panther.xpath("race/lifeStageAges/li/def/text()"),
            ["AnimalBaby", "AnimalJuvenile", "AnimalAdult"],
        )
        self.assertEqual(panther.xpath("race/lifeStageAges/@Inherit"), ["False"])
        self.assertEqual(panther.xpath("race/litterSizeCurve/@Inherit"), ["False"])


class FullListReplacementTests(unittest.TestCase):
    def setUp(self):
        self.generator = PatchGenerator.__new__(PatchGenerator)

    def test_life_stage_replacement_disables_list_inheritance(self):
        op = self.generator.create_life_stage_full_replace_or_add(
            "Panther",
            "0.2",
            "0.4",
            inherit_false=False,
        )

        nodes = op.xpath("./match/value/lifeStageAges | ./nomatch/value/lifeStageAges")
        self.assertEqual(len(nodes), 2)
        self.assertTrue(all(node.get("Inherit") == "False" for node in nodes))

    def test_litter_curve_replacement_disables_point_inheritance(self):
        op = self.generator.create_litter_curve_replace(
            "Panther",
            [(0.4, 0), (1.6, 1)],
            inherit_false=False,
        )

        nodes = op.xpath("./match/value/litterSizeCurve | ./nomatch/value/litterSizeCurve")
        self.assertEqual(len(nodes), 2)
        self.assertTrue(all(node.get("Inherit") == "False" for node in nodes))


class CombatExtendedBodyShapeSafetyTests(unittest.TestCase):
    def _generate_ce_block(self, shapes):
        generator = PatchGenerator.__new__(PatchGenerator)
        vanilla_rows = [
            {
                "XML name": f"<li>{def_name}</li>",
                "Parrent abstract": "BaseTestAnimal",
            }
            for def_name in shapes
        ]
        ce_rows = [
            {
                "XML name": f"<li>{def_name}</li>",
                "Body shape": body_shape,
            }
            for def_name, body_shape in shapes.items()
        ]
        generator.vanilla_df = pd.DataFrame(vanilla_rows)
        generator.ce_df = pd.DataFrame(ce_rows)
        def_to_row = {
            def_name: generator.vanilla_df.iloc[index]
            for index, def_name in enumerate(shapes)
        }
        return generator.generate_ce_block(def_to_row, LET.Element("Defs"))

    def _body_shape_remove_xpaths(self, patch):
        return [
            xpath
            for xpath in patch.xpath('.//*[@Class="PatchOperationRemove"]/xpath/text()')
            if "RacePropertiesExtensionCE" in xpath and xpath.rstrip().endswith("/bodyShape")
        ]

    def _assert_concrete_body_shape_upsert(self, patch, def_name, expected):
        target_xpath = (
            f'Defs/ThingDef[defName="{def_name}"]/modExtensions/'
            'li[@Class="CombatExtended.RacePropertiesExtensionCE"]'
        )
        operations = [
            node
            for node in patch.xpath('.//li[@Class="PatchOperationConditional"]')
            if node.findtext("xpath") == target_xpath
        ]
        self.assertEqual(len(operations), 1, def_name)
        self.assertEqual(set(operations[0].xpath(".//bodyShape/text()")), {expected})

    def test_different_child_shapes_remove_whole_parent_extension(self):
        patch = self._generate_ce_block(
            {
                "Megaspider": "QuadrupedLow",
                "TestLarva": "Serpentine",
            }
        )

        parent_extension_xpath = (
            'Defs/ThingDef[@Name="BaseTestAnimal"]/modExtensions/'
            'li[@Class="CombatExtended.RacePropertiesExtensionCE"]'
        )
        remove_xpaths = patch.xpath(
            './/*[@Class="PatchOperationRemove"]/xpath/text()'
        )
        self.assertIn(parent_extension_xpath, remove_xpaths)
        self.assertEqual(self._body_shape_remove_xpaths(patch), [])
        self._assert_concrete_body_shape_upsert(
            patch, "Megaspider", "QuadrupedLow"
        )
        self._assert_concrete_body_shape_upsert(
            patch, "TestLarva", "Serpentine"
        )

    def test_common_parent_shape_is_also_written_to_every_child(self):
        patch = self._generate_ce_block(
            {
                "CatA": "Quadruped",
                "CatB": "Quadruped",
            }
        )

        self.assertEqual(self._body_shape_remove_xpaths(patch), [])
        self._assert_concrete_body_shape_upsert(patch, "CatA", "Quadruped")
        self._assert_concrete_body_shape_upsert(patch, "CatB", "Quadruped")

    def test_missing_body_shape_fails_generation(self):
        with self.assertRaisesRegex(
            ValueError, "Combat Extended Body shape is required"
        ):
            self._generate_ce_block({"BrokenAnimal": ""})

    def test_standalone_generator_never_removes_child_body_shape(self):
        patch = build_ce_patch(
            "CatA",
            pd.Series({"XML name": "<li>CatA</li>"}),
            pd.Series({"Body shape": "Quadruped"}),
            parent_abstract="BaseTestAnimal",
            ce_parent_common={"Body shape": "Quadruped"},
            generate_parent=True,
        )

        self.assertEqual(self._body_shape_remove_xpaths(patch), [])
        body_shape_values = patch.xpath(
            './/li[@Class="CombatExtended.RacePropertiesExtensionCE"]/'
            "bodyShape/text()"
        )
        self.assertEqual(body_shape_values, ["Quadruped", "Quadruped"])

    def test_patch_fills_body_shape_on_every_existing_extension(self):
        patch = self._generate_ce_block({"TestAnimal": "Quadruped"})
        runtime_root = LET.fromstring(
            b"""<Defs>
  <ThingDef Name="BaseTestAnimal" Abstract="True"/>
  <ThingDef>
    <defName>TestAnimal</defName>
    <modExtensions>
      <li Class="CombatExtended.RacePropertiesExtensionCE">
        <bodyShape>OldShape</bodyShape>
      </li>
      <li Class="CombatExtended.RacePropertiesExtensionCE">
        <canParry>true</canParry>
      </li>
    </modExtensions>
  </ThingDef>
</Defs>"""
        )

        PatchApplier(
            active_mods=["Combat Extended"],
            record_missing=True,
        ).apply_patch_root(patch, runtime_root)

        extensions = runtime_root.xpath(
            './ThingDef[defName="TestAnimal"]/modExtensions/'
            'li[@Class="CombatExtended.RacePropertiesExtensionCE"]'
        )
        self.assertEqual(
            [extension.findtext("bodyShape") for extension in extensions],
            ["Quadruped", "Quadruped"],
        )

    def test_inplace_generator_fills_body_shape_on_every_extension(self):
        runtime_root = LET.fromstring(
            b"""<Defs>
  <ThingDef>
    <defName>TestAnimal</defName>
    <modExtensions>
      <li Class="CombatExtended.RacePropertiesExtensionCE">
        <bodyShape>OldShape</bodyShape>
      </li>
      <li Class="CombatExtended.RacePropertiesExtensionCE">
        <canParry>true</canParry>
      </li>
    </modExtensions>
  </ThingDef>
</Defs>"""
        )
        thing = runtime_root.find("ThingDef")

        update_ce_on_thing_inplace(
            runtime_root,
            thing,
            pd.Series({"XML name": "<li>TestAnimal</li>"}),
            pd.Series({"Body shape": "Quadruped"}),
        )

        extensions = thing.xpath(
            './modExtensions/'
            'li[@Class="CombatExtended.RacePropertiesExtensionCE"]'
        )
        self.assertEqual(
            [extension.findtext("bodyShape") for extension in extensions],
            ["Quadruped", "Quadruped"],
        )

    def test_insect_patch_repairs_original_ce_megaspider_extension(self):
        index = OriginalXmlIndex(
            str(CHECKER_DIR / "OriginalXML"),
            patches_dir=str(CHECKER_DIR / "OriginalPatches"),
        ).load()
        runtime_root = index.clone_root()
        patch = LET.parse(
            str(
                CHECKER_DIR
                / "generated_patches"
                / "Core"
                / "Races_Animal_Insects.xml"
            )
        ).getroot()

        applier = PatchApplier(
            active_mods=["Combat Extended"],
            record_missing=True,
        )
        applier.apply_patch_root(patch, runtime_root)

        base_extensions = runtime_root.xpath(
            './ThingDef[@Name="BaseInsect"]/modExtensions/'
            'li[@Class="CombatExtended.RacePropertiesExtensionCE"]'
        )
        megaspider_extensions = runtime_root.xpath(
            './ThingDef[defName="Megaspider"]/modExtensions/'
            'li[@Class="CombatExtended.RacePropertiesExtensionCE"]'
        )
        self.assertEqual(base_extensions, [])
        self.assertEqual(len(megaspider_extensions), 1)
        self.assertEqual(
            megaspider_extensions[0].findtext("bodyShape"),
            "QuadrupedLow",
        )
        self.assertEqual(
            megaspider_extensions[0].findtext("canParry"),
            "true",
        )
        self.assertEqual(applier.errors, [])

    def test_committed_patches_never_remove_only_body_shape(self):
        roots = [
            CHECKER_DIR / "generated_patches",
            CHECKER_DIR.parent / "Zoology",
        ]
        unsafe = []
        incomplete_extensions = []
        for root in roots:
            for xml_path in root.rglob("*.xml"):
                patch = LET.parse(str(xml_path))
                for xpath in self._body_shape_remove_xpaths(patch):
                    unsafe.append(f"{xml_path}: {xpath}")
                for extension in patch.xpath(
                    './/value/li[@Class="CombatExtended.RacePropertiesExtensionCE"]'
                ):
                    body_shape = extension.findtext("bodyShape")
                    if body_shape is None or not body_shape.strip():
                        incomplete_extensions.append(str(xml_path))

        self.assertEqual(unsafe, [], "\n".join(unsafe))
        self.assertEqual(
            incomplete_extensions,
            [],
            "\n".join(incomplete_extensions),
        )


if __name__ == "__main__":
    unittest.main()
