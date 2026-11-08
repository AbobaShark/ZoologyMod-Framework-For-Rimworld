"""Keep upstream references tied to the actual Zoology generation registry."""
import json
from pathlib import Path

from lxml import etree as E

from core.reference_inventory import _definitions, _patch_is_relevant, _candidate_path
from tools import reference_sync_auto as sync


def _write(root, relative, data):
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(data, encoding='utf-8')
    return path


def test_rejects_human_pawnkinds_buildings_plants_and_unrelated_misc_xml(tmp_path):
    checker = tmp_path / 'checker'
    checker.mkdir()
    steam = tmp_path / 'SteamLibrary'
    workshop = steam / 'steamapps/workshop/content/294100'
    mod = workshop / '1541721856'
    _write(mod, '1.7/Defs/ThingDefs_Races/Animals.xml',
           '<Defs><ThingDef><defName>AA_TestWolf</defName><race/></ThingDef></Defs>')
    _write(mod, '1.7/Defs/PawnKindDefs_Humanlikes/PawnKinds_Mercenary.xml',
           '<Defs><PawnKindDef><defName>Mercenary_Slasher</defName></PawnKindDef></Defs>')
    _write(mod, '1.7/Defs/ThingDefs_Buildings/Buildings.xml',
           '<Defs><ThingDef><defName>AA_AlienBuilding</defName></ThingDef></Defs>')
    _write(mod, '1.7/Defs/ThingDefs_Plants/Plants.xml',
           '<Defs><ThingDef><defName>AA_AlienPlant</defName></ThingDef></Defs>')
    _write(mod, '1.7/Defs/ThingDefs_Items/Items_Resource_Misc.xml',
           '<Defs><ThingDef><defName>GU_RedWood</defName></ThingDef></Defs>')
    _write(mod, '1.7/Defs/ThingDefs_Items/Items_Resource_Stuff.xml',
           '<Defs><ThingDef ParentName="LeatherBase">'
           '<defName>AA_WolfLeather</defName></ThingDef></Defs>')
    _write(mod, '1.7/Defs/ThingDefs_Items/Items_Eggs.xml',
           '<Defs><ThingDef><defName>EggAAWolfFertilized</defName></ThingDef></Defs>')
    (checker / 'generation.json').write_text(
        json.dumps({'groups':[{'mod':'AlphaAnimals','defs':['AA_TestWolf'],
                               'abstracts':['AA_WolfBase']}], 'biomes':[]}),encoding='utf-8')
    config = {'rimworld_version':'1.7','mods':[{
        'id':'AlphaAnimals','workshop_ids':['1541721856'],
        'reference_dirs':['OriginalXML/AlphaAnimals']}]}
    entries, warnings = sync.discover(config, checker_root=checker,
                                      steam=str(steam), github=False)
    destinations = [entry['destination'] for entry in entries]
    assert len(entries) == 3
    assert all(entry['status'] == 'COPY' for entry in entries)
    assert any('Animals.xml' in p for p in destinations)
    assert any('Items_Resource_Stuff.xml' in p for p in destinations)
    assert any('Items_Eggs.xml' in p for p in destinations)
    assert not any('Misc.xml' in p or 'Buildings.xml' in p
                   or 'Plants.xml' in p or 'Mercenary.xml' in p
                   for p in destinations)


def test_existing_irrelevant_references_are_scheduled_for_reviewed_removal(tmp_path):
    checker = tmp_path / 'checker'
    checker.mkdir()
    steam = tmp_path / 'SteamLibrary'
    workshop = steam / 'steamapps/workshop/content/294100'
    mod = workshop / '1541721856'
    good = '<Defs><ThingDef><defName>AA_TestWolf</defName></ThingDef></Defs>'
    bad = '<Defs><ThingDef><defName>GU_RedWood</defName></ThingDef></Defs>'
    _write(mod, '1.7/Defs/ThingDefs_Races/Animals.xml', good)
    _write(mod, '1.7/Defs/ThingDefs_Items/Items_Resource_Misc.xml', bad)
    obsolete = _write(checker, 'OriginalXML/AlphaAnimals/1.7/Defs/ThingDefs_Items/Items_Resource_Misc.xml', bad)
    (checker / 'generation.json').write_text(
        json.dumps({'groups':[{'mod':'AlphaAnimals','defs':['AA_TestWolf'],
                               'abstracts':[]}], 'biomes':[]}), encoding='utf-8')
    config = {'rimworld_version':'1.7','mods':[{
        'id':'AlphaAnimals','workshop_ids':['1541721856'],
        'reference_dirs':['OriginalXML/AlphaAnimals']}]}
    preview, warnings = sync.discover(config, checker_root=checker,
                                      steam=str(steam), github=False)
    assert {entry['status'] for entry in preview} == {'COPY','DELETE'}
    assert obsolete.is_file()
    backup = sync.apply(preview, checker_root=checker)
    assert not obsolete.exists()
    assert (backup / obsolete.relative_to(checker)).is_file()


def test_ce_filter_selects_relevant_animal_and_material_operations_only():
    def patch(name):
        return E.fromstring(
            '<Patch><Operation Class="PatchOperationReplace">'
            f'<xpath>Defs/ThingDef[defName="{name}"]/statBases/MarketValue</xpath>'
            '<value><MarketValue>5</MarketValue></value>'
            '</Operation></Patch>')
    sample = 'ModPatches/Alpha Animals/Patches/Alpha Animals/Races_Animal.xml'
    assert _patch_is_relevant(sample, patch('AA_TestWolf'), {'AA_TestWolf'}, strict=True)
    assert not _patch_is_relevant(sample, patch('GU_RedWood'), {'AA_TestWolf'}, strict=True)
    assert not _patch_is_relevant(sample, patch('AEXP_RawFish'), {'AA_TestWolf'}, strict=True)
    assert _patch_is_relevant('Patches/Core/Items_Resource_Stuff.xml',
                              patch('Leather_Light'), {'AA_TestWolf'}, strict=True)


def test_ce_modpatches_exclude_unsupported_mods_even_for_generic_leatherbase():
    allowed = {'alphaanimals', 'vanillaanimalsexpandedendangered'}
    assert _candidate_path(
        'ModPatches/Alpha Animals/Patches/Alpha Animals/AA_Resource_CEStuff.xml',
        ce=True, allowed_mods=allowed)
    assert not _candidate_path(
        'ModPatches/Unsupported Mod/Patches/Items_Resource_Stuff.xml',
        ce=True, allowed_mods=allowed)
    assert _candidate_path(
        'Patches/Core/ThingDefs_Items/Items_Resource_Stuff.xml',
        ce=True, allowed_mods=allowed)


def test_no_odyssey_optional_source_is_allowed():
    assert _candidate_path(
        '1.6/Mods/NotOdyssey/Defs/Races_Expanded_Endangered_NotOdyssey.xml',
        ce=False, allowed_mods={'vanillaanimalsexpandedendangered'})


def test_panda_reference_import_is_independent_of_installed_odyssey(tmp_path):
    checker = tmp_path / 'checker'
    checker.mkdir()
    steam = tmp_path / 'SteamLibrary'
    mod = steam / 'steamapps/workshop/content/294100/2366589898'
    _write(mod, '1.6/Defs/ThingDefs_Races/Races_Expanded_Endangered.xml',
           '<Defs><ThingDef><defName>AEXP_Pangolin</defName></ThingDef></Defs>')
    _write(mod, '1.6/Mods/NotOdyssey/Defs/Races_Expanded_Endangered_NotOdyssey.xml',
           '<Defs><ThingDef ParentName="AnimalThingBase">'
           '<defName>AEXP_Panda</defName></ThingDef>'
           '<PawnKindDef><defName>AEXP_Panda</defName>'
           '<race>AEXP_Panda</race></PawnKindDef></Defs>')
    (checker / 'generation.json').write_text(
        json.dumps({'groups': [
            {'mod': 'VanillaAnimalsExpandedEndangered', 'defs': ['AEXP_Pangolin']},
            {'mod': 'VanillaAnimalsExpandedEndangered', 'defs': ['AEXP_Panda']},
        ], 'biomes': []}), encoding='utf-8')
    cfg = {'rimworld_version': '1.7', 'mods': [
        {'id': 'Odyssey', 'enabled': True, 'game_dlc': 'Odyssey',
         'reference_dirs': ['OriginalXML/Odyssey']},
        {'id': 'VanillaAnimalsExpandedEndangered', 'enabled': True,
         'workshop_ids': ['2366589898'],
         'reference_dirs': ['OriginalXML/Vanilla Animals Expanded — Endangered']},
    ]}
    def snapshot():
        result, warnings = sync.discover(
            cfg, checker_root=checker, steam=str(steam), github=False)
        assert not any('AEXP_Panda' in warning for warning in warnings)
        files = [item for item in result
                 if item['source_id'] == 'VanillaAnimalsExpandedEndangered']
        assert len(files) == 2
        assert any('Mods/NotOdyssey/Defs/' in item['destination'] and
                   b'AEXP_Panda' in item['payload'] for item in files)
        return {item['destination'] for item in files}

    absent = snapshot()
    _write(steam / 'steamapps/common/RimWorld/Data/Odyssey',
           'Defs/ThingDefs_Races/Races_Animal.xml',
           '<Defs><ThingDef><defName>UnrelatedOdysseyAnimal</defName>'
           '</ThingDef></Defs>')
    assert snapshot() == absent


def test_optional_mod_definitions_require_supported_owner():
    allowed = {'odyssey','alphaanimals'}
    assert _candidate_path('1.7/Mods/Odyssey/Defs/BiomeDefs.xml',
                           ce=False, allowed_mods=allowed)
    assert not _candidate_path('1.7/Mods/UnsupportedExpansion/Defs/AnimalItems.xml',
                               ce=False, allowed_mods=allowed)


def test_duplicate_keys_after_ce_are_distinguished_from_source_duplicates(tmp_path):
    from core.rimworld_original_xml import OriginalXmlIndex

    core = tmp_path / 'Core'
    ce = tmp_path / 'CEPatches'
    _write(core, 'Defs/Animals.xml',
           '<Defs><ThingDef><defName>TestAnimal</defName></ThingDef></Defs>')
    _write(ce, 'Patches/Animals.xml',
           '<Patch><Operation Class="PatchOperationAdd">'
           '<xpath>Defs</xpath><value>'
           '<ThingDef><defName>TestAnimal</defName></ThingDef>'
           '</value></Operation></Patch>')
    index = OriginalXmlIndex(str(core), str(ce)).load()
    assert not index.duplicates_before_patches
    assert len(index.duplicates) == 1
    assert index.duplicates[0][0] == ('ThingDef', 'defName', 'TestAnimal')
    assert index.duplicates[0][1] == index.duplicates[0][2]


def test_zoology_still_removes_endangered_raw_fish_definitions():
    root = Path(__file__).resolve().parents[2]
    path = root / ('Zoology/ModPatches/VanillaAnimalsExpandedEndangered/'
                   'Patches/Misc/Foraging_VAEE.xml')
    patch = E.parse(str(path))
    for xpath in ('/Defs/ThingDef[@Name="AEXP_RawFishBase"]',
                  '/Defs/ThingDef[defName="AEXP_RawFish"]'):
        operations = patch.xpath('//Operation[@Class="PatchOperationConditional"]'
                                 '/match[@Class="PatchOperationRemove"]'
                                 '/xpath[text()=$target]', target=xpath)
        assert len(operations) == 1
