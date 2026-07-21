import sys
import tempfile
import unittest
from pathlib import Path

from lxml import etree as LET


CHECKER_DIR = Path(__file__).resolve().parents[1]
if str(CHECKER_DIR) not in sys.path:
    sys.path.insert(0, str(CHECKER_DIR))

from rimworld_original_xml import OriginalXmlIndex
from rimworld_patch_apply import PatchApplier
from rimworld_patch_fixer import PatchGenerator
from rimworld_patch_optimizer import PatchOptimizer


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


if __name__ == "__main__":
    unittest.main()
