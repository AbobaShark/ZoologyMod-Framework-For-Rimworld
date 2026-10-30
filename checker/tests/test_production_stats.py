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
        {'F':'<li>Ostrich</li>','C':'7','D':'18.81533101','E':'6~12',
         'K':'Cow','N':'22','P':'Dromedary','T':'9'},
        {'F':'<li>AEXP_Crocodile</li>','C':'17','D':'60','E':'35~65'},
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
            self.assertEqual(frame.iloc[0]['D'],'18.81533101')
            self.assertEqual(frame.iloc[0]['F'],'<li>Ostrich</li>')

    def test_formula_error_prevents_invalid_xml_export(self):
        tables=_tables();tables['EggMilk'].loc[0,'D']='#ERROR!'
        tree=_tree('''<Patch><Operation><xpath>/Defs/ThingDef[defName="Ostrich"]/comps/li[@Class="CompProperties_EggLayer"]/eggLayIntervalDays</xpath>
        <match><value><eggLayIntervalDays>15.6</eggLayIntervalDays></value></match></Operation></Patch>''')
        with self.assertRaisesRegex(ValueError,'Formula error'):
            refresh_tree(tree,tables)


if __name__=='__main__': unittest.main()
