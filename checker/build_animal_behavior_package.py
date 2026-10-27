"""Reproduce validated Zoology behavior/venom XML from exported Sheets snapshots.

Snapshots are get_spreadsheet_range UNFORMATTED_VALUE JSON responses (values).
Usage: python build_animal_behavior_package.py --snapshots DIR --venom-zip ZIP
The original Google Sheet and ZIP are never written.
"""
from __future__ import annotations
import argparse
import copy
import csv
import io
import json
import re
import zipfile
from pathlib import Path

import pandas as pd
from lxml import etree as E
from rimworld_xml_generator import build_tools_vanilla, build_tools_ce, write_xml
from rimworld_patch_fixer import PatchGenerator

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Zoology"
MOD_FOLDERS = {
    "VanillaExpanded.VanillaAnimalsExpanded": "ModPatches/VanillaAnimalsExpanded",
    "VanillaExpanded.VAEWaste": "ModPatches/VanillaAnimalsExpandedWasteland",
    "sarg.alphaanimals": "ModPatches/AlphaAnimals",
    "Spino.Megafauna": "ModPatches/Megafauna",
}


def child(parent, tag, text=None, **attrs):
    el = E.SubElement(parent, tag, **attrs)
    if text is not None:
        el.text = str(text)
    return el


def guarded_sequence(root, xpath):
    op = child(root, "Operation", Class="PatchOperationConditional")
    child(op, "xpath", xpath)
    seq = child(op, "match", Class="PatchOperationSequence")
    return child(seq, "operations")


def extension(ops, xpath, class_name, **fields):
    op = child(ops, "li", Class="PatchOperationAddModExtension")
    child(op, "xpath", xpath)
    payload = child(child(op, "value"), "li", Class=f"ZoologyMod.{class_name}, ZoologyMod")
    for key, value in fields.items():
        child(payload, key, value)


def replace_tools(branch, def_name, tools):
    branch.set("Class", "PatchOperationSequence")
    ops = child(branch, "operations")
    xpath = f'Defs/ThingDef[defName="{def_name}"]'
    conditional = child(ops, "li", Class="PatchOperationConditional")
    child(conditional, "xpath", xpath + "/tools")
    child(child(conditional, "match", Class="PatchOperationRemove"), "xpath", xpath + "/tools")
    op = child(ops, "li", Class="PatchOperationAdd")
    child(op, "xpath", xpath)
    tools.set("Inherit", "False")
    child(op, "value").append(tools)


def load_rows(path):
    values = json.loads(path.read_text(encoding="utf-8"))["values"]
    return [pd.Series(dict(zip(values[0], row))) for row in values[1:] if len(row) > 2]


def build(snapshots: Path, archive: Path, game: Path):
    animals = load_rows(snapshots / "Animals_unformatted.json")
    ce_rows = {r['Latin name']: r for r in load_rows(snapshots / "Animals_CE_unformatted.json")}
    source_hashes = {str(archive): __import__('hashlib').sha256(archive.read_bytes()).hexdigest()}
    archive_status = []
    with zipfile.ZipFile(archive) as z:
        xmls = {name: E.fromstring(z.read(name)) for name in z.namelist() if name.endswith('.xml')}
        assignments = list(csv.DictReader(io.StringIO(z.read('ASSIGNMENTS.csv').decode('utf-8-sig'))))
    # Validate exact XML parent Names, not just defNames, before installing anything.
    parents = set()
    for folder in [game / 'Data/Core/Defs', MOD / 'Core/Defs']:
        for path in folder.rglob('*.xml'):
            for node in E.parse(str(path)).getroot():
                if node.get('Name'):
                    parents.add((node.tag, node.get('Name')))
    all_defs = {}
    for name, root in xmls.items():
        for node in root:
            key = (node.tag, node.findtext('defName'))
            assert key not in all_defs, f'Duplicate {key}'
            all_defs[key] = node
            if node.get('ParentName'):
                assert (node.tag, node.get('ParentName')) in parents, (name, node.get('ParentName'))
    assert len(xmls) == 6
    assert len([k for k in all_defs if k[0] == 'ToolCapacityDef']) == 17
    for capacity in xmls['Defs/ToolCapacityDefs/ZVenom_ToolCapacities.xml']:
        name = capacity.findtext('defName')
        maneuvers = [n for n in xmls['Defs/ManeuverDefs/ZVenom_Maneuvers.xml'] if n.findtext('requiredCapacity') == name]
        assert len(maneuvers) == 1, name
        damage = all_defs['DamageDef', maneuvers[0].findtext('verb/meleeDamageDef')]
        assert ('HediffDef', damage.findtext('additionalHediffs/li/hediff')) in all_defs
        assert float(damage.findtext('additionalHediffs/li/severityPerDamageDealt')) > 0
    # The live export owns the inventory of profiles; the draft ZIP is not authoritative.
    active_capacities = {part.strip() for row in animals for value in row.values
                         if isinstance(value, str) for part in re.split(r'[,;|]', value)
                         if part.strip().startswith('ZVenom_')}
    maneuvers = xmls['Defs/ManeuverDefs/ZVenom_Maneuvers.xml']
    active_maneuvers = [m for m in maneuvers if m.findtext('requiredCapacity') in active_capacities]
    active_damages = {m.findtext('verb/meleeDamageDef') for m in active_maneuvers}
    active_hediffs = {all_defs['DamageDef', d].findtext('additionalHediffs/li/hediff') for d in active_damages}
    removed = []
    for tag, filename, active in [
        ('ToolCapacityDef', 'ToolCapacityDefs/ZVenom_ToolCapacities.xml', active_capacities),
        ('ManeuverDef', 'ManeuverDefs/ZVenom_Maneuvers.xml', {m.findtext('defName') for m in active_maneuvers}),
        ('DamageDef', 'DamageDefs/ZVenom_Damages.xml', active_damages),
        ('HediffDef', 'HediffDefs/ZVenom_Hediffs.xml', active_hediffs)]:
        root = xmls['Defs/' + filename]
        for node in list(root):
            if node.findtext('defName') not in active:
                removed.append(node.findtext('defName')); root.remove(node)
    assert active_capacities <= {n.findtext('defName') for n in xmls['Defs/ToolCapacityDefs/ZVenom_ToolCapacities.xml']}
    # Monitor venom potentiates wounds and hypotension; even a small injury has a visible mild effect.
    varanid = all_defs['HediffDef', 'ZVenom_Varanid']
    stages = varanid.find('stages')
    thresholds = [0, .12, .35, .65]
    for stage, threshold in zip(stages, thresholds):
        stage.find('minSeverity').text = str(threshold)
    stages[0].find('painOffset').text = '.03'
    child(stages[0], 'totalBleedFactor', '1.05')
    for stage, offset in [(stages[1], -.05), (stages[2], -.12)]:
        mods = stage.find('capMods')
        if mods is None: mods = child(stage, 'capMods')
        modifier = child(mods, 'li'); child(modifier, 'capacity', 'BloodPumping'); child(modifier, 'offset', offset)
    # The spur must inherit all Scratch properties, including harmAllLayers/overkill.
    spur = all_defs['DamageDef', 'ZVenom_Damage_PlatypusSpur']
    spur.set('ParentName', 'Scratch')
    for field in list(spur):
        if field.tag not in ('defName', 'label', 'additionalHediffs'):
            spur.remove(field)
    hediffs = xmls['Defs/HediffDefs/ZVenom_Hediffs.xml']
    for h in hediffs:
        h.find('hediffClass').text = 'ZoologyMod.Hediff_AnimalVenom, ZoologyMod'
        for comp in list(h.find('comps')):
            if comp.get('Class') == 'HediffCompProperties_RemoveIfOtherHediff':
                h.find('comps').remove(comp)
    # The old marker granted one hour of prophylaxis, and four weak-profile drugs were unnecessary.
    for name, root in xmls.items():
        if name.endswith('ZVenom_AntivenomMarkers.xml'):
            archive_status.append((name, 'rejected', '', 'Replaced by targeted ingestion; no prophylactic marker'))
            continue
        if name.endswith('ZVenom_AntivenomDrugs.xml'):
            continue
        destination = MOD / 'Core' / name
        write_xml(str(destination), root)
        archive_status.append((name, 'corrected', str(destination.relative_to(REPO)),
                               'Unused draft profiles removed against live export; ParentName chains checked; physiological calibration'))
    # Reuse the stock drug/administer system, with one exact-profile outcome and no hidden protection.
    drugs = E.Element('Defs')
    samples = E.Element('Defs')
    recipes = E.Element('Defs')
    cobra_profile = all_defs['DamageDef', 'ZVenom_Damage_CobraBite'].findtext('additionalHediffs/li/hediff')
    hydra_row = next(r for r in animals if r['Latin name'] == 'Hydra toxifera')
    hydra_cap = hydra_row['Bite capacities']
    hydra_maneuver = next(m for m in active_maneuvers if m.findtext('requiredCapacity') == hydra_cap)
    hydra_profile = all_defs['DamageDef', hydra_maneuver.findtext('verb/meleeDamageDef')].findtext('additionalHediffs/li/hediff')
    for family, venom in [('Cobra', cobra_profile), ('Viper', 'ZVenom_ViperHemo'), ('Hydra', hydra_profile)]:
        drug_name, sample_name = f'ZVenom_Drug_{family}', f'ZVenom_Sample_{family}'
        drug = child(drugs, 'ThingDef', ParentName='DrugBase')
        child(drug, 'defName', drug_name); child(drug, 'label', family.lower() + ' antivenom')
        child(drug, 'description', 'Neutralizes the matching animal toxin. Does not heal wounds or protect against later bites.')
        child(drug, 'rotatable', 'false'); child(drug, 'stackLimit', '25')
        graphic = child(drug, 'graphicData')
        child(graphic, 'texPath', 'Things/Item/ZoologyVenom/Antivenom')
        child(graphic, 'graphicClass', 'Graphic_Single'); child(graphic, 'drawSize', '0.65')
        child(graphic, 'color', {'Cobra': '(0.85,1,0.85)', 'Viper': '(1,0.85,0.75)', 'Hydra': '(0.8,0.85,1)'}[family])
        stats = child(drug, 'statBases'); child(stats, 'MarketValue', '60'); child(stats, 'Mass', '0.1')
        ingest = child(drug, 'ingestible'); child(ingest, 'drugCategory', 'Medical'); child(ingest, 'foodType', 'Processed')
        child(ingest, 'baseIngestTicks', '100')
        outcome = child(child(ingest, 'outcomeDoers'), 'li', Class='ZoologyMod.IngestionOutcomeDoer_AnimalAntivenom, ZoologyMod')
        child(outcome, 'venom', venom)
        comp = child(child(drug, 'comps'), 'li', Class='CompProperties_Drug')
        child(comp, 'listOrder', '1000'); child(comp, 'overdoseSeverityOffset', '0~0')
        sample = child(samples, 'ThingDef', ParentName='ResourceBase')
        child(sample, 'defName', sample_name); child(sample, 'label', family.lower() + ' venom sample')
        child(sample, 'description', 'A small sample collected once when butchering an adult animal; used to make matching antivenom.')
        child(sample, 'stackLimit', '25'); child(sample, 'rotatable', 'false')
        graphic = child(sample, 'graphicData'); child(graphic, 'texPath', 'Things/Item/ZoologyVenom/VenomSample')
        child(graphic, 'graphicClass', 'Graphic_Single'); child(graphic, 'drawSize', '0.65')
        child(graphic, 'color', {'Cobra': '(0.85,1,0.85)', 'Viper': '(1,0.85,0.75)', 'Hydra': '(0.8,0.85,1)'}[family])
        stats = child(sample, 'statBases'); child(stats, 'Mass', '0.05'); child(stats, 'MarketValue', '15')
        child(child(sample, 'thingCategories'), 'li', 'Manufactured')
        recipe = child(recipes, 'RecipeDef')
        child(recipe, 'defName', 'ZVenom_Make_' + family); child(recipe, 'label', 'make ' + family.lower() + ' antivenom')
        child(recipe, 'description', 'Prepare antivenom from herbal medicine and a matching animal venom sample.')
        child(recipe, 'jobString', 'Preparing antivenom.'); child(recipe, 'workAmount', '1200')
        child(recipe, 'workSpeedStat', 'DrugSynthesisSpeed'); child(recipe, 'workSkill', 'Intellectual')
        child(recipe, 'researchPrerequisite', 'MedicineProduction')
        child(child(recipe, 'recipeUsers'), 'li', 'DrugLab')
        ingredients = child(recipe, 'ingredients')
        for item, count in [('MedicineHerbal', 2), (sample_name, 1)]:
            entry = child(ingredients, 'li'); child(child(child(entry, 'filter'), 'thingDefs'), 'li', item); child(entry, 'count', count)
        fixed = child(child(recipe, 'fixedIngredientFilter'), 'thingDefs')
        child(fixed, 'li', 'MedicineHerbal'); child(fixed, 'li', sample_name)
        child(child(recipe, 'products'), drug_name, 1)
    write_xml(str(MOD / 'Core/Defs/ThingDefs/ZVenom_AntivenomDrugs.xml'), drugs)
    write_xml(str(MOD / 'Core/Defs/ThingDefs/ZVenom_Samples.xml'), samples)
    write_xml(str(MOD / 'Core/Defs/RecipeDefs/ZVenom_Recipes.xml'), recipes)
    archive_status.append(('Defs/ThingDefs/ZVenom_AntivenomDrugs.xml', 'replaced',
                           'Zoology/Core/Defs/ThingDefs/ZVenom_AntivenomDrugs.xml', '3 drugs, valid textures, CompDrug/Administer, direct targeted outcome'))
    # Strategy extensions belong on concrete race defs. All optional animals are existence-guarded.
    behaviors = E.Element('Patch'); generator = PatchGenerator.__new__(PatchGenerator)
    for row in animals:
        name = re.search(r'<li[^>]*>([^<]+)</li>', row['XML name']).group(1).strip()
        ops = guarded_sequence(behaviors, f'Defs/ThingDef[defName="{name}"]')
        for op in generator.create_concrete_mod_extension_patches(name, row):
            op.tag = 'li'; ops.append(op)
        if name in ['AEXP_Anaconda', 'Titanoboa', 'Gigantophis']:
            extension(ops, f'Defs/ThingDef[defName="{name}"]', 'ModExtension_Constrictor')
        if name in ['Cobra', 'AEXP_Rattlesnake', 'VAEWaste_Hydra']:
            family = {'Cobra': 'Cobra', 'AEXP_Rattlesnake': 'Viper', 'VAEWaste_Hydra': 'Hydra'}[name]
            extension(ops, f'Defs/ThingDef[defName="{name}"]', 'ModExtension_VenomSample', sample='ZVenom_Sample_' + family)
    vertebrate_bodies = [
        'Human','Ape','Monkey','Bird','Pinniped','PinnipedWithTusks','Snake','SnakeWithTwoLimbs','TurtleLike',
        'Ceratops','Hadrosaurid','Theropod','Thyreophorae','Sauropod','Hydra','BipedalAnimalWithPawsClawsAndTail',
        'QuadrupedAnimalWithPaws','QuadrupedAnimalWithPawsAndHorn','QuadrupedAnimalWithPawsAndTail',
        'QuadrupedAnimalWithPawsAndTailAndSpur','QuadrupedAnimalWithPawsAndTailAttackTool',
        'QuadrupedAnimalWithHooves','QuadrupedAnimalWithHoovesAndHorn','QuadrupedAnimalWithHoovesAndHump',
        'QuadrupedAnimalWithHoovesAndTusks','QuadrupedAnimalWithHoovesTailAndHorn','QuadrupedAnimalWithHoovesTusksAndTrunk']
    for name in vertebrate_bodies:
        xpath = f'Defs/BodyDef[defName="{name}"]'
        extension(guarded_sequence(behaviors, xpath), xpath, 'ModExtension_ConstrictibleBody')
    write_xml(str(MOD / 'Core/Patches/ZoologyBehavior/Strategies.xml'), behaviors)
    # Append one generated block to the SAME species patch file, after its existing vanilla/CE tool rebuilds.
    documents = {}; coverage = []
    for assignment in assignments:
        row = next(r for r in animals if r['Latin name'] == assignment['Species_key'])
        name = re.search(r'<li[^>]*>([^<]+)</li>', row['XML name']).group(1).strip()
        folder = MOD / MOD_FOLDERS.get(str(row['MayRequire']), 'Core') / 'Patches'
        matches = []
        for path in folder.rglob('*.xml'):
            if path not in documents:
                documents[path] = E.parse(str(path))
            root = documents[path].getroot()
            if any('/tools' in (x.text or '') and re.search(r'defName\s*=\s*["\']' + re.escape(name) + r'["\']', x.text or '') for x in root.iter('xpath')):
                matches.append(path)
        if not matches and name == 'MonitorLizard':
            # The table labels this race MayRequire=No, but the installed race is in Odyssey.
            # Use an existence guard, without inventing/editing biological inputs.
            path = MOD / 'Core/Patches/ZoologyBehavior/MonitorVenom.xml'
            documents[path] = E.ElementTree(E.Element('Patch'))
            matches = [path]
        assert len(matches) == 1, (name, matches)
        path = matches[0]; root = documents[path].getroot()
        marker = ' Zoology validated venom tools for ' + name + ' '
        for comment in list(root):
            if isinstance(comment, E._Comment) and comment.text == marker:
                following = comment.getnext()
                if following is not None: root.remove(following)
                root.remove(comment)
        root.append(E.Comment(marker))
        branch = guarded_sequence(root, f'Defs/ThingDef[defName="{name}"]')
        switch = child(branch, 'li', Class='PatchOperationFindMod'); child(child(switch, 'mods'), 'li', 'Combat Extended')
        replace_tools(child(switch, 'match'), name, build_tools_ce(ce_rows[row['Latin name']], row))
        replace_tools(child(switch, 'nomatch'), name, build_tools_vanilla(row))
        col = {'W':'Poke/leg claws capacities','AB':'Horn/Antler/Tusks capacities','AH':'Bite capacities'}[assignment['Animals_capacity_column']]
        actual = row.get(col)
        coverage.append({**assignment, 'Exported_capacity':actual, 'Patch':str(path.relative_to(REPO)),
                         'Status':'source differs from archive' if actual != assignment['New_capacity'] else 'accepted'})
    for path, doc in documents.items():
        if any(isinstance(n, E._Comment) and (n.text or '').startswith(' Zoology validated venom tools for ') for n in doc.getroot()):
            write_xml(str(path), doc.getroot())
    ce = E.Element('Patch')
    # CE's global maneuver patch normally already sees all loaded defs. These explicit idempotent operations
    # also cover a third-party patch changing load order; no parallel venom application path is introduced.
    op = child(ce, 'Operation', Class='PatchOperationAttributeSet')
    child(op, 'xpath', 'Defs/ManeuverDef[starts-with(defName,"ZVenom_")]/verb')
    child(op, 'attribute', 'Class'); child(op, 'value', 'CombatExtended.VerbPropertiesCE')
    op = child(ce, 'Operation', Class='PatchOperationReplace')
    child(op, 'xpath', 'Defs/ManeuverDef[starts-with(defName,"ZVenom_")]/verb/verbClass')
    child(child(op, 'value'), 'verbClass', 'CombatExtended.Verb_MeleeAttackCE')
    op = child(ce, 'Operation', Class='PatchOperationAddModExtension')
    child(op, 'xpath', 'Defs/ToolCapacityDef[starts-with(defName,"ZVenom_") and not(defName="ZVenom_PlatypusSpur")]')
    child(child(child(op, 'value'), 'li', Class='CombatExtended.ModExtensionMeleeToolPenetration'), 'canHitInternal', 'true')
    write_xml(str(MOD / 'ModPatches/CombatExtended/Patches/ZoologyVenom/Compatibility.xml'), ce)
    report = REPO / 'docs/animal-behavior-1.7'; report.mkdir(parents=True, exist_ok=True)
    with (report/'venom-assignments.csv').open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=coverage[0].keys()); writer.writeheader(); writer.writerows(coverage)
    (report/'archive-status.json').write_text(json.dumps({'archive_sha256':source_hashes,'files':archive_status,
        'active_capacities':sorted(active_capacities), 'removed_unused_defs':removed}, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'Validated {len(xmls)} archive XML files, {len(active_capacities)} active chains, {len(coverage)} assignments; source files untouched.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--snapshots', required=True, type=Path)
    parser.add_argument('--venom-zip', required=True, type=Path)
    parser.add_argument('--game-root', type=Path, default=Path(r'D:\SteamLibrary\steamapps\common\RimWorld'))
    args = parser.parse_args()
    build(args.snapshots, args.venom_zip, args.game_root)
