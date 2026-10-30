"""Bionics-only staging must work with a single TSV, without Animals or workbook."""
import json
import tempfile
from pathlib import Path
from lxml import etree as E
from core.pipeline import stage_pipeline
from core.ce_bionics import render_ce_bionics
from test_ce_bionics_generation import data, source_file


def test_single_bionics_tsv_stages_vanilla_and_ce_without_main_source():
    with tempfile.TemporaryDirectory() as directory:
        root=Path(directory)
        mod=root/'Zoology';mod.mkdir()
        (root/'checker').mkdir()
        load=E.Element('loadFolders');v=E.SubElement(load,'v1.7')
        E.SubElement(v,'li').text='Core'
        E.SubElement(v,'li',IfModActive='CETeam.CombatExtended').text='ModPatches/CombatExtended'
        (mod/'LoadFolders.xml').write_bytes(E.tostring(load))
        defs=mod/'Core/Defs/HediffDefs/BionicAnimalVariants.xml'
        defs.parent.mkdir(parents=True)
        defs.write_bytes(source_file(directory).read_bytes())
        tab=data();tab['Attack interval']='1';tab['Damage']='11'
        tsv=root/'Bionics.tsv';tab.to_csv(tsv,sep='\t',index=False)
        cfg=root/'groups.json';cfg.write_text(json.dumps({'schema':1,'groups':[],'biomes':[],'shared_defs':[]}),encoding='utf8')
        manifest=stage_pipeline('',root=root,registry_path=cfg,sheet_sources={'Bionics':str(tsv)},categories=('bionics',),ce=True,output_dir=root/'output')
        entries=json.loads(manifest.read_text(encoding='utf8'))['files']
        assert len(entries)==2
        assert {e['pipeline_category'] for e in entries}=={'bionics'}
        vpath=manifest.parent/'files/Core/Defs/HediffDefs/BionicAnimalVariants.xml'
        cpath=manifest.parent/'files/ModPatches/CombatExtended/Patches/HediffDefs/BionicAnimalVariants_CE.xml'
        assert len(E.parse(str(vpath)).xpath('.//power[text()="11"]'))==30
        assert len(E.parse(str(cpath)).xpath('.//li[@Class="CombatExtended.ToolCE"]'))==30
