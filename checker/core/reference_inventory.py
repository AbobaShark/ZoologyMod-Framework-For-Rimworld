"""Source-first, reproducible Reference Sync inventory.

Import relevant Defs without requiring a pre-existing OriginalXML tree.
Each snapshot preserves the upstream relative path under its source owner.
"""
from __future__ import annotations

from pathlib import PurePosixPath
import re
import subprocess
import xml.etree.ElementTree as ET

from core.leather_reference_sync import _contains_leather


_DEFS = {'ThingDef', 'PawnKindDef', 'BiomeDef', 'LifeStageDef', 'HediffDef'}
_EXCLUDE = {'photonozoa', 'kaliska'}
_CE_HINT = re.compile(
    r'leather|wool|silk|stuff|resource|races?[_/]|animal|biome|egg|hatch|milk|shear|pawnkind',
    re.I,
)
_CE_XPATH = re.compile(r'ThingDef|PawnKindDef|BiomeDef|LifeStageDef|LeatherBase|WoolBase')
_VERSION = re.compile(r'^v?1\.\d+$', re.I)


def selected_paths(source, version='1.7'):
    """Use common and highest compatible version paths, never merge 1.6 and 1.7."""
    paths = sorted(path for path in source.files if path.lower().endswith('.xml'))
    versions = set()
    for path in paths:
        for part in PurePosixPath(path).parts[:-1]:
            if _VERSION.fullmatch(part):
                versions.add(part.lstrip('v'))
    eligible = sorted((v for v in versions if tuple(map(int, v.split('.'))) <=
                      tuple(map(int, version.split('.')))),
                      key=lambda v: tuple(map(int, v.split('.'))))
    chosen = version if version in versions else (eligible[-1] if eligible else None)
    for path in paths:
        components = PurePosixPath(path).parts[:-1]
        versions_in_path = [part.lstrip('v') for part in components
                            if _VERSION.fullmatch(part)]
        if versions_in_path and versions_in_path[-1] != chosen:
            continue
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


def discover_source_references(spec, sources, checker_root, existing, version,
                               *, safe_inside, parse_xml, digest, animal_names=()):
    """Yield new Defs/CE reference snapshots using upstream XML, not old files."""
    if not sources:
        return []
    ce = spec['id'] == 'CombatExtended'
    owner = spec['reference_dirs'][0].removeprefix('OriginalXML/')
    prior = {path.casefold() for path in existing}
    results = {}
    for source in sources:
        for rel in selected_paths(source, version):
            if any(token in rel.casefold() for token in _EXCLUDE):
                continue
            parts = [part.casefold() for part in PurePosixPath(rel).parts]
            if ce:
                if not _CE_HINT.search(rel) or not ('patches' in parts or
                                                    'modpatches' in parts):
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
                subset = '_Leathers' if _contains_leather(doc, False) else '_Defs'
                dest = 'OriginalXML/' + owner + '/' + subset + '/' + rel
            key = dest.casefold()
            if key in prior or key in results:
                continue
            target = safe_inside(checker_root, dest)
            current = target.read_bytes() if target.is_file() else None
            results[key] = {
                'destination': dest,
                'status': 'COPY' if current is None else
                          ('SKIP' if current == data else 'REPLACE'),
                'source_id': spec['id'], 'provenance': source.provenance,
                'source_file': rel, 'source_sha256': digest(data),
                'previous_sha256': digest(current), 'payload': data,
            }
    return sorted(results.values(), key=lambda result: result['destination'])


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
