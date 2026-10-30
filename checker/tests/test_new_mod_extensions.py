"""Regression tests for hunting/refuge extensions generated from Animals."""
import sys
import unittest
from pathlib import Path

import pandas as pd
from lxml import etree as LET

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from rimworld_xml_generator import (
    MOD_EXTENSION_CLASS_MAP, build_mod_extensions_for_child,
    build_mod_extensions_for_parent, _merge_mod_extensions_node,
    update_thingdef_inplace,
)
from core.patch_engine import PatchGenerator
from core.rimworld_patch_apply import PatchApplier

CLASSES = {k: MOD_EXTENSION_CLASS_MAP[k] for k in
           ('PredationStrategy', 'Burrower', 'TreeClimber')}


def row(strategy='Ambush', burrow='True', tree='False', predator='true', **kwargs):
    return pd.Series({'XML name': '<li>TestAnimal</li>', 'Common name': 'Test',
                      'Parrent abstract': 'AnimalThingBase',
                      'PredationStrategy': strategy, 'Burrower': burrow,
                      'TreeClimber': tree, 'predator': predator, **kwargs})


def classes(root):
    return [x.get('Class') for x in root.findall('./modExtensions/li')]


class XmlExtensionTests(unittest.TestCase):
    def test_concrete_children_keep_distinct_strategy_even_when_parent_common(self):
        parent = build_mod_extensions_for_parent({'PredationStrategy': 'Ambush',
                                                  'Burrower': 'True', 'TreeClimber': 'True'})
        self.assertIsNone(parent)
        child = build_mod_extensions_for_child(row(),
            {'PredationStrategy': 'Ambush', 'Burrower': 'True', 'TreeClimber': 'False'},
            include_all=False)
        self.assertEqual({x.get('Class') for x in child},
                         {CLASSES['PredationStrategy'], CLASSES['Burrower']})
        self.assertEqual(child.xpath('./li/strategy/text()'), ['Ambush'])

    def test_switch_or_disable_removes_only_owned_extensions_and_is_idempotent(self):
        root = LET.fromstring('''<ThingDef><modExtensions>
             <li Class="Alien.Custom, Alien" />
             <li Class="ZoologyMod.ModExtension_PredationStrategy, ZoologyMod"><strategy>Ambush</strategy></li>
             <li Class="ZoologyMod.ModExtension_Burrower, ZoologyMod" />
             <li Class="ZoologyMod.ModExtension_Burrower, ZoologyMod" />
          </modExtensions></ThingDef>''')
        new = build_mod_extensions_for_child(row('WaterAmbush', 'False', 'True'), {}, True)
        for _ in range(2):
            _merge_mod_extensions_node(root, new, manage_concrete_extensions=True)
        self.assertEqual(classes(root),
                         ['Alien.Custom, Alien', CLASSES['PredationStrategy'], CLASSES['TreeClimber']])
        self.assertEqual(root.xpath('./modExtensions/li/strategy/text()'), ['WaterAmbush'])
        _merge_mod_extensions_node(root, None, manage_concrete_extensions=True)
        self.assertEqual(classes(root), ['Alien.Custom, Alien'])

    def test_non_predator_gets_no_strategy(self):
        generated = build_mod_extensions_for_child(row(predator='false'), {}, True)
        self.assertNotIn(CLASSES['PredationStrategy'], [x.get('Class') for x in generated])

    def test_blank_and_nan_are_off(self):
        generated = build_mod_extensions_for_child(row(strategy=float('nan'),burrow=float('nan')), {}, True)
        self.assertIsNone(generated)

    def test_invalid_strategy_raises(self):
        with self.assertRaisesRegex(ValueError,'PredationStrategy'):
            build_mod_extensions_for_child(row(strategy='SomethingElse'), {}, True)

    def test_real_update_reconciles_existing_extension(self):
        defn = LET.fromstring('''<ThingDef ParentName="AnimalThingBase"><defName>TestAnimal</defName>
          <modExtensions><li Class="Unrelated"/>
            <li Class="ZoologyMod.ModExtension_PredationStrategy, ZoologyMod"><strategy>Ambush</strategy></li>
          </modExtensions></ThingDef>''')
        update_thingdef_inplace(defn, row('No', 'False', 'True', predator='false'))
        self.assertEqual(classes(defn), ['Unrelated', CLASSES['TreeClimber']])
        update_thingdef_inplace(defn, row('No', 'False', 'False', predator='false'))
        self.assertEqual(classes(defn), ['Unrelated'])


class PatchExtensionTests(unittest.TestCase):
    def setUp(self):
        self.gen = PatchGenerator.__new__(PatchGenerator)

    def test_patch_generation_guards_removal_and_adds_correct_payload(self):
        ops = self.gen.create_concrete_mod_extension_patches('TestAnimal', row())
        self.assertEqual(len(ops), 5)  # three guarded removals, two additions
        self.assertEqual([x.get('Class') for x in ops].count('PatchOperationConditional'), 3)
        self.assertEqual([x.get('Class') for x in ops].count('PatchOperationAddModExtension'), 2)
        self.assertEqual(ops[1].xpath('./value/li/strategy/text()'), ['Ambush'])
        root = LET.fromstring('''<Defs><ThingDef><defName>TestAnimal</defName>
          <modExtensions><li Class="Unrelated"/>
          <li Class="ZoologyMod.ModExtension_PredationStrategy, ZoologyMod"><strategy>Chasing</strategy></li>
          <li Class="ZoologyMod.ModExtension_TreeClimber, ZoologyMod"/>
          </modExtensions></ThingDef></Defs>''')
        patch = LET.Element('Patch');patch.extend(ops)
        PatchApplier(record_missing=True).apply_patch_root(patch, root)
        defn = root.xpath('./ThingDef[defName="TestAnimal"]')[0]
        self.assertEqual(classes(defn), ['Unrelated', CLASSES['PredationStrategy'], CLASSES['Burrower']])
        self.assertEqual(defn.xpath('./modExtensions/li/strategy/text()'), ['Ambush'])

    def test_false_and_no_do_not_leave_stale_extensions(self):
        ops=self.gen.create_concrete_mod_extension_patches('TestAnimal',row('No','False','False'))
        self.assertEqual(len(ops),3)
        self.assertTrue(all(x.get('Class')=='PatchOperationConditional' for x in ops))

    def test_non_predator_strategy_blocked_even_with_non_no_input(self):
        ops=self.gen.create_concrete_mod_extension_patches('TestAnimal',row(predator='false'))
        self.assertEqual(len(ops),4)
        self.assertFalse(any(x.xpath('./value/li/strategy') for x in ops))

    def test_legacy_schema_is_ignored(self):
        ops=self.gen.create_concrete_mod_extension_patches('TestAnimal',pd.Series({'predator':'true'}))
        self.assertEqual(ops, [])

    def test_invalid_strategy_is_error(self):
        with self.assertRaisesRegex(ValueError,'PredationStrategy'):
            self.gen.create_concrete_mod_extension_patches('TestAnimal',row('NotKnown'))


if __name__ == '__main__':
    unittest.main()
