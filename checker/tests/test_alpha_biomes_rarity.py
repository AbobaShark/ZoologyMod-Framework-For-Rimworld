"""Regression tests for explicitly configured biome-local animal spawn weights."""
import tempfile
import unittest
from pathlib import Path

import pandas as pd
from lxml import etree as E

from core.patch_engine import PatchGenerator


def _generated_values(rows):
    generator = PatchGenerator.__new__(PatchGenerator)
    generator.vanilla_df = pd.DataFrame(rows)
    with tempfile.TemporaryDirectory() as temp:
        paths = generator.generate_biome_patch_files(temp)
        result = {}
        for path in paths:
            root = E.parse(path)
            biome = Path(path).stem.removeprefix('Biomes_')
            containers = {}
            for tag in ('wildAnimals', 'pollutionWildAnimals', 'coastalWildAnimals'):
                nodes = [node for node in root.xpath('.//*[@Class="PatchOperationAdd"]')
                         if node.findtext('xpath') == f'/Defs/BiomeDef[defName="{biome}"]/{tag}']
                if nodes:
                    containers[tag] = {child.tag: child.text for child in nodes[-1].find('value')}
            result[biome] = containers
        return result


def _animal(**kwargs):
    row = {
        'XML name': '<li>AA_RareCat</li>',
        'Latin name': 'Felis rara',
        'MayRequire': 'sarg.alphaanimals',
        'WildBiomes': 'TemperateForest AB_OcularForest AB_RockyCrags',
        'Eco system number': '0.015',
        'Toxic eco system number': '0.01',
        'Special biomes': '',
        'Special biome weight': '',
        'Costal': 'false',
        'ModConflict': 'None',
    }
    row.update(kwargs)
    return row


class SpecialBiomeWeightTests(unittest.TestCase):
    def test_explicit_special_weight_changes_only_listed_biome(self):
        result = _generated_values([_animal(**{
            'Special biomes': 'AB_OcularForest',
            'Special biome weight': '0.15',
        })])
        self.assertEqual(result['AB_OcularForest']['wildAnimals']['AA_RareCat'], '0.15')
        self.assertEqual(result['TemperateForest']['wildAnimals']['AA_RareCat'], '0.015')
        self.assertEqual(result['AB_RockyCrags']['wildAnimals']['AA_RareCat'], '0.015')
        for biome in result:
            self.assertEqual(result[biome]['pollutionWildAnimals']['AA_RareCat'], '0.01')

    def test_no_special_biomes_preserves_rarity_in_alpha_biomes(self):
        result = _generated_values([_animal()])
        for biome in result:
            self.assertEqual(result[biome]['wildAnimals']['AA_RareCat'], '0.015')
            self.assertEqual(result[biome]['pollutionWildAnimals']['AA_RareCat'], '0.01')

    def test_does_not_depend_on_alpha_animals_mod_or_prefix(self):
        row = _animal(**{
            'XML name': '<li>OtherRare</li>',
            'MayRequire': 'other.mod',
            'Special biomes': 'TemperateForest AB_RockyCrags',
            'Special biome weight': '0.125',
        })
        result = _generated_values([row])
        self.assertEqual(result['TemperateForest']['wildAnimals']['OtherRare'], '0.125')
        self.assertEqual(result['AB_RockyCrags']['wildAnimals']['OtherRare'], '0.125')
        self.assertEqual(result['AB_OcularForest']['wildAnimals']['OtherRare'], '0.015')

    def test_old_tsv_without_special_columns_remains_compatible(self):
        row = _animal()
        del row['Special biomes']
        del row['Special biome weight']
        self.assertEqual(_generated_values([row])['AB_OcularForest']['wildAnimals']['AA_RareCat'], '0.015')

    def test_multiple_biomes_without_duplicate_entries(self):
        row = _animal(**{
            'Special biomes': 'AB_OcularForest, AB_OcularForest AB_RockyCrags',
            'Special biome weight': '0.2',
        })
        result = _generated_values([row])
        for biome in ('AB_OcularForest', 'AB_RockyCrags'):
            self.assertEqual(result[biome]['wildAnimals'], {'AA_RareCat': '0.2'})

    def test_localized_decimal_is_serialized_with_decimal_point(self):
        row = _animal(**{
            'Special biomes': 'AB_OcularForest',
            'Special biome weight': '0,15',
        })
        self.assertEqual(_generated_values([row])['AB_OcularForest']['wildAnimals']['AA_RareCat'], '0.15')

    def test_rejects_missing_or_invalid_weight(self):
        for weight in ('', 'No', 'nan', '-0.1', '0', 'inf'):
            with self.subTest(weight=weight):
                row = _animal(**{'Special biomes': 'AB_OcularForest', 'Special biome weight': weight})
                with self.assertRaisesRegex(ValueError, 'Special biome weight'):
                    _generated_values([row])

    def test_rejects_unknown_biome(self):
        row = _animal(**{
            'Special biomes': 'AB_NotInWildBiomes',
            'Special biome weight': '0.15',
        })
        with self.assertRaisesRegex(ValueError, 'not present in WildBiomes'):
            _generated_values([row])

    def test_modconflict_race_wildbiomes_uses_override(self):
        generator = PatchGenerator.__new__(PatchGenerator)
        row = pd.Series(_animal(**{
            'ModConflict': 'other.mod',
            'Special biomes': 'AB_OcularForest',
            'Special biome weight': '0.125',
        }))
        wild = generator._split_biomes(row['WildBiomes'])
        weights = generator._special_biome_weights(row, wild, 'AA_RareCat')
        node = generator._build_wild_biomes_element(wild, row['Eco system number'], weights)
        self.assertEqual(node.findtext('TemperateForest'), '0.015')
        self.assertEqual(node.findtext('AB_OcularForest'), '0.125')
        self.assertEqual(node.findtext('AB_RockyCrags'), '0.015')
        self.assertEqual(node.find('AB_OcularForest').get('MayRequire'), 'sarg.alphabiomes')

    def test_no_base_stats_israre_lookup_required(self):
        row = _animal(**{
            'Special biomes': 'AB_OcularForest',
            'Special biome weight': '0.1',
        })
        self.assertEqual(_generated_values([row])['AB_OcularForest']['wildAnimals']['AA_RareCat'], '0.1')


if __name__ == '__main__':
    unittest.main()
