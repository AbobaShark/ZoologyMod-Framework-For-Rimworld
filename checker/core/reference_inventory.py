"""Source-first, reproducible Reference Sync inventory.

Import relevant Defs without requiring a pre-existing OriginalXML tree.
Each snapshot preserves the upstream relative path under its source owner.
"""
from __future__ import annotations

from pathlib import PurePosixPath
import re
import subprocess
import xml.etree.ElementTree as ET

_DEFS = {'ThingDef', 'PawnKindDef', 'BiomeDef', 'LifeStageDef', 'HediffDef'}
_EXCLUDE = {'photonozoa', 'kaliska'}
_CE_HINT = re.compile(
    r'leather|wool|silk|stuff|resource|races?[_/]|animal|biome|egg|hatch|milk|shear|pawnkind',
    re.I,
)
_CE_XPATH = re.compile(r'ThingDef|PawnKindDef|BiomeDef|LifeStageDef|LeatherBase|WoolBase')
_VERSION = re.compile(r'^v?(\d+)\.(\d+)


def version_tuple(version):
    """Compare arbitrary RimWorld major.minor versions numerically."""
    if version is None or str(version).strip().lower() in ('', 'auto'):
        return None
    match = re.match(r'^v?(\d+)\.(\d+)(?:\.\d+)?$', str(version).strip(), re.I)
    if not match:
        raise ValueError(f'Invalid RimWorld version: {version!r}')
    return tuple(map(int, match.groups()))


def source_path_version(path):
    for part in PurePosixPath(path).parts[:-1]:
        match = _VERSION.fullmatch(part)
        if match:
            return tuple(map(int, match.groups()))
    return None


def _logical_path(path):
    """A versioned file replaces the same root/Common relative path."""
    parts = [part for part in PurePosixPath(path).parts if
             not _VERSION.fullmatch(part) and part.casefold() != 'common']
    return '/'.join(parts).casefold()


def selected_paths(source, version=None):
    """Pick one highest compatible version per source, plus shared root/Common XML.

    RimWorld loads the highest compatible version directory, not every version.
    If the game version is unknown, use the newest version supplied by the mod.
    """
    paths = sorted(path for path in source.files if path.casefold().endswith('.xml'))
    target = version_tuple(version)
    versions = {source_path_version(path) for path in paths}
    compatible = [v for v in versions if v is not None and (target is None or v <= target)]
    selected = max(compatible) if compatible else None
    matches = {}
    for path in paths:
        path_version = source_path_version(path)
        if path_version is not None and path_version != selected:
            continue
        if path_version is not None and selected is None:
            continue
        parts = PurePosixPath(path).parts
        priority = (2 if path_version is not None else
                    1 if any(p.casefold() == 'common' for p in parts) else 0)
        key = _logical_path(path)
        previous = matches.get(key)
        if previous is None or priority > previous[0]:
            matches[key] = (priority, path)
    for _, path in sorted(matches.values(), key=lambda entry: entry[1].casefold()):
        yield path


def _definitions(path, root):
    if root.tag != 'Defs':
        return False
    # Any Defs XML may contain patched creatures, eggs, items, biome or
    # inherited abstract definitions, regardless of its filename.
    return any(node.tag in _DEFS for node in root)


def _patch_is_relevant(path, root, animal_names):
    if root.tag != 'Patch':
        return False
    name = str(PurePosixPath(path))
    if not _CE_HINT.search(name):
        return False
    xpaths = ' '.join((node.text or '') for node in root.iter('xpath'))
    if not _CE_XPATH.search(xpaths):
        return False
    # Do not accumulate unrelated CE weapon, faction or human patches;
    # include generic material/biome operations and patch files targeting
    # declared animals or resources.
    if re.search(r'Leather|Wool|Silk|StuffPower_|BiomeDef|Egg|Milk|Hatch|Shear', xpaths, re.I):
        return True
    if any(name in xpaths for name in animal_names):
        return True
    return 'races_animal' in name.casefold()


def discover_source_references(spec, sources, checker_root, version,
                               *, safe_inside, parse_xml, digest, animal_names=()):
    """Build canonical snapshots from one preferred upstream source.

    A local Workshop source is used only if the preferred GitHub source yields
    no usable references; different installations are never merged as if one mod.
    """
    if not sources:
        return []
    ce = spec['id'] == 'CombatExtended'
    owner = spec['reference_dirs'][0].removeprefix('OriginalXML/')
    target_version = version_tuple(version)

    def source_rank(source):
        compatible = [source_path_version(path) for path in source.files]
        compatible = [item for item in compatible
                      if item is not None and
                      (target_version is None or item <= target_version)]
        return max(compatible, default=(0, 0))

    # Pick the newest compatible upstream mod snapshot across GitHub/Workshop.
    # For equal versions, preserve the supplied GitHub-before-local preference.
    ordered_sources = sorted(sources, key=source_rank, reverse=True)
    for source in ordered_sources:
        results = {}
        for rel in selected_paths(source, version):
            if any(token in rel.casefold() for token in _EXCLUDE):
                continue
            parts = [part.casefold() for part in PurePosixPath(rel).parts]
            if ce:
                if not _CE_HINT.search(rel) or not ('patches' in parts or 'modpatches' in parts):
                    continue
            elif not ('defs' in parts or 'def' in parts):
                continue
            try:
                data = source.read(rel)
                doc = parse_xml(data, 'Patch' if ce else 'Defs')
            except (ValueError, OSError, ET.ParseError, subprocess.TimeoutExpired):
                continue
            if ce:
                if not _patch_is_relevant(rel, doc, animal_names):
                    continue
                dest = 'OriginalXML/CEPatches/' + rel
            else:
                if not _definitions(rel, doc):
                    continue
                dest = 'OriginalXML/' + owner + '/' + rel
            target = safe_inside(checker_root, dest)
            current = target.read_bytes() if target.is_file() else None
            results[dest.casefold()] = {
                'destination': dest,
                'status': 'COPY' if current is None else
                          ('SKIP' if current == data else 'REPLACE'),
                'source_id': spec['id'], 'provenance': source.provenance,
                'source_file': rel, 'source_sha256': digest(data),
                'previous_sha256': digest(current), 'payload': data,
            }
        if results:
            return sorted(results.values(), key=lambda result: result['destination'])
    return []


def coverage(registry, spec, items):
    """Report uncovered registered animals/biomes when an upstream source exists."""
    if not registry:
        return []
    group = spec['id']
    expected = set()
    for entry in registry.get('groups', []):
        if entry['mod'] in (group, 'DLC:' + group):
            expected.update(entry['defs'])
    for entry in registry.get('biomes', []):
        destination = entry['destination']
        if ((group == 'Core' and destination.startswith('Core/')) or
            (group == 'Odyssey' and destination.startswith('DLC/Odyssey/')) or
            (group == 'AlphaBiomes' and destination.startswith('ModPatches/AlphaBiomes/'))):
            expected.add(entry['defName'])
    if not expected:
        return []
    actual = set()
    for item in items:
        if item.get('source_id') != group or 'payload' not in item:
            continue
        try:
            root = ET.fromstring(item['payload'])
        except ET.ParseError:
            continue
        if root.tag != 'Defs':
            continue
        actual.update((node.findtext('defName') or '').strip()
                      for node in root if node.tag in _DEFS)
    missing = sorted(expected - actual)
    if not missing:
        return []
    return [f'{group}: {len(missing)} registered DefNames missing from sourced '
            f'XML: {", ".join(missing[:12])}' +
            (' ...' if len(missing) > 12 else '')]
, re.I)


def version_tuple(version):
    """Compare arbitrary RimWorld major.minor versions numerically."""
    if version is None or str(version).strip().lower() in ('', 'auto'):
        return None
    match = re.match(r'^v?(\d+)\.(\d+)(?:\.\d+)?$', str(version).strip(), re.I)
    if not match:
        raise ValueError(f'Invalid RimWorld version: {version!r}')
    return tuple(map(int, match.groups()))


def source_path_version(path):
    for part in PurePosixPath(path).parts[:-1]:
        match = _VERSION.fullmatch(part)
        if match:
            return tuple(map(int, match.groups()))
    return None


def _logical_path(path):
    """A versioned file replaces the same root/Common relative path."""
    parts = [part for part in PurePosixPath(path).parts if
             not _VERSION.fullmatch(part) and part.casefold() != 'common']
    return '/'.join(parts).casefold()


def selected_paths(source, version=None):
    """Pick one highest compatible version per source, plus shared root/Common XML.

    RimWorld loads the highest compatible version directory, not every version.
    If the game version is unknown, use the newest version supplied by the mod.
    """
    paths = sorted(path for path in source.files if path.casefold().endswith('.xml'))
    target = version_tuple(version)
    versions = {source_path_version(path) for path in paths}
    compatible = [v for v in versions if v is not None and (target is None or v <= target)]
    selected = max(compatible) if compatible else None
    matches = {}
    for path in paths:
        path_version = source_path_version(path)
        if path_version is not None and path_version != selected:
            continue
        if path_version is not None and selected is None:
            continue
        parts = PurePosixPath(path).parts
        priority = (2 if path_version is not None else
                    1 if any(p.casefold() == 'common' for p in parts) else 0)
        key = _logical_path(path)
        previous = matches.get(key)
        if previous is None or priority > previous[0]:
            matches[key] = (priority, path)
    for _, path in sorted(matches.values(), key=lambda entry: entry[1].casefold()):
        yield path


def _definitions(path, root):
    if root.tag != 'Defs':
        return False
    # Any Defs XML may contain patched creatures, eggs, items, biome or
    # inherited abstract definitions, regardless of its filename.
    return any(node.tag in _DEFS for node in root)


def _patch_is_relevant(path, root, animal_names):
    if root.tag != 'Patch':
        return False
    name = str(PurePosixPath(path))
    if not _CE_HINT.search(name):
        return False
    xpaths = ' '.join((node.text or '') for node in root.iter('xpath'))
    if not _CE_XPATH.search(xpaths):
        return False
    # Do not accumulate unrelated CE weapon, faction or human patches;
    # include generic material/biome operations and patch files targeting
    # declared animals or resources.
    if re.search(r'Leather|Wool|Silk|StuffPower_|BiomeDef|Egg|Milk|Hatch|Shear', xpaths, re.I):
        return True
    if any(name in xpaths for name in animal_names):
        return True
    return 'races_animal' in name.casefold()


def discover_source_references(spec, sources, checker_root, version,
                               *, safe_inside, parse_xml, digest, animal_names=()):
    """Build canonical snapshots from one preferred upstream source.

    A local Workshop source is used only if the preferred GitHub source yields
    no usable references; different installations are never merged as if one mod.
    """
    if not sources:
        return []
    ce = spec['id'] == 'CombatExtended'
    owner = spec['reference_dirs'][0].removeprefix('OriginalXML/')
    target_version = version_tuple(version)

    def source_rank(source):
        compatible = [source_path_version(path) for path in source.files]
        compatible = [item for item in compatible
                      if item is not None and
                      (target_version is None or item <= target_version)]
        return max(compatible, default=(0, 0))

    # Pick the newest compatible upstream mod snapshot across GitHub/Workshop.
    # For equal versions, preserve the supplied GitHub-before-local preference.
    ordered_sources = sorted(sources, key=source_rank, reverse=True)
    for source in ordered_sources:
        results = {}
        for rel in selected_paths(source, version):
            if any(token in rel.casefold() for token in _EXCLUDE):
                continue
            parts = [part.casefold() for part in PurePosixPath(rel).parts]
            if ce:
                if not _CE_HINT.search(rel) or not ('patches' in parts or 'modpatches' in parts):
                    continue
            elif not ('defs' in parts or 'def' in parts):
                continue
            try:
                data = source.read(rel)
                doc = parse_xml(data, 'Patch' if ce else 'Defs')
            except (ValueError, OSError, ET.ParseError, subprocess.TimeoutExpired):
                continue
            if ce:
                if not _patch_is_relevant(rel, doc, animal_names):
                    continue
                dest = 'OriginalXML/CEPatches/' + rel
            else:
                if not _definitions(rel, doc):
                    continue
                dest = 'OriginalXML/' + owner + '/' + rel
            target = safe_inside(checker_root, dest)
            current = target.read_bytes() if target.is_file() else None
            results[dest.casefold()] = {
                'destination': dest,
                'status': 'COPY' if current is None else
                          ('SKIP' if current == data else 'REPLACE'),
                'source_id': spec['id'], 'provenance': source.provenance,
                'source_file': rel, 'source_sha256': digest(data),
                'previous_sha256': digest(current), 'payload': data,
            }
        if results:
            return sorted(results.values(), key=lambda result: result['destination'])
    return []


def coverage(registry, spec, items):
    """Report uncovered registered animals/biomes when an upstream source exists."""
    if not registry:
        return []
    group = spec['id']
    expected = set()
    for entry in registry.get('groups', []):
        if entry['mod'] in (group, 'DLC:' + group):
            expected.update(entry['defs'])
    for entry in registry.get('biomes', []):
        destination = entry['destination']
        if ((group == 'Core' and destination.startswith('Core/')) or
            (group == 'Odyssey' and destination.startswith('DLC/Odyssey/')) or
            (group == 'AlphaBiomes' and destination.startswith('ModPatches/AlphaBiomes/'))):
            expected.add(entry['defName'])
    if not expected:
        return []
    actual = set()
    for item in items:
        if item.get('source_id') != group or 'payload' not in item:
            continue
        try:
            root = ET.fromstring(item['payload'])
        except ET.ParseError:
            continue
        if root.tag != 'Defs':
            continue
        actual.update((node.findtext('defName') or '').strip()
                      for node in root if node.tag in _DEFS)
    missing = sorted(expected - actual)
    if not missing:
        return []
    return [f'{group}: {len(missing)} registered DefNames missing from sourced '
            f'XML: {", ".join(missing[:12])}' +
            (' ...' if len(missing) > 12 else '')]
