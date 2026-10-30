"""Summarize the three game runs produced by run-comparison.ps1.

Usage: python tests/RimWorld17/summarize-comparison.py <evidence-directory>
"""

import csv
import re
import statistics
import sys
from pathlib import Path


SCENARIOS = (
    "normal", "mass-scavenging", "mass-pets", "mass-roamers",
    "mass-families", "predator-prey", "mass-combat", "pig-farm",
    "combined-stress",
)
MODES = ("no-mod", "baseline", "updated")
LINE = re.compile(r"^(no-mod|baseline|updated) ([\w-]+): sample=(\d+), (.*)$")


def samples(path: Path, mode: str):
    result = {scenario: [] for scenario in SCENARIOS}
    for line in path.read_text(encoding="utf-8").splitlines():
        match = LINE.match(line)
        if not match or match.group(1) != mode or match.group(2) not in result:
            continue
        fields = dict(part.split("=", 1) for part in match.group(4).split(", ") if "=" in part)
        result[match.group(2)].append(fields)
    missing = [scenario for scenario, rows in result.items() if len(rows) < 3]
    if missing:
        raise ValueError(f"{path}: expected at least three samples for {missing}")
    return result


def median(rows, field):
    return statistics.median(float(row[field]) for row in rows)


def main():
    evidence = Path(sys.argv[1])
    runs = {mode: samples(evidence / f"performance-{mode}-all.txt", mode) for mode in MODES}
    with (evidence / "comparison-summary.csv").open("w", newline="", encoding="utf-8") as output:
        writer = csv.writer(output)
        writer.writerow(("scenario", "vanilla_ms_tick", "original_ms_tick", "optimized_ms_tick",
                         "vanilla_tps", "original_tps", "optimized_tps", "optimized_tps_lag_pct",
                         "vanilla_animals_end", "original_animals_end", "optimized_animals_end"))
        for scenario in SCENARIOS:
            rows = [runs[mode][scenario] for mode in MODES]
            ms = [median(run, "ms/tick") for run in rows]
            animals = [median(run, "spawnedAnimalsEnd") for run in rows]
            writer.writerow((scenario, *[f"{value:.4f}" for value in ms],
                             *[f"{1000 / value:.1f}" for value in ms],
                             f"{100 * (1 - ms[0] / ms[2]):.1f}",
                             *[f"{value:g}" for value in animals]))


if __name__ == "__main__":
    main()
