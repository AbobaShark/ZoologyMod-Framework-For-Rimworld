"""Run from checker/: py -m unittest discover -s tests -p test_reference_sync_auto.py"""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from tools import reference_sync_auto as rs


class AutoReferenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.checker = self.root / 'checker'
        self.checker.mkdir()
        self.steam = self.root / 'SteamLibrary'
        self.game = self.steam / 'steamapps' / 'common' / 'RimWorld'
        (self.game / 'Data' / 'Core' / 'Defs').mkdir(parents=True)
        self.workshop = self.steam / 'steamapps' / 'workshop' / 'content' / '294100'
        self.workshop.mkdir(parents=True)

    def source(self, rel, text, root=None):
        p = (root or self.checker) / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding='utf8')
        return p

    def spec(self, id='Core', root='OriginalXML/Core', **kw):
        return {'id':id, 'reference_dirs':[root], **kw}

    def test_steam_root_and_game_core(self):
        old = '<Defs><ThingDef><defName>Chicken</defName><a>old</a></ThingDef></Defs>'
        new = '<Defs><ThingDef><defName>Chicken</defName><a>new</a></ThingDef></Defs>'
        p = self.source('OriginalXML/Core/Races_Animal_Birds.xml', old)
        self.source('Defs/ThingDefs_Races/Races_Animal_Birds.xml', new, self.game/'Data'/'Core')
        cfg = {'rimworld_version':'1.7','mods':[self.spec(game_dlc='Core')]}
        result, warnings = rs.discover(cfg, steam=str(self.steam),github=False,checker_root=self.checker)
        self.assertEqual([x['status'] for x in result], ['REPLACE'])
        self.assertEqual(p.read_text(), old)  # preview must be read-only
        backup = rs.apply(result, checker_root=self.checker)
        self.assertEqual(p.read_text(), new)
        self.assertEqual((backup/'OriginalXML/Core/Races_Animal_Birds.xml').read_text(), old)
        self.assertTrue((backup/'report.json').is_file())

    def test_untracked_material_reference_is_imported_without_missing(self):
        # The legacy reference corpus has no material XML yet. MISSING 0
        # does not mean the new leather snapshot has been imported.
        original = '<Defs><ThingDef ParentName="LeatherBase">' \
                   '<defName>Leather_Thrumbo</defName></ThingDef></Defs>'
        self.source('OriginalXML/Core/Races_Animal_Birds.xml',
                    '<Defs><ThingDef><defName>Chicken</defName></ThingDef></Defs>')
        self.source('Defs/ThingDefs_Races/Races_Animal_Birds.xml',
                    '<Defs><ThingDef><defName>Chicken</defName></ThingDef></Defs>',
                    self.game/'Data'/'Core')
        self.source('Defs/ThingDefs_Items/Items_Resource_Stuff_Leather.xml',
                    original, self.game/'Data'/'Core')
        cfg = {'rimworld_version':'1.7','mods':[self.spec(game_dlc='Core')]}
        preview, warnings = rs.discover(cfg, steam=str(self.steam),
                                        github=False, checker_root=self.checker)
        imported = [x for x in preview if '/_Leathers/' in x['destination']]
        self.assertEqual(len(imported), 1)
        self.assertEqual(imported[0]['status'], 'COPY')
        self.assertFalse(any(x['status']=='MISSING' for x in preview))
        self.assertFalse((self.checker / imported[0]['destination']).exists())
        rs.apply(preview, checker_root=self.checker)
        self.assertEqual((self.checker / imported[0]['destination']).read_text(), original)
        again, _ = rs.discover(cfg, steam=str(self.steam),
                               github=False, checker_root=self.checker)
        self.assertEqual(next(x['status'] for x in again
                              if '/_Leathers/' in x['destination']), 'SKIP')

    def test_new_source_group_without_existing_targets_can_import_materials(self):
        root = self.workshop / '4321'
        self.source('1.7/Defs/ThingDefs_Items/Items_Resource_Stuff_Leather.xml',
                    '<Defs><ThingDef ParentName="LeatherBase">'
                    '<defName>Leather_Night</defName></ThingDef></Defs>', root)
        cfg = {'rimworld_version':'1.7','mods':[
            self.spec('AlphaAnimals','OriginalXML/AlphaAnimals',
                      workshop_ids=['4321'])]}
        preview, warnings = rs.discover(cfg, steam=str(self.steam),
                                        github=False, checker_root=self.checker)
        self.assertEqual(len(preview), 1)
        self.assertEqual(preview[0]['status'], 'COPY')
        self.assertIn('/_Leathers/', preview[0]['destination'])
        rs.apply(preview, checker_root=self.checker)
        self.assertTrue((self.checker / preview[0]['destination']).is_file())

    def test_workshop_id_without_mod_name(self):
        target = '<Defs><ThingDef><defName>Wolf</defName></ThingDef></Defs>'
        changed = '<Defs><ThingDef><defName>Wolf</defName><statBases/></ThingDef></Defs>'
        self.source('OriginalXML/Example/Races_Wolf.xml', target)
        self.source('Defs/Races_Wolf.xml', changed, self.workshop/'4321')
        cfg = {'rimworld_version':'1.7','mods':[self.spec('Example','OriginalXML/Example',workshop_ids=['4321'])]}
        result, _ = rs.discover(cfg,steam=str(self.workshop),github=False,checker_root=self.checker)
        self.assertEqual(result[0]['status'], 'REPLACE')
        self.assertEqual(result[0]['source_file'], 'Defs/Races_Wolf.xml')

    def test_no_matching_def_skip_not_overwrite(self):
        self.source('OriginalXML/Core/Races_Cat.xml','<Defs><ThingDef><defName>Cat</defName></ThingDef></Defs>')
        self.source('Defs/Races_Cat.xml','<Defs><ThingDef><defName>Steel</defName></ThingDef></Defs>', self.game/'Data'/'Core')
        cfg={'mods':[self.spec(game_dlc='Core')]}
        result,_=rs.discover(cfg,steam=str(self.game),github=False,checker_root=self.checker)
        self.assertEqual(result[0]['status'],'MISSING')

    def test_ambiguous_file_skip(self):
        self.source('OriginalXML/Core/Races_Wolf.xml','<Defs><ThingDef><defName>Wolf</defName></ThingDef></Defs>')
        self.source('Defs/One/Races_Wolf.xml','<Defs><ThingDef><defName>Wolf</defName><a>one</a></ThingDef></Defs>',self.game/'Data'/'Core')
        self.source('Defs/Two/Races_Wolf.xml','<Defs><ThingDef><defName>Wolf</defName><a>two</a></ThingDef></Defs>',self.game/'Data'/'Core')
        cfg={'mods':[self.spec(game_dlc='Core')]}
        result,_=rs.discover(cfg,steam=str(self.game),github=False,checker_root=self.checker)
        self.assertEqual(result[0]['status'],'MISSING')
        self.assertIn('Ambiguous',result[0]['detail'])

    def test_git_preference(self):
        old='<Defs><ThingDef><defName>Wolf</defName></ThingDef></Defs>'
        self.source('OriginalXML/Example/Races_Wolf.xml',old)
        self.source('Defs/Races_Wolf.xml','<Defs><ThingDef><defName>Wolf</defName><a>local</a></ThingDef></Defs>',self.workshop/'4321')
        cfg={'mods':[self.spec('Example','OriginalXML/Example', workshop_ids=['4321'],github={'url':'https://github.com/owner/repo.git'})]}
        class FakeGit:
            files=['Defs/Races_Wolf.xml']
            provenance='https://github.com/owner/repo.git@'+'a'*40
            def __init__(self, spec): pass
            def read(self, rel):return b'<Defs><ThingDef><defName>Wolf</defName><a>github</a></ThingDef></Defs>'
            def close(self):pass
        with patch.object(rs,'GitSource',FakeGit):
            result,_=rs.discover(cfg,steam=str(self.workshop), github=True,checker_root=self.checker)
        self.assertIn('@'+'a'*40,result[0]['provenance'])
        self.assertIn(b'github',result[0]['payload'])

    def test_current_version_is_read_before_older_xml(self):
        old = b'<Defs><ThingDef><defName>Hare</defName></ThingDef></Defs>'
        class Versioned:
            files = ['1.6/Defs/Races_Hare.xml', '1.7/Defs/Races_Hare.xml']
            def read(self, rel):
                if rel.startswith('1.6/'):
                    raise AssertionError('Older version need not be fetched')
                return old
        match, err = rs.choose_match(Versioned(), 'OriginalXML/Core/Races_Hare.xml',
                                     old, version='1.7', kind='Defs')
        self.assertIsNone(err)
        self.assertEqual(match[0], '1.7/Defs/Races_Hare.xml')

    def test_github_xml_failure_uses_local_source(self):
        old = '<Defs><ThingDef><defName>Wolf</defName></ThingDef></Defs>'
        self.source('OriginalXML/Example/Races_Wolf.xml', old)
        self.source('Defs/Races_Wolf.xml',
                    '<Defs><ThingDef><defName>Wolf</defName><v>local</v></ThingDef></Defs>',
                    self.workshop / '4321')
        cfg = {'mods': [self.spec('Example', 'OriginalXML/Example', workshop_ids=['4321'],
                                  github={'url': 'https://github.com/owner/repo.git'})]}
        class BrokenGit:
            files = ['Defs/Races_Wolf.xml']
            provenance = 'github'
            read_error = 'GitHub XML download unavailable: timed out'
            def __init__(self, spec): pass
            def read(self, rel): raise OSError(self.read_error)
            def close(self): pass
        with patch.object(rs, 'GitSource', BrokenGit):
            result, warnings = rs.discover(cfg, steam=str(self.steam), github=True,
                                           checker_root=self.checker)
        self.assertEqual(result[0]['status'], 'REPLACE')
        self.assertIn(b'local', result[0]['payload'])
        self.assertTrue(any('timed out' in warning for warning in warnings))

    def test_github_xml_fetch_is_bounded_cached_and_pinned(self):
        from unittest.mock import MagicMock
        src = object.__new__(rs.GitSource)
        src._cache = {}
        src._cache_size = 0
        src.read_error = None
        src.raw_base = 'https://raw.githubusercontent.com/owner/repo/'
        src.revision = 'a' * 40
        response = MagicMock()
        response.__enter__.return_value.read.return_value = b'<Defs/>'
        with patch.object(rs.urlrequest, 'urlopen', return_value=response) as remote:
            self.assertEqual(src.read('1.7/Defs/Races.xml'), b'<Defs/>')
            self.assertEqual(src.read('1.7/Defs/Races.xml'), b'<Defs/>')
            self.assertEqual(remote.call_count, 1)
            self.assertEqual(remote.call_args.args[0],
                             'https://raw.githubusercontent.com/owner/repo/' +
                             'a' * 40 + '/1.7/Defs/Races.xml')
            self.assertEqual(remote.call_args.kwargs['timeout'], 12)
        src._cache = {}
        with patch.object(rs.urlrequest, 'urlopen', side_effect=TimeoutError('timed out')) as remote:
            with self.assertRaises(OSError):
                src.read('1.7/Defs/Other.xml')
            with self.assertRaises(OSError):
                src.read('1.7/Defs/Another.xml')
            self.assertEqual(remote.call_count, 1)

    def test_no_stale_preview_apply(self):
        old='<Defs><ThingDef><defName>Wolf</defName></ThingDef></Defs>'
        self.source('OriginalXML/Core/Races_Wolf.xml',old)
        self.source('Defs/Races_Wolf.xml','<Defs><ThingDef><defName>Wolf</defName><a>v2</a></ThingDef></Defs>',self.game/'Data'/'Core')
        cfg={'mods':[self.spec(game_dlc='Core')]}
        result,_=rs.discover(cfg,steam=str(self.steam),github=False,checker_root=self.checker)
        target=self.checker/'OriginalXML/Core/Races_Wolf.xml'
        target.write_text('changed since preview', encoding='utf8')
        with self.assertRaisesRegex(RuntimeError,'changed since preview'):
            rs.apply(result,checker_root=self.checker)
        self.assertEqual(target.read_text(), 'changed since preview')

    def test_historical_megafauna_untouched(self):
        self.source('OriginalXML/Megafauna/Races_Animal_Megafauna_v1.0-v1.1.xml','<Defs/>')
        cfg={'mods':[self.spec('Megafauna','OriginalXML/Megafauna')]}
        result,_=rs.discover(cfg,steam='',github=False,checker_root=self.checker)
        self.assertEqual(result[0]['status'],'HISTORICAL')

    def test_version_selection(self):
        old=b'<Defs><ThingDef><defName>Hare</defName></ThingDef></Defs>'
        self.source('OriginalXML/Example/Races_Hare.xml',old.decode())
        mod=self.workshop/'4321'
        self.source('1.6/Defs/Races_Hare.xml','<Defs><ThingDef><defName>Hare</defName><v>1.6</v></ThingDef></Defs>',mod)
        self.source('1.7/Defs/Races_Hare.xml','<Defs><ThingDef><defName>Hare</defName><v>1.7</v></ThingDef></Defs>',mod)
        cfg={'rimworld_version':'1.7','mods':[self.spec('Example','OriginalXML/Example',workshop_ids=['4321'])]}
        result,_=rs.discover(cfg,steam=str(self.steam),github=False,checker_root=self.checker)
        self.assertEqual(result[0]['source_file'],'1.7/Defs/Races_Hare.xml')

    def test_patch_reference_from_ce(self):
        old = '<Patch><Operation Class="PatchOperationAdd"><xpath>a</xpath></Operation></Patch>'
        new = '<Patch><Operation Class="PatchOperationAdd"><xpath>b</xpath></Operation></Patch>'
        self.source('OriginalXML/CEPatches/Patches/Core/Races_Birds.xml',old)
        self.source('Patches/Core/Races_Birds.xml',new,self.workshop/'1234')
        cfg={'mods':[self.spec('CombatExtended','OriginalXML/CEPatches', workshop_ids=['1234'])]}
        result,_=rs.discover(cfg,steam=str(self.steam),github=False,checker_root=self.checker)
        self.assertEqual(result[0]['status'],'REPLACE')
        self.assertEqual(result[0]['source_file'],'Patches/Core/Races_Birds.xml')

    def test_megafauna_current_version_alias(self):
        old = '<Defs><ThingDef><defName>Mammoth</defName></ThingDef></Defs>'
        self.source('OriginalXML/Megafauna/Races_Animal_Megafauna_v1.4-.xml',old)
        self.source('Defs/Races_Animal_Megafauna.xml','<Defs><ThingDef><defName>Mammoth</defName><v>latest</v></ThingDef></Defs>',self.workshop/'5678')
        cfg={'mods':[self.spec('Megafauna','OriginalXML/Megafauna',workshop_ids=['5678'])]}
        result,_=rs.discover(cfg,steam=str(self.steam),github=False,checker_root=self.checker)
        self.assertEqual(result[0]['status'],'REPLACE')

    def test_major_def_loss_is_refused(self):
        self.source('OriginalXML/Core/Races_Birds.xml','<Defs><ThingDef><defName>A</defName></ThingDef><ThingDef><defName>B</defName></ThingDef></Defs>')
        self.source('Defs/Races_Birds.xml','<Defs><ThingDef><defName>A</defName></ThingDef></Defs>',self.game/'Data'/'Core')
        cfg={'mods':[self.spec(game_dlc='Core')]}
        result,_=rs.discover(cfg,steam=str(self.steam),github=False,checker_root=self.checker)
        self.assertEqual(result[0]['status'],'MISSING')

    def test_xml_external_entities_rejected(self):
        with self.assertRaises(ValueError):
            rs.parse_xml(b'<!DOCTYPE foo [<!ENTITY x SYSTEM "file:///etc/passwd">]><Defs/>','Defs')
        with self.assertRaises(ValueError):
            rs.safe_inside(self.checker, 'OriginalXML/../../outside.xml')


if __name__=='__main__':
    unittest.main()
