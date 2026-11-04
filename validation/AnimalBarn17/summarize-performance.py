"""Summarize completed isolated-game logs; no third-party dependencies."""
import json
from pathlib import Path
import re
import statistics as stats

root = Path(__file__).resolve().parent
result = {}
for version in ("before", "after"):
    text = (root / f"performance-{version}.txt").read_text(encoding="utf-8-sig")
    if "COMPLETE performance" not in text or re.search(r"^FAIL", text, re.M):
        raise SystemExit(f"Invalid {version} report")
    result[version] = {}
    for mode in ("loose", "barn"):
        tick = re.findall(rf"^TICK {mode}-\d .*?msPerTick=([\d.]+) bytesPerTick=([\d.]+)", text, re.M)
        draw = re.findall(rf"^DRAW {mode}-\d .*?medianMs=([\d.]+).*?medianBytes=([\d.]+)", text, re.M)
        assert len(tick) == 24 and len(draw) == 2, (version, mode, len(tick), len(draw))
        result[version][mode] = {
            "median_ms_per_tick_24_windows": stats.median(float(t[0]) for t in tick),
            "mean_ms_per_tick_24_windows": stats.mean(float(t[0]) for t in tick),
            "median_main_thread_bytes_per_tick": stats.median(float(t[1]) for t in tick),
            "dynamic_draw_median_ms_range": [min(float(d[0]) for d in draw), max(float(d[0]) for d in draw)],
            "dynamic_draw_median_bytes_range": [min(float(d[1]) for d in draw), max(float(d[1]) for d in draw)],
        }
    free, barn = (result[version][m]["median_ms_per_tick_24_windows"] for m in ("loose", "barn"))
    result[version]["packing_speedup"] = free / barn
    result[version]["packing_tick_cost_reduction_percent"] = 100 * (1 - barn / free)
old = result["before"]["barn"]["median_ms_per_tick_24_windows"]
new = result["after"]["barn"]["median_ms_per_tick_24_windows"]
result["additional_barn_cost_reduction_percent_unadjusted"] = 100 * (1 - new / old)
before_layout = re.findall(r"^LAYOUT barn-\d (\w+)", (root / "performance-before.txt").read_text(encoding="utf-8-sig"), re.M)
after_layout = re.findall(r"^LAYOUT barn-\d (\w+)", (root / "performance-after.txt").read_text(encoding="utf-8-sig"), re.M)
result["all_four_barn_layout_hashes_equal"] = len(set(before_layout + after_layout)) == 1
(root / "performance-summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))
