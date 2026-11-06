"""Inverse of the current Animals!AO mass-to-game-BodySize model.

The conversion uses the same 60 kg normalization, continuous response and
piecewise MROUND increments as the AnimalStats workbook. For every attainable
game BodySize the representative mass is the midpoint of its preimage mass
interval(s); non-attainable sizes interpolate between adjacent midpoints.
No mass is guessed below the minimum 0.05 game BodySize.
"""
from __future__ import annotations
from bisect import bisect_left
from functools import lru_cache
import math

_BETA = (math.log(5) - 0.47 * math.log(75)) / math.log(38)
_RAW_BANDS = ((0.0, 1.0, 0.05), (1.0, 2.0, 0.1),
              (2.0, 5.0, 0.25), (5.0, math.inf, 0.5))


def _raw_size(mass_kg):
    if mass_kg <= 0:
        return 0.0
    x = mass_kg / 60.0
    return x ** 0.47 * ((1.0 + x) / 2.0) ** _BETA


def _inverse_raw(raw):
    if raw <= 0:
        return 0.0
    lo, hi = 0.0, 60.0
    while _raw_size(hi) < raw:
        hi *= 2
    for _ in range(72):
        mid = (lo + hi) / 2
        if _raw_size(mid) < raw:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


def mass_ranges_for_body_size(game_size):
    """Mass intervals whose Animals!AO formula rounds to this BodySize."""
    target = float(game_size)
    if not math.isfinite(target) or target < 0.05:
        raise ValueError('Game BodySize must be finite and at least 0.05')
    out = []
    for band_lo, band_hi, step in _RAW_BANDS:
        if abs(target / step - round(target / step)) > 1e-7:
            continue
        # 0 <= raw < 0.025 rounds to zero, then MAX(0.05, ...)
        # also maps to 0.05, so it belongs in this special preimage.
        lower = 0.0 if target == 0.05 else max(0.0, target-step/2)
        upper = target+step/2
        lower = max(lower, band_lo)
        upper = min(upper, band_hi)
        if upper > lower + 1e-12:
            out.append((_inverse_raw(lower), _inverse_raw(upper)))
    return tuple(out)


@lru_cache(maxsize=1)
def _midpoint_bins():
    bins = []
    for i in range(1, 401):
        size = round(i * 0.05, 8)
        intervals = mass_ranges_for_body_size(size)
        if not intervals:
            continue
        width = sum(hi - lo for lo, hi in intervals)
        midpoint = sum((lo + hi) / 2 * (hi - lo)
                       for lo, hi in intervals) / width
        bins.append((size, midpoint))
    return tuple(bins)


def body_size_equivalent_mass_kg(game_body_size):
    """Mean mass-bin midpoint; interpolate if the game size is unattainable."""
    size = float(game_body_size)
    if not math.isfinite(size) or size < 0.05:
        raise ValueError(f'BodySize {size} outside calibrated source bins')
    pairs = _midpoint_bins()
    positions = [x for x, _ in pairs]
    i = bisect_left(positions, size)
    if i == len(pairs):
        raise ValueError(f'BodySize {size} outside calibrated source bins')
    if abs(positions[i] - size) <= 1e-8:
        return pairs[i][1]
    if i == 0:
        raise ValueError(f'BodySize {size} outside calibrated source bins')
    left, right = pairs[i - 1], pairs[i]
    fraction = (size - left[0]) / (right[0] - left[0])
    return left[1] + fraction * (right[1] - left[1])


def body_size_midpoint_pairs():
    return _midpoint_bins()


def mass_lookup_from_service_bands(table):
    """Use the workbook's calculated mass bands as the authoritative inverse.

    This prevents a later AnimalStats!AO formula change from silently leaving
    Checker with obsolete representative masses. Standalone TSV inputs may
    still use the built-in inverse when no service table was supplied.
    """
    required = ("BodySize (game)", "MassLower_kg", "MassUpper_kg",
                "MassMidpoint_kg")
    missing = set(required) - set(table.columns)
    if missing:
        raise ValueError("SERVICE_BodySizeMassBands missing columns: "
                         + repr(sorted(missing)))

    def number(raw, what):
        try:
            value = float(str(raw).strip().replace(",", "."))
        except (TypeError, ValueError):
            raise ValueError(f"SERVICE_BodySizeMassBands {what}: {raw!r}") from None
        if not math.isfinite(value):
            raise ValueError(f"SERVICE_BodySizeMassBands non-finite {what}")
        return value

    bands = []
    for _, row in table.iterrows():
        raw_size = row.get("BodySize (game)")
        if raw_size is None or str(raw_size).strip() == "":
            continue
        size = number(raw_size, "BodySize")
        lower = number(row.get("MassLower_kg"), f"{size} lower mass")
        upper = number(row.get("MassUpper_kg"), f"{size} upper mass")
        midpoint = number(row.get("MassMidpoint_kg"), f"{size} midpoint mass")
        if (size < 0.05 or lower < 0 or upper <= lower or
                not lower <= midpoint <= upper):
            raise ValueError(f"SERVICE_BodySizeMassBands invalid interval for {size}")
        bands.append((size, midpoint))
    if len(bands) < 2:
        raise ValueError("SERVICE_BodySizeMassBands has too few valid rows")
    bands.sort()
    if not math.isclose(bands[0][0], 0.05, abs_tol=1e-8):
        raise ValueError("SERVICE_BodySizeMassBands lacks 0.05 minimum")
    for left, right in zip(bands, bands[1:]):
        if right[0] <= left[0] + 1e-8 or right[1] <= left[1]:
            raise ValueError("SERVICE_BodySizeMassBands must be strictly increasing")
    positions = tuple(size for size, _ in bands)

    def mass_for_size(game_body_size):
        size = float(game_body_size)
        if not math.isfinite(size) or size < positions[0]:
            raise ValueError(f"BodySize {size} outside source mass bands")
        i = bisect_left(positions, size)
        if i == len(positions):
            raise ValueError(f"BodySize {size} outside source mass bands")
        if abs(positions[i] - size) <= 1e-8:
            return bands[i][1]
        if i == 0:
            raise ValueError(f"BodySize {size} outside source mass bands")
        left, right = bands[i - 1], bands[i]
        fraction = (size - left[0]) / (right[0] - left[0])
        return left[1] + fraction * (right[1] - left[1])

    return mass_for_size
