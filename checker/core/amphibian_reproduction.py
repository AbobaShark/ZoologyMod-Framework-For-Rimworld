"""Amphibian reproduction XML generated from EggMilkWool, without species literals.

An independent amphibian table holds larval development and cohort
limits; ordinary egg production rows are joined by species DefName.
The original egg/item definitions supply non-biological artwork and Thing metadata.
"""
from __future__ import annotations

from copy import deepcopy
from decimal import Decimal, InvalidOperation
from lxml import etree as E
import re

# EggMilkWool contains independent side-by-side tables. Their rows do not
# correspond, so the biological records must always join by XML DefName.
# Names and the unique two-column table anchors replace Excel coordinates.
EGG_FIELDS = (
    'Name', 'Species', 'hatcherDaystoHatch', 'eggLayIntervalDays',
    'eggCountRange', 'Def name', 'Incubation time real life (days)',
    'Eggs/year', 'Clutch size',
)
AMPHIBIAN_FIELDS = (
    'Name', 'Def name',
    'Tadpole development (real days)',
    'Tadpole development (RimWorld days)',
    'Hatched tadpoles (gameplay count range)',
    'Froglets per clutch (gameplay count range)',
    'Fertilization mode',
    'Ovum diameter (mm)',
    'Hydrated jelly diameter (mm)',
    'Clutch jelly geometry',
    'Egg string pitch (mm)',
    'Wet mass per ovum + jelly (g)',
    'Ovum gross energy (J)',
    'Hatch tadpole wet mass (g)',
    'Postmetamorph newborn mass (kg)',
    'Tadpole gross energy (MJ/kg wet)',
    'Energy per Nutrition (J)',
    'Mean clutch wet mass (kg)',
    'Mean clutch Nutrition',
)
INTEGER_RANGE = re.compile(r'([1-9]\d*)(?:~([1-9]\d*))?\Z')
DEF_REF = re.compile(r'<li>\s*([A-Za-z][A-Za-z_0-9]*)\s*</li>\Z')

AMBI_DEFS = 'DLC/Odyssey/Defs/ThingDefs_Items/AmphibianRoe.xml'
AMBI_PATCH = 'DLC/Odyssey/Patches/Misc/CompProperties_EggLayer_Amphibians.xml'


def _positive_number(value, label):
    try:
        number = Decimal(str(value).strip().replace(',', '.'))
    except (InvalidOperation, ValueError):
        raise ValueError(f'{label}: not numeric: {value!r}') from None
    if not number.is_finite() or number <= 0:
        raise ValueError(f'{label}: expected positive finite number: {value!r}')
    return number


def _positive_range(text, label):
    match = INTEGER_RANGE.fullmatch(str(text).strip())
    if not match:
        raise ValueError(f'{label}: expected positive integer range: {text!r}')
    low, high = int(match[1]), int(match[2] or match[1])
    if low > high or high > 2147483647:
        raise ValueError(f'{label}: invalid interval: {text!r}')
    return (low, high)


def _normalize_header(header):
    """Pandas suffixes repeated headings as '.1', '.2', etc."""
    return re.sub(r'\.\d+$', '', str(header).strip())


def _find_section(headers, required):
    """Resolve one independent table by its header anchor, not sheet offsets.

    Only headers between the anchor and the final required field belong to
    the table. This deliberately ignores stale duplicate columns to the right.
    Missing or duplicated required fields fail rather than silently borrowing
    columns from another table.
    """
    candidates = []
    for start in range(len(headers) - 1):
        if headers[start:start + 2] != list(required[:2]):
            continue
        ends = [idx for idx in range(start + 1, len(headers))
                if headers[idx] == required[-1]]
        if not ends:
            continue
        end = ends[0]
        segment = headers[start:end + 1]
        if any(segment.count(field) != 1 for field in required):
            continue
        positions = {field: start + segment.index(field) for field in required}
        candidates.append(positions)
    if len(candidates) != 1:
        raise ValueError(
            f'EggMilkWool: expected one unambiguous {required[0]!r}/'
            f'{required[1]!r} section with {required[-1]!r}; found {len(candidates)}')
    return candidates[0]


def _as_xml_def(value):
    return DEF_REF.fullmatch(str(value).strip())


def external_roe_rows(egg_frame, original_columns=None):
    """Join two physically independent EggMilkWool tables by animal DefName.

    The normal egg-production rows continue to describe every egg-laying
    species. Only rows in the separate amphibian table with an explicit
    EXTERNAL mode produce amphibian definitions.
    """
    columns = list(egg_frame.columns)
    if original_columns is not None and len(original_columns) != len(columns):
        raise ValueError('EggMilkWool: header width differs from data width')
    raw_headers = original_columns if original_columns is not None else columns
    headers = [_normalize_header(value) for value in raw_headers]
    egg_positions = _find_section(headers, EGG_FIELDS)
    amphibian_positions = _find_section(headers, AMPHIBIAN_FIELDS)

    def record(row_number, positions):
        return {field: egg_frame.iat[row_number, idx]
                for field, idx in positions.items()}

    egg_by_def = {}
    for n in range(len(egg_frame)):
        row = record(n, egg_positions)
        name = str(row['Def name']).strip()
        if not name or name.casefold() in ('nan', 'none'):
            continue
        match = _as_xml_def(name)
        if not match:
            raise ValueError(
                f'EggMilkWool: malformed ordinary egg Def name on row {n + 2}: {name!r}')
        race = match[1]
        if race in egg_by_def:
            raise ValueError(f'EggMilkWool: duplicate egg production DefName {race}')
        egg_by_def[race] = row

    rows = {}
    for n in range(len(egg_frame)):
        larva = record(n, amphibian_positions)
        mode = str(larva['Fertilization mode']).strip().upper()
        name = str(larva['Def name']).strip()
        if mode in ('', 'NAN', 'NONE'):
            if name and name.casefold() not in ('nan', 'none'):
                raise ValueError(
                    f'EggMilkWool: amphibian {name!r} on row {n + 2} '
                    'has no fertilization mode')
            continue
        if mode != 'EXTERNAL':
            raise ValueError(
                f'EggMilkWool: unsupported fertilization mode {mode!r} on row {n + 2}')
        match = _as_xml_def(name)
        if not match:
            raise ValueError(
                f'EggMilkWool: amphibian Def name on row {n + 2} invalid: {name!r}')
        race = match[1]
        if race in rows:
            raise ValueError(f'EggMilkWool: duplicate amphibian DefName {race}')
        if race not in egg_by_def:
            raise ValueError(
                f'EggMilkWool: amphibian {race} has no matching ordinary egg '
                'production row in the separate egg table')
        egg = egg_by_def[race]
        eggs = _positive_range(egg['eggCountRange'], f'{race}.eggCountRange')
        tadpoles = _positive_range(
            larva['Hatched tadpoles (gameplay count range)'],
            f'{race}.hatchedTadpoles')
        froglets = _positive_range(
            larva['Froglets per clutch (gameplay count range)'],
            f'{race}.froglets')
        hatch = _positive_number(egg['hatcherDaystoHatch'], f'{race}.hatchDays')
        interval = _positive_number(egg['eggLayIntervalDays'], f'{race}.eggLayIntervalDays')
        real_larval = _positive_number(
            larva['Tadpole development (real days)'], f'{race}.larvalRealDays')
        game_larval = _positive_number(
            larva['Tadpole development (RimWorld days)'], f'{race}.larvalGameDays')
        if froglets[0] > tadpoles[1]:
            raise ValueError(f'{race}: minimum froglets exceeds maximum tadpoles')
        geom = str(larva['Clutch jelly geometry']).strip().upper()
        if geom not in ('SPHERE', 'STRING'):
            raise ValueError(f'{race}: egg jelly geometry must be SPHERE or STRING')
        if geom == 'STRING':
            _positive_number(larva['Egg string pitch (mm)'], f'{race}.stringPitchMm')
        mass_g = _positive_number(
            larva['Wet mass per ovum + jelly (g)'], f'{race}.ovumWetMassG')
        energy_j = _positive_number(
            larva['Ovum gross energy (J)'], f'{race}.ovumEnergyJ')
        hatch_g = _positive_number(
            larva['Hatch tadpole wet mass (g)'], f'{race}.hatchTadpoleMassG')
        metamorph_kg = _positive_number(
            larva['Postmetamorph newborn mass (kg)'], f'{race}.newbornMassKg')
        tadpole_energy = _positive_number(
            larva['Tadpole gross energy (MJ/kg wet)'], f'{race}.tadpoleEnergyMJkg')
        joules_per_nutrition = _positive_number(
            larva['Energy per Nutrition (J)'], f'{race}.joulesPerNutrition')
        mean_mass_kg = _positive_number(
            larva['Mean clutch wet mass (kg)'], f'{race}.meanRoeMassKg')
        mean_nutrition = _positive_number(
            larva['Mean clutch Nutrition'], f'{race}.meanRoeNutrition')
        mean_eggs = _positive_number(egg['Clutch size'], f'{race}.meanClutchOva')
        if mean_eggs != mean_eggs.to_integral_value():
            raise ValueError(f'{race}: mean clutch size must be an integer')
        if not Decimal(eggs[0]) <= mean_eggs <= Decimal(eggs[1]):
            raise ValueError(f'{race}: mean clutch size must fall inside eggCountRange')
        expected_mass_kg = mean_eggs * mass_g / Decimal('1000')
        expected_nutrition = mean_eggs * energy_j / joules_per_nutrition
        if abs(mean_mass_kg - expected_mass_kg) > expected_mass_kg * Decimal('0.00001'):
            raise ValueError(f'{race}: mean wet clutch mass does not match count × ovum mass')
        if (abs(mean_nutrition - expected_nutrition)
                > expected_nutrition * Decimal('0.00001')):
            raise ValueError(f'{race}: mean Nutrition does not match chemical energy')
        rows[race] = {
            'race': race,
            'common': str(larva['Name']).strip() or race,
            'egg_range': str(egg['eggCountRange']).strip(),
            'tadpole_range': str(larva['Hatched tadpoles (gameplay count range)']).strip(),
            'froglet_range': str(larva['Froglets per clutch (gameplay count range)']).strip(),
            'hatch_days': format(hatch.normalize(), 'f'),
            'egg_interval': format(interval.normalize(), 'f'),
            'larval_days': format(game_larval.normalize(), 'f'),
            'real_larval_days': format(real_larval.normalize(), 'f'),
            'mean_eggs': int(mean_eggs),
            'mass_per_egg_kg': format((mass_g / Decimal('1000')).normalize(), 'f'),
            'energy_per_egg_j': format(energy_j.normalize(), 'f'),
            'hatch_mass_kg': format((hatch_g / Decimal('1000')).normalize(), 'f'),
            'metamorph_mass_kg': format(metamorph_kg.normalize(), 'f'),
            'tadpole_energy_mj_kg': format(tadpole_energy.normalize(), 'f'),
            'joules_per_nutrition': format(joules_per_nutrition.normalize(), 'f'),
            'mean_roe_mass_kg': format(mean_mass_kg.normalize(), 'f'),
            'mean_roe_nutrition': format(mean_nutrition.normalize(), 'f'),
        }
    return rows


def _set(parent, name, value):
    child = parent.find(name)
    if child is None:
        child = E.SubElement(parent, name)
    child.text = str(value)


def _remove(parent, name):
    for node in parent.findall(name):
        parent.remove(node)


def _roe_comp(node):
    comps = node.find('comps')
    if comps is None:
        comps = E.SubElement(node, 'comps')
    for comp in comps.findall('li'):
        if 'CompProperties_AmphibianRoe' in comp.get('Class', ''):
            return comp
    return E.SubElement(comps, 'li', Class='ZoologyMod.CompProperties_AmphibianRoe, ZoologyMod')


def _tad_comp(node):
    comps = node.find('comps')
    if comps is None:
        comps = E.SubElement(node, 'comps')
    for comp in comps.findall('li'):
        if 'CompProperties_Tadpole' in comp.get('Class', ''):
            return comp
    return E.SubElement(comps, 'li', Class='ZoologyMod.CompProperties_Tadpole, ZoologyMod')


def _set_ingestible(node):
    ingestible = node.find('ingestible')
    if ingestible is None:
        ingestible = E.SubElement(node, 'ingestible')
    _set(ingestible, 'foodType', 'Meat')
    _set(ingestible, 'preferability', 'RawBad')


def _neutralize_cloned_color(node):
    color = node.find('./graphicData/color')
    if color is not None:
        color.text = '(255,255,255)'


def render_roe_defs(xml_tree, species):
    root = xml_tree.getroot()
    current = {n.findtext('defName'): n for n in root.findall('ThingDef')}
    templates = (
        next((v for v in current.values() if v.get('ParentName') == 'EggUnfertBase'), None),
        next((v for k, v in current.items()
              if k.endswith('Fertilized') and not k.endswith('Unfertilized')), None),
        next((v for v in current.values() if v.findtext('thingClass') == 'ThingWithComps'), None),
    )
    if any(template is None for template in templates):
        raise ValueError('AmphibianRoe.xml: required reusable egg and larva templates absent')
    expected = set()
    for row in species.values():
        race = row['race']
        expected.update((f'Egg{race}Unfertilized', f'Egg{race}Fertilized', f'Zoology_{race}Tadpole'))
        display = re.sub(r'(?<!^)(?=[A-Z])', ' ', row['common']).lower()
        for phase, name, template in (
            ('unfertilized', f'Egg{race}Unfertilized', templates[0]),
            ('fertilized', f'Egg{race}Fertilized', templates[1]),
            ('tadpole', f'Zoology_{race}Tadpole', templates[2]),
        ):
            node = current.get(name)
            if node is None:
                node = deepcopy(template)
                root.append(node)
                current[name] = node
                _set(node, 'label', display + (' tadpole' if phase == 'tadpole'
                    else f' roe ({phase})'))
                _neutralize_cloned_color(node)
            _set(node, 'defName', name)
            if phase == 'tadpole':
                c = _tad_comp(node)
                _set(c, 'pawnKind', race)
                _set(c, 'daysToMetamorphosis', row['larval_days'])
                _set(c, 'frogletCountRange', row['froglet_range'])
                _set(c, 'hatchMassKg', row['hatch_mass_kg'])
                _set(c, 'metamorphMassKg', row['metamorph_mass_kg'])
                _set(c, 'grossEnergyMJPerKg', row['tadpole_energy_mj_kg'])
                _set(c, 'joulesPerNutrition', row['joules_per_nutrition'])
                _set_ingestible(node)
                stats = node.find('statBases')
                if stats is None:
                    stats = E.SubElement(node, 'statBases')
                # Meaningful base values for dev-spawned items; per-instance
                # StatPart uses actual current number, body mass and progress.
                count_low, count_high = _positive_range(
                    row['tadpole_range'], f'{race}.hatchedTadpoles')
                mid_count = Decimal(count_low + count_high) / Decimal('2')
                seed_mass = Decimal(row['hatch_mass_kg']) * mid_count
                seed_energy = (seed_mass * Decimal(row['tadpole_energy_mj_kg'])
                               * Decimal('1000000') / Decimal(row['joules_per_nutrition']))
                _set(stats, 'Mass', format(seed_mass.normalize(), 'f'))
                _set(stats, 'Nutrition', format(seed_energy.normalize(), 'f'))
            else:
                node.set('ParentName', 'EggUnfertBase')
                _set(node, 'stackLimit', 1)
                data = node.find('graphicData')
                if data is None:
                    data = E.SubElement(node, 'graphicData')
                _set(data, 'graphicClass', 'Graphic_Single')
                _set(data, 'texPath', 'Things/Item/Zoology/AmphibianRoe')
                stats = node.find('statBases')
                if stats is None:
                    stats = E.SubElement(node, 'statBases')
                # Eliminate rain/unroofed HP attrition while preserving direct attacks.
                _set(stats, 'DeteriorationRate', 0)
                _set(stats, 'Mass', row['mean_roe_mass_kg'])
                _set(stats, 'Nutrition', row['mean_roe_nutrition'])
                comp = _roe_comp(node)
                _set(comp, 'parentRace', race)
                _set(comp, 'fertilized', 'true' if phase == 'fertilized' else 'false')
                _set(comp, 'defaultOvaCount', row['mean_eggs'])
                _set(comp, 'wetMassPerOvumKg', row['mass_per_egg_kg'])
                _set(comp, 'grossEnergyPerOvumJ', row['energy_per_egg_j'])
                _set(comp, 'joulesPerNutrition', row['joules_per_nutrition'])
                if phase == 'unfertilized':
                    _set(comp, 'fertilizedRoeDef', f'Egg{race}Fertilized')
                    _remove(comp, 'daysToHatch')
                    _remove(comp, 'tadpoleDef')
                    _remove(comp, 'hatchlingCountRange')
                else:
                    _remove(comp, 'fertilizedRoeDef')
                    _set(comp, 'daysToHatch', row['hatch_days'])
                    _set(comp, 'tadpoleDef', f'Zoology_{race}Tadpole')
                    _set(comp, 'hatchlingCountRange', row['tadpole_range'])
    for name, node in list(current.items()):
        if (node.find('./comps/li[@Class="ZoologyMod.CompProperties_AmphibianRoe, ZoologyMod"]') is not None
            or node.find('./comps/li[@Class="ZoologyMod.CompProperties_Tadpole, ZoologyMod"]') is not None):
            if name not in expected:
                raise ValueError(f'Amphibian definition {name} lacks matching EggMilkWool EXTERNAL row')
    return xml_tree


def _value_comp(row):
    node = E.Element('li', Class='CompProperties_EggLayer')
    _set(node, 'eggLayIntervalDays', row['egg_interval'])
    _set(node, 'eggCountRange', row['egg_range'])
    # External fertilization occurs after laying, not within CompEggLayer.
    _set(node, 'eggFertilizationCountMax', 0)
    _set(node, 'eggUnfertilizedDef', f"Egg{row['race']}Unfertilized")
    _set(node, 'eggFertilizedDef', f"Egg{row['race']}Fertilized")
    return node


def render_roe_patch(species):
    root = E.Element('Patch')
    for row in species.values():
        race = row['race']
        base = f'/Defs/ThingDef[defName="{race}"]'
        comp_path = base + '/comps/li[@Class="CompProperties_EggLayer"]'
        op = E.SubElement(root, 'Operation', Class='PatchOperationConditional')
        _set(op, 'xpath', comp_path)
        match = E.SubElement(op, 'match', Class='PatchOperationReplace')
        E.SubElement(match, 'xpath').text = comp_path
        E.SubElement(match, 'value').append(_value_comp(row))
        nomatch = E.SubElement(op, 'nomatch', Class='PatchOperationConditional')
        _set(nomatch, 'xpath', base + '/comps')
        addition = E.SubElement(nomatch, 'match', Class='PatchOperationAdd')
        _set(addition, 'xpath', base + '/comps')
        E.SubElement(addition, 'value').append(_value_comp(row))
        create = E.SubElement(nomatch, 'nomatch', Class='PatchOperationAdd')
        _set(create, 'xpath', base)
        E.SubElement(E.SubElement(create, 'value'), 'comps').append(_value_comp(row))
    return E.ElementTree(root)
