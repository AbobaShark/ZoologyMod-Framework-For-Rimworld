"""Regression tests for independent EggMilkWool reproductive sections.

The egg-production table contains every egg-laying animal. The separate
aquatic reproduction table contains only opt-in species. Their rows must
never be assumed to match and the source layout is discovered by headings.
"""
import unittest
import pandas as pd
from lxml import etree as E

from core.amphibian_reproduction import (
    EGG_FIELDS, AMPHIBIAN_FIELDS, external_roe_rows,
    render_roe_defs, render_roe_patch,
)
from core.production_stats import pd_egg_map


def fixture():
    egg = list(EGG_FIELDS)
    milk = ["Name", "Food consumption (Nutrition/day)",
            "Milk fraction", "Milk yield"]
    wool = ["Name", "Food consumption (Nutrition/day)", "Wool type",
            "Wool fraction", "Wool yield per day", "Wool yield per 10 days"]
    # The five deprecated AP:AT duplicate headings deliberately remain:
    # the parser must ignore them instead of treating them as new species.
    duplicate_tail = list(AMPHIBIAN_FIELDS[2:7])
    headers = (egg + [""] + milk + [""] + wool + [""]
               + list(AMPHIBIAN_FIELDS) + duplicate_tail)
    rows = [[""] * len(headers) for _ in range(26)]

    def put(n, start, fields, values):
        for field, value in values.items():
            rows[n][start + fields.index(field)] = value

    # Normal egg table: first rows are unrelated birds. Frogs are far below.
    put(0, 0, egg, {
        "Name": "Ostrich", "Def name": "<li>Ostrich</li>",
        "hatcherDaystoHatch": 7, "eggLayIntervalDays": 18.8,
        "eggCountRange": "6~12", "Clutch size": 9,
    })
    put(1, 0, egg, {
        "Name": "Cassowary", "Def name": "<li>Cassowary</li>",
        "hatcherDaystoHatch": 8, "eggLayIntervalDays": 43.6,
        "eggCountRange": "3~5", "Clutch size": 4,
    })
    put(24, 0, egg, {
        "Name": "Bullfrog", "Def name": "<li>Bullfrog</li>",
        "hatcherDaystoHatch": 1, "eggLayIntervalDays": 56.1538461538,
        "eggCountRange": "9110~16920", "Clutch size": 13014,
    })
    put(25, 0, egg, {
        "Name": "ColossusToad", "Def name": "<li>ColossusToad</li>",
        "hatcherDaystoHatch": 1, "eggLayIntervalDays": 60,
        "eggCountRange": "21000~39000", "Clutch size": 30000,
    })

    amph_start = len(egg) + 1 + len(milk) + 1 + len(wool) + 1
    common = {
        "Hatched tadpoles (gameplay count range)": "200~400",
        "Froglets per clutch (gameplay count range)": "10~20",
        "Fertilization mode": "EXTERNAL",
        "Ovum diameter (mm)": 1.45,
        "Ovum gross energy (J)": 19.998268950437325,
        "Hatch tadpole wet mass (g)": 0.007,
        "Tadpole gross energy (MJ/kg wet)": 2.58,
        "Energy per Nutrition (J)": 6537500,
    }
    put(0, amph_start, list(AMPHIBIAN_FIELDS), {
        **common, "Name": "Bullfrog", "Def name": "<li>Bullfrog</li>",
        "Tadpole development (real days)": 365,
        "Tadpole development (RimWorld days)": 60,
        "Hydrated jelly diameter (mm)": 8.4, "Clutch jelly geometry": "SPHERE",
        "Wet mass per ovum + jelly (g)": 0.31033908869221416,
        "Postmetamorph newborn mass (kg)": 0.0122,
        "Mean clutch wet mass (kg)": 4.038752900240475,
        "Mean clutch Nutrition": 0.03980993837414782,
    })
    put(1, amph_start, list(AMPHIBIAN_FIELDS), {
        **common, "Name": "ColossusToad", "Def name": "<li>ColossusToad</li>",
        "Tadpole development (real days)": 50,
        "Tadpole development (RimWorld days)": 8,
        "Hydrated jelly diameter (mm)": 3.7, "Clutch jelly geometry": "STRING",
        "Egg string pitch (mm)": 1.45,
        "Wet mass per ovum + jelly (g)": 0.015590546242521049,
        "Postmetamorph newborn mass (kg)": 0.052,
        "Mean clutch wet mass (kg)": 0.4677163872756315,
        "Mean clutch Nutrition": 0.09177025904598389,
    })
    # Stale duplicate values deliberately contradict the true amphibian
    # block. They must not influence any output.
    rows[0][-5:] = [999, 999, "1~1", "1~1", "INTERNAL"]
    rows[1][-5:] = [999, 999, "1~1", "1~1", "INTERNAL"]
    # Simulate pandas' .1 suffixing of repeated source headings.
    counts = {}
    unique_headers = []
    for title in headers:
        count = counts.get(title, 0)
        counts[title] = count + 1
        unique_headers.append(f"{title}.{count}" if title and count else title)
    return pd.DataFrame(rows, columns=unique_headers)


def initial_defs():
    return E.ElementTree(E.fromstring("""
    <Defs>
      <ThingDef ParentName="EggUnfertBase">
        <defName>EggBullfrogUnfertilized</defName>
        <comps><li Class="ZoologyMod.CompProperties_AmphibianRoe, ZoologyMod">
          <parentRace>Bullfrog</parentRace><fertilized>false</fertilized>
        </li></comps>
      </ThingDef>
      <ThingDef ParentName="EggFertBase">
        <defName>EggBullfrogFertilized</defName>
        <comps><li Class="ZoologyMod.CompProperties_AmphibianRoe, ZoologyMod">
          <parentRace>Bullfrog</parentRace><fertilized>true</fertilized>
        </li></comps>
      </ThingDef>
      <ThingDef><defName>Zoology_BullfrogTadpole</defName>
        <thingClass>ThingWithComps</thingClass>
        <comps><li Class="ZoologyMod.CompProperties_Tadpole, ZoologyMod">
          <pawnKind>Bullfrog</pawnKind>
        </li></comps>
      </ThingDef>
    </Defs>
    """))


class AmphibianGenerationTests(unittest.TestCase):
    def test_join_is_by_defname_not_shared_sheet_row(self):
        source = fixture()
        species = external_roe_rows(source)
        self.assertEqual(set(species), {"Bullfrog", "ColossusToad"})
        self.assertEqual(species["Bullfrog"]["egg_range"], "9110~16920")
        self.assertEqual(species["Bullfrog"]["mean_eggs"], 13014)
        self.assertEqual(species["Bullfrog"]["hatch_days"], "1")
        self.assertEqual(species["Bullfrog"]["larval_days"], "60")
        self.assertEqual(species["ColossusToad"]["egg_range"], "21000~39000")
        self.assertEqual(species["ColossusToad"]["mean_eggs"], 30000)
        self.assertEqual(species["ColossusToad"]["larval_days"], "8")
        self.assertEqual(species["ColossusToad"]["froglet_range"], "10~20")

    def test_independent_xml_only_for_amphibians(self):
        species = external_roe_rows(fixture())
        defs = render_roe_defs(initial_defs(), species)
        self.assertEqual(len(defs.findall("./ThingDef")), 6)
        self.assertIsNotNone(defs.find('.//ThingDef[defName="Zoology_ColossusToadTadpole"]'))
        bull = defs.find('.//ThingDef[defName="EggBullfrogFertilized"]')
        self.assertEqual(bull.get("ParentName"), "EggUnfertBase")
        self.assertEqual(bull.findtext("./comps/li/defaultOvaCount"), "13014")
        self.assertEqual(bull.findtext("./comps/li/hatchlingCountRange"), "200~400")
        self.assertAlmostEqual(float(bull.findtext("./statBases/Mass")), 4.038752900240475)
        self.assertEqual(bull.findtext("./statBases/DeteriorationRate"), "0")
        tad = defs.find('.//ThingDef[defName="Zoology_BullfrogTadpole"]')
        self.assertEqual(tad.findtext("./comps/li/daysToMetamorphosis"), "60")
        self.assertEqual(tad.findtext("./ingestible/foodType"), "Meat")
        patch = render_roe_patch(species)
        self.assertEqual(len(patch.findall("./Operation")), 2)
        self.assertNotIn("Ostrich", E.tostring(patch).decode())
        self.assertNotIn("Cassowary", E.tostring(patch).decode())
        for op in patch.findall("./Operation"):
            self.assertEqual(
                op.findtext("./match/value/li/eggFertilizationCountMax"), "0")
        # Repeated generation does not create additional ThingDefs.
        self.assertEqual(len(render_roe_defs(defs, species).findall("./ThingDef")), 6)

    def test_amphibian_table_can_move_without_rewriting_egg_rows(self):
        base = fixture()
        species0 = external_roe_rows(base)
        # Shifting the isolated subtable leaves the base table untouched.
        moved = base.copy()
        moved.insert(22, "Other spacer", [""] * len(moved))
        species1 = external_roe_rows(moved)
        self.assertEqual(species0, species1)

    def test_unrelated_egg_layers_and_wool_are_unchanged(self):
        data = fixture()
        mapped = pd_egg_map(data)
        self.assertEqual(mapped.iloc[0]["F"], "<li>Ostrich</li>")
        self.assertEqual(mapped.iloc[24]["F"], "<li>Bullfrog</li>")
        self.assertEqual(mapped.iloc[0]["X"], "<li>Bullfrog</li>")
        self.assertEqual(mapped.iloc[0]["R"], "")
        self.assertEqual(set(external_roe_rows(data)), {"Bullfrog", "ColossusToad"})

    def test_invalid_unlinked_or_duplicate_species_fail_closed(self):
        missing = fixture()
        missing.iat[24, 5] = "<li>DifferentAnimal</li>"
        with self.assertRaisesRegex(ValueError, "no matching ordinary egg"):
            external_roe_rows(missing)

        duplicated = fixture()
        duplicated.iat[1, 23] = "<li>Bullfrog</li>"
        with self.assertRaisesRegex(ValueError, "duplicate amphibian DefName"):
            external_roe_rows(duplicated)

        missing_mode = fixture()
        missing_mode.iat[0, 28] = ""
        with self.assertRaisesRegex(ValueError, "has no fertilization mode"):
            external_roe_rows(missing_mode)

        uncalculated = fixture()
        uncalculated.iat[0, 34] = "#ERROR!"
        with self.assertRaisesRegex(ValueError, "ovumEnergyJ"):
            external_roe_rows(uncalculated)

        absent_header = fixture()
        headers = list(absent_header.columns)
        headers[35] = "wrong header"
        absent_header.columns = headers
        with self.assertRaisesRegex(ValueError, "unambiguous"):
            external_roe_rows(absent_header)


if __name__ == "__main__":
    unittest.main()
