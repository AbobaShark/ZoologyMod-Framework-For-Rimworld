"""Manifest-based XML deployment with validated destinations and staged checksums."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import tempfile
from lxml import etree as E

STATE_NAME = '.zoology-generated-manifest.json'

def find_project_root(start):
    start = Path(start).resolve()
    for parent in (start, *start.parents):
        if (parent/'Zoology/LoadFolders.xml').is_file() and (parent/'checker').is_dir(): return parent
    raise ValueError(f'ZoologyFramework root not found above {start}')


def safe_relative(value):
    value = str(value).replace('\\', '/')
    rel = PurePosixPath(value)
    if not value or rel.is_absolute() or any(part in ('..', '.') for part in rel.parts) or ':' in value:
        raise ValueError(f'Unsafe relative path: {value!r}')
    return Path(*rel.parts)


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def load_rules(mod):
    doc = E.parse(str(mod/'LoadFolders.xml'))
    return {version.tag: [(node.text.strip('/'), node.get('IfModActive')) for node in version if node.text and node.text != '/']
            for version in doc.getroot()}



def route_dependency(mod, destination, *, preferred=None):
    """Return the conditional LoadFolders requirement for a routed XML.

    Unconditional Core routes return None. Conditional targets must have a
    dependency shared by every supported RimWorld version; otherwise fail
    rather than guessing from the path or silently emitting requires=None.
    CE has two recognized package ID aliases, so use the first stable one
    in LoadFolders unless the caller explicitly supplies a valid alias.
    """
    target = safe_relative(destination).as_posix()
    version_options = []
    for version, roots in load_rules(mod).items():
        matches = [(name, dep) for name, dep in roots if target.startswith(name + '/')]
        if not matches:
            raise ValueError(f'{version} does not load {target}')
        longest = max(len(name) for name, _ in matches)
        choices = [dep for name, dep in matches if len(name) == longest]
        if None in choices:
            if preferred is not None:
                raise ValueError(f'{target}: unconditional directory cannot declare {preferred!r}')
            version_options.append((version, [None]))
        else:
            version_options.append((version, choices))
    if preferred is not None:
        if all(any(option and option.lower() == str(preferred).lower() for option in choices)
               for _, choices in version_options):
            return preferred
        raise ValueError(f'{target}: declared dependency {preferred!r} does not match LoadFolders in every version')
    if all(choices == [None] for _, choices in version_options):
        return None
    if any(None in choices for _, choices in version_options):
        raise ValueError(f'{target}: inconsistent conditional loading across versions')
    first = version_options[0][1]
    for option in first:
        if all(any(other.lower() == option.lower() for other in choices)
               for _, choices in version_options):
            return option
    raise ValueError(f'{target}: no common LoadFolders dependency across versions')

def validate_target(mod, entry):
    target = safe_relative(entry['destination'])
    category = entry.get('category')
    if category not in ('animal_patch', 'biome_patch', 'common_defs', 'ce_patch', 'ce_defs'):
        raise ValueError(f'Unknown XML category: {category!r}')
    pathparts = target.parts
    if category.endswith('defs') and 'Defs' not in pathparts: raise ValueError(f'Def output outside Defs: {target}')
    if category.endswith('patch') and 'Patches' not in pathparts: raise ValueError(f'Patch output outside Patches: {target}')
    is_ce = len(pathparts) >= 2 and pathparts[:2] == ('ModPatches', 'CombatExtended')
    if is_ce != category.startswith('ce_'): raise ValueError(f'CE category and folder disagree: {target}')
    if target.suffix.lower() != '.xml': raise ValueError(f'Output is not XML: {target}')
    required = entry.get('requires')
    for version, roots in load_rules(mod).items():
        candidates = [(name, dep) for name, dep in roots if target.as_posix().startswith(name + '/')]
        if not candidates: raise ValueError(f'{version} does not load {target}')
        longest = max(len(name) for name, _ in candidates)
        deps = [dep for name, dep in candidates if len(name) == longest]
        if all(deps) and (not required or str(required).lower() not in {d.lower() for d in deps}):
            raise ValueError(f'{target}: LoadFolders requires one of {deps}, manifest declares {required!r}')
        if required and None in deps:
            raise ValueError(f'{target}: dependency {required!r} is not enforced by LoadFolders')
    return target


def plan(manifest_path, root):
    root = Path(root).resolve(); mod = root/'Zoology'
    manifest_path = Path(manifest_path).resolve()
    data = json.loads(manifest_path.read_text(encoding='utf-8'))
    if data.get('schema') != 1 or not isinstance(data.get('files'), list): raise ValueError('Invalid generation manifest')
    state_path = mod/STATE_NAME
    state = json.loads(state_path.read_text(encoding='utf-8')) if state_path.exists() else {'schema': 1, 'files': {}}
    if state.get('schema') != 1 or not isinstance(state.get('files'), dict): raise ValueError('Invalid ownership state')
    operations, desired = [], {}
    for entry in data['files']:
        rel = validate_target(mod, entry)
        key = rel.as_posix()
        if key.casefold() in {s.casefold() for s in desired}: raise ValueError(f'Duplicate destination: {key}')
        source = (manifest_path.parent/safe_relative(entry['source'])).resolve()
        if not source.is_relative_to(manifest_path.parent) or not source.is_file(): raise ValueError(f'Missing or unsafe source: {source}')
        xml = E.parse(str(source)).getroot()
        expected = 'Defs' if entry['category'].endswith('defs') else 'Patch'
        if xml.tag != expected: raise ValueError(f'{source}: expected {expected}, got {xml.tag}')
        target = (mod/rel).resolve()
        if not target.is_relative_to(mod.resolve()): raise ValueError(f'Output escapes mod: {target}')
        new_hash = digest(source)
        declared_hash = entry.get('sha256')
        if declared_hash is not None and (not isinstance(declared_hash,str) or declared_hash != new_hash):
            raise ValueError(f'Staged file checksum mismatch: {source}')
        desired[key] = new_hash
        if target.exists():
            # Explicitly staged, registered destinations are authoritative.
            # Preview reports replacements even if the installed XML was edited
            # manually or is absent from the previous deployment manifest.
            action = 'skip' if digest(target) == new_hash else 'replace'
        else:
            action = 'copy'
        operations.append((action, source, target, key))
    # A selected material category may now need zero patch operations. Remove
    # only its exact generated XML destination, never handcrafted/static XML.
    removals = data.get('remove', [])
    if not isinstance(removals, list) or len(set(removals)) != len(removals):
        raise ValueError('Invalid generated XML removals')
    generated_names = {
        'Items_Resource_Stuff_Leather_Generated.xml',
        'Items_Resource_Stuff_AnimalTextiles_Generated.xml',
        'Items_Resource_Stuff_Leather_Zoology_CE.xml',
    }
    for name in removals:
        rel = safe_relative(name)
        if rel.name not in generated_names or name in desired:
            raise ValueError(f'Unsafe or conflicting generated XML removal: {name}')
        target = (mod/rel).resolve()
        if not target.is_relative_to(mod.resolve()):
            raise ValueError(f'Removal escapes Zoology: {name}')
        if target.is_file():
            # The preview explicitly announces each deletion. Do not delete a
            # file the user edited after its last recorded generated deployment.
            prior_hash = state['files'].get(name)
            if prior_hash is not None and digest(target) != prior_hash:
                raise ValueError(f'Generated XML changed since last deployment: {target}')
            existing = E.parse(str(target)).getroot()
            if existing.tag != 'Patch':
                raise ValueError(f'Generated XML removal expects Patch: {target}')
            operations.append(('delete', None, target, name))
    for key, old_hash in state['files'].items():
        if key in removals: continue
        if not data.get('prune',False) or key in desired: continue
        rel = safe_relative(key); target = (mod/rel).resolve()
        if not target.is_relative_to(mod.resolve()): raise ValueError(f'Owned path escapes mod: {target}')
        if target.exists():
            if digest(target) != old_hash: raise ValueError(f'Refusing to delete modified file: {target}')
            operations.append(('delete', None, target, key))
    owned_dirs = state.get('directories', [])
    if not isinstance(owned_dirs, list): raise ValueError('Invalid owned-directory state')
    deleted = {target for action, _, target, _ in operations if action == 'delete'}
    removable = set()
    for name in sorted(owned_dirs if data.get('prune',False) else [], key=lambda s: len(safe_relative(s).parts), reverse=True):
        raw_directory = mod/safe_relative(name)
        if raw_directory.is_symlink(): raise ValueError(f'Owned directory became a symlink: {raw_directory}')
        directory = raw_directory.resolve()
        if not directory.is_relative_to(mod.resolve()) or directory == mod.resolve():
            raise ValueError(f'Owned directory escapes mod: {directory}')
        if any((mod/safe_relative(key)).resolve().is_relative_to(directory) for key in desired): continue
        if directory.is_dir() and all(child.resolve() in deleted or child.resolve() in removable for child in directory.iterdir()):
            removable.add(directory)
            operations.append(('rmdir', None, directory, name))
    return operations, desired


def deploy(manifest_path, root, dry_run=False):
    root = Path(root).resolve(); mod = root/'Zoology'
    operations, desired = plan(manifest_path, root)
    for action, source, target, _ in operations:
        print(f'[{action.upper()}] {source or ""} -> {target}')
    if dry_run: return operations
    # Keep byte-for-byte backups for rollback if any write or state update fails.
    prior = {target: target.read_bytes() if target.exists() else None for action, _, target, _ in operations if action not in ('skip', 'rmdir')}
    state_path = mod/STATE_NAME
    previous_state = json.loads(state_path.read_text(encoding='utf-8')) if state_path.exists() else {}
    owned_dirs = set(previous_state.get('directories', []))
    created_dirs = []
    try:
        for action, source, target, _key in operations:
            if action == 'skip': continue
            if action == 'delete': target.unlink(); continue
            if action == 'rmdir': target.rmdir(); owned_dirs.discard(_key); continue
            missing = []; parent = target.parent
            while not parent.exists(): missing.append(parent); parent = parent.parent
            target.parent.mkdir(parents=True, exist_ok=True); created_dirs.extend(missing)
            temporary = None
            try:
                with tempfile.NamedTemporaryFile(dir=target.parent, delete=False, prefix='.zoology-stage-') as stream:
                    temporary = Path(stream.name)
                    with source.open('rb') as inp: shutil.copyfileobj(inp, stream)
                os.replace(temporary, target)
            finally:
                if temporary is not None: temporary.unlink(missing_ok=True)
        owned_dirs.update(p.relative_to(mod).as_posix() for p in created_dirs)
        manifest_data = json.loads(Path(manifest_path).read_text(encoding='utf8'))
        removed = set(manifest_data.get('remove', []))
        retained = {} if manifest_data.get('prune',False) else {
            key: sha for key,sha in previous_state.get('files',{}).items()
            if key not in desired and key not in removed}
        state = json.dumps({'schema': 1, 'files': dict(sorted({**retained,**desired}.items())), 'directories': sorted(owned_dirs)}, ensure_ascii=False, indent=2) + '\n'
        with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=mod, delete=False, prefix='.zoology-state-') as stream:
            tmp = Path(stream.name); stream.write(state)
        try: os.replace(tmp, mod/STATE_NAME)
        finally: tmp.unlink(missing_ok=True)
    except Exception:
        for target, content in prior.items():
            if content is None: target.unlink(missing_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(content)
        for directory in sorted(set(created_dirs), key=lambda p: len(p.parts), reverse=True):
            if directory.exists() and not any(directory.iterdir()): directory.rmdir()
        raise
    return operations
