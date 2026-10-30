"""Regression checks for all destinations, safe optimization, and reference refresh."""
import json
import tempfile
import unittest
from pathlib import Path
from lxml import etree as E

from core.generation_registry import load_registry, audit_routes, output_by_id
from core.patch_postprocess import PatchPostprocessor
from core.rimworld_original_xml import OriginalXmlIndex
from tools.reference_sync import compare,apply
from tests.test_complete_routing import write_loadfolders

CHECKER=Path(__file__).resolve().parents[1]


class RegistryTests(unittest.TestCase):
    def test_all_134_routes_live_in_one_json_and_match_loadfolders(self):
        with tempfile.TemporaryDirectory() as folder:
            mod=Path(folder)/'Zoology';mod.mkdir();write_loadfolders(mod)
            r=load_registry(CHECKER/'generation.json',mod)
            self.assertEqual(len(r['outputs']),19)
            self.assertEqual([len(r[k]) for k in ('groups','biomes','shared_defs','outputs')],[81,31,3,19])
            self.assertEqual(audit_routes(CHECKER/'generation.json',mod)['destinations'],134)
            self.assertEqual(output_by_id(r,'bionics.ce')['requires'],'CETeam.CombatExtended')
            self.assertEqual(next(x for x in r['outputs'] if 'EggLayer_Odyssey' in x['destination'])['requires'],'Ludeon.Rimworld.Odyssey')

    def test_misrouted_output_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            mod=Path(folder)/'Zoology';mod.mkdir();write_loadfolders(mod)
            config=json.loads((CHECKER/'generation.json').read_text())
            config['outputs'][3]['requires']=None
            p=Path(folder)/'bad.json';p.write_text(json.dumps(config))
            with self.assertRaisesRegex(ValueError,'LoadFolders requires'):load_registry(p,mod)


class PostprocessorTests(unittest.TestCase):
    def test_optimization_can_use_lifestage_reference(self):
        with tempfile.TemporaryDirectory() as tmp:
            src=Path(tmp)/'OriginalXML';src.mkdir()
            (src/'life.xml').write_text('<Defs><LifeStageDef><defName>AnimalBaby</defName><bodySizeFactor>0.5</bodySizeFactor></LifeStageDef></Defs>')
            payload=b'''<Patch><Operation Class="PatchOperationReplace"><xpath>/Defs/LifeStageDef[defName="AnimalBaby"]/bodySizeFactor</xpath><value><bodySizeFactor>0.5</bodySizeFactor></value></Operation></Patch>'''
            post=PatchPostprocessor(original_xml=src,original_patches=Path(tmp)/'OriginalPatches')
            result=post.process(payload,destination='Core/Patches/Core/LifeStageDefs/LifeStages.xml')
            self.assertEqual(E.fromstring(result).tag,'Patch')
            self.assertEqual(len(E.fromstring(result)),0)

    def test_missing_lifestage_reference_keeps_operation(self):
        with tempfile.TemporaryDirectory() as tmp:
            src=Path(tmp)/'OriginalXML';src.mkdir()
            (src/'unrelated.xml').write_text('<Defs><ThingDef><defName>Horse</defName></ThingDef></Defs>')
            payload=b'''<Patch><Operation Class="PatchOperationReplace"><xpath>/Defs/LifeStageDef[defName="AnimalBaby"]/bodySizeFactor</xpath><value><bodySizeFactor>0.5</bodySizeFactor></value></Operation></Patch>'''
            post=PatchPostprocessor(original_xml=src,original_patches=Path(tmp)/'OriginalPatches')
            self.assertEqual(post.process(payload,destination='Core/Patches/Core/LifeStageDefs/LifeStages.xml'),payload)

    def test_defs_never_pass_through_patch_optimizer(self):
        payload=b'<Defs><HediffDef><defName>BionicArm_Animal_Medium</defName></HediffDef></Defs>'
        with tempfile.TemporaryDirectory() as tmp:
            post=PatchPostprocessor(original_xml=tmp,original_patches=tmp)
            self.assertEqual(post.process(payload,destination='a.xml'),payload)


class ReferenceSyncTests(unittest.TestCase):
    def test_local_preview_apply_backup(self):
        with tempfile.TemporaryDirectory() as tmp:
            base=Path(tmp); checker=base/'checker';checker.mkdir();(checker/'OriginalXML/Core').mkdir(parents=True)
            upstream=base/'game';upstream.mkdir();(upstream/'animals.xml').write_text('<Defs><ThingDef/></Defs>')
            dest=checker/'OriginalXML/Core/animals.xml';dest.write_text('<Defs/>')
            cfg=base/'sources.json';cfg.write_text(json.dumps({'schema':1,'sources':[{'id':'core','type':'local','directory':str(upstream),
                'files':[{'from':'animals.xml','to':'OriginalXML/Core/animals.xml'}]}]}))
            items,guard=compare(cfg,checker)
            try:
                self.assertEqual(items[0]['status'],'REPLACE')
                self.assertEqual(dest.read_text(),'<Defs/>')
                backup=apply(items,checker)
                self.assertEqual(dest.read_text(),'<Defs><ThingDef/></Defs>')
                self.assertEqual((backup/'OriginalXML/Core/animals.xml').read_text(),'<Defs/>')
            finally:guard.cleanup()

    def test_refuses_symlink_or_path_traversal(self):
        with tempfile.TemporaryDirectory() as tmp:
            base=Path(tmp); checker=base/'checker';checker.mkdir();src=base/'source';src.mkdir()
            (src/'a.xml').write_text('<Defs/>')
            cfg=base/'sources.json';cfg.write_text(json.dumps({'schema':1,'sources':[{'id':'bad','type':'local','directory':str(src),
                'files':[{'from':'a.xml','to':'OriginalXML/../../outside.xml'}]}]}))
            with self.assertRaises(ValueError):compare(cfg,checker)

    def test_github_needs_explicit_fetch(self):
        with tempfile.TemporaryDirectory() as tmp:
            base=Path(tmp);checker=base/'checker';checker.mkdir()
            cfg=base/'sources.json';cfg.write_text(json.dumps({'schema':1,'sources':[{'id':'remote','type':'github',
                'url':'https://github.com/OWNER/REPOSITORY.git','commit':'a'*40,
                'files':[{'from':'Defs/a.xml','to':'OriginalXML/Core/a.xml'}]}]}))
            with self.assertRaisesRegex(ValueError,'--fetch'):compare(cfg,checker)


if __name__=='__main__':unittest.main()
