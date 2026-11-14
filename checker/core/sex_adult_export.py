"""Export canonical mean-sex endpoints without recomputing biomechanics."""
from __future__ import annotations

import math
from collections import Counter
from lxml import etree as E

from core.life_stage_species_profiles import _xml_names, _required, _number, _source_number
from core.sex_adult_profiles import audit_sex_adult_profiles, _market_price_flags

_STATS = (
    ("SpeedFactor", "MoveSpeed", False),
    ("CE_SpeedFactor", "MoveSpeed", True),
    ("FilthRateFactor", "FilthRate", False),
    ("CE_MeleeDodgeChance", "MeleeDodgeChance", True),
    ("CE_MeleeCritChance", "MeleeCritChance", True),
    ("CE_MeleeParryChance", "MeleeParryChance", True),
)
_ARMOR = (
    ("VanillaArmorSharp", "ArmorRating_Sharp", "ArmorRating_Sharp", False),
    ("VanillaArmorBlunt", "ArmorRating_Blunt", "ArmorRating_Blunt", False),
    ("CE_ArmorSharp", "ArmorRating_Sharp", "ArmorRating_Sharp", True),
    ("CE_ArmorBlunt", "ArmorRating_Blunt", "ArmorRating_Blunt", True),
)
_TOOL_NAMES = (
    "vanillaDamageFactor", "vanillaCooldownFactor",
    "ceDamageFactor", "ceCooldownFactor",
    "ceSharpPenetrationFactor", "ceBluntPenetrationFactor",
)


def _index(table):
    result = {}
    for _, row in table.iterrows():
        for name in _xml_names(row.get("XML name")):
            if name in result:
                raise ValueError(f"Duplicate adult baseline {name}")
            result[name] = row
    return result


def _add_number(parent, tag, number):
    E.SubElement(parent, tag).text = _source_number(number)


def _add_stat(node, stat, factor, ce_only, *, offset=False):
    entry = E.SubElement(node, "li")
    E.SubElement(entry, "stat").text = stat
    _add_number(entry, "offset" if offset else "factor", factor)
    if ce_only:
        E.SubElement(entry, "ceOnly").text = "true"


def render_sex_adult_profiles(profiles, sex, animals, animals_ce, life_stages,
                              base_stats=None):
    """Build immutable endpoints from evaluated SexAdultProfiles.

    An endpoint may be installed only when the adult LifeStageProfiles mass
    equals its canonical baseline. A giant intermediate or final stage cannot
    be assigned the physics of a 5 kg adult merely because its ThingDef matches.
    """
    audit = audit_sex_adult_profiles(
        profiles, sex, animals, base_stats=base_stats)
    price_flags = _market_price_flags(sex, base_stats)
    adult_index = _index(animals)
    ce_index = _index(animals_ce)
    life = {}
    for _, record in life_stages.iterrows():
        for name in _xml_names(record.get("XML name")):
            stage = str(record.get("LifeStageDef") or "")
            if ("Adult" in stage or stage == "SpectrobiontaLarvaLarge"):
                life.setdefault(name, []).append(record)

    root = E.Element("Defs")
    catalog = E.SubElement(root, "ZoologySexAdultProfileCatalogDef",
                           Class="ZoologyMod.ZoologySexAdultProfileCatalogDef, ZoologyMod")
    E.SubElement(catalog, "defName").text = "Zoology_GeneratedSexAdultProfiles"
    entries = E.SubElement(catalog, "profiles")
    diagnostics = Counter()
    for _, record in profiles.iterrows():
        names = _xml_names(record.get("XML name"))
        if not names:
            continue
        if len(names) != 1:
            raise ValueError("SexAdultProfiles must have exactly one ThingDef per row")
        name = names[0]
        adult_stages = life.get(name, [])
        if not adult_stages:
            raise ValueError(f"{name}: missing reference adult LifeStageProfiles")
        stage = adult_stages[0]
        baseline = _required(record.get("BaselineMass_kg"), name)
        stage_mass = _required(stage.get("BiologicalStageMass_kg"), name)
        if not math.isclose(stage_mass, baseline, rel_tol=1e-9, abs_tol=1e-8):
            raise ValueError(
                f"{name}: adult LifeStageProfiles mass {stage_mass} kg "
                f"disagrees with SexAdultProfiles baseline {baseline} kg")
        source = str(stage.get("LifeStageDef"))
        gender = str(record.get("Sex")).strip()
        context = name + "/" + gender
        node = E.SubElement(entries, "li")
        E.SubElement(node, "animalDefName").text = name
        E.SubElement(node, "sourceStageDefName").text = source
        E.SubElement(node, "sex").text = gender
        if price_flags.get(name, False):
            E.SubElement(node, "useSexSpecificMarketValue").text = "true"
            _add_number(node, "marketValueFactor",
                        _required(record.get("MarketValueFactor"),
                                  context + "/MarketValueFactor"))
            diagnostics["sex_specific_market_prices"] += 1
        for xml, column in (
            ("adultMass", "AdultMass_kg"),
            ("baselineMass", "BaselineMass_kg"),
            ("bodySizeFactor", "BodySizeFactor"),
            ("healthScaleFactor", "HealthScaleFactor"),
            ("hungerRateFactor", "HungerRateFactor"),
            ("combatPowerFactor", "CombatPowerFactor"),
            ("ceArmorDurabilityFactor", "CE_ArmorDurabilityFactor"),
        ):
            _add_number(node, xml, _required(record.get(column), context,
                positive=xml not in ("ceArmorDurabilityFactor",)))
        stats = E.SubElement(node, "statFactors")
        offsets = E.SubElement(node, "statOffsets")
        for column, stat, ce_only in _STATS:
            value = _number(record.get(column), context + "/" + column, positive=False)
            if value is not None:
                _add_stat(stats, stat, value, ce_only)

        for column, base_column, stat, ce_only in _ARMOR:
            target = _number(record.get(column), context + "/" + column, positive=False)
            if target is None:
                continue
            source_row = ce_index.get(name) if ce_only else adult_index.get(name)
            if source_row is None:
                raise ValueError(f"{context}: missing armor baseline")
            base = _number(source_row.get(base_column),
                           context + "/baseline " + base_column, positive=False)
            if base is None:
                raise ValueError(f"{context}: armor endpoint without numeric baseline")
            if base == 0:
                if target:
                    _add_stat(offsets, stat, target, ce_only, offset=True)
            else:
                _add_stat(stats, stat, target / base, ce_only)

        # LifeStageProfiles is the canonical post-patch Tool identity catalog;
        # SexAdultProfiles contains the six independently evaluated adult ratios.
        raw_tools = str(stage.get("ToolRecordsV2") or "").splitlines()
        tool_identities = {}
        for ordinal, line in enumerate(raw_tools):
            parts = line.split("\t")
            if len(parts) != 11:
                raise ValueError(f"{name}/{source}: malformed ToolRecordsV2")
            tool_identities[ordinal] = parts
        tools = E.SubElement(node, "tools")
        for line in str(record.get("ToolRecords9") or "").splitlines():
            parts = line.split("\t")
            if len(parts) != 9:
                raise ValueError(f"{context}: malformed ToolRecords9")
            ordinal = int(parts[0])
            matched = tool_identities.get(ordinal)
            if matched is None or parts[1:3] != matched[2:4]:
                raise ValueError(f"{context}: ToolRecords9 identity #{ordinal} "
                                 "does not match LifeStageProfiles ToolRecordsV2")
            tool = E.SubElement(tools, "li")
            E.SubElement(tool, "vanillaToolIndex").text = matched[0]
            E.SubElement(tool, "ceToolIndex").text = matched[1]
            for tag, val in (("label", parts[1]),
                             ("linkedBodyPartsGroup", parts[2]),
                             ("capacities", matched[4])):
                E.SubElement(tool, tag).text = val
            for tag, raw in zip(_TOOL_NAMES, parts[3:]):
                _add_number(tool, tag, _required(raw, context + "/" + tag,
                            positive=tag.endswith("CooldownFactor")))
            diagnostics["natural_tools"] += 1
        diagnostics["endpoints"] += 1
    diagnostics["source_endpoints"] = audit["adult_endpoints"]
    return E.tostring(root, encoding="utf-8", xml_declaration=True, pretty_print=True), dict(diagnostics)
