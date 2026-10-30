"""Spreadsheet-only biome markers must never generate biome XML destinations."""
import json
from pathlib import Path

import pandas as pd
import pytest

from core.generation_registry import load_registry
from core.pipeline import stage_pipeline


def _fixture(tmp_path):
    root = tmp_path / "project"
    mod = root / "Zoology"
    mod.mkdir(parents=True)
    (mod / "LoadFolders.xml").write_text(
        '<loadFolders>'
        '<v1.6><li>Core</li><li IfModActive="CETeam.CombatExtended">ModPatches/CombatExtended</li></v1.6>'
        '<v1.7><li>Core</li><li IfModActive="CETeam.CombatExtended">ModPatches/CombatExtended</li></v1.7>'
        '</loadFolders>', encoding="utf-8")
    entries = [
        ("bionics.vanilla", "Core/Patches/Bionics.xml", "animal_patch", "bionics"),
        ("bionics.ce", "ModPatches/CombatExtended/Patches/Bionics.xml", "ce_patch", "bionics"),
        ("life-stages.vanilla", "Core/Patches/LifeStages.xml", "animal_patch", "life-stages"),
        ("life-stages.ce", "ModPatches/CombatExtended/Patches/LifeStages.xml", "ce_patch", "life-stages"),
        ("products.test", "Core/Patches/Products.xml", "animal_patch", "products"),
    ]
    registry = {
        "schema": 1, "groups": [], "shared_defs": [],
        "biomes": [{"defName": "RealBiome", "destination": "Core/Patches/Biomes_RealBiome.xml"}],
        "reference_only_biomes": ["Alpine", "MammothSteppe"],
        "outputs": [{
            "id": name, "destination": destination, "category": category,
            "pipeline_category": pipeline_category,
            "source": "installed_xml" if name != "bionics.ce" else "generated",
            "requires": "CETeam.CombatExtended" if category == "ce_patch" else None,
        } for name, destination, category, pipeline_category in entries],
    }
    config = root / "generation.json"
    config.write_text(json.dumps(registry), encoding="utf-8")
    return root, config, registry


@pytest.mark.parametrize("invalid", [
    "Alpine",
    ["Alpine", "Alpine"],
    ["Alpine", "alpine"],
    ["Alpine", ""],
    ["Alpine", None],
    ["RealBiome"],
])
def test_invalid_reference_only_registry(tmp_path, invalid):
    root, config, registry = _fixture(tmp_path)
    registry["reference_only_biomes"] = invalid
    config.write_text(json.dumps(registry), encoding="utf-8")
    with pytest.raises(ValueError):
        load_registry(config, root / "Zoology")


@pytest.mark.parametrize("names,expected_error", [
    (["RealBiome", "Alpine", "MammothSteppe"], None),
    (["RealBiome", "Alpine", "UnknownBiome"], "Unregistered biome UnknownBiome"),
])
def test_reference_only_biomes_are_not_staged(tmp_path, monkeypatch, names, expected_error):
    root, config, registry = _fixture(tmp_path)
    assert len(load_registry(config, root / "Zoology")["biomes"]) == 1

    class FakeGenerator:
        def __init__(self, *args, **kwargs):
            self.vanilla_df = pd.DataFrame()

        def generate_biome_patch_files(self, output_dir):
            paths = []
            for name in names:
                path = Path(output_dir) / f"Biomes_{name}.xml"
                path.write_text(
                    '<Patch><Operation Class="PatchOperationAdd">'
                    f'<xpath>/Defs/BiomeDef[defName="{name}"]/wildAnimals</xpath>'
                    '<value/></Operation></Patch>', encoding="utf-8")
                paths.append(path)
            return paths

    monkeypatch.setattr("core.pipeline.PatchGenerator", FakeGenerator)
    stage = tmp_path / "stage"
    kwargs = {
        "root": root, "registry_path": config, "categories": ("biomes",),
        "source": "dummy", "optimize": False, "output_dir": stage,
    }
    if expected_error:
        with pytest.raises(ValueError, match=expected_error):
            stage_pipeline(**kwargs)
        assert not stage.exists()
    else:
        manifest = stage_pipeline(**kwargs)
        entries = json.loads(manifest.read_text(encoding="utf-8"))["files"]
        assert [entry["destination"] for entry in entries] == [
            "Core/Patches/Biomes_RealBiome.xml"
        ]
        assert not any("Alpine" in entry["destination"] or "MammothSteppe" in entry["destination"]
                       for entry in entries)
