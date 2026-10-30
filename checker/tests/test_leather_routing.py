"""Regression tests for leather ownership and CE/Vanilla source-differential routing."""
from __future__ import annotations

from pathlib import Path

import pytest
from lxml import etree as E

from core.leather_overrides import GROUP_ROUTES, render_original_overrides
from core.rimworld_patch_apply import PatchApplier


def _write(root, path, data):
    file = root / path
    file.parent.mkdir(parents=True, exist_ok=True)
    file.write_text(data, encoding='utf8')
    return file


def _row(owner, sharp, blunt, ce_sharp, ce_blunt, *, market=8):
    return {'Source mod': owner, 'MarketValue': market, 'Beauty': 1.0,
            'MaxHitPoints': 2.0, 'StuffPower_Armor_Sharp': sharp,
            'StuffPower_Armor_Blunt': blunt,
            'StuffPower_Insulation_Cold': 32.0,
            'StuffPower_Insulation_Heat': 22.0,
            'Color hex': '#685534',
            'CE Armor Sharp': ce_sharp, 'CE Armor Blunt': ce_blunt}


def _refs(tmp_path, *, ce=True, old_duplicates=False):
    originals = tmp_path / 'OriginalXML'
    patches = tmp_path / 'OriginalPatches'
    _write(originals, 'Core/_Leathers/1.7/Defs/Items_Resource_Stuff_Leather.xml', '''
      <Defs><ThingDef Name="LeatherBase" Abstract="True">
       <statBases><MarketValue>2.1</MarketValue>
        <StuffPower_Armor_Sharp>0.81</StuffPower_Armor_Sharp>
        <StuffPower_Armor_Blunt>0.24</StuffPower_Armor_Blunt>
        <StuffPower_Insulation_Cold>16</StuffPower_Insulation_Cold>
        <StuffPower_Insulation_Heat>16</StuffPower_Insulation_Heat></statBases>
       <stuffProps><color>(100,80,50)</color><statFactors>
        <Beauty>1</Beauty><MaxHitPoints>1</MaxHitPoints>
       </statFactors></stuffProps>
      </ThingDef>
      <ThingDef ParentName="LeatherBase"><defName>Leather_Elephant</defName>
       <statBases><MarketValue>3</MarketValue>
        <StuffPower_Armor_Sharp>1.12</StuffPower_Armor_Sharp>
        <StuffPower_Armor_Blunt>0.24</StuffPower_Armor_Blunt>
        <StuffPower_Insulation_Cold>14</StuffPower_Insulation_Cold>
        <StuffPower_Insulation_Heat>12</StuffPower_Insulation_Heat></statBases>
       <stuffProps><color>(130,126,119)</color><statFactors>
         <Beauty>1.6</Beauty><MaxHitPoints>1.5</MaxHitPoints>
       </statFactors></stuffProps>
      </ThingDef></Defs>''')
    dino_xml = '''<Defs>
      <ThingDef ParentName="LeatherBase"><defName>Leather_Tyrannosaurus</defName>
        <statBases><MarketValue>8</MarketValue>
         <StuffPower_Armor_Sharp>1.88</StuffPower_Armor_Sharp>
         <StuffPower_Armor_Blunt>0.36</StuffPower_Armor_Blunt>
         <StuffPower_Insulation_Cold>32</StuffPower_Insulation_Cold>
         <StuffPower_Insulation_Heat>22</StuffPower_Insulation_Heat></statBases>
        <stuffProps><color>(104,85,52)</color><statFactors>
          <Beauty>1</Beauty><MaxHitPoints>2</MaxHitPoints>
        </statFactors></stuffProps>
      </ThingDef></Defs>'''
    _write(originals, 'Dinosauria/_Leathers/1.7/Defs/Items_Resource_Dinosauria_Stuff_Leather.xml', dino_xml)
    if old_duplicates:
        _write(originals, 'Dinosauria/Items_Resource_Dinosauria_Stuff_Leather.xml',
               dino_xml.replace('<MarketValue>8</MarketValue>', '<MarketValue>7</MarketValue>'))
    if ce:
        _write(patches, 'CE/_Leathers/1.7/Patches/Core/Leather.xml', '''
          <Patch><Operation Class="PatchOperationReplace">
            <xpath>/Defs/ThingDef[@Name="LeatherBase"]/statBases/StuffPower_Armor_Sharp</xpath>
            <value><StuffPower_Armor_Sharp>0.03</StuffPower_Armor_Sharp></value>
          </Operation><Operation Class="PatchOperationReplace">
            <xpath>/Defs/ThingDef[@Name="LeatherBase"]/statBases/StuffPower_Armor_Blunt</xpath>
            <value><StuffPower_Armor_Blunt>0.02</StuffPower_Armor_Blunt></value>
          </Operation><Operation Class="PatchOperationReplace">
            <xpath>/Defs/ThingDef[defName="Leather_Elephant"]/statBases/StuffPower_Armor_Sharp</xpath>
            <value><StuffPower_Armor_Sharp>0.08</StuffPower_Armor_Sharp></value>
          </Operation><Operation Class="PatchOperationReplace">
            <xpath>/Defs/ThingDef[defName="Leather_Elephant"]/statBases/StuffPower_Armor_Blunt</xpath>
            <value><StuffPower_Armor_Blunt>0.064</StuffPower_Armor_Blunt></value>
          </Operation></Patch>''')
        _write(patches, 'CE/_Leathers/1.7/Patches/Dinosauria/Leather.xml', '''
          <Patch><Operation Class="PatchOperationReplace">
            <xpath>/Defs/ThingDef[defName="Leather_Tyrannosaurus"]/statBases/StuffPower_Armor_Sharp</xpath>
            <value><StuffPower_Armor_Sharp>0.72</StuffPower_Armor_Sharp></value>
          </Operation><Operation Class="PatchOperationReplace">
            <xpath>/Defs/ThingDef[defName="Leather_Tyrannosaurus"]/statBases/StuffPower_Armor_Blunt</xpath>
            <value><StuffPower_Armor_Blunt>0.10</StuffPower_Armor_Blunt></value>
          </Operation></Patch>''')
    return originals, patches


def test_dinosauria_ce_matches_elephant_without_vanilla_override(tmp_path):
    orig, patches = _refs(tmp_path)
    rows = {'Leather_Tyrannosaurus': _row('Dinosauria', 1.88, .36, .08, .064)}
    outputs, _, report = render_original_overrides(rows, orig, patches)
    dino = E.fromstring(outputs[GROUP_ROUTES['Dinosauria']])
    assert not dino.xpath('./Operation[@Class="PatchOperationConditional"]')
    ce = dino.xpath('./Operation[@Class="PatchOperationFindMod"]')
    assert len(ce) == 1
    assert ce[0].xpath('./mods/li/text()') == ['Combat Extended']
    changes = {(x.split('/')[-1], v) for x, v in zip(
        ce[0].xpath('.//li/xpath/text()'), ce[0].xpath('.//li/match/value/*/text()'))}
    assert changes == {('StuffPower_Armor_Sharp', '0.08'),
                       ('StuffPower_Armor_Blunt', '0.064')}
    assert len(E.fromstring(outputs[GROUP_ROUTES['Core']])) == 0
    assert len(report['ce_overrides']) == 2


def test_dinosauria_vanilla_diff_reasserts_ce_when_original_ce_matches(tmp_path):
    orig, patches = _refs(tmp_path)
    rows = {'Leather_Tyrannosaurus': _row('Dinosauria', 1.3, .36, .72, .10)}
    outputs, _, report = render_original_overrides(rows, orig, patches)
    dino = E.fromstring(outputs[GROUP_ROUTES['Dinosauria']])
    assert dino.xpath('.//Operation[@Class="PatchOperationFindMod"]')
    assert dino.xpath('string(./Operation[@Class="PatchOperationConditional"]/match/value/StuffPower_Armor_Sharp)') == '1.3'
    assert '0.72' in dino.xpath('.//Operation[@Class="PatchOperationFindMod"]//StuffPower_Armor_Sharp/text()')
    assert any(x[1] == 'StuffPower_Armor_Sharp' for x in report['vanilla_overrides'])


def test_handwritten_vanilla_armor_still_forces_ce_reassertion(tmp_path):
    orig, patches = _refs(tmp_path)
    rows = {'Leather_Tyrannosaurus': _row('Dinosauria', 1.3, .36, .72, .10)}
    already_written = {('Leather_Tyrannosaurus', 'vanilla', 'StuffPower_Armor_Sharp')}
    outputs, _, report = render_original_overrides(
        rows, orig, patches, skip_vanilla=already_written)
    dino = E.fromstring(outputs[GROUP_ROUTES['Dinosauria']])
    # The hypothetical preceding handwritten Vanilla operation writes 1.3.
    # An explicit CE override must restore .72 even though .72 also equals
    # the CE source baseline.
    assert not dino.xpath('./Operation[@Class="PatchOperationConditional"]')
    assert '0.72' in dino.xpath('.//Operation[@Class="PatchOperationFindMod"]//StuffPower_Armor_Sharp/text()')
    assert any(change[1] == 'StuffPower_Armor_Sharp' for change in report['ce_overrides'])


def test_identical_sources_produce_no_xml_operations(tmp_path):
    orig, patches = _refs(tmp_path)
    rows = {'Leather_Tyrannosaurus': _row('Dinosauria', 1.88, .36, .72, .10)}
    outputs, _, report = render_original_overrides(rows, orig, patches)
    assert all(len(E.fromstring(patch)) == 0 for patch in outputs.values())
    assert report['vanilla_overrides'] == []
    assert report['ce_overrides'] == []


def test_current_original_wins_over_legacy_same_group(tmp_path):
    orig, patches = _refs(tmp_path, old_duplicates=True)
    rows = {'Leather_Tyrannosaurus': _row('Dinosauria', 1.88, .36, .72, .10)}
    outputs, _, report = render_original_overrides(rows, orig, patches)
    assert len(E.fromstring(outputs[GROUP_ROUTES['Dinosauria']])) == 0



def _alpha_thrumbo_source(tmp_path):
    originals = tmp_path / 'OriginalXML'
    original = '''<Defs><ThingDef ParentName="LeatherBase">
      <defName>Leather_AlphaThrumbo</defName>
      <statBases>
        <MarketValue>20</MarketValue>
        <StuffPower_Armor_Sharp>2.12</StuffPower_Armor_Sharp>
        <StuffPower_Armor_Blunt>0.4</StuffPower_Armor_Blunt>
        <StuffPower_Insulation_Cold>36</StuffPower_Insulation_Cold>
        <StuffPower_Insulation_Heat>24</StuffPower_Insulation_Heat>
      </statBases>
      <stuffProps><color>(242,242,242)</color><statFactors>
        <Beauty>8</Beauty><MaxHitPoints>2.2</MaxHitPoints>
      </statFactors></stuffProps>
    </ThingDef></Defs>'''
    _write(originals, 'Odyssey/_Leathers/1.7/Defs/Items_Resource_Stuff_Leather.xml', original)
    return originals, original


def test_alpha_thrumbo_routes_to_odyssey_not_alpha_animals(tmp_path):
    orig, original = _alpha_thrumbo_source(tmp_path)
    row = _row('Odyssey', 2.12, .4, .77, .12, market=20)
    row.update(Beauty=8, MaxHitPoints=2.2,
               StuffPower_Insulation_Cold=36,
               StuffPower_Insulation_Heat=24, **{'Color hex': '#F2F2F2'})
    # A historical stale AlphaAnimals reference must not hijack the owner.
    _write(orig, 'AlphaAnimals/_Leathers/1.7/Defs/Old.xml', original)
    output, _, report = render_original_overrides(
        {'Leather_AlphaThrumbo': row},
        orig, tmp_path / 'OriginalPatches', ce=False)
    assert report['material_routes']['Leather_AlphaThrumbo'] == GROUP_ROUTES['Odyssey']
    assert len(E.fromstring(output[GROUP_ROUTES['Odyssey']])) == 0
    assert len(E.fromstring(output[GROUP_ROUTES['AlphaAnimals']])) == 0
    assert not report['vanilla_overrides']


def test_stale_alpha_thrumbo_owner_explains_mismatch(tmp_path):
    orig, _ = _alpha_thrumbo_source(tmp_path)
    row = _row('AlphaAnimals', 2.12, .4, .77, .12, market=20)
    with pytest.raises(ValueError, match='Leather_AlphaThrumbo.*Source mod.*AlphaAnimals.*Odyssey'):
        render_original_overrides(
            {'Leather_AlphaThrumbo': row},
            orig, tmp_path / 'OriginalPatches', ce=False)


def test_missing_odyssey_reference_reports_exact_missing_source(tmp_path):
    orig, _ = _alpha_thrumbo_source(tmp_path)
    for path in (orig / 'Odyssey').rglob('*.xml'):
        path.unlink()
    with pytest.raises(ValueError, match='Leather_AlphaThrumbo.*OriginalXML/Odyssey'):
        render_original_overrides(
            {'Leather_AlphaThrumbo': _row('Odyssey',2.12,.4,.77,.12)},
            orig, tmp_path / 'OriginalPatches', ce=False)


def test_missing_dinosauria_material_is_fatal(tmp_path):
    orig, patches = _refs(tmp_path)
    for path in (orig / 'Dinosauria').rglob('*.xml'):
        path.unlink()
    with pytest.raises(ValueError, match='Leather_Tyrannosaurus.*Dinosauria'):
        render_original_overrides(
            {'Leather_Tyrannosaurus': _row('Dinosauria',1.88,.36,.08,.064)},
            orig, patches)


def test_wrong_mod_owner_fails_before_emitting_xml(tmp_path):
    orig, patches = _refs(tmp_path)
    with pytest.raises(ValueError, match='Source mod .*Core.*Dinosauria'):
        render_original_overrides(
            {'Leather_Tyrannosaurus': _row('Core',1.88,.36,.72,.10)},
            orig, patches)


def test_ce_requires_real_baseline_and_no_ce_disables_all_ce_ops(tmp_path):
    orig, patches = _refs(tmp_path, ce=False)
    rows = {'Leather_Tyrannosaurus': _row('Dinosauria',1.88,.36,.08,.064)}
    with pytest.raises(ValueError, match='CE material references'):
        render_original_overrides(rows, orig, patches)
    outputs, _, _ = render_original_overrides(rows, orig, patches, ce=False)
    assert len(E.fromstring(outputs[GROUP_ROUTES['Dinosauria']])) == 0


def test_runtime_ce_route_condition(tmp_path):
    orig, patches = _refs(tmp_path)
    rows = {'Leather_Tyrannosaurus': _row('Dinosauria',1.88,.36,.08,.064)}
    outputs, _, _ = render_original_overrides(rows, orig, patches)
    patch = E.fromstring(outputs[GROUP_ROUTES['Dinosauria']])
    for mods, sharp in (([], '0.72'), (['Combat Extended'], '0.08')):
        runtime = E.Element('Defs')
        item = E.SubElement(runtime, 'ThingDef')
        E.SubElement(item, 'defName').text = 'Leather_Tyrannosaurus'
        stats = E.SubElement(item, 'statBases')
        E.SubElement(stats, 'StuffPower_Armor_Sharp').text = sharp
        E.SubElement(stats, 'StuffPower_Armor_Blunt').text = '0.10'
        applier = PatchApplier(active_mods=mods)
        assert applier.apply_patch_root(patch, runtime)
        assert runtime.xpath('string(ThingDef/statBases/StuffPower_Armor_Sharp)') == sharp
