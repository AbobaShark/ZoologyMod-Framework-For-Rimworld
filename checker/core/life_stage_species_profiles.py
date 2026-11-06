"""Compile LifeStage profiles from numerical cells ALREADY evaluated by AnimalStats.

No age- or species-specific formula is evaluated in Checker. All numerical
inputs, including separate tool physics, come from workbook/TSV/XLSX columns.
Checker validates identities, types and baselines and serializes XML.
"""
from __future__ import annotations

import math
import json
import re
from collections import Counter, defaultdict
from lxml import etree as E

from core.life_stages import STAGE_GROUPS, ROYAL_STAGES, parse_stages

_XML_NAMES = re.compile(r"<li>\s*([^<>]+?)\s*</li>")
_TOOL_FIELDS = (
    ("VanillaDamageFactor", "vanillaDamageFactor"),
    ("VanillaCooldownFactor", "vanillaCooldownFactor"),
    ("CEDamageFactor", "ceDamageFactor"),
    ("CECooldownFactor", "ceCooldownFactor"),
    ("CESharpPenetrationFactor", "ceSharpPenetrationFactor"),
    ("CEBluntPenetrationFactor", "ceBluntPenetrationFactor"),
)
_STAT_FIELDS = (
    ("FilthRate", "FilthRate", False),
    ("MeleeDodgeChance", "MeleeDodgeChance", True),
    ("MeleeCritChance", "MeleeCritChance", True),
    ("MeleeParryChance", "MeleeParryChance", True),
    ("ArmorRating_Sharp", "ArmorRating_Sharp", False),
    ("ArmorRating_Blunt", "ArmorRating_Blunt", False),
)
_ERRORS = ("#REF!", "#N/A", "#DIV/0!", "#VALUE!", "#NUM!", "#NAME?", "#ERROR!")


def _xml_names(value):
    source = str(value or "").strip()
    if not source:
        return []
    names = _XML_NAMES.findall(source)
    if not names or _XML_NAMES.sub("", source).strip(" ,;\r\n\t"):
        raise ValueError(f"Invalid AnimalStats XML name {source!r}")
    return names


def _is_empty(value):
    return value is None or str(value).strip().lower() in ("", "none", "nan", "no")


def _number(value, context, *, positive=True):
    if _is_empty(value):
        return None
    if str(value).lstrip().startswith(_ERRORS):
        raise ValueError(f"{context}: source formula error: {value!r}")
    try:
        number = float(str(value).strip().replace(",", "."))
    except (ValueError, TypeError):
        raise ValueError(f"{context}: expected numeric cell, got {value!r}") from None
    if not math.isfinite(number) or (number <= 0 if positive else number < 0):
        raise ValueError(f"{context}: invalid {'positive' if positive else 'nonnegative'} number {value!r}")
    return number


def _required(value, context, *, positive=True):
    number = _number(value, context, positive=positive)
    if number is None:
        raise ValueError(f"{context}: missing required numeric cell")
    return number


def _text_number(value):
    return format(value, ".12g")


def _adult_rows(animals):
    if animals is None or not {"XML name", "baseBodySize", "baseHungerRate"}.issubset(animals.columns):
        raise ValueError("Animals must provide adult XML names, baseBodySize and baseHungerRate")
    rows = {}
    for _, record in animals.iterrows():
        names = _xml_names(record.get("XML name", ""))
        for name in names:
            if name in rows:
                raise ValueError(f"Animals duplicates XML name {name!r}")
            rows[name] = record.to_dict()
    return rows


def _tool_profiles(table, diagnostics):
    """Strict, individually addressed Tool rows.

    A non-ready row is never interpreted as six neutral coefficients. A READY
    row must provide all six factors, making missing physics unrepresentable.
    """
    output = defaultdict(list)
    if table is None:
        diagnostics["attack_sheet_missing"] += 1
        return output
    required = {"XML name", "LifeStageDef", "ToolIndex", "CalculationStatus",
                *(column for column, _ in _TOOL_FIELDS)}
    missing = required - set(table.columns)
    if missing:
        raise ValueError("LifeStageAttackProfiles missing columns: "
                         + repr(sorted(missing)))
    encountered = set()
    for number, (_, record) in enumerate(table.iterrows(), start=2):
        row = record.to_dict()
        if _is_empty(row.get("XML name")):
            continue
        names = _xml_names(row.get("XML name"))
        if len(names) != 1:
            raise ValueError(f"LifeStageAttackProfiles row {number}: one XML name required")
        name = names[0]
        stage = str(row.get("LifeStageDef", "")).strip()
        status = str(row.get("CalculationStatus", "")).strip().upper()
        if status not in ("READY", "INCOMPLETE", "INVALID"):
            raise ValueError(f"LifeStageAttackProfiles row {number}: invalid CalculationStatus {status!r}")
        if status == "INVALID":
            raise ValueError(
                f"LifeStageAttackProfiles row {number}: invalid calculated attack factors")
        if status != "READY":
            diagnostics["attack_rows_incomplete"] += 1
            continue
        raw_index = row.get("ToolIndex")
        if _is_empty(raw_index):
            raise ValueError(f"LifeStageAttackProfiles row {number}: no ToolIndex")
        try:
            index_number = float(str(raw_index).replace(",", "."))
        except ValueError:
            raise ValueError(f"LifeStageAttackProfiles row {number}: invalid ToolIndex") from None
        if index_number < 0 or not index_number.is_integer():
            raise ValueError(f"LifeStageAttackProfiles row {number}: non-integer ToolIndex")
        index = int(index_number)
        if not stage:
            raise ValueError(f"LifeStageAttackProfiles row {number}: missing source LifeStageDef")
        key = (name, stage, index)
        if key in encountered:
            raise ValueError(f"LifeStageAttackProfiles duplicate tool {key!r}")
        encountered.add(key)
        label = str(row.get("ToolLabel", "")).strip()
        linked = str(row.get("LinkedBodyPartsGroup", "")).strip()
        if not label and not linked:
            raise ValueError(f"LifeStageAttackProfiles row {number}: tool identity guard missing")
        factors = []
        for column, field in _TOOL_FIELDS:
            value = _required(row.get(column), f"{name}/{stage}/{index}/{column}")
            if not math.isclose(value, 1.0, abs_tol=1e-10, rel_tol=0):
                factors.append((field, value))
        if factors:
            output[(name, stage)].append((index, label, linked, factors))
            diagnostics["attack_tools_ready"] += 1
    return output


def render_species_life_stage_profiles(species_profiles, attacks,
                                       animals, life_stages, *, ce=True,
                                       mass_lookup=None):
    """Render XML from *evaluated* LifeStageSpeciesProfiles/AttackProfiles.

    Missing biomechanical outputs remain absent: no universal power law or
    adult-times-BodySize pseudo-mass is introduced in Checker.
    """
    if species_profiles is None:
        raise ValueError("LifeStageSpeciesProfiles source is required")
    required = {"XML name", "LifeStageDef", "ExpectedBaseBodySize",
                "ExpectedBaseHungerRate", "ExpectedBodySizeFactor",
                "HungerRateFactor", "CalculationStatus"}
    missing = required - set(species_profiles.columns)
    if missing:
        raise ValueError(f"LifeStageSpeciesProfiles missing columns: {sorted(missing)}")
    adults = _adult_rows(animals)
    stage_inputs = parse_stages(life_stages, mass_lookup=mass_lookup)
    stage_groups = {}
    for names, stage_values in stage_inputs:
        label = " or ".join(names)
        stage_groups[label] = (names, stage_values)
    diagnostics = Counter()
    tool_profiles = _tool_profiles(attacks, diagnostics)
    rendered = set()
    root = E.Element("Defs")
    catalog = E.SubElement(root, "ZoologyLifeStageProfileCatalogDef",
                           Class="ZoologyMod.ZoologyLifeStageProfileCatalogDef, ZoologyMod")
    E.SubElement(catalog, "defName").text = "Zoology_GeneratedLifeStageProfiles"
    E.SubElement(catalog, "label").text = "Zoology calculated animal life stages"
    stage_nodes = E.SubElement(catalog, "stages")

    for number, (_, record) in enumerate(species_profiles.iterrows(), start=2):
        row = record.to_dict()
        if _is_empty(row.get("XML name")):
            continue
        names = _xml_names(row["XML name"])
        label = str(row.get("LifeStageDef", "")).strip()
        if label not in stage_groups:
            raise ValueError(f"LifeStageSpeciesProfiles row {number}: unknown LifeStageDef {label!r}")
        sources, common = stage_groups[label]
        status = str(row.get("CalculationStatus", "")).strip().upper()
        if status == "SOURCE ERROR":
            diagnostics["species_rows_invalid_source"] += 1
            continue
        if status not in ("READY", "INCOMPLETE"):
            raise ValueError(f"LifeStageSpeciesProfiles row {number}: invalid status {status!r}")
        expected_body = _required(row.get("ExpectedBaseBodySize"),
                                  f"{label} row {number} adult BodySize")
        expected_hunger = _required(row.get("ExpectedBaseHungerRate"),
                                    f"{label} row {number} adult hunger")
        expected_stage_size = _required(row.get("ExpectedBodySizeFactor"),
                                        f"{label} row {number} stage factor")
        if not math.isclose(expected_stage_size, common["bodySizeFactor"], abs_tol=1e-7):
            raise ValueError(f"{label} row {number}: stage factor differs from LifeStages")
        hunger = _required(row.get("HungerRateFactor"),
                           f"{label} row {number} stage hunger factor")
        stats = []
        for column, stat_name, ce_only in _STAT_FIELDS:
            if ce_only and not ce:
                continue
            value = _number(row.get(column), f"{label} row {number} {column}",
                            positive=False)
            if value is not None and not math.isclose(value, 1, abs_tol=1e-10):
                stats.append((stat_name, value, ce_only))
        combat_power = _number(row.get("CombatPowerFactor"),
                               f"{label} row {number} CombatPowerFactor")
        market_value = _number(row.get("MarketValueFactor"),
                               f"{label} row {number} MarketValueFactor")
        if status == "READY" and (combat_power is None or market_value is None or
                                  _number(row.get("ArmorRating_Sharp"), "sharp armor") is None or
                                  _number(row.get("ArmorRating_Blunt"), "blunt armor") is None):
            raise ValueError(f"{label} row {number}: READY profile has missing outputs")
        if status == "INCOMPLETE":
            diagnostics["species_rows_incomplete"] += 1
        for name in names:
            adult = adults.get(name)
            if adult is None:
                raise ValueError(f"LifeStageSpeciesProfiles unknown species {name!r}")
            for field, wanted in (("baseBodySize", expected_body),
                                  ("baseHungerRate", expected_hunger)):
                original = _required(adult.get(field), f"{name}/{field}")
                if not math.isclose(original, wanted, abs_tol=1e-4):
                    raise ValueError(f"{name}/{field}: source baseline changed")
            if name == "VAERoy_Megachicken" and not sources[0].startswith("VAERoy_"):
                continue
            if name != "VAERoy_Megachicken" and sources[0].startswith("VAERoy_"):
                continue
            for source in sources:
                key = (name, source)
                if key in rendered:
                    raise ValueError(f"Duplicate life stage {key!r}")
                rendered.add(key)
                tools = tool_profiles.get(key, [])
                changed = (not math.isclose(hunger, 1, abs_tol=1e-10)
                           or bool(stats) or bool(tools)
                           or combat_power is not None and not math.isclose(combat_power, 1)
                           or market_value is not None and not math.isclose(market_value, 1))
                if not changed:
                    diagnostics["neutral_stages_omitted"] += 1
                    continue
                node = E.SubElement(stage_nodes, "li")
                for tag, value in (
                    ("animalDefName", name), ("sourceStageDefName", source),
                    ("expectedBaseBodySize", _text_number(expected_body)),
                    ("expectedBaseHungerRate", _text_number(expected_hunger)),
                    ("expectedBodySizeFactor", _text_number(expected_stage_size)),
                    ("hungerRateFactor", _text_number(hunger)),
                ):
                    E.SubElement(node, tag).text = value
                if market_value is not None:
                    E.SubElement(node, "marketValueFactor").text = _text_number(market_value)
                if combat_power is not None:
                    E.SubElement(node, "combatPowerFactor").text = _text_number(combat_power)
                if stats:
                    stat_list = E.SubElement(node, "statFactors")
                    for stat_name, number, is_ce in stats:
                        stat = E.SubElement(stat_list, "li")
                        E.SubElement(stat, "stat").text = stat_name
                        E.SubElement(stat, "factor").text = _text_number(number)
                        if is_ce:
                            E.SubElement(stat, "ceOnly").text = "true"
                # Attack data are compiled into a generated C# registry.
                # An attack-only profile still produces a specialized stage.
                if tools:
                    diagnostics["profiles_with_compiled_attacks"] += 1
                diagnostics["profiles"] += 1
    unconsumed = set(tool_profiles) - rendered
    if unconsumed:
        raise ValueError("Unmatched READY attack profile stages: "
                         + repr(sorted(unconsumed)[:15]))
    return (E.tostring(root, encoding="utf-8", xml_declaration=True, pretty_print=True),
            dict(diagnostics))


def render_life_stage_attack_profiles_cs(attack_profiles):
    """Generate the *single* compiled C# tool-factor registry from sheet cells.

    The C# file is deployed atomically with the generated XML through the
    existing Checker manifest; it contains no biomechanical formulas.
    """
    diagnostics = Counter()
    grouped = _tool_profiles(attack_profiles, diagnostics)
    lines = [
        "// <auto-generated> Checker / AnimalStats LifeStageAttackProfiles",
        "// All ratios below are calculated in AnimalStats, never at game time.",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "namespace ZoologyMod",
        "{",
        "    internal static class ZoologyGeneratedLifeStageAttacks",
        "    {",
        "        private static readonly Dictionary<string, List<ZoologyLifeStageToolFactor>> Values =",
        "            new Dictionary<string, List<ZoologyLifeStageToolFactor>>(StringComparer.Ordinal)",
        "            {",
    ]
    for (species, stage), tools in sorted(grouped.items()):
        name = json.dumps(species + "\n" + stage, ensure_ascii=True)
        lines.append(f"                {{ {name}, new List<ZoologyLifeStageToolFactor>")
        lines.append("                    {")
        for index, label, group, factors in sorted(tools):
            cells = [f"toolIndex = {index}"]
            if label:
                cells.append(f"label = {json.dumps(label, ensure_ascii=True)}")
            if group:
                cells.append(f"linkedBodyPartsGroup = {json.dumps(group, ensure_ascii=True)}")
            for field, value in factors:
                cells.append(f"{field} = {format(value, '.9g')}f")
            lines.append("                        new ZoologyLifeStageToolFactor { "
                         + ", ".join(cells) + " },")
        lines.append("                    } },")
    lines.extend([
        "            };",
        "",
        "        public static List<ZoologyLifeStageToolFactor> For(string species, string sourceStage)",
        "        {",
        "            if (string.IsNullOrEmpty(species) || string.IsNullOrEmpty(sourceStage))",
        "                return null;",
        "            List<ZoologyLifeStageToolFactor> selected;",
        "            Values.TryGetValue(species + \"\\n\" + sourceStage, out selected);",
        "            return selected;",
        "        }",
        "    }",
        "}",
        "",
    ])
    return "\n".join(lines).encode("utf-8"), dict(diagnostics)
