"""Regression tests for Reference Sync configuration and obsolete cache deletion."""
import json
from unittest.mock import patch
import reference_sync_gui as gui


def test_uses_versioned_reference_paths_not_local_paths(tmp_path):
    defaults = tmp_path / 'reference_sources.auto.json'
    defaults.write_text(json.dumps({'schema': 2, 'mods': [
        {'id': 'Core', 'reference_dirs': ['OriginalXML/Core'], 'enabled': True},
        {'id': 'CombatExtended', 'reference_dirs': ['OriginalXML/CEPatches'], 'enabled': True},
    ]}), encoding='utf8')
    local = tmp_path / 'checker.local.json'
    local.write_text(json.dumps({'patch_generator': {'enable_ce': False},
                                 'reference_sync': {'steam_root': 'C:/Steam', 'mods': {
                                     'CombatExtended': {'enabled': False,
                                                        'reference_dirs': ['OriginalPatches/CE']}}}}),
                     encoding='utf8')
    with patch.object(gui, 'ROOT', tmp_path), patch.object(gui, 'DEFAULT', defaults), patch.object(gui, 'SETTINGS', local):
        config = gui._load_reference_config()
        assert config['mods'][1]['reference_dirs'] == ['OriginalXML/CEPatches']
        assert config['mods'][1]['enabled'] is False
        assert config['steam_root'] == 'C:/Steam'
        gui._save_reference_config(config)
    saved = json.loads(local.read_text(encoding='utf8'))
    assert saved['patch_generator'] == {'enable_ce': False}
    assert 'reference_dirs' not in str(saved['reference_sync'])


def test_discard_obsolete_files(tmp_path):
    obsolete = tmp_path / 'output/reference_sources.local.json'
    obsolete.parent.mkdir()
    obsolete.write_text('old configuration')
    patches = tmp_path / 'OriginalPatches/CE'
    patches.mkdir(parents=True)
    (patches / 'file.xml').write_text('<Patch/>')
    generated = tmp_path / 'generated_patches'
    generated.mkdir()
    (generated / 'old.xml').write_text('<Patch/>')
    with patch.object(gui, 'ROOT', tmp_path):
        gui._remove_obsolete_files()
    assert not obsolete.exists()
    assert not patches.exists()
    assert not generated.exists()
