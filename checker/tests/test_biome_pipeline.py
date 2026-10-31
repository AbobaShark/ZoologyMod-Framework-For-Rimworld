import json
from pathlib import Path
from core.generation_registry import load_registry
from core.xml_routing import plan,deploy


def _mod(tmp_path):
    mod=tmp_path/'Zoology';mod.mkdir()
    (mod/'LoadFolders.xml').write_text('<loadFolders><v1.7><li>Core</li><li IfModActive="CETeam.CombatExtended">ModPatches/CombatExtended</li></v1.7></loadFolders>',encoding='utf8')
    return mod


def test_registry_routes_biomes_to_exact_versioned_path(tmp_path):
    mod=_mod(tmp_path)
    reg={'schema':1,'groups':[{'id':'test','mod':'Core','destination':'Core/Patches/Core/ThingDefs_Races/Test.xml','defs':['TestAnimal'],'abstracts':[]}],
         'biomes':[{'defName':'TestBiome','destination':'Core/Patches/Core/BiomeDefs/Biomes_Test.xml'}],
         'shared_defs':[],
         'outputs':[
             {'id': name, 'destination': destination, 'category': category,
              'pipeline_category': pipeline, 'source': source,
              'requires': 'CETeam.CombatExtended' if category == 'ce_patch' else None}
             for name, destination, category, pipeline, source in (
                 ('products.test', 'Core/Patches/Core/Misc/Products.xml', 'animal_patch', 'products', 'installed_xml'),
                 ('bionics.vanilla', 'Core/Defs/HediffDefs/BionicAnimalVariants.xml', 'common_defs', 'bionics', 'installed_xml'),
                 ('bionics.ce', 'ModPatches/CombatExtended/Patches/HediffDefs/BionicAnimalVariants_CE.xml', 'ce_patch', 'bionics', 'generated'),
                 ('life-stages.vanilla', 'Core/Patches/Core/LifeStageDefs/LifeStages.xml', 'animal_patch', 'life-stages', 'installed_xml'),
                 ('life-stages.ce', 'ModPatches/CombatExtended/Patches/LifeStageDefs/LifeStages_CE.xml', 'ce_patch', 'life-stages', 'installed_xml'),
             )]}
    path=tmp_path/'generation.json';path.write_text(json.dumps(reg))
    obj=load_registry(path,mod)
    assert obj['biomes'][0]['destination']=='Core/Patches/Core/BiomeDefs/Biomes_Test.xml'
    reg['biomes'][0]['destination']='ModPatches/CombatExtended/Patches/Biomes_Test.xml'
    path.write_text(json.dumps(reg))
    import pytest
    with pytest.raises(ValueError):load_registry(path,mod)


def test_partial_deployment_does_not_delete_other_owned_files(tmp_path):
    mod=_mod(tmp_path)
    a=mod/'Core/Patches/Core/ThingDefs_Races/Previously.xml';a.parent.mkdir(parents=True);a.write_bytes(b'<Patch/>')
    import hashlib
    sha=hashlib.sha256(a.read_bytes()).hexdigest()
    (mod/'.zoology-generated-manifest.json').write_text(json.dumps({'schema':1,'files':{'Core/Patches/Core/ThingDefs_Races/Previously.xml':sha}}))
    stage=tmp_path/'stage';stage.mkdir();(stage/'new.xml').write_bytes(b'<Patch/>')
    manifest=stage/'generation-manifest.json'
    manifest.write_text(json.dumps({'schema':1,'prune':False,'files':[{'source':'new.xml','destination':'Core/Patches/Core/ThingDefs_Races/New.xml','category':'animal_patch'}]}))
    ops,_=plan(manifest,tmp_path)
    assert not any(action=='delete' for action,*_ in ops)
    deploy(manifest,tmp_path)
    assert a.read_bytes()==b'<Patch/>'
    state=json.loads((mod/'.zoology-generated-manifest.json').read_text())
    assert 'Core/Patches/Core/ThingDefs_Races/Previously.xml' in state['files']
