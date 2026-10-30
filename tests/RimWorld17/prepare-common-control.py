"""Remove 1.7 food-policy ThingDef references absent from the comparison mode.

Only food restriction
allowedDefs entries are removed. Map, pawns, jobs and world data stay intact.
"""
from argparse import ArgumentParser
from pathlib import Path
import re
from xml.etree import ElementTree

parser = ArgumentParser()
parser.add_argument("source", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("missing_defs", type=Path)
args = parser.parse_args()

names = set(args.missing_defs.read_text(encoding="utf-8").splitlines())
if not names or any(not re.fullmatch(r"(?:Meat_[A-Za-z0-9_]+|ToxicMeat)", name) for name in names):
    raise ValueError("Expected a nonempty list of meat ThingDefs")

source = args.source.read_text(encoding="utf-8")
root = ElementTree.fromstring(source)
allowed = root.find("./game/foodRestrictionDatabase/foodRestrictions")
if allowed is None:
    raise ValueError("No food restriction database")
for name in names:
    count_in_policies = sum(1 for policy in allowed.findall("li")
                            for item in policy.findall("./filter/allowedDefs/li") if item.text == name)
    count_in_save = source.count(f"<li>{name}</li>")
    if count_in_policies != 6 or count_in_save != count_in_policies:
        raise ValueError(f"Unexpected reference locations for {name}: {count_in_policies}/{count_in_save}")

removed = 0
for name in sorted(names):
    source, count = re.subn(r"(?m)^\s*<li>" + re.escape(name) + r"</li>\r?\n", "", source)
    removed += count
if removed != len(names) * 6:
    raise ValueError(f"Expected {len(names) * 6} removals, found {removed}")

args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(source, encoding="utf-8")
print(f"Removed {removed} food-policy references; saved {args.output}")
