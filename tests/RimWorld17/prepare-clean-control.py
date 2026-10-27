"""Strip only Zoology serialized components from the identical benchmark world."""
from pathlib import Path
from lxml import etree as E
import json

base = Path.home() / 'AppData/Local/Temp'
source = base / 'ZoologyBenchmark17/Saves/ZoologyPerfStart.rws'
destination = base / 'ZoologyBenchmarkNoMod17/Saves/ZoologyPerfStart.rws'
tree = E.parse(str(source))
removed = []
for node in list(tree.xpath('//*[@Class]')):
    if node.get('Class', '').startswith('ZoologyMod.'):
        removed.append(node.get('Class')); node.getparent().remove(node)
# Metadata changes avoid a misleading different-mods warning; world/map/pawns are untouched.
for parent in tree.xpath('/savegame/meta/modIds|/savegame/meta/modNames'):
    for node in list(parent):
        if 'zoology' in (node.text or '').lower(): parent.remove(node)
destination.parent.mkdir(parents=True, exist_ok=True)
tree.write(str(destination), encoding='utf-8', xml_declaration=True)
print(json.dumps({'source': str(source), 'destination': str(destination), 'removed_components': removed}, indent=2))
