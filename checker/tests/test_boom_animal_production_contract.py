import unittest
from pathlib import Path

from lxml import etree as E

from core.rimworld_patch_apply import PatchApplier


MOD = Path(__file__).resolve().parents[2] / "Zoology"
CORE = MOD / "Core/Patches/Core/Misc/ChemfuelProduction.xml"
CE = MOD / "ModPatches/CombatExtended/Patches/ChemfuelProduction_CE.xml"


class BoomAnimalProductionTests(unittest.TestCase):
    def fixture(self, rat_comps="", lope_comps=""):
        return E.fromstring(f"""<Defs>
            <ThingDef><defName>Boomrat</defName>{rat_comps}</ThingDef>
            <ThingDef><defName>Boomalope</defName>{lope_comps}</ThingDef>
        </Defs>""")

    def apply(self, root, path):
        applier = PatchApplier()
        self.assertTrue(applier.apply_patch_root(E.parse(str(path)).getroot(), root))
        self.assertEqual(applier.missing_targets, [])

    def assert_fuel(self, root, name, amount):
        race = root.xpath(f"ThingDef[defName='{name}']")[0]
        self.assertEqual(len(race.findall("comps")), 1)
        comp = race.xpath("comps/li[milkDef='Chemfuel']")
        self.assertEqual(len(comp), 1)
        self.assertEqual(comp[0].findtext("milkAmount"), str(amount))
        self.assertEqual(comp[0].findtext("milkIntervalDays"), "1")
        self.assertEqual(comp[0].findtext("milkFemaleOnly"), "false")

    def test_without_ce_missing_and_existing_comps(self):
        root = self.fixture("", """<comps>
            <li Class="CompProperties_Milkable"><milkDef>Chemfuel</milkDef>
                <milkAmount>12</milkAmount></li>
            <li Class="Unrelated"/>
        </comps>""")
        for _ in range(2):
            self.apply(root, CORE)
            self.assert_fuel(root, "Boomrat", 1)
            self.assert_fuel(root, "Boomalope", 11)
        self.assertEqual(len(root.xpath("//li[@Class='Unrelated']")), 1)

    def test_with_ce_exclusive_fsx_preserves_yield_and_butchery(self):
        def comps(amount, interval, chemical):
            return f"""<comps>
                <li Class="CombatExtended.CompProperties_ShearableRenameable">
                    <woolDef>FSX</woolDef><woolAmount>{amount}</woolAmount>
                    <shearIntervalDays>{interval}</shearIntervalDays>
                </li>{chemical}<li Class="Unrelated"/>
            </comps><butcherProducts><FSX>2</FSX></butcherProducts>"""
        root = self.fixture(comps(1, 3, ""), comps(6, 10, """
            <li Class="CompProperties_Milkable"><milkDef>Chemfuel</milkDef></li>
        """))
        for _ in range(2):
            self.apply(root, CORE)
            self.apply(root, CE)
        self.assertEqual(root.xpath("//li[milkDef='Chemfuel']"), [])
        self.assertEqual(root.xpath("//li[woolDef='FSX']/woolAmount/text()"), ["1", "6"])
        self.assertEqual(root.xpath("//li[woolDef='FSX']/shearIntervalDays/text()"), ["3", "10"])
        self.assertEqual(len(root.xpath("//butcherProducts/FSX")), 2)
        self.assertEqual(len(root.xpath("//li[@Class='Unrelated']")), 2)

    def test_ce_without_expected_fsx_does_not_remove_chemfuel(self):
        root = self.fixture()
        self.apply(root, CORE)
        self.apply(root, CE)
        self.assert_fuel(root, "Boomrat", 1)
        self.assert_fuel(root, "Boomalope", 11)


if __name__ == "__main__":
    unittest.main()
