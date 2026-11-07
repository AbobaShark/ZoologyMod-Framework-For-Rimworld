"""Validate biological stage masses without inventing missing values.

The authoritative calculations live in AnimalStats. This module checks the
evaluated inputs, stage identities and source provenance; it does not calculate
birth masses, growth curves, speed, combat factors or BodySize.
"""
from __future__ import annotations

import math
import re
from collections import defaultdict

NAME = re.compile(r"^\\s*<li>([^<>]+)</li>\\s*$")
CORE = {
    "AnimalAdult or EusocialInsectAdult": ("Adult", 2),
    "AnimalJuvenile or EusocialInsectJuvenile": ("Juvenile", 1),
    "AnimalBaby or EusocialInsectLarva": ("Baby", 0),
    # The original Tiny Def is an optional alias of Baby, never another age.
    "AnimalBaby or EusocialInsectLarva or AnimalBabyTiny": ("Baby", 0),
}
ROYAL = (
    "VAERoy_AnimalBaby", "VAERoy_AnimalJuvenile", "VAERoy_AnimalAdult",
    "VAERoy_AnimalAdultIntermediate", "VAERoy_AnimalAdultMax",
)
SPECTRO = {
    "Predimago": (
        "SpectrobiontaPredimagoSmall", "SpectrobiontaPredimago",
        "SpectrobiontaImago",
    ),
    "Larva": (
        "SpectrobiontaLarvaSmall", "SpectrobiontaLarvaMedium",
        "SpectrobiontaLarvaLarge",
    ),
}
METHODS = {
    "DIRECT", "REAL_ANALOG", "PHYLOGENETIC_ANALOG", "GROUP_ALLOMETRY",
    "SPECIALIZED_RECONSTRUCTION",
}


def _positive(value):
    if value is None or str(value).strip() == "":
        return None
    try:
        x = float(str(value).replace(",", "."))
    except (ValueError, TypeError):
        return None
    return x if math.isfinite(x) and x > 0 else None


def _name(value):
    match = NAME.fullmatch(str(value or ""))
    return match.group(1).strip() if match else None


def _stage(label):
    label = str(label or "").strip()
    if label in CORE:
        return ("Core", *CORE[label])
    if label in ROYAL:
        return ("Royal", label, ROYAL.index(label))
    for family, stages in SPECTRO.items():
        if label in stages:
            return (family, label, stages.index(label))
    return None


def audit_mass_profiles(profiles, *, require_complete=False):
    """Audit evaluated LifeStageProfiles and optionally reject unfinished data.

    Only an original Tiny LifeStageDef can survive as an alias of the single
    Baby profile. The separate AnimalBabyTiny row is always a violation.
    This function can audit an in-progress workbook without changing its
    installed XML. Set require_complete=True at the migration cutover.
    """
    mandatory = {
        "XML name", "LifeStageDef", "AdultMass_kg", "BiologicalStageMass_kg",
        "MassEvidenceMethod", "MassReference", "MassDerivedStageBodySize",
        "MassDerivedBodySizeFactor", "ExpectedBaseBodySize",
    }
    absent = mandatory - set(profiles.columns)
    if absent:
        raise ValueError(f"LifeStageProfiles lacks mass contract columns: {sorted(absent)}")

    species = defaultdict(list)
    invalid = []
    missing = []
    stage_rows = 0

    for row_number, (_, record) in enumerate(profiles.iterrows(), start=2):
        name_raw = record.get("XML name")
        if not name_raw or str(name_raw).strip().lower() in ("", "nan", "none"):
            continue
        stage_rows += 1
        name = _name(name_raw)
        stage_name = str(record.get("LifeStageDef") or "").strip()
        parsed = _stage(stage_name)
        if not name or not parsed:
            invalid.append(f"row {row_number}: unknown animal or stage {name_raw}/{stage_name}")
            continue

        family, semantic, rank = parsed
        adult = _positive(record.get("AdultMass_kg"))
        mass = _positive(record.get("BiologicalStageMass_kg"))
        size = _positive(record.get("MassDerivedStageBodySize"))
        factor = _positive(record.get("MassDerivedBodySizeFactor"))
        base_size = _positive(record.get("ExpectedBaseBodySize"))
        method = str(record.get("MassEvidenceMethod") or "").strip()
        reference = str(record.get("MassReference") or "").strip()

        species[name].append((family, semantic, rank, adult, mass, row_number))
        if any(x is None for x in (adult, mass, size, factor, base_size)):
            missing.append(f"{name}/{stage_name} (row {row_number}): mass or derived size missing")
            continue

        if not math.isclose(size / base_size, factor, rel_tol=2e-5, abs_tol=1e-8):
            invalid.append(f"{name}/{stage_name}: derived BodySize/factor mismatch")

        is_adult = rank == (4 if family == "Royal" else 2)
        if is_adult and not math.isclose(mass, adult, rel_tol=1e-6):
            # A megachicken can have radically different late-age masses;
            # its reference adult mass is the final-stage mass, not the
            # ancestor's base mass in 'Base stats'.
            invalid.append(f"{name}/{stage_name}: terminal mass must equal AdultMass_kg")
        if not is_adult and mass >= adult:
            invalid.append(f"{name}/{stage_name}: young mass must be below adult mass")
        if is_adult and method != "ADULT_ANIMALSTATS":
            invalid.append(f"{name}/{stage_name}: missing adult mass provenance")
        if not is_adult and method not in METHODS:
            invalid.append(f"{name}/{stage_name}: unknown evidence method {method!r}")
        if not reference:
            invalid.append(f"{name}/{stage_name}: missing source reference")

    verified = 0
    for name, records in species.items():
        families = {r[0] for r in records}
        if len(families) != 1:
            invalid.append(f"{name}: mixed incompatible lifecycle families: {sorted(families)}")
            continue
        family = next(iter(families))
        ranks = [r[2] for r in records]
        expected = list(range(5 if family == "Royal" else 3))
        if sorted(ranks) != expected:
            invalid.append(f"{name}: lifecycle stages {sorted(ranks)}, expected {expected}")
            continue
        adults = [r[3] for r in records]
        masses = [r[4] for r in sorted(records, key=lambda x: x[2])]
        if any(a is not None and not math.isclose(a, adults[0], rel_tol=1e-8)
               for a in adults if adults[0] is not None):
            invalid.append(f"{name}: inconsistent adult mass across stages")
        if all(m is not None for m in masses):
            if not all(a < b for a, b in zip(masses, masses[1:])):
                invalid.append(f"{name}: stage masses must strictly increase")
            else:
                verified += 1

    summary = {
        "species": len(species),
        "stage_rows": stage_rows,
        "complete_mass_series": verified,
        "missing_count": len(missing),
        "invalid_count": len(invalid),
        "missing_examples": missing[:30],
        "invalid_examples": invalid[:30],
    }
    if require_complete and (missing or invalid or not species):
        raise ValueError(
            "Incomplete biological LifeStageProfiles: "
            f"{len(missing)} missing and {len(invalid)} invalid. "
            + "; ".join((invalid + missing)[:8])
        )
    return summary
