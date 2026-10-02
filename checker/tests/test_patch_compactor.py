import copy
import tempfile
import unittest
from pathlib import Path

from lxml import etree as LET

from rimworld_patch_apply import PatchApplier

# Production module intentionally does not exist yet: these tests define the API.
from rimworld_patch_compactor import PatchCompactor, compact_file, patch_metrics


def parse(xml):
    return LET.fromstring(xml.encode('utf-8'))


def apply_patch(patch_root, defs_root, active_mods=None):
    root = copy.deepcopy(defs_root)
    PatchApplier(record_missing=True, active_mods=active_mods).apply_patch_root(
        patch_root, root
    )
    return root


def c14n(root):
    return LET.tostring(root, method='c14n')


class PatchCompactorTests(unittest.TestCase):
    def test_groups_same_replace_across_distinct_defs(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="A"]/race/petness</xpath><value><petness>0.4</petness></value></Operation>
  <Operation Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="B"]/race/petness</xpath><value><petness>0.4</petness></value></Operation>
</Patch>''')
        compacted = PatchCompactor().compact(patch)
        replaces = compacted.xpath('./*[@Class="PatchOperationReplace"]')
        self.assertEqual(len(replaces), 1)
        xpath = replaces[0].findtext('xpath')
        self.assertIn('defName="A"', xpath)
        self.assertIn('defName="B"', xpath)
        self.assertTrue('|' in xpath or ' or ' in xpath)

    def test_groups_remove_only_conditionals_without_changing_result(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationConditional">
    <xpath>/Defs/ThingDef[defName="A"]/race/old</xpath>
    <match Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="A"]/race/old</xpath></match>
  </Operation>
  <Operation Class="PatchOperationConditional">
    <xpath>/Defs/ThingDef[defName="B"]/race/old</xpath>
    <match Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="B"]/race/old</xpath></match>
  </Operation>
</Patch>''')
        defs = parse('''<Defs>
  <ThingDef><defName>A</defName><race><old>1</old></race></ThingDef>
  <ThingDef><defName>B</defName><race/></ThingDef>
</Defs>''')
        expected = apply_patch(patch, defs)
        compacted = PatchCompactor().compact(patch)
        self.assertEqual(len(compacted.xpath('./*[@Class="PatchOperationConditional"]')), 1)
        actual = apply_patch(compacted, defs)
        self.assertEqual(c14n(actual), c14n(expected))

    def test_factorizes_replace_or_add_upserts_with_mixed_presence(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationConditional">
    <xpath>/Defs/ThingDef[defName="A"]/race/petness</xpath>
    <match Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="A"]/race/petness</xpath><value><petness>0.4</petness></value></match>
    <nomatch Class="PatchOperationAdd"><xpath>/Defs/ThingDef[defName="A"]/race</xpath><value><petness>0.4</petness></value></nomatch>
  </Operation>
  <Operation Class="PatchOperationConditional">
    <xpath>/Defs/ThingDef[defName="B"]/race/petness</xpath>
    <match Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="B"]/race/petness</xpath><value><petness>0.4</petness></value></match>
    <nomatch Class="PatchOperationAdd"><xpath>/Defs/ThingDef[defName="B"]/race</xpath><value><petness>0.4</petness></value></nomatch>
  </Operation>
</Patch>''')
        defs = parse('''<Defs>
  <ThingDef><defName>A</defName><race><petness>0.1</petness></race></ThingDef>
  <ThingDef><defName>B</defName><race/></ThingDef>
</Defs>''')
        expected = apply_patch(patch, defs)
        compacted = PatchCompactor().compact(patch)
        actual = apply_patch(compacted, defs)
        self.assertEqual(c14n(actual), c14n(expected))
        # Two per-def conditionals should become at most two aggregate guards.
        self.assertLessEqual(len(compacted.xpath('.//*[@Class="PatchOperationConditional"]')), 2)
        self.assertLess(patch_metrics(compacted)['operations'], patch_metrics(patch)['operations'])


    def test_reorders_disjoint_defs_to_unlock_cross_def_grouping(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="A"]/race/petness</xpath><value><petness>0.1</petness></value></Operation>
  <Operation Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="A"]/race/old</xpath></Operation>
  <Operation Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="B"]/race/petness</xpath><value><petness>0.2</petness></value></Operation>
  <Operation Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="B"]/race/old</xpath></Operation>
</Patch>''')
        defs = parse('''<Defs>
  <ThingDef><defName>A</defName><race><petness>9</petness><old>1</old></race></ThingDef>
  <ThingDef><defName>B</defName><race><petness>9</petness><old>1</old></race></ThingDef>
</Defs>''')
        expected = apply_patch(patch, defs)
        compacted = PatchCompactor().compact(patch)
        removes = compacted.xpath('./*[@Class="PatchOperationRemove"]')
        self.assertEqual(len(removes), 1)
        actual = apply_patch(compacted, defs)
        self.assertEqual(c14n(actual), c14n(expected))


    def test_batches_multiple_upserts_and_removals_for_one_parent(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationConditional"><xpath>/Defs/ThingDef[defName="A"]/race/petness</xpath><match Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="A"]/race/petness</xpath><value><petness>0.4</petness></value></match><nomatch Class="PatchOperationAdd"><xpath>/Defs/ThingDef[defName="A"]/race</xpath><value><petness>0.4</petness></value></nomatch></Operation>
  <Operation Class="PatchOperationConditional"><xpath>/Defs/ThingDef[defName="A"]/race/waterSeeker</xpath><match Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="A"]/race/waterSeeker</xpath><value><waterSeeker>false</waterSeeker></value></match><nomatch Class="PatchOperationAdd"><xpath>/Defs/ThingDef[defName="A"]/race</xpath><value><waterSeeker>false</waterSeeker></value></nomatch></Operation>
  <Operation Class="PatchOperationConditional"><xpath>/Defs/ThingDef[defName="A"]/race/old</xpath><match Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="A"]/race/old</xpath></match></Operation>
</Patch>''')
        defs = parse('''<Defs><ThingDef><defName>A</defName><race><petness>9</petness><old>1</old></race></ThingDef></Defs>''')
        expected = apply_patch(patch, defs)
        compacted = PatchCompactor().compact(patch)
        actual = apply_patch(compacted, defs)
        self.assertEqual(c14n(actual), c14n(expected))
        self.assertLessEqual(patch_metrics(compacted)['operations'], 4)
        adds = compacted.xpath('.//*[@Class="PatchOperationAdd"]')
        self.assertEqual(len(adds), 1)
        self.assertEqual({child.tag for child in adds[0].find('value')}, {'petness', 'waterSeeker'})


    def test_merges_adjacent_add_mod_extensions_on_same_target(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationAddModExtension"><xpath>/Defs/ThingDef[defName="A"]</xpath><value><li Class="Ext.One"/></value></Operation>
  <Operation Class="PatchOperationAddModExtension"><xpath>/Defs/ThingDef[defName="A"]</xpath><value><li Class="Ext.Two"/></value></Operation>
</Patch>''')
        defs = parse('''<Defs><ThingDef><defName>A</defName></ThingDef></Defs>''')
        expected = apply_patch(patch, defs)
        compacted = PatchCompactor().compact(patch)
        self.assertEqual(len(compacted.xpath('./*[@Class="PatchOperationAddModExtension"]')), 1)
        self.assertEqual(c14n(apply_patch(compacted, defs)), c14n(expected))

    def test_sequence_inside_findmod_is_semantic_barrier(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationFindMod">
    <mods><li>Combat Extended</li></mods>
    <match Class="PatchOperationSequence"><operations>
      <li Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="A"]/tools</xpath></li>
      <li Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="B"]/tools</xpath></li>
    </operations></match>
  </Operation>
</Patch>''')
        compacted = PatchCompactor().compact(patch)
        removes = compacted.xpath('.//*[@Class="PatchOperationRemove"]')
        self.assertEqual(len(removes), 2)
        self.assertEqual(
            [node.findtext("xpath") for node in removes],
            [
                '/Defs/ThingDef[defName="A"]/tools',
                '/Defs/ThingDef[defName="B"]/tools',
            ],
        )

    def test_top_level_sequence_is_not_flattened(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationSequence"><operations>
    <li Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="Missing"]/tools</xpath></li>
    <li Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="A"]/tools</xpath></li>
  </operations></Operation>
</Patch>''')
        defs = parse('''<Defs><ThingDef><defName>A</defName><tools><li/></tools></ThingDef></Defs>''')

        expected_root = copy.deepcopy(defs)
        expected_applier = PatchApplier(record_missing=True)
        expected_success = expected_applier.apply_patch_root(patch, expected_root)
        self.assertFalse(expected_success)
        self.assertEqual(len(expected_root.xpath('/Defs/ThingDef[defName="A"]/tools')), 1)

        compacted = PatchCompactor().compact(patch)
        sequences = compacted.xpath('./*[@Class="PatchOperationSequence"]')
        self.assertEqual(len(sequences), 1)
        self.assertEqual(len(sequences[0].xpath('./operations/*')), 2)

        actual_root = copy.deepcopy(defs)
        actual_success = PatchApplier(record_missing=True).apply_patch_root(compacted, actual_root)
        self.assertEqual(actual_success, expected_success)
        self.assertEqual(c14n(actual_root), c14n(expected_root))

    def test_adjacent_findmods_are_not_merged_into_sequence(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationFindMod">
    <mods><li>Combat Extended</li></mods>
    <match Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="A"]/tools</xpath></match>
  </Operation>
  <Operation Class="PatchOperationFindMod">
    <mods><li>Combat Extended</li></mods>
    <match Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="B"]/tools</xpath></match>
  </Operation>
</Patch>''')
        compacted = PatchCompactor().compact(patch)
        findmods = compacted.xpath('./*[@Class="PatchOperationFindMod"]')
        self.assertEqual(len(findmods), 2)
        self.assertFalse(compacted.xpath('./*[@Class="PatchOperationFindMod"]/*[@Class="PatchOperationSequence"]'))

    def test_compaction_reaches_fixed_point_in_one_call(self):
        patch = parse('''<Patch>
  <Operation Class="PatchOperationSequence"><operations>
    <li Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="A"]/race/petness</xpath><value><petness>0.4</petness></value></li>
    <li Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName="B"]/race/petness</xpath><value><petness>0.4</petness></value></li>
  </operations></Operation>
</Patch>''')
        compactor = PatchCompactor()
        once = compactor.compact(patch)
        twice = PatchCompactor().compact(copy.deepcopy(once))
        self.assertEqual(c14n(once), c14n(twice))

    def test_compact_file_overwrites_same_file_without_backup(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / 'patch.xml'
            path.write_text('''<?xml version="1.0" encoding="utf-8"?>\n<Patch>\n  <!-- remove me -->\n  <Operation Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="A"]/tools</xpath></Operation>\n  <Operation Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="B"]/tools</xpath></Operation>\n</Patch>\n''', encoding='utf-8')
            before = path.stat().st_size
            report = compact_file(path)
            self.assertEqual(report.path, str(path))
            self.assertLess(path.stat().st_size, before)
            self.assertEqual(list(Path(td).glob('*.bak')), [])
            text = path.read_text(encoding='utf-8')
            self.assertNotIn('remove me', text)
            self.assertEqual(len(LET.parse(str(path)).xpath('/*/*[@Class="PatchOperationRemove"]')), 1)


if __name__ == '__main__':
    unittest.main()
