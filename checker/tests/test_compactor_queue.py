import random
import unittest
from types import SimpleNamespace

from lxml import etree as E
from core.rimworld_patch_compactor import PatchCompactor


def reference_groups(specs):
    consumed = set()
    result = []
    for i, anchor in enumerate(specs):
        if i in consumed:
            continue
        group = [anchor.node]
        subjects = {anchor.subject}
        for j in range(i + 1, len(specs)):
            candidate = specs[j]
            if j in consumed or candidate.key != anchor.key or candidate.subject in subjects:
                continue
            if any(k not in consumed and specs[k].subject == candidate.subject
                   for k in range(i + 1, j)):
                continue
            group.append(candidate.node)
            subjects.add(candidate.subject)
            consumed.add(j)
        result.append(tuple(group))
    return result


class CompactorQueueTests(unittest.TestCase):
    def test_queue_grouping_matches_reference_order(self):
        rng = random.Random(20261115)
        for size in (1, 2, 10, 50, 150):
            for _ in range(100):
                specs = [SimpleNamespace(node=i, subject=rng.randrange(12),
                                         key=rng.randrange(8)) for i in range(size)]
                compactor = PatchCompactor()
                compactor._merge_group = lambda group: [tuple(s.node for s in group)]
                actual = compactor._coalesce_segment_conservative(specs)
                normalized = [(v,) if isinstance(v, int) else v for v in actual]
                self.assertEqual(normalized, reference_groups(specs))

    def test_noop_compaction_preserves_bytes_and_stops(self):
        patch = E.fromstring("""<Patch><Operation Class="PatchOperationAdd"><xpath>/Defs/ThingDef[defName='A']</xpath><value><x>1</x></value></Operation></Patch>""")
        compactor = PatchCompactor()
        result = compactor.compact(patch)
        self.assertEqual(E.tostring(result), E.tostring(patch))
        self.assertEqual(compactor.passes, 1)


if __name__ == "__main__":
    unittest.main()
