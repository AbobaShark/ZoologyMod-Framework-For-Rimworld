"""Independent group-based patch generation and manifest staging.

No generated XML is used as an input for animal patches. All destinations
come from the Git-versioned generation registry or explicit static file paths.
"""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
from lxml import etree as E
from core.patch_engine import PatchGenerator
from core.generation_registry import load_registry,target_entry,output_by_id,outputs_for
from core.xml_routing import digest,find_project_root,route_dependency
from core.static_stats import read_table,update_bionics,update_life_stages,SHEET_REQUIRED
from core.production_stats import render_reproduction
from core.checker_config import ROOT
from core.patch_postprocess import PatchPostprocessor
from core.leather_generation import render_leather_outputs
from core.textile_generation import render_textile_outputs

STAGE=ROOT/'output/staged'
CATEGORIES=('animals','venom','biomes','products','bionics','life-stages','leathers','textiles')


def _guard_leather_assignments(payload):
    """Leave existing race leather unchanged when a referenced material is absent.

    This removes any need to define extra or fallback leather ThingDefs for
    Odyssey, VAE Waste or other optional material providers.
    """
    root = E.fromstring(payload)
    if root.tag != 'Patch':
        return payload
    changed = False
    for operation in list(root):
        if not isinstance(operation.tag, str) or operation.tag != 'Operation':
            continue
        xpaths = operation.xpath('./xpath/text() | ./match/xpath/text() | ./nomatch/xpath/text()')
        if not any('/race/leatherDef' in xpath for xpath in xpaths):
            continue
        materials = {str(value).strip() for value in operation.xpath(
            './value//leatherDef/text() | ./match/value//leatherDef/text() | '
            './nomatch/value//leatherDef/text()')}
        if not materials:
            continue
        if len(materials) != 1:
            raise ValueError(f'Conflicting leatherDef assignment: {sorted(materials)}')
        name = materials.pop()
        if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_]*', name):
            raise ValueError(f'Invalid assigned leatherDef: {name!r}')
        idx = root.index(operation)
        guard = E.Element('Operation', Class='PatchOperationConditional')
        E.SubElement(guard, 'xpath').text = f'/Defs/ThingDef[defName="{name}"]'
        seq = E.SubElement(E.SubElement(guard, 'match', Class='PatchOperationSequence'), 'operations')
        root.remove(operation)
        operation.tag = 'li'
        seq.append(operation)
        # The neighboring useLeatherFrom removal is part of this reassignment.
        if idx < len(root):
            following = root[idx]
            if (isinstance(following.tag, str) and following.tag == 'Operation'
                and any('/race/useLeatherFrom' in xpath for xpath in
                        following.xpath('./xpath/text() | ./match/xpath/text()'))):
                root.remove(following)
                following.tag = 'li'
                seq.append(following)
        root.insert(idx, guard)
        changed = True
    return E.tostring(root, xml_declaration=True, encoding='utf-8',
                      pretty_print=True) if changed else payload


def _data_as_entry(mod,rel,category,requires=None):
    return target_entry(mod,rel,category,requires)


def _check_selected(registry, selected):
    if selected is None:return registry['groups']
    by_id={x['id']:x for x in registry['groups']}
    if any(n not in by_id for n in selected):raise ValueError(f'Unknown group ID(s): {sorted(set(selected)-set(by_id))}')
    return [by_id[n] for n in selected]


def stage_pipeline(source, *, root=None, registry_path=None, sheet_sources=None, categories=('animals',),
                   groups=None, ce=True, optimize=True, original_xml=None, original_patches=None,
                   venom_source=None, output_dir=None, preserve_runtime_preconditions=True):
    """Atomically publish staging output; never modifies Zoology. Selection keeps other staged entries."""
    root=Path(root or find_project_root(ROOT)).resolve()
    mod=root/'Zoology'
    if not (mod/'LoadFolders.xml').is_file():raise ValueError(f'Not a Zoology project root: {root}')
    registry=load_registry(registry_path or ROOT/'generation.json',mod)
    registered_outputs={entry['destination']:entry for entry in registry['outputs']}
    animal_group_destinations={group['destination'] for group in registry['groups']}
    declared_routes={entry['destination']:(section,entry) for section in ('groups','biomes','shared_defs','outputs') for entry in registry[section]}
    cats=set(categories)
    if cats-set(CATEGORIES):raise ValueError(f'Unknown categories: {cats-set(CATEGORIES)}')
    sheets=dict(sheet_sources or {})
    if not (source or any(sheets.values())):raise ValueError('Specify a workbook or an individual sheet source')
    if cats & {'animals','venom','biomes','products'} and not (source or sheets.get('Animals')):
        raise ValueError('Selected categories require an Animals table or a multisheet workbook')
    if 'venom' in cats and not (venom_source or sheets.get('Venom profiles') or (source and str(source).lower().endswith(('.xlsx','.xlsm','.xls')) or (source and str(source).startswith(('https://','gsheet:'))))):
        raise ValueError('Venom generation requires Venom profiles sheet or explicit TSV')
    # Never create or guess a destination: registry provides exact versioned paths.
    stage=Path(output_dir or STAGE).resolve()
    if stage.is_relative_to(mod.resolve()):raise ValueError('Staging must be outside Zoology')
    stage.parent.mkdir(parents=True,exist_ok=True)
    oldmanifest=stage/'generation-manifest.json'
    old=json.loads(oldmanifest.read_text(encoding='utf8')) if oldmanifest.exists() else {'schema':1,'files':[]}
    if old.get('schema')!=1:raise ValueError('Invalid prior manifest')
    # Verify all retained staged files before generating or pruning anything.
    for e in old['files']:
        if e['destination'] not in declared_routes:
            if e.get('pipeline_category') in cats:
                continue  # Retired category output is not retained in the next stage.
            raise ValueError(f'Staged destination not present in generation.json: {e["destination"]}. Regenerate its category.')
        p=stage/e['source']
        if not p.is_file() or digest(p)!=e.get('sha256'):raise ValueError(f'Staged file changed externally: {p}')
    entry_by_dest={}
    remove_generated=set()
    postprocessor=PatchPostprocessor(original_xml=original_xml or ROOT/'OriginalXML',
        original_patches=(original_patches or ROOT/'OriginalXML/CEPatches') if ce else None,
        preserve_guards=preserve_runtime_preconditions) if optimize else None
    with tempfile.TemporaryDirectory(prefix='.zoology-stage-',dir=stage.parent) as temp:
        temp=Path(temp)
        def emit(dest,category,data,requires=None):
            if isinstance(data,Path):data=data.read_bytes()
            if not isinstance(data,bytes):raise TypeError('Expected generated XML bytes')
            # Animal group patches are already processed by PatchGenerator.
            # Do not run the expensive semantic optimizer on them twice.
            if dest not in declared_routes:
                raise ValueError(f'Undeclared destination: {dest}; add to generation.json before generating')
            declared_section,declared=declared_routes[dest]
            expected_category = 'biome_patch' if declared_section=='biomes' else ('animal_patch' if declared_section=='groups' else declared['category'])
            if expected_category!=category or declared.get('requires')!=requires:
                raise ValueError(f'Output category or requirement differs from generation.json: {dest}')
            if postprocessor is not None and category.endswith('patch') and dest not in animal_group_destinations:
                if postprocessor.index is None and generator is not None:
                    postprocessor.index=getattr(generator,'_original_xml_index',None)
                data=postprocessor.process(data,destination=dest)
            entry=_data_as_entry(mod,dest,category,requires)
            if dest in animal_group_destinations:
                data=_guard_leather_assignments(data)
            xml=E.fromstring(data)
            if xml.tag!=('Defs' if category.endswith('defs') else 'Patch'):
                raise ValueError(f'{dest}: incorrect XML root {xml.tag}')
            if dest.casefold() in {s.casefold() for s in entry_by_dest}:
                raise ValueError(f'Duplicate generated destination: {dest}')
            entry['sha256']=hashlib.sha256(data).hexdigest()
            destfile=temp/entry['source'];destfile.parent.mkdir(parents=True,exist_ok=True)
            destfile.write_bytes(data);entry_by_dest[dest]=entry

        # Preserve categories not selected; remove all previously selected category entries.
        active_groups={g['destination'] for g in _check_selected(registry,groups)} if 'animals' in cats else set()
        selection_dest={e['destination'] for e in registry['shared_defs']} if 'venom' in cats else set()
        selection_dest |= {x['destination'] for x in registry['biomes']} if 'biomes' in cats else set()
        for e in old['files']:
            if e['destination'] not in declared_routes:
                continue
            c=e.get('pipeline_category')
            selected=(c in cats and (c!='animals' or groups is None or e['destination'] in active_groups))
            if selected:continue
            dest=e['destination']
            # Earlier Checker builds could stage conditional files with
            # requires=None. Revalidate each retained entry against the
            # current LoadFolders rules and fill only a missing requirement.
            e=dict(e)
            expected_dependency=route_dependency(mod,dest)
            if e.get('requires') is None:
                e['requires']=expected_dependency
            elif e['requires'] != expected_dependency:
                route_dependency(mod,dest,preferred=e['requires'])
            if dest.casefold() in {s.casefold() for s in entry_by_dest}:raise ValueError(f'Duplicate prior destination: {dest}')
            out=temp/e['source'];out.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(stage/e['source'],out);entry_by_dest[dest]=e

        generator=None
        if cats & {'animals','venom','biomes'}:
            generator=PatchGenerator(source, None, [],
                original_xml_dir=original_xml or ROOT/'OriginalXML',
                original_patches_dir=original_patches or ROOT/'OriginalXML/CEPatches',
                preserve_runtime_preconditions=preserve_runtime_preconditions,
                venom_profile_source=venom_source or sheets.get('Venom profiles'),
                source_map=sheets,enable_ce=ce,optimize=optimize,
                require_venom=('venom' in cats),mod_root=mod)
            # Enforce complete data for generated groups, including CE body shapes.
            known={generator._extract_def_name_from_row(row) for _,row in generator.vanilla_df.iterrows()}
            if 'animals' in cats:
                for group in _check_selected(registry,groups):
                    missing=set(group['defs'])-known
                    if missing:raise ValueError(f'{group["id"]}: missing in Animals: {sorted(missing)}')
                    path=temp/'work'/group['id'];path.parent.mkdir(parents=True,exist_ok=True)
                    if not generator.generate_patch_from_definitions(str(path),group['defs'],patched_abstracts=group['abstracts'],group_overrides=group.get('overrides')):
                        raise RuntimeError(f'Generation failed for {group["id"]}')
                    e=group
                    emit(e['destination'],'animal_patch',path.read_bytes(),e.get('requires'))
                    entry_by_dest[e['destination']]['pipeline_category']='animals'
            if 'venom' in cats:
                if generator.venom_catalog is None:raise ValueError('Validated Venom profiles not available')
                defs=generator.venom_catalog.render_item_defs()
                mapping={Path(e['destination']).name:e for e in registry['shared_defs']}
                if set(defs)!=set(mapping):raise ValueError(f'Venom Def output does not match registry: {set(defs)^set(mapping)}')
                for filename,element in defs.items():
                    e=mapping[filename]
                    data=E.tostring(element,pretty_print=True,encoding='utf-8',xml_declaration=True)
                    emit(e['destination'],e['category'],data,e.get('requires'))
                    entry_by_dest[e['destination']]['pipeline_category']='venom'
            if 'biomes' in cats:
                by_biome={e['defName']:e for e in registry['biomes']}
                reference_only=set(registry.get('reference_only_biomes',[]))
                bdir=temp/'work/biomes';bdir.mkdir(parents=True,exist_ok=True)
                rendered=generator.generate_biome_patch_files(str(bdir))
                found=set()
                for path in rendered:
                    payload=Path(path).read_bytes();tree=E.fromstring(payload)
                    names={n for xpath in tree.xpath('//xpath/text()') for n in re.findall(r'BiomeDef\[defName=["\']([^"\']+)["\']',xpath)}
                    if len(names)!=1:raise ValueError(f'Cannot determine biome DefName: {path}')
                    name=names.pop()
                    # Spreadsheet-only markers establish wild status without a
                    # corresponding BiomeDef; never generate XML for them.
                    if name in reference_only:continue
                    if name not in by_biome:raise ValueError(f'Unregistered biome {name}: add destination to generation.json')
                    e=by_biome[name];found.add(name)
                    if '/GeneratedBiomes/' in e['destination']:
                        # Preserve the original managed-pipeline optional-biome guard.
                        # Unknown biome DefNames must not produce missing-target errors.
                        tree=E.fromstring(payload)
                        old_operations=list(tree)
                        tree.clear()
                        guard=E.SubElement(tree,'Operation',Class='PatchOperationConditional')
                        E.SubElement(guard,'xpath').text=f'/Defs/BiomeDef[defName="{name}"]'
                        seq=E.SubElement(E.SubElement(guard,'match',Class='PatchOperationSequence'),'operations')
                        for op in old_operations:
                            if not isinstance(op.tag,str):continue
                            op.tag='li';seq.append(op)
                        payload=E.tostring(tree,pretty_print=True,encoding='utf-8',xml_declaration=True)
                    emit(e['destination'],'biome_patch',payload,e.get('requires'))
                    entry_by_dest[e['destination']]['pipeline_category']='biomes'
                if set(by_biome)!=found:raise ValueError(f'Biomes missing from source: {sorted(set(by_biome)-found)}')

        if 'products' in cats:
            product_entries=outputs_for(registry,'products')
            outputs, report=render_reproduction(source,root,egg_milk_source=sheets.get('EggMilkWool'),
                                                 animals_source=sheets.get('Animals'),
                                                 animals_frame=generator.vanilla_df if generator is not None else None,
                                                 destinations=[entry['destination'] for entry in product_entries])
            # Current mixed handcrafted XML remains the baseline, not generated_patches.
            unknown={d for d,_ in outputs}-{e['destination'] for e in product_entries}
            if unknown:raise ValueError(f'Unregistered products path: {unknown}')
            missing=[item['target'] for item in report if item['status']=='missing']
            if missing:raise ValueError('Missing required reproductive XML in Zoology: '+', '.join(missing))
            for dest,payload in outputs:
                e=registered_outputs[dest]
                emit(dest,e['category'],payload,e.get('requires'))
                entry_by_dest[dest]['pipeline_category']='products'
            (temp/'products-report.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf8')

        if 'leathers' in cats:
            entries=outputs_for(registry,'leathers')
            if not entries:raise ValueError('No leathers outputs registered in generation.json')
            leather_source=sheets.get('Leathers') or source
            rendered, report=render_leather_outputs(leather_source,mod,entries,ce=ce,original_xml=original_xml or ROOT/'OriginalXML',original_patches=original_patches or ROOT/'OriginalXML/CEPatches',animals_source=sheets.get('Animals') or (source if source != leather_source else None))
            from core.leather_overrides import GROUP_ROUTES as LEATHER_ROUTES
            from core.leather_generation import CE_DEST as LEATHER_CE_DEST
            for dest,payload in rendered:
                if dest in set(LEATHER_ROUTES.values()) | {LEATHER_CE_DEST} and not len(E.fromstring(payload)):
                    remove_generated.add(dest)
                    continue
                entry=registered_outputs[dest]
                emit(dest,entry['category'],payload,entry.get('requires'))
                entry_by_dest[dest]['pipeline_category']='leathers'
            (temp/'leathers-report.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf8')

        if 'textiles' in cats:
            entries=outputs_for(registry,'textiles')
            if not entries:raise ValueError('No textile output routes registered')
            textile_source=sheets.get('Animal textiles') or source
            rendered,report=render_textile_outputs(textile_source,entries,ce=ce,
                original_xml=original_xml or ROOT/'OriginalXML',
                original_patches=original_patches or ROOT/'OriginalXML/CEPatches')
            for dest,payload in rendered:
                if not len(E.fromstring(payload)):
                    remove_generated.add(dest)
                    continue
                entry=registered_outputs[dest]
                emit(dest,entry['category'],payload,entry.get('requires'))
                entry_by_dest[dest]['pipeline_category']='textiles'
            (temp/'textiles-report.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf8')

        if cats & {'bionics','life-stages'}:
            static_frames={}
            def sheet_frame(tab):
                if tab in static_frames:
                    return static_frames[tab]
                selected=sheets.get(tab) or source
                if not selected:raise ValueError(f'{tab}: no spreadsheet source selected')
                if Path(str(selected)).suffix.lower()=='.tsv':
                    import pandas as pd
                    frame=pd.read_csv(selected,sep='\t',dtype=str,keep_default_na=False)
                    missing=set(SHEET_REQUIRED.get(tab,()))-set(frame.columns)
                    if missing:raise ValueError(f'{tab}: missing required columns: {sorted(missing)}')
                else:
                    frame=read_table(selected,tab)
                static_frames[tab]=frame
                return frame

            def static(tab,dest,kind,updater):
                f=mod/dest
                if not f.is_file():raise ValueError(f'Missing installed static source {f}')
                table=sheet_frame(tab)
                tree=E.parse(str(f),E.XMLParser(remove_blank_text=False))
                changes,warnings=updater(tree,table)
                data=E.tostring(tree,encoding='utf-8',xml_declaration=True,pretty_print=False)
                emit(dest,kind,data,route_dependency(mod,dest))
                entry_by_dest[dest]['pipeline_category']='bionics' if tab=='Bionics' else 'life-stages'
                return {'destination':dest,'changes':changes,'warnings':warnings}
            results=[]
            if 'bionics' in cats:
                e=output_by_id(registry,'bionics.vanilla')
                results.append(static('Bionics',e['destination'],e['category'],update_bionics))
                if ce:
                    from core.ce_bionics import render_ce_bionics
                    t=sheet_frame('Bionics')
                    e=output_by_id(registry,'bionics.ce')
                    dest=e['destination']
                    payload=render_ce_bionics(t,mod/output_by_id(registry,'bionics.vanilla')['destination'])
                    emit(dest,e['category'],payload,e.get('requires'))
                    entry_by_dest[dest]['pipeline_category']='bionics'
            if 'life-stages' in cats:
                e=output_by_id(registry,'life-stages.vanilla')
                results.append(static('LifeStages',e['destination'],e['category'],lambda tree,table:update_life_stages(tree,table,False)))
                if ce:
                    e=output_by_id(registry,'life-stages.ce')
                    results.append(static('LifeStages',e['destination'],e['category'],lambda tree,table:update_life_stages(tree,table,True)))
            (temp/'static-report.json').write_text(json.dumps(results,indent=2,ensure_ascii=False)+'\n',encoding='utf8')
        if postprocessor is not None:
            (temp/'optimization-report.json').write_text(json.dumps(postprocessor.stats,indent=2,ensure_ascii=False)+'\n',encoding='utf8')
        # A full run should be self contained, but partial generation never purges unrelated outputs.
        data={'schema':1,'prune':False,'files':sorted(entry_by_dest.values(),key=lambda x:x['destination']),
              'remove':sorted(remove_generated)}
        (temp/'generation-manifest.json').write_text(json.dumps(data,indent=2,ensure_ascii=False)+'\n',encoding='utf8')
        # Final route/collision/integrity checks without requiring generated files inside Zoology.
        # Validate destination routing and staged checksums before publishing.
        # Installed XML is replaceable without historical ownership hashes.
        from core.xml_routing import validate_target
        for entry in data['files']:
            validate_target(mod,entry)
            payload=temp/entry['source']
            if digest(payload)!=entry['sha256']:raise ValueError(f'Corrupt staged file: {payload}')
        if (temp/'work').exists():shutil.rmtree(temp/'work')
        # Publish via rename, preserving last working stage on error.
        backup=stage.parent/(stage.name+'.previous')
        if backup.exists():shutil.rmtree(backup)
        if stage.exists():stage.rename(backup)
        try:shutil.copytree(temp,stage)
        except Exception:
            if stage.exists():shutil.rmtree(stage)
            if backup.exists():backup.rename(stage)
            raise
        if backup.exists():shutil.rmtree(backup)
    return stage/'generation-manifest.json'
