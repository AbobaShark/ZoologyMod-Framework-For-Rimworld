"""Explicit cleanup of material outputs with zero operations."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

import pytest

from core.xml_routing import plan, deploy, STATE_NAME


LEATHER = 'ModPatches/Dinosauria/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml'
STATIC = 'Core/Defs/ThingDefs_Items/Items_Resource_Stuff_Leather_Zoology.xml'


def fixture(tmp_path):
    mod = tmp_path / 'Zoology'
    original = mod / LEATHER
    original.parent.mkdir(parents=True)
    original.write_text('<Patch><Operation Class="PatchOperationAdd"><xpath>Defs</xpath></Operation></Patch>')
    retained = mod / STATIC
    retained.parent.mkdir(parents=True)
    retained.write_text('<Defs/>')
    old_sha = hashlib.sha256(original.read_bytes()).hexdigest()
    (mod / STATE_NAME).write_text(json.dumps({
        'schema': 1, 'files': {LEATHER: old_sha}, 'directories': [],
    }))
    stage = tmp_path / 'staged'
    stage.mkdir()
    manifest = stage / 'generation-manifest.json'
    manifest.write_text(json.dumps({
        'schema': 1, 'prune': False, 'files': [], 'remove': [LEATHER],
    }))
    return mod, manifest, original, retained


def test_cleanup_only_generated_material_xml(tmp_path):
    _, manifest, original, retained = fixture(tmp_path)
    operations, _ = plan(manifest, tmp_path)
    assert [(action, key) for action, _, _, key in operations] == [('delete', LEATHER)]
    deploy(manifest, tmp_path)
    assert not original.exists()
    assert retained.exists()


def test_cleanup_never_removes_native_material_definitions(tmp_path):
    _, manifest, _, retained = fixture(tmp_path)
    data = json.loads(manifest.read_text())
    data['remove'] = [STATIC]
    manifest.write_text(json.dumps(data))
    with pytest.raises(ValueError, match='Unsafe or conflicting'):
        plan(manifest, tmp_path)
    assert retained.exists()


def test_cleanup_rejects_modified_generated_xml(tmp_path):
    _, manifest, original, _ = fixture(tmp_path)
    original.write_text('<Patch><Operation Class="PatchOperationRemove"/></Patch>')
    with pytest.raises(ValueError, match='changed since last deployment'):
        plan(manifest, tmp_path)


def test_cleanup_rejects_duplicate_targets(tmp_path):
    _, manifest, _, _ = fixture(tmp_path)
    data = json.loads(manifest.read_text())
    data['remove'] = [LEATHER, LEATHER]
    manifest.write_text(json.dumps(data))
    with pytest.raises(ValueError, match='Invalid generated XML removals'):
        plan(manifest, tmp_path)
