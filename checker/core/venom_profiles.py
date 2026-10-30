"""Validate the spreadsheet's venom catalog and bind producers by toxin identity.

Loading is deliberately supplied by the existing AnimalStats reader; this module
performs no network access and has no built-in species or profile lists.
"""
from dataclasses import dataclass
from pathlib import Path
import re
import pandas as pd
from lxml import etree as E

REQUIRED = ('ToolCapacity', 'Venom HediffDef', 'Has antivenom', 'Profile label',
            'Color hex', 'DamageDef', 'ManeuverDef')
CAPACITY_COLUMNS = ('Poke/leg claws capacities', 'Horn/Antler/Tusks capacities',
                    'Bite capacities', 'Paw claw/punch capacities')
IDENTIFIER = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')
COLOR = re.compile(r'^#[0-9A-Fa-f]{6}$')


def boolean(value, context):
    value = str(value).strip().lower()
    if value in ('true', '1'): return True
    if value in ('false', '0'): return False
    raise ValueError(f'{context}: expected TRUE/FALSE, got {value!r}')


@dataclass(frozen=True)
class VenomProfile:
    capacity: str
    hediff: str
    has_antivenom: bool
    label: str
    color: str
    damage: str
    maneuver: str

    @property
    def suffix(self): return self.label
    @property
    def sample(self): return 'ZVenom_Sample_' + self.suffix
    @property
    def drug(self): return 'ZVenom_Drug_' + self.suffix
    @property
    def recipe(self): return 'ZVenom_Make_' + self.suffix


class VenomCatalog:
    def __init__(self, frame, mod_root):
        missing = [name for name in REQUIRED if name not in frame.columns]
        if missing: raise ValueError('Venom profiles missing columns: ' + ', '.join(missing))
        self.by_capacity = {}
        self.by_hediff = {}
        labels = {}
        for number, row in frame.iterrows():
            if all(pd.isna(row[n]) or not str(row[n]).strip() for n in REQUIRED): continue
            values = {n: '' if pd.isna(row[n]) else str(row[n]).strip() for n in REQUIRED}
            for field in REQUIRED:
                if not values[field]: raise ValueError(f'Venom profiles row {number + 2}: {field} is empty')
            for field in ('ToolCapacity', 'Venom HediffDef', 'Profile label', 'DamageDef', 'ManeuverDef'):
                if not IDENTIFIER.fullmatch(values[field]):
                    raise ValueError(f'Venom profiles row {number + 2}: invalid {field}: {values[field]!r}')
            if not COLOR.fullmatch(values['Color hex']):
                raise ValueError(f'Venom profiles row {number + 2}: invalid Color hex: {values["Color hex"]!r}')
            profile = VenomProfile(values['ToolCapacity'], values['Venom HediffDef'],
                                   boolean(values['Has antivenom'], f'Venom profiles row {number + 2}'),
                                   values['Profile label'], values['Color hex'].upper(),
                                   values['DamageDef'], values['ManeuverDef'])
            if profile.capacity in self.by_capacity:
                raise ValueError(f'duplicate ToolCapacity: {profile.capacity}')
            previous = self.by_hediff.get(profile.hediff)
            if previous and (previous.has_antivenom, previous.label, previous.color) != (profile.has_antivenom, profile.label, profile.color):
                raise ValueError(f'conflicting antivenom fields for HediffDef {profile.hediff}')
            if profile.label in labels and labels[profile.label] != profile.hediff:
                raise ValueError(f'Profile label {profile.label} names multiple HediffDefs')
            labels[profile.label] = profile.hediff
            self.by_capacity[profile.capacity] = profile
            self.by_hediff.setdefault(profile.hediff, profile)
        if not self.by_capacity: raise ValueError('Venom profiles contains no profiles')
        self._validate_defs(Path(mod_root))

    def _validate_defs(self, mod):
        definitions = {}
        for directory in ('ToolCapacityDefs', 'ManeuverDefs', 'DamageDefs', 'HediffDefs'):
            for path in (mod/'Core/Defs'/directory).glob('*.xml'):
                for node in E.parse(str(path)).getroot():
                    if not isinstance(node.tag, str): continue
                    name = node.findtext('defName')
                    if name:
                        key = (node.tag, name)
                        if key in definitions: raise ValueError(f'duplicate definition: {key}')
                        definitions[key] = node
        for profile in self.by_capacity.values():
            for kind, name in [('ToolCapacityDef', profile.capacity), ('ManeuverDef', profile.maneuver),
                               ('DamageDef', profile.damage), ('HediffDef', profile.hediff)]:
                if (kind, name) not in definitions: raise ValueError(f'{profile.capacity}: missing {kind} {name}')
            maneuver = definitions['ManeuverDef', profile.maneuver]
            damage = definitions['DamageDef', profile.damage]
            if maneuver.findtext('requiredCapacity') != profile.capacity or maneuver.findtext('verb/meleeDamageDef') != profile.damage:
                raise ValueError(f'{profile.capacity}: inconsistent maneuver/damage chain')
            if damage.xpath('./additionalHediffs/li/hediff/text()') != [profile.hediff]:
                raise ValueError(f'{profile.capacity}: inconsistent damage/hediff chain')

    def sample_for(self, row):
        if not boolean(row['Venom sample producer'], f'{row.get("Latin name", "animal")} Venom sample producer'):
            return None
        capacities = set()
        for col in CAPACITY_COLUMNS:
            capacities.update(re.split(r'[,\s]+', str(row.get(col, ''))))
        profiles = {p.hediff: p for c, p in self.by_capacity.items() if c in capacities}
        if len(profiles) != 1:
            raise ValueError(f'{row.get("Latin name", "animal")}: expected exactly one toxin identity, got {sorted(profiles)}')
        profile = next(iter(profiles.values()))
        if not profile.has_antivenom:
            raise ValueError(f'{row.get("Latin name", "animal")}: {profile.hediff} has no antivenom')
        return profile.sample

    def decorate(self, animals):
        required = ['Constrictor', 'Venomous', 'Venom sample producer', *CAPACITY_COLUMNS]
        missing = [key for key in required if key not in animals.columns]
        if missing: raise ValueError('Animals missing columns: ' + ', '.join(missing))
        result = animals.copy()
        samples = []
        for _, row in result.iterrows():
            boolean(row['Constrictor'], f'{row.get("Latin name", "animal")} Constrictor')
            venomous = boolean(row['Venomous'], f'{row.get("Latin name", "animal")} Venomous')
            capacities = set()
            for col in CAPACITY_COLUMNS: capacities.update(re.split(r'[,\s]+', str(row.get(col, ''))))
            unknown = sorted(c for c in capacities if c.startswith('ZVenom_') and c not in self.by_capacity)
            if unknown:
                raise ValueError(f'{row.get("Latin name", "animal")}: ToolCapacity missing from Venom profiles: {unknown}')
            if venomous != bool(capacities.intersection(self.by_capacity)):
                raise ValueError(f'{row.get("Latin name", "animal")}: Venomous does not match ToolCapacity')
            samples.append(self.sample_for(row))
        result['_ZoologyVenomSample'] = samples
        return result

    def render_item_defs(self):
        """Generate concrete Defs only; common behavior lives in static parents."""
        samples, drugs, recipes = E.Element('Defs'), E.Element('Defs'), E.Element('Defs')
        def add(parent, tag, text=None, **attributes):
            node = E.SubElement(parent, tag, **attributes)
            if text is not None: node.text = str(text)
            return node
        for profile in sorted(self.by_hediff.values(), key=lambda p: p.hediff):
            if not profile.has_antivenom: continue
            rgb = tuple(int(profile.color[i:i+2], 16) / 255 for i in (1, 3, 5))
            color = '(' + ','.join(f'{v:.6f}'.rstrip('0').rstrip('.') for v in rgb) + ')'
            sample = add(samples, 'ThingDef', ParentName='Zoology_AnimalVenomSampleBase')
            add(sample, 'defName', profile.sample)
            add(sample, 'label', profile.label.lower() + ' venom sample')
            add(add(sample, 'graphicData'), 'color', color)
            drug = add(drugs, 'ThingDef', ParentName='Zoology_AnimalAntivenomBase')
            add(drug, 'defName', profile.drug)
            add(drug, 'label', profile.label.lower() + ' antivenom')
            add(add(drug, 'graphicData'), 'color', color)
            outcome = add(add(add(drug, 'ingestible'), 'outcomeDoers'), 'li',
                          Class='ZoologyMod.IngestionOutcomeDoer_AnimalAntivenom, ZoologyMod')
            add(outcome, 'venom', profile.hediff)
            recipe = add(recipes, 'RecipeDef')
            add(recipe, 'defName', profile.recipe); add(recipe, 'label', 'make ' + profile.label.lower() + ' antivenom')
            add(recipe, 'description', 'Prepare antivenom from herbal medicine and a matching animal venom sample.')
            add(recipe, 'jobString', 'Preparing antivenom.'); add(recipe, 'workAmount', '1200')
            add(recipe, 'workSpeedStat', 'DrugSynthesisSpeed'); add(recipe, 'workSkill', 'Intellectual')
            add(recipe, 'researchPrerequisite', 'MedicineProduction'); add(add(recipe, 'recipeUsers'), 'li', 'DrugLab')
            ingredients = add(recipe, 'ingredients')
            for thing, count in [('MedicineHerbal', 2), (profile.sample, 1)]:
                entry = add(ingredients, 'li'); add(add(add(entry, 'filter'), 'thingDefs'), 'li', thing); add(entry, 'count', count)
            fixed = add(add(recipe, 'fixedIngredientFilter'), 'thingDefs')
            add(fixed, 'li', 'MedicineHerbal'); add(fixed, 'li', profile.sample)
            add(add(recipe, 'products'), profile.drug, 1)
        return {'ZVenom_Samples.xml': samples, 'ZVenom_AntivenomDrugs.xml': drugs, 'ZVenom_Recipes.xml': recipes}
