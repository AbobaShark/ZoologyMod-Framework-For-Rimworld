"""Convert real mass into game BodySize and invert its quantized bands.

The game BodySize is a dimensionless gameplay scale, not a mass multiplier.
The smooth forward mapping is anchored at 60 kg -> 1 and 4500 kg -> 5.
Quantization gives one contiguous mass interval per attainable game size.
For unattainable intermediate sizes used by life stages, interpolate the
midpoints of the neighboring attainable bands.
"""
from __future__ import annotations

from bisect import bisect_left
from functools import lru_cache
import math

MASS_REFERENCE_KG = 60.0
MASS_EXPONENT = 0.47
# Normalization B(4500 kg) = 5 with B(60 kg) = 1.
MASS_TRANSITION_EXPONENT = (
    math.log(5) - MASS_EXPONENT * math.log(75)
) / math.log(38)

# Raw BodySize bounds and game quantization step. These are size-based
# rounding intervals, not arbitrary physical-mass bands.
_SIZE_ROUNDING = (
    (0.0, 1.0, 0.05),
    (1.0, 2.0, 0.1),
    (2.0, 5.0, 0.25),
    (5.0, math.inf, 0.5),
)


def _raw_body_size(mass_kg):
    x = mass_kg / MASS_REFERENCE_KG
    return x ** MASS_EXPONENT * ((1 + x) / 2) ** MASS_TRANSITION_EXPONENT


def body_size_for_mass_kg(mass_kg):
    """The same positive-mass -> rounded BodySize model as Animals!AO."""
    mass = float(mass_kg)
    if not math.isfinite(mass) or mass <= 0:
        raise ValueError("Physical mass must be finite and positive")
    raw = _raw_body_size(mass)
    step = next(step for lower, upper, step in _SIZE_ROUNDING if raw <= upper)
    # Google Sheets MROUND rounds positive half-way cases away from zero.
    return max(0.05, math.floor(raw / step + 0.5) * step)


def _mass_at_raw_body_size(size):
    """Invert strictly increasing raw size by bisection, without mass knots."""
    if size <= 0:
        return 0.0
    lower, upper = 0.0, MASS_REFERENCE_KG
    while _raw_body_size(upper) < size:
        upper *= 2
    for _ in range(90):
        midpoint = (lower + upper) / 2
        if _raw_body_size(midpoint) < size:
            lower = midpoint
        else:
            upper = midpoint
    return (lower + upper) / 2


def mass_ranges_for_body_size(game_size):
    """All mass intervals mapping to a particular rounded game BodySize."""
    target = float(game_size)
    if not math.isfinite(target) or target <= 0:
        raise ValueError("Game BodySize must be finite and positive")
    intervals = []
    for raw_lower, raw_upper, step in _SIZE_ROUNDING:
        if abs(target / step - round(target / step)) > 1e-7:
            continue
        left = max(raw_lower, target - step / 2)
        right = min(raw_upper, target + step / 2)
        if math.isclose(target, 0.05, abs_tol=1e-9):
            # MAX(0.05, ...) also collects raw values below 0.025.
            left = 0.0
        if right <= left:
            continue
        mass_lower = _mass_at_raw_body_size(left)
        mass_upper = _mass_at_raw_body_size(right)
        # At the step changes (1, 2, 5) adjacent raw intervals can map to
        # the same rounded BodySize. Their union is still one mass interval.
        if intervals and mass_lower <= intervals[-1][1] + 1e-8 * max(1, mass_lower):
            intervals[-1] = (intervals[-1][0], mass_upper)
        else:
            intervals.append((mass_lower, mass_upper))
    return tuple(intervals)


@lru_cache(maxsize=1)
def _midpoint_bins():
    bins = []
    # Covers all currently supported species and Royal stage game BodySizes.
    for i in range(1, 401):
        size = round(i * 0.05, 8)
        intervals = mass_ranges_for_body_size(size)
        if not intervals:
            continue
        width = sum(upper - lower for lower, upper in intervals)
        midpoint = sum((lower + upper) / 2 * (upper - lower)
                       for lower, upper in intervals) / width
        bins.append((size, midpoint))
    return tuple(bins)


def body_size_equivalent_mass_kg(game_body_size):
    """Representative mass-bin midpoint, interpolated for unavailable sizes."""
    size = float(game_body_size)
    if not math.isfinite(size) or size <= 0:
        raise ValueError("Game BodySize must be finite and positive")
    pairs = _midpoint_bins()
    positions = [x for x, _ in pairs]
    i = bisect_left(positions, size)
    if i == len(pairs) or (i == 0 and size < positions[0]):
        raise ValueError(f"BodySize {size} outside calibrated source bins")
    if abs(positions[i] - size) <= 1e-8:
        return pairs[i][1]
    if i == 0:
        return pairs[0][1]
    left, right = pairs[i - 1], pairs[i]
    portion = (size - left[0]) / (right[0] - left[0])
    return left[1] + portion * (right[1] - left[1])


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
