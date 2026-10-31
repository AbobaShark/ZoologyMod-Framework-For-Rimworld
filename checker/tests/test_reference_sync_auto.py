"""Test canonical Reference Sync paths, source version selection and atomic cleanup."""
import io
import json
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

from tools import reference_sync_auto as rs


class AutoReferenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.checker = self.root / 'checker'
        self.checker.mkdir()
        self.steam = self.root / 'SteamLibrary'
        self.game = self.steam / 'steamapps/common/RimWorld'
        (self.game / 'Data/Core/Defs').mkdir(parents=True)
        self.workshop = self.steam / 'steamapps/workshop/content/294100'
        self.workshop.mkdir(parents=True)

    def write(self, relative, content, base=None):
        path = (base or self.checker) / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding='utf-8')
        return path

    def cfg(self, group='Core', **other):
        root = 'OriginalXML/CEPatches' if group == 'CombatExtended' else 'OriginalXML/' + group
        return {'rimworld_version': 'auto',
                'mods': [{'id': group, 'reference_dirs': [root], **other}]}

    def test_game_version_selects_latest_compatible_not_all_versions(self):
        self.write('Version.txt', '1.7.1234', self.game)
        mod = self.workshop / '1136958577'
        for number in range(9):
            self.write(f'1.{number}/Defs/Animals.xml',
                       f'<Defs><ThingDef><defName>Dino</defName><statBases>'
                       f'<MarketValue>{number}</MarketValue></statBases></ThingDef></Defs>', mod)
        cfg = self.cfg('Dinosauria', workshop_ids=['1136958577'])
        preview, warnings = rs.discover(cfg, steam=str(self.steam), github=False,
                                         checker_root=self.checker)
        self.assertEqual([e['source_file'] for e in preview], ['1.7/Defs/Animals.xml'])
        self.assertFalse([e for e in preview if e['status'] == 'DELETE'])
        rs.apply(preview, checker_root=self.checker)
        self.write('Version.txt', '1.8.10', self.game)
        next_preview, _ = rs.discover(cfg, steam=str(self.steam), github=False,
                                      checker_root=self.checker)
        self.assertEqual({e['status'] for e in next_preview}, {'COPY', 'DELETE'})
        self.assertIn('1.8/Defs/Animals.xml', next(e['source_file'] for e in next_preview
                                                  if e['status'] == 'COPY'))
        rs.apply(next_preview, checker_root=self.checker)
        self.assertFalse((self.checker / 'OriginalXML/Dinosauria/1.7').exists())
        self.assertTrue((self.checker / 'OriginalXML/Dinosauria/1.8/Defs/Animals.xml').is_file())

    def test_mod_without_current_version_uses_highest_older_version(self):
        self.write('Version.txt', '1.8.0', self.game)
        mod = self.workshop / '1055485938'
        for number in range(7):
            self.write(f'1.{number}/Defs/Megafauna.xml',
                       '<Defs><ThingDef><defName>MegafaunaTest</defName></ThingDef></Defs>', mod)
        cfg = self.cfg('Megafauna', workshop_ids=['1055485938'])
        preview, _ = rs.discover(cfg, steam=str(self.steam), github=False,
                                 checker_root=self.checker)
        self.assertEqual([e['source_file'] for e in preview], ['1.6/Defs/Megafauna.xml'])

    def test_material_reference_has_no_extra_classification_directory(self):
        xml = '<Defs><ThingDef Name="LeatherBase" Abstract="True"/></Defs>'
        self.write('Defs/ThingDefs_Items/Leather.xml', xml, self.game / 'Data/Core')
        cfg = self.cfg(game_dlc='Core')
        preview, _ = rs.discover(cfg, steam=str(self.steam), github=False,
                                 checker_root=self.checker)
        self.assertEqual([e['destination'] for e in preview],
                         ['OriginalXML/Core/Defs/ThingDefs_Items/Leather.xml'])
        rs.apply(preview, checker_root=self.checker)
        again, _ = rs.discover(cfg, steam=str(self.steam), github=False,
                               checker_root=self.checker)
        self.assertEqual([e['status'] for e in again], ['SKIP'])

    def test_replacement_removes_obsolete_classification_folders(self):
        xml = '<Defs><ThingDef Name="LeatherBase" Abstract="True"/></Defs>'
        self.write('Defs/Leather.xml', xml, self.game / 'Data/Core')
        stale = self.write('OriginalXML/Core/_Leathers/Defs/Leather.xml', xml)
        cfg = self.cfg(game_dlc='Core')
        preview, _ = rs.discover(cfg, steam=str(self.steam), github=False,
                                 checker_root=self.checker)
        self.assertEqual({e['status'] for e in preview}, {'COPY', 'DELETE'})
        self.assertTrue(stale.is_file())
        backup = rs.apply(preview, checker_root=self.checker)
        self.assertFalse(stale.exists())
        self.assertFalse((self.checker / 'OriginalXML/Core/_Leathers').exists())
        self.assertTrue((backup / 'OriginalXML/Core/_Leathers/Defs/Leather.xml').is_file())

    def test_current_canonical_file_replaced_with_backup(self):
        old = '<Defs><ThingDef><defName>Chicken</defName><label>old</label></ThingDef></Defs>'
        new = '<Defs><ThingDef><defName>Chicken</defName><label>new</label></ThingDef></Defs>'
        target = self.write('OriginalXML/Core/Defs/Birds.xml', old)
        self.write('Defs/Birds.xml', new, self.game / 'Data/Core')
        preview, _ = rs.discover(self.cfg(game_dlc='Core'), steam=str(self.steam),
                                 github=False, checker_root=self.checker)
        self.assertEqual([e['status'] for e in preview], ['REPLACE'])
        backup = rs.apply(preview, checker_root=self.checker)
        self.assertEqual(target.read_text(), new)
        self.assertEqual((backup / 'OriginalXML/Core/Defs/Birds.xml').read_text(), old)

    def test_installed_workshop_source_skips_github_entirely(self):
        local_xml = ('<Defs><ThingDef><defName>Wolf</defName>'
                     '<label>installed</label></ThingDef></Defs>')
        workshop = self.workshop / '4321'
        self.write('Defs/Wolf.xml', local_xml, workshop)
        cfg = self.cfg('Example', workshop_ids=['4321'],
                       github={'url': 'https://github.com/owner/repo.git'})
        # Network access must not be attempted, even with github=True.
        with patch.object(rs, 'GitSource',
                          side_effect=AssertionError('Unexpected GitHub access')):
            entries, warnings = rs.discover(cfg, steam=str(self.steam),
                                            github=True, checker_root=self.checker)
        self.assertEqual(len(entries), 1)
        self.assertEqual(entries[0]['payload'], local_xml.encode())
        self.assertEqual(entries[0]['provenance'], str(workshop.resolve()))

    def test_workshop_id_resolves_without_scanning_every_about_xml(self):
        workshop = self.workshop / '1541721856'
        self.write('Defs/Animals.xml',
                   '<Defs><ThingDef><defName>TestAnimal</defName></ThingDef></Defs>',
                   workshop)
        for index in range(20):
            self.write('About/About.xml', '<ModMetaData><name>Other</name></ModMetaData>',
                       self.workshop / str(800000 + index))
        with patch.object(rs, 'read_about',
                          side_effect=AssertionError('Scanned unrelated About.xml')):
            found, error = rs.find_local_mod(
                {'id':'AlphaAnimals','workshop_ids':['1541721856']},
                [], [self.workshop], [])
        self.assertEqual(found, workshop)
        self.assertIsNone(error)

    def test_github_used_if_workshop_not_installed(self):
        cfg = self.cfg('Example', workshop_ids=['missing'],
                       github={'url': 'https://github.com/owner/repo.git'})
        class FakeGit:
            files = ['Defs/Wolf.xml']
            provenance = 'fake github'
            def __init__(self, spec): pass
            def read(self, rel):
                return b'<Defs><ThingDef><defName>Wolf</defName></ThingDef></Defs>'
            def close(self): pass
        with patch.object(rs, 'GitSource', FakeGit):
            entries, warnings = rs.discover(cfg, steam=str(self.steam),
                                            github=True, checker_root=self.checker)
        self.assertEqual(len(entries), 1)
        self.assertEqual(entries[0]['provenance'], 'fake github')

    def test_github_used_if_installed_source_has_no_definitions(self):
        workshop = self.workshop / '4321'
        self.write('Defs/NotDefs.xml', '<Other/>', workshop)
        cfg = self.cfg('Example', workshop_ids=['4321'],
                       github={'url': 'https://github.com/owner/repo.git'})
        class FakeGit:
            files = ['Defs/Wolf.xml']
            provenance = 'fake github'
            def __init__(self, spec): pass
            def read(self, rel):
                return b'<Defs><ThingDef><defName>Wolf</defName></ThingDef></Defs>'
            def close(self): pass
        with patch.object(rs, 'GitSource', FakeGit):
            entries, warnings = rs.discover(cfg, steam=str(self.steam),
                                            github=True, checker_root=self.checker)
        self.assertEqual(len(entries), 1)
        self.assertEqual(entries[0]['provenance'], 'fake github')

    def test_missing_sources_never_delete_existing_references(self):
        old = self.write('OriginalXML/Megafauna/Defs/Mammoth.xml',
                         '<Defs><ThingDef><defName>Mammoth</defName></ThingDef></Defs>')
        preview, warnings = rs.discover(
            self.cfg('Megafauna', workshop_ids=['missing']),
            steam=str(self.steam), github=False, checker_root=self.checker)
        self.assertEqual(preview, [])
        self.assertTrue(old.is_file())

    def test_preview_checksum_guards_removals(self):
        previous = self.write('OriginalXML/Core/Other.xml',
                              '<Defs><ThingDef><defName>Old</defName></ThingDef></Defs>')
        self.write('Defs/Birds.xml', '<Defs><ThingDef><defName>Chicken</defName></ThingDef></Defs>',
                   self.game / 'Data/Core')
        preview, _ = rs.discover(self.cfg(game_dlc='Core'), steam=str(self.steam),
                                 github=False, checker_root=self.checker)
        self.assertIn('DELETE', {e['status'] for e in preview})
        previous.write_text('changed since preview')
        with self.assertRaisesRegex(RuntimeError, 'changed since preview'):
            rs.apply(preview, checker_root=self.checker)
        self.assertEqual(previous.read_text(), 'changed since preview')

    def test_xml_entities_and_path_escape_rejected(self):
        with self.assertRaises(ValueError):
            rs.parse_xml(b'<!DOCTYPE foo [<!ENTITY x SYSTEM "file:///etc/passwd">]><Defs/>',
                         'Defs')
        with self.assertRaises(ValueError):
            rs.safe_inside(self.checker, 'OriginalXML/../../outside.xml')

    def test_github_download_cached_and_bounded(self):
        source = object.__new__(rs.GitSource)
        source._cache = {}
        source._cache_size = 0
        source.read_error = None
        source.raw_base = 'https://raw.githubusercontent.com/owner/repo/'
        source.revision = 'a'*40
        response = MagicMock()
        response.__enter__.return_value.read.return_value = b'<Defs/>'
        with patch.object(rs.urlrequest, 'urlopen', return_value=response) as mock:
            source.read('1.8/Defs/Animals.xml')
            source.read('1.8/Defs/Animals.xml')
            self.assertEqual(mock.call_count, 1)


    def test_github_index_uses_two_api_requests_and_no_git_clone(self):
        sha = 'a' * 40
        index = {'truncated': False, 'tree': [
            {'path': '1.7/Defs/Races.xml', 'type': 'blob', 'size': 127},
            {'path': 'Textures/Animal.png', 'type': 'blob', 'size': 500},
            {'path': '1.7/Defs', 'type': 'tree'},
        ]}
        urls = []
        def fake_open(request, timeout):
            urls.append(request.full_url)
            self.assertEqual(timeout, 12)
            data = {'sha': sha} if '/commits/' in request.full_url else index
            return io.BytesIO(json.dumps(data).encode('utf-8'))
        with patch.object(rs.urlrequest, 'urlopen', side_effect=fake_open):
            with patch.object(rs.subprocess, 'run', side_effect=AssertionError('git invoked')):
                source = rs.GitSource({'github': {
                    'url': 'https://github.com/juanosarg/AlphaAnimals.git'}})
        self.assertEqual(source.revision, sha)
        self.assertEqual(source.files, ['1.7/Defs/Races.xml'])
        self.assertEqual(len(urls), 2)
        self.assertIn('/commits/HEAD', urls[0])
        self.assertIn('/git/trees/' + sha, urls[1])

    def test_parallel_xml_batch_reports_progress_and_finishes(self):
        source = object.__new__(rs.GitSource)
        source._cache = {}
        source._cache_size = 0
        source.read_error = None
        source.provenance = 'fake github'
        paths = [f'1.7/Defs/Animal_{i}.xml' for i in range(24)]
        progress = []
        with patch.object(source, '_fetch_raw', side_effect=lambda rel: rel.encode()):
            found = source.read_many(paths, progress=progress.append)
        self.assertEqual(len(found), 24)
        self.assertEqual(set(found), set(paths))
        self.assertEqual(len(source._cache), 24)
        self.assertEqual(len(progress), 3)
        self.assertIn('24/24', progress[-1])

    def test_github_batch_failure_preserves_existing_references(self):
        existing = self.write('OriginalXML/Example/Defs/Wolf.xml',
                              '<Defs><ThingDef><defName>Wolf</defName></ThingDef></Defs>')
        cfg = self.cfg('Example', workshop_ids=['missing'],
                       github={'url': 'https://github.com/owner/repo.git'})
        class BrokenGit:
            files = ['Defs/Wolf.xml']
            provenance = 'broken github'
            read_error = 'network timeout'
            def __init__(self, spec): pass
            def read_many(self, paths, progress=None, cancelled=None):
                raise OSError('network timeout')
            def close(self): pass
        with patch.object(rs, 'GitSource', BrokenGit):
            entries, warnings = rs.discover(cfg, steam=str(self.steam),
                                            github=True, checker_root=self.checker)
        self.assertEqual(entries, [])
        self.assertTrue(existing.is_file())
        self.assertTrue(any('network timeout' in x for x in warnings))

    def test_cancelled_parallel_download_exits_without_writing_xml(self):
        source = object.__new__(rs.GitSource)
        source._cache = {}
        source._cache_size = 0
        source.read_error = None
        source.provenance = 'fake github'
        cancelled = threading.Event()
        def progress(message):
            if '10/24' in message:
                cancelled.set()
        with patch.object(source, '_fetch_raw', side_effect=lambda rel: rel.encode()):
            with self.assertRaises(InterruptedError):
                source.read_many([f'1.7/Defs/Animal_{i}.xml' for i in range(24)],
                                 progress=progress, cancelled=cancelled)


if __name__ == '__main__':
    unittest.main()
