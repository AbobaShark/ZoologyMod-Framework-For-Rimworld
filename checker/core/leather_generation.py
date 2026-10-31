"""Generate Zoology's existing leather XML from the AnimalStats Leathers tab.

Only paths declared by generation.json are emitted. Existing patch structure,
mod-presence predicates, race assignments, labels and non-leather operations are
kept. This is deliberately not a generic patch of every leather in every mod:
upstream materials without a Zoology override remain owned by their source mod.
"""
from __future__ import annotations

import math
import re
from pathlib import Path

import pandas as pd
from lxml import etree as E

from core.animalstats_source import is_multisheet_source
from core.static_stats import read_table

FIELDS = {
    "MarketValue": "MarketValue",
    "Beauty": "Beauty",
    "MaxHitPoints": "MaxHitPoints",
    "StuffPower_Armor_Sharp": "StuffPower_Armor_Sharp",
    "StuffPower_Armor_Blunt": "StuffPower_Armor_Blunt",
    "StuffPower_Insulation_Cold": "StuffPower_Insulation_Cold",
    "StuffPower_Insulation_Heat": "StuffPower_Insulation_Heat",
}
REQUIRED = ("Name", "Source mod", *FIELDS.values(), "Color hex", "CE Armor Sharp", "CE Armor Blunt")
# Explicit ownership is required. A material is never routed by guessing its
# provider from the first matching XML filename.
OWNERS = {"Core", "Odyssey", "AlphaAnimals", "Dinosauria", "Megafauna",
          "Vanilla Animals Expanded", "Vanilla Animals Expanded — Endangered",
          "Vanilla Animals Expanded — Waste Animals", "Zoology", "External"}
CE_COL = {"StuffPower_Armor_Sharp": "CE Armor Sharp", "StuffPower_Armor_Blunt": "CE Armor Blunt"}
CUSTOM_DEST = "Core/Defs/ThingDefs_Items/Items_Resource_Stuff_Leather_Zoology.xml"
CE_DEST = "ModPatches/CombatExtended/Patches/ThingDefs_Items/Items_Resource_Stuff_Leather_Zoology_CE.xml"
CUSTOM_NAMES = ("Leather_Beaver", "Leather_Ostrich", "Leather_Otter", "Leather_Squirrel")


def read_leathers(source):
    if not source:
        raise ValueError("Leathers: select a workbook, Google Sheet or Leathers.tsv")
    if Path(str(source)).suffix.lower() == ".tsv" and not is_multisheet_source(source):
        df = pd.read_csv(source, sep="\t", dtype=str, keep_default_na=False)
    else:
        df = read_table(source, "Leathers")
    missing = set(REQUIRED) - set(df.columns)
    if missing:
        raise ValueError(f"Leathers: missing columns: {sorted(missing)}")
    result = {}
    for index, row in df.iterrows():
        name = str(row["Name"]).strip()
        if not name or name.casefold() in ("no", "none"):
            continue
        if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", name):
            raise ValueError(f"Leathers: invalid defName on sheet row {index + 2}: {name!r}")
        if name in result:
            raise ValueError(f"Leathers: duplicate defName {name}")
        data = {}
        for column in (*FIELDS.values(), *CE_COL.values()):
            raw = str(row[column]).strip()
            if not raw:
                raise ValueError(f"Leathers: {name} has an empty {column}")
            try:
                n = float(raw.replace(",", "."))
            except ValueError as error:
                raise ValueError(f"Leathers: {name} invalid number {column}={raw!r}") from error
            if not math.isfinite(n) or n < 0:
                raise ValueError(f"Leathers: {name} invalid nonnegative number {column}={raw!r}")
            data[column] = n
        value = str(row["Color hex"]).strip().upper()
        if not re.fullmatch(r"#[0-9A-F]{6}", value):
            raise ValueError(f"Leathers: {name}: invalid Color hex {value!r}")
        data["Color hex"] = value
        owner = str(row["Source mod"]).strip()
        if owner not in OWNERS:
            raise ValueError(f"Leathers: {name} has unknown Source mod {owner!r} (row {index + 2})")
        if name in CUSTOM_NAMES and owner != "Zoology":
            raise ValueError(f"Leathers: {name} is a Zoology definition, not {owner}")
        if owner == "Zoology" and name not in CUSTOM_NAMES:
            raise ValueError(f"Leathers: {name} declares Zoology ownership but is not a registered custom definition")
        data["Source mod"] = owner
        result[name] = data
    if not result:
        raise ValueError("Leathers is empty")
    return result


def number(value):
    return format(value, ".12g")


def rgb(hex_value):
    return "(" + ",".join(str(int(hex_value[i:i + 2], 16)) for i in (1, 3, 5)) + ")"


def _field_value(data, field, ce):
    return data[CE_COL[field]] if ce and field in CE_COL else data[field]


def _set_path(parent, path, value):
    parts = path.split("/")
    node = parent
    for part in parts:
        nxt = node.find(part)
        if nxt is None:
            nxt = E.SubElement(node, part)
        node = nxt
    node.text = str(value)
    return node


def _write_thing_def(thing, rows, visited):
    name = thing.findtext("defName")
    if name not in rows:
        return
    data = rows[name]
    for field in FIELDS:
        path = ("stuffProps/statFactors/" if field in ("Beauty", "MaxHitPoints") else "statBases/") + field
        _set_path(thing, path, number(data[field]))
        visited.add((name, "vanilla", field))
    for path in ("graphicData/color", "stuffProps/color"):
        # Do not overwrite an independently configured graphicData.color if
        # the existing material uses two different explicit RGB values.
        if path == "graphicData/color":
            material_color = thing.findtext("stuffProps/color")
            drawing_color = thing.findtext(path)
            if material_color and drawing_color and material_color.strip() != drawing_color.strip():
                continue
        _set_path(thing, path, rgb(data["Color hex"]))
    visited.add((name, "vanilla", "Color hex"))


def _is_ce(operation):
    """Whether this patch operation is inside a CE FindMod.match branch."""
    node = operation.getparent()
    while node is not None:
        if node.get("Class") == "PatchOperationFindMod":
            names = [str(x).strip().lower() for x in node.xpath("./mods/li/text()")]
            if "combat extended" in names:
                match = operation
                while match is not None and match.getparent() is not node:
                    match = match.getparent()
                return match is not None and match.tag == "match"
        node = node.getparent()
    return False


def _update_operation(operation, rows, visited, ce_enabled):
    xpath = operation.findtext("xpath") or ""
    names = re.findall(r'''defName\s*=\s*["']([A-Za-z0-9_]+)["']''', xpath)
    names = [n for n in names if n in rows]
    if not names:
        return
    in_ce = _is_ce(operation)
    if in_ce and not ce_enabled:
        return
    # For Replace/Add/Conditional, touch only the value branches attached to
    # this operation itself (not nested child operations).
    nodes = list(operation.xpath("./value/* | ./match/value/* | ./nomatch/value/*"))
    for field in FIELDS:
        if in_ce and field not in CE_COL:
            continue
        if not (xpath.endswith("/" + field) or
                (xpath.endswith("/statBases") and field.startswith("StuffPower_") or
                 xpath.endswith("/statBases") and field == "MarketValue") or
                (xpath.endswith("/statFactors") and field in ("Beauty", "MaxHitPoints"))):
            continue
        relevant = [node for node in nodes if node.tag == field]
        if not relevant:
            continue
        expected_values = {number(_field_value(rows[name], field, in_ce)) for name in names}
        if len(expected_values) != 1:
            raise ValueError(f"Leathers: grouped XPath has conflicting sheet values for {field}: {names}")
        target = expected_values.pop()
        for node in relevant:
            node.text = target
        for name in names:
            visited.add((name, "ce" if in_ce else "vanilla", field))


def _render_patch(path, rows, ce_enabled, visited):
    tree = E.parse(str(path), E.XMLParser(remove_blank_text=False))
    root = tree.getroot()
    if root.tag != "Patch":
        raise ValueError(f"Leather template is not a Patch: {path}")
    for thing in root.xpath(".//ThingDef[defName]"):
        # Embedded fallback definitions: preserve parent, label and metadata.
        _write_thing_def(thing, rows, visited)
    for operation in root.iter():
        if (operation.get("Class") or "").startswith("PatchOperation"):
            _update_operation(operation, rows, visited, ce_enabled)
    if path.name == 'Items_Resource_Stuff_Leather_OdysseyFallback.xml':
        _refresh_odyssey_fallback_ce(root, rows, ce_enabled, visited)
    return E.tostring(root, xml_declaration=True, encoding="utf-8", pretty_print=False)



def _refresh_odyssey_fallback_ce(root, rows, enabled, visited):
    """CE operations run after Core fallbacks exist, only if Odyssey is absent.

    The material generator writes explicit Sharp/Blunt fields to its ThingDefs;
    inherited CE LeatherBase values no longer suffice. Each differing field
    is therefore replaced here inside the original Odyssey nomatch branch.
    """
    outer = root.find('Operation')
    if outer is None or outer.get('Class') != 'PatchOperationFindMod':
        raise ValueError('Unexpected Core Odyssey fallback structure')
    mods = outer.xpath('./mods/li/text()')
    if mods != ['Odyssey']:
        raise ValueError('Core fallback must be guarded by Odyssey absence')
    operations = outer.find('nomatch/operations')
    if operations is None:
        raise ValueError('Core fallback lacks Odyssey nomatch operations')
    # Replace only an earlier fallback-specific CE section, never other ops.
    for old in list(operations):
        if old.get('Class') == 'PatchOperationFindMod' and 'Combat Extended' in old.xpath('./mods/li/text()'):
            operations.remove(old)
    if not enabled:
        return
    ce = E.SubElement(operations, 'li', Class='PatchOperationFindMod')
    E.SubElement(E.SubElement(ce, 'mods'), 'li').text = 'Combat Extended'
    seq = E.SubElement(E.SubElement(ce, 'match', Class='PatchOperationSequence'), 'operations')
    for thing in root.xpath('.//ThingDef[defName]'):
        name = thing.findtext('defName')
        data = rows.get(name)
        if data is None:
            raise ValueError(f'Odyssey fallback {name} absent in Leathers')
        for field, col in CE_COL.items():
            visited.add((name, 'ce', field))
            if abs(data[field] - data[col]) <= 1e-12:
                continue
            xpath = f'/Defs/ThingDef[defName="{name}"]/statBases/{field}'
            op = E.SubElement(seq, 'li', Class='PatchOperationReplace')
            E.SubElement(op, 'xpath').text = xpath
            E.SubElement(E.SubElement(op, 'value'), field).text = number(data[col])
    if not len(seq):
        operations.remove(ce)

def _custom_defs(template, rows, visited):
    tree = E.parse(str(template), E.XMLParser(remove_blank_text=True))
    root = tree.getroot()
    if root.tag != "Defs":
        raise ValueError(f"Leather template must contain Defs: {template}")
    names = {t.findtext("defName") for t in root.findall("ThingDef")}
    if names != set(CUSTOM_NAMES):
        raise ValueError(f"Leather template must define exactly {sorted(CUSTOM_NAMES)}, found {sorted(names)}")
    for name in CUSTOM_NAMES:
        if name not in rows:
            raise ValueError(f"Missing {name} from Leathers sheet")
    for thing in root.findall("ThingDef"):
        _write_thing_def(thing, rows, visited)
    return E.tostring(root, xml_declaration=True, encoding="utf-8", pretty_print=True)


def _custom_ce(rows, enabled, visited):
    root = E.Element("Patch")
    if not enabled:
        return E.tostring(root, xml_declaration=True, encoding="utf-8", pretty_print=True)
    for name in CUSTOM_NAMES:
        data = rows[name]
        if all(abs(data[field] - data[ce_key]) < 1e-12 for field, ce_key in CE_COL.items()):
            for field in CE_COL:
                visited.add((name, "ce", field))
            continue
        # New standalone ThingDefs explicitly declare both armor fields;
        # use Replace, not Add, so the operation works in CE as loaded.
        for field, col in CE_COL.items():
            if abs(data[field] - data[col]) < 1e-12:
                visited.add((name, "ce", field))
                continue
            op = E.SubElement(root, "Operation", Class="PatchOperationConditional")
            xpath = f'/Defs/ThingDef[defName="{name}"]/statBases/{field}'
            E.SubElement(op, "xpath").text = xpath
            replacement = E.SubElement(E.SubElement(op, "match", Class="PatchOperationReplace"), "xpath")
            replacement.text = xpath
            target = E.SubElement(op.find("match"), "value")
            E.SubElement(target, field).text = number(data[col])
            visited.add((name, "ce", field))
    return E.tostring(root, xml_declaration=True, encoding="utf-8", pretty_print=True)


def _trim_core_manual(root_bytes, rows, refs, ce):
    """Prune only stat changes already matching their original Vanilla/CE values.

    Existing non-stat operations and other mods' fallbacks remain untouched.
    A CE override must survive a Vanilla operation that would otherwise
    overwrite the same stat even when the desired CE value equals its baseline.
    """
    from core.leather_overrides import NUMERICS, CE_FIELDS, _equals
    root = E.fromstring(root_bytes)
    stat_paths = {value: key for key, value in NUMERICS.items()}
    def field_target(op):
        xpath = (op.findtext('xpath') or '').strip()
        match = re.search(r"""defName\s*=\s*['"]([A-Za-z0-9_]+)['"]""", xpath)
        name = match.group(1) if match else None
        field = stat_paths.get(xpath.split(']', 1)[-1].lstrip('/'))
        # The expression above isolates the suffix after the material selector.
        return name, field

    kept_vanilla = set()
    for op in list(root):
        if op.get('Class') != 'PatchOperationConditional':
            continue
        name, field = field_target(op)
        if name not in rows or not field or name not in refs.materials:
            continue
        group, original = refs.materials[name]
        current = refs.get_number(original, field, group=group)
        if _equals(current, rows[name][field]):
            root.remove(op)
        else:
            kept_vanilla.add((name, field))

    for wrapper in list(root):
        if wrapper.get('Class') != 'PatchOperationFindMod':
            continue
        mods = wrapper.xpath('./mods/li/text()')
        if 'Combat Extended' not in mods:
            continue
        if not ce:
            root.remove(wrapper)
            continue
        operations = wrapper.find('match/operations')
        if operations is None:
            continue
        for op in list(operations):
            name, field = field_target(op)
            if name not in rows or field not in CE_FIELDS:
                continue
            baseline = refs.ce_before.get(name, {}).get(field)
            desired = rows[name][CE_FIELDS[field]]
            if _equals(baseline, desired) and (name, field) not in kept_vanilla:
                operations.remove(op)
        if not any((x.get('Class') or '').startswith('PatchOperation') for x in operations):
            root.remove(wrapper)

    # Only still-emitted manual operations suppress generated source diffs.
    handwritten = set()
    for op in root.iter():
        if not (op.get('Class') or '').startswith('PatchOperation'):
            continue
        name, field = field_target(op)
        if name in rows and field in NUMERICS:
            handwritten.add((name, 'ce' if _is_ce(op) else 'vanilla', field))
    return E.tostring(root, encoding='utf-8', xml_declaration=True), handwritten


def render_leather_outputs(source, mod, entries, *, ce=True, template_dir=None, original_xml=None, original_patches=None):
    """Return (destination, bytes) and a coverage report; never write Zoology."""
    rows = read_leathers(source)
    mod = Path(mod)
    template_dir = Path(template_dir or Path(__file__).resolve().parent.parent / "templates")
    visited = set()
    # Every original material is compared against source XML. Core/CE
    # handcrafted overrides and fallback definitions are still authoritative
    # for their own targets; additional overrides cover all other originals.
    from core.leather_overrides import render_original_overrides, GROUP_ROUTES, _emit, LeatherReferences
    original_xml = Path(original_xml or Path(__file__).resolve().parent.parent / 'OriginalXML')
    original_patches = Path(original_patches or Path(__file__).resolve().parent.parent / 'OriginalPatches')
    refs = LeatherReferences(original_xml, original_patches, rows, ce=ce)
    generated_destinations = set(GROUP_ROUTES.values())
    installed_patch_entries = [x for x in entries if x['destination'] not in
                               generated_destinations | {CUSTOM_DEST, CE_DEST}]
    outputs_by_destination = {}
    core_handwritten = set()
    for entry in installed_patch_entries:
        dest = entry['destination']
        source_path = mod / dest
        if not source_path.is_file():
            raise ValueError(f'Leathers: original Zoology patch template missing: {source_path}')
        before = set(visited)
        rendered = _render_patch(source_path, rows, ce, visited)
        # Only the Core legacy stat operations compete with generated material
        # overrides. Suppress their no-op values, then re-index what remains.
        if dest == 'Core/Patches/Core/ThingDefs_Items/Items_Resource_Stuff_Leather.xml':
            rendered, core_handwritten = _trim_core_manual(rendered, rows, refs, ce)
            visited.difference_update(visited - before)
            visited.update(core_handwritten)
        outputs_by_destination[dest] = rendered
    custom_entry = next((e for e in entries if e['destination'] == CUSTOM_DEST), None)
    if custom_entry:
        source_path = mod / CUSTOM_DEST
        template = source_path if source_path.is_file() else template_dir / 'Items_Resource_Stuff_Leather_Zoology.xml'
        outputs_by_destination[CUSTOM_DEST] = _custom_defs(template, rows, visited)
    overrides, ce_root, ref_report = render_original_overrides(
        rows, original_xml, original_patches, ce=ce, refs=refs,
        skip_vanilla=core_handwritten, skip_ce=core_handwritten)
    for dest in generated_destinations:
        if any(e['destination'] == dest for e in entries):
            outputs_by_destination[dest] = overrides[dest]
    if any(e['destination'] == CE_DEST for e in entries):
        # Include custom definitions in the same CE-specific XML file.
        custom = E.fromstring(_custom_ce(rows, ce, visited))
        if ce:
            for op in list(ce_root): custom.append(op)
        outputs_by_destination[CE_DEST] = _emit(custom)
    expected = {e['destination'] for e in entries}
    if expected != set(outputs_by_destination):
        raise ValueError(f'Leathers: output mismatch {expected ^ set(outputs_by_destination)}')
    report = {
        'source':str(source),
        'materials_in_sheet':len(rows),
        'existing_zoology_targets_updated':sorted({n for n, _, _ in visited}),
        'extra_overrides':ref_report,
        'warning':'Odyssey fallbacks are Core-owned and unconditional on VAE. Original overrides remain source-dependent; reference snapshots are required for comparison.'
    }
    return [(e['destination'],outputs_by_destination[e['destination']]) for e in entries], report
