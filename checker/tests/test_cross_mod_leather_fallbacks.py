"""Odyssey material dependency tests: no standalone mods or invented values."""
from __future__ import annotations

import csv
from pathlib import Path

from lxml import etree as E

from core.leather_generation import _cross_mod_consumers
from core.leather_overrides import LeatherReferences, GROUP_ROUTES, render_original_overrides
from core.rimworld_patch_apply import PatchApplier


def _fixture(tmp_path):
    core = tmp_path / 'OriginalXML/Core/Defs'
    core.mkdir(parents=True)
    (core / 'Leather.xml').write_text(
        '<Defs><ThingDef Name="LeatherBase" Abstract="True">'
        '<statBases><MarketValue>1</MarketValue><StuffPower_Armor_Sharp>1</StuffPower_Armor_Sharp>'
        '<StuffPower_Armor_Blunt>0.1</StuffPower_Armor_Blunt>'
        '<StuffPower_Insulation_Cold>10</StuffPower_Insulation_Cold>'
        '<StuffPower_Insulation_Heat>10</StuffPower_Insulation_Heat></statBases>'
        '<stuffProps><color>(1,2,3)</color><statFactors>'
        '<Beauty>1</Beauty><MaxHitPoints>1</MaxHitPoints>'
        '</statFactors></stuffProps></ThingDef></Defs>', encoding='utf8')
    odyssey = tmp_path / 'OriginalXML/Odyssey/_Leathers/1.7/Defs'
    odyssey.mkdir(parents=True)
    (odyssey / 'Leather.xml').write_text(
        '<Defs><ThingDef ParentName="LeatherBase">'
        '<defName>Leather_Armadillo</defName><label>armadillo leather</label>'
        '<statBases><MarketValue>4</MarketValue>'
        '<StuffPower_Armor_Sharp>1.29</StuffPower_Armor_Sharp>'
        '<StuffPower_Armor_Blunt>0.24</StuffPower_Armor_Blunt>'
        '<StuffPower_Insulation_Cold>14</StuffPower_Insulation_Cold>'
        '<StuffPower_Insulation_Heat>14</StuffPower_Insulation_Heat></statBases>'
        '<stuffProps><color>(204,154,104)</color><statFactors>'
        '<Beauty>1.75</Beauty><MaxHitPoints>1.5</MaxHitPoints>'
        '</statFactors></stuffProps></ThingDef></Defs>', encoding='utf8')
    row = dict(MarketValue=4.0, Beauty=1.75, MaxHitPoints=1.5,
               StuffPower_Armor_Sharp=1.29, StuffPower_Armor_Blunt=0.24,
               StuffPower_Insulation_Cold=14.0, StuffPower_Insulation_Heat=14.0,
               **{'Color hex':'#CC9A68', 'Source mod':'Odyssey',
                  'CE Armor Sharp':0.09,'CE Armor Blunt':0.064})
    rows = {'Leather_Armadillo': row}
    entries = [
        {'destination': destination,
         'requires': {'Megafauna':'Spino.Megafauna',
                      'Vanilla Animals Expanded — Endangered':'VanillaExpanded.VAEEndAndExt'}.get(group)}
        for group, destination in GROUP_ROUTES.items()
    ]
    return rows, entries, tmp_path / 'OriginalXML', tmp_path / 'OriginalXML/CEPatches'


def _animals(tmp_path):
    path = tmp_path / 'Animals.tsv'
    with path.open('w', encoding='utf8', newline='') as f:
        writer = csv.writer(f, delimiter='\t')
        writer.writerow(('LeatherDef', 'MayRequire'))
        writer.writerow(('Leather_Armadillo', 'Ludeon.RimWorld.Odyssey'))
        writer.writerow(('Leather_Armadillo', 'Spino.Megafauna'))
        writer.writerow(('Leather_Armadillo', 'VanillaExpanded.VAEEndAndExt'))
    return path


def _execute(patch, runtime, mods):
    applier = PatchApplier(active_mods=mods, record_missing=True)
    assert applier.apply_patch_root(patch, runtime)
    assert not applier.missing_targets


def test_cross_mod_consumers_from_worksheet_and_registry(tmp_path):
    rows, entries, _, _ = _fixture(tmp_path)
    consumers = _cross_mod_consumers(rows, entries, None, _animals(tmp_path))
    assert consumers == {'Leather_Armadillo':
                         {'Megafauna', 'Vanilla Animals Expanded — Endangered'}}


def test_one_stub_across_any_consumer_combination_without_odyssey(tmp_path):
    rows, entries, originals, patches = _fixture(tmp_path)
    consumers = _cross_mod_consumers(rows, entries, None, _animals(tmp_path))
    rendered, _, report = render_original_overrides(
        rows, originals, patches, ce=False, consumers=consumers)
    assert set(report['odyssey_fallbacks']['Leather_Armadillo']) == set(consumers['Leather_Armadillo'])
    megafauna = E.fromstring(rendered[GROUP_ROUTES['Megafauna']])
    endangered = E.fromstring(rendered[GROUP_ROUTES['Vanilla Animals Expanded — Endangered']])
    for mods, apply_megafauna, apply_endangered, expected in (
        ([], False, False, 0),
        (['Megafauna'], True, False, 1),
        (['Vanilla Animals Expanded — Endangered'], False, True, 1),
        (['Megafauna','Vanilla Animals Expanded — Endangered'], True, True, 1),
        (['Megafauna','Vanilla Animals Expanded — Endangered','Odyssey'], True, True, 0),
    ):
        root = E.Element('Defs')
        if apply_endangered:
            _execute(endangered, root, mods)
        if apply_megafauna:
            _execute(megafauna, root, mods)
        assert len(root.xpath('./ThingDef[defName="Leather_Armadillo"]')) == expected
        if expected:
            assert root.xpath('string(ThingDef/statBases/StuffPower_Armor_Sharp)') == '1.29'


def test_fallback_material_uses_sheet_ce_stats(tmp_path):
    rows, _, originals, patches = _fixture(tmp_path)
    refs = LeatherReferences(originals, patches, rows, ce=False)
    refs.ce_available = True
    refs.ce_before = {'Leather_Armadillo': {
        'StuffPower_Armor_Sharp': 0.09,
        'StuffPower_Armor_Blunt': 0.064}}
    rendered, _, _ = render_original_overrides(
        rows, originals, patches, ce=True, refs=refs,
        consumers={'Leather_Armadillo': {'Megafauna'}})
    patch = E.fromstring(rendered[GROUP_ROUTES['Megafauna']])
    for mods, sharp in (([], '1.29'), (['Combat Extended'], '0.09')):
        root = E.Element('Defs')
        _execute(patch, root, mods)
        assert root.xpath('string(ThingDef/statBases/StuffPower_Armor_Sharp)') == sharp


def test_nested_field_addition_does_not_skip_desired_value(tmp_path):
    from core.leather_overrides import _conditional
    patch = E.Element('Patch')
    original = E.fromstring('<ThingDef><defName>WoolTest</defName></ThingDef>')
    _conditional(patch, 'WoolTest', 'stuffProps/statFactors/MaxHitPoints', '1.5', original)
    root = E.fromstring('<Defs><ThingDef><defName>WoolTest</defName></ThingDef></Defs>')
    _execute(patch, root, [])
    assert root.xpath('string(ThingDef/stuffProps/statFactors/MaxHitPoints)') == '1.5'
    # Repeat after another source introduced the intermediate containers.
    again = E.fromstring('<Defs><ThingDef><defName>WoolTest</defName>'
                         '<stuffProps><statFactors><MaxHitPoints>0.8</MaxHitPoints>'
                         '</statFactors></stuffProps></ThingDef></Defs>')
    _execute(patch, again, [])
    assert again.xpath('string(ThingDef/stuffProps/statFactors/MaxHitPoints)') == '1.5'


def test_reference_index_never_reads_ce_patch_files_as_defs(tmp_path):
    from core.rimworld_original_xml import OriginalXmlIndex
    originals = tmp_path / 'OriginalXML'
    core = originals / 'Core'
    core.mkdir(parents=True)
    (core / 'Core.xml').write_text('<Defs><ThingDef><defName>Wolf</defName>'
                                  '</ThingDef></Defs>', encoding='utf8')
    patches = originals / 'CEPatches' / 'Patches' / 'Core'
    patches.mkdir(parents=True)
    (patches / 'Animals.xml').write_text(
        '<Patch><Operation Class="PatchOperationReplace">'
        '<xpath>/Defs/ThingDef[defName="Wolf"]/label</xpath>'
        '<value><label>CE Wolf</label></value></Operation></Patch>', encoding='utf8')
    index = OriginalXmlIndex(str(originals)).load()
    assert index.files_loaded == 1
    assert index.patch_files_loaded == 0
    enabled = OriginalXmlIndex(str(originals), str(originals / 'CEPatches')).load()
    assert enabled.files_loaded == 1
    assert enabled.patch_files_loaded == 1


def test_ce_reference_sync_discovers_base_and_race_patches(tmp_path):
    import hashlib
    from core.leather_reference_sync import discover_new_leather_references
    class Source:
        files = ['Patches/Core/Races_Animal_Bears.xml',
                 'Patches/Core/Items_Resource_Stuff.xml']
        provenance = 'local CE fixture'
        def read(self, rel):
            if 'Races_' in rel:
                return b'<Patch><Operation Class="PatchOperationReplace"><xpath>Defs/ThingDef[defName="Bear"]</xpath><value><ThingDef/></value></Operation></Patch>'
            return b'<Patch><Operation Class="PatchOperationReplace"><xpath>Defs/ThingDef[@Name="LeatherBase"]/statBases/StuffPower_Armor_Sharp</xpath><value><StuffPower_Armor_Sharp>0.03</StuffPower_Armor_Sharp></value></Operation></Patch>'
    def safe_inside(root, rel):
        return Path(root) / rel
    def parse(data, expected):
        node = E.fromstring(data)
        assert node.tag == expected
        return node
    entries = discover_new_leather_references(
        {'id':'CombatExtended','reference_dirs':['OriginalXML/CEPatches']},
        [Source()], tmp_path, [], '1.7',
        safe_inside=safe_inside, parse_xml=parse,
        digest=lambda data: hashlib.sha256(data).hexdigest() if data is not None else None)
    assert len(entries) == 2
    assert all(x['destination'].startswith('OriginalXML/CEPatches/') for x in entries)
    assert all(x['status'] == 'COPY' for x in entries)


def test_missing_animals_source_fails_instead_of_silently_omitting_fallbacks(tmp_path):
    import pytest
    rows, entries, _, _ = _fixture(tmp_path)
    with pytest.raises(ValueError, match='Odyssey cross-mod material dependencies'):
        _cross_mod_consumers(rows, entries, str(tmp_path / 'Leathers.tsv'), None)
