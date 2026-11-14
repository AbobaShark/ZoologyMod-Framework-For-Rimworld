"""Validate and serialize evaluated AnimalStats reproductive energetics."""
from __future__ import annotations
import math
import re
from lxml import etree as E

DESTINATION = "Core/Defs/Misc/EnergeticsProfiles_Generated.xml"
OUTPUT_ID = "products.energetics"
REQUIRED = {
    "XML name", "Adult base feed (N/d)", "Reproduction mode", "Reproductive coverage",
    "Gestation (d)", "Gestation budget (N)", "Pregnancy stage 1 (N/d)",
    "Pregnancy stage 2 (N/d)", "Pregnancy stage 3 (N/d)", "Egg interval (d)",
    "Clutch budget (N)", "Gravid extra (N/d)", "Egg nutrition / ovum (N)",
}

def number(row, column):
    try:
        value = float(str(row[column]).replace(",", "."))
    except (TypeError, ValueError) as exc:
        raise ValueError(f"{row['XML name']}: invalid {column}") from exc
    if not math.isfinite(value) or value <= 0:
        raise ValueError(f"{row['XML name']}: nonpositive or nonfinite {column}")
    return value

def verify_budget(actual, expected, identity):
    if not math.isclose(actual, expected, rel_tol=1e-7, abs_tol=1e-9):
        raise ValueError(f"{identity}: inconsistent reproductive energy budget")

def render_energetics_profiles(frame):
    missing = REQUIRED - set(frame.columns)
    if missing:
        raise ValueError(f"EnergeticsProfiles missing columns: {sorted(missing)}")
    root = E.Element("Defs")
    catalog = E.SubElement(root, "ZoologyEnergeticsCatalogDef",
                           Class="ZoologyMod.ZoologyEnergeticsCatalogDef, ZoologyMod")
    E.SubElement(catalog, "defName").text = "Zoology_GeneratedEnergeticsProfiles"
    records = E.SubElement(catalog, "profiles")
    seen = set()
    diagnostics = {"ready": 0, "unresolved": []}
    for _, row in frame.iterrows():
        raw = str(row["XML name"]).strip()
        if not raw or raw in ("nan", "None"):
            continue
        match = re.fullmatch(r"<li>\s*([A-Za-z0-9_.-]+)\s*</li>", raw)
        if not match or match[1] in seen:
            raise ValueError(f"Duplicate or invalid EnergeticsProfiles identity: {raw}")
        name = match[1]
        seen.add(name)
        status = str(row["Reproductive coverage"]).strip()
        if status != "READY":
            diagnostics["unresolved"].append(name)
            continue
        fields = {"animalDefName": name, "expectedBaseHungerRate": number(row, "Adult base feed (N/d)") / 1.6}
        mode = str(row["Reproduction mode"]).strip()
        if mode == "Gestation":
            fields.update(gestationDays=number(row, "Gestation (d)"),
                          gestationBudget=number(row, "Gestation budget (N)"),
                          pregnancyStage1=number(row, "Pregnancy stage 1 (N/d)"),
                          pregnancyStage2=number(row, "Pregnancy stage 2 (N/d)"),
                          pregnancyStage3=number(row, "Pregnancy stage 3 (N/d)"))
            verify_budget((fields["pregnancyStage1"] + fields["pregnancyStage2"] +
                           fields["pregnancyStage3"]) * fields["gestationDays"] / 3,
                          fields["gestationBudget"], name)
        elif mode in ("Egg", "Aquatic cohort"):
            fields.update(eggIntervalDays=number(row, "Egg interval (d)"),
                          clutchBudget=number(row, "Clutch budget (N)"),
                          gravidExtra=number(row, "Gravid extra (N/d)"))
            verify_budget(fields["gravidExtra"] * fields["eggIntervalDays"] / 2,
                          fields["clutchBudget"], name)
            if mode == "Egg":
                fields["eggNutrition"] = number(row, "Egg nutrition / ovum (N)")
            else:
                fields["aquatic"] = "true"
        else:
            raise ValueError(f"{name}: unknown READY reproduction mode {mode!r}")
        node = E.SubElement(records, "li")
        for key, value in fields.items():
            E.SubElement(node, key).text = format(value, ".12g") if isinstance(value, float) else str(value)
        diagnostics["ready"] += 1
    if not diagnostics["ready"]:
        raise ValueError("EnergeticsProfiles contains no READY reproductive profiles")
    return E.tostring(root, encoding="utf-8", xml_declaration=True, pretty_print=True), diagnostics
