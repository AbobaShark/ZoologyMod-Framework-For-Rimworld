"""Generate LifeStage XML solely from calculated AnimalStats factors.

All numeric inputs are required; malformed or missing entries fail before
any generated XML is installed. CE-only StatDefs stay in the CE load folder.
"""
from __future__ import annotations

import math
from lxml import etree as E

STAGE_GROUPS = (
    ("AnimalAdult", "EusocialInsectAdult"),
    ("AnimalJuvenile", "EusocialInsectJuvenile"),
    ("AnimalBaby", "EusocialInsectLarva"),
    ("AnimalBabyTiny",),
)

ROYAL_SOURCE = "Vanilla Animals Expanded — Royal Animals"
ROYAL_STAGES = tuple((name,) for name in (
    "VAERoy_AnimalBaby",
    "VAERoy_AnimalJuvenile",
    "VAERoy_AnimalAdult",
    "VAERoy_AnimalAdultIntermediate",
    "VAERoy_AnimalAdultMax",
))
SOURCE_STAGES = {"Core": STAGE_GROUPS, ROYAL_SOURCE: ROYAL_STAGES}

BASE_FIELDS = (
    "bodySizeFactor", "marketValueFactor", "healthScaleFactor",
    "hungerRateFactor", "foodMaxFactor", "meleeDamageFactor",
)
BASE_STATS = {
    "MoveSpeed": "SpeedFactor",
    "MeleeCooldownFactor": "MeleeAttackIntervalFactor",
    "FilthRate": "FilthRate",
    "ArmorRating_Sharp": "ArmorRating_Sharp",
    "ArmorRating_Blunt": "ArmorRating_Blunt",
    "MeleeDodgeChance": "MeleeDodgeChance",
}
CE_STATS = {
    "MeleeCooldownFactor": "MeleeAttackIntervalFactor(CE)",
    "MeleeCritChance": "MeleeCritChance",
    "MeleeParryChance": "MeleeParryChance",
}
CE_PENETRATION = {
    "meleePenetrationSharpFactor": "ArmorPenetration_Sharp(CE)",
    "meleePenetrationBluntFactor": "ArmorPenetration_Blunt(CE)",
}
COMBAT_POWER_EXTENSION = "ZoologyMod.LifeStageCombatPowerExtension, ZoologyMod"

REQUIRED_COLUMNS = (
    {"Life stage", "Source mod", *BASE_FIELDS, *BASE_STATS.values(), "CombatPowerFactor",
     "meleeDamageFactor(CE)", *CE_STATS.values(), *CE_PENETRATION.values()}
)


def _number(value, stage, column):
    raw = str(value).strip().replace(",", ".")
    try:
        number = float(raw)
    except (ValueError, TypeError):
        raise ValueError(f"LifeStages {stage}: missing or invalid {column}: {value!r}") from None
    if not math.isfinite(number) or number <= 0:
        raise ValueError(f"LifeStages {stage}: {column} must be finite and positive: {value!r}")
    return number


def parse_stages(table, source_mod=None):
    """Validate every source/stage combination before writing any XML."""
    missing = REQUIRED_COLUMNS - set(table.columns)
    if missing:
        raise ValueError(f"LifeStages missing columns: {sorted(missing)}")
    if source_mod is not None and source_mod not in SOURCE_STAGES:
        raise ValueError(f"Unsupported LifeStages source: {source_mod!r}")
    expected = {(owner, frozenset(group))
                for owner, groups in SOURCE_STAGES.items() for group in groups}
    found = {}
    for _, record in table.iterrows():
        title = str(record.get("Life stage", "")).strip()
        if not title:
            continue
        owner = str(record.get("Source mod", "")).strip()
        group = frozenset(p.strip() for p in title.split(" or "))
        key = (owner, group)
        if key not in expected:
            raise ValueError(f"Unknown LifeStages source/stage: {owner!r} / {title!r}")
        if key in found:
            raise ValueError(f"Duplicate LifeStages source/stage: {owner!r} / {title!r}")
        found[key] = {column: _number(record.get(column), title, column)
                      for column in REQUIRED_COLUMNS
                      if column not in ("Life stage", "Source mod")}
    if set(found) != expected:
        missing_groups = sorted((owner, sorted(group))
                                for owner, group in expected - set(found))
        raise ValueError(f"Missing LifeStages source/stage rows: {missing_groups}")
    adult = found[("Core", frozenset(STAGE_GROUPS[0]))]
    wrong = {key: value for key, value in adult.items() if abs(value - 1) > 1e-7}
    if wrong:
        raise ValueError(f"Adult Core LifeStages baseline must be 1: {wrong}")
    sizes = [found[(ROYAL_SOURCE, frozenset(group))]["bodySizeFactor"]
             for group in ROYAL_STAGES]
    if any(right <= left for left, right in zip(sizes, sizes[1:])):
        raise ValueError("Royal megachicken bodySizeFactors must strictly increase with age")
    sources = SOURCE_STAGES if source_mod is None else {source_mod: SOURCE_STAGES[source_mod]}
    return [(group, found[(owner, frozenset(group))])
            for owner, groups in sources.items() for group in groups]

def _number_xml(value):
    return format(value, ".12g")


def _xpath(group):
    return "/Defs/LifeStageDef[" + " or ".join(f'defName="{name}"' for name in group) + "]"


def _operation(root, cls, xpath):
    op = E.SubElement(root, "Operation", Class=cls)
    E.SubElement(op, "xpath").text = xpath
    return op


def _remove_existing(root, xpath):
    op = _operation(root, "PatchOperationConditional", xpath)
    match = E.SubElement(op, "match", Class="PatchOperationRemove")
    E.SubElement(match, "xpath").text = xpath


def _ensure_container(root, parent, tag):
    xpath = parent + "/" + tag
    op = _operation(root, "PatchOperationConditional", xpath)
    E.SubElement(op, "match")
    nomatch = E.SubElement(op, "nomatch", Class="PatchOperationAdd")
    E.SubElement(nomatch, "xpath").text = parent
    E.SubElement(E.SubElement(nomatch, "value"), tag)


def _append_values(root, parent, fields, *, tag=None, attrs=None):
    op = _operation(root, "PatchOperationAdd", parent)
    value = E.SubElement(op, "value")
    target = value if tag is None else E.SubElement(value, tag, **(attrs or {}))
    for field, number in fields.items():
        E.SubElement(target, field).text = _number_xml(number)


def _replace_scalars(root, parent, fields):
    selector = " or ".join(f"self::{name}" for name in fields)
    _remove_existing(root, parent + "/*[" + selector + "]")
    _append_values(root, parent, fields)


def _replace_stats(root, parent, fields):
    _ensure_container(root, parent, "statFactors")
    selector = " or ".join(f"self::{name}" for name in fields)
    _remove_existing(root, parent + "/statFactors/*[" + selector + "]")
    _append_values(root, parent + "/statFactors", fields)


def _append_penetration(root, name, numbers):
    add = _operation(root, "PatchOperationAdd", "Defs")
    entry = E.SubElement(E.SubElement(add, "value"), "LifeStagePenetrationDef",
                         Class="ZoologyMod.LifeStagePenetrationDef, ZoologyMod")
    E.SubElement(entry, "defName").text = name
    for field, column in CE_PENETRATION.items():
        E.SubElement(entry, field).text = _number_xml(numbers[column])


def render_life_stages(table, *, ce=False, source_mod="Core"):
    root = E.Element("Patch")
    for group, numbers in parse_stages(table, source_mod):
        for name in group:
            parent = _xpath((name,))
            temp = E.Element("Patch")
            if ce:
                _replace_scalars(temp, parent, {"meleeDamageFactor": numbers["meleeDamageFactor(CE)"]})
                _replace_stats(temp, parent, {field: numbers[column] for field, column in CE_STATS.items()})
                if source_mod != "Core":
                    # CE can be enabled without Royal Animals. Never create a
                    # penetration Def or target an absent Royal stage in that case.
                    _append_penetration(temp, name, numbers)
                    guard = _operation(root, "PatchOperationConditional", parent)
                    ops = E.SubElement(E.SubElement(guard, "match",
                                      Class="PatchOperationSequence"), "operations")
                    for op in temp:
                        op.tag = "li"
                        ops.append(op)
                else:
                    root.extend(temp)
            else:
                _replace_scalars(temp, parent, {field: numbers[field] for field in BASE_FIELDS})
                _replace_stats(temp, parent, {field: numbers[column] for field, column in BASE_STATS.items()})
                root.extend(temp)
    if ce and source_mod == "Core":
        for group, numbers in parse_stages(table, source_mod):
            for name in group:
                _append_penetration(root, name, numbers)
    return E.tostring(root, encoding="utf-8", xml_declaration=True, pretty_print=True)

def render_life_stage_combat_power(table, *, source_mod="Core"):
    root = E.Element("Patch")
    for group, numbers in parse_stages(table, source_mod):
        for name in group:
            parent = _xpath((name,))
            extension_path = parent + '/modExtensions/li[@Class="' + COMBAT_POWER_EXTENSION + '"]'
            _remove_existing(root, extension_path)
            _ensure_container(root, parent, "modExtensions")
            op = _operation(root, "PatchOperationAdd", parent + "/modExtensions")
            val = E.SubElement(op, "value")
            ext = E.SubElement(val, "li", Class=COMBAT_POWER_EXTENSION)
            E.SubElement(ext, "combatPowerFactor").text = _number_xml(numbers["CombatPowerFactor"])
    return E.tostring(root, encoding="utf-8", xml_declaration=True, pretty_print=True)
