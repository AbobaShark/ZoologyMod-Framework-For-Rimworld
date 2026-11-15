"""Compare two Checker checkouts against one evaluated input snapshot."""
import argparse
from contextlib import nullcontext
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import tempfile
import time


RUN = """
import json, sys
from pathlib import Path
run_config = json.loads(sys.argv[2])
if run_config["snapshot"]:
    from core import animalstats_source as inputs
    snapshot = json.loads(Path(run_config["snapshot"]).read_text(encoding="utf-8"))
    def read_snapshot(spreadsheet_id, sheet_name):
        if spreadsheet_id != snapshot["spreadsheet_id"]:
            raise RuntimeError("Unexpected spreadsheet in comparison")
        if sheet_name not in snapshot["sheets"]:
            raise RuntimeError(f"Sheet missing from comparison snapshot: {sheet_name}")
        return snapshot["sheets"][sheet_name]
    inputs._fetch_google_values = read_snapshot
from core.pipeline import CATEGORIES, stage_pipeline
options = json.loads(sys.argv[1])
options['categories'] = options['categories'] or CATEGORIES
if run_config["profile"]:
    import cProfile, pstats
    profiler = cProfile.Profile()
    profiler.runcall(stage_pipeline, **options)
    target = Path(run_config["profile"])
    profiler.dump_stats(str(target.with_suffix(".prof")))
    with target.open("w", encoding="utf-8") as stream:
        stats = pstats.Stats(profiler, stream=stream)
        for key in ("cumulative", "time", "calls"):
            print(f"\\n=== Sorted by {key} ===", file=stream)
            stats.sort_stats(key).print_stats(100)
        for name in ("optimize", "xpath", "deepcopy", "compact", "generate_patch", "read_table"):
            print(f"\\n=== Callers matching {name} ===", file=stream)
            stats.sort_stats("cumulative").print_callers(name)
else:
    stage_pipeline(**options)
"""


SNAPSHOT = """
import json, sys, urllib.parse, urllib.request
from pathlib import Path
from core.animalstats_source import google_spreadsheet_id, _load_google_credentials
from core.pipeline import CATEGORIES

source, destination, options = sys.argv[1:]
options = json.loads(options)
sid = google_spreadsheet_id(source)
if not sid:
    raise ValueError("Invalid Google Sheets source")
cats = set(options["categories"] or CATEGORIES)
tabs = set()
if cats & {"animals", "venom", "biomes"}:
    tabs.update(("Animals", "Venom profiles"))
    if options["ce"]:
        tabs.add("Animals CE")
if "products" in cats:
    tabs.update(("Animals", "EggMilkWool", "EnergeticsProfiles"))
if "bionics" in cats:
    tabs.add("Bionics")
if "life-stages" in cats:
    tabs.update(("Animals", "Animals CE", "LifeStageProfiles",
                 "SexDimorphism", "SexAdultProfiles", "Base stats"))
if "leathers" in cats:
    tabs.update(("Leathers", "Animals"))
if "textiles" in cats:
    tabs.add("Animal textiles")
tabs = sorted(tabs - set(options["overrides"]))
sheets = {}
if tabs:
    creds = _load_google_credentials()
    token = getattr(creds, "token", None)
    if not token:
        raise RuntimeError("Google authorization produced no token")
    ranges = ["'" + tab.replace("'", "''") + "'" for tab in tabs]
    query = urllib.parse.urlencode({
        "ranges": ranges, "majorDimension": "ROWS",
        "valueRenderOption": "UNFORMATTED_VALUE",
        "dateTimeRenderOption": "FORMATTED_STRING",
    }, doseq=True)
    request = urllib.request.Request(
        f"https://sheets.googleapis.com/v4/spreadsheets/{sid}/values:batchGet?{query}",
        headers={"Authorization": f"Bearer {token}", "Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=120) as response:
        result = json.load(response)["valueRanges"]
    if len(result) != len(tabs):
        raise RuntimeError("Incomplete Google Sheets snapshot")
    sheets = {tab: item.get("values", []) for tab, item in zip(tabs, result)}
Path(destination).write_text(json.dumps({"spreadsheet_id": sid, "sheets": sheets},
                                        ensure_ascii=False), encoding="utf-8")
print(f"Captured {len(sheets)} Google Sheets tabs", flush=True)
"""


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def files(stage):
    manifest = json.loads((stage / "generation-manifest.json").read_text(encoding="utf-8"))
    result = {}
    for item in manifest["files"]:
        path = stage / item["source"]
        actual = digest(path)
        if actual != item["sha256"]:
            raise ValueError(f"Manifest hash mismatch: {path}")
        result[item["destination"]] = {
            "sha256": actual,
            "size": path.stat().st_size,
            "category": item["category"],
            "pipeline_category": item.get("pipeline_category"),
            "requires": item.get("requires"),
        }
    return result, sorted(manifest.get("remove", []))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--baseline", type=Path)
    p.add_argument("--candidate", required=True, type=Path)
    p.add_argument("--source", required=True, help="Local workbook or Google Sheets URL/ID")
    p.add_argument("--original-xml", required=True, type=Path)
    p.add_argument("--ce-patches", type=Path)
    p.add_argument("--category", action="append")
    p.add_argument("--group", action="append")
    p.add_argument("--no-ce", action="store_true")
    p.add_argument("--no-optimize", action="store_true")
    p.add_argument("--report", type=Path)
    p.add_argument("--profile-only", type=Path, metavar="PROFILE_TXT",
                   help="Profile candidate generation only; write text and .prof reports")
    p.add_argument("--artifacts-dir", type=Path, help="Retain both staged output directories (must not exist)")
    p.add_argument("--sheet", action="append", default=[], metavar="TAB=LOCAL_FILE")
    args = p.parse_args()
    if args.profile_only is None and (args.baseline is None or args.report is None):
        p.error("Comparison requires --baseline and --report")
    raw_source = args.source.strip()
    google = bool(raw_source.lower().startswith(("https://", "gsheet:"))
                  or re.fullmatch(r"[A-Za-z0-9_-]{20,}", raw_source))
    source = raw_source if google else str(Path(raw_source).resolve(strict=True))
    refs = args.original_xml.resolve(strict=True)
    patches = (args.ce_patches or refs / "CEPatches").resolve()
    if (not google and not Path(source).is_file()) or not refs.is_dir():
        p.error("Use an AnimalStats workbook or Google Sheets URL and OriginalXML directory")
    if not args.no_ce and not patches.is_dir():
        p.error("CE comparison requires a reference patch directory")
    candidate = args.candidate.resolve(strict=True)
    profile = args.profile_only.resolve() if args.profile_only else None
    if profile is not None:
        if profile.suffix.lower() != ".txt":
            p.error("--profile-only requires a .txt report path")
        profile.parent.mkdir(parents=True, exist_ok=True)
        runs = [("candidate", candidate)]
    else:
        baseline = args.baseline.resolve(strict=True)
        if baseline == candidate:
            p.error("Use separate baseline and candidate checkouts")
        runs = [("baseline", baseline), ("candidate", candidate)]
        report = args.report.resolve()
        report.parent.mkdir(parents=True, exist_ok=True)
    overrides = {}
    for item in args.sheet:
        tab, sep, path = item.partition("=")
        if not sep or not tab or not path:
            p.error("--sheet expects TAB=LOCAL_FILE")
        local = Path(path).resolve(strict=True)
        if not local.is_file():
            p.error(f"Sheet override is not a file: {local}")
        overrides[tab] = str(local)
    env = dict(os.environ, PYTHONHASHSEED="0")
    results = []
    if args.artifacts_dir is None:
        workspace = tempfile.TemporaryDirectory(prefix="zoology-compare-")
    else:
        directory = args.artifacts_dir.resolve()
        directory.mkdir(parents=True, exist_ok=False)
        workspace = nullcontext(directory)
    with workspace as td, tempfile.TemporaryDirectory(prefix="zoology-input-") as inputs:
        snapshot_path = None
        snapshot_seconds = None
        if google:
            snapshot_file = Path(inputs) / "google-values.json"
            snapshot_options = dict(categories=args.category, ce=not args.no_ce, overrides=overrides)
            print("Capturing evaluated Google Sheets values...", flush=True)
            start = time.perf_counter()
            subprocess.run([sys.executable, "-c", SNAPSHOT, source, str(snapshot_file),
                            json.dumps(snapshot_options)], cwd=candidate / "checker",
                           env=env, check=True)
            snapshot_seconds = time.perf_counter() - start
            snapshot_path = str(snapshot_file)
        for name, root in runs:
            print(f"Generating {name}...", flush=True)
            stage = Path(td) / name
            options = dict(
                source=str(source), root=str(root),
                registry_path=str(root / "checker" / "generation.json"),
                categories=args.category, groups=args.group, sheet_sources=overrides,
                original_xml=str(refs), original_patches=str(patches),
                ce=not args.no_ce, optimize=not args.no_optimize,
                output_dir=str(stage),
            )
            start = time.perf_counter()
            run_config = dict(snapshot=snapshot_path, profile=str(profile) if profile else None)
            subprocess.run([sys.executable, "-c", RUN, json.dumps(options), json.dumps(run_config)],
                           cwd=root / "checker", env=env, check=True)
            elapsed = time.perf_counter() - start
            generated, removed = files(stage)
            results.append(dict(seconds=elapsed, files=generated, remove=removed))
        if profile is not None:
            print(f"Profile saved: {profile}", flush=True)
            return 0
        a, b = results
        differences = [
            dict(destination=path, baseline=a["files"].get(path), candidate=b["files"].get(path))
            for path in sorted(a["files"].keys() | b["files"].keys())
            if a["files"].get(path) != b["files"].get(path)
        ]
        output = dict(
            input_source="google_snapshot" if google else "local_workbook",
            snapshot_seconds=snapshot_seconds,
            baseline_seconds=a["seconds"], candidate_seconds=b["seconds"],
            speedup=a["seconds"] / b["seconds"] if b["seconds"] else None,
            baseline_files=len(a["files"]), candidate_files=len(b["files"]),
            baseline_bytes=sum(item["size"] for item in a["files"].values()),
            candidate_bytes=sum(item["size"] for item in b["files"].values()),
            differences=differences,
            baseline_remove=a["remove"], candidate_remove=b["remove"],
            identical=not differences and a["remove"] == b["remove"],
        )
        report.write_text(json.dumps(output, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(json.dumps(output, indent=2, ensure_ascii=False))
        return 0 if output["identical"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
