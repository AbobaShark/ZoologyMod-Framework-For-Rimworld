import errno
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch

from lxml import etree as ET
from rimworld_patch_fixer import GeneratorApp, PatchGenerator


class SafePatchOutputTests(unittest.TestCase):
    def setUp(self):
        self.generator = PatchGenerator.__new__(PatchGenerator)
        self.root = ET.fromstring(b'<Patch><Operation Class="PatchOperationTest"/></Patch>')

    def test_successfully_replaces_existing_patch_with_valid_xml(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / 'existing.xml'
            destination.write_bytes(b'<Patch><old/></Patch>')
            self.assertTrue(self.generator._write_patch_root(self.root, destination))
            self.assertEqual(ET.parse(str(destination)).getroot().find('Operation').get('Class'), 'PatchOperationTest')
            self.assertEqual(list(Path(directory).glob('.zoology-patch-*')), [])

    def test_failed_replace_preserves_existing_patch_and_cleans_temporary_file(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / 'existing.xml'
            before = b'<Patch><old/></Patch>'
            destination.write_bytes(before)
            with patch('rimworld_patch_fixer.os.replace', side_effect=OSError(errno.EINVAL, 'Invalid argument')):
                with self.assertRaises(OSError):
                    self.generator._write_patch_root(self.root, destination)
            self.assertEqual(destination.read_bytes(), before)
            self.assertEqual(list(Path(directory).glob('.zoology-patch-*')), [])

    def test_invalid_argument_when_opening_output_preserves_existing_patch(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / 'existing.xml'
            before = b'<Patch><old/></Patch>'
            destination.write_bytes(before)
            with patch('rimworld_patch_fixer.tempfile.NamedTemporaryFile',
                       side_effect=OSError(errno.EINVAL, 'Invalid argument')):
                with self.assertRaises(OSError):
                    self.generator._write_patch_root(self.root, destination)
            self.assertEqual(destination.read_bytes(), before)

    def test_validation_failure_preserves_existing_patch(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / 'existing.xml'
            before = b'<Patch><old/></Patch>'
            destination.write_bytes(before)
            with patch.object(self.generator, 'validate_xml', return_value=False):
                with self.assertRaises(ValueError):
                    self.generator._write_patch_root(self.root, destination)
            self.assertEqual(destination.read_bytes(), before)
            self.assertEqual(list(Path(directory).glob('.zoology-patch-*')), [])

    def test_partial_generation_is_reported_as_incomplete(self):
        field = lambda value: SimpleNamespace(get=lambda: value)
        app = SimpleNamespace(
            _persist_table_sources=lambda: None,
            vanilla_tsv=field('source.tsv'), ce_tsv=field(''),
            xml_paths=['first.xml', 'failed.xml'], replace_in_place=field(False),
            preserve_runtime_preconditions=field(True), report_dir='out',
            _make_unique_output_path=lambda source, used: 'out/' + source,
            _compact_current_outputs=lambda results: [], status=SimpleNamespace(set=Mock()),
        )
        generator = SimpleNamespace(generate_fixed_xml=Mock(side_effect=[True, False]))
        with patch('rimworld_patch_fixer.source_available', return_value=True), \
             patch('rimworld_patch_fixer.PatchGenerator', return_value=generator), \
             patch('rimworld_patch_fixer.messagebox.showwarning') as warning, \
             patch('rimworld_patch_fixer.messagebox.showinfo') as success:
            GeneratorApp.run_generation(app)
        warning.assert_called_once()
        self.assertIn('failed.xml', warning.call_args.args[1])
        self.assertIn('1/2', warning.call_args.args[1])
        app.status.set.assert_called_with('Generated 1/2 file(s); 1 FAILED.')
        success.assert_not_called()


if __name__ == '__main__':
    unittest.main()
