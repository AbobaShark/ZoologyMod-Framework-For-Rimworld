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
from core.production_stats import pd_egg_map, _index
from core.product_generation import render_products
from core.animalstats_source import _values_to_dataframe


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
        "Fertilization mode": "EXTERNAL",
        "Ovum diameter (mm)": 1.45,
        "Ovum gross energy (J)": 19.998268950437325,
        "Hatch tadpole wet mass (g)": 0.007,
        "Tadpole gross energy (MJ/kg wet)": 2.58,
        "Energy per Nutrition (J)": 6537500,
    }
    put(0, amph_start, list(AMPHIBIAN_FIELDS), {
        **common, "Name": "Bullfrog", "Def name": "<li>Bullfrog</li>",
        "Embryo survival": 0.92,
        "Tadpole survival to metamorphosis": "5,62%",
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
        "Embryo survival": "71,8%",
        "Tadpole survival to metamorphosis": 0.05,
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
    rows[0][-5:] = [999, 999, 0, 0, "INTERNAL"]
    rows[1][-5:] = [999, 999, 0, 0, "INTERNAL"]
    # Simulate pandas' .1 suffixing of repeated source headings.
    counts = {}
    unique_headers = []
    for title in headers:
        count = counts.get(title, 0)
        counts[title] = count + 1
        unique_headers.append(f"{title}.{count}" if title and count else title)
    return pd.DataFrame(rows, columns=unique_headers, dtype=object)


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
        self.assertEqual(species["Bullfrog"]["embryo_survival"], "0.92")
        self.assertEqual(species["Bullfrog"]["tadpole_survival"], "0.0562")
        self.assertEqual(species["ColossusToad"]["embryo_survival"], "0.718")
        self.assertEqual(species["ColossusToad"]["tadpole_survival"], "0.05")

    def test_independent_xml_only_for_amphibians(self):
        species = external_roe_rows(fixture())
        defs = render_roe_defs(initial_defs(), species)
        self.assertEqual(len(defs.findall("./ThingDef")), 6)
        self.assertIsNotNone(defs.find('.//ThingDef[defName="Zoology_ColossusToadTadpole"]'))
        bull = defs.find('.//ThingDef[defName="EggBullfrogFertilized"]')
        self.assertEqual(bull.get("ParentName"), "EggUnfertBase")
        self.assertEqual(bull.findtext("./comps/li/defaultOvaCount"), "13014")
        self.assertEqual(bull.findtext("./comps/li/embryoSurvival"), "0.92")
        self.assertAlmostEqual(float(bull.findtext("./statBases/Mass")), 4.038752900240475)
        self.assertEqual(bull.findtext("./statBases/DeteriorationRate"), "0")
        tad = defs.find('.//ThingDef[defName="Zoology_BullfrogTadpole"]')
        self.assertEqual(tad.findtext("./comps/li/daysToMetamorphosis"), "60")
        self.assertEqual(tad.findtext("./comps/li/tadpoleSurvival"), "0.0562")
        self.assertEqual(tad.findtext("./comps/li/sourceRoeDef"), "EggBullfrogFertilized")
        self.assertEqual(tad.findtext("./statBases/Mass"), "0.083811")
        self.assertAlmostEqual(float(tad.findtext("./statBases/Nutrition")), 0.03307569866156788)
        toad = defs.find('.//ThingDef[defName="Zoology_ColossusToadTadpole"]')
        self.assertEqual(toad.findtext("./comps/li/tadpoleSurvival"), "0.05")
        self.assertEqual(toad.findtext("./statBases/Mass"), "0.15078")
        self.assertEqual(toad.findtext("./comps/li/sourceRoeDef"), "EggColossusToadFertilized")
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

    def test_reordered_amphibian_fields_keep_primary_record(self):
        base = fixture()
        headers = list(base.columns)
        # Move the egg Species field and the aquatic Def name and last field
        # without crossing the Name-led section boundaries.
        order = list(range(len(headers)))
        for a, b in ((1, 7), (23, 38), (24, 40)):
            order[a], order[b] = order[b], order[a]
        self.assertEqual(external_roe_rows(base.iloc[:, order]), external_roe_rows(base))

    def test_unrelated_egg_layers_and_wool_are_unchanged(self):
        data = fixture()
        mapped = pd_egg_map(data)
        self.assertEqual(mapped.iloc[0]["Def name"], "<li>Ostrich</li>")
        self.assertEqual(mapped.iloc[24]["Def name"], "<li>Bullfrog</li>")
        self.assertEqual(mapped.iloc[0]["Wool animal"], "")
        self.assertEqual(set(external_roe_rows(data)), {"Bullfrog", "ColossusToad"})

    def test_survival_probabilities_validate_and_include_endpoints(self):
        for value in ("", "NaN", "-1%", 1.01, "200~400"):
            data = fixture()
            data.iat[0, 26] = value
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, "embryoSurvival"):
                external_roe_rows(data)
        data = fixture()
        data.iat[0, 26] = 0
        data.iat[0, 27] = 1
        species = external_roe_rows(data)
        self.assertEqual(species["Bullfrog"]["embryo_survival"], "0")
        self.assertEqual(species["Bullfrog"]["tadpole_survival"], "1")

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

    def test_current_eggmilkwool_layout_and_reordered_fields(self):
        base = fixture()
        # The sheet has 52 columns: egg outputs, milk item Nutrition,
        # wool XML identity, aquatic block, and the repeated projection.
        raw = base.copy()
        raw.insert(9, 'Nutrition (per ovum)', [0.5] * len(raw))
        raw.insert(10, 'Formation cost / clutch (N)', [0] * len(raw))
        raw.insert(11, 'Gravid extra feed (N/d)', [0] * len(raw))
        raw.insert(12, 'Hatchling XML name', ['<li>Ostrich</li>', '<li>Cassowary</li>'] + [''] * 24)
        raw.insert(18, 'Milk item Nutrition', [0.05] * len(raw))
        raw.insert(26, 'Wool XML name', ['<li>Muskox</li>', '<li>AEXP_Muskox</li>'] + [''] * 24)
        self.assertEqual(len(raw.columns), 52)
        raw.iat[0, 14] = 'Cow'
        raw.iat[0, 17] = 18
        raw.iloc[:2, 20] = ['Muskox', 'Muskox']
        raw.iloc[:2, 24] = [6, 7]
        animals = pd.DataFrame([
            {'Common name': name, 'XML name': f'<li>{slug}</li>'}
            for name, slug in [('Cow', 'Cow'), ('Muskox', 'Muskox'), ('Muskox', 'AEXP_Muskox')]
        ])
        # Google Sheets disambiguates duplicate headings differently from pandas.
        headers = [str(c).rsplit('.', 1)[0] if str(c).rsplit('.', 1)[-1].isdigit()
                   else c for c in raw.columns]
        live = _values_to_dataframe([headers] + raw.values.tolist())
        amphibians = external_roe_rows(live)
        xml, records = render_products(live, animals, amphibians)
        self.assertEqual(set(records), {'Ostrich', 'Cassowary', 'Cow', 'Muskox', 'AEXP_Muskox'})
        eggs, milk, wool, ambiguous = _index({'EggMilk': pd_egg_map(live), 'Animals': animals})
        self.assertEqual(set(wool), {'Muskox', 'AEXP_Muskox'})
        self.assertFalse(ambiguous)
        # Reproductive inputs are inserted after Hatchling XML name (M).
        extended = live.copy()
        extended.insert(13, 'Reproductive pause (real days)', [15] + [0] * (len(extended) - 1))
        extended.insert(14, 'Active clutch formation (real days)', [18] * len(extended))
        self.assertEqual(len(extended.columns), 54)
        self.assertEqual(external_roe_rows(extended), amphibians)
        self.assertEqual(render_products(extended, animals, amphibians), (xml, records))
        self.assertEqual(pd_egg_map(extended).to_dict('records'), pd_egg_map(live).to_dict('records'))
        # Move fields within each Name-led table, keeping separators.
        order = list(range(52))
        for a, b in ((1, 12), (2, 8), (15, 18), (21, 26), (29, 46)):
            order[a], order[b] = order[b], order[a]
        moved = live.iloc[:, order]
        self.assertEqual(external_roe_rows(moved), amphibians)
        self.assertEqual(render_products(moved, animals, amphibians), (xml, records))
        self.assertEqual(pd_egg_map(moved).to_dict('records'), pd_egg_map(live).to_dict('records'))



if __name__ == "__main__":
    unittest.main()
