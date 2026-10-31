"""Source-differential leather patches: no fallback definitions or manual overrides."""
import sys
from pathlib import Path

from lxml import etree as E

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from core.leather_overrides import render_original_overrides, GROUP_ROUTES
from core.leather_reference_sync import discover_new_leather_references


def _make_sources(tmp_path):
    original = tmp_path/'OriginalXML'
    path = original/'Core'/'_Leathers'/'Data'/'Core'/'Defs'/'ThingDefs_Items'/'Items_Resource_Stuff_Leather.xml'
    path.parent.mkdir(parents=True)
    path.write_text('''<Defs>
    <ThingDef Abstract="True" Name="LeatherBase"><statBases>
        <MarketValue>2.1</MarketValue><StuffPower_Armor_Sharp>0.81</StuffPower_Armor_Sharp>
        <StuffPower_Armor_Blunt>0.24</StuffPower_Armor_Blunt>
        <StuffPower_Insulation_Cold>16</StuffPower_Insulation_Cold>
        <StuffPower_Insulation_Heat>16</StuffPower_Insulation_Heat>
    </statBases><stuffProps><statFactors><MaxHitPoints>1.3</MaxHitPoints></statFactors></stuffProps></ThingDef>
    <ThingDef ParentName="LeatherBase"><defName>Leather_Bear</defName>
      <statBases><MarketValue>3.4</MarketValue><StuffPower_Insulation_Cold>20</StuffPower_Insulation_Cold></statBases>
      <stuffProps><color>(112,82,65)</color><statFactors><Beauty>1.9</Beauty></statFactors></stuffProps>
      <graphicData><color>(112,82,65)</color></graphicData>
    </ThingDef>
    </Defs>''',encoding='utf8')
    ody = original/'Odyssey'/'_Leathers'/'Data'/'Odyssey'/'Defs'/'ThingDefs_Items'/'Items_Resource_Stuff_Leather.xml'
    ody.parent.mkdir(parents=True)
    ody.write_text('''<Defs><ThingDef ParentName="LeatherBase">
        <defName>Leather_Armadillo</defName><statBases>
          <MarketValue>3.2</MarketValue><StuffPower_Armor_Sharp>1.24</StuffPower_Armor_Sharp>
          <StuffPower_Insulation_Cold>16</StuffPower_Insulation_Cold>
          <StuffPower_Insulation_Heat>18</StuffPower_Insulation_Heat>
        </statBases><stuffProps><color>(204,154,104)</color>
          <statFactors><MaxHitPoints>1.3</MaxHitPoints><Beauty>2</Beauty></statFactors>
        </stuffProps><graphicData><color>(204,154,104)</color></graphicData>
      </ThingDef></Defs>''',encoding='utf8')
    return original


def _row(**kw):
    row={'MarketValue':3.2,'Beauty':2.0,'MaxHitPoints':1.3,
         'StuffPower_Armor_Sharp':1.24,'StuffPower_Armor_Blunt':0.24,
         'StuffPower_Insulation_Cold':16.0,'StuffPower_Insulation_Heat':18.0,
         'Color hex':'#CC9A68','CE Armor Sharp':0.09,'CE Armor Blunt':0.064}
    row.update(kw)
    return row


def test_odyssey_override_only_if_different(tmp_path):
    xml=_make_sources(tmp_path)
    rows={'Leather_Armadillo':_row()}
    outputs,ce,report=render_original_overrides(rows,xml,tmp_path/'OriginalPatches')
    route=GROUP_ROUTES['Odyssey']
    assert E.fromstring(outputs[route]).tag=='Patch'
    assert not E.fromstring(outputs[route]).xpath('./Operation[@Class="PatchOperationConditional"]'), 'Unchanged original material must not get a vanilla override'
    assert len(ce) == 0, 'Original CE ops must live in source route, not a global early-loading file'
    assert E.fromstring(outputs[route]).xpath('count(Operation[@Class="PatchOperationFindMod"])') == 1
    assert not report['missing_originals']
    rows['Leather_Armadillo']['MarketValue']=4.5
    outputs,_,report=render_original_overrides(rows,xml,tmp_path/'OriginalPatches')
    root=E.fromstring(outputs[route])
    assert any('Leather_Armadillo' in s for s in root.xpath('//xpath/text()'))
    assert '4.5' in root.xpath('//value//MarketValue/text()')
    assert len(E.fromstring(outputs[GROUP_ROUTES['Core']])) == 0
    assert len(report['vanilla_overrides']) == 1


def test_inherited_stat_and_existing_zoology_patch_not_duplicated(tmp_path):
    xml=_make_sources(tmp_path)
    rows={'Leather_Bear':_row(MarketValue=4.,Beauty=1.9,MaxHitPoints=1.3,
        StuffPower_Armor_Sharp=0.81,StuffPower_Armor_Blunt=0.24,
        StuffPower_Insulation_Cold=24.,StuffPower_Insulation_Heat=16.,
        **{'Color hex':'#705241','CE Armor Sharp':0.08,'CE Armor Blunt':0.064})}
    skip={('Leather_Bear','vanilla','MarketValue'),('Leather_Bear','vanilla','StuffPower_Insulation_Cold')}
    outputs,_,report=render_original_overrides(rows,xml,tmp_path/'OriginalPatches',skip_vanilla=skip)
    assert len(E.fromstring(outputs[GROUP_ROUTES['Core']])) >= 1  # CE-only change after the vanilla handcrafted patch
    assert not report['missing_originals']


def test_missing_source_is_explicit(tmp_path):
    src=tmp_path/'OriginalXML'; src.mkdir()
    try:
        render_original_overrides({'Leather_Armadillo':_row()},src,tmp_path/'OriginalPatches')
    except ValueError as exc:
        assert 'reference_sync_gui' in str(exc)
    else:
        raise AssertionError('Missing original definitions must not silently generate empty patches')


def test_reference_sync_discovers_original_and_updates(tmp_path):
    src_data=b'''<Defs><ThingDef ParentName="LeatherBase"><defName>Leather_Armadillo</defName></ThingDef></Defs>'''
    src_rel='Data/Odyssey/Defs/ThingDefs_Items/Items_Resource_Stuff_Leather.xml'
    class Fake:
        files=[src_rel]
        provenance='local fixture'
        def read(self,x):return src_data
    import hashlib
    def digest(x):return hashlib.sha256(x).hexdigest() if x is not None else None
    def parser(blob,kind):
        tree=E.fromstring(blob)
        if tree.tag!=kind: raise ValueError(kind)
        return tree
    def inside(root,rel):return Path(root)/rel
    spec={'id':'Odyssey','reference_dirs':['OriginalXML/Odyssey']}
    out=discover_new_leather_references(spec,[Fake()],tmp_path,[],'1.7',
        safe_inside=inside,parse_xml=parser,digest=digest)
    assert len(out)==1 and out[0]['status']=='COPY'
    assert out[0]['destination'].startswith('OriginalXML/Odyssey/_Leathers/')
    target=tmp_path/out[0]['destination'];target.parent.mkdir(parents=True);target.write_bytes(src_data)
    out2=discover_new_leather_references(spec,[Fake()],tmp_path,[],'1.7',
        safe_inside=inside,parse_xml=parser,digest=digest)
    assert out2[0]['status']=='SKIP'
