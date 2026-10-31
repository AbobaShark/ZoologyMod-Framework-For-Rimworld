"""Reference Sync rebuilds game and Workshop definitions without a seeded cache."""
from __future__ import annotations

import json
from pathlib import Path

from lxml import etree as E

from core.reference_inventory import selected_paths
from tools import reference_sync_auto as sync


def _write(root, relative, content):
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding='utf8')
    return path


def test_empty_originalxml_imports_animals_biomes_eggs_materials_and_ce(tmp_path):
    checker = tmp_path / 'checker'
    checker.mkdir()
    steam = tmp_path / 'SteamLibrary'
    game = steam / 'steamapps/common/RimWorld'
    workshop = steam / 'steamapps/workshop/content/294100'

    _write(game / 'Data/Core', 'Defs/ThingDefs_Races/Races_Animal.xml',
           '<Defs><ThingDef><defName>Rat</defName><race/></ThingDef></Defs>')
    _write(game / 'Data/Core', 'Defs/BiomeDefs/Biomes.xml',
           '<Defs><BiomeDef><defName>Desert</defName></BiomeDef></Defs>')
    _write(game / 'Data/Core', 'Defs/ThingDefs_Items/Items_Eggs.xml',
           '<Defs><ThingDef><defName>EggChickenFertilized</defName></ThingDef></Defs>')
    _write(game / 'Data/Core', 'Defs/ThingDefs_Items/Items_Resource_Stuff.xml',
           '<Defs><ThingDef Name="LeatherBase" Abstract="True"/>'
           '<ThingDef ParentName="LeatherBase"><defName>Leather_Light</defName></ThingDef></Defs>')
    _write(game / 'Data/Core', 'Defs/ThingDefs_Races/Photonozoa.xml',
           '<Defs><ThingDef><defName>Photonozoa</defName></ThingDef></Defs>')
    _write(workshop / '1136958577', '1.6/Defs/ThingDefs_Races/Animals.xml',
           '<Defs><ThingDef><defName>OldDino</defName></ThingDef></Defs>')
    _write(workshop / '1136958577', '1.7/Defs/ThingDefs_Races/Animals.xml',
           '<Defs><ThingDef><defName>TestDino</defName></ThingDef></Defs>')
    _write(workshop / '1055485938', '1.6/Defs/ThingDefs_Races/Mega.xml',
           '<Defs><ThingDef><defName>TestMega</defName></ThingDef></Defs>')
    _write(workshop / '1841354677', '1.6/Defs/BiomeDefs/Custom.xml',
           '<Defs><BiomeDef><defName>AB_IdyllicMeadows</defName></BiomeDef></Defs>')
    _write(workshop / '2890901044', 'Patches/Core/Items_Resource_Stuff.xml',
           '<Patch><Operation Class="PatchOperationReplace">'
           '<xpath>Defs/ThingDef[@Name="LeatherBase"]/statBases/StuffPower_Armor_Sharp</xpath>'
           '<value><StuffPower_Armor_Sharp>0.05</StuffPower_Armor_Sharp></value>'
           '</Operation></Patch>')

    registry = {'groups': [
        {'mod': 'Core', 'defs': ['Rat']},
        {'mod': 'Dinosauria', 'defs': ['TestDino']},
        {'mod': 'Megafauna', 'defs': ['TestMega']},
    ], 'biomes': [
        {'destination': 'Core/Patches/BiomeDefs/Desert.xml', 'defName': 'Desert'},
        {'destination': 'ModPatches/AlphaBiomes/Patches/Biomes/Idyllic.xml',
         'defName': 'AB_IdyllicMeadows'},
    ]}
    (checker / 'generation.json').write_text(json.dumps(registry), encoding='utf8')
    mods = [
        {'id': 'Core', 'game_dlc': 'Core', 'reference_dirs': ['OriginalXML/Core']},
        {'id': 'Dinosauria', 'workshop_ids': ['1136958577'],
         'reference_dirs': ['OriginalXML/Dinosauria']},
        {'id': 'Megafauna', 'workshop_ids': ['1055485938'],
         'reference_dirs': ['OriginalXML/Megafauna']},
        {'id': 'AlphaBiomes', 'workshop_ids': ['1841354677'],
         'reference_dirs': ['OriginalXML/AlphaBiomes']},
        {'id': 'CombatExtended', 'workshop_ids': ['2890901044'],
         'reference_dirs': ['OriginalXML/CEPatches']},
    ]
    assert not (checker / 'OriginalXML').exists()
    result, warnings = sync.discover({'mods': mods, 'rimworld_version': '1.7'},
                                     steam=str(steam), github=False, checker_root=checker)
    assert len(result) == 8
    assert all(item['status'] == 'COPY' for item in result)
    assert not [warning for warning in warnings if 'registered DefNames missing' in warning]
    destinations = {item['destination'] for item in result}
    assert 'OriginalXML/Core/_Defs/Defs/ThingDefs_Races/Races_Animal.xml' in destinations
    assert 'OriginalXML/Core/_Defs/Defs/BiomeDefs/Biomes.xml' in destinations
    assert 'OriginalXML/Core/_Defs/Defs/ThingDefs_Items/Items_Eggs.xml' in destinations
    assert 'OriginalXML/Core/_Leathers/Defs/ThingDefs_Items/Items_Resource_Stuff.xml' in destinations
    assert 'OriginalXML/Dinosauria/_Defs/1.7/Defs/ThingDefs_Races/Animals.xml' in destinations
    assert 'OriginalXML/Megafauna/_Defs/1.6/Defs/ThingDefs_Races/Mega.xml' in destinations
    assert 'OriginalXML/AlphaBiomes/_Defs/1.6/Defs/BiomeDefs/Custom.xml' in destinations
    assert 'OriginalXML/CEPatches/Patches/Core/Items_Resource_Stuff.xml' in destinations
    assert not any('Photonozoa' in path or 'OldDino' in path for path in destinations)

    sync.apply(result, checker_root=checker)
    second, _ = sync.discover({'mods': mods, 'rimworld_version': '1.7'},
                              steam=str(steam), github=False, checker_root=checker)
    assert {item['status'] for item in second} == {'SKIP'}
    assert len(second) == len(result)


def test_version_selection_does_not_mix_incompatible_main_versions():
    class Source:
        files = ['1.6/Defs/Animals.xml', '1.7/Defs/Animals.xml',
                 '1.6NotOdyssey/Defs/Animals.xml', 'Defs/Common.xml']
    paths = list(selected_paths(Source(), '1.7'))
    assert '1.6/Defs/Animals.xml' not in paths
    assert '1.7/Defs/Animals.xml' in paths
    assert '1.6NotOdyssey/Defs/Animals.xml' in paths
    assert 'Defs/Common.xml' in paths


def test_manual_grasslands_vegetation_is_not_checker_generated():
    root = Path(__file__).resolve().parents[2]
    manual = root / 'Zoology/DLC/Odyssey/Patches/BiomeDefs/Biomes_Grasslands_Manual.xml'
    patch = E.parse(str(manual))
    assert patch.xpath('//xpath[contains(text(), "wildPlants")]')
    registry = json.loads((root / 'checker/generation.json').read_text(encoding='utf8'))
    assert not any(item['destination'].endswith('Biomes_Grasslands_Manual.xml')
                   for item in registry['biomes'])
