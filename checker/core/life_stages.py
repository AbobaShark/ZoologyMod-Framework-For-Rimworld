"""Source life stage identities. Numerical factors come solely from LifeStageProfiles.

Foreign AnimalBabyTiny is an alias of AnimalBaby when installing an individual
stage, never a fourth age or an independent numerical model.
"""
STAGE_GROUPS = (
    ("AnimalAdult", "EusocialInsectAdult"),
    ("AnimalJuvenile", "EusocialInsectJuvenile"),
    ("AnimalBaby", "EusocialInsectLarva"),
)
ROYAL_STAGES = tuple((name,) for name in (
    "VAERoy_AnimalBaby",
    "VAERoy_AnimalJuvenile",
    "VAERoy_AnimalAdult",
    "VAERoy_AnimalAdultIntermediate",
    "VAERoy_AnimalAdultMax",
))
SPECTROBIONTA_STAGES = tuple((name,) for name in (
    "SpectrobiontaImago", "SpectrobiontaPredimago",
    "SpectrobiontaPredimagoMedium", "SpectrobiontaPredimagoSmall",
    "SpectrobiontaLarvaLarge",
    "SpectrobiontaLarvaMedium", "SpectrobiontaLarvaSmall",
))
ROYAL_SOURCE = "Vanilla Animals Expanded — Royal Animals"
SPECTROBIONTA_SOURCE = "Spectrobionta"
