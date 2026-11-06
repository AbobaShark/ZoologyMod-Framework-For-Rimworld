import json
import tempfile
import unittest
from pathlib import Path
from core.xml_routing import deploy


class RoutingTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name); self.mod = self.root/'Zoology'; self.mod.mkdir()
        (self.mod/'LoadFolders.xml').write_text('<loadFolders><v1.6><li>Core</li><li IfModActive="DLC">DLC/X</li><li IfModActive="CE">ModPatches/CombatExtended</li><li IfModActive="Other.Mod">ModPatches/Other</li></v1.6><v1.7><li>Core</li><li IfModActive="DLC">DLC/X</li><li IfModActive="CE">ModPatches/CombatExtended</li><li IfModActive="Other.Mod">ModPatches/Other</li></v1.7></loadFolders>')
        self.staging = self.root/'staging'; self.staging.mkdir(); self.manifest = self.staging/'manifest.json'
        (self.staging/'patch.xml').write_text('<Patch/>')

    def manifest_for(self, destination, category='animal_patch', dependency=None, **extra):
        entry = {'source': 'patch.xml', 'destination': destination, 'category': category, 'requires': dependency, **extra}
        self.manifest.write_text(json.dumps({'schema': 1, 'files': [entry]}))

    def test_current_run_only_never_redeploys_retained_older_xml(self):
        old=self.mod/'Core/Patches/Animals/old.xml'
        old.parent.mkdir(parents=True)
        old.write_text('<Patch><manual/></Patch>',encoding='utf8')
        (self.staging/'new.xml').write_text('<Patch><new/></Patch>',encoding='utf8')
        self.manifest.write_text(json.dumps({
            'schema':1,'prune':False,'generated_this_run':['Core/Patches/Animals/new.xml'],
            'files':[
                {'source':'patch.xml','destination':'Core/Patches/Animals/old.xml','category':'animal_patch'},
                {'source':'new.xml','destination':'Core/Patches/Animals/new.xml','category':'animal_patch'},
            ],
        }))
        expected,_=__import__('core.xml_routing',fromlist=['plan']).plan(
            self.manifest,self.root,current_run_only=True)
        self.assertEqual([(kind,key) for kind,_,_,key in expected],
                         [('copy','Core/Patches/Animals/new.xml')])
        deploy(self.manifest,self.root,current_run_only=True)
        self.assertIn('manual',old.read_text())
        self.assertIn('new',(self.mod/'Core/Patches/Animals/new.xml').read_text())
        self.assertEqual(deploy(self.manifest,self.root,current_run_only=True)[0][0],'skip')

    def test_current_run_only_rejects_unmarked_old_manifests(self):
        self.manifest_for('Core/Patches/Animals/old.xml')
        with self.assertRaisesRegex(ValueError,'generated_this_run'):
            deploy(self.manifest,self.root,current_run_only=True)
        self.assertFalse((self.mod/'Core/Patches/Animals/old.xml').exists())

    def test_current_run_only_cannot_hide_bad_destinations(self):
        self.manifest.write_text(json.dumps({
            'schema':1,'generated_this_run':['Core/Patches/not-listed.xml'],'files':[]}))
        with self.assertRaisesRegex(ValueError,'undeclared destination'):
            deploy(self.manifest,self.root,current_run_only=True)

    def test_dry_run_and_repeat_and_owned_stale_cleanup(self):
        target = self.mod/'Core/Patches/Animals/a.xml'
        self.manifest_for('Core/Patches/Animals/a.xml')
        before = set(self.root.rglob('*'))
        deploy(self.manifest, self.root, dry_run=True)
        self.assertEqual(set(self.root.rglob('*')), before)
        deploy(self.manifest, self.root); self.assertTrue(target.exists())
        self.assertEqual(deploy(self.manifest, self.root)[0][0], 'skip')
        self.manifest.write_text(json.dumps({'schema': 1, 'prune': True, 'files': []}))
        dry = deploy(self.manifest, self.root, dry_run=True)
        self.assertTrue(any(action == 'rmdir' for action, *_ in dry))
        self.assertTrue(target.exists())
        deploy(self.manifest, self.root); self.assertFalse(target.exists())
        self.assertFalse(target.parent.exists())

    def test_existing_file_can_be_replaced_without_adoption(self):
        target = self.mod/'Core/Patches/Animals/a.xml'; target.parent.mkdir(parents=True)
        target.write_text('<Patch><manual/></Patch>')
        self.manifest_for('Core/Patches/Animals/a.xml')
        self.assertEqual(deploy(self.manifest, self.root, dry_run=True)[0][0], 'replace')
        self.assertIn('manual', target.read_text())  # Preview must not write.
        deploy(self.manifest, self.root)
        self.assertEqual(target.read_text(), '<Patch/>')
        self.manifest_for('DLC/X/Patches/Animals/a.xml', dependency='wrong')
        with self.assertRaisesRegex(ValueError, 'requires'): deploy(self.manifest, self.root)

    def test_externally_modified_installed_xml_is_replaced(self):
        self.manifest_for('Core/Patches/a.xml')
        deploy(self.manifest, self.root)
        target = self.mod/'Core/Patches/a.xml'
        target.write_text('<Patch><manual/></Patch>')
        self.assertEqual(deploy(self.manifest, self.root, dry_run=True)[0][0], 'replace')
        self.assertIn('manual', target.read_text())
        deploy(self.manifest, self.root)
        self.assertEqual(target.read_text(), '<Patch/>')

    def test_ce_and_unknown_categories(self):
        self.manifest_for('ModPatches/CombatExtended/Patches/a.xml', 'ce_patch', 'CE')
        deploy(self.manifest, self.root)
        self.manifest_for('Core/Patches/a.xml', 'unknown')
        with self.assertRaisesRegex(ValueError, 'Unknown'): deploy(self.manifest, self.root)

    def test_dlc_third_party_biome_and_common_def_routes(self):
        for destination, category, dependency in [
            ('DLC/X/Patches/Races/a.xml', 'animal_patch', 'DLC'),
            ('ModPatches/Other/Patches/Biomes/a.xml', 'biome_patch', 'Other.Mod'),
            ('ModPatches/CombatExtended/Patches/a.xml', 'ce_patch', 'CE')]:
            self.manifest_for(destination, category, dependency)
            self.assertEqual(deploy(self.manifest, self.root, dry_run=True)[0][0], 'copy')
        (self.staging/'patch.xml').write_text('<Defs><ThingDef><defName>SharedVenom</defName></ThingDef></Defs>')
        self.manifest_for('Core/Defs/ThingDefs/shared.xml', 'common_defs')
        self.assertEqual(deploy(self.manifest, self.root, dry_run=True)[0][0], 'copy')
        self.manifest_for('ModPatches/CombatExtended/Defs/Things/a.xml', 'ce_defs', 'CE')
        self.assertEqual(deploy(self.manifest, self.root, dry_run=True)[0][0], 'copy')

    def test_conflicting_destinations_and_modified_owned_file(self):
        self.manifest_for('Core/Patches/a.xml')
        data = json.loads(self.manifest.read_text()); data['files'].append(dict(data['files'][0]))
        self.manifest.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'Duplicate destination'): deploy(self.manifest, self.root)
        self.manifest_for('Core/Patches/a.xml'); deploy(self.manifest, self.root)
        target = self.mod/'Core/Patches/a.xml'; target.write_text('<Patch><manual/></Patch>')
        self.manifest.write_text(json.dumps({'schema': 1, 'prune': True, 'files': []}))
        with self.assertRaisesRegex(ValueError, 'modified'): deploy(self.manifest, self.root)
        self.assertIn('manual', target.read_text())

    def test_generated_attack_source_is_atomically_deployed_at_one_allowed_path(self):
        from core.xml_routing import plan
        source = self.staging/'attacks.cs'
        source.write_text(
            '// <auto-generated> Checker / AnimalStats LifeStageAttackProfiles\\n'
            'namespace ZoologyMod { internal static class '
            'ZoologyGeneratedLifeStageAttacks { } }\\n', encoding='utf8')
        target = 'Source/Behaviour/Shared/ZoologyLifeStageAttackProfiles_Generated.cs'
        self.manifest.write_text(json.dumps({
            'schema': 1,
            'generated_this_run': [target],
            'files': [{'source': 'attacks.cs', 'destination': target,
                       'category': 'source_code', 'requires': None}],
        }))
        tasks, _ = plan(self.manifest, self.root, current_run_only=True)
        self.assertEqual(tasks[0][0], 'copy')
        deploy(self.manifest, self.root, current_run_only=True)
        self.assertIn('auto-generated', (self.mod/target).read_text())
        tasks, _ = plan(self.manifest, self.root, current_run_only=True)
        self.assertEqual(tasks[0][0], 'skip')

    def test_generated_attack_source_cannot_escape_approved_cs_destination(self):
        from core.xml_routing import plan
        (self.staging/'attacks.cs').write_text(
            '// <auto-generated> Checker / AnimalStats LifeStageAttackProfiles\\n'
            'namespace ZoologyMod { internal static class '
            'ZoologyGeneratedLifeStageAttacks {} }')
        self.manifest.write_text(json.dumps({
            'schema': 1,
            'files': [{'source': 'attacks.cs',
                       'destination': 'Source/Other.cs',
                       'category': 'source_code'}],
        }))
        with self.assertRaisesRegex(ValueError, 'Unapproved source-code output'):
            plan(self.manifest, self.root)
