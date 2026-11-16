"""Serialize evaluated species reproductive profiles from the Animals sheet."""
from __future__ import annotations

from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
import re

SEASON_COLUMN = "Mating season"
DIAPAUSE_COLUMN = "Embryonic diapause (game days)"
PAUSE_COLUMN = "Reproductive pause (game days)"
COLUMNS = (SEASON_COLUMN, DIAPAUSE_COLUMN, PAUSE_COLUMN)
SEASONS = {"spring": 1, "summer": 2, "autumn": 4, "winter": 8}
TICKS_PER_DAY = 60000
TICKS_PER_SEASON = 15 * TICKS_PER_DAY
ALL_SEASONS = 15
NATIVE_REPRODUCTION = "native reproduction"
BIRTH_COLUMN = "Birth / laying season"
THERMAL_BREEDING_COLUMN = "Thermal breeding minimum (°C)"
DORMANCY_COLUMN = "Dormancy threshold (°C)"
CAPTIVE_DURATIONS = {
    "Captive gestation (game days)": "captiveGestationDays",
    "Captive diapause (game days)": "captiveDiapauseDays",
    "Captive real diapause (game days)": "captiveRealDiapauseDays",
    "Captive egg formation (game days)": "captiveEggFormationDays",
    "Captive real egg formation (game days)": "captiveRealEggFormationDays",
    "Captive reproductive pause (game days)": "captiveReproductivePauseDays",
    "Captive incubation (game days)": "captiveIncubationDays",
}


def _text(value):
    value = "" if value is None else str(value).strip()
    return "" if value.casefold() in ("", "nan", "none", "<na>") else value


def native_reproduction(value):
    return _text(value).casefold() == NATIVE_REPRODUCTION


def season_mask(value):
    value = _text(value).casefold()
    if not value or value == "all":
        return ALL_SEASONS
    tokens = [part.strip() for part in value.split(",")]
    if len(tokens) != len(set(tokens)) or any(token not in SEASONS for token in tokens):
        raise ValueError(f"Invalid mating season: {value!r}")
    return sum(SEASONS[token] for token in tokens)


def duration_ticks(value):
    value = _text(value)
    if not value:
        return 0
    try:
        days = Decimal(value.replace(",", "."))
    except InvalidOperation as exc:
        raise ValueError(f"Invalid reproductive duration: {value!r}") from exc
    if not days.is_finite() or days < 0:
        raise ValueError(f"Invalid reproductive duration: {value!r}")
    ticks = int((days * TICKS_PER_DAY).to_integral_value(rounding=ROUND_HALF_UP))
    if ticks > 2147483647:
        raise ValueError(f"Reproductive duration exceeds tick range: {value!r}")
    return ticks


def duration_days(value):
    value = _text(value)
    if not value:
        return None
    try:
        result = Decimal(value.replace(",", "."))
    except InvalidOperation as exc:
        raise ValueError(f"Invalid reproductive duration: {value!r}") from exc
    if not result.is_finite() or result < 0 or result > Decimal(2147483647) / TICKS_PER_DAY:
        raise ValueError(f"Invalid reproductive duration: {value!r}")
    return format(result, "f")


def temperature(value):
    value = _text(value)
    if not value:
        return None
    try:
        result = Decimal(value.replace(",", "."))
    except InvalidOperation as exc:
        raise ValueError(f"Invalid climate temperature: {value!r}") from exc
    if not result.is_finite():
        raise ValueError(f"Invalid climate temperature: {value!r}")
    return format(result, "f")


def gestation_calendar_possible(mating_mask, birth_mask, pregnancy_ticks):
    """A shifted mating quadrum can intersect at most two birth quadrums."""
    for index in range(4):
        if not mating_mask & (1 << index):
            continue
        start = index * TICKS_PER_SEASON + pregnancy_ticks
        end = start + TICKS_PER_SEASON - 1
        if birth_mask & (1 << ((start // TICKS_PER_SEASON) % 4)):
            return True
        if birth_mask & (1 << ((end // TICKS_PER_SEASON) % 4)):
            return True
    return False


def seasonal_fields(row):
    present = [column in row for column in COLUMNS]
    if not any(present):
        return {}
    if not all(present):
        raise ValueError("Animals: incomplete seasonal reproduction columns")
    try:
        mask = season_mask(row[SEASON_COLUMN])
        diapause = duration_ticks(row[DIAPAUSE_COLUMN])
        pause = duration_ticks(row[PAUSE_COLUMN])
        birth = season_mask(row.get(BIRTH_COLUMN))
        thermal = temperature(row.get(THERMAL_BREEDING_COLUMN))
        dormancy = temperature(row.get(DORMANCY_COLUMN))
        gestation = _text(row.get("Gestation period (days)"))
        if (thermal is None and birth != ALL_SEASONS and mask != ALL_SEASONS
                and gestation.casefold() not in ("", "lay eggs", "no", NATIVE_REPRODUCTION)):
            active = duration_ticks(gestation)
            if active > 0 and not gestation_calendar_possible(mask, birth, active + diapause):
                raise ValueError("No mating date can reach the configured birth season")
        captive = {field: duration_days(row.get(column)) for column, field in CAPTIVE_DURATIONS.items()}
        for field in ("captiveGestationDays", "captiveEggFormationDays",
                      "captiveRealEggFormationDays", "captiveIncubationDays"):
            if captive[field] is not None and Decimal(captive[field]) <= 0:
                raise ValueError(f"{field} must be positive")
        if captive["captiveRealEggFormationDays"] is not None and captive["captiveEggFormationDays"] is None:
            raise ValueError("Captive egg formation duration is required for both laying-rate modes")
        litter = duration_days(row.get("Realistic litter size"))
        if litter is not None and Decimal(litter) < 1:
            raise ValueError("Realistic litter size must be at least one")
        eggs = _text(row.get("Realistic egg count range"))
        if eggs:
            # Sheets exports equal endpoints as a numeric cell.
            if re.fullmatch(r"[0-9]+(?:[.,]0+)?", eggs):
                eggs = str(int(Decimal(eggs.replace(",", "."))))
            bounds = eggs.split("~")
            if (len(bounds) > 2 or any(not re.fullmatch(r"[0-9]+", part) for part in bounds)
                    or int(bounds[0]) < 1 or int(bounds[-1]) < int(bounds[0])
                    or int(bounds[-1]) > 2147483647):
                raise ValueError(f"Invalid realistic egg count range: {eggs!r}")
            if len(bounds) == 1:
                eggs = f"{eggs}~{eggs}"
    except ValueError as exc:
        raise ValueError(f"{row.get('XML name', row.get('Common name', 'Animal'))}: {exc}") from exc
    fields = {}
    if mask != ALL_SEASONS:
        fields["matingSeasonMask"] = mask
    if diapause:
        fields["embryonicDiapauseTicks"] = diapause
    if pause:
        fields["reproductivePauseTicks"] = pause
    if _text(row.get(BIRTH_COLUMN)):
        fields["birthSeasonMask"] = birth
    if thermal is not None:
        fields["thermalBreedingMinimum"] = thermal
    if dormancy is not None:
        fields["dormancyThreshold"] = dormancy
    fields.update({key: value for key, value in captive.items() if value is not None})
    if litter is not None:
        fields["realisticLitterMean"] = litter
    if eggs:
        fields["realisticEggCountRange"] = eggs
    return fields


def dormancy_fields(row):
    """Serialize winter shelter eligibility and the maternal denning calendar."""
    value = row.get("WinterDormancy")
    raw = "" if value is None else str(value).strip().casefold()
    if raw in ("nan", "<na>"):
        raw = ""
    if raw not in ("", "false", "true"):
        raise ValueError(f"Invalid WinterDormancy: {raw!r}")
    ordinary = raw == "true"
    maternity = _text(row.get("Maternity denning season"))
    if not ordinary and not maternity:
        return {}
    fields = {"ordinaryDormancy": str(ordinary).lower()}
    if maternity:
        fields["maternitySeasonMask"] = season_mask(maternity)
    return fields
