"""Runtime checks for leather material ownership and optional-mod fallbacks."""
from __future__ import annotations

from collections import Counter
from pathlib import Path
import sys

import pytest
from lxml import etree as E

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 'checker'))

from core.rimworld_patch_apply import PatchApplier

CUSTOM = 'Zoology/Core/Defs/ThingDefs_Items/Items_Resource_Stuff_Leather_Zoology.xml'
ODYSSEY = 'Zoology/Core/Patches/Core/ThingDefs_Items/Items_Resource_Stuff_Leather_OdysseyFallback.xml'
CHITIN = 'Zoology/Core/Patches/Core/ThingDefs_Items/Leather_Chitin.xml'
TOXIC = 'Zoology/DLC/Biotech/Patches/Misc/Leather_Biotech.xml'
VAE = 'Zoology/ModPatches/VanillaAnimalsExpanded/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_VAE.xml'


def _apply(path, active=(), originals=()):
    target = E.Element('Defs')
    for name in originals:
        item = E.SubElement(target, 'ThingDef')
        E.SubElement(item, 'defName').text = name
        stats = E.SubElement(item, 'statBases')
        E.SubElement(stats, 'StuffPower_Armor_Sharp').text = '0.321'
    patch = E.parse(str(REPO / path)).getroot()
    errors = []
    applier = PatchApplier(active_mods=active, errors=errors, record_missing=False)
    assert applier.apply_patch_root(patch, target), (path, errors)
    assert not errors, (path, errors)
    return target, patch, applier


def _names(root):
    return Counter(root.xpath('./ThingDef/defName/text()'))


def test_custom_leathers_have_unique_defnames_and_parent():
    defs = E.parse(str(REPO / CUSTOM)).getroot()
    expected = {'Leather_Beaver', 'Leather_Ostrich', 'Leather_Otter', 'Leather_Squirrel'}
    assert set(_names(defs)) == expected
    assert all(count == 1 for count in _names(defs).values())
    assert all(thing.get('ParentName') == 'LeatherBase' for thing in defs)


@pytest.mark.parametrize('active,existing,expected', [
    ((), (), {'Leather_Armadillo', 'Leather_Hippo', 'Leather_Sealskin'}),
    (('Vanilla Animals Expanded — Endangered',), (), {'Leather_Armadillo', 'Leather_Hippo', 'Leather_Sealskin'}),
    (('Combat Extended',), (), {'Leather_Armadillo', 'Leather_Hippo', 'Leather_Sealskin'}),
    (('Odyssey',), (), set()),
    ((), ('Leather_Armadillo',), {'Leather_Armadillo', 'Leather_Hippo', 'Leather_Sealskin'}),
])
def test_odyssey_fallback_has_no_duplicates(active, existing, expected):
    root, patch, applier = _apply(ODYSSEY, active, existing)
    assert set(_names(root)) == expected
    assert all(count == 1 for count in _names(root).values())
    assert patch.xpath('string(./Operation/mods/li)') == 'Odyssey'
    if 'Combat Extended' in active:
        assert root.xpath('string(./ThingDef[defName="Leather_Armadillo"]/statBases/StuffPower_Armor_Sharp)') == '0.09'


@pytest.mark.parametrize('path,source_mod,material', [
    (CHITIN, 'Alpha Animals', 'Leather_Chitin'),
    (TOXIC, 'Vanilla Animals Expanded — Waste Animals', 'VAEWaste_ToxicLeather'),
])
def test_optional_materials_are_added_only_when_needed(path, source_mod, material):
    created, _, _ = _apply(path, ())
    assert _names(created)[material] == 1
    installed, _, _ = _apply(path, (source_mod,))
    assert _names(installed)[material] == 0
    supplied, _, _ = _apply(path, ('Combat Extended',), (material,))
    assert _names(supplied)[material] == 1
    # A third-party material with the same defName belongs to its source,
    # not to our fallback or its CE-specific values.
    assert supplied.xpath('string(./ThingDef[defName="' + material +
                          '"]/statBases/StuffPower_Armor_Sharp)') == '0.321'


@pytest.mark.parametrize('path,material', [
    (CHITIN, 'Leather_Chitin'),
    (TOXIC, 'VAEWaste_ToxicLeather'),
])
def test_optional_fallbacks_apply_ce_only_to_own_new_material(path, material):
    created, _, _ = _apply(path, ('Combat Extended',))
    assert _names(created)[material] == 1
    sharp = created.xpath('string(./ThingDef[defName="' + material +
                          '"]/statBases/StuffPower_Armor_Sharp)')
    assert sharp == ('0.15' if material == 'Leather_Chitin' else '0.045')


def test_vae_optional_leather_removals_are_idempotent():
    patch = E.parse(str(REPO / VAE)).getroot()
    targets = {'AEXP_Leather_Wildebeest', 'AEXP_Leather_Platypus', 'AEXP_Leather_Lion'}
    # Optional source defs may be absent on some VAE builds.
    for present in (False, True):
        root = E.Element('Defs')
        if present:
            for name in targets:
                item = E.SubElement(root, 'ThingDef')
                E.SubElement(item, 'defName').text = name
        applier = PatchApplier(record_missing=True)
        for operation in list(patch)[:3]:
            assert applier.apply_operation(operation, root)
        assert not (set(_names(root)) & targets)
        assert not applier.missing_targets
