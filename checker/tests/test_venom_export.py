"""Venom capacity passthrough and runtime-XML contracts in both exports."""
import csv
import unittest
from pathlib import Path
import pandas as pd
from lxml import etree as E
from rimworld_xml_generator import build_tools_vanilla, build_tools_ce

ROOT = Path(__file__).resolve().parents[2]


def animal(capacity, latin='Test animal'):
    return pd.Series({'Latin name': latin, 'Bite damage': 1.5, 'Bite label': 'fangs',
                      'Bite capacities': capacity, 'Bite attack interval': 2,
                      'Bite linkedBodyPartsGroup': 'Mouth'})


class VenomExportTests(unittest.TestCase):
    def test_every_capacity_survives_both_tool_exports(self):
        capacities = E.parse(str(ROOT/'Zoology/Core/Defs/ToolCapacityDefs/ZVenom_ToolCapacities.xml'))
        for cap in capacities.xpath('/Defs/ToolCapacityDef/defName/text()') + ['VAEWaste_Sting', 'AA_ParalysingBite']:
            with self.subTest(capacity=cap):
                row = animal(cap)
                vanilla = build_tools_vanilla(row)
                ce = build_tools_ce(pd.Series({'Bite damage': 1.5, 'Bite Sharp AP': .2, 'Bite attack interval': 2}), row)
                self.assertEqual(vanilla.xpath('./li/capacities/li/text()'), [cap])
                self.assertEqual(ce.xpath('./li/capacities/li/text()'), [cap])
                self.assertEqual(ce.find('li').get('Class'), 'CombatExtended.ToolCE')
                self.assertEqual(ce.findtext('li/power'), '1.5')

    def test_special_species_effect_is_data_driven(self):
        for latin, capacity in [('Toxoscorpio bellator', 'VAEWaste_Sting'), ('Cimex titanus', 'AA_ParalysingBite')]:
            tools = build_tools_vanilla(animal(capacity, latin))
            self.assertEqual(tools.xpath('./li/capacities/li/text()'), [capacity])
        # A future source edit remains authoritative, without a species override in Checker.
        tools = build_tools_vanilla(animal('Bite', 'Cimex titanus'))
        self.assertEqual(tools.xpath('./li/capacities/li/text()'), ['Bite'])

    def test_spur_gender_does_not_replace_ce_tool_class(self):
        row = animal('ZVenom_PlatypusSpur')
        vanilla = build_tools_vanilla(row)
        ce = build_tools_ce(pd.Series({'Bite damage': 1.5, 'Bite attack interval': 2}), row)
        self.assertEqual(vanilla.find('li').get('Class'), 'ZoologyMod.ToolWithGender, ZoologyMod')
        self.assertEqual(vanilla.findtext('li/restrictedGender'), 'Male')
        self.assertEqual(ce.find('li').get('Class'), 'CombatExtended.ToolCE')
        self.assertEqual(ce.findtext('li/restrictedGender'), 'Male')

    def test_all_assignment_rows_accounted_for(self):
        with (ROOT/'docs/animal-behavior-1.7/venom-assignments.csv').open(encoding='utf-8-sig') as f:
            rows = list(csv.DictReader(f))
        self.assertEqual(len(rows), 37)
        exceptions = {r['Species_key']: r['Exported_capacity'] for r in rows if r['Status'] != 'accepted'}
        self.assertEqual(exceptions, {'Toxoscorpio bellator': 'VAEWaste_Sting', 'Cimex titanus': 'AA_ParalysingBite',
            'Hydra toxifera': 'ZVenom_CobraBite', 'Anolis audiens': 'Bite', 'Carcharofelis arenae': 'Bite',
            'Noctiluca ferox': 'ZVenom_VaranidBite', 'Noctiluca oculatus': 'ZVenom_VaranidBite',
            })
        for row in rows:
            self.assertTrue((ROOT/row['Patch']).is_file())

    def test_single_vanilla_injection_path(self):
        damage = E.parse(str(ROOT/'Zoology/Core/Defs/DamageDefs/ZVenom_Damages.xml'))
        hediffs = E.parse(str(ROOT/'Zoology/Core/Defs/HediffDefs/ZVenom_Hediffs.xml'))
        names = set(hediffs.xpath('/Defs/HediffDef/defName/text()'))
        for d in damage.getroot():
            self.assertEqual(len(d.findall('additionalHediffs/li')), 1)
            effect = d.find('additionalHediffs/li')
            self.assertIn(effect.findtext('hediff'), names)
            self.assertGreater(float(effect.findtext('severityPerDamageDealt')), 0)
            self.assertEqual(effect.findtext('victimSeverityScalingByInvBodySize'), 'true')
            self.assertIsNone(d.find('workerClass'))
        self.assertNotIn('ToxicBuildup', E.tostring(damage).decode())
        self.assertFalse(hediffs.xpath('//li[@Class="HediffCompProperties_RemoveIfOtherHediff"]'))

    def test_no_unassigned_venom_chains(self):
        with (ROOT/'docs/animal-behavior-1.7/venom-assignments.csv').open(encoding='utf-8-sig') as f:
            assigned = {r['Exported_capacity'] for r in csv.DictReader(f) if r['Exported_capacity'].startswith('ZVenom_')}
        capacities = E.parse(str(ROOT/'Zoology/Core/Defs/ToolCapacityDefs/ZVenom_ToolCapacities.xml'))
        self.assertEqual(set(capacities.xpath('/Defs/ToolCapacityDef/defName/text()')), assigned)
        hediffs = set(E.parse(str(ROOT/'Zoology/Core/Defs/HediffDefs/ZVenom_Hediffs.xml')).xpath('/Defs/HediffDef/defName/text()'))
        for outcome in E.parse(str(ROOT/'Zoology/Core/Defs/ThingDefs/ZVenom_AntivenomDrugs.xml')).xpath('//outcomeDoers/li'):
            self.assertIn(outcome.findtext('venom'), hediffs)


if __name__ == '__main__':
    unittest.main()
