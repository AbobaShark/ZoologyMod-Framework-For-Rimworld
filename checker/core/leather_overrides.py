"""Source-aware leather overrides, independent of animal race patches.

OriginalXML provides the baseline. OriginalPatches/CE provides the CE
baseline; no undocumented numeric defaults are inferred when sources are absent.
"""
from __future__ import annotations

import copy
from collections import defaultdict
from pathlib import Path
import re

from lxml import etree as E

from core.rimworld_patch_apply import PatchApplier

NUMERICS = {
    'MarketValue': 'statBases/MarketValue',
    'StuffPower_Armor_Sharp': 'statBases/StuffPower_Armor_Sharp',
    'StuffPower_Armor_Blunt': 'statBases/StuffPower_Armor_Blunt',
    'StuffPower_Insulation_Cold': 'statBases/StuffPower_Insulation_Cold',
    'StuffPower_Insulation_Heat': 'statBases/StuffPower_Insulation_Heat',
    'Beauty': 'stuffProps/statFactors/Beauty',
    'MaxHitPoints': 'stuffProps/statFactors/MaxHitPoints',
}
CE_FIELDS = {'StuffPower_Armor_Sharp': 'CE Armor Sharp', 'StuffPower_Armor_Blunt': 'CE Armor Blunt'}
GROUP_ROUTES = {
    'Core': 'Core/Patches/Core/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml',
    'Odyssey': 'DLC/Odyssey/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml',
    'AlphaAnimals': 'ModPatches/AlphaAnimals/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml',
    'Dinosauria': 'ModPatches/Dinosauria/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml',
    'Megafauna': 'ModPatches/Megafauna/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml',
    'Vanilla Animals Expanded': 'ModPatches/VanillaAnimalsExpanded/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml',
    'Vanilla Animals Expanded — Endangered': 'ModPatches/VanillaAnimalsExpandedEndangered/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml',
    'Vanilla Animals Expanded — Waste Animals': 'ModPatches/VanillaAnimalsExpandedWasteland/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Generated.xml',
}


def _numeric(text):
    if text is None or not str(text).strip():
        return None
    try:
        return float(str(text).strip().replace(',', '.'))
    except ValueError:
        return None


def _equals(actual, desired):
    return actual is not None and abs(actual - desired) <= 1e-9


def _xml_color(text):
    if not text: return None
    s = str(text).strip().strip('()')
    parts = [x.strip() for x in s.split(',')]
    if len(parts) != 3: return None
    try:
        values = [int(x) for x in parts]
    except ValueError: return None
    if any(x < 0 or x > 255 for x in values): return None
    return '#'+''.join(f'{x:02X}' for x in values)


def _rgb(value):
    return '('+','.join(str(int(value[k:k+2], 16)) for k in (1,3,5))+')'


def _source_priority(path):
    # Current-version synchronized snapshots outrank legacy manual copies.
    parts = [p.casefold() for p in path.parts]
    is_sync = int('_leathers' in parts)
    versions = [p.lstrip('v') for p in parts if re.fullmatch(r'v?1\.\d+', p)]
    # Source version outranks storage location. Stale 1.6 snapshots cannot
    # displace a newer 1.7 reference merely because they are in _Leathers.
    version = 300 if '1.7' in versions else (100 if versions else 200)
    return (version, is_sync)


class LeatherReferences:
    def __init__(self, originals, patches, rows, *, ce=True):
        self.originals = Path(originals)
        self.patches = Path(patches)
        self.names = set(rows)
        self.expected_owner = {name: data.get('Source mod') for name, data in rows.items()}
        self.strict_ownership = any(self.expected_owner.values())
        self.include_ce = ce
        self.materials = {}  # name => (group, source node)
        self.parents = {}
        parent_priority = {}
        self.duplicates = []
        self.ce_before = {}
        self.ce_available = False
        self.warnings = []
        self._load()

    def _load(self):
        options = defaultdict(list)
        other_sources = defaultdict(set)
        if not self.originals.is_dir():
            raise ValueError('OriginalXML not found: synchronize material references before generating leathers')
        for path in sorted(self.originals.rglob('*.xml')):
            rel = path.relative_to(self.originals)
            if len(rel.parts) < 2: continue
            group = rel.parts[0]
            if group not in GROUP_ROUTES: continue
            try:
                root = E.parse(str(path), E.XMLParser(resolve_entities=False, no_network=True, remove_blank_text=True)).getroot()
            except (OSError, E.XMLSyntaxError) as exc:
                self.warnings.append(f'{path}: {exc}')
                continue
            if root.tag != 'Defs': continue
            for thing in root.findall('ThingDef'):
                name = thing.findtext('defName')
                parent = thing.get('Name')
                if name in self.names and self.expected_owner.get(name) not in ('Zoology', 'External'):
                    # A source snapshot may contain definitions from another
                    # mod (or a legacy reference file). Only the explicitly
                    # declared owner is authorized to provide the baseline.
                    if group == self.expected_owner[name]:
                        options[name].append((group, path, copy.deepcopy(thing)))
                    else:
                        other_sources[name].add(group)
                if parent and (parent == 'LeatherBase' or parent.startswith('Leather')):
                    for key in ([(group, parent), (None, parent)] if group == 'Core'
                                else [(group, parent)]):
                        priority = _source_priority(path)
                        if key not in self.parents or priority > parent_priority[key]:
                            self.parents[key] = copy.deepcopy(thing)
                            parent_priority[key] = priority
                        elif priority == parent_priority[key] and E.tostring(self.parents[key], method='c14n') != E.tostring(thing, method='c14n'):
                            raise ValueError(f'Conflicting original {parent} definitions for {group}')
        for name, entries in options.items():
            # All entries here belong to the declared owner. Compare only
            # top-ranked snapshots of this owner; older versions may differ.
            entries.sort(key=lambda e: _source_priority(e[1]), reverse=True)
            first_group, first_path, first_node = entries[0]
            if len(entries) > 1 and any(_source_priority(item[1]) == _source_priority(first_path)
                    and E.tostring(item[2], method='c14n') != E.tostring(first_node, method='c14n')
                    for item in entries[1:]):
                self.duplicates.append(name)
                continue
            self.materials[name] = (first_group, first_node)
        if self.duplicates:
            raise ValueError('Ambiguous original leather definitions: '+', '.join(sorted(self.duplicates)))
        unresolved = []
        for name, owner in sorted(self.expected_owner.items()):
            if owner in (None, 'Zoology', 'External') or name in self.materials:
                continue
            locations = sorted(other_sources.get(name, ()))
            if locations:
                unresolved.append(
                    f'{name}: Source mod {owner!r}, but original ThingDef found '
                    f'under {", ".join(locations)}')
            else:
                unresolved.append(
                    f'{name} ({owner}): missing from OriginalXML/{owner}')
        if unresolved:
            raise ValueError(
                'Leathers: original material ownership/reference validation failed '
                f'for {len(unresolved)} material(s):\\n'
                + '\\n'.join(unresolved)
                + '\\nVerify Source mod in the local workbook (download a '
                  'fresh AnimalStats copy if stale), then update OriginalXML '
                  'with Reference Sync and apply the reviewed changes.')
        if not self.materials and not self.strict_ownership:
            raise ValueError('No original leather ThingDefs in OriginalXML. Run reference_sync_gui.py to import upstream leather XML before generating.')
        if self.include_ce:
            self._load_ce()

    def _load_ce(self):
        # Simulate the CE leather XML against copies of original material defs.
        # All source XML is used, not Zoology fallback definitions.
        if not self.patches.is_dir(): return
        ce_root = self.patches/'CE'/'_Leathers'
        discovered = sorted(ce_root.rglob('*.xml')) if ce_root.is_dir() else []
        selected = {}
        for path in discovered:
            relative = path.relative_to(ce_root)
            identity = tuple(p for p in relative.parts if not re.fullmatch(r'v?1\.\d+', p.casefold()))
            existing = selected.get(identity)
            if existing is None or _source_priority(path) > _source_priority(existing):
                selected[identity] = path
        paths = sorted(selected.values())
        if not paths:
            if self.strict_ownership and self.materials:
                raise ValueError('Leathers: CE material references are missing. '
                                 'Synchronize OriginalPatches/CE/_Leathers or use --no-ce.')
            return
        root = E.Element('Defs')
        for (_, parent), node in self.parents.items():
            if parent == 'LeatherBase' and node not in root:
                if not root.xpath('./ThingDef[@Name="LeatherBase"]'):
                    root.append(copy.deepcopy(node))
        for _, thing in self.materials.values(): root.append(copy.deepcopy(thing))
        # PatchApplier is also the evaluator used by the existing OriginalXML optimizer.
        errors = []
        apply = PatchApplier(active_mods=['Combat Extended', 'Alpha Animals', 'Odyssey',
            'Vanilla Animals Expanded', 'Vanilla Animals Expanded — Endangered',
            'Vanilla Animals Expanded — Waste Animals', 'Dinosauria', 'Megafauna'],
            errors=errors, record_missing=False)
        loaded = 0
        leather_base_patched = False
        for path in paths:
            try:
                doc = E.parse(str(path), E.XMLParser(resolve_entities=False, no_network=True)).getroot()
                if doc.tag != 'Patch': continue
                leather_base_patched |= any('LeatherBase' in (x.text or '') for x in doc.iter('xpath'))
                apply.apply_patch_root(doc, root, source_path=str(path))
                loaded += 1
            except (OSError, E.XMLSyntaxError) as exc:
                self.warnings.append(f'Invalid CE reference {path}: {exc}')
        self.ce_available = loaded > 0
        if errors:
            self.warnings.append(f'CE reference simulation: {len(errors)} unapplied operations (unrelated targets may be absent)')
        base = root.xpath('./ThingDef[@Name="LeatherBase"]')
        ce_base = base[0] if base else None
        if self.strict_ownership and self.materials and (not self.ce_available or ce_base is None or not leather_base_patched):
            raise ValueError('Leathers: CE LeatherBase baseline is unavailable; synchronize CE references.')
        ce_parents = dict(self.parents)
        if ce_base is not None:
            ce_parents[('Core', 'LeatherBase')] = ce_base
            ce_parents[(None, 'LeatherBase')] = ce_base
        for thing in root.findall('ThingDef'):
            name = thing.findtext('defName')
            if name in self.materials:
                group = self.materials[name][0]
                self.ce_before[name] = {field: self.get_number(
                    thing, field, group=group, parents=ce_parents, base=ce_base) for field in CE_FIELDS}

    def get_number(self, thing, field, *, group=None, parents=None, base=None):
        path = NUMERICS[field]
        current = thing
        seen = set()
        parents = self.parents if parents is None else parents
        if group is None:
            group = next((g for g, item in self.materials.values() if item is thing), None)
        while current is not None:
            val = _numeric(current.findtext(path))
            if val is not None: return val
            parent = current.get('ParentName')
            if not parent or parent in seen: break
            seen.add(parent)
            current = parents.get((group, parent)) or parents.get((None, parent))
        if base is not None and base is not thing:
            return _numeric(base.findtext(path))
        # Multiplicative stat factors have the exact neutral value 1 when
        # unspecified in the ancestry (not a fitted coefficient).
        if field == 'Beauty':
            return 1.0
        return None

    def get_color(self, thing):
        group = next((g for g, item in self.materials.values() if item is thing), None)
        current = thing
        seen = set()
        while current is not None:
            color = _xml_color(current.findtext('stuffProps/color'))
            if color is not None:
                return color
            parent = current.get('ParentName')
            if not parent or parent in seen:
                break
            seen.add(parent)
            current = self.parents.get((group, parent)) or self.parents.get((None, parent))
        return None


def _conditional(root, name, path, value, original):
    """Only patch existing ThingDefs; add missing subnodes via runtime guards."""
    target = f'/Defs/ThingDef[defName="{name}"]'
    # Missing entire ThingDef must not make an operation fail.
    outer = E.SubElement(root, 'Operation', Class='PatchOperationConditional')
    E.SubElement(outer, 'xpath').text = target
    match = E.SubElement(outer, 'match', Class='PatchOperationSequence')
    seq = E.SubElement(match, 'operations')
    # Fields may be inherited and absent in the concrete ThingDef.
    fragments = path.split('/')
    for depth in range(1, len(fragments)):
        sub = '/'.join(fragments[:depth])
        existing = original.find(sub)
        if existing is not None: continue
        # Root lacks a container: add it with the full remaining branch.
        parent = '/'.join(fragments[:depth-1]); missing = '/'.join(fragments[depth-1:])
        dest = target + ('/'+parent if parent else '')
        op = E.SubElement(seq, 'li', Class='PatchOperationConditional')
        E.SubElement(op, 'xpath').text = target+'/'+sub
        no = E.SubElement(op, 'nomatch', Class='PatchOperationAdd')
        E.SubElement(no, 'xpath').text = dest
        val = E.SubElement(no, 'value')
        at = val
        for tag in fragments[depth-1:]: at = E.SubElement(at, tag)
        at.text = str(value)
        return
    cond = E.SubElement(seq, 'li', Class='PatchOperationConditional')
    E.SubElement(cond, 'xpath').text = target+'/'+path
    m = E.SubElement(cond, 'match', Class='PatchOperationReplace')
    E.SubElement(m, 'xpath').text = target+'/'+path
    E.SubElement(E.SubElement(m, 'value'), fragments[-1]).text = str(value)
    no = E.SubElement(cond, 'nomatch', Class='PatchOperationAdd')
    E.SubElement(no, 'xpath').text = target+'/'+ '/'.join(fragments[:-1])
    E.SubElement(E.SubElement(no, 'value'), fragments[-1]).text = str(value)


def _emit(root):
    return E.tostring(root, encoding='utf-8', xml_declaration=True, pretty_print=True)


def render_original_overrides(rows, original_xml, original_patches, *, ce=True, skip_vanilla=(), skip_ce=(), refs=None):
    """Return route-payload dict, CE payload, and comparison report.

    The original source group decides the route. No Odyssey-only material is
    patched when Odyssey is absent; the existing fallback owns that scenario.
    """
    refs = refs or LeatherReferences(original_xml, original_patches, rows, ce=ce)
    groups = {name: E.Element('Patch') for name in GROUP_ROUTES}
    ce_root = E.Element('Patch')  # Retained for custom-def CE output compatibility.
    ce_by_group = {name: E.Element('Patch') for name in GROUP_ROUTES}
    no_reference = []
    vanilla_changes = []
    ce_changes = []
    ce_unverified = []
    for name, sheet in sorted(rows.items()):
        if name not in refs.materials:
            if sheet.get('Source mod') not in ('Zoology', 'External', None):
                raise ValueError(f'Leathers: missing original material {name} from {sheet["Source mod"]}')
            no_reference.append(name)
            continue
        group, original = refs.materials[name]
        for field in NUMERICS:
            if (name, 'vanilla', field) in skip_vanilla: continue
            old = refs.get_number(original, field)
            if old is None:
                if refs.strict_ownership:
                    raise ValueError(f'Leathers: cannot resolve original {name} {field}')
                refs.warnings.append(f'{name}: cannot resolve original {field}; skipped')
                continue
            new = sheet[field]
            if not _equals(old, new):
                _conditional(groups[group], name, NUMERICS[field], format(new,'.12g'), original)
                vanilla_changes.append((name, field, old, new, group))
        if (name, 'vanilla', 'Color hex') not in skip_vanilla:
            old = refs.get_color(original)
            if old is None and refs.strict_ownership:
                raise ValueError(f'Leathers: cannot resolve original {name} stuffProps/color')
            if old is not None and old != sheet['Color hex']:
                _conditional(groups[group], name, 'stuffProps/color', _rgb(sheet['Color hex']), original)
                # If graphicData has its own independent color, preserve it.
                if _xml_color(original.findtext('graphicData/color')) == old:
                    _conditional(groups[group], name, 'graphicData/color', _rgb(sheet['Color hex']), original)
                vanilla_changes.append((name, 'Color hex', old, sheet['Color hex'], group))
        if not ce:
            continue
        for field, col in CE_FIELDS.items():
            if (name,'ce',field) in skip_ce: continue
            new = sheet[col]
            reference = refs.ce_before.get(name, {}).get(field)
            # If source CE patches aren't present, only force a value when
            # the desired CE value differs from the Sheet vanilla value.
            known = refs.ce_available and reference is not None
            vanilla_touch = ((name, 'vanilla', field) in skip_vanilla or
                             any(n == name and f == field for n, f, *_ in vanilla_changes))
            if not known:
                ce_unverified.append((name, field))
                if refs.strict_ownership:
                    raise ValueError(f'Leathers: cannot verify CE {field} for {name} against OriginalPatches')
            if (known and _equals(reference, new) and not vanilla_touch) or (
                not known and _equals(sheet[field], new) and not vanilla_touch):
                continue
            _conditional(ce_by_group[group], name, NUMERICS[field], format(new,'.12g'), original)
            ce_changes.append((name, field, reference, new, group))
    # Apply CE last in the same source-specific patch, AFTER its vanilla
    # override. A global CE file in ModPatches/CombatExtended would run before
    # ModPatches/AlphaAnimals or ModPatches/Dinosauria, and lose that ordering.
    for group, patch in ce_by_group.items():
        if not len(patch): continue
        wrapper = E.SubElement(groups[group], 'Operation', Class='PatchOperationFindMod')
        E.SubElement(E.SubElement(wrapper, 'mods'), 'li').text = 'Combat Extended'
        sequence = E.SubElement(wrapper, 'match', Class='PatchOperationSequence')
        operations = E.SubElement(sequence, 'operations')
        for original_op in list(patch):
            original_op.tag = 'li'
            operations.append(original_op)
    report = {
        'original_materials_indexed': len(refs.materials),
        'material_routes': {name: GROUP_ROUTES[group] for name, (group, _) in sorted(refs.materials.items())},
        'excluded_materials': {name: sheet.get('Source mod') for name, sheet in sorted(rows.items())
                               if sheet.get('Source mod') in ('Zoology', 'External')},
        'missing_originals': no_reference,
        'vanilla_overrides': vanilla_changes,
        'ce_overrides': ce_changes,
        'ce_unverified_fields': ce_unverified,
        'warnings': refs.warnings,
    }
    return {GROUP_ROUTES[group]: _emit(root) for group, root in groups.items()}, ce_root, report
