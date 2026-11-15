import copy
import tempfile
import unittest
from pathlib import Path

from lxml import etree as E

from core.rimworld_original_xml import OriginalXmlIndex
from core.rimworld_patch_optimizer import PatchOptimizer
from core.rimworld_patch_apply import PatchApplier
from core.reference_projection import ReferenceProjection, patch_targets


BASE = """<Defs>
<ThingDef Name="Base"><statBases><MoveSpeed>4</MoveSpeed></statBases></ThingDef>
<ThingDef Name="Mid" ParentName="Base"/>
<ThingDef ParentName="Mid"><defName>A</defName><statBases/><race><baseBodySize>1</baseBodySize><specialTrainables><li>Dig</li><li>Forage</li></specialTrainables></race></ThingDef>
<ThingDef><defName>B</defName><race><baseBodySize>2</baseBodySize></race></ThingDef>
<PawnKindDef><defName>A</defName><combatPower>10</combatPower></PawnKindDef>
<ThingDef><defName>Unrelated</defName></ThingDef>
</Defs>"""

PATCH = """<Patch>
<Operation Class="PatchOperationAdd"><xpath>/Defs/ThingDef[defName='A']/statBases</xpath><value><MoveSpeed>4</MoveSpeed></value></Operation>
<Operation Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName='A']/race/baseBodySize</xpath><value><baseBodySize>1</baseBodySize></value></Operation>
<Operation Class="PatchOperationConditional"><xpath>/Defs/ThingDef[defName='A']/race/specialTrainables/li[text()='Dig' or text()='Forage']</xpath><match Class="PatchOperationRemove"><xpath>/Defs/ThingDef[defName='A']/race/specialTrainables/li[text()='Dig' or text()='Forage']</xpath></match></Operation>
<Operation Class="PatchOperationFindMod"><mods><li>Combat Extended</li></mods><match Class="PatchOperationSequence"><operations>
<li Class="PatchOperationReplace"><xpath>/Defs/PawnKindDef[defName='A']/combatPower</xpath><value><combatPower>20</combatPower></value></li>
</operations></match></Operation>
<Operation Class="PatchOperationAdd"><xpath>/Defs/ThingDef[defName='Missing']/race</xpath><value><baseBodySize>1</baseBodySize></value></Operation>
</Patch>"""


class FullIndex(OriginalXmlIndex):
    def simulation_roots(self, patch_root):
        return self.clone_base_root(), self.clone_root() if self.patches_enabled else None


class ReferenceProjectionTests(unittest.TestCase):
    def test_parent_closure_and_document_order(self):
        root = E.fromstring(BASE)
        projection = ReferenceProjection(root)
        result = projection.clone({("ThingDef", "defName", "A"), ("PawnKindDef", "defName", "A")})
        self.assertEqual([n.get("Name") or n.findtext("defName") for n in result], ["Base", "Mid", "A", "A"])
        result[2].find("race/baseBodySize").text = "99"
        self.assertEqual(root[2].findtext("race/baseBodySize"), "1")
        self.assertEqual(projection.clone({("ThingDef", "defName", "A")})[2].findtext("race/baseBodySize"), "1")

    def test_duplicate_nodes_and_multiple_defnames(self):
        root = E.fromstring("<Defs><ThingDef><defName>A</defName><defName>B</defName></ThingDef><ThingDef><defName>B</defName></ThingDef></Defs>")
        projected = ReferenceProjection(root).clone({("ThingDef", "defName", "B")})
        self.assertEqual(len(projected), 2)

    def test_nonlocal_and_identity_mutating_operations_fall_back(self):
        for xpath in (
            "/Defs/ThingDef[defName='A']/../ThingDef",
            "/Defs/ThingDef[defName='A'] | /Defs/ThingDef[defName='B']",
            "/Defs/ThingDef[defName='A']/race[../defName='A']",
            "/Defs/ThingDef[defName='A']/defName",
            "/Defs/ThingDef[defName='A']",
        ):
            with self.subTest(xpath=xpath):
                patch = E.Element("Patch")
                op = E.SubElement(patch, "Operation", Class="PatchOperationReplace")
                E.SubElement(op, "xpath").text = xpath
                self.assertIsNone(patch_targets(patch))
        root_add = E.fromstring("""<Patch><Operation Class="PatchOperationAdd"><xpath>/Defs/ThingDef[defName='A']</xpath><value><defName>B</defName></value></Operation></Patch>""")
        self.assertIsNone(patch_targets(root_add))

    def test_optimizer_byte_equivalence_with_and_without_ce(self):
        with tempfile.TemporaryDirectory() as td:
            base = Path(td) / "base"
            ce = Path(td) / "ce"
            base.mkdir()
            ce.mkdir()
            (base / "defs.xml").write_text(BASE, encoding="utf-8")
            (ce / "patch.xml").write_text("""<Patch><Operation Class="PatchOperationReplace"><xpath>/Defs/ThingDef[defName='A']/race/baseBodySize</xpath><value><baseBodySize>3</baseBodySize></value></Operation></Patch>""", encoding="utf-8")
            for patches in (None, str(ce)):
                with self.subTest(ce=patches):
                    index = OriginalXmlIndex(str(base), patches).load()
                    full = FullIndex(str(base), patches).load()
                    patch = E.fromstring(PATCH)
                    self.assertIsNotNone(patch_targets(patch))
                    local_root, _ = index.simulation_roots(patch)
                    self.assertLess(len(local_root), len(index.base_root))
                    before = E.tostring(index.root)
                    old = PatchOptimizer(full).optimize(copy.deepcopy(patch))
                    new = PatchOptimizer(index).optimize(copy.deepcopy(patch))
                    self.assertEqual(E.tostring(old), E.tostring(new))
                    self.assertEqual(E.tostring(index.root), before)
                    for active in (False, True):
                        old_state, new_state = full.clone_root(), index.clone_root()
                        self.assertEqual(PatchApplier(apply_find_mod_match=active).apply_patch_root(old, old_state), PatchApplier(apply_find_mod_match=active).apply_patch_root(new, new_state))
                        self.assertEqual(E.tostring(old_state), E.tostring(new_state))


if __name__ == "__main__":
    unittest.main()
