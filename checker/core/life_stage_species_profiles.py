"""Generate species-specific LifeStageDef input data, never game-time formulas.

The generated XML describes *corrections* to resolved vanilla/mod LifeStageDefs.
Numerical values come from the same workbook that supplies the adult ThingDefs.
Attack-specific factors are accepted only from explicitly calculated per-tool
rows; missing age-specific biomechanical predictions are not fabricated.
"""
from __future__ import annotations

import math
import re
from collections import Counter, defaultdict
from lxml import etree as E

from core.body_size_mass_equivalence import body_size_equivalent_mass_kg
from core.life_stages import STAGE_GROUPS, ROYAL_STAGES, parse_stages

_XML_NAMES = re.compile(r"<li>\s*([^<>]+?)\s*</li>")
_STEP = 0.05
_HUNGER_DENOMINATOR = 4.82 * (100_000 ** 0.734)
_TOOL_FIELDS = (
    ("VanillaDamageFactor", "vanillaDamageFactor"),
    ("VanillaCooldownFactor", "vanillaCooldownFactor"),
    ("CEDamageFactor", "ceDamageFactor"),
    ("CECooldownFactor", "ceCooldownFactor"),
    ("CESharpPenetrationFactor", "ceSharpPenetrationFactor"),
    ("CEBluntPenetrationFactor", "ceBluntPenetrationFactor"),
)


def _value(row, column, what):
    if column not in row:
        raise ValueError(f"{what}: missing column {column!r}")
    return row[column]


def _number(value, what, *, zero=False):
    try:
        result = float(str(value).strip().replace(",", "."))
    except (ValueError, TypeError):
        raise ValueError(f"{what}: missing/non-numeric: {value!r}") from None
    if not math.isfinite(result) or (result < 0 if zero else result <= 0):
        raise ValueError(f"{what}: expected finite {'non-negative' if zero else 'positive'} number: {value!r}")
    return result


def _optional_number(row, column):
    value = row.get(column)
    if value is None or str(value).strip().lower() in ("", "no", "none", "nan"):
        return None
    return _number(value, column, zero=True)


def _mround_nonnegative(value, step=_STEP):
    return math.floor(value / step + 0.5 + 1e-12) * step


def _as_float(value):
    return format(value, ".12g")


def _xml_names(text):
    raw = str(text or "").strip()
    if not raw:
        return []
    names = _XML_NAMES.findall(raw)
    if not names or _XML_NAMES.sub("", raw).strip(" ,;\r\n\t"):
        raise ValueError(f"Invalid Animals XML name: {raw!r}")
    return names


def _source_rows(frame, key, label):
    if frame is None:
        raise ValueError(f"Missing required AnimalStats sheet: {label}")
    if key not in frame.columns:
        raise ValueError(f"{label}: missing {key} column")
    result = {}
    for _, row in frame.iterrows():
        value = str(row.get(key, "")).strip()
        if not value:
            continue
        if value in result:
            raise ValueError(f"{label}: duplicate lookup key {key}={value!r}")
        result[value] = row.to_dict()
    return result


def _hunger_for_body_size(body_size, physiology, ectothermic):
    """Exactly Animals!AQ's body-size formula, including game quantization."""
    s = float(body_size)
    if not math.isfinite(s) or s <= 0:
        raise ValueError(f"Invalid life-stage BodySize: {s}")
    if ectothermic:
        amount = 0.196 * (s * 100_000) ** 0.889
    elif str(physiology) == "Bird":
        amount = 10.5 * (s * 100_000) ** 0.681
    else:
        amount = 4.82 * (s * 100_000) ** 0.734
    threshold = 2.0 if s < 3.5 else (1.5 if abs(s - 3.5) < 1e-10 else 1.25)
    allometric_correction = 1 + 0.132282374779864 / (
        s ** 0.994035016789033 + 0.00905238001496948)
    return max(0.01, _mround_nonnegative(amount / _HUNGER_DENOMINATOR / threshold * allometric_correction))


def _validate_adult_ce_stats(ce_row, group, mass, species):
    """Detect stale CE source values after a workbook group/mass change."""
    for stat, group_col, exponent in (
        ("MeleeDodgeChance", "meleeDodgeChanceMultiplier", -0.165),
        ("MeleeCritChance", "meleeCritChanceMultiplier", 0.165),
        ("MeleeParryChance", "meleeParryChanceMultiplier", 0.165),
    ):
        actual = _optional_number(ce_row, stat)
        coefficient = _optional_number(group, group_col)
        if actual is None or coefficient is None:
            continue
        expected = _mround_nonnegative(coefficient * mass ** exponent)
        if not math.isclose(actual, expected, rel_tol=0, abs_tol=1e-7):
            raise ValueError(
                f"{species}: Animals CE {stat}={actual} differs from the "
                f"adult mass/group formula ({expected}); recalculate source")


def _has_spreadsheet_error(row):
    return any(str(value).strip().startswith(
        ("#REF!", "#N/A", "#DIV/0!", "#VALUE!", "#NUM!", "#NAME?", "#ERROR!"))
        for value in row.values())


def _age_stat_factors(ce_row, group, mass, diagnostics):
    entries = []
    if ce_row is None or mass is None:
        return entries
    for name, header, group_col, exponent in (
        ("MeleeDodgeChance", "MeleeDodgeChance", "meleeDodgeChanceMultiplier", -0.165),
        ("MeleeCritChance", "MeleeCritChance", "meleeCritChanceMultiplier", 0.165),
        ("MeleeParryChance", "MeleeParryChance", "meleeParryChanceMultiplier", 0.165),
    ):
        base = _optional_number(ce_row, header)
        group_coefficient = _optional_number(group, group_col)
        if base is None or group_coefficient is None:
            continue
        predicted = _mround_nonnegative(group_coefficient * mass ** exponent)
        if base == 0:
            if predicted != 0:
                diagnostics["zero_base_nonzero_age_stat"] += 1
            continue
        factor = predicted / base
        if not math.isfinite(factor) or factor < 0:
            raise ValueError(f"Invalid age-scaled {name}: {factor}")
        if abs(factor - 1) > 1e-9:
            entries.append((name, factor, True))
    return entries


def _attack_rows(frame):
    if frame is None:
        return {}
    required = {"XML name", "LifeStageDef", "ToolIndex"}
    missing = required - set(frame.columns)
    if missing:
        raise ValueError(f"LifeStageAttackProfiles: missing columns {sorted(missing)}")
    entries = defaultdict(list)
    indices = set()
    for row_number, (_, record) in enumerate(frame.iterrows(), start=2):
        row = record.to_dict()
        species = _xml_names(row.get("XML name"))
        if len(species) != 1:
            raise ValueError(f"LifeStageAttackProfiles row {row_number}: exactly one XML name required")
        stage = str(row.get("LifeStageDef", "")).strip()
        try:
            index = int(str(row.get("ToolIndex", "")).strip())
        except ValueError:
            raise ValueError(f"LifeStageAttackProfiles row {row_number}: invalid ToolIndex") from None
        if index < 0 or not stage:
            raise ValueError(f"LifeStageAttackProfiles row {row_number}: empty stage or negative ToolIndex")
        key = (species[0], stage, index)
        if key in indices:
            raise ValueError(f"LifeStageAttackProfiles: duplicate {key}")
        indices.add(key)
        factors = []
        for column, output in _TOOL_FIELDS:
            val = row.get(column)
            if val is None or str(val).strip().lower() in ("", "no", "none", "nan"):
                val = 1
            factor = _number(val, f"LifeStageAttackProfiles row {row_number}/{column}")
            if abs(factor - 1) > 1e-9:
                factors.append((output, factor))
        if not factors:
            continue
        entries[(species[0], stage)].append({
            "index": index,
            "label": str(row.get("ToolLabel", "")).strip(),
            "linked": str(row.get("LinkedBodyPartsGroup", "")).strip(),
            "factors": factors,
        })
    return entries


def render_species_life_stage_profiles(animals, animals_ce, base_stats, groups, life_stages,
                                       attack_profiles=None, *, mass_lookup=None):
    """Return (XML bytes, diagnostic counts).

    'LifeStageAttackProfiles' is optional; it must contain genuine, previously
    calculated per-tool corrections. It is not inferred from adult attack DPS.
    """
    stage_inputs = parse_stages(life_stages, mass_lookup=mass_lookup)
    base_by_latin = _source_rows(base_stats, "Species", "Base stats")
    groups_by_name = _source_rows(groups, "Name", "Groups")
    ce_by_name = {}
    if animals_ce is not None:
        for _, record in animals_ce.iterrows():
            row = record.to_dict()
            for name in _xml_names(row.get("XML name", "")):
                if name in ce_by_name:
                    raise ValueError("Animals CE: duplicated XML name " + name)
                ce_by_name[name] = row
    attacks = _attack_rows(attack_profiles)
    root = E.Element("Defs")
    catalog = E.SubElement(root, "ZoologyLifeStageProfileCatalogDef",
                           Class="ZoologyMod.ZoologyLifeStageProfileCatalogDef, ZoologyMod")
    E.SubElement(catalog, "defName").text = "Zoology_GeneratedLifeStageProfiles"
    E.SubElement(catalog, "label").text = "Zoology generated life-stage profiles"
    stage_nodes = E.SubElement(catalog, "stages")
    diagnostics = Counter()
    rendered_keys = set()
    unresolved_species = []

    for _, record in animals.iterrows():
        animal = record.to_dict()
        names = _xml_names(animal.get("XML name", ""))
        if not names:
            continue
        latin = str(animal.get("Latin name", "")).strip()
        if latin not in base_by_latin:
            raise ValueError("Animals: species absent from Base stats: " + latin)
        raw_group = str(base_by_latin[latin].get("Skill group", "")).strip()
        group = groups_by_name.get(raw_group)
        if group is None:
            raise ValueError(f"{latin}: group {raw_group!r} not found")
        # A broken imported formula must never be turned into a fabricated
        # stage value. Preserve the unmodified original stage for this race.
        # Report the source error in the staged generation diagnostics.
        if _has_spreadsheet_error(animal) or _has_spreadsheet_error(base_by_latin[latin]) or any(
            _has_spreadsheet_error(ce_by_name[name])
            for name in names if name in ce_by_name
        ):
            unresolved_species.extend(names)
            diagnostics["unresolved_source_rows"] += 1
            continue
        body_size = _number(_value(animal, "baseBodySize", latin), f"{latin} baseBodySize")
        adult_mass = _number(_value(base_by_latin[latin], "Body mass (kg)", latin),
                             f"{latin} adult mass (kg)")
        base_hunger = _number(_value(animal, "baseHungerRate", latin), f"{latin} baseHungerRate")
        for name in names:
            ce_row = ce_by_name.get(name)
            if animals_ce is not None and ce_row is None:
                raise ValueError("Animals CE lacks species: " + name)
            if ce_row is not None:
                _validate_adult_ce_stats(ce_row, group, adult_mass, name)
        adult_filth = _number(_value(animal, "FilthRate", latin), f"{latin} adult FilthRate", zero=True)
        group_filth = _number(_value(group, "filthMultiplier", raw_group), f"{raw_group} filthMultiplier", zero=True)
        physiology = str(group.get("Physiology", "")).strip()
        ectothermic = str(animal.get("Ectothermic", "")).strip().lower() in ("true", "1", "yes")
        base_expected_hunger = _hunger_for_body_size(body_size, physiology, ectothermic)
        if abs(base_expected_hunger - base_hunger) > 0.00001:
            raise ValueError(f"{latin}: Animals!AQ={base_hunger} does not match model "
                             f"({base_expected_hunger}); update source workbook first")
        expected_filth = math.ceil(body_size * group_filth - 1e-10)
        if abs(expected_filth - adult_filth) > 0.00001:
            raise ValueError(f"{latin}: Animals!K={adult_filth} differs from group "
                             f"filth model ({expected_filth})")
        is_royal = "VAERoy_Megachicken" in names
        for group_stages, stage in stage_inputs:
            royal_stage = group_stages[0].startswith("VAERoy_")
            if royal_stage != is_royal:
                continue
            factor = stage["bodySizeFactor"]
            if abs(factor - 1) <= 1e-9:
                continue
            game_stage_size = body_size * factor
            hunger_factor = _hunger_for_body_size(game_stage_size, physiology, ectothermic) / base_hunger
            stage_filth = math.ceil(game_stage_size * group_filth - 1e-10)
            filth_factor = None
            if adult_filth:
                filth_factor = stage_filth / adult_filth
            elif stage_filth:
                diagnostics["zero_base_nonzero_age_filth"] += 1

            mass = None
            try:
                mass = (mass_lookup or body_size_equivalent_mass_kg)(game_stage_size)
            except ValueError:
                diagnostics["mass_below_or_above_mapped_bins"] += 1

            for name in names:
                ce_row = ce_by_name.get(name)
                if animals_ce is not None and ce_row is None:
                    raise ValueError("Animals CE lacks species: " + name)
                stats = []
                if filth_factor is not None and abs(filth_factor - 1) > 1e-9:
                    stats.append(("FilthRate", filth_factor, False))
                stats.extend(_age_stat_factors(ce_row, group, mass, diagnostics))
                for source_stage in group_stages:
                    profile_key = (name, source_stage)
                    if profile_key in rendered_keys:
                        raise ValueError("Duplicate age profile " + repr(profile_key))
                    rendered_keys.add(profile_key)
                    entry = E.SubElement(stage_nodes, "li")
                    E.SubElement(entry, "animalDefName").text = name
                    E.SubElement(entry, "sourceStageDefName").text = source_stage
                    E.SubElement(entry, "expectedBaseBodySize").text = _as_float(body_size)
                    E.SubElement(entry, "expectedBaseHungerRate").text = _as_float(base_hunger)
                    E.SubElement(entry, "expectedBodySizeFactor").text = _as_float(factor)
                    E.SubElement(entry, "hungerRateFactor").text = _as_float(hunger_factor)
                    if stats:
                        items = E.SubElement(entry, "statFactors")
                        for stat_name, value, ce_only in stats:
                            item = E.SubElement(items, "li")
                            E.SubElement(item, "stat").text = stat_name
                            E.SubElement(item, "factor").text = _as_float(value)
                            if ce_only:
                                E.SubElement(item, "ceOnly").text = "true"
                    attack_entries = attacks.get(profile_key)
                    if attack_entries:
                        tool_list = E.SubElement(entry, "tools")
                        for a in sorted(attack_entries, key=lambda x: x["index"]):
                            tool = E.SubElement(tool_list, "li")
                            E.SubElement(tool, "toolIndex").text = str(a["index"])
                            if a["label"]:
                                E.SubElement(tool, "label").text = a["label"]
                            if a["linked"]:
                                E.SubElement(tool, "linkedBodyPartsGroup").text = a["linked"]
                            for field, value in a["factors"]:
                                E.SubElement(tool, field).text = _as_float(value)
                    diagnostics["profiles"] += 1
    unknown_attacks = set(attacks) - rendered_keys
    if unknown_attacks:
        raise ValueError("Unmatched LifeStageAttackProfiles keys: " + repr(sorted(unknown_attacks)[:15]))
    xml = E.tostring(root, encoding="utf-8", xml_declaration=True, pretty_print=True)
    summary = dict(diagnostics)
    if unresolved_species:
        summary["unresolved_species"] = sorted(set(unresolved_species))
    return xml, summary
