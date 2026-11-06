"""Life-stage XML must be fully table-driven and safe to apply repeatedly."""
import copy
import unittest
import pandas as pd
from lxml import etree as E
from core.locomotion_allometry import age_speed_factor
from core.body_size_mass_equivalence import body_size_equivalent_mass_kg

from core.life_stages import (
    REQUIRED_COLUMNS, STAGE_GROUPS, ROYAL_SOURCE, ROYAL_STAGES, parse_stages,
    render_life_stages, render_life_stage_combat_power,
)


def fixture():
    labels = [' or '.join(group) for group in STAGE_GROUPS]
    rows = []
    for index, label in enumerate(labels):
        row = {name: 1.0 for name in REQUIRED_COLUMNS if name not in ('Life stage','Source mod')}
        row['Life stage'] = label
        row['Source mod'] = 'Core'
        row['bodySizeFactor'] = [1, .5, .2, .15][index]
        row['marketValueFactor'] = [1, .6, .3, .25][index]
        row['healthScaleFactor'] = row['bodySizeFactor']
        row['hungerRateFactor'] = [1, .6, .3, .25][index]
        row['foodMaxFactor'] = 1.0
        row['meleeDamageFactor'] = [.99, .65, .35, .3][index]
        row['meleeDamageFactor(CE)'] = [1, .65, .35, .3][index]
        row['MeleeAttackIntervalFactor'] = [1, .9, .8, .75][index]
        row['MeleeAttackIntervalFactor(CE)'] = [1, .9, .8, .75][index]
        row['SpeedFactor'] = [1, .9, .75, .7][index]
        row['FilthRate'] = row['bodySizeFactor']
        row['ArmorRating_Sharp'] = [1, .8, .6, .55][index]
        row['ArmorRating_Blunt'] = row['ArmorRating_Sharp']
        row['ArmorPenetration_Sharp(CE)'] = row['ArmorRating_Sharp']
        row['ArmorPenetration_Blunt(CE)'] = row['bodySizeFactor']
        row['MeleeDodgeChance'] = [1, 1.1, 1.3, 1.35][index]
        row['MeleeCritChance'] = [1, .9, .75, .75][index]
        row['MeleeParryChance'] = row['MeleeCritChance']
        row['CombatPowerFactor'] = [1, .6, .3, .25][index]
        if index == 0:
            row['meleeDamageFactor'] = 1.0
        rows.append(row)
    for group, size in zip(ROYAL_STAGES, (.2,4,13,15,20)):
        royal={name: 1.0 for name in REQUIRED_COLUMNS
               if name not in ('Life stage','Source mod')}
        royal.update({'Life stage':group[0],'Source mod':ROYAL_SOURCE,
                      'bodySizeFactor':size,'marketValueFactor':max(.8,size**.35),
                      'healthScaleFactor':size,'FilthRate':size,
                      'ArmorPenetration_Blunt(CE)':size,'CombatPowerFactor':size,
                      'foodMaxFactor':1.0,
                      'BaseBodySize':.3,
                      'StageBodySize':.3*size,
                      'BodySizeMassMid_kg':body_size_equivalent_mass_kg(.3*size),
                      'BaseMassMid_kg':body_size_equivalent_mass_kg(.3),
                      'SpeedFactor':age_speed_factor(size,.3),
                      'MeleeCritChance':round(size**.165/.05)*.05,
                      'MeleeParryChance':round(size**.165/.05)*.05})
        rows.append(royal)
    return pd.DataFrame(rows)


def starting_defs(add_royal=False):
    root = E.Element('Defs')
    for group in (STAGE_GROUPS + ROYAL_STAGES if add_royal else STAGE_GROUPS):
        for name in group:
            node = E.SubElement(root, 'LifeStageDef')
            E.SubElement(node, 'defName').text = name
            E.SubElement(node, 'meleeDamageFactor').text = '999'
            if name.endswith('Juvenile') or name == 'AnimalBaby':
                stat = E.SubElement(node, 'statFactors')
                E.SubElement(stat, 'MoveSpeed').text = '999'
                E.SubElement(stat, 'OtherModStat').text = '7'
    return root


def apply_patch(defs, output):
    """Minimal fixture evaluator of the RimWorld XML patch operation shapes."""
    def nodes(xpath):
        return [defs] if xpath == 'Defs' else defs.xpath(xpath)

    def execute(operation):
        node=operation
        if operation.get('Class')=='PatchOperationConditional':
            node=operation.find('match' if nodes(operation.findtext('xpath')) else 'nomatch')
            if node is None:
                return
        kind=node.get('Class')
        if kind=='PatchOperationSequence':
            for child in node.find('operations'):
                execute(child)
        elif kind=='PatchOperationRemove':
            for match in list(nodes(node.findtext('xpath'))):
                match.getparent().remove(match)
        elif kind=='PatchOperationAdd':
            for match in nodes(node.findtext('xpath')):
                for value in node.find('value'):
                    match.append(copy.deepcopy(value))
        elif kind is not None:
            raise AssertionError(f'Unsupported patch operation: {kind}')
    for operation in E.fromstring(output):
        execute(operation)

class LifeStageGenerationTests(unittest.TestCase):
    def test_exact_factors_for_all_seven_stage_defs(self):
        table = fixture()
        stages = parse_stages(table, 'Core')
        self.assertEqual(len(stages), 4)
        defs = starting_defs()
        apply_patch(defs, render_life_stages(table))
        for group, expected in stages:
            for name in group:
                nodes = defs.xpath(f'/Defs/LifeStageDef[defName="{name}"]')
                self.assertEqual(len(nodes), 1)
                node = nodes[0]
                for scalar in ('bodySizeFactor', 'marketValueFactor', 'healthScaleFactor',
                               'hungerRateFactor', 'foodMaxFactor', 'meleeDamageFactor'):
                    self.assertEqual(len(node.findall(scalar)), 1)
                    self.assertAlmostEqual(float(node.findtext(scalar)), expected[scalar])
                for stat, column in (
                    ('MoveSpeed','SpeedFactor'),('MeleeCooldownFactor','MeleeAttackIntervalFactor'),
                    ('FilthRate','FilthRate'),('ArmorRating_Sharp','ArmorRating_Sharp'),
                    ('ArmorRating_Blunt','ArmorRating_Blunt'),('MeleeDodgeChance','MeleeDodgeChance')):
                    found = node.findall('statFactors/' + stat)
                    self.assertEqual(len(found), 1)
                    self.assertAlmostEqual(float(found[0].text), expected[column])
                self.assertIsNone(node.find('statFactors/MeleeCritChance'))
        # Reapplying must not duplicate any managed factor or erase other mods' factors.
        apply_patch(defs, render_life_stages(table))
        self.assertEqual(len(defs.xpath('/Defs/LifeStageDef[defName="AnimalBaby"]/statFactors/MoveSpeed')),1)
        self.assertEqual(defs.xpath('/Defs/LifeStageDef[defName="AnimalBaby"]/statFactors/OtherModStat/text()'),['7'])

    def test_ce_only_factors_and_penetration(self):
        table = fixture()
        defs = starting_defs()
        apply_patch(defs, render_life_stages(table))
        apply_patch(defs, render_life_stages(table, ce=True))
        for group, expected in parse_stages(table, 'Core'):
            for name in group:
                stage = defs.xpath(f'/Defs/LifeStageDef[defName="{name}"]')[0]
                self.assertAlmostEqual(float(stage.findtext('meleeDamageFactor')), expected['meleeDamageFactor(CE)'])
                for stat, col in [('MeleeCooldownFactor','MeleeAttackIntervalFactor(CE)'),
                                  ('MeleeCritChance','MeleeCritChance'),('MeleeParryChance','MeleeParryChance')]:
                    self.assertAlmostEqual(float(stage.findtext('statFactors/'+stat)), expected[col])
                pen=defs.xpath(f'/Defs/LifeStagePenetrationDef[defName="{name}"]')
                self.assertEqual(len(pen),1)
                self.assertAlmostEqual(float(pen[0].findtext('meleePenetrationSharpFactor')),expected['ArmorPenetration_Sharp(CE)'])
                self.assertAlmostEqual(float(pen[0].findtext('meleePenetrationBluntFactor')),expected['ArmorPenetration_Blunt(CE)'])

    def test_combat_power_is_from_extension_and_original_attributes_survive(self):
        defs=starting_defs()
        apply_patch(defs,render_life_stage_combat_power(fixture()))
        for group, expected in parse_stages(fixture(), 'Core'):
            for name in group:
                stage=defs.xpath(f'/Defs/LifeStageDef[defName="{name}"]')[0]
                values=stage.xpath('modExtensions/li[@Class="ZoologyMod.LifeStageCombatPowerExtension, ZoologyMod"]/combatPowerFactor/text()')
                self.assertEqual(len(values),1)
                self.assertAlmostEqual(float(values[0]),expected['CombatPowerFactor'])
        apply_patch(defs,render_life_stage_combat_power(fixture()))
        self.assertEqual(len(defs.xpath('/Defs/LifeStageDef[defName="AnimalBaby"]/modExtensions/li')),1)

    def test_conditional_branches_are_valid_and_noop_is_successful(self):
        """Missing optional fields and existing containers must not log patch failures."""
        table=fixture()
        for payload in (render_life_stages(table),
                        render_life_stages(table,ce=True),
                        render_life_stages(table,ce=True,source_mod=ROYAL_SOURCE),
                        render_life_stage_combat_power(table)):
            root=E.fromstring(payload)
            for op in root.xpath('./Operation[@Class="PatchOperationConditional"]'):
                for attr in ('match','nomatch'):
                    branch=op.find(attr)
                    self.assertIsNotNone(branch)
                    self.assertIn(branch.get('Class'),
                                  ('PatchOperationRemove','PatchOperationAdd',
                                   'PatchOperationSequence'))
                    if branch.get('Class')=='PatchOperationAdd':
                        self.assertIsNotNone(branch.find('value'))
        defs=starting_defs(add_royal=True)
        apply_patch(defs,render_life_stages(table,source_mod=ROYAL_SOURCE))
        apply_patch(defs,render_life_stages(table,source_mod=ROYAL_SOURCE))
        for group,_ in parse_stages(table,ROYAL_SOURCE):
            name=group[0]
            stage=defs.xpath(f'/Defs/LifeStageDef[defName="{name}"]')[0]
            self.assertEqual(len(stage.findall('statFactors')),1)
            self.assertEqual(len(stage.findall('marketValueFactor')),1)

    def test_ce_melee_chances_scale_with_relative_body_size(self):
        table = fixture()
        for owner, stage_groups in (('Core', STAGE_GROUPS),
                                    (ROYAL_SOURCE, ROYAL_STAGES)):
            for group, stage in parse_stages(table, owner):
                b = stage['bodySizeFactor']
                expected = round(b ** 0.165 / 0.05) * 0.05
                self.assertAlmostEqual(stage['MeleeCritChance'], expected)
                self.assertAlmostEqual(stage['MeleeParryChance'], expected)
        table.loc[8, 'MeleeCritChance'] = 1
        with self.assertRaisesRegex(ValueError, 'MeleeCritChance'):
            render_life_stages(table, ce=True, source_mod=ROYAL_SOURCE)
        table = fixture()
        table.loc[2, 'MeleeParryChance'] = 1
        with self.assertRaisesRegex(ValueError, 'MeleeParryChance'):
            render_life_stages(table, ce=True)

    def test_melee_cooldown_stat_is_zoology_owned(self):
        table=fixture()
        for payload in (render_life_stages(table),
                        render_life_stages(table,ce=True),
                        render_life_stages(table,source_mod=ROYAL_SOURCE)):
            doc=E.fromstring(payload)
            self.assertTrue(doc.xpath('.//Zoology_MeleeCooldownFactor'))
            self.assertFalse(doc.xpath('.//MeleeCooldownFactor'))

    def test_no_missing_or_nonnumeric_columns_are_silently_accepted(self):
        df=fixture()
        with self.assertRaisesRegex(ValueError,'missing columns'):
            parse_stages(df.drop(columns=['FilthRate']))
        df.loc[1,'foodMaxFactor']='No'
        with self.assertRaisesRegex(ValueError,'foodMaxFactor'):
            render_life_stages(df)
        df=fixture()
        df.loc[0,'CombatPowerFactor']=.8
        with self.assertRaisesRegex(ValueError,'baseline'):
            render_life_stage_combat_power(df)


if __name__ == '__main__':
    unittest.main()
