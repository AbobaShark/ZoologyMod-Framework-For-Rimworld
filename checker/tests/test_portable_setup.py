import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import pandas as pd
from lxml import etree as E
import core.checker_config as checker_config
from core.static_stats import update_bionics, update_life_stages
from core.xml_routing import plan


class PortableSettingsTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.patch_root=patch.object(checker_config,'ROOT',self.root)
        self.patch_root.start();self.addCleanup(self.patch_root.stop)
        for name in checker_config.SECTIONS.values():
            (self.root/name).write_text('{}',encoding='utf8')

    def test_local_overrides_do_not_edit_shared_defaults(self):
        default=self.root/'rimworld_patch_generator_config.json'
        default.write_text(json.dumps({'generation_config':'generation.json','enable_ce':True}),encoding='utf8')
        original=default.read_bytes()
        cfg=checker_config.load('patch_generator')
        self.assertEqual(cfg['generation_config'],str(self.root/'generation.json'))
        cfg['vanilla_tsv']=str(self.root/'animal.xlsx')
        checker_config.save('patch_generator',cfg)
        self.assertEqual(default.read_bytes(),original)
        values=json.loads((self.root/'checker.local.json').read_text())['patch_generator']
        self.assertEqual(values['vanilla_tsv'],'animal.xlsx')
        self.assertNotIn('generation_config',values)
        self.assertEqual(checker_config.load('patch_generator')['vanilla_tsv'],str(self.root/'animal.xlsx'))

    def test_multiple_tools_share_local_file_without_overwriting(self):
        checker_config.save('patch_generator',{'ce_tsv':'https://docs.google.com/test'})
        checker_config.save('xml_generator',{'game_root_dir':'/tmp/gameroot'})
        saved=json.loads((self.root/'checker.local.json').read_text())
        self.assertEqual(set(saved),{'patch_generator','xml_generator'})
        self.assertEqual(saved['patch_generator']['ce_tsv'],'https://docs.google.com/test')

    def test_reject_corrupt_user_config_instead_of_clobber(self):
        (self.root/'checker.local.json').write_text('{bad json')
        with self.assertRaisesRegex(ValueError,'Invalid configuration'):
            checker_config.load('patch_generator')

class DeploymentHashTests(unittest.TestCase):
    def test_declared_source_hash_must_match(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);mod=root/'Zoology';mod.mkdir()
            (mod/'LoadFolders.xml').write_text('<loadFolders><v1.7><li>Core</li></v1.7></loadFolders>')
            stage=root/'stage';stage.mkdir();(stage/'f.xml').write_text('<Patch/>')
            manifest=stage/'manifest.json'
            manifest.write_text(json.dumps({'schema':1,'files':[{'source':'f.xml','destination':'Core/Patches/f.xml',
                          'category':'animal_patch','sha256':'0'*64}]}))
            with self.assertRaisesRegex(ValueError,'checksum mismatch'):
                plan(manifest,root)
            self.assertFalse((mod/'Core/Patches/f.xml').exists())

class StaticStatsTests(unittest.TestCase):
    def test_bionics_are_updated_from_sheet_preserving_other_xml(self):
        tree=E.ElementTree(E.fromstring('''<Defs><HediffDef><defName>BionicArm_Animal_Medium</defName>
        <comps><li><tools><li><power>10</power><cooldownTime>1</cooldownTime><label>fist</label></li></tools></li></comps>
        </HediffDef></Defs>'''))
        df=pd.DataFrame([{'Weapon':'BionicArm_Medium','Damage':'10.5','Attack interval':'1.05'}])
        changes,warnings=update_bionics(tree,df)
        self.assertEqual(len(changes),2)
        self.assertEqual(warnings,[])
        self.assertEqual(tree.findtext('.//power'),'10.5')
        self.assertEqual(tree.findtext('.//label'),'fist')

    def test_missing_sheet_value_keeps_xml_and_warns(self):
        tree=E.ElementTree(E.fromstring('''<Defs><HediffDef><defName>BionicArm_Animal_VerySmall</defName>
        <comps><li><tools><li><power>3</power><cooldownTime>0.6</cooldownTime></li></tools></li></comps>
        </HediffDef></Defs>'''))
        df=pd.DataFrame([{'Weapon':'BionicArm_VerySmall','Damage':'','Attack interval':''}])
        changes,warnings=update_bionics(tree,df)
        self.assertFalse(changes)
        self.assertEqual(len(warnings),2)

    def test_life_stage_vanilla_changes_but_ce_only_uses_ce_column(self):
        xml='''<Patch><Operation Class="PatchOperationConditional">
        <xpath>/Defs/LifeStageDef[defName="AnimalJuvenile" or defName="EusocialInsectJuvenile"]/meleeDamageFactor</xpath>
        <match Class="PatchOperationReplace"><xpath>unused</xpath><value><meleeDamageFactor>0.5</meleeDamageFactor></value></match>
        <nomatch Class="PatchOperationAdd"><xpath>unused</xpath><value><meleeDamageFactor>0.5</meleeDamageFactor></value></nomatch>
        </Operation></Patch>'''
        df=pd.DataFrame([{'Life stage':'AnimalJuvenile or EusocialInsectJuvenile',
                 'meleeDamageFactor':'0.65','meleeDamageFactor(CE)':'0.45'}])
        vanilla=E.ElementTree(E.fromstring(xml)); ce=E.ElementTree(E.fromstring(xml))
        update_life_stages(vanilla,df)
        update_life_stages(ce,df,ce=True)
        self.assertEqual([x.text for x in vanilla.xpath('//value/meleeDamageFactor')],['0.65','0.65'])
        self.assertEqual([x.text for x in ce.xpath('//value/meleeDamageFactor')],['0.45','0.45'])

if __name__=='__main__': unittest.main()
