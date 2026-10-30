import errno
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch

from lxml import etree as ET
from core.patch_engine import PatchGenerator


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
            with patch('core.patch_engine.os.replace', side_effect=OSError(errno.EINVAL, 'Invalid argument')):
                with self.assertRaises(OSError):
                    self.generator._write_patch_root(self.root, destination)
            self.assertEqual(destination.read_bytes(), before)
            self.assertEqual(list(Path(directory).glob('.zoology-patch-*')), [])

    def test_invalid_argument_when_opening_output_preserves_existing_patch(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory) / 'existing.xml'
            before = b'<Patch><old/></Patch>'
            destination.write_bytes(before)
            with patch('core.patch_engine.tempfile.NamedTemporaryFile',
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



if __name__ == '__main__':
    unittest.main()
