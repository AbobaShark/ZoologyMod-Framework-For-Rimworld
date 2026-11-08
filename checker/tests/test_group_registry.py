"""Full Git-configured group ownership with no generated-patch inputs."""
from pathlib import Path
import json
import tempfile
from lxml import etree as E
from core.generation_registry import load_registry, _unregistered_material_xml


def test_all_versioned_groups_have_unique_targets_and_defs():
    root=Path(__file__).resolve().parents[1]
    config=json.loads((root/'generation.json').read_text())
    groups=config['groups']
    assert len(groups)==81
    assert len({g['destination'] for g in groups})==81
    animal_names=[name for g in groups for name in g['defs']]
    assert len(animal_names)==407
    assert len(set(animal_names))==407
    assert len(config['biomes']) == 29
    assert set(config['reference_only_biomes']) == {
        'Alpine', 'MammothSteppe', 'Delta'
    }
    assert not (root/'generated_patches').exists()
    assert not (root/'rimworld_patch_fixer.py').exists()


def test_group_metadata_contains_only_explicit_references():
    root=Path(__file__).resolve().parents[1]
    config=json.loads((root/'generation.json').read_text())
    for group in config['groups']:
        assert all('/' not in def_name for def_name in group['defs'])
        assert group['destination'].endswith('.xml')
        assert group['destination'].startswith(('Core/','DLC/','ModPatches/'))
        assert all(key in {'sounds','trade_tags_inherit_false'} for key in group.get('overrides',{}))
        assert 'template' not in group


def test_leathers_only_generated_and_three_launchers():
    root = Path(__file__).resolve().parents[1]
    conf = json.loads((root / 'generation.json').read_text())
    leathers = [x for x in conf['outputs'] if x['pipeline_category'] == 'leathers']
    assert len(leathers) == 10
    assert all(x['source'] == 'generated' for x in leathers)
    assert {p.name for p in root.glob('*.bat')} == {
        'START_REFERENCE_SYNC.bat', 'START_PATCH_GENERATOR.bat',
        'START_DEF_GENERATOR.bat',
    }
    mod = root.parent / 'Zoology'
    if (mod / 'LoadFolders.xml').exists():
        # The preflight must not depend on any old manual leather templates.
        load_registry(root / 'generation.json', mod)
        source = mod / 'Core/Defs/ThingDefs_Items/Items_Resource_Stuff_Leather_Zoology.xml'
        xml = E.parse(str(source)).getroot()
        assert {t.findtext('defName') for t in xml.findall('ThingDef')} == {
            'Leather_Beaver', 'Leather_Ostrich', 'Leather_Otter', 'Leather_Squirrel'
        }


def test_material_preflight_checks_loaded_folders_not_archived_sources(tmp_path):
    mod = tmp_path / 'Zoology'
    mod.mkdir()
    (mod / 'LoadFolders.xml').write_text(
        '<loadFolders><v1.7><li>/</li><li>Core</li>'
        '<li>ModPatches/AlphaAnimals</li></v1.7></loadFolders>',
        encoding='utf-8')
    copied = mod / 'Source/codex-build/implementation/test-mods-archive'
    copied.mkdir(parents=True)
    (copied / 'Items_Resource_Stuff_Leather.xml').write_text('<Patch/>')
    assert _unregistered_material_xml(mod, set()) == []
    active = mod / 'Core/Patches/Core/ThingDefs_Items'
    active.mkdir(parents=True)
    legacy = active / 'Leather_Chitin.xml'
    legacy.write_text('<Patch/>')
    assert _unregistered_material_xml(mod, set()) == [
        'Core/Patches/Core/ThingDefs_Items/Leather_Chitin.xml'
    ]
    assert _unregistered_material_xml(
        mod, {'Core/Patches/Core/ThingDefs_Items/Leather_Chitin.xml'}) == []
