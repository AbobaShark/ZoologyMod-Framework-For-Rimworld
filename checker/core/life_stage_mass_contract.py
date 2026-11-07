"""Validate biological stage masses without inventing missing values.

The authoritative calculations live in AnimalStats. This module checks the
evaluated inputs, stage identities and source provenance; it does not calculate
birth masses, growth curves, speed, combat factors or BodySize.
"""
from __future__ import annotations

import math
import re
from collections import defaultdict

NAME = re.compile(r"^\s*<li>([^<>]+)</li>\s*$")
CORE = {
    "AnimalAdult or EusocialInsectAdult": ("Adult", 2),
    "AnimalJuvenile or EusocialInsectJuvenile": ("Juvenile", 1),
    "AnimalBaby or EusocialInsectLarva": ("Baby", 0),
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
    """Validate stage masses and mapped BodySize without duplicating adult data.

    The adult-stage row supplies the comparison mass and reference BodySize.
    Biological evidence methods remain attached as Google Sheets cell notes
    to MassReference, not mandatory columns in the exported numerical model.
    """
    mandatory = {
        "XML name", "LifeStageDef", "BiologicalStageMass_kg",
        "MassReference", "MassDerivedStageBodySize", "ExpectedBodySizeFactor",
    }
    absent = mandatory - set(profiles.columns)
    if absent:
        raise ValueError(f"LifeStageProfiles lacks mass contract columns: {sorted(absent)}")

    species = defaultdict(list)
    invalid, missing = [], []
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
            invalid.append(
                f"row {row_number}: unknown animal or stage {name_raw}/{stage_name}")
            continue
        family, semantic, rank = parsed
        mass = _positive(record.get("BiologicalStageMass_kg"))
        size = _positive(record.get("MassDerivedStageBodySize"))
        factor = _positive(record.get("ExpectedBodySizeFactor"))
        source = str(record.get("MassReference") or "").strip()
        species[name].append((family, semantic, rank, mass, size, factor, row_number))
        if any(value is None for value in (mass, size, factor)):
            missing.append(
                f"{name}/{stage_name} (row {row_number}): mass or derived size missing")
            continue
        if not source:
            invalid.append(f"{name}/{stage_name}: missing source reference")

    verified = 0
    for name, records in species.items():
        families = {r[0] for r in records}
        if len(families) != 1:
            invalid.append(
                f"{name}: mixed incompatible lifecycle families: {sorted(families)}")
            continue
        family = next(iter(families))
        ranks = [r[2] for r in records]
        expected = list(range(5 if family == "Royal" else 3))
        if sorted(ranks) != expected:
            invalid.append(
                f"{name}: lifecycle stages {sorted(ranks)}, expected {expected}")
            continue
        adult = next(r for r in records if r[2] == 2)
        adult_mass, adult_size = adult[3], adult[4]
        if adult_mass is not None and adult_size is not None:
            for r in records:
                rank, mass, size, factor = r[2:6]
                if any(value is None for value in (mass, size, factor)):
                    continue
                if not math.isclose(
                        size / adult_size, factor, rel_tol=2e-5, abs_tol=1e-8):
                    invalid.append(f"{name} stage {rank}: BodySize/factor mismatch")
                if rank < 2 and mass >= adult_mass:
                    invalid.append(
                        f"{name} stage {rank}: preadult mass must be below adult mass")
                if family == "Royal" and rank > 2 and mass <= adult_mass:
                    invalid.append(
                        f"{name} stage {rank}: late growth must exceed adult mass")
        masses = [r[3] for r in sorted(records, key=lambda x: x[2])]
        if all(mass is not None for mass in masses):
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
