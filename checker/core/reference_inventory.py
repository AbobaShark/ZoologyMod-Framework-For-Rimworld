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
_VERSION = re.compile(r'^v?(\d+)\.(\d+)$', re.I)


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


# File names are not authoritative: mixed XML files may contain supported
# animals and unrelated objects. Use registered Def keys for file selection.
_MATERIAL_NAME = re.compile(r'Leather|Wool|Silk|Egg|Hatcher', re.I)
_ANIMAL_BASE = re.compile(r'Animal|Mammal|Bird|Insect|Race|Stuff|Egg', re.I)
_ANIMAL_LIFESTAGE = re.compile(r'Animal|Insect|Bird|Mammal', re.I)
_ANIMAL_HEDIFF = re.compile(r'Animal|Venom|Poison|Toxin', re.I)
_XPATH_KEY = re.compile(r'(?:defName|@Name)\s*=\s*["\']([^"\']+)["\']')


def _candidate_path(rel, ce):
    """Avoid fetching definite nonanimal Def families, without requiring filenames."""
    parts = [part.casefold() for part in PurePosixPath(rel).parts]
    if ce:
        return bool(_CE_HINT.search(rel) and ('patches' in parts or 'modpatches' in parts))
    if not ('defs' in parts or 'def' in parts):
        return False
    return not any('thingdefs_building' in part or
                   'thingdefs_plant' in part or
                   'pawnkinddefs_human' in part or
                   'pawnkinds_human' in part
                   for part in parts)


def _definitions(path, root, animal_names=(), biome_names=(),
                 abstract_names=(), strict=False):
    if root.tag != 'Defs':
        return False
    if not strict:
        return any(node.tag in _DEFS for node in root)
    animals = set(animal_names)
    biomes = set(biome_names)
    abstracts = set(abstract_names)
    for node in root:
        if node.tag not in _DEFS:
            continue
        name = (node.findtext('defName') or '').strip()
        abstract = (node.get('Name') or '').strip()
        if node.tag == 'BiomeDef':
            if name in biomes:
                return True
        elif node.tag == 'PawnKindDef':
            race = (node.findtext('race') or '').strip()
            if name in animals or race in animals or abstract in abstracts:
                return True
        elif node.tag == 'ThingDef':
            if name in animals or abstract in abstracts:
                return True
            if _MATERIAL_NAME.search(name) or _MATERIAL_NAME.search(abstract):
                return True
            if (node.get('Abstract', '').casefold() == 'true' and
                    _ANIMAL_BASE.search(abstract)):
                return True
        elif node.tag == 'LifeStageDef':
            if _ANIMAL_LIFESTAGE.search(name) or _ANIMAL_LIFESTAGE.search(abstract):
                return True
        elif node.tag == 'HediffDef':
            if _ANIMAL_HEDIFF.search(name) or _ANIMAL_HEDIFF.search(abstract):
                return True
    return False


def _patch_is_relevant(path, root, animal_names, abstract_names=(), biome_names=(),
                       strict=False):
    if root.tag != 'Patch':
        return False
    if not _CE_HINT.search(path):
        return False
    xpaths = ' '.join((node.text or '') for node in root.iter('xpath'))
    if not _CE_XPATH.search(xpaths):
        return False
    if not strict:
        return (bool(re.search(r'Leather|Wool|Silk|StuffPower_|BiomeDef|Egg|Milk|Hatch|Shear',
                              xpaths, re.I))
                or any(name in xpaths for name in animal_names)
                or 'races_animal' in path.casefold())
    targets = set(_XPATH_KEY.findall(xpaths))
    relevant = set(animal_names) | set(abstract_names) | set(biome_names)
    return bool(targets & relevant or
                any(_MATERIAL_NAME.search(name) for name in targets))


def discover_source_references(spec, sources, checker_root, version,
                               *, safe_inside, parse_xml, digest, animal_names=(),
                               abstract_names=(), biome_names=(), strict=False,
                               progress=None, cancelled=None):
    """Build canonical snapshots from one preferred upstream source.

    Every snapshot belongs to a single upstream source. Local installed
    Workshop/game definitions take priority; GitHub is only a fallback.
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
        paths = []
        for rel in selected_paths(source, version):
            if any(token in rel.casefold() for token in _EXCLUDE):
                continue
            if not _candidate_path(rel, ce):
                continue
            paths.append(rel)

        if progress is not None:
            progress(f'Inspecting {spec["id"]}: {len(paths)} upstream XML files')
        if cancelled is not None and cancelled.is_set():
            raise InterruptedError('Reference Sync preview cancelled')
        try:
            batch = (source.read_many(paths, progress=progress, cancelled=cancelled)
                     if hasattr(source, 'read_many') else None)
        except InterruptedError:
            raise
        except OSError:
            # A failed remote batch cannot define the complete snapshot.
            # Discard it and try a local Workshop installation instead.
            continue
        for index, rel in enumerate(paths, 1):
            if cancelled is not None and cancelled.is_set():
                raise InterruptedError('Reference Sync preview cancelled')
            if progress is not None and batch is None and (index % 50 == 0 or index == len(paths)):
                progress(f'Inspecting {spec["id"]}: {index}/{len(paths)} XML files')
            try:
                data = batch[rel] if batch is not None else source.read(rel)
                doc = parse_xml(data, 'Patch' if ce else 'Defs')
            except (ValueError, OSError, ET.ParseError, subprocess.TimeoutExpired):
                continue
            if ce:
                if not _patch_is_relevant(rel, doc, animal_names, abstract_names,
                                          biome_names, strict=strict):
                    continue
                dest = 'OriginalXML/CEPatches/' + rel
            else:
                if not _definitions(rel, doc, animal_names, biome_names,
                                    abstract_names, strict=strict):
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
