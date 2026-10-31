"""Generate source-differential wool and silk patches from Animal textiles.

OriginalXML owns material definitions; this generator never synthesizes ThingDefs.
Reference Sync maintains the exact upstream snapshots, including CE patches.
"""
from __future__ import annotations

from collections import defaultdict
import copy
import csv
import math
from pathlib import Path
import re

from lxml import etree as E

from core.animalstats_source import is_multisheet_source
from core.static_stats import read_table
from core.leather_overrides import GROUP_ROUTES, _conditional, _emit
from core.rimworld_patch_apply import PatchApplier

FIELDS = {
    'MarketValue': 'statBases/MarketValue',
    'Beauty': 'stuffProps/statFactors/Beauty',
    'MaxHitPoints factor': 'stuffProps/statFactors/MaxHitPoints',
    'StuffPower_Armor_Sharp': 'statBases/StuffPower_Armor_Sharp',
    'StuffPower_Armor_Blunt': 'statBases/StuffPower_Armor_Blunt',
    'StuffPower_Armor_Heat': 'statBases/StuffPower_Armor_Heat',
    'StuffPower_Insulation_Cold': 'statBases/StuffPower_Insulation_Cold',
    'StuffPower_Insulation_Heat': 'statBases/StuffPower_Insulation_Heat',
    'Flammability factor': 'stuffProps/statFactors/Flammability',
}
CE_FIELDS = {
    'StuffPower_Armor_Sharp': 'CE Armor Sharp',
    'StuffPower_Armor_Blunt': 'CE Armor Blunt',
    'StuffPower_Armor_Heat': 'CE Armor Heat',
}
ROUTES = {
    group: path.replace('Items_Resource_Stuff_Leather_Generated.xml',
                        'Items_Resource_Stuff_AnimalTextiles_Generated.xml')
    for group, path in GROUP_ROUTES.items()
    if group in ('Core', 'Odyssey', 'AlphaAnimals', 'Megafauna',
                 'Vanilla Animals Expanded')
}
REQUIRED = ('Name', 'Type', 'Source mod', *FIELDS, 'Color hex',
            *CE_FIELDS.values())


def read_textiles(source):
    if not source:
        raise ValueError('Animal textiles: a workbook or TSV source is required')
    if Path(str(source)).suffix.lower() == '.tsv' and not is_multisheet_source(source):
        with Path(source).open(encoding='utf-8-sig', newline='') as stream:
            records = list(csv.DictReader(stream, delimiter='\t'))
    else:
        records = read_table(source, 'Animal textiles').to_dict('records')
    if not records:
        raise ValueError('Animal textiles: empty table')
    missing = set(REQUIRED) - set(records[0])
    if missing:
        raise ValueError('Animal textiles: missing columns: ' + ', '.join(sorted(missing)))
    results = {}
    for index, row in enumerate(records, 2):
        name = str(row['Name'] or '').strip()
        if not name or name.lower() in ('none', 'no', 'nan'):
            continue
        if not re.fullmatch(r'[A-Za-z][A-Za-z0-9_]*', name) or name in results:
            raise ValueError(f'Animal textiles: invalid or duplicate defName on row {index}: {name}')
        if row['Type'] not in ('Wool', 'Silk'):
            raise ValueError(f'Animal textiles: unexpected type for {name}: {row["Type"]}')
        group = str(row['Source mod']).strip()
        if group not in ROUTES:
            raise ValueError(f'Animal textiles: unregistered source group {group} for {name}')
        data = {'Source mod': group}
        for field in (*FIELDS, *CE_FIELDS.values()):
            raw = str(row[field]).strip().replace(',', '.')
            try:
                value = float(raw)
            except ValueError as exc:
                raise ValueError(f'Animal textiles: {name}, invalid {field}: {raw}') from exc
            if not math.isfinite(value) or value < 0:
                raise ValueError(f'Animal textiles: {name}, invalid {field}: {raw}')
            data[field] = value
        color = str(row['Color hex']).strip().upper()
        if not re.fullmatch(r'#[0-9A-F]{6}', color):
            raise ValueError(f'Animal textiles: {name}, invalid color: {color}')
        data['Color hex'] = color
        results[name] = data
    if not results:
        raise ValueError('Animal textiles: no material definitions')
    return results


def _number(value):
    try:
        result = float(str(value).strip())
        return result if math.isfinite(result) else None
    except (ValueError, TypeError):
        return None


def _hex(value):
    try:
        values = [int(part) for part in str(value).strip(' ()').split(',')]
    except ValueError:
        return None
    if len(values) != 3 or any(not 0 <= c <= 255 for c in values):
        return None
    return '#' + ''.join(f'{v:02X}' for v in values)


def _rgb(hex_color):
    return '(' + ','.join(str(int(hex_color[i:i+2], 16)) for i in (1, 3, 5)) + ')'


def _same(a, b):
    return a is not None and abs(a - b) < 1e-9


def _version_rank(path):
    parts = [p.casefold() for p in path.parts]
    versions = [p.removeprefix('v') for p in parts if re.fullmatch(r'v?1\.\d+', p)]
    if '1.7' in versions:
        return 300
    if '1.6' in versions or any('1.6notodyssey' in p for p in parts):
        return 200
    if versions:
        return 0
    return 100


def _absent_mod_for_source(path):
    """Interpret upstream conditional source folders, never a defName allowlist.

    VAE LoadFolders loads 1.6NotOdyssey only if Odyssey is absent, in both
    RimWorld 1.6 and 1.7. This condition must survive the generated override.
    """
    pieces = (part.casefold() for part in Path(path).parts)
    return 'Odyssey' if any(part in ('1.6notodyssey', '1.7notodyssey',
                                    'noodyssey', 'notodyssey')
                            for part in pieces) else None


def _read_xml(path):
    return E.parse(str(path), E.XMLParser(resolve_entities=False, no_network=True, remove_blank_text=True)).getroot()


class TextileReferences:
    def __init__(self, originals, patches, rows, *, ce=True):
        self.originals = Path(originals)
        self.patches = Path(patches)
        self.rows = rows
        self.parents = {}
        self.materials = {}
        self.absent_mod = {}
        self.ce = {}
        self.ce_ready = False
        self.warnings = []
        self._load()

    def _load(self):
        candidates = defaultdict(list)
        for path in sorted(self.originals.rglob('*.xml')):
            relative = path.relative_to(self.originals)
            if len(relative.parts) < 2 or relative.parts[0] not in ROUTES:
                continue
            group = relative.parts[0]
            try:
                root = _read_xml(path)
            except (OSError, E.XMLSyntaxError) as exc:
                self.warnings.append(f'{path}: {exc}')
                continue
            if root.tag != 'Defs':
                continue
            rank = (_version_rank(relative), int('_Leathers' in relative.parts))
            for node in root.findall('ThingDef'):
                abstract = node.get('Name')
                if abstract:
                    key = (group, abstract)
                    entry = (rank, copy.deepcopy(node))
                    previous = self.parents.get(key)
                    if previous is None or entry[0] > previous[0]:
                        self.parents[key] = entry
                    elif entry[0] == previous[0] and E.tostring(entry[1], method='c14n') != E.tostring(previous[1], method='c14n'):
                        raise ValueError(f'Animal textiles: conflicting original abstract {abstract} in {group}')
                name = node.findtext('defName')
                if name in self.rows and self.rows[name]['Source mod'] == group:
                    candidates[name].append((rank, copy.deepcopy(node), str(path)))
        for name, entries in candidates.items():
            entries.sort(key=lambda entry: entry[0], reverse=True)
            top = entries[0]
            if any(e[0] == top[0] and E.tostring(e[1], method='c14n') != E.tostring(top[1], method='c14n')
                   for e in entries[1:]):
                raise ValueError(f'Animal textiles: ambiguous original ThingDef {name}')
            self.materials[name] = top[1]
            condition = _absent_mod_for_source(top[2])
            if condition:
                self.absent_mod[name] = condition
        missing = sorted(set(self.rows) - set(self.materials))
        if missing:
            raise ValueError('Animal textiles: missing original ThingDefs: ' + ', '.join(missing)
                             + '. Update OriginalXML using Reference Sync.')
        if ce:
            self._load_ce()

    def value(self, name, field, *, concrete=None, parents=None):
        node = concrete if concrete is not None else self.materials[name]
        group = self.rows[name]['Source mod']
        chain = parents if parents is not None else self.parents
        visited = set()
        while node is not None:
            number = _number(node.findtext(FIELDS[field]))
            if number is not None:
                return number
            parent = node.get('ParentName')
            if not parent or parent in visited:
                break
            visited.add(parent)
            selected = chain.get((group, parent)) or chain.get(('Core', parent))
            node = selected[1] if selected is not None else None
        # Unspecified stat multipliers have identity value, not zero.
        if field in ('Beauty', 'MaxHitPoints factor', 'Flammability factor'):
            return 1.0
        # Core Stuff stat definitions use zero as the default for absent
        # armor/insulation material factors; no guessed fit is required.
        if field.startswith('StuffPower_'):
            return 0.0
        return None

    def color(self, name):
        node = self.materials[name]
        group = self.rows[name]['Source mod']
        visited = set()
        while node is not None:
            result = _hex(node.findtext('stuffProps/color'))
            if result:
                return result
            parent = node.get('ParentName')
            if not parent or parent in visited:
                break
            visited.add(parent)
            entry = self.parents.get((group, parent)) or self.parents.get(('Core', parent))
            node = entry[1] if entry else None
        return None

    def _load_ce(self):
        rootdir = self.patches
        if not rootdir.is_dir():
            return
        # Choose one upstream version for each logical CE patch.
        selected = {}
        for path in sorted(rootdir.rglob('*.xml')):
            relative = path.relative_to(rootdir)
            logical = tuple(part for part in relative.parts
                            if part.casefold() not in ('1.6', '1.7', 'v1.6', 'v1.7'))
            rank = _version_rank(relative)
            old = selected.get(logical)
            if old is None or rank > old[0]:
                selected[logical] = (rank, path)
        patches = []
        for _, path in sorted(selected.values(), key=lambda item: str(item[1])):
            try:
                root = _read_xml(path)
            except (OSError, E.XMLSyntaxError) as exc:
                self.warnings.append(f'{path}: {exc}')
                continue
            if root.tag == 'Patch':
                patches.append((path, root))
        if not any('WoolBase' in ' '.join(root.xpath('//xpath/text()')) for _, root in patches):
            return

        all_mods = ['Combat Extended', 'Alpha Animals', 'Odyssey',
                    'Megafauna', 'Vanilla Animals Expanded']

        def simulated(mods):
            sim = E.Element('Defs')
            for (group, _), (_, node) in self.parents.items():
                if group == 'Core':
                    sim.append(copy.deepcopy(node))
            for node in self.materials.values():
                sim.append(copy.deepcopy(node))
            applier = PatchApplier(active_mods=mods, record_missing=False)
            for path, root in patches:
                applier.apply_patch_root(root, sim, source_path=str(path))
            parents = dict(self.parents)
            for abstract in sim.xpath('./ThingDef[@Name]'):
                key = ('Core', abstract.get('Name'))
                if key in parents:
                    parents[key] = (parents[key][0], abstract)
            return sim, parents

        with_odyssey = simulated(all_mods)
        without_odyssey = None
        if 'Odyssey' in self.absent_mod.values():
            without_odyssey = simulated([m for m in all_mods if m != 'Odyssey'])
        for name in self.materials:
            sim, parents = (without_odyssey if name in self.absent_mod
                            else with_odyssey)
            match = sim.xpath('./ThingDef[defName=$name]', name=name)
            if match:
                self.ce[name] = {
                    field: self.value(name, field, concrete=match[0],
                                      parents=parents)
                    for field in CE_FIELDS
                }
        self.ce_ready = True

def render_textile_outputs(source, entries, *, original_xml, original_patches, ce=True):
    rows = read_textiles(source)
    refs = TextileReferences(original_xml, original_patches, rows, ce=ce)
    if ce and not refs.ce_ready:
        raise ValueError('Animal textiles: missing CE WoolBase reference in OriginalXML/CEPatches. '
                         'Synchronize references or explicitly generate with --no-ce.')
    outputs = {group: E.Element('Patch') for group in ROUTES}
    ce_outputs = {group: E.Element('Patch') for group in ROUTES}
    # Mod-absence conditions belong to the source directory in upstream
    # LoadFolders, not to the material name or the entire VAE patch folder.
    conditional = {}
    vanilla = []
    ce_changes = []
    unknown = []
    for name, row in sorted(rows.items()):
        original = refs.materials[name]
        group = row['Source mod']
        absent = refs.absent_mod.get(name)
        if absent:
            targets = conditional.setdefault((group, absent),
                            (E.Element('Patch'), E.Element('Patch')))
            vanilla_target, ce_target = targets
        else:
            vanilla_target, ce_target = outputs[group], ce_outputs[group]
        for field, xpath in FIELDS.items():
            existing = refs.value(name, field)
            if existing is None:
                unknown.append(f'{name}: {field}')
                continue
            if not _same(existing, row[field]):
                _conditional(vanilla_target, name, xpath, format(row[field], '.12g'), original)
                vanilla.append((name, field, existing, row[field]))
        color = refs.color(name)
        if color != row['Color hex']:
            # If the source leaves color unspecified, write the explicit
            # spreadsheet color; do not assume graphicData uses that color.
            _conditional(vanilla_target, name, 'stuffProps/color', _rgb(row['Color hex']), original)
            if color is not None and _hex(original.findtext('graphicData/color')) == color:
                _conditional(vanilla_target, name, 'graphicData/color', _rgb(row['Color hex']), original)
            vanilla.append((name, 'Color hex', color, row['Color hex']))
        if ce:
            for field, column in CE_FIELDS.items():
                baseline = refs.ce.get(name, {}).get(field)
                if baseline is None:
                    unknown.append(f'{name}: CE {field}')
                    continue
                was_overridden = any(v[0] == name and v[1] == field for v in vanilla)
                if was_overridden or not _same(baseline, row[column]):
                    _conditional(ce_target, name, FIELDS[field],
                                 format(row[column], '.12g'), original)
                    ce_changes.append((name, field, baseline, row[column]))
    def attach_ce(root, ce_operations):
        if not len(ce_operations):
            return
        wrapper = E.SubElement(root, 'Operation', Class='PatchOperationFindMod')
        E.SubElement(E.SubElement(wrapper, 'mods'), 'li').text = 'Combat Extended'
        target = E.SubElement(E.SubElement(wrapper, 'match', Class='PatchOperationSequence'),
                              'operations')
        for op in list(ce_operations):
            op.tag = 'li'
            target.append(op)

    for group in ROUTES:
        attach_ce(outputs[group], ce_outputs[group])
    for (group, absent), (operations, ce_operations) in conditional.items():
        attach_ce(operations, ce_operations)
        if not len(operations):
            continue
        wrapper = E.SubElement(outputs[group], 'Operation', Class='PatchOperationFindMod')
        E.SubElement(E.SubElement(wrapper, 'mods'), 'li').text = absent
        # Unlike ordinary VAE patches, these materials exist only when the
        # optional DLC is missing (e.g. 1.6NotOdyssey).
        seq = E.SubElement(E.SubElement(wrapper, 'nomatch',
                                       Class='PatchOperationSequence'), 'operations')
        for op in list(operations):
            op.tag = 'li'
            seq.append(op)
    expected = set(ROUTES.values())
    actual = {entry['destination'] for entry in entries}
    if actual != expected:
        raise ValueError(f'Animal textiles: registry mismatch: {expected ^ actual}')
    report = {
        'materials_in_sheet': len(rows),
        'originals_available': len(refs.materials),
        'vanilla_overrides': vanilla,
        'ce_overrides': ce_changes,
        'unknown_original_values': unknown,
        'source_warnings': refs.warnings,
        'absent_mod_guards': dict(sorted(refs.absent_mod.items())),
    }
    if unknown:
        raise ValueError('Animal textiles: unresolved original material stats: '
                         + ', '.join(unknown) + '. Check synchronized XML/CE patches.')
    return [(entry['destination'],
             _emit(outputs[next(group for group, dest in ROUTES.items()
                                if dest == entry['destination'])]))
            for entry in entries], report
