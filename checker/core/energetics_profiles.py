"""Validate and serialize evaluated AnimalStats reproductive energetics."""
from __future__ import annotations
import math
import re
from lxml import etree as E

DESTINATION = "Core/Defs/Misc/EnergeticsProfiles_Generated.xml"
OUTPUT_ID = "products.energetics"
BASE_FEED_SHARE = 0.5
EGG_FORMATION_EFFICIENCY = 1.0
GESTATION_RETENTION_EFFICIENCY = 1.0
REQUIRED = {
    "XML name", "Adult base feed (N/d)", "Reproduction mode", "Reproductive coverage",
    "Gestation (d)", "Mean litter", "Gestation budget (N)", "Pregnancy stage 1 (N/d)",
    "Pregnancy stage 2 (N/d)", "Pregnancy stage 3 (N/d)", "Egg interval (d)",
    "Clutch budget (N)", "Gravid extra (N/d)", "Egg nutrition / ovum (N)",
    "Mean eggs / clutch", "Female adult HungerRateFactor", "Newborn food (N)",
}

def number(row, column, *, allow_zero=False):
    try:
        value = float(str(row[column]).replace(",", "."))
    except (KeyError, TypeError, ValueError) as exc:
        raise ValueError(f"{row['XML name']}: invalid {column}") from exc
    if not math.isfinite(value) or value < 0 or (value == 0 and not allow_zero):
        raise ValueError(f"{row['XML name']}: invalid sign or nonfinite {column}")
    return value

def verify_budget(actual, expected, identity):
    if not math.isclose(actual, expected, rel_tol=1e-7, abs_tol=1e-9):
        raise ValueError(f"{identity}: inconsistent reproductive energy budget")

def extra_production_budget(gross_budget, ordinary_feed):
    return max(0.0, gross_budget - BASE_FEED_SHARE * ordinary_feed)


def extra_clutch_budget(egg_count, egg_nutrition, interval, female_feed):
    return extra_production_budget(egg_count * egg_nutrition / EGG_FORMATION_EFFICIENCY,
                                   female_feed * interval)


def body_nutrition(size):
    return 7 * size


def round_base_hunger_rate(value):
    step = 0.05 if value >= 0.5 else 0.01
    return max(0.01, math.floor(value / step + 0.5) * step)

def hunger_shape(size, exponent):
    divisor = 1.5 if abs(size - 3.5) < 1e-5 else 2 if size < 3.5 else 1.25
    return (body_nutrition(size) / 7) ** exponent / divisor

def growth_parameters(row):
    exponent = number(row, "Allometric exponent")
    efficiency = number(row, "Growth efficiency")
    adult_feed = number(row, "Adult base feed (N/d)")
    baseline = number(row, "Baseline adult BodySize")
    coefficients = {0.889: 0.196, 0.681: 10.5, 0.734: 4.82, 0.856: 0.3362688,
                    0.916: 0.17712, 0.85: 0.1606392}
    if exponent not in coefficients or efficiency > 1:
        raise ValueError(f"{row['XML name']}: invalid growth parameters")
    shape_base = hunger_shape(baseline, exponent)
    nominal_base = coefficients[exponent] * 100000 ** exponent / (4.82 * 100000 ** 0.734) * shape_base
    verify_budget(adult_feed, 1.6 * round_base_hunger_rate(nominal_base),
                  row["XML name"] + " adult hunger")
    return exponent, efficiency, adult_feed, baseline


def stage_growth_budget(start, end, days, adult_feed, baseline, exponent, efficiency):
    """Return ordinary feed, retained tissue requirement, and extra feed for a stage."""
    if days <= 0:
        raise ValueError("Growth stage duration must be positive")
    base_shape = hunger_shape(baseline, exponent)
    ordinary = math.fsum(
        adult_feed * hunger_shape(start + (end - start) * min(1, day / max(1, days - 1)), exponent)
        / base_shape for day in range(days))
    gain = max(0, body_nutrition(end) - body_nutrition(start)) / efficiency
    extra = extra_production_budget(gain, ordinary)
    return ordinary, gain, extra


def growth_stage_factors(row):
    exponent, efficiency, adult_feed, baseline = growth_parameters(row)
    juvenile_age = number(row, "Juvenile age (d)")
    adult_age = number(row, "Adult age (d)")
    if not juvenile_age.is_integer() or not adult_age.is_integer() or adult_age <= juvenile_age:
        raise ValueError(f"{row['XML name']}: invalid growth ages")
    juvenile_age, adult_age = int(juvenile_age), int(adult_age)
    baby = number(row, "Baby BodySize")
    juvenile = number(row, "Juvenile BodySize")
    adults = [number(row, label) for label in ("Male adult BodySize", "Female adult BodySize")]

    def factor(start, end, days):
        ordinary, gain, extra = stage_growth_budget(
            start, end, days, adult_feed, baseline, exponent, efficiency)
        verify_budget(max(gain, BASE_FEED_SHARE * ordinary),
                      BASE_FEED_SHARE * ordinary + extra, row["XML name"] + " growth")
        return (ordinary + extra) / (days * adult_feed)

    return (factor(baby, juvenile, juvenile_age),
            math.fsum(factor(juvenile, adult, adult_age - juvenile_age) for adult in adults) / 2,
            *(hunger_shape(adult, exponent) / hunger_shape(baseline, exponent) for adult in adults))


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
        fields = {"animalDefName": name, "expectedBaseHungerRate": number(row, "Adult base feed (N/d)") / 1.6}
        # Species with custom or absent standard life stages still use their
        # canonical allometric parameters with the actual loaded stage ages.
        has_growth_parameters = all(column in frame.columns and str(row.get(column, "")).strip()
            not in ("", "nan", "None") for column in
            ("Allometric exponent", "Growth efficiency", "Baseline adult BodySize"))
        if has_growth_parameters:
            exponent, efficiency, _, _ = growth_parameters(row)
            fields.update(growthExponent=exponent, growthEfficiency=efficiency)
        elif status != "READY":
            continue
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
        mode = str(row["Reproduction mode"]).strip() if status == "READY" else None
        if mode == "Gestation":
            fields.update(meanLitter=number(row, "Mean litter"),
                          gestationDays=number(row, "Gestation (d)"),
                          gestationBudget=number(row, "Gestation budget (N)"),
                          pregnancyStage1=number(row, "Pregnancy stage 1 (N/d)"),
                          pregnancyStage2=number(row, "Pregnancy stage 2 (N/d)"),
                          pregnancyStage3=number(row, "Pregnancy stage 3 (N/d)"))
            verify_budget(fields["gestationBudget"],
                          fields["meanLitter"] * number(row, "Newborn food (N)")
                          / GESTATION_RETENTION_EFFICIENCY, name + " gestation")
            stage_unit = fields["gestationBudget"] / (3 * fields["gestationDays"])
            for stage, weight in enumerate((1, 3, 5), start=1):
                verify_budget(fields[f"pregnancyStage{stage}"], stage_unit * weight, name)
        elif mode in ("Egg", "Aquatic cohort"):
            fields.update(eggIntervalDays=number(row, "Egg interval (d)"),
                          clutchBudget=number(row, "Clutch budget (N)", allow_zero=True),
                          gravidExtra=number(row, "Gravid extra (N/d)", allow_zero=True))
            egg_nutrition = number(row, "Egg nutrition / ovum (N)")
            female_feed = number(row, "Adult base feed (N/d)") * number(row, "Female adult HungerRateFactor")
            fields.update(meanClutch=number(row, "Mean eggs / clutch"),
                          formationNutritionPerOvum=egg_nutrition, femaleBaseFeed=female_feed)
            expected = extra_clutch_budget(fields["meanClutch"], egg_nutrition,
                                           fields["eggIntervalDays"], female_feed)
            verify_budget(fields["clutchBudget"], expected, name)
            verify_budget(fields["gravidExtra"] * fields["eggIntervalDays"] / 2,
                          fields["clutchBudget"], name)
            if "Captive egg interval (d)" in frame.columns and "Captive clutch budget (N)" in frame.columns:
                captive_interval = number(row, "Captive egg interval (d)")
                captive_budget = number(row, "Captive clutch budget (N)", allow_zero=True)
                verify_budget(captive_budget, extra_clutch_budget(number(row, "Mean eggs / clutch"),
                              egg_nutrition, captive_interval, female_feed), name + " captive clutch")
                fields["captiveClutchBudget"] = captive_budget
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
