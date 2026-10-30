"""Generated CE bionics must use CE worksheet columns, not Vanilla damage."""
import tempfile
from pathlib import Path
import pandas as pd
from lxml import etree as E
from core.ce_bionics import render_ce_bionics

KINDS=('BionicArm','BionicLeg','BionicJaw','ArchotechArm','ArchotechLeg')
SIZES=('VerySmall','Small','Medium','Large','VeryLarge','Huge')


def data():
    return pd.DataFrame([{'Weapon':f'{kind}_{size}','Damage':'999',
                          'CE Damage':'7.5','CE Blunt AP':'1.25','CE Sharp AP':'No',
                          'CE Attack interval':'1.6'} for kind in KINDS for size in SIZES])


def source_file(directory):
    root=E.Element('Defs')
    for kind in KINDS:
        for size in SIZES:
            thing=E.SubElement(root,'HediffDef');E.SubElement(thing,'defName').text=f'{kind}_Animal_{size}'
            comps=E.SubElement(thing,'comps');li=E.SubElement(comps,'li',Class='HediffCompProperties_VerbGiver')
            tools=E.SubElement(li,'tools');tool=E.SubElement(tools,'li')
            E.SubElement(tool,'label').text='fist';E.SubElement(tool,'power').text='4';E.SubElement(tool,'cooldownTime').text='1'
            E.SubElement(tool,'soundMeleeHit').text='ExistingSound'
    path=Path(directory)/'bionics.xml';path.write_bytes(E.tostring(root));return path


def test_ce_bionics_uses_ce_values_and_preserves_tool_properties():
    with tempfile.TemporaryDirectory() as directory:
        root=E.fromstring(render_ce_bionics(data(),source_file(directory)))
    assert len(root)==30
    tools=root.xpath('.//match/value/li[@Class="CombatExtended.ToolCE"]')
    assert len(tools)==30
    assert {tool.findtext('power') for tool in tools}=={'7.5'}
    assert {tool.findtext('cooldownTime') for tool in tools}=={'1.6'}
    assert {tool.findtext('armorPenetrationBlunt') for tool in tools}=={'1.25'}
    assert not root.xpath('.//armorPenetrationSharp')
    assert {tool.findtext('soundMeleeHit') for tool in tools}=={'ExistingSound'}


def test_ce_bionics_rejects_missing_numeric_fields():
    df=data();df.loc[0,'CE Damage']='#ERROR!'
    with tempfile.TemporaryDirectory() as directory:
        import pytest
        with pytest.raises(ValueError,match='nonnumeric'):
            render_ce_bionics(df,source_file(directory))
