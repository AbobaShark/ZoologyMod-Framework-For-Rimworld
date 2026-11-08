"""Color comparisons and the elimination of redundant material patches."""
from lxml import etree as E
from core.leather_overrides import GROUP_ROUTES, _conditional, render_original_overrides
from core.material_colors import xml_color
from core.rimworld_original_xml import OriginalXmlIndex
from core.rimworld_patch_optimizer import PatchOptimizer
from core.rimworld_patch_apply import PatchApplier


def _source(tmp_path, *, stuff='(100,80,50)', graphic='(100,80,50)', color='#645032'):
    folder = tmp_path / 'OriginalXML' / 'Core' / '1.7' / 'Defs'
    folder.mkdir(parents=True)
    s = f'<color>{stuff}</color>' if stuff is not None else ''
    g = f'<graphicData><color>{graphic}</color></graphicData>' if graphic is not None else ''
    (folder / 'Leather.xml').write_text(f'''<Defs>
      <ThingDef Name="LeatherBase" Abstract="True">
        <statBases><MarketValue>2</MarketValue>
          <StuffPower_Armor_Sharp>0.81</StuffPower_Armor_Sharp>
          <StuffPower_Armor_Blunt>0.24</StuffPower_Armor_Blunt>
          <StuffPower_Insulation_Cold>16</StuffPower_Insulation_Cold>
          <StuffPower_Insulation_Heat>16</StuffPower_Insulation_Heat>
        </statBases>
        <stuffProps><statFactors><Beauty>1</Beauty>
          <MaxHitPoints>1.3</MaxHitPoints></statFactors></stuffProps>
      </ThingDef>
      <ThingDef ParentName="LeatherBase">
        <defName>Leather_Test</defName>{g}<stuffProps>{s}</stuffProps>
      </ThingDef>
    </Defs>''', encoding='utf8')
    row = {'Source mod': 'Core', 'MarketValue': 2, 'Beauty': 1,
           'MaxHitPoints': 1.3, 'StuffPower_Armor_Sharp': .81,
           'StuffPower_Armor_Blunt': .24, 'StuffPower_Insulation_Cold': 16,
           'StuffPower_Insulation_Heat': 16, 'Color hex': color}
    return tmp_path / 'OriginalXML', row


def _render(tmp_path, **kwargs):
    directory, row = _source(tmp_path, **kwargs)
    outputs, _, report = render_original_overrides(
        {'Leather_Test': row}, directory, directory / 'CEPatches', ce=False)
    return E.fromstring(outputs[GROUP_ROUTES['Core']]), report


def test_xml_color_formats():
    assert xml_color('(100,80,50)') == '#645032'
    assert xml_color('(0.392156862745,0.313725490196,0.196078431373)') == '#645032'
    assert xml_color('(1,0,0)') == '#010000'
    assert xml_color('(1.0,0.0,0.0)') == '#FF0000'
    assert xml_color(' #d3c28f ') == '#D3C28F'
    assert xml_color('(300,0,0)') is None


def test_unchanged_leather_color_generates_no_operations(tmp_path):
    patch, report = _render(tmp_path)
    assert len(patch) == 0
    assert report['vanilla_overrides'] == []


def test_equal_float_color_is_not_patched(tmp_path):
    patch, report = _render(tmp_path, stuff='(0.392156862745,0.313725490196,0.196078431373)')
    assert len(patch) == 0
    assert report['vanilla_overrides'] == []


def test_independent_graphic_tint_is_preserved(tmp_path):
    patch, report = _render(tmp_path, stuff='(10,20,30)')
    assert len(patch) == 0
    assert report['vanilla_overrides'] == []


def test_genuine_color_change_patches_both_matching_channels(tmp_path):
    patch, report = _render(tmp_path, color='#112233')
    assert any('/stuffProps/color' in x for x in patch.xpath('.//xpath/text()'))
    assert any('/graphicData/color' in x for x in patch.xpath('.//xpath/text()'))
    assert sum(change[1] == 'Color hex' for change in report['vanilla_overrides']) == 1
    defs = E.fromstring(b'<Defs><ThingDef><defName>Leather_Test</defName>'
                        b'<graphicData><color>(100,80,50)</color></graphicData>'
                        b'<stuffProps><color>(100,80,50)</color></stuffProps>'
                        b'</ThingDef></Defs>')
    applier = PatchApplier(record_missing=True)
    assert applier.apply_patch_root(patch, defs)
    assert not applier.missing_targets
    assert defs.xpath('string(ThingDef/stuffProps/color)') == '(17,34,51)'
    assert defs.xpath('string(ThingDef/graphicData/color)') == '(17,34,51)'


def test_optimizer_drops_redundant_color_and_empty_sequence(tmp_path):
    original, _ = _source(tmp_path)
    patch = E.Element('Patch')
    _conditional(patch, 'Leather_Test', 'stuffProps/color',
                 '(0.392156862745,0.313725490196,0.196078431373)', None)
    result = PatchOptimizer(OriginalXmlIndex(str(original)).load()).optimize(patch)
    assert len(result) == 0


def test_optimizer_keeps_real_color_edit(tmp_path):
    original, _ = _source(tmp_path)
    patch = E.Element('Patch')
    _conditional(patch, 'Leather_Test', 'stuffProps/color', '(17,34,51)', None)
    result = PatchOptimizer(OriginalXmlIndex(str(original)).load()).optimize(patch)
    assert '(17,34,51)' in result.xpath('.//color/text()')
