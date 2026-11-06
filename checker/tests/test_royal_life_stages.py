"""Royal Animals' five custom megachicken stages are source-gated and allometric."""
import json
import unittest
from pathlib import Path

from lxml import etree as E
from tests.test_life_stages import fixture, starting_defs, apply_patch
from core.life_stages import (
    ROYAL_SOURCE, ROYAL_STAGES, parse_stages,
    render_life_stages, render_life_stage_combat_power,
)

ROOT=Path(__file__).resolve().parents[2]


class RoyalLifeStageTests(unittest.TestCase):
    def test_royal_source_contains_exactly_five_distinct_growth_sizes(self):
        table=fixture()
        actual=parse_stages(table,ROYAL_SOURCE)
        self.assertEqual([group[0] for group,_ in actual],
                         [group[0] for group in ROYAL_STAGES])
        self.assertEqual([value['bodySizeFactor'] for _,value in actual],
                         [.2,4,13,15,20])
        self.assertEqual({value['foodMaxFactor'] for _,value in actual},{1})

    def test_royal_main_and_combat_power_patches_do_not_touch_core_stages(self):
        table=fixture()
        defs=starting_defs(add_royal=True)
        core=defs.xpath('/Defs/LifeStageDef[defName="AnimalBaby"]')[0]
        royal=defs.xpath('/Defs/LifeStageDef[defName="VAERoy_AnimalAdult"]')[0]
        E.SubElement(royal,'reproductive').text='true'
        apply_patch(defs,render_life_stages(table,source_mod=ROYAL_SOURCE))
        self.assertEqual(core.findtext('meleeDamageFactor'),'999')
        self.assertEqual(royal.findtext('reproductive'),'true')
        for group,expected in parse_stages(table,ROYAL_SOURCE):
            stage=defs.xpath(f'/Defs/LifeStageDef[defName="{group[0]}"]')[0]
            for field in ('bodySizeFactor','healthScaleFactor','foodMaxFactor',
                          'hungerRateFactor','marketValueFactor','meleeDamageFactor'):
                self.assertAlmostEqual(float(stage.findtext(field)),expected[field])
            for stat,source in (('MoveSpeed','SpeedFactor'),('FilthRate','FilthRate'),
                                ('MeleeCooldownFactor','MeleeAttackIntervalFactor')):
                self.assertAlmostEqual(float(stage.findtext('statFactors/'+stat)),expected[source])
        apply_patch(defs,render_life_stage_combat_power(table,source_mod=ROYAL_SOURCE))
        for group,expected in parse_stages(table,ROYAL_SOURCE):
            values=defs.xpath(
                f'/Defs/LifeStageDef[defName="{group[0]}"]/modExtensions/li/combatPowerFactor/text()')
            self.assertEqual(len(values),1)
            self.assertAlmostEqual(float(values[0]),expected['CombatPowerFactor'])

    def test_ce_never_adds_royal_penetration_defs_when_royal_absent(self):
        payload=render_life_stages(fixture(),ce=True,source_mod=ROYAL_SOURCE)
        root=E.fromstring(payload)
        self.assertEqual(len(root),5)
        self.assertEqual(len(root.xpath('./Operation[@Class="PatchOperationConditional"]')),5)
        self.assertEqual(len(root.xpath('.//li[@Class="PatchOperationAdd"]/value/LifeStagePenetrationDef')),5)
        core=starting_defs()
        apply_patch(core,payload)
        self.assertEqual(len(core.findall('LifeStagePenetrationDef')),0)
        self.assertEqual(core.xpath('/Defs/LifeStageDef[defName="AnimalAdult"]/meleeDamageFactor/text()'),['999'])

    def test_ce_applies_all_five_royal_stages_when_present(self):
        table=fixture()
        defs=starting_defs(add_royal=True)
        apply_patch(defs,render_life_stages(table,source_mod=ROYAL_SOURCE))
        apply_patch(defs,render_life_stages(table,ce=True,source_mod=ROYAL_SOURCE))
        for group,expected in parse_stages(table,ROYAL_SOURCE):
            name=group[0]
            stage=defs.xpath(f'/Defs/LifeStageDef[defName="{name}"]')[0]
            self.assertAlmostEqual(float(stage.findtext('meleeDamageFactor')),
                                   expected['meleeDamageFactor(CE)'])
            self.assertAlmostEqual(float(stage.findtext('statFactors/MeleeCritChance')),
                                   expected['MeleeCritChance'])
            penetrations=defs.xpath(f'/Defs/LifeStagePenetrationDef[defName="{name}"]')
            self.assertEqual(len(penetrations),1)
            self.assertAlmostEqual(float(penetrations[0].findtext('meleePenetrationBluntFactor')),
                                   expected['ArmorPenetration_Blunt(CE)'])

    def test_source_mod_is_required_and_no_royal_age_can_be_dropped(self):
        df=fixture()
        df.loc[4,'Source mod']='Core'
        with self.assertRaisesRegex(ValueError,'source/stage'):
            render_life_stages(df)
        df=fixture().iloc[:-1]
        with self.assertRaisesRegex(ValueError,'Missing LifeStages'):
            render_life_stages(df)
        df=fixture()
        df.loc[6,'bodySizeFactor']=.05
        with self.assertRaisesRegex(ValueError,'strictly increase'):
            render_life_stages(df,source_mod=ROYAL_SOURCE)

    def test_megachicken_keeps_native_five_stage_age_list(self):
        registry=json.loads((ROOT/'checker/generation.json').read_text(encoding='utf8'))
        groups=[g for g in registry['groups'] if 'VAERoy_Megachicken' in g['defs']]
        self.assertEqual(len(groups),1)
        group=groups[0]
        self.assertEqual(group['overrides']['preserve_life_stages'],['VAERoy_Megachicken'])
        data=E.parse(str(ROOT/'Zoology'/group['destination']))
        self.assertFalse([
            node.text for node in data.xpath('//xpath')
            if node.text and 'VAERoy_Megachicken' in node.text
            and '/race/lifeStageAges' in node.text
        ])

    def test_ce_loads_after_royal_and_remains_ce_gated(self):
        tree=E.parse(str(ROOT/'Zoology/LoadFolders.xml'))
        for version in ('v1.6','v1.7'):
            roots=tree.xpath(f'/loadFolders/{version}/li')
            paths=[node.text for node in roots]
            royal=paths.index('ModPatches/VanillaAnimalsExpandedRoyal')
            ce=paths.index('ModPatches/CombatExtended')
            self.assertGreater(ce,royal)
            self.assertEqual(roots[ce].get('IfModActive'),'CETeam.CombatExtended')
            self.assertEqual(roots[royal].get('IfModActive'),'VanillaExpanded.VAERoy')


if __name__=='__main__':
    unittest.main()
