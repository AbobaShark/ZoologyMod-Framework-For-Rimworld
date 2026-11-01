"""Verify the mutually exclusive native and VAE muskox patch routes."""
from pathlib import Path

from lxml import etree as E

ZOOLOGY = Path(__file__).resolve().parents[2] / "Zoology"


def _read(relative):
    return E.parse(str(ZOOLOGY / relative))


def _assert_no_odyssey_guard(relative, defname):
    tree = _read(relative)
    matches = tree.xpath('//xpath[contains(text(), $name)]', name=defname)
    assert matches, f"No patches for {defname} in {relative}"
    for node in matches:
        guards = node.xpath('ancestor::*[@Class="PatchOperationFindMod" and mods/li="Odyssey"]')
        assert guards, f"Unguarded patch for {defname} in {relative}"
        assert any(node in guard.xpath('./nomatch//xpath') for guard in guards), (
            f"Patch for {defname} must use Odyssey nomatch")


def test_vae_muskox_only_patched_without_odyssey():
    _assert_no_odyssey_guard(
        'ModPatches/VanillaAnimalsExpanded/Patches/ThingDefs_Races/'
        'Races_Animal_NoOdyssey_VAE.xml', 'AEXP_Muskox')


def test_vae_muskox_wool_only_patched_without_odyssey():
    _assert_no_odyssey_guard(
        'ModPatches/VanillaAnimalsExpanded/Patches/ThingDefs_Items/'
        'Items_Resource_Stuff_AnimalTextiles_Generated.xml', 'AEXP_MuskOxWool')


def test_odyssey_muskox_and_wool_use_conditional_dlc_folder():
    folders = _read('LoadFolders.xml')
    for version in ('v1.6', 'v1.7'):
        assert folders.xpath(
            '/loadFolders/' + version
            + '/li[@IfModActive="Ludeon.Rimworld.Odyssey" and text()="DLC/Odyssey"]')
    race = _read('DLC/Odyssey/Patches/ThingDefs_Races/Races_Animal_Ruminants_Odyssey.xml')
    wool = _read('DLC/Odyssey/Patches/ThingDefs_Items/Items_Resource_Stuff_AnimalTextiles_Generated.xml')
    assert race.xpath('//xpath[contains(text(), "Muskox")]')
    assert wool.xpath('//xpath[contains(text(), "WoolMuskox")]')
    assert not wool.xpath('//xpath[contains(text(), "AEXP_MuskOxWool")]')
