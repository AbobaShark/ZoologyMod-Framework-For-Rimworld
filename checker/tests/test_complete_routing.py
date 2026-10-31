"""Audit every declared route and staging of all static generated categories."""
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import pandas as pd
from core.generation_registry import load_registry
from core.pipeline import stage_pipeline
from core.generation_registry import outputs_for
from core.xml_routing import route_dependency, validate_target, plan

CHECKER = Path(__file__).resolve().parents[1]

# Relevant LoadFolders.xml entries are copied from the GitHub main tree at
# 4eb6a84813257cabcdaaf4d58859f66733af00ae. Both versions currently
# have the same conditional paths; use the real names, not guessed placeholders.
RULES = (
    ('Core', None),
    ('DLC/Biotech', 'Ludeon.Rimworld.Biotech'),
    ('DLC/Royalty', 'Ludeon.RimWorld.Royalty'),
    ('DLC/Odyssey', 'Ludeon.Rimworld.Odyssey'),
    ('ModPatches/CombatExtended', 'CETeam.CombatExtended'),
    ('ModPatches/CombatExtended', 'CETeam.CombatExtended_copy'),
    ('ModPatches/VanillaAnimalsExpanded', 'VanillaExpanded.VanillaAnimalsExpanded'),
    ('ModPatches/VanillaAnimalsExpandedRoyal', 'VanillaExpanded.VAERoy'),
    ('ModPatches/VanillaAnimalsExpandedEndangered', 'VanillaExpanded.VAEEndAndExt'),
    ('ModPatches/VanillaAnimalsExpandedWasteland', 'VanillaExpanded.VAEWaste'),
    ('ModPatches/AlphaAnimals', 'sarg.alphaanimals'),
    ('ModPatches/Dinosauria', 'spincrus.dinosauria'),
    ('ModPatches/Megafauna', 'Spino.Megafauna'),
    ('ModPatches/AlphaBiomes', 'sarg.alphabiomes'),
)


def write_loadfolders(mod):
    from lxml import etree as E
    root = E.Element('loadFolders')
    for version in ('v1.6', 'v1.7'):
        item = E.SubElement(root, version)
        for folder, dependency in RULES:
            attrs = {'IfModActive': dependency} if dependency else {}
            E.SubElement(item, 'li', **attrs).text = folder
    (mod/'LoadFolders.xml').write_bytes(E.tostring(root))


class RouteAudit(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.mod = self.root/'Zoology'; self.mod.mkdir()
        write_loadfolders(self.mod)
        (self.root/'checker').mkdir()
        self.stage = self.root/'output'/'staged'

    def test_whole_registry_and_every_static_category(self):
        reg = load_registry(CHECKER/'generation.json', self.mod)
        self.assertEqual((len(reg['groups']), len(reg['biomes']), len(reg['shared_defs'])), (81, 29, 3))
        self.assertEqual(len(outputs_for(reg,'products')), 15)
        seen = set()
        category_paths = []
        category_paths.extend((x['destination'],'animal_patch',x.get('requires')) for x in reg['groups'])
        category_paths.extend((x['destination'],'biome_patch',x.get('requires')) for x in reg['biomes'])
        category_paths.extend((x['destination'],x['category'],x.get('requires')) for x in reg['shared_defs'])
        category_paths.extend((x['destination'],x['category'],x.get('requires')) for x in reg['outputs'])
        self.assertEqual(len(category_paths), 147)
        for dest, category, required in category_paths:
            with self.subTest(destination=dest):
                self.assertNotIn(dest.lower(), seen)
                seen.add(dest.lower())
                self.assertEqual(route_dependency(self.mod,dest,preferred=required),required)
                self.assertEqual(validate_target(self.mod,{'destination':dest,'category':category,'requires':required}).as_posix(),dest)
        self.assertEqual(len(seen), 147)

    def test_all_real_reproduction_paths_include_core_and_odyssey_hatchers(self):
        products=[e['destination'] for e in load_registry(CHECKER/'generation.json',self.mod)['outputs'] if e['pipeline_category']=='products']
        self.assertIn('Core/Patches/Core/Misc/CompProperties_Hatcher.xml', products)
        self.assertIn('DLC/Odyssey/Patches/Misc/CompProperties_Hatcher_Odyssey.xml', products)
        for path in products:
            with self.subTest(path=path):
                dependency = route_dependency(self.mod,path)
                if path.startswith('Core/'): self.assertIsNone(dependency)
                else: self.assertIsNotNone(dependency)

    def test_conditional_dependency_required_and_conflicts_detected(self):
        odyssey='DLC/Odyssey/Patches/Misc/CompProperties_EggLayer_Odyssey.xml'
        self.assertEqual(route_dependency(self.mod,odyssey),'Ludeon.Rimworld.Odyssey')
        with self.assertRaisesRegex(ValueError,'does not match LoadFolders'):
            route_dependency(self.mod,odyssey,preferred='VanillaExpanded.VAERoy')
        with self.assertRaisesRegex(ValueError,'unconditional'):
            route_dependency(self.mod,'Core/Patches/Core/Misc/example.xml',preferred='Ludeon.Rimworld.Odyssey')
        ce='ModPatches/CombatExtended/Patches/HediffDefs/BionicAnimalVariants_CE.xml'
        self.assertEqual(route_dependency(self.mod,ce),'CETeam.CombatExtended')

    def test_reproduction_stages_dependencies_and_can_be_previewed(self):
        targets = ['Core/Patches/Core/Misc/CompProperties_Milkable.xml',
                   'DLC/Odyssey/Patches/Misc/CompProperties_EggLayer_Odyssey.xml',
                   'ModPatches/VanillaAnimalsExpandedRoyal/Patches/Misc/CompProperties_EggLayer_VAER.xml']
        for dest in targets:
            f=self.mod/dest; f.parent.mkdir(parents=True,exist_ok=True)
            f.write_text('<Patch><Operation/></Patch>',encoding='utf8')
        with patch('core.pipeline.render_reproduction',return_value=([(d,b'<Patch><Operation Class="PatchOperationConditional"/></Patch>') for d in targets],[])):
            out=stage_pipeline('unused-source',root=self.root,registry_path=CHECKER/'generation.json',
                               output_dir=self.stage,categories=('products',))
        obj=json.loads(out.read_text(encoding='utf8'))
        self.assertEqual(len(obj['files']),3)
        for entry in obj['files']:
            self.assertEqual(entry['requires'],route_dependency(self.mod,entry['destination']))
        actions,_ = plan(out,self.root)
        self.assertEqual([a[0] for a in actions],['replace']*3)
        self.assertIn('Operation', (self.mod/targets[0]).read_text())  # staging did not deploy

    def test_static_categories_include_the_ce_outputs(self):
        names=['Core/Defs/HediffDefs/BionicAnimalVariants.xml',
               'Core/Patches/Core/LifeStageDefs/LifeStages.xml',
               'ModPatches/CombatExtended/Patches/LifeStageDefs/LifeStages_CE.xml']
        for dest in names:
            f=self.mod/dest; f.parent.mkdir(parents=True,exist_ok=True)
            f.write_text('<Defs/>' if '/Defs/' in dest else '<Patch/>',encoding='utf8')
        with patch('core.pipeline.read_table',return_value=pd.DataFrame()), \
             patch('core.pipeline.update_bionics',return_value=([],[])), \
             patch('core.pipeline.update_life_stages',return_value=([],[])), \
             patch('core.ce_bionics.render_ce_bionics',return_value=b'<Patch/>'):
            out=stage_pipeline('unused-source',root=self.root,registry_path=CHECKER/'generation.json',
                               output_dir=self.stage,categories=('bionics','life-stages'),ce=True)
        entries={x['destination']:x for x in json.loads(out.read_text())['files']}
        self.assertEqual(len(entries),4)
        for dest, entry in entries.items():
            self.assertEqual(entry['requires'],route_dependency(self.mod,dest))
        self.assertEqual(len(plan(out,self.root)[0]),4)

    def test_partial_generation_keeps_staged_reproduction_when_static_changed(self):
        product='DLC/Odyssey/Patches/Misc/CompProperties_EggLayer_Odyssey.xml'
        p=self.mod/product;p.parent.mkdir(parents=True,exist_ok=True);p.write_text('<Patch/>')
        bionic='Core/Defs/HediffDefs/BionicAnimalVariants.xml'
        b=self.mod/bionic;b.parent.mkdir(parents=True,exist_ok=True);b.write_text('<Defs/>')
        with patch('core.pipeline.render_reproduction',return_value=([(product,b'<Patch><Operation/></Patch>')],[])):
            stage_pipeline('unused-source',root=self.root,registry_path=CHECKER/'generation.json',
                           output_dir=self.stage,categories=('products',))
        with patch('core.pipeline.read_table',return_value=pd.DataFrame()), \
             patch('core.pipeline.update_bionics',return_value=([],[])):
            out=stage_pipeline('unused-source',root=self.root,registry_path=CHECKER/'generation.json',
                               output_dir=self.stage,categories=('bionics',),ce=False)
        entries={x['destination']:x for x in json.loads(out.read_text())['files']}
        self.assertEqual(len(entries),2)
        self.assertEqual(entries[product]['requires'],'Ludeon.Rimworld.Odyssey')
        self.assertEqual(entries[bionic]['requires'],None)
        self.assertEqual(len(plan(out,self.root)[0]),2)


if __name__ == '__main__': unittest.main()

class RoutingAuditExtensions(unittest.TestCase):
    def test_aggregate_audit_catches_missing_static_files(self):
        from core.generation_registry import audit_routes
        with tempfile.TemporaryDirectory() as tmp:
            mod=Path(tmp)/'Zoology';mod.mkdir();write_loadfolders(mod)
            report=audit_routes(CHECKER/'generation.json',mod)
            self.assertEqual(report['destinations'],147)
            self.assertEqual(len(report['missing_static']),18)
            with self.assertRaisesRegex(ValueError,'Missing installed static XML'):
                audit_routes(CHECKER/'generation.json',mod,require_static_sources=True)

    def test_old_staging_without_dependency_is_repaired_on_next_stage(self):
        from core.pipeline import stage_pipeline
        from core.xml_routing import plan
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);mod=root/'Zoology';mod.mkdir();write_loadfolders(mod)
            target='DLC/Odyssey/Patches/Misc/CompProperties_Hatcher_Odyssey.xml'
            f=mod/target;f.parent.mkdir(parents=True);f.write_text('<Patch/>')
            stage=root/'staging'
            with patch('core.pipeline.render_reproduction',return_value=([(target,b'<Patch><li/></Patch>')],[])):
                staged=stage_pipeline('dummy',root=root,registry_path=CHECKER/'generation.json',
                        output_dir=stage,categories=('products',))
            content=json.loads(staged.read_text());content['files'][0]['requires']=None
            staged.write_text(json.dumps(content))
            # Regenerate a disjoint category; the old staged patch is retained
            # but its conditional LoadFolders requirement is revalidated.
            bionic='Core/Defs/HediffDefs/BionicAnimalVariants.xml';bf=mod/bionic
            bf.parent.mkdir(parents=True);bf.write_text('<Defs/>')
            with patch('core.pipeline.read_table',return_value=pd.DataFrame()), \
                 patch('core.pipeline.update_bionics',return_value=([],[])):
                staged=stage_pipeline('dummy',root=root,registry_path=CHECKER/'generation.json',
                        output_dir=stage,categories=('bionics',),ce=False)
            after=json.loads(staged.read_text())
            self.assertEqual(next(x for x in after['files'] if x['destination']==target)['requires'],
                             'Ludeon.Rimworld.Odyssey')
            self.assertEqual(len(plan(staged,root)[0]),2)

    def test_cached_excel_tabs_are_revision_aware(self):
        from core.animalstats_source import read_excel_cached
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'example.xlsx'
            pd.DataFrame({'Value':['first']}).to_excel(p,index=False,sheet_name='Animals')
            self.assertEqual(read_excel_cached(p,sheet_name='Animals').iloc[0]['Value'],'first')
            pd.DataFrame({'Value':['second']}).to_excel(p,index=False,sheet_name='Animals')
            self.assertEqual(read_excel_cached(p,sheet_name='Animals').iloc[0]['Value'],'second')
