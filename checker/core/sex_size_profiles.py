"""Serialize the evaluated SexDimorphism table without inventing biology.

The workbook owns masses, bounds and the stat/CE coefficients for the mean
and both extrema, including effective Adult and LargeAdult bounds.
"""
from __future__ import annotations

import math
from lxml import etree as E

from core.life_stage_species_profiles import _xml_names, _required


_ADULT_COLUMNS = {
    "bodySize": ("{sex}MinBodySize", "{sex}MeanBodySize", "{sex}MaxBodySize"),
    "healthScale": ("{sex}MinHealthFactor", "{sex}MeanHealthFactor", "{sex}MaxHealthFactor"),
    "moveSpeed": ("{sex}MinSpeedFactor", "{sex}MeanSpeedFactor", "{sex}MaxSpeedFactor"),
    "linearScale": ("{sex}MinLinearScale", "{sex}MeanLinearScale", "{sex}MaxLinearScale"),
    "hungerRate": ("{sex}MinHungerRateFactor", "{sex}MeanHungerRateFactor", "{sex}MaxHungerRateFactor"),
    "filthRate": ("{sex}MinFilthRateFactor", "{sex}MeanFilthRateFactor", "{sex}MaxFilthRateFactor"),
    "armorDurability": ("{sex}MinCE_ArmorDurabilityFactor",
                        "{sex}MeanCE_ArmorDurabilityFactor",
                        "{sex}MaxCE_ArmorDurabilityFactor"),
}
_LARGE_COLUMNS = {
    "bodySize": ("{sex}LargeMinBodySize", "{sex}LargeAdultBodySize", "{sex}LargeMaxBodySize"),
    "healthScale": ("{sex}LargeMinHealthFactor", "{sex}LargeAdultHealthFactor", "{sex}LargeMaxHealthFactor"),
    "moveSpeed": ("{sex}LargeMinSpeedFactor", "{sex}LargeAdultSpeedFactor", "{sex}LargeMaxSpeedFactor"),
    "linearScale": ("{sex}LargeMinLinearScale", "{sex}LargeAdultLinearScale", "{sex}LargeMaxLinearScale"),
    "hungerRate": ("{sex}LargeMinHungerFactor", "{sex}LargeAdultHungerRateFactor", "{sex}LargeMaxHungerFactor"),
    "filthRate": ("{sex}LargeMinFilthFactor", "{sex}LargeAdultFilthRateFactor", "{sex}LargeMaxFilthFactor"),
    "armorDurability": ("{sex}LargeMinCEDurabilityFactor",
                        "{sex}LargeAdultCE_ArmorDurabilityFactor",
                        "{sex}LargeMaxCEDurabilityFactor"),
}


def _number(value, where):
    number = _required(value, where)
    if not math.isfinite(number):
        raise ValueError(f"{where}: non-finite AnimalStats output")
    return format(number, ".12g")


def _boolean(value, where):
    if isinstance(value, bool):
        return value
    s = str(value).strip().lower()
    if s in ("true", "1"):
        return True
    if s in ("false", "0"):
        return False
    raise ValueError(f"{where}: expected TRUE or FALSE")


def _xml(parent, tag, value):
    E.SubElement(parent, tag).text = str(value)


def _stage(parent, row, species, sex, is_large):
    stage = E.SubElement(parent, "largeAdult" if is_large else "adult")
    sexName = sex.title()
    min_name = f"{sexName}LargeAdultMinEffective_kg" if is_large else f"{sexName}Min_kg"
    mean_name = f"{sexName}LargeAdultMass_kg" if is_large else f"{sexName}Mean_kg"
    max_name = f"{sexName}Max_kg" if is_large else (
        f"{sexName}AdultMaxEffective_kg" if _boolean(
            row["ProlongedAdultGrowth"], species + "/ProlongedAdultGrowth")
        else f"{sexName}Max_kg")
    minimum = float(_number(row[min_name], species + "/" + min_name))
    mean = float(_number(row[mean_name], species + "/" + mean_name))
    maximum = float(_number(row[max_name], species + "/" + max_name))
    reference = float(_number(row[f"{sexName}Max_kg"], species + "/referenceMax"))
    if not 0 < minimum <= mean <= maximum <= reference:
        raise ValueError(
            f"{species}/{sex}: invalid {'LargeAdult' if is_large else 'Adult'} "
            f"mass bounds {minimum}, {mean}, {maximum} (reference {reference})")
    for key, value in (
        ("minMassKg", minimum),
        ("meanMassKg", mean),
        ("maxMassKg", maximum),
        ("referenceMaxMassKg", reference),
    ):
        _xml(stage, key, _number(value, species + "/" + key))
    mapping = _LARGE_COLUMNS if is_large else _ADULT_COLUMNS
    for idx, name in enumerate(("minimum", "average", "maximum")):
        values = E.SubElement(stage, name)
        for property_name, columns in mapping.items():
            column = columns[idx].format(sex=sexName)
            _xml(values, property_name, _number(row[column], species + "/" + column))


def render_sex_size_profiles(sex_profiles, *, expected_species=None):
    if sex_profiles is None:
        raise ValueError("SexDimorphism is required for sex-size generation")
    columns = {
        "XML name", "SexualDimorphism", "ProlongedAdultGrowth",
        "BaselineMass_kg", "MaleMean_kg", "FemaleMean_kg",
        "MaleMin_kg", "FemaleMin_kg", "MaleMax_kg", "FemaleMax_kg",
        "LargeAdultRimworldAge_years", "MaleAdultMaxEffective_kg",
        "FemaleAdultMaxEffective_kg", "MaleLargeAdultMinEffective_kg",
        "FemaleLargeAdultMinEffective_kg",
    }
    for mapping in (_ADULT_COLUMNS, _LARGE_COLUMNS):
        for variants in mapping.values():
            columns.update(v.format(sex=sex) for sex in ("Male", "Female")
                           for v in variants)
    missing = columns - set(sex_profiles.columns)
    if missing:
        raise ValueError(f"SexDimorphism missing numerical columns: {sorted(missing)}")

    root = E.Element("Defs")
    catalog = E.SubElement(root, "ZoologySexSizeCatalogDef",
                           Class="ZoologyMod.ZoologySexSizeCatalogDef, ZoologyMod")
    _xml(catalog, "defName", "Zoology_GeneratedSexSizeProfiles")
    entries = E.SubElement(catalog, "profiles")
    seen = set()
    active = late = 0
    for number, (_, record) in enumerate(sex_profiles.iterrows(), 2):
        row = record.to_dict()
        names = _xml_names(row["XML name"])
        for species in names:
            if species in seen:
                raise ValueError(f"SexDimorphism duplicate {species} at row {number}")
            seen.add(species)
            dimorphic = _boolean(row["SexualDimorphism"], species + "/dimorphism")
            prolonged = _boolean(row["ProlongedAdultGrowth"], species + "/growth")
            if dimorphic:
                active += 1
            if prolonged:
                late += 1
                _number(row["LargeAdultRimworldAge_years"], species + "/gameAge")
            item = E.SubElement(entries, "li")
            _xml(item, "animalDefName", species)
            _xml(item, "sexualDimorphism", "true" if dimorphic else "false")
            _xml(item, "prolongedAdultGrowth", "true" if prolonged else "false")
            _xml(item, "baselineMassKg", _number(row["BaselineMass_kg"], species + "/baseMass"))
            for sex in ("male", "female"):
                sexElement = E.SubElement(item, sex)
                _stage(sexElement, row, species, sex, is_large=False)
                if prolonged:
                    _stage(sexElement, row, species, sex, is_large=True)
    if expected_species is not None and seen != set(expected_species):
        absent = sorted(set(expected_species) - seen)
        unexpected = sorted(seen - set(expected_species))
        raise ValueError(
            f"SexDimorphism/Animals XML coverage mismatch: "
            f"missing {absent[:12]}, unexpected {unexpected[:12]}")
    return E.tostring(root, encoding="utf-8", xml_declaration=True,
                      pretty_print=True), {
        "sex_size_profiles": len(seen),
        "functional_dimorphism": active,
        "prolonged_adult_growth": late,
    }
