"""Validate evaluated sex-specific adult endpoints from canonical AnimalStats.

No biomechanics, growth rates or size distributions are evaluated in Checker.
AnimalStats SERVICE_LifeStageCalculations supplies each mean-sex tool result;
Checker checks identities and numeric completeness before export.
"""
from __future__ import annotations

import math

from core.life_stage_species_profiles import _xml_names
from core.sex_dimorphism import _animal_index, _gender, _value

_COLUMNS = {
    "AdultBodySize": ("MaleMeanBodySize", "FemaleMeanBodySize"),
    "HealthScaleFactor": ("MaleMeanHealthFactor", "FemaleMeanHealthFactor"),
    "SpeedFactor": ("MaleMeanSpeedFactor", "FemaleMeanSpeedFactor"),
    "HungerRateFactor": ("MaleMeanHungerRateFactor", "FemaleMeanHungerRateFactor"),
    "FilthRateFactor": ("MaleMeanFilthRateFactor", "FemaleMeanFilthRateFactor"),
    "CE_ArmorDurabilityFactor": (
        "MaleMeanCE_ArmorDurabilityFactor", "FemaleMeanCE_ArmorDurabilityFactor"),
    "LinearScale": ("MaleMeanLinearScale", "FemaleMeanLinearScale"),
}
_OPTIONAL_NUMBERS = (
    "CE_MeleeDodgeChance", "CE_MeleeCritChance", "CE_MeleeParryChance",
    "VanillaArmorSharp", "VanillaArmorBlunt",
)
_REQUIRED_NUMBERS = (
    "BodySizeFactor", "CE_SpeedFactor",
    "CombatPowerFactor", "MarketValueFactor",
    "CE_ArmorSharp", "CE_ArmorBlunt",
)
_SOURCE_ERRORS = ("#REF!", "#N/A", "#DIV/0!", "#VALUE!", "#NUM!", "#NAME?", "#ERROR!")


def _tools(raw, expected, context):
    if raw is None or str(raw).strip() == "":
        raise ValueError(f"{context}: no calculated natural Tool records")
    lines = str(raw).splitlines()
    count = _value({"count": expected}, "count", context)
    if not count.is_integer() or int(count) != len(lines):
        raise ValueError(f"{context}: ToolCount and ToolRecords9 disagree")
    seen = set()
    for line in lines:
        fields = line.split("\t")
        if len(fields) != 9:
            raise ValueError(f"{context}: each ToolRecords9 entry must contain nine cells")
        try:
            ordinal = int(fields[0])
        except (ValueError, TypeError):
            raise ValueError(f"{context}: invalid tool ordinal {fields[0]!r}") from None
        if ordinal < 0 or ordinal in seen:
            raise ValueError(f"{context}: duplicate or negative Tool ordinal {ordinal}")
        seen.add(ordinal)
        if not fields[1].strip() or not fields[2].strip():
            raise ValueError(f"{context}: missing tool label or linked-body-part group")
        for index, source in enumerate(fields[3:]):
            try:
                value = float(source.strip().replace(",", "."))
            except (ValueError, TypeError):
                raise ValueError(f"{context}: nonnumeric Tool component {source!r}") from None
            if not math.isfinite(value) or value < 0 or (index in (1, 3) and value == 0):
                raise ValueError(f"{context}: invalid Tool component {source!r}")
    return len(lines)


def _market_price_flags(sex, base_stats):
    """Map species' explicit Base stats switches to their mean-sex XML names.

    This is the only authority for enabling sex-dependent sale prices. Missing
    input is allowed only for isolated fixtures; the production pipeline passes
    the canonical Base stats table.
    """
    if base_stats is None:
        return {}
    for column in ("Species", "UseSexSpecificMarketValue"):
        if column not in base_stats.columns:
            raise ValueError(f"Base stats missing required field {column}")
    if "Latin name" not in sex.columns:
        raise ValueError("SexDimorphism missing Latin name")

    species = {}
    for _, row in base_stats.iterrows():
        latin = str(row.get("Species") or "").strip()
        if not latin or latin.lower() in ("nan", "none"):
            continue
        if latin in species:
            raise ValueError(f"Base stats duplicates Latin species {latin}")
        raw = row.get("UseSexSpecificMarketValue")
        if isinstance(raw, bool):
            enabled = raw
        elif isinstance(raw, (int, float)) and raw in (0, 1):
            enabled = bool(raw)
        elif str(raw).strip().upper() in ("TRUE", "FALSE"):
            enabled = str(raw).strip().upper() == "TRUE"
        else:
            raise ValueError(f"Base stats/{latin}: invalid UseSexSpecificMarketValue {raw!r}")
        species[latin] = enabled

    flags = {}
    for _, row in sex.iterrows():
        names = _xml_names(row.get("XML name"))
        if not names or not _gender(row.get("SexualDimorphism"), names[0]):
            continue
        if len(names) != 1:
            raise ValueError("SexDimorphism must list one animal per row")
        latin = str(row.get("Latin name") or "").strip()
        if latin not in species:
            raise ValueError(f"SexDimorphism/{names[0]}: missing Base stats price switch for {latin}")
        flags[names[0]] = species[latin]
    return flags


def audit_sex_adult_profiles(profiles, sex, animals, *, require_complete=True, base_stats=None):
    """Require exactly two canonical, precomputed endpoints for dimorphic species.

    Do not permit SexAdultProfiles for monomorphic animals. Mass minima/maxima
    and prolonged-adult parameters have no effect on this calculation contract.
    """
    if profiles is None or sex is None:
        raise ValueError("SexAdultProfiles and SexDimorphism are required")
    required = {
        "XML name", "Sex", "AdultMass_kg", "BaselineMass_kg",
        "ToolRecords9", "ToolCount", "CalculationStatus", "GroupName",
        *_COLUMNS.keys(), *_REQUIRED_NUMBERS,
    }
    missing = required - set(profiles.columns)
    if missing:
        raise ValueError(f"SexAdultProfiles missing fields: {sorted(missing)}")
    sex_required = {
        "XML name", "SexualDimorphism", "BaselineMass_kg",
        "MaleMean_kg", "FemaleMean_kg",
        *(column for pair in _COLUMNS.values() for column in pair),
    }
    if missing := sex_required - set(sex.columns):
        raise ValueError(f"SexDimorphism missing fields: {sorted(missing)}")

    adult = _animal_index(animals)
    price_flags = _market_price_flags(sex, base_stats)
    dimorphic = {}
    for _, source in sex.iterrows():
        raw = source.get("XML name")
        if raw is None or str(raw).strip() == "":
            continue
        names = _xml_names(raw)
        if len(names) != 1:
            raise ValueError("SexDimorphism must use one XML name per row")
        key = names[0]
        if key in dimorphic:
            raise ValueError(f"SexDimorphism duplicates ThingDef {key}")
        if _gender(source.get("SexualDimorphism"), key):
            dimorphic[key] = source

    seen = set()
    tools_count = 0
    for _, record in profiles.iterrows():
        raw = record.get("XML name")
        if raw is None or str(raw).strip() == "":
            continue
        names = _xml_names(raw)
        if len(names) != 1:
            raise ValueError("SexAdultProfiles row must contain one ThingDef")
        name = names[0]
        if name not in dimorphic:
            raise ValueError(f"SexAdultProfiles/{name}: no matching dimorphic species")
        if name not in adult:
            raise ValueError(f"SexAdultProfiles/{name}: missing Animals baseline")
        gender = str(record.get("Sex") or "").strip()
        if gender not in ("Male", "Female"):
            raise ValueError(f"{name}: sex must be Male or Female")
        key = (name, gender)
        if key in seen:
            raise ValueError(f"duplicate SexAdultProfiles species/sex: {key}")
        seen.add(key)
        context = f"{name}/{gender}"
        if str(record.get("CalculationStatus") or "").upper() != "READY":
            raise ValueError(f"{context}: adult endpoint is not READY")
        if not str(record.get("GroupName") or "").strip():
            raise ValueError(f"{context}: missing biological group")

        source = dimorphic[name]
        expected_mass = _value(
            source, "MaleMean_kg" if gender == "Male" else "FemaleMean_kg", context)
        mass = _value(record, "AdultMass_kg", context)
        baseline = _value(record, "BaselineMass_kg", context)
        expected_baseline = _value(source, "BaselineMass_kg", context)
        if not math.isclose(mass, expected_mass, rel_tol=1e-12, abs_tol=1e-8):
            raise ValueError(f"{context}: mass differs from SexDimorphism mean")
        if not math.isclose(baseline, expected_baseline, rel_tol=1e-12, abs_tol=1e-8):
            raise ValueError(f"{context}: baseline mass mismatch")

        for field, mean_columns in _COLUMNS.items():
            expected = _value(source, mean_columns[gender == "Female"], context,
                              zero=field in ("FilthRateFactor", "CE_ArmorDurabilityFactor"))
            actual = _value(record, field, context,
                            zero=field in ("FilthRateFactor", "CE_ArmorDurabilityFactor"))
            if not math.isclose(expected, actual, rel_tol=1e-9, abs_tol=1e-9):
                raise ValueError(f"{context}: {field} disagrees with mean-sex table")

        base_body = _value(adult[name], "baseBodySize", context)
        body = _value(record, "AdultBodySize", context)
        factor = _value(record, "BodySizeFactor", context)
        if not math.isclose(body, factor * base_body, rel_tol=1e-10, abs_tol=1e-8):
            raise ValueError(f"{context}: BodySize factor disagrees with Animals")

        for field in _REQUIRED_NUMBERS:
            _value(record, field, context, zero=field.startswith("CE_Armor"))
        if price_flags and not price_flags[name] and not math.isclose(
                _value(record, "MarketValueFactor", context), 1.,
                rel_tol=0, abs_tol=1e-10):
            raise ValueError(
                f"{context}: market value must be 1 when UseSexSpecificMarketValue is FALSE")
        for field in _OPTIONAL_NUMBERS:
            raw_number = record.get(field)
            if raw_number is not None and str(raw_number).strip() not in ("", "No"):
                _value(record, field, context, zero=True)
        tools_count += _tools(record.get("ToolRecords9"),
                              record.get("ToolCount"), context)

    if require_complete:
        expected = {(name, gender) for name in dimorphic
                    for gender in ("Male", "Female")}
        if expected != seen:
            missing_pairs = sorted(expected - seen)
            raise ValueError(f"Missing mean-sex adult endpoints: {missing_pairs[:8]}")
        if not seen:
            raise ValueError("SexAdultProfiles has no READY endpoints")

    return {
        "dimorphic_species": len(dimorphic),
        "adult_endpoints": len(seen),
        "natural_tools": tools_count,
    }
