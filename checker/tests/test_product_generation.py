import unittest
import pandas as pd
from lxml import etree as E

from core.product_generation import render_products

HEADERS = [
    'Name', 'Species', 'hatcherDaystoHatch', 'eggLayIntervalDays', 'eggCountRange',
    'Def name', 'Incubation time real life (days)', 'Eggs/year', 'Clutch size',
    'Nutrition (per ovum)', 'Hatchling XML name',
    'Name', 'Food consumption (Nutrition/day)', 'Milk fraction', 'Milk yield',
    'Name', 'Food consumption (Nutrition/day)', 'Wool type', 'Wool fraction',
    'Wool yield per day', 'Wool yield per 10 days', 'Wool XML name',
]


class ProductGenerationTests(unittest.TestCase):
    def test_one_record_per_species_and_explicit_hatchling_and_wool_identity(self):
        frame = pd.DataFrame([
            ['Spider', '', 7, 12, '1~2', '<li>AA_Spider</li>', 42, 12, 2,
             0.8, '<li>AA_Larva</li>', 'Cow', 1.68, .55, 18,
             'Muskox', 1.2, 'Wool', .25, 6.5, 65, '<li>Muskox</li>'],
            ['Frog', '', 1, 60, '100~200', '<li>Frog</li>', 5, 1000, 150,
             .00001, '<li>Frog</li>', '', '', '', '',
             'Muskox', 1.4, 'Wool', .25, 7, 70, '<li>AEXP_Muskox</li>'],
        ], columns=HEADERS)
        animals = pd.DataFrame([
            {'Common name': x, 'XML name': f'<li>{d}</li>'}
            for x, d in [('Spider','AA_Spider'), ('Frog','Frog'), ('Cow','Cow'),
                         ('Muskox','Muskox'), ('Muskox','AEXP_Muskox')]
        ])
        payload, rows = render_products(frame, animals, {'Frog'})
        root = E.fromstring(payload)
        self.assertEqual(set(rows), {'AA_Spider', 'Cow', 'Muskox', 'AEXP_Muskox'})
        self.assertEqual(root.xpath('string(//li[animal="AA_Spider"]/hatchling)'), 'AA_Larva')
        self.assertEqual(rows['AA_Spider']['eggCount'], '1~2')
        self.assertEqual(rows['Cow']['milkPerDay'], '18')
        self.assertEqual(rows['Muskox']['woolPerDay'], '6.5')
        self.assertEqual(rows['AEXP_Muskox']['woolPerDay'], '7')
        self.assertEqual(root.xpath('string(/Patch/Operation/@Class)'), 'ZoologyMod.PatchOperation_AnimalProducts')


if __name__ == '__main__':
    unittest.main()
