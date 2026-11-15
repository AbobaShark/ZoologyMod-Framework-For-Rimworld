"""Exact XPath equivalence on mutable reference and branch trees."""
from copy import deepcopy
from unittest.mock import patch
import unittest

from lxml import etree as E

from core.indexed_xpath import indexed_xpath, xpath_scope
from core.rimworld_patch_apply import PatchApplier
from core.rimworld_original_xml import OriginalXmlIndex
from core.rimworld_patch_optimizer import PatchOptimizer


def raw(root, text):
    return E.ElementTree(root).xpath(text)


def fixture():
    return E.fromstring(b"""<Defs>
        <ThingDef Name="Base"><defName>A</defName><defName>A</defName>
          <race><tools><li><label>one</label></li><li><label>two</label></li></tools></race>
        </ThingDef>
        <ThingDef><defName>A</defName><race><tools><li><label>three</label></li></tools></race></ThingDef>
        <ThingDef><defName>B</defName></ThingDef>
        <ThingDef Name="Other"><defName>C</defName></ThingDef>
        <PawnKindDef><defName>A</defName></PawnKindDef>
    </Defs>""")


def operation(cls, xpath, value=None, attribute=None):
    root = E.Element("Operation", Class="PatchOperation" + cls)
    E.SubElement(root, "xpath").text = xpath
    if attribute is not None:
        E.SubElement(root, "attribute").text = attribute
    if value is not None:
        E.SubElement(root, "value").extend(E.fromstring("<v>" + value + "</v>"))
    return root


class IndexedXPathTests(unittest.TestCase):
    def test_exact_selectors_duplicates_and_fallback(self):
        root = fixture()
        expressions = [
            "/Defs/ThingDef[defName='A']",
            "/Defs/ThingDef[@Name='Base']/race/tools/li[2]",
            "/Defs/ThingDef[defName='A']/race/tools/li[1]",
            "/Defs/ThingDef[defName='A']/race/tools/li[label='one' or label='three']",
            "/Defs/ThingDef[defName='A']/race/tools/li[not(value)]",
            "/Defs/ThingDef[defName='X']",
            "/Defs/ThingDef[defName='A']/../PawnKindDef",
            "/Defs/ThingDef[defName='A'] | /Defs/ThingDef[defName='B']",
            "/Defs/ThingDef[defName='A']/race/tools/li/@Class",
            "count(/Defs/ThingDef)",
            "/Defs/PawnKindDef[defName='A']",
        ]
        with xpath_scope():
            for expr in expressions:
                with self.subTest(expr=expr):
                    self.assertEqual(indexed_xpath(root, expr), raw(root, expr))

    def test_root_selector_accepts_rimworld_document_relative_xpath(self):
        for xpath in ("Defs", "/Defs"):
            root = fixture()
            with xpath_scope():
                applier = PatchApplier()
                action = operation("Add", xpath, "<ThingDef><defName>Added</defName></ThingDef>")
                self.assertTrue(applier.apply_operation(action, root))
                self.assertEqual(len(indexed_xpath(root, "/Defs/ThingDef[defName='Added']")), 1)
                index = OriginalXmlIndex(None)
                self.assertEqual(index.xpath(xpath, root=root), [root])

    def test_live_values_and_identity_updates(self):
        root = fixture()
        applier = PatchApplier()
        queries = [
            "/Defs/ThingDef[defName='A']", "/Defs/ThingDef[defName='B']",
            "/Defs/ThingDef[defName='D']", "/Defs/ThingDef[@Name='Base']",
            "/Defs/ThingDef[@Name='New']", "/Defs/ThingDef[defName='A']/race/x",
        ]
        actions = [
            operation("Add", "/Defs/ThingDef[defName='A']/race", "<x>1</x>"),
            operation("Replace", "/Defs/ThingDef[defName='A']/race/x", "<x>2</x>"),
            operation("Replace", "/Defs/ThingDef[@Name='Base']/defName", "<defName>D</defName>"),
            operation("Add", "/Defs/ThingDef[defName='B']", "<defName>A</defName>"),
            operation("Remove", "/Defs/ThingDef[defName='B']"),
            operation("Add", "/Defs", "<ThingDef><defName>B</defName></ThingDef>"),
            operation("Remove", "/Defs/ThingDef[@Name='Base']"),
        ]
        attr = operation("AttributeSet", "/Defs/ThingDef[@Name='Other']", attribute="Name")
        E.SubElement(attr, "value").text = "New"
        actions.insert(0, attr)
        with xpath_scope():
            for action in actions:
                for expr in queries:
                    self.assertEqual(indexed_xpath(root, expr), raw(root, expr))
                self.assertTrue(applier.apply_operation(action, root))
            for expr in queries:
                self.assertEqual(indexed_xpath(root, expr), raw(root, expr))

    def test_optimizer_matches_native_xpath_with_full_tree_fallback(self):
        root = fixture()
        index = OriginalXmlIndex(None)
        index.root = root
        index.base_root = deepcopy(root)
        index.files_loaded = 1
        patch_root = E.Element("Patch")
        patch_root.extend([
            operation("Add", "/Defs/ThingDef[defName='B']", "<race/>"),
            operation("Replace", "/Defs/ThingDef[defName='B']/defName", "<defName>D</defName>"),
            operation("Add", "/Defs/ThingDef[defName='D']/race", "<x>2</x>"),
            operation("Replace", "/Defs/ThingDef[defName='D']/race/x", "<x>2</x>"),
            operation("Remove", "/Defs/ThingDef[defName='A']/race/tools/li[1]"),
        ])
        fast = PatchOptimizer(index)
        actual = E.tostring(fast.optimize(patch_root))
        with patch("core.rimworld_original_xml.indexed_xpath", raw), patch("core.rimworld_patch_apply.indexed_xpath", raw):
            native = PatchOptimizer(index)
            expected = E.tostring(native.optimize(patch_root))
        self.assertEqual(actual, expected)
        self.assertEqual(fast.stats, native.stats)


if __name__ == "__main__":
    unittest.main()
