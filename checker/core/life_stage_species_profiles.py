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
from core.generated_source_contract import LIFE_STAGE_ATTACK_CSHARP_HEADER

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
    ("MeleeDodgeChance", "MeleeDodgeChance", False),
    ("MeleeCritChance", "MeleeCritChance", True),
    ("MeleeParryChance", "MeleeParryChance", True),
    ("ArmorRating_Sharp", "ArmorRating_Sharp", False),
    ("ArmorRating_Blunt", "ArmorRating_Blunt", False),
    ("ArmorRating_Sharp_CE", "ArmorRating_Sharp", True),
    ("ArmorRating_Blunt_CE", "ArmorRating_Blunt", True),
)
_STAT_OFFSET_FIELDS = (
    ("ArmorRating_Sharp_Offset", "ArmorRating_Sharp", False),
    ("ArmorRating_Blunt_Offset", "ArmorRating_Blunt", False),
    ("ArmorRating_Sharp_CE_Offset", "ArmorRating_Sharp", True),
    ("ArmorRating_Blunt_CE_Offset", "ArmorRating_Blunt", True),
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
    """Read the single AnimalStats LifeStageProfiles, never compute attack physics."""
    if table is None:
        raise ValueError("LifeStageProfiles is required")
    required = {"XML name", "LifeStageDef", "CalculationStatus",
                "ToolStatus", "ToolCount", "ToolRecordsV2"}
    missing = required - set(table.columns)
    if missing:
        raise ValueError(f"LifeStageProfiles missing columns: {sorted(missing)}")
    output = defaultdict(list)
    seen = set()
    for number, (_, record) in enumerate(table.iterrows(), 2):
        row = record.to_dict()
        if _is_empty(row.get("XML name")):
            continue
        names = _xml_names(row["XML name"])
        if len(names) != 1:
            raise ValueError(f"LifeStageProfiles row {number}: expected one species")
        name = names[0]
        stage = str(row.get("LifeStageDef", "")).strip()
        sources = tuple(x.strip() for x in stage.split(" or "))
        legal = (*STAGE_GROUPS, *ROYAL_STAGES)
        if not sources or not (any(set(sources) == set(group) for group in legal)
                              or len(sources) == 1 and any(sources[0] in group for group in legal)):
            raise ValueError(f"LifeStageProfiles row {number}: unknown stage {stage!r}")
        for source in sources:
            if (name, source) in seen:
                raise ValueError(f"LifeStageProfiles duplicate stage {name}/{source}")
            seen.add((name, source))
        status = str(row.get("CalculationStatus", "")).strip().upper()
        if status not in ("READY", "INCOMPLETE", "SOURCE ERROR"):
            raise ValueError(f"LifeStageProfiles row {number}: invalid status {status!r}")
        if status != "READY":
            diagnostics["attack_rows_incomplete"] += 1
            continue
        if str(row.get("ToolStatus", "")).strip().upper() != "READY":
            raise ValueError(f"LifeStageProfiles row {number}: incomplete attack set marked READY")
        raw = str(row.get("ToolRecordsV2") or "").strip()
        records = raw.splitlines() if raw else []
        expected = _required(row.get("ToolCount"), f"{name}/{stage}/ToolCount", positive=False)
        if not expected.is_integer() or len(records) != int(expected):
            raise ValueError(f"{name}/{stage}: ToolRecordsV2 and ToolCount disagree")
        used = (set(), set())
        for ordinal, line in enumerate(records):
            cells = line.split("\t")
            if len(cells) != 11:
                raise ValueError(f"{name}/{stage}: ToolRecordsV2 attack {ordinal} must have 11 fields")
            indexes = []
            for mode, value in zip(("Vanilla", "CE"), cells[:2]):
                try:
                    index = float(str(value).strip().replace(",", "."))
                except (ValueError, TypeError):
                    raise ValueError(f"{name}/{stage}: invalid {mode} ToolIndex {value!r}") from None
                if not math.isfinite(index) or not index.is_integer() or index < -1:
                    raise ValueError(f"{name}/{stage}: invalid {mode} ToolIndex {value!r}")
                indexes.append(int(index))
            if indexes == [-1, -1]:
                raise ValueError(f"{name}/{stage}: attack absent in both modes")
            for i, index in enumerate(indexes):
                if index >= 0:
                    if index in used[i]:
                        raise ValueError(f"{name}/{stage}: duplicate {('Vanilla','CE')[i]} ToolIndex {index}")
                    used[i].add(index)
            label, linked, capacities = (cell.strip() for cell in cells[2:5])
            if not (label and linked and capacities) or capacities.lower() == "no":
                raise ValueError(f"{name}/{stage}: missing label, linkedBodyPartsGroup or capacities")
            factors = []
            for component, ((source_field, field), value) in enumerate(zip(_TOOL_FIELDS, cells[5:])):
                # An attack absent from one mode has no components for that
                # mode. Blank cells are legitimate only in that mode. Nonzero
                # or nonunit ghost coefficients indicate inconsistent input.
                mode_index = 0 if component < 2 else 1
                context = f"{name}/{stage}/{label}/{source_field}"
                if indexes[mode_index] < 0:
                    if not _is_empty(value):
                        redundant = _required(value, context, positive=False)
                        if not math.isclose(redundant, 1, abs_tol=1e-10, rel_tol=0):
                            raise ValueError(
                                f"{context}: non-neutral factor for attack absent in this mode")
                    continue
                number_value = _required(value, context,
                                         positive=source_field.endswith("CooldownFactor"))
                if not math.isclose(number_value, 1, abs_tol=1e-10, rel_tol=0):
                    factors.append((field, number_value))
            if factors:
                for source in sources:
                    output[(name, source)].append(
                        (indexes[0], indexes[1], label, linked, capacities, factors))
                diagnostics["attack_tools_ready"] += 1
    return output


def render_species_life_stage_profiles(species_profiles, attacks,
                                       animals, life_stages, *, ce=True,
                                       mass_lookup=None):
    """Render XML from *evaluated* LifeStageProfiles/AttackProfiles.

    Missing biomechanical outputs remain absent: no universal power law or
    adult-times-BodySize pseudo-mass is introduced in Checker.
    """
    if species_profiles is None:
        raise ValueError("LifeStageProfiles source is required")
    required = {"XML name", "LifeStageDef", "ExpectedBaseBodySize",
                "ExpectedBaseHungerRate", "ExpectedBodySizeFactor",
                "HungerRateFactor", "CalculationStatus",
                "StageBodySize", "StageHealthScaleFactor", "CEArmorDurabilityFactor"}
    missing = required - set(species_profiles.columns)
    if missing:
        raise ValueError(f"LifeStageProfiles missing columns: {sorted(missing)}")
    adults = _adult_rows(animals)
    stage_inputs = parse_stages(life_stages, mass_lookup=mass_lookup)
    stage_groups = {}
    for names, stage_values in stage_inputs:
        label = " or ".join(names)
        stage_groups[label] = (names, stage_values)
    diagnostics = Counter()
    tool_profiles = _tool_profiles(species_profiles, diagnostics)
    diagnostics["species_rows_total"] = 0
    diagnostics["species_rows_ready"] = 0
    diagnostics["species_rows_incomplete"] = 0
    diagnostics["species_rows_invalid_source"] = 0
    diagnostics["incomplete_examples"] = []
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
        diagnostics["species_rows_total"] += 1
        names = _xml_names(row["XML name"])
        label = str(row.get("LifeStageDef", "")).strip()
        if label not in stage_groups:
            raise ValueError(f"LifeStageProfiles row {number}: unknown LifeStageDef {label!r}")
        sources, common = stage_groups[label]
        status = str(row.get("CalculationStatus", "")).strip().upper()
        if status == "SOURCE ERROR":
            diagnostics["species_rows_invalid_source"] += 1
            if len(diagnostics["incomplete_examples"]) < 24:
                diagnostics["incomplete_examples"].append(
                    {"row": number, "animal": names, "stage": label,
                     "reason": "SOURCE ERROR"})
            continue
        if status not in ("READY", "INCOMPLETE"):
            raise ValueError(f"LifeStageProfiles row {number}: invalid status {status!r}")
        expected_body = _required(row.get("ExpectedBaseBodySize"),
                                  f"{label} row {number} adult BodySize")
        expected_hunger = _required(row.get("ExpectedBaseHungerRate"),
                                    f"{label} row {number} adult hunger")
        expected_stage_size = _required(row.get("ExpectedBodySizeFactor"),
                                        f"{label} row {number} stage factor")
        if not math.isclose(expected_stage_size, common["bodySizeFactor"], abs_tol=1e-7):
            raise ValueError(f"{label} row {number}: stage factor differs from LifeStages")
        stage_body = _required(row.get("StageBodySize"),
                               f"{label} row {number} effective stage BodySize")
        body_factor = stage_body / expected_body
        health_factor = _required(row.get("StageHealthScaleFactor"),
                                  f"{label} row {number} stage health scale")
        durability_factor = (_required(row.get("CEArmorDurabilityFactor"),
                                       f"{label} row {number} CE armor durability",
                                       positive=False)
                             if ce and status == "READY" else None)
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
        offsets = []
        for column, stat_name, ce_only in _STAT_OFFSET_FIELDS:
            if ce_only and not ce:
                continue
            offset = _number(row.get(column), f"{label} row {number} {column}",
                             positive=False)
            if offset is None or offset == 0:
                continue
            factor_column = column.removesuffix("_Offset")
            factor = _number(row.get(factor_column),
                             f"{label} row {number} {factor_column}",
                             positive=False)
            if factor is None or not math.isclose(factor, 1, abs_tol=1e-10, rel_tol=0):
                raise ValueError(
                    f"{label} row {number}: {column} requires neutral {factor_column}"
                )
            offsets.append((stat_name, offset, ce_only))
        combat_power = _number(row.get("CombatPowerFactor"),
                               f"{label} row {number} CombatPowerFactor")
        market_value = _number(row.get("MarketValueFactor"),
                               f"{label} row {number} MarketValueFactor")
        if status == "READY" and (combat_power is None or market_value is None):
            raise ValueError(f"{label} row {number}: READY profile has missing outputs")
        if status == "INCOMPLETE":
            diagnostics["species_rows_incomplete"] += 1
            if len(diagnostics["incomplete_examples"]) < 24:
                diagnostics["incomplete_examples"].append(
                    {"row": number, "animal": names, "stage": label,
                     "reason": str(row.get("MissingToolDiagnostics") or
                                   "INCOMPLETE in AnimalStats; not installed").strip()})
            continue
        diagnostics["species_rows_ready"] += 1
        for name in names:
            adult = adults.get(name)
            if adult is None:
                raise ValueError(f"LifeStageProfiles unknown species {name!r}")
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
                changed = (not math.isclose(body_factor, common["bodySizeFactor"], abs_tol=1e-7)
                           or not math.isclose(health_factor, common["healthScaleFactor"], abs_tol=1e-7)
                           or durability_factor is not None and not math.isclose(durability_factor, 1, abs_tol=1e-10)
                           or not math.isclose(hunger, 1, abs_tol=1e-10)
                           or bool(stats) or bool(offsets) or bool(tools)
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
                    ("bodySizeFactor", _text_number(body_factor)),
                    ("healthScaleFactor", _text_number(health_factor)),
                    ("hungerRateFactor", _text_number(hunger)),
                ):
                    E.SubElement(node, tag).text = value
                if durability_factor is not None:
                    E.SubElement(node, "ceArmorDurabilityFactor").text = _text_number(durability_factor)
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
                if offsets:
                    offset_list = E.SubElement(node, "statOffsets")
                    for stat_name, number, is_ce in offsets:
                        offset_entry = E.SubElement(offset_list, "li")
                        E.SubElement(offset_entry, "stat").text = stat_name
                        E.SubElement(offset_entry, "offset").text = _text_number(number)
                        if is_ce:
                            E.SubElement(offset_entry, "ceOnly").text = "true"
                # Attack data are compiled into a generated C# registry.
                # An attack-only profile still produces a specialized stage.
                if tools:
                    diagnostics["profiles_with_compiled_attacks"] += 1
                diagnostics["profiles"] += 1
    unconsumed = set(tool_profiles) - rendered
    if unconsumed:
        raise ValueError("Unmatched READY attack profile stages: "
                         + repr(sorted(unconsumed)[:15]))
    if diagnostics["species_rows_ready"] == 0 or diagnostics["profiles"] == 0:
        raise ValueError("LifeStageProfiles produced no READY specialized stages")
    return (E.tostring(root, encoding="utf-8", xml_declaration=True, pretty_print=True),
            dict(diagnostics))


def render_life_stage_attack_profiles_cs(attack_profiles):
    """Compile evaluated attack ratios into bounded C# initialization batches.

    Batches avoid generating a single enormous .cctor when the AnimalStats
    workbook contains thousands of independent natural-tool profiles.
    No biomechanical formula or numerical extrapolation executes in Checker.
    """
    diagnostics = Counter()
    grouped = _tool_profiles(attack_profiles, diagnostics)
    batch_size = 64
    records = sorted(grouped.items())
    batches = [records[i:i + batch_size]
               for i in range(0, len(records), batch_size)]
    lines = [
        LIFE_STAGE_ATTACK_CSHARP_HEADER,
        "// All ratios below are calculated in AnimalStats, never at game time.",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "namespace ZoologyMod",
        "{",
        "    internal static class ZoologyGeneratedLifeStageAttacks",
        "    {",
        "        private static readonly Dictionary<string, List<ZoologyLifeStageToolFactor>> Values =",
        "            new Dictionary<string, List<ZoologyLifeStageToolFactor>>(StringComparer.Ordinal);",
        "",
        "        static ZoologyGeneratedLifeStageAttacks()",
        "        {",
    ]
    for batch_number in range(len(batches)):
        lines.append(f"            AddBatch{batch_number}(Values);")
    lines.extend(["        }", ""])
    for batch_number, batch in enumerate(batches):
        lines.append(
            f"        private static void AddBatch{batch_number}("
            "Dictionary<string, List<ZoologyLifeStageToolFactor>> target)")
        lines.append("        {")
        for (species, stage), tools in batch:
            key = json.dumps(species + "\n" + stage, ensure_ascii=True)
            lines.append(f"            target.Add({key}, new List<ZoologyLifeStageToolFactor>")
            lines.append("            {")
            for vanilla_index, ce_index, label, group, capacity, factors in sorted(tools):
                cells = [f"vanillaToolIndex = {vanilla_index}",
                         f"ceToolIndex = {ce_index}",
                         f"capacities = {json.dumps(capacity, ensure_ascii=True)}"]
                if label:
                    cells.append(f"label = {json.dumps(label, ensure_ascii=True)}")
                if group:
                    cells.append(
                        f"linkedBodyPartsGroup = {json.dumps(group, ensure_ascii=True)}")
                for field, value in factors:
                    cells.append(f"{field} = {format(value, '.9g')}f")
                lines.append("                new ZoologyLifeStageToolFactor { "
                             + ", ".join(cells) + " },")
            lines.append("            });")
        lines.extend(["        }", ""])
    lines.extend([
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
    diagnostics["compiled_stage_keys"] = len(records)
    diagnostics["generated_cs_batches"] = len(batches)
    return "\n".join(lines).encode("utf-8"), dict(diagnostics)
