"""Portable shared defaults + ignored, per-machine Checker settings.

Only the two *_config.json defaults are versioned. GUI changes are written
atomically to checker.local.json and never mutate versioned defaults.
"""
from __future__ import annotations
import json
import os
from pathlib import Path
import tempfile

ROOT = Path(__file__).resolve().parents[1]
LOCAL_NAME = 'checker.local.json'
SECTIONS = {'patch_generator': 'rimworld_patch_generator_config.json',
            'xml_generator': 'rimworld_xml_generator_config.json'}
PATH_FIELDS = {'patch_generator': ('vanilla_tsv','ce_tsv','venom_profiles_source','egg_milk_tsv','generation_config','original_xml_dir','ce_patches_dir'),
               'xml_generator': ('xlsx','out_dir','existing_xml','game_root_dir','core_thingdefs_dir')}


def _read_json(path):
    if not path.exists(): return {}
    try:
        data = json.loads(path.read_text(encoding='utf-8-sig'))
    except (ValueError, UnicodeError) as e:
        raise ValueError(f'Invalid configuration {path}: {e}') from e
    if not isinstance(data,dict): raise ValueError(f'{path}: expected JSON object')
    return data


def _is_external(value):
    if not isinstance(value,str): return False
    return (value.startswith(('http://','https://','gsheet:')) or
            (len(value)>=20 and all(c.isalnum() or c in '_-' for c in value)) or
            value.startswith('~'))


def resolve_path(value):
    """Resolve portable checker-relative paths; do not touch Sheets URLs/IDs."""
    if not isinstance(value,str) or not value.strip() or _is_external(value): return value
    path = Path(os.path.expanduser(value))
    if path.is_absolute(): return str(path)
    # For legacy Windows absolute paths used on POSIX, reject instead of constructing
    # an implausible relative path.
    if len(value)>1 and value[1]==':': raise ValueError(f'Windows path on this system: {value}')
    return str((ROOT/path).resolve())


def _portable(value):
    if not isinstance(value,str) or not value or _is_external(value): return value
    path=Path(value).expanduser()
    if not path.is_absolute(): return value.replace('\\','/')
    try: return path.resolve().relative_to(ROOT).as_posix()
    except ValueError: return str(path)


def defaults(section):
    if section not in SECTIONS: raise ValueError(f'Unknown config section: {section}')
    return _read_json(ROOT/SECTIONS[section])


def load(section):
    base=defaults(section)
    local=_read_json(ROOT/LOCAL_NAME)
    overrides=local.get(section,{})
    if not isinstance(overrides,dict): raise ValueError(f'{LOCAL_NAME}: {section} must be an object')
    base.update(overrides)
    for field in PATH_FIELDS[section]:
        if field in base: base[field]=resolve_path(base[field])
    if section=='patch_generator':
        xmls=base.get('xmls',[])
        if not isinstance(xmls,list): raise ValueError('xmls must be a list')
        base['xmls']=[resolve_path(p) for p in xmls]
        base['sheet_sources']={k:resolve_path(v) for k,v in base.get('sheet_sources',{}).items()}
    return base


def save(section,config):
    if section not in SECTIONS: raise ValueError(f'Unknown config section: {section}')
    if not isinstance(config,dict): raise TypeError('config must be a dict')
    current=_read_json(ROOT/LOCAL_NAME)
    base=defaults(section)
    values=dict(config)
    for field in PATH_FIELDS[section]:
        if field in values: values[field]=_portable(values[field])
    if section=='patch_generator' and 'xmls' in values:
        values['xmls']=[_portable(p) for p in values['xmls']]
    if section=='patch_generator' and 'sheet_sources' in values:
        values['sheet_sources']={k:_portable(v) for k,v in values['sheet_sources'].items()}
    # Only differing personal settings go to local config.
    section_values={k:v for k,v in values.items() if v!=base.get(k)}
    if section_values: current[section]=section_values
    else: current.pop(section,None)
    with tempfile.NamedTemporaryFile('w',encoding='utf8',dir=ROOT,prefix='.checker-config-',
                                     suffix='.tmp',delete=False) as f:
        temp=Path(f.name)
        json.dump(current,f,ensure_ascii=False,indent=2)
        f.write('\n')
    try: os.replace(temp,ROOT/LOCAL_NAME)
    finally: temp.unlink(missing_ok=True)
