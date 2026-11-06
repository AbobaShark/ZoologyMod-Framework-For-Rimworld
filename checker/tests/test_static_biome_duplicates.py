"""Guard against duplicate hand-authored pack animal patches."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BIOMES = ROOT / "Zoology/ModPatches/VanillaAnimalsExpanded/Patches/BiomeDefs"


def test_checker_managed_pack_animal_entries_have_no_manual_duplicates():
    # AnimalStats.Animals: AEXP_Camel PackAnimal=true in its listed biomes;
    # AEXP_IndianElephant PackAnimal=true in both tropical biomes.
    for biome in ("AridShrubland", "Desert", "ExtremeDesert", "LavaField",
                  "TropicalRainforest", "TropicalSwamp"):
        assert not (BIOMES / f"Biomes_{biome}_VAE.xml").exists()


def test_non_checker_biome_overrides_are_preserved():
    for biome in ("Grasslands", "Glowforest", "Tundra"):
        assert (BIOMES / f"Biomes_{biome}_VAE.xml").is_file()
    assert (ROOT / "Zoology/DLC/Odyssey/Patches/BiomeDefs/"
            "Biomes_Grasslands_Manual.xml").is_file()


def test_removed_static_baby_and_meat_overrides_stay_absent():
    assert not (ROOT / 'Zoology/Core/Patches/Core/LifeStageDefs/BabyLifeStages.xml').exists()
    assert not (ROOT / 'Zoology/ModPatches/VanillaAnimalsExpanded/Patches/LifeStageDefs/BabyLifeStages_VAE.xml').exists()
    assert not (ROOT / 'Zoology/ModPatches/AlphaAnimals/Patches/ThingDefs_Races/ZZ_OvergrownColossusMeat.xml').exists()


def test_tiny_babies_are_owned_by_the_correct_generated_group():
    import json
    from lxml import etree as E
    config = json.loads((ROOT / 'checker/generation.json').read_text(encoding='utf8'))
    names = config['baby_tiny_defs']
    assert len(names) == len(set(names)) == 15
    by_name = {name: group for group in config['groups'] for name in group['defs']}
    for name in names:
        group = by_name[name]
        tree = E.parse(str(ROOT / 'Zoology' / group['destination']))
        expected = f'/Defs/ThingDef[defName="{name}"]/race/lifeStageAges/li/def[text()="AnimalBaby"]'
        ops = tree.xpath('./Operation[xpath=$expr]',expr=expected)
        assert len(ops) == 1, (name, group['destination'])
        assert ops[0].find('match').get('Class') == 'PatchOperationReplace'
        assert ops[0].find('match/xpath').text == expected
        assert ops[0].find('match/value/def').text == 'AnimalBabyTiny'
