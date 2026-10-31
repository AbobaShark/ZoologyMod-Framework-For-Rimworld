"""Git-versioned ownership registry. All routing decisions live here, not in generators."""
from __future__ import annotations
import json
from pathlib import Path
from core.xml_routing import safe_relative, validate_target


def _owner(destination):
    parts=Path(destination).parts
    if parts[0]=='Core':return 'Core'
    if parts[0]=='DLC' and len(parts)>1:return 'DLC:'+parts[1]
    if parts[0]=='ModPatches' and len(parts)>1:return parts[1]
    raise ValueError(f'Unsupported mod location {destination}')


def load_registry(path,mod):
    path=Path(path)
    obj=json.loads(path.read_text(encoding='utf-8-sig'))
    if obj.get('schema')!=1 or not isinstance(obj.get('groups'),list):
        raise ValueError('Invalid generation.json schema')
    if not isinstance(obj.get('outputs'),list):
        raise ValueError('generation.json must declare all generated static outputs')
    groups=obj['groups']; seen=set();animal_names={};ids=set()
    for group in groups:
        ident=group['id'];dest=group['destination']
        if ident in ids: raise ValueError(f'Duplicate group ID: {ident}')
        ids.add(ident)
        if not isinstance(group.get('defs'),list) or not group['defs']:
            raise ValueError(f'{ident}: no animal DefNames')
        if len(set(group['defs']))!=len(group['defs']):
            raise ValueError(f'{ident}: repeated animal DefName')
        if len(set(group.get('abstracts',[])))!=len(group.get('abstracts',[])):
            raise ValueError(f'{ident}: repeated abstract')
        if _owner(dest)!=group['mod']: raise ValueError(f'{ident}: mod and destination disagree')
        _verify(mod,dest,'animal_patch',group.get('requires'))
        for name in group['defs']:
            if name in animal_names:raise ValueError(f'{name}: two groups: {animal_names[name]}, {ident}')
            animal_names[name]=ident
        if dest.casefold() in seen: raise ValueError(f'Duplicate destination: {dest}')
        seen.add(dest.casefold())
    for section in ('biomes','shared_defs','outputs'):
        if not isinstance(obj.get(section),list): raise ValueError(f'Missing registry section {section}')
        for entry in obj[section]:
            dest=entry['destination']
            category='biome_patch' if section=='biomes' else entry['category']
            _verify(mod,dest,category,entry.get('requires'))
            if dest.casefold() in seen:raise ValueError(f'Duplicate destination: {dest}')
            seen.add(dest.casefold())
            if section!='outputs':continue
            ident=entry.get('id')
            if not isinstance(ident,str) or not ident or ident in ids:
                raise ValueError(f'Missing or duplicate output ID: {ident!r}')
            ids.add(ident)
            if entry.get('pipeline_category') not in ('products','bionics','life-stages','leathers','textiles'):
                raise ValueError(f'Unsupported output pipeline category: {ident}')
            if entry.get('source') not in ('installed_xml','generated'):
                raise ValueError(f'Unknown source for {ident}')
            if entry['pipeline_category']=='products' and (entry.get('source')!='installed_xml' or category!='animal_patch'):
                raise ValueError(f'Invalid reproductive output {ident}')
    # These WildBiomes markers classify animals but have no actual BiomeDef
    # and therefore must never become XML patch destinations.
    reference_only=obj.get('reference_only_biomes',[])
    if not isinstance(reference_only,list) or any(
            not isinstance(name,str) or not name or name.strip()!=name
            for name in reference_only):
        raise ValueError('reference_only_biomes must be a list of nonempty DefNames')
    marker_names={name.casefold() for name in reference_only}
    if len(marker_names)!=len(reference_only):
        raise ValueError('Duplicate reference-only biome')
    biome_names=[entry['defName'] for entry in obj['biomes']]
    if len({name.casefold() for name in biome_names})!=len(biome_names):
        raise ValueError('Duplicate registered biome DefName')
    overlap=marker_names & {name.casefold() for name in biome_names}
    if overlap:raise ValueError(f'Biomes cannot be registered and reference-only: {sorted(overlap)}')
    required_ids={'bionics.vanilla','bionics.ce','life-stages.vanilla','life-stages.ce'}
    actual={e['id'] for e in obj['outputs']}
    if not required_ids <= actual or not any(e['pipeline_category']=='products' for e in obj['outputs']):
        raise ValueError('Missing required named static outputs')
    # Material balance has a single generated owner. A handwritten leather
    # patch can silently overwrite CE values after generation, so reject it.
    if (Path(mod) / 'LoadFolders.xml').is_file():
        allowed = {e['destination'] for e in obj['outputs']
                   if e['pipeline_category'] in ('leathers', 'textiles')}
        forbidden = []
        for candidate in Path(mod).rglob('*.xml'):
            name = candidate.name.casefold()
            if 'leather' not in name and 'animaltextiles' not in name:
                continue
            relative = candidate.relative_to(mod).as_posix()
            if relative not in allowed:
                forbidden.append(relative)
        if forbidden:
            raise ValueError('Handwritten or unregistered material XML forbidden: '
                             + ', '.join(sorted(forbidden)))
    return obj


def output_by_id(registry, ident):
    try:return next(x for x in registry['outputs'] if x['id']==ident)
    except StopIteration:raise ValueError(f'Missing output {ident!r} in generation.json') from None


def outputs_for(registry, category):
    return [x for x in registry['outputs'] if x['pipeline_category']==category]


def _verify(mod,destination,category,required):
    safe_relative(destination)
    validate_target(mod,{'destination':destination,'category':category,'requires':required})


def target_entry(mod,destination,category,requires=None):
    _verify(mod,destination,category,requires)
    return {'source':'files/'+destination,'destination':destination,'category':category,'requires':requires}


def audit_routes(path, mod, *, require_static_sources=False):
    """Audit registered XML destinations against LoadFolders."""
    mod=Path(mod);registry=load_registry(path,mod)
    missing=[e['destination'] for e in registry['outputs']
             if e['source']=='installed_xml' and not (mod/e['destination']).is_file()]
    if require_static_sources and missing:
        raise ValueError('Missing installed static XML at the reviewed paths:\n'+'\n'.join(missing))
    return {
        'groups':len(registry['groups']),
        'animals':sum(len(g['defs']) for g in registry['groups']),
        'biomes':len(registry['biomes']),
        'shared_defs':len(registry['shared_defs']),
        'reproduction':len(outputs_for(registry,'products')),
        'other_static':sum(e['source']=='installed_xml' for e in registry['outputs'] if e['pipeline_category']!='products'),
        'new_ce_bionics':sum(e['source']=='generated' and e['pipeline_category']=='bionics' for e in registry['outputs']),
        'destinations':sum(len(registry[k]) for k in ('groups','biomes','shared_defs','outputs')),
        'missing_static':missing,
    }
