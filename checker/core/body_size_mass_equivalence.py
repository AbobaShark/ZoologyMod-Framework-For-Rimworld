"""Invert the *game* BodySize assignment bins to equivalent mass ranges.

This is the inverse of the piecewise MROUND model in AnimalStats!Animals!AO,
not a physical rule that mass = 70 kg * BodySize (which is false).
For each attainable discrete BodySize, take the midpoint of its source mass
range; if disjoint intervals yield one size, take their width-weighted
midpoint. For a LifeStage body size between attainable bins, linearly
interpolate adjacent midpoint masses.

The 70 kg constants below are *knots of the source map*, not a reference
animal mass used in any life-stage factor. Keep source-map changes in the
AnimalStats workbook and this explicit inverse in sync.
"""
from __future__ import annotations
from bisect import bisect_left
from functools import lru_cache
import math


# (mass lower, mass upper, quantization step, raw-mapping scale,
#  raw-mapping pivot mass, raw-mapping exponent).
# A zero exponent denotes a constant raw BodySize equal to the scale.
_SEGMENTS = (
    (0.0, 0.1, 0.05, 0.05, 1, 0),
    (0.1, 0.4, 0.05, 0.1, 1, 0),
    (0.4, 0.5, 0.05, 1.0, 70, 0.445823913056526),
    (0.5, 0.8, 0.05, 1.0, 70, 0.426489913056526),
    (0.8, 70, 0.05, 1.0, 70, 0.445823913056526),
    (70, 100, 0.05, 1.0, 70, 0.572811455315093),
    (100, 250, 0.1, 1.0, 70, 0.572811455315093),
    (250, 400, 0.25, 1.0, 70, 0.572811455315093),
    (400, 2000, 0.25, 2.71391647193063, 400, 0.252459582904293),
    (2000, 4000, 0.25, 3.74314280240376, 2000, 0.357012633876913),
    (4000, 100000000, 0.5, 3.74314280240376, 2000, 0.357012633876913),
)


def mass_ranges_for_body_size(game_size):
    """Exact source mass intervals mapping to a particular rounded BodySize."""
    target = float(game_size)
    if not math.isfinite(target) or target <= 0:
        raise ValueError("Game BodySize must be finite and positive")
    out = []
    for low, high, step, scale, pivot, exponent in _SEGMENTS:
        if abs(target / step - round(target / step)) > 1e-7:
            continue
        minimum, maximum = target - step / 2, target + step / 2
        if exponent == 0:
            if minimum <= scale < maximum:
                out.append((low, high))
            continue
        f = lambda mass: scale * (mass / pivot) ** exponent
        raw_low, raw_high = f(low), f(high)
        if raw_high < minimum or raw_low >= maximum:
            continue
        def inverse(value):
            return pivot * (max(value, 1e-12) / scale) ** (1 / exponent)
        lower = max(low, min(high, inverse(minimum)))
        upper = max(low, min(high, inverse(maximum)))
        if upper > lower + 1e-8:
            out.append((lower, upper))
    return tuple(out)


@lru_cache(maxsize=1)
def _midpoint_bins():
    bins = []
    # This range covers the currently supported Zoo species and Royal stages.
    # Out-of-range cases fail loudly rather than extrapolating a fake mass.
    for i in range(1, 401):
        size = i * 0.05
        intervals = mass_ranges_for_body_size(size)
        if not intervals:
            continue
        width = sum(right - left for left, right in intervals)
        weighted_midpoint = sum((left + right) / 2 * (right - left)
                                for left, right in intervals) / width
        bins.append((round(size, 8), weighted_midpoint))
    return tuple(bins)


def body_size_equivalent_mass_kg(game_body_size):
    """Representative mass: mass-bin midpoint, interpolated if inaccessible."""
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
    left, right = pairs[i-1], pairs[i]
    portion = (size - left[0]) / (right[0] - left[0])
    return left[1] + portion * (right[1] - left[1])


def body_size_midpoint_pairs():
    return _midpoint_bins()
