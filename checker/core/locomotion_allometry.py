"""Maximum running speed allometry from Hirt et al. (2017).

M. R. Hirt, W. Jetz, B. C. Rall & U. Brose, Nature Ecology & Evolution 1,
1116–1122 (2017). doi:10.1038/s41559-017-0241-4.
Terrestrial running, Supplementary Table 4:
    vmax(km/h) = 25.5 * mass_kg**0.26 * (1 - exp(-22 * mass_kg**-0.6))

This is the *cross-species maximum running speed* model. Only dimensionless
speed ratios are used as a proxy for RimWorld MoveSpeed; absolute speeds are
never inserted as game statistics. Do not interpret this as an experimentally
validated ontogenetic speed curve for a fictional animal.
"""
import math

RUNNING_A = 25.5
RUNNING_B = 0.26
RUNNING_H = 22.0
RUNNING_I = -0.6


def maximum_running_speed_kmh(mass_kg):
    mass_kg = float(mass_kg)
    if not math.isfinite(mass_kg) or mass_kg <= 0:
        raise ValueError(f"Running body mass must be positive and finite: {mass_kg!r}")
    # expm1 has better relative precision for high masses.
    return RUNNING_A * mass_kg ** RUNNING_B * (-math.expm1(-RUNNING_H * mass_kg ** RUNNING_I))


def age_speed_factor(body_size_factor, reference_mass_kg, decimals=3):
    body_size_factor = float(body_size_factor)
    if not math.isfinite(body_size_factor) or body_size_factor <= 0:
        raise ValueError("body_size_factor must be positive and finite")
    denominator = maximum_running_speed_kmh(reference_mass_kg)
    numerator = maximum_running_speed_kmh(reference_mass_kg * body_size_factor)
    return round(numerator / denominator, decimals)
