"""Discover leather, wool and silk XML and CE material patches for auto-sync.

This extends the existing read-only preview model; it never writes on discovery.
The original source-relative path is retained to avoid matching by filename.
"""
from __future__ import annotations

from pathlib import PurePosixPath
import subprocess
import xml.etree.ElementTree as ET


def _interesting(rel, is_ce):
    path = rel.casefold()
    if not path.endswith('.xml'): return False
    # Narrow the scan to original item/stuff and CE patches. Non-leather
    # resource files may contain the abstract LeatherBase or material defs.
    return any(part in path for part in ('leather', 'stuff', 'resource', 'wool', 'silk'))


def _contains_leather(root, is_ce):
    if is_ce:
        if root.tag != 'Patch': return False
        return any(
            'ThingDef[' in (node.text or '')
            and any(term in (node.text or '').casefold()
                    for term in ('leather', 'wool', 'silk'))
            and any(part in (node.text or '')
                    for part in ('/statBases', '/stuffProps', '/graphicData', '@Name='))
            for node in root.iter('xpath')
        )
    if root.tag != 'Defs': return False
    for td in root.findall('ThingDef'):
        n = td.findtext('defName') or ''
        if any(term in n.casefold() for term in ('leather', 'wool', 'silk')) or td.get('Name') in ('LeatherBase', 'WoolBase'):
            return True
    return False


def discover_new_leather_references(spec, sources, checker_root, targets, version, *,
                                    safe_inside, parse_xml, digest):
    """Return COPY/REPLACE preview entries for material original XML and CE patches.

    Exact source paths ensure a later sync updates rather than duplicates. The
    source preference is that of the existing auto-sync (GitHub, then local).
    """
    if not sources: return []
    ce = spec['id'] == 'CombatExtended'
    group = 'CE' if ce else spec['reference_dirs'][0].removeprefix('OriginalXML/')
    prefix = ('OriginalPatches/CE' if ce else 'OriginalXML/'+group) + '/_Leathers/'
    existing = {dest.casefold() for dest, _ in targets}
    output = {}
    for src in sources:
        for rel in sorted(src.files):
            if not _interesting(rel, ce): continue
            dest = prefix + PurePosixPath(rel).as_posix()
            if dest.casefold() in existing or dest.casefold() in output:
                continue
            try:
                data = src.read(rel)
                root = parse_xml(data, 'Patch' if ce else 'Defs')
                if not _contains_leather(root, ce): continue
            except (ValueError, OSError, ET.ParseError, subprocess.TimeoutExpired):
                continue
            destination = safe_inside(checker_root, dest)
            prior = destination.read_bytes() if destination.is_file() else None
            # Only update exact source snapshots; no heuristic matching.
            state = 'COPY' if prior is None else ('SKIP' if prior == data else 'REPLACE')
            output[dest.casefold()] = {
                'destination':dest, 'status':state, 'source_id':spec['id'],
                'provenance':src.provenance, 'source_file':rel,
                'source_sha256':digest(data), 'previous_sha256':digest(prior),
                'payload':data,
            }
    return sorted(output.values(), key=lambda item:item['destination'])
