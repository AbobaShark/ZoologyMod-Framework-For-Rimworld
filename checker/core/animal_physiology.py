"""Generate species physiology overrides from evaluated AnimalStats cells."""
from __future__ import annotations

from collections import defaultdict
from math import isfinite
import re
from lxml import etree as E

REQUIRED = frozenset(("XML name", "bleedRateFactor", "WoundInfectionChanceFactor"))
_EXT = "ZoologyMod.ModExtension_WoundInfectionChanceFactor, ZoologyMod"
_DEF_NAME = re.compile(r"^[A-Za-z_][A-Za-z0-9_.-]*$")
_XML_NAMES = re.compile(r"<li>\s*([^<]+?)\s*</li>")


def _number(value, default, field, species):
    if value is None:
        return default
    raw = str(value).strip()
    if raw.casefold() in ("", "no", "none", "nan"):
        return default
    try:
        number = float(raw.replace(",", "."))
    except ValueError:
        raise ValueError(f"{species}: invalid {field}: {value!r}") from None
    if not isfinite(number):
        raise ValueError(f"{species}: {field} must be finite")
    if field == "bleedRateFactor" and number <= 0:
        raise ValueError(f"{species}: bleedRateFactor must be positive")
    if field == "WoundInfectionChanceFactor" and not 0 <= number <= 1:
        raise ValueError(f"{species}: WoundInfectionChanceFactor must be in [0, 1]")
    return number


def _selector(defs):
    ordered = sorted(set(defs))
    for name in ordered:
        if not _DEF_NAME.fullmatch(name):
            raise ValueError(f"Invalid animal DefName: {name!r}")
    return "/Defs/ThingDef[" + " or ".join(f'defName="{name}"' for name in ordered) + "]"


def _conditional(root, xpath, action, action_xpath=None, node_name=None, text=None):
    op = E.SubElement(root, "Operation", Class="PatchOperationConditional")
    E.SubElement(op, "xpath").text = xpath
    match = E.SubElement(op, "match", Class=action)
    E.SubElement(match, "xpath").text = action_xpath or xpath
    if node_name is not None:
        value = E.SubElement(match, "value")
        E.SubElement(value, node_name).text = text
    return op


def _race_field(root, defs, field, value):
    select = _selector(defs)
    # Each operation is guarded so the same patch supports optional mod animals.
    _conditional(root, f"{select}[not(race)]", "PatchOperationAdd",
                 node_name="race")
    _conditional(root, f"{select}/race/{field}", "PatchOperationReplace",
                 node_name=field, text=value)
    _conditional(root, f"{select}/race[not({field})]", "PatchOperationAdd",
                 node_name=field, text=value)


def _wound_factor(root, defs, factor):
    select = _selector(defs)
    existing = f'{select}/modExtensions/li[@Class="{_EXT}"]'
    _conditional(root, existing, "PatchOperationRemove")
    _conditional(root, select, "PatchOperationAddModExtension",
                 node_name=None)
    match = root[-1].find("match")
    value = E.SubElement(match, "value")
    li = E.SubElement(value, "li", Class=_EXT)
    E.SubElement(li, "factor").text = format(factor, ".6g")


def render_animal_physiology(animals):
    """Return a complete RimWorld patch with data-driven blood-loss and infection factors."""
    missing = REQUIRED - set(animals.columns)
    if missing:
        raise ValueError(f"AnimalStats Animals missing physiology columns: {sorted(missing)}")

    bleeding = defaultdict(list)
    infections = defaultdict(list)
    seen = set()
    for _, row in animals.iterrows():
        names = _XML_NAMES.findall(str(row["XML name"]))
        if not names:
            continue
        bleed = _number(row["bleedRateFactor"], 1.0, "bleedRateFactor", names[0])
        infection = _number(row["WoundInfectionChanceFactor"], 1.0,
                            "WoundInfectionChanceFactor", names[0])
        for name in names:
            if name in seen:
                raise ValueError(f"Duplicate animal DefName in physiology: {name}")
            seen.add(name)
            if bleed != 1:
                bleeding[bleed].append(name)
            if infection != 1:
                infections[infection].append(name)

    root = E.Element("Patch")
    for factor, names in sorted(bleeding.items()):
        _race_field(root, names, "bleedRateFactor", format(factor, ".6g"))
    for factor, names in sorted(infections.items()):
        if factor == 0:
            _race_field(root, names, "isImmuneToInfections", "true")
        else:
            _wound_factor(root, names, factor)

    return E.tostring(root, encoding="utf-8", xml_declaration=True,
                      pretty_print=True)
