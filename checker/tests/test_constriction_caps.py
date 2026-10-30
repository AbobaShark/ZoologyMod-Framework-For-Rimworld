import unittest
from pathlib import Path
from lxml import etree as E

class ConstrictionCapacityTests(unittest.TestCase):
    def test_every_stage_caps_moving_without_changing_other_capacities(self):
        path = Path(__file__).resolve().parents[2]/'Zoology/Core/Defs/HediffDefs/AnimalConstriction.xml'
        if not path.exists():self.skipTest('Needs full Zoology repository')
        stages = E.parse(str(path)).xpath('//stages/li')
        self.assertGreater(len(stages), 1)
        for stage in stages:
            moving = stage.xpath('./capMods/li[capacity="Moving"]')
            self.assertEqual(len(moving), 1)
            self.assertEqual(float(moving[0].findtext('setMax')), .15)
            self.assertIsNone(moving[0].find('offset'))
