"""Regression checks for all destinations, safe optimization, and reference refresh."""
import json
import tempfile
import unittest
from pathlib import Path
from lxml import etree as E

from core.generation_registry import load_registry, audit_routes, output_by_id
from core.patch_postprocess import PatchPostprocessor
from core.rimworld_original_xml import OriginalXmlIndex
from tests.test_complete_routing import write_loadfolders

CHECKER=Path(__file__).resolve().parents[1]


class RegistryTests(unittest.TestCase):
    def test_all_registered_routes_live_in_one_json_and_match_loadfolders(self):
        with tempfile.TemporaryDirectory() as folder:
            mod=Path(folder)/'Zoology';mod.mkdir();write_loadfolders(mod)
            r=load_registry(CHECKER/'generation.json',mod)
            self.assertEqual(len(r['outputs']),34)
            self.assertEqual([len(r[k]) for k in ('groups','biomes','shared_defs','outputs')],[81,29,3,34])
            self.assertEqual(audit_routes(CHECKER/'generation.json',mod)['destinations'],147)
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
            post=PatchPostprocessor(original_xml=src,original_patches=Path(tmp)/'OriginalXML/CEPatches')
            result=post.process(payload,destination='Core/Patches/Core/LifeStageDefs/LifeStages.xml')
            self.assertEqual(E.fromstring(result).tag,'Patch')
            self.assertEqual(len(E.fromstring(result)),0)

    def test_missing_lifestage_reference_keeps_operation(self):
        with tempfile.TemporaryDirectory() as tmp:
            src=Path(tmp)/'OriginalXML';src.mkdir()
            (src/'unrelated.xml').write_text('<Defs><ThingDef><defName>Horse</defName></ThingDef></Defs>')
            payload=b'''<Patch><Operation Class="PatchOperationReplace"><xpath>/Defs/LifeStageDef[defName="AnimalBaby"]/bodySizeFactor</xpath><value><bodySizeFactor>0.5</bodySizeFactor></value></Operation></Patch>'''
            post=PatchPostprocessor(original_xml=src,original_patches=Path(tmp)/'OriginalXML/CEPatches')
            self.assertEqual(post.process(payload,destination='Core/Patches/Core/LifeStageDefs/LifeStages.xml'),payload)

    def test_defs_never_pass_through_patch_optimizer(self):
        payload=b'<Defs><HediffDef><defName>BionicArm_Animal_Medium</defName></HediffDef></Defs>'
        with tempfile.TemporaryDirectory() as tmp:
            post=PatchPostprocessor(original_xml=tmp,original_patches=tmp)
            self.assertEqual(post.process(payload,destination='a.xml'),payload)


if __name__=='__main__':unittest.main()
