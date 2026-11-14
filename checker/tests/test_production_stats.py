import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import pandas as pd
from lxml import etree as E
from core.production_stats import refresh_tree, _hatcher_animal, _read_repro, pd_egg_map, render_reproduction, REPRODUCTION_SHEET
from core.xml_routing import plan


def _tables():
    egg=pd.DataFrame([
        {'Def name':'<li>Ostrich</li>','hatcherDaystoHatch':'7','eggLayIntervalDays':'18.81533101','eggCountRange':'6~12',
         'Milk animal':'Cow','Milk yield':'22','Wool animal':'Dromedary','Wool yield per day':'9'},
        {'Def name':'<li>AEXP_Crocodile</li>','hatcherDaystoHatch':'17','eggLayIntervalDays':'60','eggCountRange':'35~65'},
    ])
    animals=pd.DataFrame([
        {'XML name':'<li>Ostrich</li>','Common name':'Ostrich'},
        {'XML name':'<li>AEXP_Crocodile</li>','Common name':'Crocodile'},
        {'XML name':'<li>Cow</li>','Common name':'Cow'},
        {'XML name':'<li>Dromedary</li>','Common name':'Dromedary'}
    ])
    return {'Animals':animals,'EggMilk':egg}


def _tree(xml): return E.ElementTree(E.fromstring(xml))


class ReproductionSheetNameTests(unittest.TestCase):
    def test_renamed_workbook_sheet_is_requested(self):
        self.assertEqual(REPRODUCTION_SHEET, 'EggMilkWool')
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            mod=root/'Zoology'
            mod.mkdir()
            (mod/'LoadFolders.xml').write_text('<loadFolders/>', encoding='utf8')
            animals=_tables()['Animals']
            seen=[]
            def fake_read(source, sheet):
                seen.append(sheet)
                if sheet=='Animals': return animals
                if sheet=='EggMilkWool': return pd.DataFrame()
                raise AssertionError(f'Unexpected sheet name: {sheet}')
            with patch('core.production_stats._read_repro', side_effect=fake_read), \
                 patch('core.production_stats.pd_egg_map', return_value=_tables()['EggMilk']):
                outputs, reports=render_reproduction('AnimalStats.xlsx', root, destinations=['Core/Patches/Core/Misc/CompProperties_Milkable.xml'])
            self.assertEqual(seen, ['EggMilkWool','Animals'])
            self.assertEqual(outputs, [])
            self.assertEqual(len(reports), 1)

    def test_new_sheet_name_is_used_by_multisheet_reader(self):
        with patch('core.animalstats_source.is_multisheet_source', return_value=True), \
             patch('core.production_stats.read_table', return_value=pd.DataFrame()) as reader:
            _read_repro('AnimalStats.xlsx', REPRODUCTION_SHEET)
            reader.assert_called_once_with('AnimalStats.xlsx','EggMilkWool')


class ReproductiveValuesTests(unittest.TestCase):
    def test_egg_fields_update_existing_xml_without_changing_structure(self):
        tree=_tree('''<Patch><Operation Class="PatchOperationConditional">
        <xpath>/Defs/ThingDef[defName="Ostrich"]/comps/li[@Class="CompProperties_EggLayer"]/eggLayIntervalDays</xpath>
        <match Class="PatchOperationReplace"><value><eggLayIntervalDays>15.6</eggLayIntervalDays></value></match>
        </Operation><Operation Class="PatchOperationConditional">
        <xpath>/Defs/ThingDef[defName="Ostrich"]/comps/li[@Class="CompProperties_EggLayer"]/eggCountRange</xpath>
        <match Class="PatchOperationReplace"><value><eggCountRange>10~15</eggCountRange></value></match>
        </Operation></Patch>''')
        changes,warnings,touched=refresh_tree(tree,_tables())
        self.assertEqual({item['field'] for item in changes},{'eggLayIntervalDays','eggCountRange'})
        self.assertEqual(tree.findtext('.//eggLayIntervalDays'),'18.81533101')
        self.assertEqual(tree.findtext('.//eggCountRange'),'6~12')
        self.assertFalse(warnings)

    def test_direct_grouped_replacements_resolve_per_species(self):
        tree = _tree('''<Patch><Operation Class="PatchOperationReplace">
        <xpath>/Defs/ThingDef[defName="Ostrich" or defName="AEXP_Crocodile"]/comps/li[@Class="CompProperties_EggLayer"]/eggCountRange</xpath>
        <value><eggCountRange>1~2</eggCountRange></value>
        </Operation></Patch>''')
        changes, warnings, touched = refresh_tree(tree, _tables())
        self.assertEqual([x['new'] for x in changes], ['6~12', '35~65'])
        self.assertEqual(tree.xpath('//Operation/value/eggCountRange/text()'), ['6~12', '35~65'])
        self.assertFalse(warnings)
        self.assertEqual(len(touched), 2)

    def test_direct_single_replacement(self):
        tree = _tree('''<Patch><Operation Class="PatchOperationReplace">
        <xpath>/Defs/ThingDef[defName="Ostrich"]/comps/li[@Class="CompProperties_EggLayer"]/eggCountRange</xpath>
        <value><eggCountRange>10~15</eggCountRange></value>
        </Operation></Patch>''')
        changes, warnings, _ = refresh_tree(tree, _tables())
        self.assertEqual(changes[0]['new'], '6~12')
        self.assertFalse(warnings)

    def test_hatcher_egg_name_resolution_uses_exact_normalized_def(self):
        self.assertEqual(_hatcher_animal('AEXP_EggCrocodileFertilized',{'AEXP_Crocodile':{}}),'AEXP_Crocodile')
        self.assertEqual(_hatcher_animal('EggAEXP_RockhopperPenguinFertilized',{'AEXP_RockhopperPenguin':{}}),'AEXP_RockhopperPenguin')
        self.assertIsNone(_hatcher_animal('Unknown_Egg',{'AEXP_Crocodile':{}}))
        tree=_tree('''<Patch><Operation Class="PatchOperationConditional">
        <xpath>/Defs/ThingDef[defName="AEXP_EggCrocodileFertilized"]/comps/li[Class="CompProperties_Hatcher"]/hatcherDaystoHatch/text()</xpath>
        <match Class="PatchOperationReplace"><value>12</value></match></Operation></Patch>''')
        c,_,_=refresh_tree(tree,_tables())
        self.assertEqual(c[0]['new'],'17')

    def test_daily_milk_yield_uses_explicit_interval_and_preserves_chemfuel(self):
        tree=_tree('''<Patch>
        <Operation Class="PatchOperationConditional"><xpath>/Defs/ThingDef[defName="Cow"]/comps/li[Class="CompProperties_Milkable"]/milkAmount/text()</xpath>
        <match Class="PatchOperationReplace"><value>12</value></match></Operation>
        <Operation Class="PatchOperationConditional"><xpath>/Defs/ThingDef[defName="Cow"]/comps/li[Class="CompProperties_Milkable"]/milkIntervalDays/text()</xpath>
        <match Class="PatchOperationReplace"><value>2</value></match></Operation>
        <Operation Class="PatchOperationConditional"><xpath>/Defs/ThingDef[defName="Boomrat"]/comps/li[Class="CompProperties_Milkable"]</xpath>
        <match Class="PatchOperationReplace"><value><li Class="CompProperties_Milkable"><milkDef>Chemfuel</milkDef><milkAmount>1</milkAmount><milkIntervalDays>1</milkIntervalDays></li></value></match></Operation>
        </Patch>''')
        c,_,_=refresh_tree(tree,_tables())
        self.assertEqual(c[0]['new'],'44')
        self.assertEqual(tree.xpath('string(//li[milkDef="Chemfuel"]/milkAmount)'),'1')

    def test_wool_yield_scaled_by_explicit_shearing_interval(self):
        tree=_tree('''<Patch><Operation Class="PatchOperationConditional"><xpath>/Defs/ThingDef[defName="Dromedary"]/comps/li[Class="CompProperties_Shearable"]</xpath>
        <match Class="PatchOperationReplace"><value><li Class="CompProperties_Shearable"><woolDef>WoolCamel</woolDef><shearIntervalDays>15</shearIntervalDays><woolAmount>120</woolAmount></li></value></match></Operation></Patch>''')
        c,_,_=refresh_tree(tree,_tables())
        self.assertEqual(c[0]['new'],'135')
        self.assertEqual(tree.findtext('.//woolDef'),'WoolCamel')

    def test_half_item_wool_and_milk_yields_serialize_as_int32(self):
        tables = _tables()
        tables["EggMilk"].loc[0, "Wool yield per day"] = "8.5"
        tables["EggMilk"].loc[0, "Milk yield"] = "8.5"
        source = '''<Patch>
          <Operation Class="PatchOperationConditional">
            <xpath>/Defs/ThingDef[defName="Dromedary"]/comps/li[Class="CompProperties_Shearable"]</xpath>
            <match Class="PatchOperationReplace"><value>
              <li Class="CompProperties_Shearable"><shearIntervalDays>15</shearIntervalDays>
                <woolAmount>135</woolAmount></li>
            </value></match>
          </Operation>
          <Operation Class="PatchOperationConditional">
            <xpath>/Defs/ThingDef[defName="Cow"]/comps/li[Class="CompProperties_Milkable"]</xpath>
            <match Class="PatchOperationReplace"><value>
              <li Class="CompProperties_Milkable"><milkIntervalDays>15</milkIntervalDays>
                <milkAmount>120</milkAmount></li>
            </value></match>
          </Operation>
        </Patch>'''
        tree = _tree(source)
        changes, warnings, touched = refresh_tree(tree, tables)
        self.assertEqual(
            tree.xpath('string(//li[@Class="CompProperties_Shearable"]/woolAmount)'),
            "128")
        self.assertEqual(
            tree.xpath('string(//li[@Class="CompProperties_Milkable"]/milkAmount)'),
            "128")
        self.assertEqual({c["new"] for c in changes}, {"128"})
        self.assertFalse(warnings)

    def test_full_eggmilk_tsv_can_be_loaded_independently(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'EggMilkWool.tsv'
            headers=['Name','Species','hatcherDaystoHatch','eggLayIntervalDays','eggCountRange',
                     'Def name','Incubation time real life (days)','Eggs/year','Clutch size','',
                     'Name','Food consumption (Nutrition/day)','Milk fraction','Milk yield','',
                     'Name','Food consumption (Nutrition/day)','Wool type','Wool fraction',
                     'Wool yield per day','Wool yield per 10 days']
            import csv
            with path.open('w',encoding='utf8',newline='') as f:
                w=csv.writer(f,delimiter='\t');w.writerow(headers)
                w.writerow(['Ostrich','',7,'18.81533101','6~12','<li>Ostrich</li>']+['']*15)
            frame=pd_egg_map(_read_repro(path,'EggMilkWool'))
            self.assertEqual(frame.iloc[0]['eggLayIntervalDays'],'18.81533101')
            self.assertEqual(frame.iloc[0]['Def name'],'<li>Ostrich</li>')

    def test_relocated_production_sections_ignore_extra_columns(self):
        headers = [
            'Name', 'Species', 'hatcherDaystoHatch', 'eggLayIntervalDays',
            'eggCountRange', 'Def name', 'Incubation time real life (days)',
            'Eggs/year', 'Clutch size', 'Nutrition (per ovum)',
            'Formation cost / clutch (N)', 'Gravid extra feed (N/d)', '',
            'Name', 'Food consumption (Nutrition/day)', 'Milk fraction', 'Milk yield', '',
            'Name', 'Food consumption (Nutrition/day)', 'Wool type', 'Wool fraction',
            'Wool yield per day', 'Wool yield per 10 days',
        ]
        values = ['Ostrich', '', 7, 18.81533101, '2~3', '<li>Ostrich</li>',
                  45, 28.7, 9, 2.29, 5.73, 0.61, '',
                  'Cow', 1.68, 0.55, 18, '',
                  'Dromedary', 1.68, 'Wool_Camel', 0.25, 8.5, 85]
        frame = pd.DataFrame([values], columns=headers)
        expected = pd_egg_map(frame).iloc[0].to_dict()
        self.assertEqual(expected['eggCountRange'], '2~3')
        self.assertEqual(expected['Milk animal'], 'Cow')
        self.assertEqual(expected['Milk yield'], 18)
        self.assertEqual(expected['Wool animal'], 'Dromedary')
        self.assertEqual(expected['Wool yield per day'], 8.5)
        # Moving complete blocks, or adding columns between blocks, is safe.
        moved = frame.iloc[:, list(range(18, 24)) + [17] + list(range(13, 17)) + [12] + list(range(12))]
        self.assertEqual(pd_egg_map(moved).iloc[0].to_dict(), expected)

    def test_missing_production_header_is_rejected(self):
        headers = ['Name', 'Species', 'hatcherDaystoHatch', 'eggLayIntervalDays',
                   'eggCountRange', 'Def name', 'Incubation time real life (days)',
                   'Eggs/year', 'Clutch size']
        with self.assertRaisesRegex(ValueError, 'unambiguous'):
            pd_egg_map(pd.DataFrame(columns=headers))

    def test_formula_error_prevents_invalid_xml_export(self):
        tables=_tables();tables['EggMilk'].loc[0,'eggLayIntervalDays']='#ERROR!'
        tree=_tree('''<Patch><Operation><xpath>/Defs/ThingDef[defName="Ostrich"]/comps/li[@Class="CompProperties_EggLayer"]/eggLayIntervalDays</xpath>
        <match><value><eggLayIntervalDays>15.6</eggLayIntervalDays></value></match></Operation></Patch>''')
        with self.assertRaisesRegex(ValueError,'Formula error'):
            refresh_tree(tree,tables)


if __name__=='__main__': unittest.main()
