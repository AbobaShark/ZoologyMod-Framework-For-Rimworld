"""Inventory the pre-implementation workspace; never mutate mod files."""
from pathlib import Path
import hashlib
import json
import subprocess
from collections import Counter
from lxml import etree as E

root = Path(__file__).resolve().parents[2]
mod = root/'Zoology'
out = Path(__file__).resolve().parent
load = E.parse(str(mod/'LoadFolders.xml'))
folders = {}
for version in load.getroot():
    folders[version.tag] = [(x.text, x.get('IfModActive')) for x in version if x.text]
entries = []
for path in sorted(mod.rglob('*.xml')):
    if 'Source' in path.parts or 'obj' in path.parts:
        continue
    rel = path.relative_to(mod).as_posix()
    xml = E.parse(str(path)).getroot()
    kind = 'defs' if xml.tag == 'Defs' else 'patch' if xml.tag == 'Patch' else 'localization' if rel.startswith('Languages/') else 'metadata'
    rules = {}
    for version, items in folders.items():
        # The root item loads assemblies/metadata; the explicit nested content roots
        # are classified separately so conditional DLC/mod ownership is visible.
        rules[version] = [{'folder': f, 'requires': req} for f, req in items if f != '/' and (rel == f or rel.startswith(f+'/'))]
        if rel.startswith('Languages/') or rel.startswith('About/') or rel == 'LoadFolders.xml':
            rules[version].append({'folder': '/', 'requires': None})
    same_name = sorted(str(p.relative_to(root)) for p in (root/'checker/generated_patches').rglob(path.name))
    entries.append({'path': rel, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'kind': kind,
                    'load_rules': rules, 'definitions': [n.findtext('defName') for n in xml if isinstance(n.tag,str) and n.findtext('defName')] if kind=='defs' else [],
                    'checker_same_name_candidates': same_name,
                    'ownership': 'unproven; retain until specifically established',
                    'operations': len(xml.xpath('.//*[@Class]'))})
status = subprocess.check_output(['git','status','--short'], cwd=root, text=True)
(out/'initial-status.txt').write_text(status, encoding='utf-8')
(out/'xml-inventory.json').write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding='utf-8')
print('Inventoried', len(entries), 'XML files:', dict(Counter(x['kind'] for x in entries)))
