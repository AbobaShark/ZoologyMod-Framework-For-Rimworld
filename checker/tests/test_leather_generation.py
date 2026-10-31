"""Self-contained tests for the Leathers sheet and template-based XML rendering."""
from __future__ import annotations

import sys
from pathlib import Path

import pandas as pd
import pytest
from lxml import etree as E

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from core.leather_generation import (
    read_leathers, render_leather_outputs, _render_patch, _trim_core_manual, CUSTOM_DEST, CE_DEST,
)


@pytest.fixture
def sample(tmp_path):
    source = tmp_path / 'Leathers.tsv'
    rows = [
        ['Leather_Bear', 4, 1.9, 1.3, 1.12, .24, 24, 20, '#705241', .08, .064],
        ['Leather_Squirrel', 3.6, 2.2, .6, .067, .014, 20, 3, '#B37D37', .03, .024],
        ['Leather_GuineaPig', 2.4, 1.5, .6, .5, .1, 22, 10, '#B37D37', .03, .024],
        ['Leather_Pig', 1.9, 1, 1.3, .64, .24, 12, 12, '#AE967E', .05, .04],
        ['Leather_Beaver', 4.7, 2.8, 1.2, .81, .21, 30, 14, '#6B4C39', .05, .04],
        ['Leather_Ostrich', 4.2, 2.6, 1.1, .81, .24, 14, 20, '#AF8F6D', .81, .24],
        ['Leather_Otter', 5, 3, 1, .67, .14, 34, 16, '#5E4B3D', .67, .14],
    ]
    rows = [r + ['Zoology' if r[0] in {
        'Leather_Beaver','Leather_Ostrich','Leather_Otter','Leather_Squirrel'
    } else 'Core'] for r in rows]
    pd.DataFrame(rows, columns=['Name', 'MarketValue', 'Beauty', 'MaxHitPoints',
        'StuffPower_Armor_Sharp', 'StuffPower_Armor_Blunt',
        'StuffPower_Insulation_Cold','StuffPower_Insulation_Heat', 'Color hex',
        'CE Armor Sharp','CE Armor Blunt','Source mod']).to_csv(source, sep='\t', index=False)
    mod = tmp_path/'Zoology';mod.mkdir()
    refs = tmp_path/'OriginalXML'/'Core'/'_Leathers'
    refs.mkdir(parents=True)
    (refs/'Items_Resource_Stuff_Leather.xml').write_text('''''''''<Defs>
    <ThingDef Abstract="True" Name="LeatherBase"><statBases><MarketValue>2.1</MarketValue>
      <StuffPower_Armor_Sharp>0.81</StuffPower_Armor_Sharp><StuffPower_Armor_Blunt>0.24</StuffPower_Armor_Blunt>
      <StuffPower_Insulation_Cold>16</StuffPower_Insulation_Cold>
      <StuffPower_Insulation_Heat>16</StuffPower_Insulation_Heat>
    </statBases><stuffProps><statFactors><MaxHitPoints>1.3</MaxHitPoints></statFactors></stuffProps></ThingDef>
    <ThingDef ParentName="LeatherBase"><defName>Leather_Bear</defName>
      <statBases><MarketValue>3.4</MarketValue><StuffPower_Insulation_Cold>20</StuffPower_Insulation_Cold></statBases>
      <stuffProps><color>(112,82,65)</color><statFactors><Beauty>1.9</Beauty></statFactors></stuffProps>
    </ThingDef>
    <ThingDef ParentName="LeatherBase"><defName>Leather_Pig</defName>
      <statBases><MarketValue>1.9</MarketValue>
        <StuffPower_Armor_Sharp>0.64</StuffPower_Armor_Sharp>
        <StuffPower_Armor_Blunt>0.24</StuffPower_Armor_Blunt>
        <StuffPower_Insulation_Cold>12</StuffPower_Insulation_Cold>
        <StuffPower_Insulation_Heat>12</StuffPower_Insulation_Heat></statBases>
      <stuffProps><color>(174,150,126)</color><statFactors><Beauty>1</Beauty>
        <MaxHitPoints>1.3</MaxHitPoints></statFactors></stuffProps>
    </ThingDef>
    <ThingDef ParentName="LeatherBase"><defName>Leather_GuineaPig</defName>
      <statBases><MarketValue>5</MarketValue></statBases>
      <stuffProps><color>(179,125,55)</color><statFactors><Beauty>1.5</Beauty>
        <MaxHitPoints>0.6</MaxHitPoints></statFactors></stuffProps>
    </ThingDef>
    </Defs>''''''''', encoding='utf8')
    ce = tmp_path / 'OriginalPatches' / 'CE' / '_Leathers' / '1.7' / 'Patches'
    ce.mkdir(parents=True)
    (ce / 'Leather.xml').write_text('''<Patch>
      <Operation Class="PatchOperationReplace">
        <xpath>Defs/ThingDef[@Name="LeatherBase"]/statBases/StuffPower_Armor_Sharp</xpath>
        <value><StuffPower_Armor_Sharp>0.03</StuffPower_Armor_Sharp></value>
      </Operation>
      <Operation Class="PatchOperationReplace">
        <xpath>Defs/ThingDef[@Name="LeatherBase"]/statBases/StuffPower_Armor_Blunt</xpath>
        <value><StuffPower_Armor_Blunt>0.024</StuffPower_Armor_Blunt></value>
      </Operation>
    </Patch>''', encoding='utf8')
    return source,mod


def test_read_errors(sample):
    src,_ = sample
    rows=read_leathers(src)
    assert rows['Leather_Beaver']['MaxHitPoints']==1.2
    text=src.read_text()
    src.write_text(text+'\n'+text.splitlines()[1]+'\n')
    with pytest.raises(ValueError,match='duplicate'):
        read_leathers(src)


def test_leathers_source_mod_is_mandatory(sample):
    src, _ = sample
    df = pd.read_csv(src, sep='\t')
    df = df.drop(columns=['Source mod'])
    df.to_csv(src, sep='\t', index=False)
    with pytest.raises(ValueError, match='Source mod'):
        read_leathers(src)


def test_render_ignores_nodes_without_patch_operation_class(sample, tmp_path):
    src, _ = sample
    template = tmp_path / 'NonPatchOperationElements.xml'
    template.write_text('''<Patch>
      <!-- Real RimWorld patches also contain plain XML elements with no Class. -->
      <Operation><xpath>Defs/ThingDef[defName="Ignored"]</xpath></Operation>
      <Operation Class="PatchOperationReplace">
        <xpath>Defs/ThingDef[defName="Leather_Pig"]/statBases/MarketValue</xpath>
        <value><MarketValue>999</MarketValue></value>
      </Operation>
      <Operation Class="PatchOperationFindMod">
        <mods><li>Combat Extended</li></mods>
        <match Class="PatchOperationSequence"><operations><li/></operations></match>
      </Operation>
    </Patch>''', encoding='utf8')
    touched = set()
    tree = E.fromstring(_render_patch(template, read_leathers(src), True, touched))
    assert tree.xpath('string(./Operation[2]/value/MarketValue)') == '1.9'
    assert ('Leather_Pig', 'vanilla', 'MarketValue') in touched


def test_prune_ignores_non_operation_xml_nodes(sample):
    from types import SimpleNamespace
    src, _ = sample
    source_xml = b'''<Patch>
      <Operation Class="PatchOperationFindMod">
        <mods><li>Combat Extended</li></mods>
        <match Class="PatchOperationSequence">
          <operations><li/></operations>
        </match>
      </Operation>
      <Operation><xpath>Defs/ThingDef[defName="OtherDef"]</xpath></Operation>
    </Patch>'''
    refs = SimpleNamespace(materials={}, ce_before={})
    payload, touched = _trim_core_manual(source_xml, read_leathers(src), refs, True)
    root = E.fromstring(payload)
    assert len(root) == 1
    assert root[0].get('Class') is None
    assert touched == set()


def test_render_existing_patch_preserves_original_ops(sample):
    src,mod = sample
    relative='Core/Patches/Core/ThingDefs_Items/Items_Resource_Stuff_Leather.xml'
    target=mod/relative;target.parent.mkdir(parents=True)
    target.write_text('''<Patch>
  <Operation Class="PatchOperationConditional">
    <xpath>Defs/ThingDef[defName="Leather_Bear"]/statBases/MarketValue</xpath>
    <match Class="PatchOperationReplace"><xpath>Defs/ThingDef[defName="Leather_Bear"]/statBases/MarketValue</xpath><value><MarketValue>3.4</MarketValue></value></match>
    <nomatch Class="PatchOperationAdd"><xpath>Defs/ThingDef[defName="Leather_Bear"]/statBases</xpath><value><MarketValue>3.4</MarketValue></value></nomatch>
  </Operation>
  <Operation Class="PatchOperationConditional"><xpath>Defs/ThingDef[defName="Leather_GuineaPig"]/statBases/MarketValue</xpath>
  <match Class="PatchOperationReplace"><xpath>Defs/ThingDef[defName="Leather_GuineaPig"]/statBases/MarketValue</xpath>
  <value><MarketValue>1</MarketValue></value></match></Operation>
  <Operation Class="PatchOperationFindMod"><mods><li>Combat Extended</li></mods>
    <match Class="PatchOperationSequence"><operations>
      <li Class="PatchOperationConditional"><xpath>Defs/ThingDef[defName="Leather_Squirrel"]/statBases/StuffPower_Armor_Sharp</xpath>
      <match Class="PatchOperationReplace"><xpath>Defs/ThingDef[defName="Leather_Squirrel"]/statBases/StuffPower_Armor_Sharp</xpath><value><StuffPower_Armor_Sharp>0.99</StuffPower_Armor_Sharp></value></match></li>
      <li Class="PatchOperationConditional"><xpath>Defs/ThingDef[defName="Leather_Pig"]/statBases/StuffPower_Armor_Blunt</xpath>
      <match Class="PatchOperationReplace"><xpath>Defs/ThingDef[defName="Leather_Pig"]/statBases/StuffPower_Armor_Blunt</xpath><value><StuffPower_Armor_Blunt>0.99</StuffPower_Armor_Blunt></value></match></li>
    </operations></match>
  </Operation>
  <Operation Class="PatchOperationRemove"><xpath>Defs/ThingDef[defName="OtherDef"]</xpath></Operation>
</Patch>''')
    entries=[{'destination':relative}]
    outputs,report=render_leather_outputs(src,mod,entries,original_xml=src.parent/'OriginalXML')
    root=E.fromstring(outputs[0][1]); assert len(root)==4
    assert root.xpath('string(./Operation[1]/match/value/MarketValue)')=='4'
    assert root.xpath('string(./Operation[1]/nomatch/value/MarketValue)')=='4'
    assert not root.xpath('.//ThingDef[defName="Leather_Squirrel"]')
    assert root.xpath('string(./Operation[2]/match/value/MarketValue)')=='2.4'
    assert root.xpath('string(.//li[xpath[contains(text(),"Leather_Squirrel")]]/match/value/StuffPower_Armor_Sharp)')=='0.03'
    assert root.xpath('string(.//li[xpath[contains(text(),"Leather_Pig")]]/match/value/StuffPower_Armor_Blunt)')=='0.04'
    assert root[-1].findtext('xpath')=='Defs/ThingDef[defName="OtherDef"]'
    assert 'Leather_GuineaPig' in report['existing_zoology_targets_updated']


def test_core_manual_matching_original_is_pruned(sample):
    src, mod = sample
    import pandas as pd
    rows = pd.read_csv(src, sep='\t')
    rows.loc[rows['Name'] == 'Leather_Bear', 'MarketValue'] = 3.4
    rows.to_csv(src, sep='\t', index=False)
    relative = 'Core/Patches/Core/ThingDefs_Items/Items_Resource_Stuff_Leather.xml'
    target = mod / relative
    target.parent.mkdir(parents=True)
    target.write_text('''<Patch><Operation Class="PatchOperationConditional">
       <xpath>Defs/ThingDef[defName="Leather_Bear"]/statBases/MarketValue</xpath>
       <match Class="PatchOperationReplace">
          <xpath>Defs/ThingDef[defName="Leather_Bear"]/statBases/MarketValue</xpath>
          <value><MarketValue>999</MarketValue></value>
       </match>
    </Operation></Patch>''')
    outputs, _ = render_leather_outputs(src, mod, [{'destination': relative}],
                                        original_xml=src.parent / 'OriginalXML',
                                        original_patches=src.parent / 'OriginalPatches')
    assert len(E.fromstring(outputs[0][1])) == 0


def test_custom_defs_and_ce(sample):
    src,mod=sample
    src_template=Path(__file__).resolve().parents[1]/'templates/Items_Resource_Stuff_Leather_Zoology.xml'
    outputs,report=render_leather_outputs(src,mod,[{'destination':CUSTOM_DEST},{'destination':CE_DEST}],template_dir=src_template.parent,original_xml=src.parent/'OriginalXML')
    defs=E.fromstring(outputs[0][1]);assert defs.tag=='Defs'
    assert {t.findtext('defName') for t in defs}=={'Leather_Beaver','Leather_Ostrich','Leather_Otter','Leather_Squirrel'}
    assert defs.xpath('string(ThingDef[defName="Leather_Beaver"]/stuffProps/statFactors/MaxHitPoints)')=='1.2'
    ce=E.fromstring(outputs[1][1]); assert ce.tag=='Patch'
    assert len(ce)==4  # Beaver and Squirrel have sharp and blunt overrides
    assert ce.xpath('string(Operation[1]/match/value/StuffPower_Armor_Sharp)')=='0.05'
    assert ce.xpath('string(Operation[2]/match/value/StuffPower_Armor_Blunt)')=='0.04'
    outputs2,_=render_leather_outputs(src,mod,[{'destination':CE_DEST}],ce=False,original_xml=src.parent/'OriginalXML')
    assert len(E.fromstring(outputs2[0][1]))==0
    assert report['materials_in_sheet']==7
