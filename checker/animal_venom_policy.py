"""Tool-level export contract; species assignments come exclusively from AnimalStats."""
import re


def is_platypus_spur(capacities):
    return "ZVenom_PlatypusSpur" in re.split(r"[,\s]+", str(capacities))
