"""Integration-oriented tests for Checker wool and silk material generation."""
from __future__ import annotations

import csv
import json
from pathlib import Path

import pytest
from lxml import etree as E

from core.textile_generation import ROUTES, read_textiles, render_textile_outputs
from core.rimworld_patch_apply import PatchApplier

CHECKER = Path(__file__).resolve().parents[1]
MOD = CHECKER.parent / 'Zoology'
HEAD = ('Name', 'Type', 'Source mod', 'MarketValue', 'Beauty',
        'MaxHitPoints factor', 'StuffPower_Armor_Sharp',
        'StuffPower_Armor_Blunt', 'StuffPower_Armor_Heat',
        'StuffPower_Insulation_Cold', 'StuffPower_Insulation_Heat',
        'Flammability factor', 'Color hex', 'CE Armor Sharp',
        'CE Armor Blunt', 'CE Armor Heat')


def fixture(tmp_path):
    xml = tmp_path / 'OriginalXML'
    core = xml / 'Core' / 'Data' / 'Core' / 'Defs'
    core.mkdir(parents=True)
    (core / 'Items_Resource_Stuff.xml').write_text('''<Defs>
      <ThingDef Name="WoolBase" ParentName="ResourceBase" Abstract="True">
        <statBases><MarketValue>2.7</MarketValue>
          <StuffPower_Armor_Sharp>0.36</StuffPower_Armor_Sharp>
          <StuffPower_Armor_Blunt>0</StuffPower_Armor_Blunt>
          <StuffPower_Armor_Heat>1.1</StuffPower_Armor_Heat>
          <StuffPower_Insulation_Cold>26</StuffPower_Insulation_Cold>
          <StuffPower_Insulation_Heat>10</StuffPower_Insulation_Heat>
        </statBases><stuffProps><statFactors>
          <Beauty>1.5</Beauty><Flammability>1.7</Flammability>
          <MaxHitPoints>1</MaxHitPoints></statFactors></stuffProps>
      </ThingDef>
      <ThingDef ParentName="WoolBase"><defName>WoolSheep</defName>
        <stuffProps><color>(219,214,209)</color></stuffProps>
      </ThingDef>
    </Defs>''', encoding='utf8')
    vae = xml / 'Vanilla Animals Expanded' / 
    vae.mkdir(parents=True)
    (vae / 'Items_Resource_WoolLeather.xml').write_text('''<Defs>
      <ThingDef ParentName="WoolBase"><defName>WoolCamel</defName>
        <statBases><StuffPower_Insulation_Cold>10</StuffPower_Insulation_Cold>
          <StuffPower_Insulation_Heat>26</StuffPower_Insulation_Heat></statBases>
        <stuffProps><color>(219,214,209)</color></stuffProps>
      </ThingDef>
    </Defs>''', encoding='utf8')
    ce = tmp_path / 'OriginalXML' / 'CEPatches' / 
    ce.mkdir(parents=True)
    (ce / 'Items_Resource_Stuff.xml').write_text('''<Patch>
      <Operation Class="PatchOperationReplace">
        <xpath>Defs/ThingDef[@Name="WoolBase"]/statBases/StuffPower_Armor_Sharp</xpath>
        <value><StuffPower_Armor_Sharp>0.01</StuffPower_Armor_Sharp></value>
      </Operation>
      <Operation Class="PatchOperationReplace">
        <xpath>Defs/ThingDef[@Name="WoolBase"]/statBases/StuffPower_Armor_Blunt</xpath>
        <value><StuffPower_Armor_Blunt>0.044</StuffPower_Armor_Blunt></value>
      </Operation>
      <Operation Class="PatchOperationReplace">
        <xpath>Defs/ThingDef[@Name="WoolBase"]/statBases/StuffPower_Armor_Heat</xpath>
        <value><StuffPower_Armor_Heat>0.05</StuffPower_Armor_Heat></value>
      </Operation>
    </Patch>''', encoding='utf8')
    tsv = tmp_path / 'Animal_textiles.tsv'
    records = [
        ('WoolSheep','Wool','Core','2,7','1,5','1,','0,36','0,','1,1','26,','10,','1,7','#DBD6D1','0,01','0,044','0,05'),
        ('WoolCamel','Wool','Vanilla Animals Expanded','2,7','1,5','1,','0,36','0,','1,1','28,','14,','1,7','#DBD6D1','0,01','0,044','0,05')
    ]
    with tsv.open('w', encoding='utf8', newline='') as out:
        writer = csv.writer(out, delimiter='\t')
        writer.writerow(HEAD)
        writer.writerows(records)
    return tsv, xml, tmp_path / 'OriginalXML' / 'CEPatches'


def run_generator(tsv, xml, patches, ce=True):
    entries = [{'destination': path} for path in ROUTES.values()]
    outputs, report = render_textile_outputs(
        tsv, entries, original_xml=xml, original_patches=patches, ce=ce)
    return {path: E.fromstring(payload) for path, payload in outputs}, report


def test_live_sheet_decimal_style_and_camel_only_differences(tmp_path):
    tsv, xml, patches = fixture(tmp_path)
    data = read_textiles(tsv)
    assert data['WoolCamel']['StuffPower_Insulation_Cold'] == 28
    outputs, report = run_generator(tsv, xml, patches)
    assert len(outputs[ROUTES['Core']]) == 0
    vae = outputs[ROUTES['Vanilla Animals Expanded']]
    assert len(vae.xpath('./Operation')) == 2
    assert set(vae.xpath('//value/StuffPower_Insulation_Cold/text()')) == {'28'}
    assert set(vae.xpath('//value/StuffPower_Insulation_Heat/text()')) == {'14'}
    assert not report['ce_overrides']
    assert report['originals_available'] == 2


def test_ce_reasserts_vanilla_change_when_required(tmp_path):
    tsv, xml, patches = fixture(tmp_path)
    raw = tsv.read_text(encoding='utf8')
    tsv.write_text(raw.replace('0,36\t0,\t1,1\t28,',
                               '0,42\t0,\t1,1\t28,'), encoding='utf8')
    outputs, report = run_generator(tsv, xml, patches)
    vae = outputs[ROUTES['Vanilla Animals Expanded']]
    assert any('WoolCamel' in xp for xp in vae.xpath('./Operation/xpath/text()'))
    wrapper = vae.xpath('./Operation[@Class="PatchOperationFindMod"]')
    assert len(wrapper) == 1
    assert 'Combat Extended' in wrapper[0].xpath('./mods/li/text()')
    assert '0.01' in wrapper[0].xpath('.//StuffPower_Armor_Sharp/text()')
    assert report['ce_overrides']


def test_generated_camel_ops_apply_with_and_without_ce(tmp_path):
    tsv, xml, patches = fixture(tmp_path)
    outputs, _ = run_generator(tsv, xml, patches)
    patch = outputs[ROUTES['Vanilla Animals Expanded']]
    for active in ([], ['Combat Extended']):
        runtime = E.Element('Defs')
        material = E.SubElement(runtime, 'ThingDef')
        E.SubElement(material, 'defName').text = 'WoolCamel'
        stats = E.SubElement(material, 'statBases')
        for tag, value in [('StuffPower_Insulation_Cold','10'),
                           ('StuffPower_Insulation_Heat','26')]:
            E.SubElement(stats, tag).text = value
        applier = PatchApplier(active_mods=active)
        assert applier.apply_patch_root(patch, runtime)
        assert runtime.xpath('string(ThingDef/statBases/StuffPower_Insulation_Cold)') == '28'
        assert runtime.xpath('string(ThingDef/statBases/StuffPower_Insulation_Heat)') == '14'


def test_missing_original_material_is_fatal(tmp_path):
    tsv, xml, patches = fixture(tmp_path)
    for path in (xml / 'Vanilla Animals Expanded').rglob('*.xml'):
        path.unlink()
    with pytest.raises(ValueError, match='missing original ThingDefs.*WoolCamel'):
        run_generator(tsv, xml, patches)


def test_missing_ce_original_is_fatal_only_if_ce_enabled(tmp_path):
    tsv, xml, patches = fixture(tmp_path)
    for path in patches.rglob('*.xml'):
        path.unlink()
    with pytest.raises(ValueError, match='CE WoolBase reference'):
        run_generator(tsv, xml, patches)
    outputs, _ = run_generator(tsv, xml, patches, ce=False)
    assert len(outputs) == len(ROUTES)


def test_no_handwritten_camel_or_duplicate_routes():
    assert not (MOD/'ModPatches/VanillaAnimalsExpanded/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_VAE.xml').exists()
    registry = json.loads((CHECKER/'generation.json').read_text(encoding='utf8'))
    entries = [e for e in registry['outputs'] if e['pipeline_category'] == 'textiles']
    assert {e['destination'] for e in entries} == set(ROUTES.values())
    assert len(entries) == len(ROUTES)
    assert len({e['destination'] for e in registry['outputs']}) == len(registry['outputs'])


def test_no_odyssey_upstream_wool_is_guarded_in_vanilla_and_ce(tmp_path):
    tsv, xml, patches = fixture(tmp_path)
    vae = xml / 'Vanilla Animals Expanded' / '1.6NotOdyssey' / 'Defs'
    vae.mkdir(parents=True)
    (vae / 'Items_Resource_WoolLeather_NO.xml').write_text('''<Defs>
      <ThingDef ParentName="WoolBase"><defName>AEXP_MuskOxWool</defName>
        <statBases><MarketValue>7.2</MarketValue>
          <StuffPower_Armor_Sharp>0.96</StuffPower_Armor_Sharp>
          <StuffPower_Insulation_Cold>39</StuffPower_Insulation_Cold>
          <StuffPower_Insulation_Heat>4</StuffPower_Insulation_Heat></statBases>
        <stuffProps><color>(94,76,66)</color>
          <statFactors><Beauty>1.75</Beauty></statFactors></stuffProps>
      </ThingDef></Defs>''', encoding='utf8')
    with tsv.open('a', encoding='utf8', newline='') as out:
        csv.writer(out, delimiter='\t').writerow([
            'AEXP_MuskOxWool','Wool','Vanilla Animals Expanded',
            '7,2','1,75','1','0,96','0','1,1','39','4','1,7',
            '#5E4C42','0,02','0,03','0,05'
        ])
    outputs, report = run_generator(tsv, xml, patches)
    assert report['absent_mod_guards'] == {'AEXP_MuskOxWool': 'Odyssey'}
    vae_patch = outputs[ROUTES['Vanilla Animals Expanded']]
    guarded = vae_patch.xpath('./Operation[@Class="PatchOperationFindMod"]'
                              '[mods/li="Odyssey"]')
    assert len(guarded) == 1
    assert not guarded[0].xpath('./match')
    assert guarded[0].xpath('./nomatch/operations/li[@Class="PatchOperationFindMod"]'
                           '[mods/li="Combat Extended"]')
    # Other VAE materials (WoolCamel) remain outside the Odyssey guard.
    assert len(vae_patch.xpath('./Operation[@Class="PatchOperationConditional"]')) == 2
    # VAE+Odyssey: NoOdyssey defs are absent, so the guarded operations must
    # be skipped without generating missing-def patch failures.
    runtime = E.Element('Defs')
    applier = PatchApplier(active_mods=['Vanilla Animals Expanded','Odyssey',
                                         'Combat Extended'], record_missing=True)
    assert applier.apply_patch_root(guarded[0], runtime)
    assert not applier.missing_targets
    # VAE without Odyssey: both Vanilla and CE changes may target that def.
    runtime = E.Element('Defs')
    item = E.SubElement(runtime, 'ThingDef')
    E.SubElement(item, 'defName').text = 'AEXP_MuskOxWool'
    stats = E.SubElement(item, 'statBases')
    E.SubElement(stats, 'StuffPower_Armor_Sharp').text = '0.96'
    E.SubElement(stats, 'StuffPower_Armor_Blunt').text = '0.044'
    applier = PatchApplier(active_mods=['Vanilla Animals Expanded',
                                        'Combat Extended'], record_missing=True)
    assert applier.apply_patch_root(guarded[0], runtime)
    assert not applier.missing_targets
    assert runtime.xpath('string(ThingDef/statBases/StuffPower_Armor_Sharp)') == '0.02'
    assert runtime.xpath('string(ThingDef/statBases/StuffPower_Armor_Blunt)') == '0.03'
