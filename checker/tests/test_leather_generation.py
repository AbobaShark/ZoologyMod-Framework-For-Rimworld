"""No handwritten leather templates: direct ThingDefs + generated comparisons only."""
from pathlib import Path
from unittest.mock import patch
from lxml import etree as E
from core.leather_generation import (
    render_leather_outputs, CUSTOM_DEST, CE_DEST, CUSTOM_NAMES,
)
from core.leather_overrides import GROUP_ROUTES


def test_every_native_leather_updates_in_defs_and_ce_patches_only(tmp_path):
    mod = tmp_path / 'Zoology'
    target = mod / CUSTOM_DEST
    target.parent.mkdir(parents=True)
    root = E.Element('Defs')
    rows = {}
    for name in CUSTOM_NAMES:
        node = E.SubElement(root, 'ThingDef', ParentName='LeatherBase')
        E.SubElement(node, 'defName').text = name
        E.SubElement(node, 'label').text = name
        E.SubElement(node, 'statBases')
        E.SubElement(node, 'stuffProps')
        rows[name] = {
            'Source mod': 'Zoology', 'MarketValue': 4.5, 'Beauty': 2,
            'MaxHitPoints': 1.5, 'StuffPower_Armor_Sharp': 0.81,
            'StuffPower_Armor_Blunt': 0.24, 'StuffPower_Insulation_Cold': 22,
            'StuffPower_Insulation_Heat': 15, 'Color hex': '#112233',
            'CE Armor Sharp': 0.05, 'CE Armor Blunt': 0.04,
        }
    target.write_bytes(E.tostring(root))
    routes = {path: E.tostring(E.Element('Patch')) for path in GROUP_ROUTES.values()}
    entries = [{'destination': dest} for dest in
               [CUSTOM_DEST, CE_DEST, *GROUP_ROUTES.values()]]
    with patch('core.leather_generation.read_leathers', return_value=rows), \
         patch('core.leather_overrides.render_original_overrides',
               return_value=(routes, E.Element('Patch'), {})):
        outputs, report = render_leather_outputs(
            'unused', mod, entries, original_xml=tmp_path/'OriginalXML',
            original_patches=tmp_path/'OriginalXML/CEPatches')
    generated = dict(outputs)
    native = E.fromstring(generated[CUSTOM_DEST])
    assert {x.findtext('defName') for x in native.findall('ThingDef')} == set(CUSTOM_NAMES)
    for node in native.findall('ThingDef'):
        assert node.findtext('statBases/MarketValue') == '4.5'
        assert node.findtext('statBases/StuffPower_Armor_Sharp') == '0.81'
        assert node.findtext('stuffProps/color') == '(17,34,51)'
    ce = E.fromstring(generated[CE_DEST])
    xpaths = ce.xpath('.//xpath/text()')
    assert len(xpaths) == 4 * len(CUSTOM_NAMES)
    assert all('/statBases/StuffPower_Armor_' in x for x in xpaths)
    assert not any('OdysseyFallback' in dest or 'Leather_Chitin.xml' in dest
                   for dest in generated)
    assert sorted(report['custom_defs']) == sorted(CUSTOM_NAMES)


def test_core_materials_require_original_refs_not_handwritten_files(tmp_path):
    mod = tmp_path / 'Zoology'
    native = mod / CUSTOM_DEST
    native.parent.mkdir(parents=True)
    root = E.Element('Defs')
    for name in CUSTOM_NAMES:
        node = E.SubElement(root, 'ThingDef', ParentName='LeatherBase')
        E.SubElement(node, 'defName').text = name
    native.write_bytes(E.tostring(root))
    # No Core/Patches/Core/ThingDefs_Items/Items_Resource_Stuff_Leather.xml
    # exists here, by design. Missing original XML must fail, not resurrect it.
    rows = {name: {'Source mod': 'Zoology'} for name in CUSTOM_NAMES}
    with patch('core.leather_generation.read_leathers', return_value=rows), \
         patch('core.leather_overrides.render_original_overrides',
               side_effect=ValueError('OriginalXML is required')):
        try:
            render_leather_outputs(
                'unused', mod, [{'destination': CUSTOM_DEST}],
                original_xml=tmp_path/'OriginalXML')
        except ValueError as error:
            assert 'OriginalXML is required' in str(error)
        else:
            raise AssertionError('OriginalXML validation was bypassed')
