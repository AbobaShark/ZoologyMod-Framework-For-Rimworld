"""Validate and serialize evaluated AnimalStats reproductive energetics."""
from __future__ import annotations
import math
import re
from lxml import etree as E

DESTINATION = "Core/Defs/Misc/EnergeticsProfiles_Generated.xml"
OUTPUT_ID = "products.energetics"
EGG_BASE_FEED_SHARE = 0.5
EGG_FORMATION_EFFICIENCY = 1.0
REQUIRED = {
    "XML name", "Adult base feed (N/d)", "Reproduction mode", "Reproductive coverage",
    "Gestation (d)", "Gestation budget (N)", "Pregnancy stage 1 (N/d)",
    "Pregnancy stage 2 (N/d)", "Pregnancy stage 3 (N/d)", "Egg interval (d)",
    "Clutch budget (N)", "Gravid extra (N/d)", "Egg nutrition / ovum (N)",
    "Mean eggs / clutch", "Female adult HungerRateFactor",
}

def number(row, column, *, allow_zero=False):
    try:
        value = float(str(row[column]).replace(",", "."))
    except (TypeError, ValueError) as exc:
        raise ValueError(f"{row['XML name']}: invalid {column}") from exc
    if not math.isfinite(value) or value < 0 or (value == 0 and not allow_zero):
        raise ValueError(f"{row['XML name']}: invalid sign or nonfinite {column}")
    return value

def verify_budget(actual, expected, identity):
    if not math.isclose(actual, expected, rel_tol=1e-7, abs_tol=1e-9):
        raise ValueError(f"{identity}: inconsistent reproductive energy budget")

def extra_clutch_budget(egg_count, egg_nutrition, interval, female_feed):
    return max(0.0, egg_count * egg_nutrition / EGG_FORMATION_EFFICIENCY
               - EGG_BASE_FEED_SHARE * female_feed * interval)


def body_nutrition(size):
    return 7 * size


def round_base_hunger_rate(value):
    step = 0.05 if value >= 0.5 else 0.01
    return max(0.01, math.floor(value / step + 0.5) * step)

def hunger_shape(size, exponent):
    divisor = 1.5 if abs(size - 3.5) < 1e-5 else 2 if size < 3.5 else 1.25
    return (body_nutrition(size) / 7) ** exponent / divisor

def growth_stage_factors(row):
    exponent = number(row, "Allometric exponent")
    efficiency = number(row, "Growth efficiency")
    adult_feed = number(row, "Adult base feed (N/d)")
    juvenile_age = number(row, "Juvenile age (d)")
    adult_age = number(row, "Adult age (d)")
    if not juvenile_age.is_integer() or not adult_age.is_integer() or adult_age <= juvenile_age:
        raise ValueError(f"{row['XML name']}: invalid growth ages")
    juvenile_age, adult_age = int(juvenile_age), int(adult_age)
    baby = number(row, "Baby BodySize")
    juvenile = number(row, "Juvenile BodySize")
    baseline = number(row, "Baseline adult BodySize")
    adults = [number(row, label) for label in ("Male adult BodySize", "Female adult BodySize")]
    names = {0.889: 0.196, 0.681: 10.5, 0.734: 4.82, 0.856: 0.3362688, 0.916: 0.17712, 0.85: 0.1606392}
    if exponent not in names or efficiency > 1:
        raise ValueError(f"{row['XML name']}: invalid growth parameters")
    shape_base = hunger_shape(baseline, exponent)
    nominal_base = names[exponent] * 100000 ** exponent / (4.82 * 100000 ** 0.734) * shape_base
    expected_feed = 1.6 * round_base_hunger_rate(nominal_base)
    verify_budget(adult_feed, expected_feed, row["XML name"] + " adult hunger")

    def size_at(age, adult):
        if age < juvenile_age:
            return baby + (juvenile - baby) * min(1, age / max(1, juvenile_age - 1))
        return juvenile + (adult - juvenile) * min(1, (age - juvenile_age) / max(1, adult_age - juvenile_age - 1))

    def daily(age, adult):
        size = size_at(age, adult)
        nominal = adult_feed * hunger_shape(size, exponent) / shape_base
        gain = max(0, body_nutrition(size_at(age + 1, adult)) - body_nutrition(size))
        return max(nominal, gain / efficiency)

    def average_factor(start, stop):
        return math.fsum(daily(age, adult) for age in range(start, stop) for adult in adults) / (
            2 * (stop - start) * adult_feed)

    return (average_factor(0, juvenile_age), average_factor(juvenile_age, adult_age),
            *(hunger_shape(adult, exponent) / shape_base for adult in adults))

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
    growth_checked = 0
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
        growth_ready = str(row.get("Growth coverage", "")).strip() == "READY"
        if status != "READY":
            diagnostics["unresolved"].append(name)
            if not growth_ready:
                continue
        fields = {"animalDefName": name, "expectedBaseHungerRate": number(row, "Adult base feed (N/d)") / 1.6}
        if growth_ready:
            residual = float(str(row["Growth budget check (N)"]).replace(",", "."))
            if not math.isfinite(residual) or abs(residual) > 1e-7:
                raise ValueError(f"{name}: inconsistent growth budget")
            for column, expected in zip(
                ("Baby HungerRateFactor", "Juvenile HungerRateFactor",
                 "Male adult HungerRateFactor", "Female adult HungerRateFactor"),
                growth_stage_factors(row),
            ):
                verify_budget(number(row, column), expected, name + " " + column)
            growth_checked += 1
            fields.update(growthExponent=number(row, "Allometric exponent"),
                          growthEfficiency=number(row, "Growth efficiency"))
            if fields["growthEfficiency"] > 1:
                raise ValueError(f"{name}: growth efficiency exceeds one")
        mode = str(row["Reproduction mode"]).strip() if status == "READY" else None
        if mode == "Gestation":
            fields.update(gestationDays=number(row, "Gestation (d)"),
                          gestationBudget=number(row, "Gestation budget (N)"),
                          pregnancyStage1=number(row, "Pregnancy stage 1 (N/d)"),
                          pregnancyStage2=number(row, "Pregnancy stage 2 (N/d)"),
                          pregnancyStage3=number(row, "Pregnancy stage 3 (N/d)"))
            stage_unit = fields["gestationBudget"] / (3 * fields["gestationDays"])
            for stage, weight in enumerate((1, 3, 5), start=1):
                verify_budget(fields[f"pregnancyStage{stage}"], stage_unit * weight, name)
        elif mode in ("Egg", "Aquatic cohort"):
            fields.update(eggIntervalDays=number(row, "Egg interval (d)"),
                          clutchBudget=number(row, "Clutch budget (N)", allow_zero=True),
                          gravidExtra=number(row, "Gravid extra (N/d)", allow_zero=True))
            egg_nutrition = number(row, "Egg nutrition / ovum (N)")
            female_feed = number(row, "Adult base feed (N/d)") * number(row, "Female adult HungerRateFactor")
            expected = extra_clutch_budget(number(row, "Mean eggs / clutch"), egg_nutrition,
                                           fields["eggIntervalDays"], female_feed)
            verify_budget(fields["clutchBudget"], expected, name)
            verify_budget(fields["gravidExtra"] * fields["eggIntervalDays"] / 2,
                          fields["clutchBudget"], name)
            if mode == "Egg":
                fields["eggNutrition"] = egg_nutrition
            else:
                fields["aquatic"] = "true"
        elif mode is not None:
            raise ValueError(f"{name}: unknown READY reproduction mode {mode!r}")
        node = E.SubElement(records, "li")
        for key, value in fields.items():
            E.SubElement(node, key).text = format(value, ".12g") if isinstance(value, float) else str(value)
        if status == "READY":
            diagnostics["ready"] += 1
    diagnostics["growth_checked"] = growth_checked
    if not diagnostics["ready"]:
        raise ValueError("EnergeticsProfiles contains no READY reproductive profiles")
    return E.tostring(root, encoding="utf-8", xml_declaration=True, pretty_print=True), diagnostics
