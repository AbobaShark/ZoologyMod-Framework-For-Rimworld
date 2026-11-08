"""Maximum running speed allometry from Hirt et al. (2017).

M. R. Hirt, W. Jetz, B. C. Rall & U. Brose, Nature Ecology & Evolution 1,
1116–1122 (2017). doi:10.1038/s41559-017-0241-4.
Terrestrial running, Supplementary Table 4:
    vmax(km/h) = 25.5 * mass_kg**0.26 * (1 - exp(-22 * mass_kg**-0.6))

The Hirt curve is evaluated at the representative physical-mass midpoints
of the game's *nonlinear and quantized* BodySize-to-mass mapping. RimWorld's
BodySize is not kilograms or proportional to kilograms. Dimensionless ratios
are used as a proxy for RimWorld MoveSpeed; absolute km/h are not imported
as game stats. The fitted cross-species sprint model is not a validated
ontogenetic walking-speed law for fictional animals.
"""
import math
from core.body_size_mass_equivalence import body_size_equivalent_mass_kg

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


def age_speed_factor(body_size_factor, base_body_size, decimals=3):
    """Ratio of Hirt speeds at the *mapped* masses of game body sizes.

    NOT v(mass * body_size_factor)/v(mass). The LifeStageDef coefficient
    multiplies game BodySize, whose physical-mass mapping is nonlinear.
    """
    body_size_factor = float(body_size_factor)
    base_body_size = float(base_body_size)
    if not math.isfinite(body_size_factor) or body_size_factor <= 0:
        raise ValueError("body_size_factor must be positive and finite")
    if not math.isfinite(base_body_size) or base_body_size <= 0:
        raise ValueError("base_body_size must be positive and finite")
    m0 = body_size_equivalent_mass_kg(base_body_size)
    m1 = body_size_equivalent_mass_kg(base_body_size * body_size_factor)
    return round(maximum_running_speed_kmh(m1) / maximum_running_speed_kmh(m0),
                 decimals)
