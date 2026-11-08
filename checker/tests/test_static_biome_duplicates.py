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
