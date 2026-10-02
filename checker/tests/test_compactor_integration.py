import tempfile
import unittest
from pathlib import Path
from lxml import etree as LET

from rimworld_patch_fixer import compact_successful_outputs


PATCH = '''<?xml version="1.0" encoding="utf-8"?>
<Patch>
  <Operation Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="A"]/tools</xpath></Operation>
  <Operation Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName="B"]/tools</xpath></Operation>
</Patch>
'''


class CompactorIntegrationTests(unittest.TestCase):
    def test_only_successful_outputs_from_current_run_are_compacted(self):
        with tempfile.TemporaryDirectory() as td:
            ok = Path(td) / 'ok.xml'
            failed = Path(td) / 'failed.xml'
            stale = Path(td) / 'stale.xml'
            for path in (ok, failed, stale):
                path.write_text(PATCH, encoding='utf-8')
            stale_before = stale.read_bytes()
            failed_before = failed.read_bytes()

            reports = compact_successful_outputs(
                [('source-a.xml', str(ok), True), ('source-b.xml', str(failed), False)],
                enabled=True,
            )

            self.assertEqual([Path(r.path).name for r in reports], ['ok.xml'])
            self.assertEqual(failed.read_bytes(), failed_before)
            self.assertEqual(stale.read_bytes(), stale_before)
            self.assertEqual(
                len(LET.parse(str(ok)).xpath('/*/*[@Class="PatchOperationRemove"]')),
                1,
            )

    def test_disabled_compaction_does_not_touch_outputs(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / 'patch.xml'
            path.write_text(PATCH, encoding='utf-8')
            before = path.read_bytes()
            reports = compact_successful_outputs(
                [('source.xml', str(path), True)], enabled=False
            )
            self.assertEqual(reports, [])
            self.assertEqual(path.read_bytes(), before)


if __name__ == '__main__':
    unittest.main()
