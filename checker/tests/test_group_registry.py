"""Full Git-configured group ownership with no generated-patch inputs."""
from pathlib import Path
import json
import tempfile
from lxml import etree as E
from core.generation_registry import load_registry


def test_all_versioned_groups_have_unique_targets_and_defs():
    root=Path(__file__).resolve().parents[1]
    config=json.loads((root/'generation.json').read_text())
    groups=config['groups']
    assert len(groups)==81
    assert len({g['destination'] for g in groups})==81
    animal_names=[name for g in groups for name in g['defs']]
    assert len(animal_names)==407
    assert len(set(animal_names))==407
    assert len(config['biomes'])==31
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
