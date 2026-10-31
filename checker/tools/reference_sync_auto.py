"""Optional automatic, reviewed upstream reference synchronization for Checker.

Runs independently of the generator. No Git operations are performed in the Zoology checkout.

Launch GUI: python reference_sync_gui.py
CLI: python -m tools.reference_sync_auto --config reference_sources.auto.json --steam <path> [--no-github] [--apply]
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
from functools import lru_cache
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
from typing import Optional
from urllib import parse as urlparse, request as urlrequest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
GAME_ID = '294100'
MAX_XML_BYTES = 12 * 1024 * 1024
_SHA = re.compile(r'^[0-9a-fA-F]{40}$')
_V = re.compile(r'^(?:1\.[0-9]+|v1\.[0-9]+)$', re.I)


def digest(data: Optional[bytes]):
    return hashlib.sha256(data).hexdigest() if data is not None else None


def normalized(s):
    return re.sub(r'[^a-z0-9]+', '', str(s).casefold())


def safe_inside(root: Path, rel: str) -> Path:
    p = PurePosixPath(rel.replace('\\', '/'))
    if not rel or p.is_absolute() or any(s in ('.', '..', '') for s in p.parts):
        raise ValueError(f'Unsafe reference path: {rel!r}')
    root = Path(root).resolve()
    dest = (root.joinpath(*p.parts)).resolve()
    if not dest.is_relative_to(root):
        raise ValueError(f'Reference path escapes checker: {rel!r}')
    return dest


def parse_xml(blob: bytes, expected: str):
    if len(blob) > MAX_XML_BYTES or b'<!DOCTYPE' in blob.upper() or b'<!ENTITY' in blob.upper():
        raise ValueError('Oversized XML or prohibited DOCTYPE/ENTITY declaration')
    root = ET.fromstring(blob)
    if root.tag != expected:
        raise ValueError(f'Expected <{expected}>, got <{root.tag}>')
    return root


def signature(root):
    return {(c.tag, (c.findtext('defName') or c.get('Name') or '').strip())
            for c in root if (c.findtext('defName') or c.get('Name') or '').strip()}


def version_rank(path: str, target_version: str):
    # Explicit version in a path takes precedence over common/root. Prefer
    # requested version, then non-versioned directories, then earlier versions.
    parts = [x.lower().lstrip('v') for x in PurePosixPath(path).parts[:-1]]
    versions = [x for x in parts if _V.fullmatch(x)]
    if not versions:
        return 250
    if target_version in versions:
        return 1000
    ver = versions[-1]
    try:
        number = float(ver)
        target = float(target_version)
    except ValueError:
        return -100
    return int(number * 100) if number < target else -500


def file_stem(path: str) -> str:
    stem = PurePosixPath(path).stem
    return normalized(stem)


def read_about(folder: Path):
    xml = folder / 'About' / 'About.xml'
    try:
        if xml.is_file():
            tree = ET.parse(xml)
            return (tree.findtext('.//packageId') or '', tree.findtext('.//name') or '')
    except (OSError, ET.ParseError):
        pass
    return ('', '')


def steam_locations(root: str):
    """Bounded discovery of installations and libraries, not a disk-wide crawl."""
    if not root:
        return [], [], []
    p = Path(root).expanduser().resolve()
    candidates = [p]
    for ancestor in (p, *list(p.parents)[:5]):
        candidates.append(ancestor)
        candidates.extend((ancestor / 'steamapps', ancestor / 'steamapps' / 'common' / 'RimWorld',
                           ancestor / 'steamapps' / 'workshop' / 'content' / GAME_ID))
    # Steam may have secondary library folders listed in libraryfolders.vdf.
    for steamapps in {x for x in candidates if x.name.lower() == 'steamapps'}:
        vdf = steamapps / 'libraryfolders.vdf'
        if vdf.is_file():
            try:
                raw = vdf.read_text(encoding='utf-8-sig', errors='replace')
                for path in re.findall(r'"path"\s*"([^"\r\n]+)"', raw):
                    base = Path(path.replace('\\\\', '\\'))
                    candidates.extend((base / 'steamapps' / 'common' / 'RimWorld',
                                       base / 'steamapps' / 'workshop' / 'content' / GAME_ID))
            except OSError:
                pass
    games, workshops, mods = set(), set(), set()
    for candidate in candidates:
        if not candidate.is_dir():
            continue
        if (candidate / 'Data' / 'Core' / 'Defs').is_dir():
            games.add(candidate)
        if (candidate / 'Core' / 'Defs').is_dir() and candidate.name == 'Data':
            games.add(candidate.parent)
        if candidate.name == GAME_ID and candidate.parent.name == 'content':
            workshops.add(candidate)
        if (candidate / 'workshop' / 'content' / GAME_ID).is_dir():
            workshops.add(candidate / 'workshop' / 'content' / GAME_ID)
        if (candidate / 'Mods').is_dir():
            mods.add(candidate / 'Mods')
        if candidate.name.lower() == 'mods':
            mods.add(candidate)
    return sorted(games), sorted(workshops), sorted(mods)


def find_local_mod(spec, games, workshops, mod_roots):
    if 'game_dlc' in spec:
        roots = [game / 'Data' / spec['game_dlc'] for game in games]
        present = [p for p in roots if p.is_dir()]
        return (present[0], None) if len(present) == 1 else (None, 'Multiple installations' if present else 'Game/DLC not found')
    candidates = []
    ids = {str(x) for x in spec.get('workshop_ids', [])}
    package_ids = {normalized(x) for x in spec.get('package_ids', [])}
    names = {normalized(x) for x in [spec['id'], *spec.get('names', [])]}
    for workshop in workshops:
        for item in workshop.iterdir():
            if item.is_dir():
                candidates.append(item)
    for modroot in mod_roots:
        for item in modroot.iterdir():
            if item.is_dir():
                candidates.append(item)
    scored = []
    for p in candidates:
        # About.xml helps determine the identity even if Steam folders are numeric.
        packid, name = read_about(p)
        score = (1000 if p.name in ids else 0) + (400 if normalized(packid) in package_ids and packid else 0)
        score += (200 if normalized(name) in names and name else 0) + (100 if normalized(p.name) in names else 0)
        if score:
            scored.append((score, p))
    if not scored:
        return None, 'Not installed / not detected'
    scored.sort(key=lambda x: (-x[0], str(x[1]).lower()))
    if len(scored) > 1 and scored[0][0] == scored[1][0]:
        return None, 'Ambiguous local installations: ' + ', '.join(str(x[1]) for x in scored if x[0] == scored[0][0])
    return scored[0][1], None


class LocalSource:
    def __init__(self, root):
        self.root = Path(root).resolve()
        self.provenance = str(self.root)
        self._cache = {}
        self._cache_size = 0
        self.files = []
        for p in self.root.rglob('*.xml'):
            try:
                if p.is_file() and p.stat().st_size <= MAX_XML_BYTES and p.resolve().is_relative_to(self.root):
                    self.files.append(p.relative_to(self.root).as_posix())
            except OSError:
                pass

    def read(self, rel):
        if rel in self._cache:
            return self._cache[rel]
        path = safe_inside(self.root, rel)
        data = path.read_bytes() if path.stat().st_size <= MAX_XML_BYTES else b''
        if self._cache_size + len(data) < 48 * 1024 * 1024:
            self._cache[rel] = data
            self._cache_size += len(data)
        return data

    def close(self):
        pass


class GitSource:
    """Shallow, partial fetch to a private temp bare repo; never touch parent .git."""
    def __init__(self, spec):
        gh = spec.get('github') or {}
        url = gh.get('url', '')
        if not re.fullmatch(r'https://github\.com/[\w.-]+/[\w.-]+(?:\.git)?', url):
            raise ValueError('Only explicit GitHub HTTPS repository URLs are accepted')
        revision = gh.get('revision', '').strip()
        branch = gh.get('branch', '').strip()
        if revision and not _SHA.fullmatch(revision):
            raise ValueError('Pinned Git revision must be a full 40-digit SHA')
        if branch and not re.fullmatch(r'[\w./-]+', branch):
            raise ValueError('Invalid Git branch')
        self.temp = tempfile.TemporaryDirectory(prefix='zoology-upstream-')
        self.gitdir = Path(self.temp.name) / 'refs.git'
        self._cache = {}
        self._cache_size = 0
        self.read_error = None
        self.raw_base = 'https://raw.githubusercontent.com/' + url.removeprefix('https://github.com/').removesuffix('.git') + '/'
        try:
            self._run('init', '--bare', '-q', str(self.gitdir), timeout=20)
            self._run('remote', 'add', 'origin', url, timeout=20)
            self._run('fetch', '-q', '--depth=1', '--filter=blob:none', 'origin', revision or (f'refs/heads/{branch}' if branch else 'HEAD'), timeout=120)
            self.revision = self._run('rev-parse', 'FETCH_HEAD').strip()
            if revision and self.revision.lower() != revision.lower():
                raise ValueError('Remote revision mismatch')
            out = self._run('ls-tree', '-r', '--name-only', 'FETCH_HEAD', timeout=40)
            self.files = [x for x in out.splitlines() if x.lower().endswith('.xml')]
            self.provenance = url + '@' + self.revision
        except BaseException:
            self.close()
            raise

    def _run(self, *args, timeout=30, raw=False):
        env = dict(os.environ, GIT_TERMINAL_PROMPT='0', GIT_CONFIG_NOSYSTEM='1')
        cmd = ['git', '--git-dir=' + str(self.gitdir), *args] if args[0] != 'init' else ['git', *args]
        cp = subprocess.run(cmd, check=True, capture_output=True, timeout=timeout, env=env)
        return cp.stdout if raw else cp.stdout.decode('utf-8', 'replace')

    def read(self, rel):
        if rel in self._cache:
            return self._cache[rel]
        path = PurePosixPath(rel)
        if path.is_absolute() or '..' in path.parts or '\x00' in rel:
            raise ValueError('Invalid Git path')
        if self.read_error:
            raise OSError(self.read_error)
        # Blobless git show lazily fetches each XML and may stall for a minute.
        # Fetch the immutable commit's raw XML over bounded HTTPS instead.
        url = self.raw_base + self.revision + '/' + urlparse.quote(rel, safe='/')
        try:
            with urlrequest.urlopen(url, timeout=12) as stream:
                data = stream.read(MAX_XML_BYTES + 1)
        except OSError as exc:
            self.read_error = 'GitHub XML download unavailable: ' + str(exc)
            raise OSError(self.read_error) from exc
        if len(data) > MAX_XML_BYTES:
            raise ValueError('XML exceeds size limit')
        if self._cache_size + len(data) < 48 * 1024 * 1024:
            self._cache[rel] = data
            self._cache_size += len(data)
        return data

    def close(self):
        if hasattr(self, 'temp'):
            self.temp.cleanup()


def read_config(path):
    cfg = json.loads(Path(path).read_text(encoding='utf-8-sig'))
    if cfg.get('schema') != 2 or not isinstance(cfg.get('mods'), list):
        raise ValueError('Auto-sync requires configuration schema 2 with mods[]')
    seen_groups = set()
    for mod in cfg['mods']:
        dirs = mod.get('reference_dirs', [])
        if not mod.get('id') or not isinstance(dirs, list) or not dirs:
            raise ValueError('Each mod needs id and reference_dirs[]')
        for dr in dirs:
            if not dr.startswith(('OriginalXML/',)):
                raise ValueError('Reference paths must belong to OriginalXML')
            safe_inside(ROOT, dr)
            if dr.casefold() in seen_groups:
                raise ValueError('Duplicate reference directory '+dr)
            seen_groups.add(dr.casefold())
    return cfg


def candidate_order(rel, dest, version):
    # Bias towards same directory context (e.g. NoOdyssey / Core).
    dest_parts = {normalized(x) for x in PurePosixPath(dest).parts[:-1]}
    shared = sum(1 for part in PurePosixPath(rel).parts[:-1] if normalized(part) in dest_parts)
    return (version_rank(rel, version), shared)


def choose_match(src, dest_rel, old_bytes, *, version, kind):
    if dest_rel.startswith('OriginalXML/CEPatches/'):
        rel = dest_rel.removeprefix('OriginalXML/CEPatches/')
        if rel not in src.files:
            return None, 'Exact CE patch file not present in upstream source'
        try:
            data = src.read(rel)
            parse_xml(data, 'Patch')
        except (ValueError, ET.ParseError, OSError, subprocess.TimeoutExpired) as exc:
            return None, str(exc)
        return (rel, data), None
    if '/_Defs/' in dest_rel:
        rel = dest_rel.split('/_Defs/', 1)[1]
        if rel not in src.files:
            return None, 'Exact original Defs file not present in upstream source'
        try:
            data = src.read(rel)
            parse_xml(data, kind)
        except (ValueError, ET.ParseError, OSError, subprocess.TimeoutExpired) as exc:
            return None, str(exc)
        return (rel, data), None
    if '/_Leathers/' in dest_rel:
        rel = dest_rel.split('/_Leathers/', 1)[1]
        if rel not in src.files:
            return None, 'Exact original leather file not present in upstream source'
        try:
            data = src.read(rel)
            parse_xml(data, kind)
        except (ValueError, ET.ParseError, OSError, subprocess.TimeoutExpired) as exc:
            return None, str(exc)
        return (rel, data), None
    basename = file_stem(dest_rel)
    alternatives = [p for p in src.files if file_stem(p) == basename]
    old_sig = signature(parse_xml(old_bytes, 'Defs')) if kind == 'Defs' else set()
    # For renamed race XML, try semantic matching only when filenames match
    # approximately. Otherwise the risk of replacing unrelated files is high.
    if not alternatives and kind == 'Defs' and old_sig:
        alternatives = [p for p in src.files if 'race' in p.lower() or 'animal' in p.lower()]
    # Prefer current-version candidates before downloading any XML. Once a
    # validated higher-ranked candidate is found, older versions are irrelevant.
    candidates = []
    for rel in alternatives:
        exact = file_stem(rel) == basename
        vscore, dscore = candidate_order(rel, dest_rel, version)
        candidates.append(((int(exact), vscore, dscore), rel))
    candidates.sort(key=lambda item: item[0], reverse=True)
    best = None
    for score, rel in candidates:
        if best is not None and score < best[0]:
            break
        try:
            data = src.read(rel)
            root = parse_xml(data, kind)
        except (ValueError, ET.ParseError, OSError, subprocess.CalledProcessError, subprocess.TimeoutExpired):
            continue
        exact = file_stem(rel) == basename
        if kind == 'Defs':
            theirs = signature(root)
            intersection = len(old_sig & theirs)
            if old_sig and intersection / len(old_sig) < 0.8:
                continue
            if not exact and (not theirs or intersection / len(theirs) < 0.6):
                continue
        elif not exact:
            continue
        if best is not None and best[2] != data:
            return None, 'Ambiguous source: ' + best[1] + ', ' + rel
        best = (score, rel, data)
    if best is None:
        return None, 'No matching validated upstream XML'
    return (best[1], best[2]), None


def discover(cfg, *, steam='', github=True, checker_root=ROOT, progress=None):
    """Return (items, warnings), without writing the project or references."""
    checker_root = Path(checker_root).resolve()
    games, workshops, mod_roots = steam_locations(steam)
    version = cfg.get('rimworld_version', '1.7')
    items, warnings = [], []
    callback = progress or (lambda _: None)
    destinations = set()
    from core.reference_inventory import discover_source_references, coverage
    registry_path = checker_root / 'generation.json'
    registry = json.loads(registry_path.read_text(encoding='utf-8')) if registry_path.is_file() else {}
    animal_names = {name for group in registry.get('groups', ()) for name in group['defs']}
    for spec in cfg['mods']:
        if not spec.get('enabled', True):
            continue
        callback('Scanning '+spec['id'])
        targets = []
        for ref_dir in spec['reference_dirs']:
            base = safe_inside(checker_root, ref_dir)
            if not base.exists():
                # Unicode dash / spacing variations are common in VAE folder
                # names. Resolve a uniquely matching directory, never guess
                # between several possible reference groups.
                parent = base.parent
                candidates = ([p for p in parent.iterdir() if p.is_dir() and
                              normalized(p.name) == normalized(base.name)]
                              if parent.is_dir() else [])
                if len(candidates) == 1:
                    base = candidates[0]
                    warnings.append(spec['id']+': resolved directory spelling '+str(base))
                else:
                    warnings.append(spec['id'] + ': reference directory not found: '+ref_dir)
                    continue
            for path in sorted(base.rglob('*.xml')):
                dest_rel = path.relative_to(checker_root).as_posix()
                if dest_rel.lower() in destinations:
                    raise ValueError('Duplicate target reference '+dest_rel)
                destinations.add(dest_rel.lower())
                targets.append((dest_rel, path))
        # New material references may need to be imported even when this
        # group has no pre-existing tracked XML files. Do not skip a source
        # merely because there is nothing yet to compare or replace.
        sources = []
        gh = spec.get('github') or {}
        if github and gh.get('url'):
            callback('GitHub '+spec['id']+' (latest default branch unless pinned)')
            try:
                sources.append(GitSource(spec))
            except (OSError, ValueError, subprocess.CalledProcessError, subprocess.TimeoutExpired) as e:
                warnings.append(spec['id'] + ': GitHub unavailable: '+str(e)[:300])
        loc, err = find_local_mod(spec, games, workshops, mod_roots)
        if loc:
            try:
                sources.append(LocalSource(loc))
            except OSError as e:
                warnings.append(spec['id']+': local scan failed: '+str(e))
        elif err:
            warnings.append(spec['id']+': '+err)
        try:
            claimed_source_paths = set()
            for target_rel, target_path in targets:
                kind = 'Patch' if target_rel.startswith('OriginalXML/CEPatches/') else 'Defs'
                old_data = target_path.read_bytes()
                try:
                    parse_xml(old_data, kind)
                except (ValueError, ET.ParseError) as e:
                    items.append({'destination': target_rel, 'status': 'REVIEW', 'source_id':spec['id'],
                                  'detail':'Existing reference invalid: '+str(e)})
                    continue
                found, errors = None, []
                for src in sources:
                    callback(spec['id']+': '+target_path.name)
                    match, error = choose_match(src, target_rel, old_data, version=version, kind=kind)
                    if match:
                        found = (src, *match)
                        break
                    errors.append(src.provenance+': '+str(error))
                if not found:
                    items.append({'destination':target_rel, 'status':'MISSING', 'source_id':spec['id'],
                                  'detail':'; '.join(errors) if errors else 'No available upstream source'})
                    continue
                src, source_path, payload = found
                claimed_source_paths.add(source_path)
                status = 'SKIP' if payload == old_data else 'REPLACE'
                items.append({'destination':target_rel, 'status':status,
                              'source_id':spec['id'], 'provenance':src.provenance,
                              'source_file':source_path, 'source_sha256':digest(payload),
                              'previous_sha256':digest(old_data), 'payload':payload})
            # Reconstruct relevant Defs and CE reference patches directly from
            # upstream files. Existing references are not a prerequisite.
            discovered = discover_source_references(
                spec, sources, checker_root, {dest for dest, _ in targets}, version,
                safe_inside=safe_inside, parse_xml=parse_xml, digest=digest,
                animal_names=animal_names, claimed_source_paths=claimed_source_paths)
            items.extend(discovered)
            if sources:
                warnings.extend(coverage(registry, spec,
                                [item for item in items if item.get('source_id') == spec['id']]))
        finally:
            for src in sources:
                if getattr(src, 'read_error', None):
                    warnings.append(spec['id'] + ': ' + src.read_error)
                src.close()
    return items, warnings


def apply(items, checker_root=ROOT):
    """Atomic best-effort deployment with backups and rollback on failure."""
    checker_root = Path(checker_root).resolve()
    changes = [it for it in items if it['status'] in ('REPLACE', 'COPY')]
    if not changes:
        return None
    root = checker_root / 'output' / 'reference-backups'
    root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='reference-stage-', dir=root) as temp:
        staged = []
        for index, it in enumerate(changes):
            target = safe_inside(checker_root, it['destination'])
            current = target.read_bytes() if target.exists() else None
            if digest(current) != it['previous_sha256']:
                raise RuntimeError('Reference changed since preview: '+str(target))
            if digest(it['payload']) != it['source_sha256']:
                raise RuntimeError('Source payload checksum mismatch: '+str(target))
            p = Path(temp) / f'{index:05d}.xml'
            p.write_bytes(it['payload'])
            staged.append((it, target, p, current))
        backup = Path(tempfile.mkdtemp(prefix='reference-sync-', dir=root))
        written = []
        try:
            for it, target, stage, original in staged:
                if original is not None:
                    bp = safe_inside(backup, it['destination'])
                    bp.parent.mkdir(parents=True, exist_ok=True)
                    bp.write_bytes(original)
                target.parent.mkdir(parents=True, exist_ok=True)
                # Temp target is on same filesystem for os.replace.
                with tempfile.NamedTemporaryFile(dir=target.parent, delete=False, prefix='.sync-') as f:
                    f.write(stage.read_bytes())
                    temp_path = Path(f.name)
                try:
                    os.replace(temp_path, target)
                finally:
                    temp_path.unlink(missing_ok=True)
                written.append((target, original))
            report = [{k:v for k,v in it.items() if k != 'payload'} for it in items]
            (backup / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
        except BaseException:
            for target, original in reversed(written):
                if original is None:
                    target.unlink(missing_ok=True)
                else:
                    target.write_bytes(original)
            raise
        return backup


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--config', default=str(ROOT / 'reference_sources.auto.json'))
    ap.add_argument('--steam', default='', help='Steam library, RimWorld or workshop directory')
    ap.add_argument('--no-github', action='store_true')
    ap.add_argument('--apply', action='store_true', help='Write only validated replacements')
    args = ap.parse_args(argv)
    cfg = read_config(args.config)
    items, warnings = discover(cfg, steam=args.steam, github=not args.no_github, progress=print)
    for x in items:
        print(f"{x['status']:10s} {x['destination']}" + (' <- '+x['source_file'] if 'source_file' in x else ''))
    print('Statuses: ', {status:sum(x['status']==status for x in items) for status in sorted({x['status'] for x in items})})
    for warn in warnings:
        print('WARNING: '+warn)
    if args.apply:
        print('Backup: ', apply(items))
    else:
        print('Read-only preview: pass --apply to update files')


if __name__ == '__main__':
    main()
