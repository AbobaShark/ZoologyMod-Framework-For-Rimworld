import tempfile
import unittest
from pathlib import Path
import pandas as pd
from lxml import etree as E
from core.venom_profiles import VenomCatalog


class VenomCatalogTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.mod = Path(self.temp.name)
        for folder in ('ToolCapacityDefs', 'ManeuverDefs', 'DamageDefs', 'HediffDefs'):
            (self.mod/'Core/Defs'/folder).mkdir(parents=True)
        data = {
            'ToolCapacityDefs': '<ToolCapacityDef><defName>CapA</defName></ToolCapacityDef><ToolCapacityDef><defName>CapB</defName></ToolCapacityDef>',
            'ManeuverDefs': '<ManeuverDef><defName>ManA</defName><requiredCapacity>CapA</requiredCapacity><verb><meleeDamageDef>DmgA</meleeDamageDef></verb></ManeuverDef><ManeuverDef><defName>ManB</defName><requiredCapacity>CapB</requiredCapacity><verb><meleeDamageDef>DmgB</meleeDamageDef></verb></ManeuverDef>',
            'DamageDefs': '<DamageDef><defName>DmgA</defName><additionalHediffs><li><hediff>VenomA</hediff></li></additionalHediffs></DamageDef><DamageDef><defName>DmgB</defName><additionalHediffs><li><hediff>VenomA</hediff></li></additionalHediffs></DamageDef>',
            'HediffDefs': '<HediffDef><defName>VenomA</defName></HediffDef>',
        }
        for folder, body in data.items(): (self.mod/'Core/Defs'/folder/'defs.xml').write_text('<Defs>'+body+'</Defs>')
        self.profiles = pd.DataFrame([
            {'ToolCapacity': 'CapA', 'Venom HediffDef': 'VenomA', 'Has antivenom': True, 'Profile label': 'Example', 'Color hex': '#123456', 'DamageDef': 'DmgA', 'ManeuverDef': 'ManA'},
            {'ToolCapacity': 'CapB', 'Venom HediffDef': 'VenomA', 'Has antivenom': True, 'Profile label': 'Example', 'Color hex': '#123456', 'DamageDef': 'DmgB', 'ManeuverDef': 'ManB'},
        ])

    def test_multiple_capacities_share_one_generated_toxin(self):
        catalog = VenomCatalog(self.profiles, self.mod)
        output = catalog.render_item_defs()
        for root in output.values(): self.assertEqual(len(root), 1)
        self.assertEqual(output['ZVenom_Samples.xml'][0].findtext('defName'), 'ZVenom_Sample_Example')
        self.assertEqual(output['ZVenom_AntivenomDrugs.xml'].xpath('//venom/text()'), ['VenomA'])
        row = {'Latin name': 'test', 'Venom sample producer': True, 'Bite capacities': 'CapA, CapB'}
        self.assertEqual(catalog.sample_for(row), 'ZVenom_Sample_Example')

    def test_conflicting_fields_and_missing_chain_are_rejected(self):
        self.profiles.loc[1, 'Color hex'] = '#FFFFFF'
        with self.assertRaisesRegex(ValueError, 'conflicting'): VenomCatalog(self.profiles, self.mod)
        self.profiles.loc[1, 'Color hex'] = '#123456'
        self.profiles.loc[1, 'DamageDef'] = 'missing'
        with self.assertRaisesRegex(ValueError, 'missing DamageDef'): VenomCatalog(self.profiles, self.mod)

    def test_false_producer_has_no_sample(self):
        catalog = VenomCatalog(self.profiles, self.mod)
        self.assertIsNone(catalog.sample_for({'Venom sample producer': False}))

    def test_unknown_zvenom_capacity_is_not_silently_accepted(self):
        catalog = VenomCatalog(self.profiles, self.mod)
        row = {'Latin name': 'example', 'Constrictor': False, 'Venomous': False,
               'Venom sample producer': False, 'Bite capacities': 'ZVenom_Missing',
               'Poke/leg claws capacities': '', 'Horn/Antler/Tusks capacities': '',
               'Paw claw/punch capacities': ''}
        with self.assertRaisesRegex(ValueError, 'missing from Venom profiles'):
            catalog.decorate(pd.DataFrame([row]))
