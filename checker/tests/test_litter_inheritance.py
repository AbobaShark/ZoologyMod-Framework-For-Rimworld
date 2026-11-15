import unittest

import pandas as pd
from lxml import etree as E

from core.patch_engine import PatchGenerator
from core.rimworld_patch_apply import PatchApplier


class LitterInheritanceTests(unittest.TestCase):
    def test_child_with_shared_mean_does_not_merge_a_local_curve(self):
        # The same pattern as VAE's cheetah: an explicit upstream list
        # without Inherit=False and a parent with a generated common curve.
        root = E.fromstring(b'''<Defs>
          <ThingDef Name="BigCatThingBase">
            <race><litterSizeCurve Inherit="False"><points>
              <li>(0.3, 0)</li><li>(1.5, 1)</li><li>(2.7, 0)</li>
            </points></litterSizeCurve></race>
          </ThingDef>
          <ThingDef ParentName="BigCatThingBase"><defName>AEXP_Cheetah</defName>
            <race><litterSizeCurve><points>
              <li>(0.5, 0)</li><li>(2, 1.7)</li><li>(3.5, 0)</li>
            </points></litterSizeCurve></race>
          </ThingDef>
        </Defs>''')
        generator = PatchGenerator(None, enable_ce=False, optimize=False)
        row = pd.Series({"XML name": "<li>AEXP_Cheetah</li>",
                         "Parrent abstract": "BigCatThingBase",
                         "Litter size": "1.5"})
        parent_map = {"BigCatThingBase": {"common_values": {"Litter size": "1.5"},
                                          "differing_cols": [], "tool_signature": None}}
        operations = generator.generate_def_patches(
            "AEXP_Cheetah", row, root, "BigCatThingBase", parent_common_map=parent_map)
        target = '/Defs/ThingDef[defName = "AEXP_Cheetah"]/race/litterSizeCurve'
        matching = [op for op in operations if op.findtext("xpath") == target]
        self.assertEqual(len(matching), 1)
        self.assertEqual(matching[0].find("match").get("Class"), "PatchOperationRemove")
        self.assertTrue(PatchApplier().apply_operation(matching[0], root))
        self.assertFalse(root.xpath(target))
        self.assertEqual(len(root.xpath('/Defs/ThingDef[@Name="BigCatThingBase"]/race/litterSizeCurve/points/li')), 3)


if __name__ == "__main__":
    unittest.main()
