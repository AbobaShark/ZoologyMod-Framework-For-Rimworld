import unittest

import pandas as pd
from lxml import etree as E

from core.patch_engine import PatchGenerator


class GenerationContextTests(unittest.TestCase):
    def generator(self):
        g = PatchGenerator.__new__(PatchGenerator)
        g.vanilla_df = pd.DataFrame([
            {"XML name": "<li>A</li>", "Parrent abstract": "Base", "Combat power": "10"},
            {"XML name": "<li>B</li>", "Parrent abstract": "Base", "Combat power": "10"},
        ]).fillna("")
        g.ce_df = None
        g.optimize = False
        g._group_overrides = {}
        # Exercise the actual group selection and shared data preparation
        # independently from the large field-generation routines.
        g.generate_abstract_patches = lambda *args, **kwargs: []
        g.generate_def_patches = lambda name, *args, **kwargs: [E.Element("Operation", Class="PatchOperationAdd", animal=name)]
        g.generate_ce_block = lambda *args, **kwargs: E.Comment("CE disabled")
        return g

    def test_prepared_context_matches_uncached_group_generation(self):
        uncached = self.generator()
        cached = self.generator()
        self.assertEqual(cached.prepare_generation_context(), frozenset(("A", "B")))
        for names in (["A"], ["B"], ["A", "B"], ["A"]):
            with self.subTest(names=names):
                root = E.Element("Patch")
                a = uncached._build_patch_root(root, names, ["Base"])
                b = cached._build_patch_root(root, names, ["Base"])
                self.assertEqual(E.tostring(a), E.tostring(b))

    def test_pack_animal_updates_are_explicit_even_with_shared_parent(self):
        generator = PatchGenerator.__new__(PatchGenerator)
        generator._group_overrides = {}
        generator.build_tools_vanilla = lambda *args, **kwargs: None

        for source_value, expected in (
            ("true", "true"), ("false", "false"), ("1", "true"), ("0", "false")
        ):
            with self.subTest(source_value=source_value):
                row = pd.Series({
                    "XML name": "<li>TestPackAnimal</li>",
                    "Parrent abstract": "SharedPackBase",
                    "Parrent Pawn kind abstract": "AnimalKindBase",
                    "PackAnimal": source_value,
                })
                common = {
                    "SharedPackBase": {
                        "common_values": {"PackAnimal": source_value},
                        "differing_cols": [],
                        "tool_signature": None,
                    }
                }
                ops = generator.generate_def_patches(
                    "TestPackAnimal", row, E.Element("Patch"),
                    "SharedPackBase", parent_common_map=common,
                    pawn_parent_common_map={},
                )
                pack_ops = [
                    op for op in ops
                    if isinstance(getattr(op, "tag", None), str)
                    and op.findtext("xpath") == (
                        '/Defs/ThingDef[defName = "TestPackAnimal"]/race/packAnimal'
                    )
                ]
                self.assertEqual(len(pack_ops), 1)
                self.assertEqual(pack_ops[0].findtext("match/value/packAnimal"), expected)
                self.assertEqual(pack_ops[0].findtext("nomatch/value/packAnimal"), expected)

    def test_prepare_ce_tool_rows_preserves_source_order(self):
        g = self.generator()
        g.ce_df = pd.DataFrame([
            {"XML name": "<li>AB</li>", "Body shape": "Quadruped"},
            {"XML name": "<li>A</li>", "Body shape": "Quadruped"},
        ])
        g.prepare_generation_context()
        self.assertEqual([txt for txt, _ in g._prepared_ce_tool_rows], ["<li>AB</li>", "<li>A</li>"])
        self.assertEqual(list(g._prepared_ce_context[0]), ["AB", "A"])


if __name__ == "__main__":
    unittest.main()
