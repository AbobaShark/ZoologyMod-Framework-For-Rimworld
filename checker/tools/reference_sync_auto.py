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


def installed_game_version(games):
    """Infer major.minor from the installed RimWorld Version.txt."""
    found = set()
    for game in games:
        path = game / 'Version.txt'
        if not path.is_file():
            continue
        try:
            content = path.read_text(encoding='utf-8-sig', errors='replace')
        except OSError:
            continue
        match = re.search(r'(?<!\d)(\d+)\.(\d+)(?:\.\d+)?', content)
        if match:
            found.add('.'.join(match.groups()))
    if len(found) > 1:
        raise ValueError('Multiple RimWorld installations with different versions: ' +
                         ', '.join(sorted(found)) + '. Choose one game installation.')
    return next(iter(found), None)


def discover(cfg, *, steam='', github=True, checker_root=ROOT, progress=None):
    """Preview the canonical upstream snapshot for the selected RimWorld version."""
    from core.reference_inventory import discover_source_references, coverage

    checker_root = Path(checker_root).resolve()
    games, workshops, mod_roots = steam_locations(steam)
    configured_version = cfg.get('rimworld_version', 'auto')
    version = (installed_game_version(games) if str(configured_version).lower() == 'auto'
               else configured_version)
    items, warnings = [], []
    if version is None:
        warnings.append('Game Version.txt unavailable: using the latest version folder in each '
                        'upstream mod. Select a game installation to constrain compatibility.')
    callback = progress or (lambda _: None)
    registry_path = checker_root / 'generation.json'
    registry = json.loads(registry_path.read_text(encoding='utf-8')) if registry_path.is_file() else {}
    animal_names = {name for group in registry.get('groups', ()) for name in group['defs']}

    for spec in cfg['mods']:
        if not spec.get('enabled', True):
            continue
        callback('Scanning ' + spec['id'])
        existing = {}
        for ref_dir in spec['reference_dirs']:
            base = safe_inside(checker_root, ref_dir)
            if not base.is_dir():
                continue
            for path in base.rglob('*.xml'):
                relative = path.relative_to(checker_root).as_posix()
                key = relative.casefold()
                if key in existing:
                    raise ValueError('Duplicate reference destination: ' + relative)
                existing[key] = (relative, path)

        sources = []
        gh = spec.get('github') or {}
        if github and gh.get('url'):
            callback('GitHub ' + spec['id'])
            try:
                sources.append(GitSource(spec))
            except (OSError, ValueError, subprocess.CalledProcessError,
                    subprocess.TimeoutExpired) as exc:
                warnings.append(spec['id'] + ': GitHub unavailable: ' + str(exc)[:300])
        location, err = find_local_mod(spec, games, workshops, mod_roots)
        if location:
            try:
                sources.append(LocalSource(location))
            except OSError as exc:
                warnings.append(spec['id'] + ': local scan failed: ' + str(exc))
        elif err:
            warnings.append(spec['id'] + ': ' + err)

        try:
            if not sources:
                if existing:
                    warnings.append(spec['id'] + ': no upstream source; existing references preserved')
                continue
            current = discover_source_references(
                spec, sources, checker_root, version,
                safe_inside=safe_inside, parse_xml=parse_xml, digest=digest,
                animal_names=animal_names)
            if not current:
                warnings.append(spec['id'] + ': no usable upstream XML; existing references preserved')
                continue

            items.extend(current)
            missing = coverage(registry, spec, current)
            warnings.extend(missing)
            expected = {item['destination'].casefold() for item in current}
            imported_hashes = {item['source_sha256'] for item in current}
            from core.reference_inventory import source_path_version
            current_versions = {source_path_version(item['source_file']) for item in current}
            current_versions.discard(None)
            for key, (relative, path) in sorted(existing.items()):
                if key in expected:
                    continue
                # Identical XML in another location is redundant regardless
                # of its old directory naming or reference categorization.
                superseded = digest(path.read_bytes()) in imported_hashes
                old_version = source_path_version(relative)
                # RimWorld uses one compatible versioned folder. Snapshots
                # from another exact version are obsolete even if a separate
                # registered DefName requires further review.
                obsolete_version = bool(current_versions and old_version is not None
                                        and old_version not in current_versions)
                if missing and not superseded and not obsolete_version:
                    continue
                items.append({
                    'destination': relative, 'status': 'DELETE',
                    'source_id': spec['id'], 'previous_sha256': digest(path.read_bytes()),
                    'detail': 'Not part of the selected upstream version',
                })
            if missing:
                warnings.append(spec['id'] + ': unrelated stale references retained '
                                'until missing source definitions are resolved')
        finally:
            for source in sources:
                if getattr(source, 'read_error', None):
                    warnings.append(spec['id'] + ': ' + source.read_error)
                source.close()
    return items, warnings


def apply(items, checker_root=ROOT):
    """Apply imports and reviewed removals with backups and checksum protection."""
    checker_root = Path(checker_root).resolve()
    changes = [item for item in items if item['status'] in ('COPY', 'REPLACE', 'DELETE')]
    if not changes:
        return None
    backup_root = checker_root / 'output' / 'reference-backups'
    backup_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='reference-stage-', dir=backup_root) as staging:
        staged = []
        for index, item in enumerate(changes):
            target = safe_inside(checker_root, item['destination'])
            original = target.read_bytes() if target.is_file() else None
            if digest(original) != item['previous_sha256']:
                raise RuntimeError('Reference changed since preview: ' + str(target))
            candidate = None
            if item['status'] != 'DELETE':
                if digest(item['payload']) != item['source_sha256']:
                    raise RuntimeError('Source payload checksum mismatch: ' + str(target))
                candidate = Path(staging) / f'{index:05d}.xml'
                candidate.write_bytes(item['payload'])
            elif original is None:
                raise RuntimeError('Reference removed since preview: ' + str(target))
            staged.append((item, target, candidate, original))

        backup = Path(tempfile.mkdtemp(prefix='reference-sync-', dir=backup_root))
        written = []
        try:
            for item, target, candidate, original in staged:
                if original is not None:
                    bp = safe_inside(backup, item['destination'])
                    bp.parent.mkdir(parents=True, exist_ok=True)
                    bp.write_bytes(original)
                if item['status'] == 'DELETE':
                    target.unlink()
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with tempfile.NamedTemporaryFile(dir=target.parent, delete=False,
                                                     prefix='.sync-') as output:
                        output.write(candidate.read_bytes())
                        temporary = Path(output.name)
                    try:
                        os.replace(temporary, target)
                    finally:
                        temporary.unlink(missing_ok=True)
                written.append((target, original))
            report = [{key: value for key, value in item.items() if key != 'payload'}
                      for item in items]
            (backup / 'report.json').write_text(
                json.dumps(report, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
        except BaseException:
            for target, original in reversed(written):
                if original is None:
                    target.unlink(missing_ok=True)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(original)
            raise

        reference_root = checker_root / 'OriginalXML'
        for item, target, _, _ in staged:
            if item['status'] != 'DELETE':
                continue
            parent = target.parent
            while parent.is_relative_to(reference_root) and parent != reference_root:
                try:
                    parent.rmdir()
                except OSError:
                    break
                parent = parent.parent
        return backup


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--config', default=str(ROOT / 'reference_sources.auto.json'))
    ap.add_argument('--steam', default='', help='Steam library, RimWorld or workshop directory')
    ap.add_argument('--no-github', action='store_true')
    ap.add_argument('--apply', action='store_true', help='Apply reviewed imports, updates and removals')
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
