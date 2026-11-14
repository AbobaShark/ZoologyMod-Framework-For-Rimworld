"""Audit evaluated mean-sex inputs in AnimalStats without deriving biomechanics.

These checks prepare the LifeStageProfiles data contract. No biological model,
adult stage, combat coefficient or runtime scaling is generated from mass here.
"""
from __future__ import annotations

import math
from collections import Counter

from core.life_stage_species_profiles import _xml_names

_MASS_COLUMNS = ("BaselineMass_kg", "MaleMean_kg", "FemaleMean_kg")
_MEAN_COLUMNS = (
    ("MaleMeanBodySize", "FemaleMeanBodySize"),
    ("MaleMeanHealthFactor", "FemaleMeanHealthFactor"),
    ("MaleMeanSpeedFactor", "FemaleMeanSpeedFactor"),
    ("MaleMeanLinearScale", "FemaleMeanLinearScale"),
    ("MaleMeanHungerRateFactor", "FemaleMeanHungerRateFactor"),
    ("MaleMeanFilthRateFactor", "FemaleMeanFilthRateFactor"),
    ("MaleMeanCE_ArmorDurabilityFactor", "FemaleMeanCE_ArmorDurabilityFactor"),
)
_SOURCE_ERRORS = ("#REF!", "#N/A", "#DIV/0!", "#VALUE!", "#NUM!", "#NAME?", "#ERROR!")


def _value(record, column, context, *, zero=False):
    raw = record.get(column)
    if raw is None or str(raw).strip() == "":
        raise ValueError(f"{context}/{column}: missing evaluated numeric value")
    if isinstance(raw, bool) or str(raw).strip().upper().startswith(_SOURCE_ERRORS):
        raise ValueError(f"{context}/{column}: invalid formula result {raw!r}")
    try:
        value = float(str(raw).replace(",", ".").strip())
    except (ValueError, TypeError):
        raise ValueError(f"{context}/{column}: expected evaluated number, got {raw!r}") from None
    if not math.isfinite(value) or value < 0 or (not zero and value == 0):
        raise ValueError(f"{context}/{column}: invalid evaluated number {raw!r}")
    return value


def _gender(value, context):
    if isinstance(value, bool):
        return value
    if str(value).strip().upper() in ("TRUE", "FALSE"):
        return str(value).strip().upper() == "TRUE"
    raise ValueError(f"{context}: SexualDimorphism must be TRUE or FALSE")


def _animal_index(animals):
    if animals is None or "XML name" not in animals.columns:
        raise ValueError("Animals must contain XML name")
    lookup = {}
    for index, record in animals.iterrows():
        raw = record.get("XML name", "")
        if raw is None or str(raw).strip() in ("", "No"):
            continue
        for name in _xml_names(raw):
            if name in lookup:
                raise ValueError(f"Animals duplicates ThingDef {name}")
            lookup[name] = record
    return lookup


def _base_index(base_stats):
    if base_stats is None:
        return None
    required = {"Species", "Body mass (kg)"}
    missing = required - set(base_stats.columns)
    if missing:
        raise ValueError(f"Base stats missing {sorted(missing)}")
    lookup = {}
    for _, record in base_stats.iterrows():
        species = str(record.get("Species") or "").strip()
        if not species:
            continue
        mass = _value(record, "Body mass (kg)", species)
        old = lookup.get(species)
        if old is not None and not math.isclose(old, mass, rel_tol=0, abs_tol=1e-8):
            raise ValueError(f"Base stats contradicts itself for {species}")
        lookup[species] = mass
    return lookup


def audit_mean_sex_dimorphism(sex, animals, base_stats=None, *, require_complete=True):
    """Check source identity and evaluated adult means; never use ranges/tails.

    Assumes native floating precision for the invariant; deliberately permits
    only floating-point representation drift, not independently rounded means.
    The source workbook is free to change its biological formulas, while this
    validator stays an identity/type contract.
    """
    if sex is None:
        raise ValueError("SexDimorphism source is required")
    required = {"XML name", "Latin name", "SexualDimorphism", *_MASS_COLUMNS}
    required.update(column for pair in _MEAN_COLUMNS for column in pair)
    missing = required - set(sex.columns)
    if missing:
        raise ValueError(f"SexDimorphism missing evaluated means: {sorted(missing)}")

    animal_index = _animal_index(animals)
    base_index = _base_index(base_stats)
    stats = Counter()
    seen = set()
    for number, (_, record) in enumerate(sex.iterrows(), start=2):
        raw = record.get("XML name")
        if raw is None or str(raw).strip() == "":
            continue
        names = _xml_names(raw)
        if len(names) != 1:
            raise ValueError(f"SexDimorphism row {number}: expected exactly one ThingDef")
        name = names[0]
        if name in seen:
            raise ValueError(f"SexDimorphism duplicates ThingDef {name}")
        seen.add(name)
        if name not in animal_index:
            raise ValueError(f"SexDimorphism/{name}: absent from Animals")

        dimorphic = _gender(record.get("SexualDimorphism"), name)
        baseline, male, female = (_value(record, column, name)
                                  for column in _MASS_COLUMNS)
        if not math.isclose(male + female, 2 * baseline,
                            abs_tol=1e-8, rel_tol=1e-12):
            raise ValueError(f"{name}: (MaleMean + FemaleMean)/2 must equal BaselineMass")

        latin = str(record.get("Latin name") or "").strip()
        if base_index is not None and latin in base_index:
            if not math.isclose(baseline, base_index[latin],
                                rel_tol=1e-12, abs_tol=1e-9):
                raise ValueError(f"{name}: BaselineMass disagrees with Base stats")
        if not dimorphic and not math.isclose(male, female,
                                              rel_tol=1e-12, abs_tol=1e-9):
            raise ValueError(f"{name}: monomorphic mean masses differ")
        if dimorphic and math.isclose(male, female,
                                      rel_tol=1e-12, abs_tol=1e-9):
            raise ValueError(f"{name}: dimorphism TRUE but adult mean masses match")

        for male_column, female_column in _MEAN_COLUMNS:
            allow_zero = "Filth" in male_column or "ArmorDurability" in male_column
            m = _value(record, male_column, name, zero=allow_zero)
            f = _value(record, female_column, name, zero=allow_zero)
            if not dimorphic and not math.isclose(m, f, rel_tol=0, abs_tol=1e-10):
                raise ValueError(f"{name}: monomorphic {male_column}/{female_column} differ")

        if not dimorphic:
            adult = animal_index[name]
            if "baseBodySize" in adult.index:
                body = _value(adult, "baseBodySize", name)
                mean_body = _value(record, "MaleMeanBodySize", name)
                if not math.isclose(body, mean_body, rel_tol=1e-9, abs_tol=1e-8):
                    raise ValueError(f"{name}: monomorphic BodySize disagrees with Animals")
        stats["dimorphic" if dimorphic else "monomorphic"] += 1
    stats["species"] = len(seen)
    if require_complete and not seen:
        raise ValueError("SexDimorphism contains no evaluated animal means")
    return dict(stats)
