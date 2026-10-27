"""Produce reviewable evidence from completed native-game runs; never fabricate passes."""
from pathlib import Path
import csv
import hashlib
import json
import re
import statistics
from lxml import etree as E

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs/animal-behavior-1.7/validation'
summary = {'runtime': {}, 'performance': {}, 'sources': {}}
calibration = []
for mode in ('vanilla', 'ce'):
    path = OUT / f'runtime-{mode}.txt'
    text = path.read_text(encoding='utf-8-sig')
    passes = len(re.findall(r'^PASS ', text, re.M))
    failures = re.findall(r'^(?:FAIL|EXCEPTION).*', text, re.M)
    complete = bool(re.search(r'^COMPLETE\s*$', text, re.M))
    summary['runtime'][mode] = {'passes': passes, 'failures': failures, 'complete': complete,
                                'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    for line in text.splitlines():
        if not line.startswith('PROFILE ZVenom_'):
            continue
        profile, _, fields = line.partition(': ')
        calibration.append({'mode': mode, 'profile': profile.removeprefix('PROFILE '),
                            **dict(part.split('=', 1) for part in fields.split(', '))})
    assert complete and not failures, (mode, summary['runtime'][mode])
for mode in ('baseline', 'updated', 'no-mod'):
    path = OUT / f'performance-{mode}-final.txt'
    text = path.read_text(encoding='utf-8-sig')
    assert f'COMPLETE {mode}' in text and 'EXCEPTION' not in text, mode
    samples = []
    stress = []
    for line in text.splitlines():
        if not line.startswith(f'{mode} ') or 'tickDelta=' not in line:
            continue
        _, _, fields = line.partition(': ')
        values = dict(part.split('=', 1) for part in fields.split(', '))
        values = {k: float(v) if re.fullmatch(r'-?\d+(\.\d+)?', v) else v for k, v in values.items()}
        (samples if line.startswith(f'{mode} normal:') else stress).append(values)
    assert len(samples) >= 3 and all(s['spawnedAnimalsStart'] == 419 and s['ticks'] == s['tickDelta'] == 2000 for s in samples), mode
    summary['performance'][mode] = {
        'samples': samples, 'additional_stress': stress,
        'median_ms_per_tick': statistics.median(s['ms/tick'] for s in samples),
        'median_uncapped_TPS': statistics.median(s['uncappedTPS'] for s in samples),
        'median_cpu_ms_per_tick': statistics.median(s['cpuMs/tick'] for s in samples),
        'median_cpu_TPS': statistics.median(s['cpuTPS'] for s in samples),
        'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
base = summary['performance']['baseline']
current = summary['performance']['updated']
summary['performance']['updated_vs_baseline_percent'] = {
    'wall_tick_time': 100 * (current['median_ms_per_tick'] / base['median_ms_per_tick'] - 1),
    'cpu_tick_time': 100 * (current['median_cpu_ms_per_tick'] / base['median_cpu_ms_per_tick'] - 1)}

profile = Path.home() / 'AppData/Local/Temp'
worlds = [profile / name / 'Saves/ZoologyPerfStart.rws' for name in ('ZoologyBenchmark17', 'ZoologyBenchmarkNoMod17')]
canonical = []
for path in worlds:
    doc = E.parse(str(path), E.XMLParser(remove_blank_text=True))
    removed = []
    for node in list(doc.xpath('//*[@Class]')):
        if node.get('Class', '').startswith('ZoologyMod.'):
            removed.append(node.get('Class'))
            node.getparent().remove(node)
    for parent in doc.xpath('/savegame/meta/modIds|/savegame/meta/modNames'):
        for node in list(parent):
            if 'zoology' in (node.text or '').lower():
                parent.remove(node)
    digest = hashlib.sha256(E.tostring(doc, method='c14n')).hexdigest()
    canonical.append(digest)
    summary['sources'][path.parent.parent.name] = {'canonical_sha256': digest, 'removed_components': removed}
assert canonical[0] == canonical[1], 'Control world differs beyond Zoology components/metadata'
summary['sources']['identical_control_world'] = True

for path in (ROOT / 'Zoology').rglob('*.xml'):
    if 'Source' not in path.parts:
        E.parse(str(path))
with (OUT / 'venom-human-calibration.csv').open('w', encoding='utf-8-sig', newline='') as stream:
    writer = csv.DictWriter(stream, fieldnames=calibration[0].keys())
    writer.writeheader()
    writer.writerows(calibration)
(OUT / 'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'runtime': summary['runtime'], 'performance_delta': summary['performance']['updated_vs_baseline_percent'],
                  'identical_world': canonical[0] == canonical[1], 'calibration_rows': len(calibration)}, ensure_ascii=False, indent=2))
