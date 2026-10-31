"""Leathers: original-source-differential generation without manual templates.

Only four Zoology-native ThingDefs are stored in Core/Defs; the remaining
materials are updated through generated source-owned vanilla and CE patches.
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


def _write_thing_def(thing, rows):
    name = thing.findtext("defName")
    if name not in rows:
        return
    data = rows[name]
    for field in FIELDS:
        path = ("stuffProps/statFactors/" if field in ("Beauty", "MaxHitPoints") else "statBases/") + field
        _set_path(thing, path, number(data[field]))
    for path in ("graphicData/color", "stuffProps/color"):
        # Do not overwrite an independently configured graphicData.color if
        # the existing material uses two different explicit RGB values.
        if path == "graphicData/color":
            material_color = thing.findtext("stuffProps/color")
            drawing_color = thing.findtext(path)
            if material_color and drawing_color and material_color.strip() != drawing_color.strip():
                continue
        _set_path(thing, path, rgb(data["Color hex"]))


def _custom_defs(native_def_path, rows):
    tree = E.parse(str(native_def_path), E.XMLParser(remove_blank_text=True))
    root = tree.getroot()
    if root.tag != "Defs":
        raise ValueError(f"Zoology native leather definitions must contain Defs: {native_def_path}")
    names = {t.findtext("defName") for t in root.findall("ThingDef")}
    if names != set(CUSTOM_NAMES):
        raise ValueError(f"Zoology must define exactly {sorted(CUSTOM_NAMES)}, found {sorted(names)}")
    for name in CUSTOM_NAMES:
        if name not in rows:
            raise ValueError(f"Missing {name} from Leathers sheet")
    for thing in root.findall("ThingDef"):
        _write_thing_def(thing, rows)
    return E.tostring(root, xml_declaration=True, encoding="utf-8", pretty_print=True)


def _custom_ce(rows, enabled):
    root = E.Element("Patch")
    if not enabled:
        return E.tostring(root, xml_declaration=True, encoding="utf-8", pretty_print=True)
    for name in CUSTOM_NAMES:
        data = rows[name]
        if all(abs(data[field] - data[ce_key]) < 1e-12 for field, ce_key in CE_COL.items()):
            continue
        # New standalone ThingDefs explicitly declare both armor fields;
        # use Replace, not Add, so the operation works in CE as loaded.
        for field, col in CE_COL.items():
            if abs(data[field] - data[col]) < 1e-12:
                continue
            op = E.SubElement(root, "Operation", Class="PatchOperationConditional")
            xpath = f'/Defs/ThingDef[defName="{name}"]/statBases/{field}'
            E.SubElement(op, "xpath").text = xpath
            replacement = E.SubElement(E.SubElement(op, "match", Class="PatchOperationReplace"), "xpath")
            replacement.text = xpath
            target = E.SubElement(op.find("match"), "value")
            E.SubElement(target, field).text = number(data[col])
    return E.tostring(root, xml_declaration=True, encoding="utf-8", pretty_print=True)



def _cross_mod_consumers(rows, entries, source, animals_source):
    """Derive fallback ownership from Animals.MayRequire and Leathers.Source mod.

    Destinations and package IDs are supplied by the output registry; no
    animal-name, leather-name, or mod-specific allowlist is maintained.
    """
    if not animals_source and not is_multisheet_source(source):
        if any(row['Source mod'] == 'Odyssey' for row in rows.values()):
            raise ValueError('Leathers: Odyssey cross-mod material dependencies '
                             'require Animals sheet. Use a multisheet workbook '
                             'or pass --sheet Animals=<path-to-Animals.tsv>.')
        return {}
    selected = animals_source or source
    if Path(str(selected)).suffix.lower() == '.tsv' and not is_multisheet_source(selected):
        frame = pd.read_csv(selected, sep='\t', dtype=str, keep_default_na=False)
    else:
        frame = read_table(selected, 'Animals')
    required = {'LeatherDef', 'MayRequire'}
    if not required <= set(frame.columns):
        raise ValueError('Odyssey leather fallbacks require Animals columns: '
                         + ', '.join(sorted(required - set(frame.columns))))
    from core.leather_overrides import GROUP_ROUTES
    requirements = {str(entry.get('requires') or '').casefold(): group
                    for group, destination in GROUP_ROUTES.items()
                    for entry in entries if entry['destination'] == destination
                    and entry.get('requires')}
    consumers = {}
    for index, row in frame.iterrows():
        leather = str(row['LeatherDef']).strip()
        if leather not in rows or rows[leather]['Source mod'] != 'Odyssey':
            continue
        package = str(row['MayRequire']).strip()
        if package.casefold() in ('', 'no', 'none', 'nan'):
            group = 'Core'
        else:
            group = requirements.get(package.casefold())
            if group is None:
                raise ValueError(f'Animals row {index + 2}: leather {leather} '
                                 f'is Odyssey-owned but consumer MayRequire '
                                 f'{package!r} has no registered material route')
        if group != 'Odyssey':
            consumers.setdefault(leather, set()).add(group)
    return consumers


def render_leather_outputs(source, mod, entries, *, ce=True, original_xml=None, original_patches=None, animals_source=None):
    """Generate leathers from AnimalStats and original upstream materials only."""
    from core.leather_overrides import render_original_overrides, _emit
    rows = read_leathers(source)
    mod = Path(mod)
    original_xml = Path(original_xml or Path(__file__).resolve().parent.parent / 'OriginalXML')
    original_patches = Path(original_patches or Path(__file__).resolve().parent.parent / 'OriginalXML' / 'CEPatches')
    native = mod / CUSTOM_DEST
    if not native.is_file():
        raise ValueError(f'Missing four Zoology leather definitions: {native}')
    consumers = _cross_mod_consumers(rows, entries, source, animals_source)
    routed, _, report = render_original_overrides(
        rows, original_xml, original_patches, ce=ce, consumers=consumers)
    outputs = dict(routed)
    outputs[CUSTOM_DEST] = _custom_defs(native, rows)
    outputs[CE_DEST] = _emit(E.fromstring(_custom_ce(rows, ce)))
    expected = {entry['destination'] for entry in entries}
    if set(outputs) != expected:
        raise ValueError(f'Leathers: generation.json mismatch: {expected ^ set(outputs)}')
    return [(entry['destination'], outputs[entry['destination']]) for entry in entries], {
        'source': str(source), 'materials_in_sheet': len(rows),
        'custom_defs': sorted(CUSTOM_NAMES), 'original_overrides': report,
    }
