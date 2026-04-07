#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import os
import sys
import re
import json
import csv
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import pandas as pd
from lxml import etree as LET
from collections import defaultdict
import importlib.util
import traceback
import copy
from collections import OrderedDict

# ---------------- Config ----------------
CONFIG_FILE = "rimworld_patch_generator_config.json"
REPORT_DIRNAME = "generated_patches"

# ---------------- Utilities ----------------
def norm(s):
    if s is None:
        return ''
    return re.sub(r'\s+', ' ', str(s).strip()).lower()

def ensure_dir(p):
    d = os.path.dirname(p)
    if d:
        os.makedirs(d, exist_ok=True)

def load_config():
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
                return json.load(f)
        except Exception:
            return {}
    return {}

def save_config(cfg):
    try:
        with open(CONFIG_FILE, 'w', encoding='utf-8') as f:
            json.dump(cfg, f, indent=2)
    except Exception:
        pass

def try_parse_number(s):
    try:
        if s is None:
            return None
        s2 = str(s).strip()
        if s2.lower() in ('no', 'none', ''):
            return None
        if s2.endswith('%'):
            return float(s2[:-1]) / 100.0
        s2 = s2.replace(',', '.')
        return float(s2)
    except Exception:
        return None

def format_prob_value(s):
    """
    Нормализует вероятность в десятичную дробь без знака '%'.
    Примеры:
      "30%" -> "0.3"
      "30"  -> "0.3"
      "0.2" -> "0.2"
      "No"  -> вернёт исходную строк (если не распарсилось)
    """
    num = try_parse_number(s)
    if num is None:
        return str(s)
    # если parse вернул значение > 1, интерпретируем как проценты (30 -> 0.3)
    if num > 1:
        num = num / 100.0
    t = ('{:.6f}'.format(num)).rstrip('0').rstrip('.')
    if t == '':
        t = '0'
    return t

def is_excel_source(path):
    if not path:
        return False
    return os.path.splitext(str(path))[1].lower() in ('.xlsx', '.xlsm', '.xls')

# ---------------- Column aliases ----------------
COLUMN_ALIASES = {
    'MarketValue': ['MarketValue', 'Market value', 'market value', 'marketvalue'],
    'MoveSpeed': ['MoveSpeed', 'Move speed', 'move speed', 'movespeed'],
    'Wildness': ['Wildness', 'wildness'],
    'FilthRate': ['FilthRate', 'Filth rate', 'filth rate', 'filthrate'],
    'ComfyTemperatureMin': ['ComfyTemperatureMin', 'Comfy temperature min', 'comfytemperaturemin'],
    'ComfyTemperatureMax': ['ComfyTemperatureMax', 'Comfy temperature max', 'comfytemperaturemax'],
    'ArmorRating_Blunt': ['ArmorRating_Blunt', 'ArmorRating Blunt', 'ArmorRating_Blunt'],
    'ArmorRating_Sharp': ['ArmorRating_Sharp', 'ArmorRating Sharp', 'ArmorRating_Sharp'],
    'ToxicEnvironmentResistance': ['ToxicEnvironmentResistance', 'Toxic Environment Resistance', 'ToxicEnvironmentResistance'],
    'baseBodySize': ['baseBodySize', 'Base body size', 'BaseBodySize', 'basebodysize'],
    'baseHealthScale': ['baseHealthScale', 'Base health scale', 'basehealthscale'],
    'baseHungerRate': ['baseHungerRate', 'Base hunger rate', 'basehungerrate'],
    'lifeExpectancy': ['Lifespan (years)', 'lifeExpectancy', 'life expectancy', 'Lifespan'],
    'gestationPeriodDays': ['Gestation period (days)', 'gestationPeriodDays', 'Gestation period'],
    'herdAnimal': ['herdAnimal', 'HerdAnimal', 'herd animal'],
    'herdMigrationAllowed': ['herdMigrationAllowed', 'herd migration allowed', 'herdMigrationAllowed'],
    'Foodtype': ['Foodtype', 'Food type', 'foodType', 'food type'],
    'manhunterOnTameFailChance': ['manhunterOnTameFailChance', 'manhunterOnTameFailChance'],
    'manhunterOnDamageChance': ['manhunterOnDamageChance', 'manhunterOnDamageChance'],
    'petness': ['petness', 'Petness'],
    'nuzzleMtbHours': ['nuzzleMtbHours', 'NuzzleMtbHours'],
    'mateMtbHours': ['mateMtbHours', 'MateMtbHours'],
    'trainability': ['trainability', 'Trainability'],
    'predator': ['predator', 'Predator'],
    'maxPreyBodySize': ['maxPreyBodySize', 'MaxPreyBodySize'],
    'nameOnTameChance': ['nameOnTameChance', 'NameOnTameChance'],
    'Body': ['Body', 'BodyDef'],
    'Juv age (years)': ['Juv age (years)', 'Juv age', 'JuvAge', 'Juvenile age'],
    'Adult age (years)': ['Adult age (years)', 'Adult age', 'AdultAge'],
    'Litter size': ['Litter size', 'LitterSize', 'Litter size (avg)'],
    'TradeTags': ['TradeTags', 'Trade Tags', 'tradeTags', 'trade tags'],
    'specialTrainables': ['specialTrainables', 'special Trainables', 'Special Trainables', 'SpecialTrainables', 'specialtrainables', 'special trainables'],
    'Combat power': ['Combat power', 'combatPower', 'CombatPower'],
    'ecoSystemWeight': ['ecoSystemWeight', 'Eco system weight', 'EcoSystemWeight'],
    'WildBiomes': ['WildBiomes', 'Wild biomes', 'Wild Biomes', 'Biomes', 'biomes'],
    'Costal': ['Costal', 'Coastal', 'costal', 'coastal'],
    'PackAnimal': ['PackAnimal', 'Pack animal', 'packAnimal', 'Pack Animal'],
    'Eco system number': ['Eco system number', 'EcoSystemNumber', 'Eco system Number', 'ecosystem number'],
    'Toxic eco system number': ['Toxic eco system number', 'Toxic Eco system number', 'Toxic ecosystem number', 'ToxicEcoSystemNumber'],
    'MayRequire': ['MayRequire', 'May Require', 'mayrequire', 'May require'],
    'Wild group size': ['Wild group size', 'wildGroupSize', 'wild group size'],
    'CanArriveManhunter': ['CanArriveManhunter', 'canArriveManhunter', 'can arrive manhunter'],
    'Head damage': ['Head damage', 'HeadDamage', 'head damage'],
    'Bite damage': ['Bite damage', 'BiteDamage', 'bite damage'],
    'Paw claw/punch damage': ['Paw claw/punch damage', 'Paw damage', 'paw damage'],
    'Poke/leg claws damage': ['Poke/leg claws damage', 'Leg damage', 'leg damage'],
    'Horn/Antler/Tusks damage': ['Horn/Antler/Tusks damage', 'Horn damage'],
    # LeatherDef alias (added to ensure Leather handling)
    'LeatherDef': ['Leather def', 'LeatherDef', 'Leather_Def', 'Leather'],
    'ModConflict': ['ModConflict', 'Mod Conflict', 'modConflict', 'Mod conflict'],
}

def find_alias_in_row(row, candidates):
    for c in candidates:
        if c in row.index:
            return c
        for idx in row.index:
            if idx.strip().lower() == str(c).strip().lower():
                return idx
    return None

def get_row_value(row, canonical_key):
    aliases = COLUMN_ALIASES.get(canonical_key, [canonical_key])
    alias = find_alias_in_row(row, aliases)
    if alias:
        return row.get(alias, '')
    return row.get(canonical_key, '')

# ---------------- Load Checkers Dynamically ----------------
def load_checker_module(file_path):
    spec = importlib.util.spec_from_file_location(os.path.basename(file_path).replace('.py', ''), file_path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

# ---------------- Generate Litter Size Curve ----------------
def generate_litter_curve(mean_litter):
    if mean_litter is None or mean_litter <= 1:
        return None
    template_points = [(0.5, 0), (1, 0.2), (2, 1), (3, 1), (4, 0.2), (4.5, 0)]
    original_mean = 2.5
    scale = mean_litter / original_mean
    points = [(x * scale, y) for x, y in template_points]
    return points

# ---------------- Patch Generation Logic ----------------
class PatchGenerator:
    ODYSSEY_BIOMES = {'Grasslands', 'Glowforest', 'LavaField', 'GlacialPlain', 'Scarlands'}
    ODYSSEY_MAYREQUIRE = 'Ludeon.RimWorld.Odyssey'

    def __init__(self, vanilla_source, ce_source, xml_paths):
        self.vanilla_df = self._load_table(vanilla_source, sheet_name='Animals', required=True) if vanilla_source else None
        if ce_source:
            ce_sheet = 'Animals CE' if is_excel_source(ce_source) else None
            self.ce_df = self._load_table(ce_source, sheet_name=ce_sheet, required=True)
        elif vanilla_source and is_excel_source(vanilla_source):
            # Convenience mode: one AnimalStats.xlsx path provides both sheets.
            self.ce_df = self._load_table(vanilla_source, sheet_name='Animals CE', required=False)
        else:
            self.ce_df = None
        self.xml_paths = xml_paths

    def _load_table(self, source_path, sheet_name=None, required=True):
        def _normalize_df(df):
            num_re = re.compile(r'^[+-]?\d+(?:\.\d+)?$')

            def _cell_to_text(v):
                if v is None:
                    return ''
                try:
                    if pd.isna(v):
                        return ''
                except Exception:
                    pass
                if isinstance(v, float):
                    return ('{:.15g}'.format(v)).rstrip()
                if isinstance(v, int):
                    return str(v)
                s = str(v).strip()
                if num_re.match(s):
                    try:
                        if '.' in s:
                            return ('{:.6f}'.format(float(s))).rstrip('0').rstrip('.')
                        return str(int(float(s)))
                    except Exception:
                        return s
                return s

            out = df.apply(lambda col: col.map(_cell_to_text))
            return out.fillna('')

        if not source_path:
            if required:
                raise RuntimeError("Table source path is empty.")
            return None
        if not os.path.exists(source_path):
            if required:
                raise RuntimeError(f"Table source not found: {source_path}")
            return None

        if is_excel_source(source_path):
            try:
                read_kwargs = {'dtype': str, 'keep_default_na': False}
                if sheet_name:
                    read_kwargs['sheet_name'] = sheet_name
                return _normalize_df(pd.read_excel(source_path, **read_kwargs))
            except ValueError as e:
                if sheet_name and required:
                    raise RuntimeError(
                        f"Sheet '{sheet_name}' not found in workbook: {source_path}"
                    ) from e
                if required:
                    raise
                return None
            except ImportError:
                try:
                    read_kwargs = {'dtype': str, 'keep_default_na': False, 'engine': 'calamine'}
                    if sheet_name:
                        read_kwargs['sheet_name'] = sheet_name
                    return _normalize_df(pd.read_excel(source_path, **read_kwargs))
                except Exception as e:
                    raise RuntimeError(
                        "Excel support requires openpyxl (or python-calamine). "
                        "Install with: pip install openpyxl"
                    ) from e

        return _normalize_df(pd.read_csv(source_path, sep='\t', dtype=str, keep_default_na=False))

    def get_patched_names(self, root):
        patched_defnames = set()
        patched_abstracts = set()
        for xpath_el in root.xpath(".//xpath"):
            if not xpath_el.text:
                continue
            text = xpath_el.text
            matches = re.findall(r'ThingDef\s*\[\s*(defName|@Name)\s*=\s*"([^"]+)"\s*\]', text)
            for attr, name in matches:
                if attr == "defName":
                    patched_defnames.add(name)
                else:
                    patched_abstracts.add(name)
        return patched_defnames, patched_abstracts

    # =========================
    # Build tools from tables (TSV or Excel sheets)
    # =========================

    def _split_and_strip(self, s):
        """Split "A, B" -> ['A','B'] with stripping; if s is already list return as-is."""
        if s is None:
            return []
        if isinstance(s, (list, tuple)):
            return [str(x).strip() for x in s if str(x).strip()]
        ss = str(s).strip()
        if ss == '' or ss.lower() in ('no', 'none'):
            return []
        return [part.strip() for part in re.split(r',\s*', ss) if part.strip()]

    def _split_biomes(self, s):
        """Split biome list by comma and/or whitespace, keep order, drop duplicates."""
        if s is None:
            return []
        ss = str(s).strip()
        if ss == '' or ss.lower() in ('no', 'none'):
            return []
        parts = [p.strip() for p in re.split(r'[\s,]+', ss) if p.strip()]
        out = []
        seen = set()
        for p in parts:
            if p in seen:
                continue
            seen.add(p)
            out.append(p)
        return out

    def _split_mod_list(self, s):
        """Split mod list by comma/semicolon/newline, keep order and drop duplicates."""
        if s is None:
            return []
        if isinstance(s, (list, tuple, set)):
            out = []
            seen = set()
            for item in s:
                p = str(item).strip()
                if not p or p.lower() in ('no', 'none'):
                    continue
                key = p.lower()
                if key in seen:
                    continue
                seen.add(key)
                out.append(p)
            return out
        ss = str(s).strip()
        if ss == '' or ss.lower() in ('no', 'none'):
            return []
        out = []
        seen = set()
        for part in re.split(r'[,\n;]+', ss):
            p = part.strip()
            if not p:
                continue
            key = p.lower()
            if key in seen:
                continue
            seen.add(key)
            out.append(p)
        return out

    def _build_wild_biomes_element(self, biome_names, eco_system_number):
        """
        Build <wildBiomes> node from list of biome names and one eco weight.
        Odyssey biomes get MayRequire="Ludeon.RimWorld.Odyssey".
        """
        if not biome_names:
            return None
        eco_text = '' if eco_system_number is None else str(eco_system_number).strip()
        if eco_text == '' or eco_text.lower() in ('no', 'none'):
            return None

        node = LET.Element('wildBiomes')
        for biome in biome_names:
            biome_name = str(biome).strip()
            if not biome_name:
                continue
            entry = LET.SubElement(node, biome_name)
            entry.text = eco_text
            if biome_name in self.ODYSSEY_BIOMES:
                entry.set('MayRequire', self.ODYSSEY_MAYREQUIRE)
        return node if len(node) > 0 else None

    def _wrap_ops_in_mod_conflict(self, ops, conflict_mods, as_li=False):
        """
        Wrap operations into PatchOperationFindMod with nomatch PatchOperationSequence
        so payload runs only when conflicting mod is NOT found.
        """
        payload = list(ops or [])
        mods = self._split_mod_list(conflict_mods)
        has_real_ops = any(hasattr(op, 'tag') and isinstance(op.tag, str) for op in payload)
        if not mods or not payload or not has_real_ops:
            return payload

        wrapper_tag = 'li' if as_li else 'Operation'
        wrapper = LET.Element(wrapper_tag, Class='PatchOperationFindMod')
        mods_el = LET.SubElement(wrapper, 'mods')
        for mod_name in mods:
            LET.SubElement(mods_el, 'li').text = mod_name

        nomatch = LET.SubElement(wrapper, 'nomatch', Class='PatchOperationSequence')
        seq_ops = LET.SubElement(nomatch, 'operations')

        def _to_sequence_item(node):
            if not isinstance(getattr(node, 'tag', None), str):
                return copy.deepcopy(node)
            if node.tag == 'li':
                return copy.deepcopy(node)
            if node.tag == 'Operation':
                src = copy.deepcopy(node)
                li = LET.Element('li')
                for k, v in src.attrib.items():
                    li.set(k, v)
                li.text = src.text
                for child in src:
                    li.append(copy.deepcopy(child))
                li.tail = src.tail
                return li
            return copy.deepcopy(node)

        for op in payload:
            seq_ops.append(_to_sequence_item(op))
        return [wrapper]

    def _is_no_like(self, v):
        if v is None:
            return True
        s = str(v).strip().lower()
        return s in ('', 'no', 'none')

    def _is_truthy(self, v):
        if v is None:
            return False
        return str(v).strip().lower() in ('true', '1', 'yes', 'y', 't')

    def _extract_def_name_from_xml_name(self, xml_name_raw):
        if xml_name_raw is None:
            return ''
        xml_name = str(xml_name_raw).strip()
        m = re.search(r'<li[^>]*>(.*?)</li>', xml_name)
        if m:
            return m.group(1).strip()
        return xml_name.strip()

    def _safe_biome_filename(self, biome_name):
        safe = re.sub(r'[^A-Za-z0-9._-]+', '_', str(biome_name).strip())
        safe = safe.strip('_')
        return safe or 'UnnamedBiome'

    def _filter_may_require_for_biome(self, biome_name, may_require):
        if not may_require:
            return may_require
        if biome_name in self.ODYSSEY_BIOMES and str(may_require).strip().lower() == self.ODYSSEY_MAYREQUIRE.lower():
            return None
        return may_require

    def create_biome_list_operation(self, biome_name, container_tag, entries):
        """Create one PatchOperationAdd that appends animal entries to an existing biome list container."""
        op = LET.Element("Operation", Class="PatchOperationAdd")
        LET.SubElement(op, "xpath").text = f'/Defs/BiomeDef[defName="{biome_name}"]/{container_tag}'
        value = LET.SubElement(op, "value")
        for animal_def, weight, may_require in entries:
            animal = LET.SubElement(value, animal_def)
            animal.text = str(weight).strip()
            if may_require:
                animal.set('MayRequire', may_require)
        return op

    def create_biome_pack_animals_operation(self, biome_name, entries):
        """Create one PatchOperationAdd for /allowedPackAnimals with <li> entries."""
        op = LET.Element("Operation", Class="PatchOperationAdd")
        LET.SubElement(op, "xpath").text = f'/Defs/BiomeDef[defName="{biome_name}"]/allowedPackAnimals'
        value = LET.SubElement(op, "value")
        for animal_def, may_require in entries:
            li = LET.SubElement(value, "li")
            li.text = str(animal_def).strip()
            if may_require:
                li.set('MayRequire', may_require)
        return op

    def create_biome_pack_animals_safe_ops(self, biome_name, entries):
        """
        Create safe operations that add one pack animal only if it's not already present.
        """
        ops = []
        for animal_def, may_require in entries:
            animal_name = str(animal_def).strip()
            if not animal_name:
                continue
            check_xpath = f'/Defs/BiomeDef[defName="{biome_name}"]/allowedPackAnimals/li[text()="{animal_name}"]'
            op = LET.Element("Operation", Class="PatchOperationConditional")
            LET.SubElement(op, "xpath").text = check_xpath
            nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
            LET.SubElement(nomatch, "xpath").text = f'/Defs/BiomeDef[defName="{biome_name}"]/allowedPackAnimals'
            value = LET.SubElement(nomatch, "value")
            li = LET.SubElement(value, "li")
            li.text = animal_name
            if may_require:
                li.set('MayRequire', may_require)
            ops.append(op)
        return ops

    def create_biome_container_reset_ops(self, biome_name, container_tag):
        """
        Create two operations:
        1) Conditional remove of existing container.
        2) Add empty container under biome.
        """
        remove_op = LET.Element("Operation", Class="PatchOperationConditional")
        remove_xpath = f'/Defs/BiomeDef[defName="{biome_name}"]/{container_tag}'
        LET.SubElement(remove_op, "xpath").text = remove_xpath
        match = LET.SubElement(remove_op, "match", Class="PatchOperationRemove")
        LET.SubElement(match, "xpath").text = remove_xpath

        add_op = LET.Element("Operation", Class="PatchOperationAdd")
        LET.SubElement(add_op, "xpath").text = f'/Defs/BiomeDef[defName="{biome_name}"]'
        value = LET.SubElement(add_op, "value")
        LET.SubElement(value, container_tag)

        return [remove_op, add_op]

    def create_biome_container_ensure_op(self, biome_name, container_tag):
        """Create one conditional op that ensures biome container exists."""
        op = LET.Element("Operation", Class="PatchOperationConditional")
        LET.SubElement(op, "xpath").text = f'/Defs/BiomeDef[defName="{biome_name}"]/{container_tag}'
        nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
        LET.SubElement(nomatch, "xpath").text = f'/Defs/BiomeDef[defName="{biome_name}"]'
        value = LET.SubElement(nomatch, "value")
        LET.SubElement(value, container_tag)
        return op

    def generate_biome_patch_files(self, output_dir):
        """
        Build standalone biome patch files from vanilla table.
        Produces one file per biome: Biomes_<BiomeName>.xml
        Each file contains 3 operations for wildAnimals, pollutionWildAnimals, coastalWildAnimals.
        """
        if self.vanilla_df is None:
            raise RuntimeError("Vanilla table not provided")

        biomes_data = defaultdict(lambda: {
            'wildAnimals': OrderedDict(),
            'coastalWildAnimals': OrderedDict(),
            'pollutionWildAnimals': OrderedDict(),
            'allowedPackAnimals': OrderedDict(),
        })

        def _put_pack_animal(biome_name, animal_def, may_require):
            # keep single entry per animal per biome; if existing has no MayRequire and new has it -> upgrade
            existing = biomes_data[biome_name]['allowedPackAnimals'].get(animal_def)
            if existing is None:
                biomes_data[biome_name]['allowedPackAnimals'][animal_def] = may_require
            elif (not existing) and may_require:
                biomes_data[biome_name]['allowedPackAnimals'][animal_def] = may_require

        horse_may_require = None
        horse_seen = False
        muffalo_may_require = None
        muffalo_seen = False

        for _, row in self.vanilla_df.iterrows():
            def_name = self._extract_def_name_from_xml_name(
                row.get('XML name', '') if 'XML name' in row.index else (row.iloc[0] if len(row.index) > 0 else '')
            )
            if not def_name:
                continue

            # Animals with ModConflict are handled in ThingDef animal patches, not in biome patch files.
            if self._split_mod_list(get_row_value(row, 'ModConflict')):
                continue

            wild_biomes = self._split_biomes(get_row_value(row, 'WildBiomes'))
            if not wild_biomes:
                continue

            eco_number = get_row_value(row, 'Eco system number')
            toxic_eco_number = get_row_value(row, 'Toxic eco system number')
            is_coastal = self._is_truthy(get_row_value(row, 'Costal'))
            is_pack_animal = self._is_truthy(get_row_value(row, 'PackAnimal'))
            may_require_raw = get_row_value(row, 'MayRequire')
            may_require = None if self._is_no_like(may_require_raw) else str(may_require_raw).strip()

            for biome in wild_biomes:
                if not self._is_no_like(eco_number):
                    target = 'coastalWildAnimals' if is_coastal else 'wildAnimals'
                    biomes_data[biome][target][def_name] = (str(eco_number).strip(), may_require)
                if not self._is_no_like(toxic_eco_number):
                    biomes_data[biome]['pollutionWildAnimals'][def_name] = (str(toxic_eco_number).strip(), may_require)
                if is_pack_animal:
                    _put_pack_animal(biome, def_name, may_require)

            # Special rules:
            # Horse -> allowedPackAnimals in all biomes.
            if def_name == 'Horse':
                horse_seen = True
                if may_require and not horse_may_require:
                    horse_may_require = may_require
            # Muffalo -> allowedPackAnimals in SeaIce and IceSheet.
            if def_name == 'Muffalo':
                muffalo_seen = True
                if may_require and not muffalo_may_require:
                    muffalo_may_require = may_require

        all_biomes = set(biomes_data.keys())
        if muffalo_seen:
            all_biomes.update(['SeaIce', 'IceSheet'])
            _put_pack_animal('SeaIce', 'Muffalo', muffalo_may_require)
            _put_pack_animal('IceSheet', 'Muffalo', muffalo_may_require)
        if horse_seen:
            for biome_name in list(all_biomes):
                _put_pack_animal(biome_name, 'Horse', horse_may_require)

        os.makedirs(output_dir, exist_ok=True)
        created_files = []
        for biome_name in sorted(all_biomes):
            root = LET.Element("Patch")

            wild_entries = [(k, v[0], self._filter_may_require_for_biome(biome_name, v[1])) for k, v in biomes_data[biome_name]['wildAnimals'].items()]
            pollution_entries = [(k, v[0], self._filter_may_require_for_biome(biome_name, v[1])) for k, v in biomes_data[biome_name]['pollutionWildAnimals'].items()]
            coastal_entries = [(k, v[0], self._filter_may_require_for_biome(biome_name, v[1])) for k, v in biomes_data[biome_name]['coastalWildAnimals'].items()]
            pack_entries = [(k, self._filter_may_require_for_biome(biome_name, v)) for k, v in biomes_data[biome_name]['allowedPackAnimals'].items()]

            for container_tag in ('wildAnimals', 'pollutionWildAnimals', 'coastalWildAnimals'):
                for op in self.create_biome_container_reset_ops(biome_name, container_tag):
                    root.append(op)
            root.append(self.create_biome_container_ensure_op(biome_name, 'allowedPackAnimals'))

            root.append(self.create_biome_list_operation(biome_name, 'wildAnimals', wild_entries))
            root.append(self.create_biome_list_operation(biome_name, 'pollutionWildAnimals', pollution_entries))
            root.append(self.create_biome_list_operation(biome_name, 'coastalWildAnimals', coastal_entries))
            for op in self.create_biome_pack_animals_safe_ops(biome_name, pack_entries):
                root.append(op)

            filename = f"Biomes_{self._safe_biome_filename(biome_name)}.xml"
            out_path = os.path.join(output_dir, filename)
            with open(out_path, 'wb') as f:
                f.write(LET.tostring(root, pretty_print=True, encoding='utf-8', xml_declaration=True))
            created_files.append(out_path)

        return created_files

    def _infer_label_noloc(self, label):
        """Infer labelNoLocation from a label like 'left claw' -> 'claw'.
           If label contains only one word, return None (no labelNoLocation)."""
        if not label:
            return None
        # consider only first token if it's 'left' or 'right'
        lab = str(label).strip()
        parts = lab.split()
        if len(parts) >= 2 and parts[0].lower() in ('left', 'right'):
            # return the last word or rest joined
            return ' '.join(parts[1:]).strip()
        # also handle patterns like 'left rear claw' -> 'rear claw' -> we return last two tokens
        # fallback: if label contains space, return last token
        if len(parts) > 1:
            return parts[-1]
        return None

    def _make_capacities_element(self, capacities_str):
        """Return <capacities> element with <li> children or None if empty."""
        caps = self._split_and_strip(capacities_str)
        if not caps:
            return None
        caps_el = LET.Element('capacities')
        for c in caps:
            li = LET.SubElement(caps_el, 'li')
            li.text = c
        return caps_el

    def _make_tool_li(self, label=None, labelNoLocation=None, capacities=None,
                      power=None, cooldown=None, linked=None,
                      ensure_always=False, chance_factor=None,
                      restricted_gender=None,
                      class_attr=None,
                      ap_blunt=None, ap_sharp=None,
                      stun=None):
        """Create and return a <li> element for a tool with given parameters.
        If stun is provided (and label != 'head') add a <surpriseAttack> block with Stun damage.
        """
        li = LET.Element('li')

        # If caller explicitly passed class_attr (e.g. CE), preserve it.
        if class_attr:
            li.set('Class', class_attr)

        if label is not None:
            el = LET.SubElement(li, 'label')
            el.text = str(label)

        if labelNoLocation is not None:
            el2 = LET.SubElement(li, 'labelNoLocation')
            el2.text = str(labelNoLocation)

        if capacities is not None:
            caps_el = self._make_capacities_element(capacities)
            if caps_el is not None:
                li.append(caps_el)

        if power is not None:
            p_el = LET.SubElement(li, 'power')
            p_el.text = str(power)

        if ap_blunt is not None:
            apb = LET.SubElement(li, 'armorPenetrationBlunt')
            apb.text = str(ap_blunt)
        if ap_sharp is not None:
            aps = LET.SubElement(li, 'armorPenetrationSharp')
            aps.text = str(ap_sharp)

        if cooldown is not None:
            cd = LET.SubElement(li, 'cooldownTime')
            cd.text = str(cooldown)

        if linked is not None:
            lb = LET.SubElement(li, 'linkedBodyPartsGroup')
            lb.text = str(linked)

        if ensure_always:
            ea = LET.SubElement(li, 'ensureLinkedBodyPartsGroupAlwaysUsable')
            ea.text = 'true'

        if chance_factor is not None:
            cf = LET.SubElement(li, 'chanceFactor')
            cf.text = str(chance_factor)

        # Add restrictedGender element if requested.
        if restricted_gender is not None:
            rg = LET.SubElement(li, 'restrictedGender')
            rg.text = str(restricted_gender)

            # If there was no Class explicitly provided, mark this li so vanilla will
            # deserialize it into our ToolWithGender class.
            if li.get('Class') is None:
                li.set('Class', 'ZoologyMod.ToolWithGender, ZoologyMod')

        # --- surpriseAttack for stun (except for head) ---
        if stun is not None:
            s = str(stun).strip()
            if s and s.lower() not in ('', 'no', 'none'):
                # do not add surpriseAttack to head attack
                lab = (str(label).strip().lower() if label is not None else '')
                if lab != 'head':
                    sa = LET.SubElement(li, 'surpriseAttack')
                    emd = LET.SubElement(sa, 'extraMeleeDamages')
                    li_damage = LET.SubElement(emd, 'li')
                    def_el = LET.SubElement(li_damage, 'def')
                    def_el.text = 'Stun'
                    amt = LET.SubElement(li_damage, 'amount')
                    amt.text = s
        # --- end new ---

        return li

    def build_tools_vanilla(self, row, is_abstract=False):
        """
        Build <tools> Element for vanilla patch from TSV row (pandas Series).
        Returns <tools> Element or None if no tools should be emitted.
        """
        tools = LET.Element('tools')
        # Stun value from TSV (applies to non-head attacks)
        stun_val = get_row_value(row, 'Stun')

        # HEAD: always one
        head_power = get_row_value(row, 'Head damage')
        head_interval = row.get('Head attack interval') if 'Head attack interval' in row.index else None
        if head_power is not None and str(head_power).strip().lower() not in ('', 'no', 'none'):
            head_li = self._make_tool_li(
                label='head',
                capacities='Blunt',
                power=head_power,
                cooldown=head_interval,
                linked='HeadAttackTool',
                ensure_always=True,
                chance_factor=0.2,
                stun=stun_val
            )
            tools.append(head_li)

        # Helper to generate possibly paired tools (labels/linkedBodyPartsGroup may be comma-separated)
        def _add_paired(col_prefix, label_col, linked_col, capacities_col, ce_flag=False, row_local=row):
            """
            Robust paired tool builder for vanilla:
            - finds cooldown column resiliently
            - supports single or comma-separated cooldowns
            - uses get_row_value for power lookup (aliases)
            """
            labels = self._split_and_strip(row_local.get(label_col))
            linkeds = self._split_and_strip(row_local.get(linked_col))
            caps = row_local.get(capacities_col) if capacities_col in row_local.index else None

            # if nothing to place -> skip
            if not labels and not linkeds:
                return

            # --- discover cooldown raw value robustly ---
            def _find_cd_raw(r):
                # try exact: "<col_prefix> attack interval"
                cand_names = []
                cand_names.append(f"{col_prefix} attack interval")
                # also try dropping word "damage" if present in prefix
                short = re.sub(r'\bdamage\b', '', col_prefix, flags=re.I).strip()
                if short and short != col_prefix:
                    cand_names.append(f"{short} attack interval")
                # try exact-case-insensitive match via find_alias_in_row
                for n in cand_names:
                    alias = find_alias_in_row(r, [n])
                    if alias:
                        return r.get(alias)
                # fallback: search any column that contains both words "attack" and "interval" and shares a token with prefix
                pref_tokens = [t for t in re.findall(r'\w+', col_prefix.lower()) if t not in ('damage',)]
                for c in r.index:
                    try:
                        low = str(c).lower()
                    except Exception:
                        continue
                    if 'attack' in low and 'interval' in low:
                        # require at least one non-generic token from prefix to appear (paw, claw, punch, leg, etc.)
                        good = False
                        for t in pref_tokens:
                            if t and t in low:
                                good = True
                                break
                        if good:
                            return r.get(c)
                return None

            cd_raw = _find_cd_raw(row_local)
            cd_vals = self._split_and_strip(cd_raw)

            # number of outputs
            n = max(len(labels), len(linkeds), 1)
            if not labels:
                labels = [''] * n
            if not linkeds:
                linkeds = [''] * n
            while len(labels) < n:
                labels.append(labels[-1])
            while len(linkeds) < n:
                linkeds.append(linkeds[-1])

            # power lookup via get_row_value (handles aliases)
            power_val = get_row_value(row_local, col_prefix)

            for i in range(n):
                lab = labels[i]
                link = linkeds[i] if linkeds[i] != '' else None
                labnoloc = self._infer_label_noloc(lab)
                # choose cooldown for this index (if multiple provided, pick matching index; else last; else None)
                cd = None
                if cd_vals:
                    if i < len(cd_vals):
                        cd = cd_vals[i]
                    else:
                        cd = cd_vals[-1]

                li = self._make_tool_li(
                    label=lab or None,
                    labelNoLocation=labnoloc,
                    capacities=caps,
                    power=power_val,
                    cooldown=cd,
                    linked=link,
                    stun=stun_val
                )
                tools.append(li)

        # Poke/leg claws (bird-specific, may be called "Poke/leg claws damage" in TSV)
        poke_damage = get_row_value(row, 'Poke/leg claws damage')
        if poke_damage is not None and str(poke_damage).strip().lower() not in ('', 'no', 'none'):
            # label/linked/capacities columns names expected in vanilla TSV:
            _add_paired('Poke/leg claws damage', 'Poke/leg claws label', 'Poke/leg claws linkedBodyPartsGroup', 'Poke/leg claws capacities')

        # Paw claw/punch (paired)
        paw_damage = get_row_value(row, 'Paw claw/punch damage')
        if paw_damage is not None and str(paw_damage).strip().lower() not in ('', 'no', 'none'):
            _add_paired('Paw claw/punch damage', 'Paw claw/punch label', 'Paw claw/punch linkedBodyPartsGroup', 'Paw claw/punch capacities')

        # Horn / Antler / Tusks (single)
        horn_damage = get_row_value(row, 'Horn/Antler/Tusks damage')
        if horn_damage is not None and str(horn_damage).strip().lower() not in ('', 'no', 'none'):
            lab = row.get('Horn/Antler/Tusks label') or None
            caps = row.get('Horn/Antler/Tusks capacities') if 'Horn/Antler/Tusks capacities' in row.index else None
            link = row.get('Horn/Antler/Tusks linkedBodyPartsGroup') or None
            male_only = row.get('Horn/Antler/Tusks Male only')
            li = self._make_tool_li(
                label=lab,
                capacities=caps,
                power=horn_damage,
                cooldown=row.get('Horn/Antler/Tusks attack interval'),
                linked=link,
                restricted_gender='Male' if (str(male_only).strip().lower() in ('true', '1', 'yes', 'y', 't')) else None,
                stun=stun_val
            )
            tools.append(li)

        # Bite (single)
        bite_damage = get_row_value(row, 'Bite damage')
        if bite_damage is not None and str(bite_damage).strip().lower() not in ('', 'no', 'none'):
            lab = row.get('Bite label') or row.get('Bite') or None
            caps = row.get('Bite capacities') if 'Bite capacities' in row.index else None
            link = row.get('Bite linkedBodyPartsGroup') or row.get('Bite linkedBodyPartsGroup') or None
            li = self._make_tool_li(
                label=lab,
                capacities=caps,
                power=bite_damage,
                cooldown=row.get('Bite attack interval'),
                linked=link,
                stun=stun_val
            )
            tools.append(li)

        # If no children in tools -> return None (so caller can decide remove vs skip)
        if len(list(tools)) == 0:
            return None
        return tools

    def build_tools_ce(self, def_name, vanilla_row=None):
        """
        Build <tools> Element for Combat Extended patch.
        def_name: string name (XML defName) - used to lookup CE data in self.ce_df.
        vanilla_row: optionally supply vanilla row to reuse label/linked/capacity columns.
        Returns <tools> Element or None.
        """
        # find CE row by XML name. We assume self.ce_df exists with a column 'XML name'
        ce_row = None
        if getattr(self, 'ce_df', None) is not None:
            # try to find row where 'XML name' contains def_name or exact match
            for _, r in self.ce_df.iterrows():
                xml_name = r.get('XML name', '')
                if xml_name is None:
                    continue
                txt = str(xml_name)
                if def_name in txt or f'<li>{def_name}</li>' in txt:
                    ce_row = r
                    break

        # If no CE row -> nothing to emit
        if ce_row is None:
            return None
            
        # For labels/linked/capacities we prefer vanilla_row if provided (since CE TSV may not repeat those)
        row_for_labels = vanilla_row if vanilla_row is not None else ce_row

        # Stun: prefer CE TSV but fall back to vanilla TSV (sometimes Stun only present in vanilla)
        stun_val = get_row_value(ce_row, 'Stun') if ce_row is not None else None
        if (stun_val is None or str(stun_val).strip().lower() in ('', 'no', 'none')) and row_for_labels is not None:
            stun_val = get_row_value(row_for_labels, 'Stun')

        tools = LET.Element('tools')

        # HEAD CE
        head_power = ce_row.get('Head damage')
        head_ap = ce_row.get('Head Blunt AP') if 'Head Blunt AP' in ce_row.index else ce_row.get('Head Blunt AP')
        head_interval = ce_row.get('Head attack interval')
        if head_power is not None and str(head_power).strip().lower() not in ('', 'no', 'none'):
            li = self._make_tool_li(
                label='head',
                capacities='Blunt',
                power=head_power,
                cooldown=head_interval,
                linked='HeadAttackTool',
                ensure_always=True,
                chance_factor=0.2,
                class_attr='CombatExtended.ToolCE',
                ap_blunt=head_ap if head_ap is not None and str(head_ap).strip().lower() not in ('', 'no', 'none') else None,
                stun=stun_val
            )
            tools.append(li)

        # Helper to add CE paired tools, similar logic to vanilla but using CE AP columns
        def _add_paired_ce(prefix, label_col, linked_col, capacities_col, row_ce=ce_row, row_v=row_for_labels):
            """
            Robust paired CE tool builder:
            - finds CE cooldown column flexibly
            - supports single or comma-separated cooldowns
            - uses row_for_labels for labels/linked/capacities and get_row_value for damage if needed
            """
            # damage: try direct CE column, else alias lookup
            dmg = row_ce.get(f'{prefix} damage') if f'{prefix} damage' in row_ce.index else get_row_value(row_ce, f'{prefix} damage')
            if dmg is None or str(dmg).strip().lower() in ('', 'no', 'none'):
                return

            labels = self._split_and_strip(row_v.get(label_col))
            linkeds = self._split_and_strip(row_v.get(linked_col))
            caps = row_v.get(capacities_col) if capacities_col in row_v.index else None

            # find cooldown raw: similar logic to vanilla version but prefer CE row first
            def _find_cd_raw_ce(r_ce, r_v):
                cand_names = []
                cand_names.append(f"{prefix} attack interval")
                short = re.sub(r'\bdamage\b', '', prefix, flags=re.I).strip()
                if short and short != prefix:
                    cand_names.append(f"{short} attack interval")
                # check CE row columns first
                for n in cand_names:
                    if n in r_ce.index:
                        return r_ce.get(n)
                    alias = find_alias_in_row(r_ce, [n])
                    if alias:
                        return r_ce.get(alias)
                # fallback to CE row case-insensitive search for "attack interval" and token match
                pref_tokens = [t for t in re.findall(r'\w+', prefix.lower()) if t not in ('damage',)]
                for c in r_ce.index:
                    try:
                        low = str(c).lower()
                    except Exception:
                        continue
                    if 'attack' in low and 'interval' in low:
                        for t in pref_tokens:
                            if t and t in low:
                                return r_ce.get(c)
                # last resort: maybe interval put in vanilla row_for_labels
                for n in cand_names:
                    alias = find_alias_in_row(r_v, [n])
                    if alias:
                        return r_v.get(alias)
                for c in r_v.index:
                    try:
                        low = str(c).lower()
                    except Exception:
                        continue
                    if 'attack' in low and 'interval' in low:
                        for t in pref_tokens:
                            if t and t in low:
                                return r_v.get(c)
                return None

            cd_raw = _find_cd_raw_ce(row_ce, row_v)
            cd_vals = self._split_and_strip(cd_raw)

            n = max(len(labels), len(linkeds), 1)
            if not labels:
                labels = [''] * n
            if not linkeds:
                linkeds = [''] * n
            while len(labels) < n:
                labels.append(labels[-1])
            while len(linkeds) < n:
                linkeds.append(linkeds[-1])

            # ap columns
            apb_col = f'{prefix} Blunt AP'
            aps_col = f'{prefix} Sharp AP'
            for i in range(n):
                lab = labels[i] or None
                labnoloc = self._infer_label_noloc(lab) if lab else None
                link = linkeds[i] or None
                apb = row_ce.get(apb_col) if apb_col in row_ce.index else row_ce.get(apb_col)
                aps = row_ce.get(aps_col) if aps_col in row_ce.index else row_ce.get(aps_col)
                # pick cooldown for this item
                cd = None
                if cd_vals:
                    if i < len(cd_vals):
                        cd = cd_vals[i]
                    else:
                        cd = cd_vals[-1]
                power_val = row_ce.get(f'{prefix} damage') if f'{prefix} damage' in row_ce.index else get_row_value(row_ce, f'{prefix} damage')
                li = self._make_tool_li(
                    label=lab,
                    labelNoLocation=labnoloc,
                    capacities=caps,
                    power=power_val,
                    cooldown=cd,
                    linked=link,
                    class_attr='CombatExtended.ToolCE',
                    ap_blunt=apb if apb is not None and str(apb).strip().lower() not in ('', 'no', 'none') else None,
                    ap_sharp=aps if aps is not None and str(aps).strip().lower() not in ('', 'no', 'none') else None,
                    stun=stun_val
                )
                tools.append(li)

        # Paw claw/punch CE
        _add_paired_ce('Paw claw/punch', 'Paw claw/punch label', 'Paw claw/punch linkedBodyPartsGroup', 'Paw claw/punch capacities')

        # Poke/leg claws CE
        _add_paired_ce('Poke/leg claws', 'Poke/leg claws label', 'Poke/leg claws linkedBodyPartsGroup', 'Poke/leg claws capacities')

        # Horn / Antler / Tusks CE (single)
        if ce_row.get('Horn/Antler/Tusks damage') and str(ce_row.get('Horn/Antler/Tusks damage')).strip().lower() not in ('', 'no', 'none'):
            apb = ce_row.get('Horn/Antler/Tusks Blunt AP')
            aps = ce_row.get('Horn/Antler/Tusks Sharp AP')
            lab = row_for_labels.get('Horn/Antler/Tusks label') if 'Horn/Antler/Tusks label' in row_for_labels.index else None
            caps = row_for_labels.get('Horn/Antler/Tusks capacities') if 'Horn/Antler/Tusks capacities' in row_for_labels.index else None
            link = row_for_labels.get('Horn/Antler/Tusks linkedBodyPartsGroup') if 'Horn/Antler/Tusks linkedBodyPartsGroup' in row_for_labels.index else None
            male_only = row_for_labels.get('Horn/Antler/Tusks Male only')
            li = self._make_tool_li(
                label=lab,
                capacities=caps,
                power=ce_row.get('Horn/Antler/Tusks damage'),
                cooldown=ce_row.get('Horn/Antler/Tusks attack interval'),
                linked=link,
                class_attr='CombatExtended.ToolCE',
                ap_blunt=apb if apb is not None and str(apb).strip().lower() not in ('', 'no', 'none') else None,
                ap_sharp=aps if aps is not None and str(aps).strip().lower() not in ('', 'no', 'none') else None,
                restricted_gender='Male' if (str(male_only).strip().lower() in ('true', '1', 'yes', 'y', 't')) else None,
                stun=stun_val
            )
            tools.append(li)

        # Bite CE
        if ce_row.get('Bite damage') and str(ce_row.get('Bite damage')).strip().lower() not in ('', 'no', 'none'):
            apb = ce_row.get('Bite Blunt AP')
            aps = ce_row.get('Bite Sharp AP')
            lab = row_for_labels.get('Bite label') if 'Bite label' in row_for_labels.index else None
            caps = row_for_labels.get('Bite capacities') if 'Bite capacities' in row_for_labels.index else None
            link = row_for_labels.get('Bite linkedBodyPartsGroup') if 'Bite linkedBodyPartsGroup' in row_for_labels.index else None
            li = self._make_tool_li(
                label=lab,
                capacities=caps,
                power=ce_row.get('Bite damage'),
                cooldown=ce_row.get('Bite attack interval'),
                linked=link,
                class_attr='CombatExtended.ToolCE',
                ap_blunt=apb if apb is not None and str(apb).strip().lower() not in ('', 'no', 'none') else None,
                ap_sharp=aps if aps is not None and str(aps).strip().lower() not in ('', 'no', 'none') else None,
                stun=stun_val
            )
            tools.append(li)

        if len(list(tools)) == 0:
            return None
        return tools

    # ---------- helper: compute common values for abstract group ----------
    def compute_abstract_common(self, children, def_to_row):
        common_values = {}
        differing_cols = []

        # pick first available child row
        first_row = None
        for c in children:
            if c in def_to_row:
                first_row = def_to_row[c]
                break
        if first_row is None:
            return common_values, differing_cols, None

        candidate_canonicals = set(COLUMN_ALIASES.keys())
        candidate_canonicals.update(first_row.index.tolist())

        for col in candidate_canonicals:
            vals = []
            for c in children:
                r = def_to_row.get(c)
                if r is None:
                    vals.append('')
                    continue
                if col in COLUMN_ALIASES:
                    v = get_row_value(r, col)
                else:
                    v = r.get(col, '')
                vals.append(norm(v))
            vals_non_empty = [v for v in vals if v != '']
            if len(vals_non_empty) == 0:
                continue
            if all(v == vals_non_empty[0] for v in vals_non_empty):
                if col in COLUMN_ALIASES:
                    common_values[col] = get_row_value(first_row, col)
                else:
                    common_values[col] = first_row.get(col)
            else:
                differing_cols.append(col)

        # tool signature: check if all tool-related columns identical across children
        tool_cols = ['Head damage', 'Poke/leg claws damage', 'Horn/Antler/Tusks damage', 'Bite damage', 'Paw claw/punch damage']
        tool_vals = []
        for c in children:
            r = def_to_row.get(c)
            if r is None:
                tool_vals.append(tuple('' for _ in tool_cols))
            else:
                tool_vals.append(tuple(norm(get_row_value(r, col)) for col in tool_cols))
        if len(set(tool_vals)) == 1:
            tool_signature = tool_vals[0]
        else:
            tool_signature = None

        return common_values, differing_cols, tool_signature
        
    def _map_lifestage_def(self, original_def, parent):
        if not parent:
            return original_def
        if str(parent).strip().lower() == 'baseinsect':
            if original_def == 'AnimalBaby':
                return 'EusocialInsectLarva'
            if original_def == 'AnimalJuvenile':
                return 'EusocialInsectJuvenile'
            if original_def == 'AnimalAdult':
                return 'EusocialInsectAdult'
        return original_def
        
    # helper: считаем "No" как отсутствие значения
    def _age_value_valid(self, v):
        """
        Возвращает True если значение очевидно действительно (не None, не пустая строка и не 'No').
        """
        if v is None:
            return False
        s = str(v).strip()
        if s == '':
            return False
        if s.lower() == 'no':
            return False
        return True

    def _age_value_or_none(self, v):
        """
        Преобразует вход в None если пусто/No, иначе возвращает оригинальное значение (строку/число).
        """
        if v is None:
            return None
        s = str(v).strip()
        if s == '' or s.lower() == 'no':
            return None
        return v
        
    def create_life_stage_full_replace_or_add(self, def_name, juv_text, adult_text, sounds=None, is_abstract=False, parent='', inherit_false=False):
        """
        То же, что было, но теперь учитываем parent: если parent == "BaseInsect",
        используем EusocialInsectLarva / EusocialInsectJuvenile / EusocialInsectAdult.
        Параметр inherit_false (bool) — если True, то создаваемый <lifeStageAges> будет иметь
        атрибут Inherit="False".
        """
        attr = '@Name' if is_abstract else 'defName'
        base_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race"

        # Построим элемент <lifeStageAges> с li-элементами и звуками для Adult (если есть)
        lsa = LET.Element('lifeStageAges')
        if inherit_false:
            # задаём точно строку "False", как в оригинале
            lsa.set('Inherit', 'False')

        # Baby
        li_baby = LET.SubElement(lsa, 'li')
        d1 = LET.SubElement(li_baby, 'def')
        d1.text = self._map_lifestage_def('AnimalBaby', parent)
        m1 = LET.SubElement(li_baby, 'minAge'); m1.text = '0'

        # Juvenile
        li_juv = LET.SubElement(lsa, 'li')
        d2 = LET.SubElement(li_juv, 'def')
        d2.text = self._map_lifestage_def('AnimalJuvenile', parent)
        m2 = LET.SubElement(li_juv, 'minAge'); m2.text = str(juv_text)

        # Adult
        li_adult = LET.SubElement(lsa, 'li')
        d3 = LET.SubElement(li_adult, 'def')
        d3.text = self._map_lifestage_def('AnimalAdult', parent)
        m3 = LET.SubElement(li_adult, 'minAge'); m3.text = str(adult_text)
        # звуковые теги (если переданы)
        if sounds:
            for key in ('soundWounded', 'soundDeath', 'soundCall', 'soundAngry'):
                v = sounds.get(key)
                if v is not None and str(v).strip() != '':
                    el = LET.SubElement(li_adult, key)
                    el.text = str(v)

        # Составляем операцию
        op = LET.Element("Operation", Class="PatchOperationConditional")

        # верхний xpath указываем прямо на lifeStageAges
        xpath = LET.SubElement(op, "xpath")
        xpath.text = base_xpath + "/lifeStageAges"

        # match: заменяем весь узел lifeStageAges
        match = LET.SubElement(op, "match", Class="PatchOperationReplace")
        LET.SubElement(match, "xpath").text = base_xpath + "/lifeStageAges"
        val_match = LET.SubElement(match, "value")
        val_match.append(copy.deepcopy(lsa))

        # nomatch: добавляем внутрь /race
        nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
        LET.SubElement(nomatch, "xpath").text = base_xpath
        val_nom = LET.SubElement(nomatch, "value")
        val_nom.append(copy.deepcopy(lsa))

        return op

    def generate_fixed_xml(self, xml_path, output_path):
        try:
            if not os.path.exists(xml_path):
                print(f"Missing XML: {xml_path}")
                return False
            parser = LET.XMLParser(remove_comments=False, recover=True)
            tree = LET.parse(xml_path, parser)
            original_root = tree.getroot()
            patched_defnames, patched_abstracts = self.get_patched_names(original_root)

            # --- We'll maintain two parallel data sets:
            # def_row_all / all_abstract_groups  -> all TSV rows (used for computing parent_common_map)
            # def_to_row / present_abstract_groups -> only TSV rows that correspond to defs actually present in this XML (used to emit child ops)
            all_abstract_groups = defaultdict(list)
            def_row_all = {}
            present_abstract_groups = defaultdict(list)
            def_to_row = {}
            def_mod_conflicts = {}

            if self.vanilla_df is None:
                raise RuntimeError("Vanilla table not provided")

            # First pass: collect ALL TSV rows into def_row_all and all_abstract_groups
            for _, row in self.vanilla_df.iterrows():
                xml_name = row.get('XML name', '').strip() if 'XML name' in row.index else (row.iloc[0].strip() if len(row.index) > 0 else '')
                m = re.search(r'<li>(.*?)</li>', xml_name)
                def_name = m.group(1) if m else xml_name
                parent = row.get('Parrent abstract', '') if 'Parrent abstract' in row.index else row.get('Parent abstract', '') if 'Parent abstract' in row.index else ''
                if parent is None:
                    parent = ''
                parent = str(parent).strip()

                # store in global TSV map
                def_row_all[def_name] = row
                mods_for_def = self._split_mod_list(get_row_value(row, 'ModConflict'))
                if mods_for_def:
                    existing_mods = def_mod_conflicts.setdefault(def_name, [])
                    for mod_name in mods_for_def:
                        if mod_name not in existing_mods:
                            existing_mods.append(mod_name)
                if parent and parent != 'None':
                    all_abstract_groups[parent].append(def_name)

            # Second pass: but only mark as "present" those defs that actually are referenced in this XML
            # patched_defnames is provided by get_patched_names(original_root)
            for def_name, row in def_row_all.items():
                if def_name in patched_defnames:
                    def_to_row[def_name] = row
                    # determine parent (again, but only for rows that are present)
                    parent = row.get('Parrent abstract', '') if 'Parrent abstract' in row.index else row.get('Parent abstract', '') if 'Parent abstract' in row.index else ''
                    if parent is None:
                        parent = ''
                    parent = str(parent).strip()
                    if parent and parent != 'None':
                        present_abstract_groups[parent].append(def_name)

            # --- compute common maps for abstracts using ALL children from TSV (def_row_all) ---
            parent_common_map = {}  # abstract -> dict with keys: common_values, differing_cols, tool_signature
            for abstract, children in all_abstract_groups.items():
                # compute_abstract_common expects a list of child names and a mapping def->row
                common_values, differing_cols, tool_signature = self.compute_abstract_common(children, def_row_all)
                parent_common_map[abstract] = {
                    'common_values': common_values,
                    'differing_cols': differing_cols,
                    'tool_signature': tool_signature,
                }

            # ---------- determine FIRST child for each abstract (important change) ----------
            # We'll use TSV order (all_abstract_groups built in TSV order), and pick the first listed child.
            # Parent-level (abstract) patches will be emitted only in the XML where this first child is present.
            first_child_of_abstract = {}
            for abstract, children in all_abstract_groups.items():
                if children:
                    first_child_of_abstract[abstract] = children[0]

            # ---------- PAWN KIND parent grouping (from ALL TSV rows) ----------
            pawn_parent_groups_all = defaultdict(list)
            for def_name, row in def_row_all.items():
                pawn_parent = ''
                if 'Parrent Pawn kind abstract' in row.index:
                    pawn_parent = str(row.get('Parrent Pawn kind abstract', '')).strip()
                elif 'Parent Pawn kind abstract' in row.index:
                    pawn_parent = str(row.get('Parent Pawn kind abstract', '')).strip()
                elif 'Parrent Pawn kind' in row.index:
                    pawn_parent = str(row.get('Parrent Pawn kind', '')).strip()
                if pawn_parent and pawn_parent != 'None':
                    pawn_parent_groups_all[pawn_parent].append(def_name)

            # compute pawn parent common values (for PawnKindDef fields) using ALL children
            pawn_parent_common_map = {}  # pawn_parent -> {'common_values':..., 'differing_cols':...}
            pawn_fields = ['Combat power', 'ecoSystemWeight', 'Wild group size', 'CanArriveManhunter', 'moveSpeedFactorByTerrainTag']
            for pawn_parent, children in pawn_parent_groups_all.items():
                common_values = {}
                differing_cols = []
                # pick first child's row
                first_row = None
                for c in children:
                    if c in def_row_all:
                        first_row = def_row_all[c]
                        break
                if first_row is None:
                    pawn_parent_common_map[pawn_parent] = {'common_values': common_values, 'differing_cols': differing_cols, 'children': children}
                    continue

                for col in pawn_fields:
                    vals = []
                    for c in children:
                        r = def_row_all.get(c)
                        if r is None:
                            vals.append('')
                        else:
                            v = get_row_value(r, col)
                            vals.append(norm(v))
                    vals_non_empty = [v for v in vals if v != '']
                    if len(vals_non_empty) == 0:
                        continue
                    if all(v == vals_non_empty[0] for v in vals_non_empty):
                        # store original (non-normalized) from first_row
                        common_values[col] = get_row_value(first_row, col)
                    else:
                        differing_cols.append(col)

                pawn_parent_common_map[pawn_parent] = {'common_values': common_values, 'differing_cols': differing_cols, 'children': children}

            # ---------- determine FIRST child for each pawn_parent (same policy as for abstracts) ----------
            first_child_of_pawn_parent = {}
            for pawn_parent, children in pawn_parent_groups_all.items():
                if children:
                    first_child_of_pawn_parent[pawn_parent] = children[0]

            new_operations = []

            # Абстрактные классы (сначала)
            # KEY CHANGE: generate abstract patches only in the XML that contains the FIRST child (by TSV order)
            # For each abstract in patched_abstracts: check if its first TSV child is present in def_to_row (present in this XML).
            for abstract in patched_abstracts:
                first_child = first_child_of_abstract.get(abstract)
                if not first_child:
                    # no children known in TSV -> nothing to generate
                    continue
                # only generate the parent abstract patch in the file that contains the first child
                if first_child not in def_to_row:
                    # skip: this XML doesn't contain the first child for this abstract
                    continue
                # gather present children (for passing to generate_abstract_patches)
                children_present = present_abstract_groups.get(abstract, [])
                # PASS parent_common_map so generate_abstract_patches can decide commonness using ALL TSV rows
                new_operations.extend(self.generate_abstract_patches(abstract, children_present, def_to_row, original_root, parent_common_map=parent_common_map))

            # ---------- PawnKind parent patches (based on pawn_parent_common_map computed from ALL TSV rows) ----------
            # KEY CHANGE: create pawn-parent ops only in the XML that contains the FIRST child of that pawn_parent
            for pawn_parent, info in pawn_parent_common_map.items():
                first_child = first_child_of_pawn_parent.get(pawn_parent)
                if not first_child:
                    continue
                # only create pawn_parent operations in the XML that contains the first child
                if first_child not in def_to_row:
                    continue

                common_values = info.get('common_values', {})
                differing_cols = info.get('differing_cols', [])
                new_operations.append(LET.Comment(""))
                new_operations.append(LET.Comment(f" PawnKind parent {pawn_parent} "))
                # Combat power
                if 'Combat power' in common_values:
                    val = common_values['Combat power']
                    if val is not None and str(val).strip() != '':
                        if str(val).lower() == 'no':
                            new_operations.append(self.create_safe_remove(pawn_parent, '', 'combatPower', is_pawn=True, is_abstract=True))
                        else:
                            new_operations.append(self.create_safe_replace(pawn_parent, '', 'combatPower', val, is_pawn=True, is_abstract=True))
                elif 'Combat power' in differing_cols:
                    new_operations.append(self.create_safe_remove(pawn_parent, '', 'combatPower', is_pawn=True, is_abstract=True))

                # ecoSystemWeight
                if 'ecoSystemWeight' in common_values:
                    val = common_values['ecoSystemWeight']
                    if val is not None and str(val).strip() != '':
                        if str(val).lower() == 'no':
                            new_operations.append(self.create_safe_remove(pawn_parent, '', 'ecoSystemWeight', is_pawn=True, is_abstract=True))
                        else:
                            new_operations.append(self.create_safe_replace(pawn_parent, '', 'ecoSystemWeight', val, is_pawn=True, is_abstract=True))
                elif 'ecoSystemWeight' in differing_cols:
                    new_operations.append(self.create_safe_remove(pawn_parent, '', 'ecoSystemWeight', is_pawn=True, is_abstract=True))

                # Wild group size -> wildGroupSize (special element)
                if 'Wild group size' in common_values:
                    val = common_values['Wild group size']
                    s = str(val).strip()
                    if s.lower() in ('no', ''):
                        new_operations.append(self.create_safe_remove(pawn_parent, '', 'wildGroupSize', is_pawn=True, is_abstract=True))
                    else:
                        el = LET.Element('wildGroupSize')
                        el.text = s
                        new_operations.append(self.create_safe_replace(pawn_parent, '', 'wildGroupSize', el, is_pawn=True, is_abstract=True))
                elif 'Wild group size' in differing_cols:
                    new_operations.append(self.create_safe_remove(pawn_parent, '', 'wildGroupSize', is_pawn=True, is_abstract=True))

                # CanArriveManhunter
                if 'CanArriveManhunter' in common_values:
                    val = common_values['CanArriveManhunter']
                    if val is not None and str(val).strip() != '':
                        if str(val).lower() in ('no', 'false', '0'):
                            new_operations.append(self.create_safe_remove(pawn_parent, '', 'canArriveManhunter', is_pawn=True, is_abstract=True))
                        else:
                            new_operations.append(self.create_safe_replace(pawn_parent, '', 'canArriveManhunter', val, is_pawn=True, is_abstract=True))
                elif 'CanArriveManhunter' in differing_cols:
                    new_operations.append(self.create_safe_remove(pawn_parent, '', 'canArriveManhunter', is_pawn=True, is_abstract=True))
                    
                # moveSpeedFactorByTerrainTag (special handling: ensure container, then set li[key='Water'])
                if 'moveSpeedFactorByTerrainTag' in common_values:
                    val = common_values['moveSpeedFactorByTerrainTag']
                    s = '' if val is None else str(val).strip()
                    # if explicit 'no' or empty -> remove water entry
                    if s.lower() in ('', 'no', 'none'):
                        # remove specific Water li under pawn parent (abstract)
                        new_operations.append(self.create_safe_remove(pawn_parent, 'moveSpeedFactorByTerrainTag', "li[key = 'Water']", is_pawn=True, is_abstract=True))
                    else:
                        # ensure container exists on pawn parent (abstract)
                        new_operations.append(self.create_ensure_container(pawn_parent, 'moveSpeedFactorByTerrainTag', is_pawn=True, is_abstract=True))
                        # then replace/add the Water li
                        new_operations.append(self.create_moveSpeedFactorByTerrainTag_patch(pawn_parent, s, is_pawn=True, is_abstract=True))
                elif 'moveSpeedFactorByTerrainTag' in differing_cols:
                    # differing -> remove parent's container so children can override individually
                    new_operations.append(self.create_safe_remove(pawn_parent, '', 'moveSpeedFactorByTerrainTag', is_pawn=True, is_abstract=True))

            # Конкретные животные — только те, которые присутствуют в XML (def_to_row)
            for def_name, row in def_to_row.items():
                parent = row.get('Parrent abstract', 'None') if 'Parrent abstract' in row.index else row.get('Parent abstract', 'None') if 'Parent abstract' in row.index else 'None'
                if parent is None:
                    parent = 'None'
                parent = parent.strip()
                def_ops = self.generate_def_patches(
                    def_name,
                    row,
                    original_root,
                    parent if parent != 'None' else None,
                    parent_common_map,
                    pawn_parent_common_map
                )
                wrapped_def_ops = self._wrap_ops_in_mod_conflict(def_ops, def_mod_conflicts.get(def_name), as_li=False)
                new_operations.extend(wrapped_def_ops)

            # Вставляем комментарий перед CE-блоком (пустая строка + комментарий)
            new_operations.append(LET.Comment(""))
            new_operations.append(LET.Comment(" Combat Extended specific patches "))

            # CE блок — pass def_to_row (only present defs) so CE block will only be generated for present defs
            new_operations.append(self.generate_ce_block(def_to_row, original_root, def_mod_conflicts=def_mod_conflicts))

            # --- deduplicate identical operations (by exact XML text) to avoid double removes ---
            seen = set()
            unique_ops = []
            for op in new_operations:
                try:
                    s = LET.tostring(op)
                except Exception:
                    # fallback to str() to avoid crashing on weird nodes
                    s = str(op)
                if s in seen:
                    # skip exact duplicate
                    continue
                seen.add(s)
                unique_ops.append(op)

            # build final root from unique ops only
            new_root = LET.Element("Patch")
            for op in unique_ops:
                new_root.append(op)

            ensure_dir(output_path)
            with open(output_path, 'wb') as f:
                f.write(LET.tostring(new_root, pretty_print=True, encoding='utf-8', xml_declaration=True))

            self.validate_xml(output_path)
            return True
        except Exception as e:
            traceback.print_exc()
            return False

    def generate_abstract_patches(self, abstract, children, def_to_row, original_root, parent_common_map=None):
        """
        Создаёт патчи для абстрактного класса.
        Если передан parent_common_map и он содержит запись для abstract,
        используем precomputed common_values/differing_cols/tool_signature (они были рассчитаны по ВСЕМ TSV).
        В противном случае (нет записи) — fallback к прежнему поведению (вычисление по children и def_to_row).
        """
        ops = []
        ops.append(LET.Comment(""))
        ops.append(LET.Comment(f" {abstract} "))

        # If parent_common_map is provided and contains this abstract, use it (computed on ALL TSV rows)
        if parent_common_map and abstract in parent_common_map:
            pm = parent_common_map[abstract]
            common_values = pm.get('common_values', {})
            differing_cols = pm.get('differing_cols', [])
            tool_signature = pm.get('tool_signature', None)
            # Note: children here are the present children; pm was computed across all TSV children
        else:
            # Fallback: compute common_values/differing_cols/tool_signature using ONLY provided children (old behavior)
            first_row = None
            for c in children:
                if c in def_to_row:
                    first_row = def_to_row[c]
                    break
            if first_row is None:
                return []  # nothing to do

            common_values = {}
            differing_cols = []

            candidate_canonicals = set(COLUMN_ALIASES.keys())
            candidate_canonicals.update(first_row.index.tolist())

            for col in candidate_canonicals:
                vals = []
                for c in children:
                    r = def_to_row.get(c)
                    if r is None:
                        vals.append('')
                        continue
                    if col in COLUMN_ALIASES:
                        v = get_row_value(r, col)
                    else:
                        v = r.get(col, '')
                    vals.append(norm(v))
                vals_non_empty = [v for v in vals if v != '']
                if len(vals_non_empty) == 0:
                    continue
                if all(v == vals_non_empty[0] for v in vals_non_empty):
                    if col in COLUMN_ALIASES:
                        common_values[col] = get_row_value(first_row, col)
                    else:
                        common_values[col] = first_row.get(col)
                else:
                    differing_cols.append(col)

            # tool signature: check if all tool-related columns identical across children
            tool_cols = ['Head damage', 'Poke/leg claws damage', 'Horn/Antler/Tusks damage', 'Bite damage', 'Paw claw/punch damage']
            tool_vals = []
            for c in children:
                r = def_to_row.get(c)
                if r is None:
                    tool_vals.append(tuple('' for _ in tool_cols))
                else:
                    tool_vals.append(tuple(norm(get_row_value(r, col)) for col in tool_cols))
            if len(set(tool_vals)) == 1:
                tool_signature = tool_vals[0]
            else:
                tool_signature = None

        # --- StatBases (use common_values/differing_cols computed above) ---
        stat_order = ['MarketValue', 'MoveSpeed', 'Wildness', 'FilthRate', 'ComfyTemperatureMin', 'ComfyTemperatureMax', 'ArmorRating_Blunt', 'ArmorRating_Sharp']
        for stat in stat_order:
            if stat in common_values:
                # Decide whether this common value is actually a "remove" marker like 'No' / '' / 'None'
                val = common_values[stat]
                sval = '' if val is None else str(val).strip()
                if sval.lower() in ('', 'no', 'none'):
                    ops.append(self.create_safe_remove(abstract, 'statBases', stat, is_abstract=True))
                else:
                    ops.append(self.create_safe_replace(abstract, 'statBases', stat, common_values[stat], is_abstract=True))
            elif stat in differing_cols:
                ops.append(self.create_safe_remove(abstract, 'statBases', stat, is_abstract=True))

        # ToxicEnvironmentResistance
        if 'ToxicEnvironmentResistance' in common_values:
            v = common_values['ToxicEnvironmentResistance']
            if str(v).strip().lower() == 'standard':
                ops.append(self.create_safe_remove(abstract, 'statBases', 'ToxicEnvironmentResistance', is_abstract=True))
            else:
                ops.append(self.create_safe_replace(abstract, 'statBases', 'ToxicEnvironmentResistance', v, is_abstract=True))
        elif 'ToxicEnvironmentResistance' in differing_cols:
            ops.append(self.create_safe_remove(abstract, 'statBases', 'ToxicEnvironmentResistance', is_abstract=True))

        # --- Tools: create from TSV (vanilla) if tool_signature identical across children ---
        tool_cols = ['Head damage', 'Poke/leg claws damage', 'Horn/Antler/Tusks damage', 'Bite damage', 'Paw claw/punch damage']
        if tool_signature is None:
            # tools differ -> remove from abstract (emit conditional remove)
            # Use abstract @Name xpath for abstract-level nodes
            remove_op = LET.Element("Operation", Class="PatchOperationConditional")
            LET.SubElement(remove_op, "xpath").text = f"/Defs/ThingDef[@Name = \"{abstract}\"]/tools"
            match_node = LET.SubElement(remove_op, "match", Class="PatchOperationRemove")
            LET.SubElement(match_node, "xpath").text = f"/Defs/ThingDef[@Name = \"{abstract}\"]/tools"
            ops.append(remove_op)
        else:
            # tools identical across ALL children -> emit parent-level tools.
            # Choose a representative child (prefer present child rows)
            rep_row = None
            for c in children:
                if c in def_to_row:
                    rep_row = def_to_row[c]
                    break
            # If representative found -> build tools from TSV (vanilla)
            # If not found -> still emit conditional remove to be safe
            parent_remove_op = LET.Element("Operation", Class="PatchOperationConditional")
            LET.SubElement(parent_remove_op, "xpath").text = f"/Defs/ThingDef[@Name = \"{abstract}\"]/tools"
            LET.SubElement(LET.SubElement(parent_remove_op, "match", Class="PatchOperationRemove"), "xpath").text = f"/Defs/ThingDef[@Name = \"{abstract}\"]/tools"
            ops.append(parent_remove_op)

            if rep_row is not None:
                built = self.build_tools_vanilla(rep_row, is_abstract=True)
                if built is not None:
                    # determine if original abstract had Inherit="False" on tools
                    inherit = self.extract_tools_inherit(original_root, abstract, is_abstract=True)
                    built_copy = copy.deepcopy(built)
                    if inherit:
                        # built is a <tools> element - set attribute on it
                        built_copy.set('Inherit', 'False')
                    # add built tools under abstract (always add)
                    add_op = LET.Element("Operation", Class="PatchOperationAdd")
                    LET.SubElement(add_op, "xpath").text = f"/Defs/ThingDef[@Name = \"{abstract}\"]"
                    val_node = LET.SubElement(add_op, "value")
                    val_node.append(built_copy)
                    ops.append(add_op)
                else:
                    # TSV says "no tools" for representative -> we already emitted remove, no add
                    pass
            else:
                # no representative present: safer to not add tools (we already removed)
                pass

        # Race properties handling: use common_values/differing_cols above
        race_mapping = {
            'baseBodySize': 'baseBodySize',
            'baseHealthScale': 'baseHealthScale',
            'baseHungerRate': 'baseHungerRate',
            'lifeExpectancy': 'Lifespan (years)',
            'gestationPeriodDays': 'Gestation period (days)',
            'herdAnimal': 'herdAnimal',
            'herdMigrationAllowed': 'herdMigrationAllowed',
            'foodType': 'Foodtype',  # важная связка: tag 'foodType' -> canonical 'Foodtype'
            'roamMtbDays': 'roamMtbDays',
            'LeatherDef': 'LeatherDef',
            'manhunterOnTameFailChance': 'manhunterOnTameFailChance',
            'manhunterOnDamageChance': 'manhunterOnDamageChance',
            'petness': 'petness',
            'nuzzleMtbHours': 'nuzzleMtbHours',
            'mateMtbHours': 'mateMtbHours',
            'trainability': 'trainability',
            'packAnimal': 'PackAnimal',
            'predator': 'predator',
            'maxPreyBodySize': 'maxPreyBodySize',
            'nameOnTameChance': 'nameOnTameChance',
            'Body': 'Body',
            'waterCellCost': 'waterCellCost',
            'waterSeeker': 'waterSeeker',
            'canFishForFood': 'canFishForFood',
        }

        race_order = ['baseBodySize', 'baseHealthScale', 'baseHungerRate', 'lifeExpectancy', 'gestationPeriodDays',
                      'herdAnimal', 'herdMigrationAllowed', 'foodType', 'roamMtbDays', 'LeatherDef',
                      'manhunterOnTameFailChance', 'manhunterOnDamageChance',
                      'petness', 'nuzzleMtbHours', 'mateMtbHours', 'trainability', 'predator', 'maxPreyBodySize', 'nameOnTameChance', 'Body', 'waterCellCost', 'waterSeeker', 'canFishForFood']

        for tag in race_order:
            col = race_mapping.get(tag, tag)
            xml_tag = tag if tag and tag[0].islower() else (tag[0].lower() + tag[1:] if tag else tag)
            if tag == 'lifeExpectancy':
                col = 'Lifespan (years)'
            elif tag == 'gestationPeriodDays':
                col = 'Gestation period (days)'

            if col in common_values:
                val = common_values.get(col)
                if val is None or str(val).strip() == '':
                    continue
                s = str(val).strip()

                # LeatherDef: всегда replace/remove + удаляем useLeatherFrom
                if xml_tag == 'leatherDef':
                    if s.lower() in ('', 'no', 'none'):
                        ops.append(self.create_safe_remove(abstract, 'race', 'leatherDef', is_abstract=True))
                    else:
                        ops.append(self.create_safe_replace(abstract, 'race', 'leatherDef', s, is_abstract=True))
                    ops.append(self.create_safe_remove(abstract, 'race', 'useLeatherFrom', is_abstract=True))
                    continue

                if s.lower() in ('', 'no', 'none'):
                    ops.append(self.create_safe_remove(abstract, 'race', xml_tag, is_abstract=True))
                    continue
                if xml_tag == 'gestationPeriodDays':
                    if re.search(r'\blay\s*egg(s)?\b', s, flags=re.I) or (re.search(r'\begg(s)?\b', s, flags=re.I) and try_parse_number(s) is None):
                        ops.append(self.create_safe_remove(abstract, 'race', xml_tag, is_abstract=True))
                        continue
                    num = try_parse_number(s)
                    if num is not None:
                        ops.append(self.create_safe_replace(abstract, 'race', xml_tag, s, is_abstract=True))
                    else:
                        ops.append(self.create_safe_remove(abstract, 'race', xml_tag, is_abstract=True))
                    continue
                if xml_tag in ('manhunterOnTameFailChance', 'manhunterOnDamageChance'):
                    formatted = format_prob_value(s)
                    ops.append(self.create_safe_replace(abstract, 'race', xml_tag, formatted, is_abstract=True))
                if xml_tag == 'waterCellCost':
                    s = str(common_values.get(col)).strip()
                    if s.lower() in ('', 'no', 'none'):
                        ops.append(self.create_safe_remove(abstract, 'race', 'waterCellCost', is_abstract=True))
                    else:
                        ops.append(self.create_safe_replace(abstract, 'race', 'waterCellCost', s, is_abstract=True))
                    continue

                if xml_tag == 'waterSeeker':
                    s = str(common_values.get(col)).strip()
                    val = 'true' if s.lower() in ('true', '1', 'yes', 'y', 't') else 'false'
                    ops.append(self.create_safe_replace(abstract, 'race', 'waterSeeker', val, is_abstract=True))
                    continue
                    
                if xml_tag == 'canFishForFood':
                    s = str(common_values.get(col)).strip()
                    val = 'true' if s.lower() in ('true', '1', 'yes', 'y', 't') else 'false'
                    ops.append(self.create_safe_replace(abstract, 'race', 'canFishForFood', val, is_abstract=True))
                    continue
                else:
                    ops.append(self.create_safe_replace(abstract, 'race', xml_tag, s, is_abstract=True))
            elif col in differing_cols:
                if xml_tag == 'leatherDef':
                    ops.append(self.create_safe_remove(abstract, 'race', 'leatherDef', is_abstract=True))
                    ops.append(self.create_safe_remove(abstract, 'race', 'useLeatherFrom', is_abstract=True))
                else:
                    ops.append(self.create_safe_remove(abstract, 'race', tag, is_abstract=True))

        # lifeStageAges handling using common_values/differing_cols
        # If parent (abstract) has sound tags in original XML, create full lifeStageAges (with sounds)
        # using create_life_stage_full_replace_or_add. Otherwise fall back to per-minAge replaces
        # (or removal if children differ).
        juv_col = 'Juv age (years)'
        adult_col_candidates = ('Adult age (years)', 'Adult age')

        # Treat 'No' (case-insensitive) as absence of value
        juv_present = (juv_col in common_values) and self._age_value_valid(common_values.get(juv_col))
        adult_present = any((c in common_values and self._age_value_valid(common_values.get(c))) for c in adult_col_candidates)

        juv_val = common_values.get(juv_col) if juv_present else None
        adult_val = None
        for c in adult_col_candidates:
            if c in common_values and self._age_value_valid(common_values.get(c)):
                adult_val = common_values.get(c)
                break

        # If parent indicates removal (children differ) -> remove whole block
        if juv_col in differing_cols or any((c in differing_cols for c in adult_col_candidates)):
            ops.append(self.create_safe_remove(abstract, 'race', 'lifeStageAges', is_abstract=True))
        else:
            # If at least one age present AND original abstract has sounds, build full lifeStageAges (with sounds)
            if juv_present or adult_present:
                # try to extract sounds from original abstract (use @Name)
                sounds, inherit = self.extract_sounds(original_root, abstract, is_abstract=True)
                if sounds:
                    # defaults if one of the ages missing
                    juv_text = str(juv_val).strip() if juv_val is not None and str(juv_val).strip() != '' else "0.2"
                    adult_text = str(adult_val).strip() if adult_val is not None and str(adult_val).strip() != '' else "0.5"
                    ops.append(self.create_life_stage_full_replace_or_add(abstract, juv_text, adult_text, sounds=sounds, is_abstract=True, inherit_false=inherit))
                else:
                    # no sounds on abstract — fall back to individual minAge replaces (as before)
                    if juv_present:
                        ops.append(self.create_life_stage_replace(abstract, 'AnimalJuvenile', juv_val, is_abstract=True))
                    if adult_present:
                        ops.append(self.create_life_stage_replace(abstract, 'AnimalAdult', adult_val, is_abstract=True))
            else:
                # neither age present in common_values: nothing to do (or removal handled above)
                pass
            
        # specialTrainables
        if 'specialTrainables' in common_values:
            val = common_values.get('specialTrainables')
            if val is not None and str(val).strip() != '':
                sval = str(val).strip()
                # Если явно указано "No" / "None" -> только очистка списка (без добавления)
                if sval.lower() in ('no', 'none', ''):
                    ops.append(self.create_safe_remove(abstract, 'race', 'specialTrainables', is_abstract=True))
                else:
                    # разбиваем по запятым/новым строкам аналогично TradeTags
                    items = [t.strip() for t in re.split(r',|\n', sval) if t.strip()]
                    if items:
                        ops.extend(self.create_special_trainables_patch(abstract, items, is_abstract=True))
        elif 'specialTrainables' in differing_cols:
            # разные дети -> удаляем у абстракта, чтобы дети могли задать свои значения
            ops.append(self.create_safe_remove(abstract, 'race', 'specialTrainables', is_abstract=True))
            
        # TradeTags
        if 'TradeTags' in common_values:
            val = common_values.get('TradeTags')
            if val is not None and str(val).strip() != '':
                tags = [t.strip() for t in re.split(r',|\n', str(val)) if t.strip()]
                if tags:
                    inherit = self.extract_trade_tags_inherit(original_root, abstract, is_abstract=True)
                    ops.extend(self.create_trade_tags_patch(abstract, tags, is_abstract=True, inherit=inherit))

        elif 'TradeTags' in differing_cols:
            ops.append(self.create_safe_remove(abstract, '', 'tradeTags', is_abstract=True))

        # Litter size -> litterSizeCurve on abstract if same, else remove
        if 'Litter size' in common_values:
            val = common_values.get('Litter size')
            s = str(val).strip()
            if s in ('', 'no', 'No', 'None'):
                ops.append(self.create_safe_remove(abstract, 'race', 'litterSizeCurve', is_abstract=True))
            else:
                mean = try_parse_number(s)
                if mean is None and '~' in s:
                    parts = s.split('~')
                    try:
                        mi = try_parse_number(parts[0])
                        ma = try_parse_number(parts[1])
                        if mi is not None and ma is not None:
                            mean = (mi + ma) / 2.0
                    except Exception:
                        mean = None
                if mean and mean > 1:
                    points = generate_litter_curve(mean)
                    if points:
                        # preserve Inherit="False" from original if present on abstract
                        inherit = self.extract_litter_inherit(original_root, abstract, is_abstract=True)
                        ops.append(self.create_litter_curve_replace(abstract, points, is_abstract=True, inherit_false=inherit))
                else:
                    ops.append(self.create_safe_remove(abstract, 'race', 'litterSizeCurve', is_abstract=True))
        elif 'Litter size' in differing_cols:
            ops.append(self.create_safe_remove(abstract, 'race', 'litterSizeCurve', is_abstract=True))
            
        # --- ModExtensions for abstract (added only if all children share the value) ---
        # Map TSV column -> CLR class string
        _modext_map = {
            'IsMammal': 'ZoologyMod.ModExtension_IsMammal, ZoologyMod',
            'Ectothermic': 'ZoologyMod.ModExtension_Ectothermic, ZoologyMod',
            'AgroAtSlaughter': 'ZoologyMod.ModExtension_AgroAtSlaughter, ZoologyMod',
            'CannotBeAugmented': 'ZoologyMod.ModExtension_CannotBeAugmented, ZoologyMod',
            'CannotBeMutated': 'ZoologyMod.ModExtension_CannotBeMutated, ZoologyMod',
            'NoFlee': 'ZoologyMod.ModExtension_NoFlee, ZoologyMod',
            'CannotChew': 'ZoologyMod.ModExtension_CannotChew, ZoologyMod',
            'TakingCareOfOffspring': 'ZoologyMod.ModExtensiom_Chlidcare, ZoologyMod',
            # IsScavenger is handled specially (Flesh / Bone)
            'IsScavenger': 'ZoologyMod.ModExtension_IsScavenger, ZoologyMod',
        }

        def _is_truthy(s):
            if s is None:
                return False
            return str(s).strip().lower() in ('true', '1', 'yes', 'y', 't')

        # For each modext column: if present in common_values and non-empty/"true" (or in case of IsScavenger - Flesh/Bone),
        # create PatchOperationAddModExtension on abstract (xpath uses @Name for abstracts).
        for col, class_str in _modext_map.items():
            if col not in common_values:
                continue
            val = common_values.get(col)
            if val is None:
                continue
            s = str(val).strip()
            if s == '' or s.lower() in ('no', 'none'):
                # nothing to add at abstract-level
                continue

            # Special handling for IsScavenger: accept "Flesh" or "Bone" (case-insensitive)
            if col == 'IsScavenger':
                sval = s.lower()
                if sval not in ('flesh', 'bone'):
                    continue
                add_op = LET.Element("Operation", Class="PatchOperationAddModExtension")
                LET.SubElement(add_op, "xpath").text = f'/Defs/ThingDef[@Name = "{abstract}"]'
                val_node = LET.SubElement(add_op, "value")
                li = LET.SubElement(val_node, "li")
                li.set('Class', class_str)
                # Bone -> set allowVeryRotten true; Flesh -> no extra child
                if sval == 'bone':
                    allow = LET.SubElement(li, "allowVeryRotten")
                    allow.text = 'true'
                ops.append(add_op)
                continue

            # For boolean-like columns (IsMammal, Ectothermic, etc.)
            # Accept typical truthy strings; if truthy -> add mod extension
            if _is_truthy(s):
                add_op = LET.Element("Operation", Class="PatchOperationAddModExtension")
                LET.SubElement(add_op, "xpath").text = f'/Defs/ThingDef[@Name = \"{abstract}\"]'
                val_node = LET.SubElement(add_op, "value")
                li = LET.SubElement(val_node, "li")
                li.set('Class', class_str)
                ops.append(add_op)
            else:
                # not truthy (e.g. some textual marker) -> skip for abstract
                pass

        return ops

    def generate_def_patches(self, def_name, row, original_root, parent, parent_common_map=None, pawn_parent_common_map=None):
        """
        parent_common_map: dict mapping abstract parent -> {'common_values':..., 'differing_cols':..., 'tool_signature':...}
        pawn_parent_common_map: dict mapping pawn parent -> {'common_values':..., 'differing_cols':..., 'children':...}
        If a parent exists and a field for child equals parent's common value, we skip generating that child's op.
        If a pawn parent exists and the pawn parent has common values for pawn fields, we create safe-remove
        operations for the child (so the parent holds the value), otherwise child will create its own patch.
        """
        ops = []
        ops.append(LET.Comment(""))
        ops.append(LET.Comment(f" {def_name} "))

        # Parent safety: force ParentName from table before other vanilla patches.
        # If parent columns are None/empty, use AnimalThingBase / AnimalKindBase.
        thing_parent = ''
        if 'Parrent abstract' in row.index:
            thing_parent = str(row.get('Parrent abstract', '')).strip()
        elif 'Parent abstract' in row.index:
            thing_parent = str(row.get('Parent abstract', '')).strip()
        if thing_parent.lower() in ('', 'none', 'no'):
            thing_parent = 'AnimalThingBase'
        ops.append(self.create_attribute_set(def_name, 'ParentName', thing_parent, is_pawn=False))

        pawn_parent_forced = ''
        if 'Parrent Pawn kind abstract' in row.index:
            pawn_parent_forced = str(row.get('Parrent Pawn kind abstract', '')).strip()
        elif 'Parent Pawn kind abstract' in row.index:
            pawn_parent_forced = str(row.get('Parent Pawn kind abstract', '')).strip()
        elif 'Parrent Pawn kind' in row.index:
            pawn_parent_forced = str(row.get('Parrent Pawn kind', '')).strip()
        if pawn_parent_forced.lower() in ('', 'none', 'no'):
            pawn_parent_forced = 'AnimalKindBase'
        ops.append(self.create_attribute_set(def_name, 'ParentName', pawn_parent_forced, is_pawn=True))

        # StatBases
        ops.append(LET.Comment(f" {def_name} StatBases "))
        if parent:
            ops.append(self.create_ensure_container(def_name, 'statBases'))
        stat_order = ['MarketValue', 'MoveSpeed', 'Wildness', 'FilthRate', 'ComfyTemperatureMin', 'ComfyTemperatureMax', 'ArmorRating_Blunt', 'ArmorRating_Sharp']
        for stat in stat_order:
            # Если родитель явно владеет этим статом (вычислено заранее по TSV для ВСЕХ детей) -
            # то безопасно удаляем переопределения у ребёнка (на случай локальных override в оригинальном XML).
            if parent and parent_common_map and parent in parent_common_map:
                parent_common = parent_common_map[parent].get('common_values', {})
                # проверяем несколько вариантов ключей (колонка/тэг/регистронезависимо)
                if (stat in parent_common) or any(k.lower() == stat.lower() for k in parent_common.keys()):
                    ops.append(self.create_safe_remove(def_name, 'statBases', stat))
                    continue

            value = get_row_value(row, stat)
            if value is None or str(value).strip() == '':
                continue
            # NEW: если метки-удаления -> safe_remove
            sval = '' if value is None else str(value).strip()
            if sval.lower() in ('', 'no', 'none'):
                ops.append(self.create_safe_remove(def_name, 'statBases', stat))
            else:
                ops.append(self.create_safe_replace(def_name, 'statBases', stat, value))

        # ToxicEnvironmentResistance
        toxic = get_row_value(row, 'ToxicEnvironmentResistance')
        # Если родитель владеет этим полем по TSV — создаём safe-remove у ребёнка
        if parent and parent_common_map and parent in parent_common_map:
            parent_common = parent_common_map[parent].get('common_values', {})
            if 'ToxicEnvironmentResistance' in parent_common:
                ops.append(self.create_safe_remove(def_name, 'statBases', 'ToxicEnvironmentResistance'))
                toxic = None  # не обрабатываем значение у ребёнка дальше

        if toxic is not None and str(toxic).strip() != '':
            if str(toxic).strip().lower() == 'standard':
                ops.append(self.create_safe_remove(def_name, 'statBases', 'ToxicEnvironmentResistance'))
            else:
                ops.append(self.create_safe_replace(def_name, 'statBases', 'ToxicEnvironmentResistance', toxic))

        # LeatherAmount (statBases LeatherAmount = 0) — после ToxicEnvironmentResistance
        # Если в TSV LeatherDef == 'No' -> добавляем statBases LeatherAmount = 0.
        # Если LeatherDef != 'No' или пусто -> ничего не делаем по этому поводу.
        leather_def = get_row_value(row, 'LeatherDef')
        if leather_def is not None and str(leather_def).strip().lower() == 'no':
            # skip if parent already indicates no leather in race common values
            if parent and parent_common_map and parent in parent_common_map:
                parent_common = parent_common_map[parent]['common_values']
                pval = parent_common.get('LeatherDef') or parent_common.get('LeatherDef')
                if pval is not None and str(pval).strip().lower() == 'no':
                    pass  # parent already indicates no leather, skip
                else:
                    ops.append(self.create_safe_replace(def_name, 'statBases', 'LeatherAmount', '0'))
            else:
                ops.append(self.create_safe_replace(def_name, 'statBases', 'LeatherAmount', '0'))

        # tools
        ops.append(LET.Comment(f" {def_name} tools "))

        # Compute child's tool signature and parent's signature (if any)
        child_tool_sig = tuple(norm(get_row_value(row, col)) for col in ['Head damage', 'Poke/leg claws damage', 'Horn/Antler/Tusks damage', 'Bite damage', 'Paw claw/punch damage'])
        parent_tool_sig = None
        if parent and parent_common_map and parent in parent_common_map:
            parent_tool_sig = parent_common_map[parent]['tool_signature']

        # If parent covers tools and signatures equal -> skip child's tools entirely
        if parent_tool_sig is not None and child_tool_sig == parent_tool_sig:
            pass
        else:
            # Build vanilla tools from TSV for this child
            built = self.build_tools_vanilla(row)

            # Always emit a safe conditional REMOVE for existing tools (if any).
            # This mirrors CE behaviour: conditional remove then add.
            remove_op = LET.Element("Operation", Class="PatchOperationConditional")
            LET.SubElement(remove_op, "xpath").text = f"/Defs/ThingDef[defName = \"{def_name}\"]/tools"
            match_node = LET.SubElement(remove_op, "match", Class="PatchOperationRemove")
            LET.SubElement(match_node, "xpath").text = f"/Defs/ThingDef[defName = \"{def_name}\"]/tools"
            ops.append(remove_op)

            if built is not None:
                # preserve Inherit="False" from original if present
                inherit = self.extract_tools_inherit(original_root, def_name, is_abstract=False)
                built_copy = copy.deepcopy(built)
                if inherit:
                    built_copy.set('Inherit', 'False')
                # Add built tools under the ThingDef (even if remove did nothing, add will create/replace)
                add_op = LET.Element("Operation", Class="PatchOperationAdd")
                LET.SubElement(add_op, "xpath").text = f"/Defs/ThingDef[defName = \"{def_name}\"]"
                val_node = LET.SubElement(add_op, "value")
                val_node.append(built_copy)
                ops.append(add_op)
            else:
                # TSV indicates no tools -> we already emitted remove_op so tools will be removed.
                # (No add operation in this case.)
                pass

        # RaceProperties
        ops.append(LET.Comment(f" {def_name} RaceProperties "))
        if parent:
            ops.append(self.create_ensure_container(def_name, 'race'))
        race_mapping = {
            'baseBodySize': 'baseBodySize',
            'baseHealthScale': 'baseHealthScale',
            'baseHungerRate': 'baseHungerRate',
            'lifeExpectancy': 'Lifespan (years)',
            'gestationPeriodDays': 'Gestation period (days)',
            'herdAnimal': 'herdAnimal',
            'herdMigrationAllowed': 'herdMigrationAllowed',
            'foodType': 'Foodtype',
            'manhunterOnTameFailChance': 'manhunterOnTameFailChance',
            'manhunterOnDamageChance': 'manhunterOnDamageChance',
            'petness': 'petness',
            'nuzzleMtbHours': 'nuzzleMtbHours',
            'mateMtbHours': 'mateMtbHours',
            'trainability': 'trainability',
            'predator': 'predator',
            'maxPreyBodySize': 'maxPreyBodySize',
            'nameOnTameChance': 'nameOnTameChance',
            'Body': 'Body',
            'roamMtbDays': 'roamMtbDays',
            'LeatherDef': 'LeatherDef',
            'waterCellCost': 'waterCellCost',
            'waterSeeker': 'waterSeeker',
            'canFishForFood': 'canFishForFood',
        }
        race_order = ['baseBodySize', 'baseHealthScale', 'baseHungerRate', 'lifeExpectancy', 'gestationPeriodDays',
                      'herdAnimal', 'herdMigrationAllowed', 'foodType', 'roamMtbDays', 'LeatherDef',
                      'manhunterOnTameFailChance', 'manhunterOnDamageChance',
                      'petness', 'nuzzleMtbHours', 'mateMtbHours', 'trainability', 'packAnimal', 'predator', 'maxPreyBodySize', 'nameOnTameChance', 'Body', 'waterCellCost', 'waterSeeker', 'canFishForFood']

        for tag in race_order:
            col = race_mapping.get(tag, tag)

            # compute XML-safe field name (lowerCamelCase)
            xml_tag = tag if tag and tag[0].islower() else (tag[0].lower() + tag[1:] if tag else tag)

            # If parent_common_map states that parent owns this field -> child must remove it (safety).
            if parent and parent_common_map and parent in parent_common_map:
                parent_common = parent_common_map[parent].get('common_values', {})
                # проверяем несколько вариантов ключей (col, tag, xml_tag, регистронезависимо)
                if (col in parent_common) or (tag in parent_common) or (xml_tag in parent_common) or any(k.lower() == col.lower() for k in parent_common.keys()):
                    ops.append(self.create_safe_remove(def_name, 'race', xml_tag))
                    if tag == 'LeatherDef':
                        ops.append(self.create_safe_remove(def_name, 'race', 'useLeatherFrom'))
                    continue

            # special handling: LeatherDef comes from column 'LeatherDef'
            if tag == 'LeatherDef':
                value = get_row_value(row, 'LeatherDef')
                if value is None or str(value).strip() == '':
                    continue
                sval = str(value).strip()
                # If LeatherDef == 'No'/'None' -> remove; otherwise replace
                if sval.lower() in ('', 'no', 'none'):
                    ops.append(self.create_safe_remove(def_name, 'race', 'leatherDef'))
                else:
                    ops.append(self.create_safe_replace(def_name, 'race', 'leatherDef', sval))
                # Всегда удаляем useLeatherFrom после обработки LeatherDef
                ops.append(self.create_safe_remove(def_name, 'race', 'useLeatherFrom'))
                continue

            # generic handlers
            value = get_row_value(row, col)
            if value is None or str(value).strip() == '':
                continue
            s = str(value).strip()

            # universal "remove" markers -> safe remove
            if s.lower() in ('', 'no', 'none'):
                ops.append(self.create_safe_remove(def_name, 'race', xml_tag))
                continue

            # special: gestation -> textual markers like "Lay eggs" mean remove
            if xml_tag == 'gestationPeriodDays':
                if re.search(r'\blay\s*egg(s)?\b', s, flags=re.I) or (re.search(r'\begg(s)?\b', s, flags=re.I) and try_parse_number(s) is None):
                    ops.append(self.create_safe_remove(def_name, 'race', xml_tag))
                    continue
                # numeric gestation -> replace
                num = try_parse_number(s)
                if num is not None:
                    ops.append(self.create_safe_replace(def_name, 'race', xml_tag, s))
                else:
                    ops.append(self.create_safe_remove(def_name, 'race', xml_tag))
                continue

            # waterCellCost
            if xml_tag == 'waterCellCost':
                if s.lower() in ('', 'no', 'none'):
                    ops.append(self.create_safe_remove(def_name, 'race', 'waterCellCost'))
                else:
                    ops.append(self.create_safe_replace(def_name, 'race', 'waterCellCost', s))
                continue

            # waterSeeker (boolean)
            if xml_tag == 'waterSeeker':
                val = 'true' if s.lower() in ('true', '1', 'yes', 'y', 't') else 'false'
                ops.append(self.create_safe_replace(def_name, 'race', 'waterSeeker', val))
                continue

            # canFishForFood (boolean)
            if xml_tag == 'canFishForFood':
                val = 'true' if s.lower() in ('true', '1', 'yes', 'y', 't') else 'false'
                ops.append(self.create_safe_replace(def_name, 'race', 'canFishForFood', val))
                continue

            # packAnimal (boolean)
            if xml_tag == 'packAnimal':
                val = 'true' if s.lower() in ('true', '1', 'yes', 'y', 't') else 'false'
                ops.append(self.create_safe_replace(def_name, 'race', 'packAnimal', val))
                continue

            # probability-like fields (normalize)
            if xml_tag in ('manhunterOnTameFailChance', 'manhunterOnDamageChance'):
                formatted = format_prob_value(s)
                ops.append(self.create_safe_replace(def_name, 'race', xml_tag, formatted))
            else:
                ops.append(self.create_safe_replace(def_name, 'race', xml_tag, s))

        # race/wildBiomes:
        # - default: always safely remove
        # - when ModConflict is set: safely replace/add from table WildBiomes + Eco system number
        mod_conflicts = self._split_mod_list(get_row_value(row, 'ModConflict'))
        wild_biomes = self._split_biomes(get_row_value(row, 'WildBiomes'))
        eco_number = get_row_value(row, 'Eco system number')
        if mod_conflicts and wild_biomes and not self._is_no_like(eco_number):
            wild_biomes_node = self._build_wild_biomes_element(wild_biomes, eco_number)
            if wild_biomes_node is not None:
                ops.append(self.create_safe_replace(def_name, 'race', 'wildBiomes', wild_biomes_node))
            else:
                ops.append(self.create_safe_remove(def_name, 'race', 'wildBiomes'))
        else:
            ops.append(self.create_safe_remove(def_name, 'race', 'wildBiomes'))

        # lifeStageAges handling
        # читаем из строки и нормализуем: 'No' -> None
        juv_age = self._age_value_or_none(get_row_value(row, 'Juv age (years)'))
        adult_age = self._age_value_or_none(get_row_value(row, 'Adult age (years)') or get_row_value(row, 'Adult age'))

        # Determine whether parent *owns* both life-stage ages (so child must remove the whole lifeStageAges block).
        parent_overrode_both_lifestages = False
        if parent and parent_common_map and parent in parent_common_map:
            p_common = parent_common_map[parent].get('common_values', {})
            # parent_common may store column names as in TSV: check variations
            p_has_juv = self._age_value_valid(p_common.get('Juv age (years)'))
            p_has_adult = False
            # Adult age may be stored under 'Adult age (years)' or 'Adult age'
            if ('Adult age (years)' in p_common and self._age_value_valid(p_common.get('Adult age (years)'))) or \
               ('Adult age' in p_common and self._age_value_valid(p_common.get('Adult age'))):
                p_has_adult = True

            if p_has_juv and p_has_adult:
                parent_overrode_both_lifestages = True

        # We want to run lifeStage logic either if child provides ages OR parent_overrode_both_lifestages
        if parent_overrode_both_lifestages or (juv_age is not None and str(juv_age).strip() != '') or (adult_age is not None and str(adult_age).strip() != ''):
            # If parent overrode both ages (either by replacing whole block or by replacing both minAges),
            # then child should NOT create separate li/minAge replacements — instead we create a single safe-remove
            # for the entire lifeStageAges node (so the parent node will be authoritative).
            if parent_overrode_both_lifestages:
                ops.append(self.create_safe_remove(def_name, 'race', 'lifeStageAges'))
            else:
                # try to extract sounds from original XML (adult sounds) to preserve them when creating the node
                sounds, inherit = self.extract_sounds(original_root, def_name)
                # if not found for concrete def, and there's a parent abstract, try parent (parent likely @Name)
                if (not sounds) and parent:
                    sounds, inherit = self.extract_sounds(original_root, parent, is_abstract=True)

                # defaults for creation
                juv_text = str(juv_age).strip() if juv_age is not None and str(juv_age).strip() != '' else "0.2"
                adult_text = str(adult_age).strip() if adult_age is not None and str(adult_age).strip() != '' else "0.5"

                if sounds:
                    # передаём parent — чтобы create_life_stage_full_replace_or_add могла маппить дефы для BaseInsect
                    ops.append(self.create_life_stage_full_replace_or_add(def_name, juv_text, adult_text, sounds=sounds, parent=parent, inherit_false=inherit))
                    # не добавляем отдельные minAge replace операции — они избыточны
                else:
                    # при отсутствии звуков — создаём отдельные minAge замены, маппя имена через _map_lifestage_def
                    juv_def = self._map_lifestage_def('AnimalJuvenile', parent)
                    adult_def = self._map_lifestage_def('AnimalAdult', parent)
                    if juv_age is not None and str(juv_age).strip() != '':
                        ops.append(self.create_life_stage_replace(def_name, juv_def, juv_age))
                    if adult_age is not None and str(adult_age).strip() != '':
                        ops.append(self.create_life_stage_replace(def_name, adult_def, adult_age))

        # litterSizeCurve
        litter = get_row_value(row, 'Litter size')
        if litter is not None and str(litter).strip() != '':
            # if parent has same value -> skip
            skip_litter = False
            if parent and parent_common_map and parent in parent_common_map:
                p_common = parent_common_map[parent]['common_values']
                pval = p_common.get('Litter size')
                if pval is not None and norm(pval) == norm(litter):
                    skip_litter = True
            if skip_litter:
                pass
            else:
                s = str(litter).strip()
                if s in ['1', '1.00', '1.0']:
                    ops.append(self.create_safe_remove(def_name, 'race', 'litterSizeCurve'))
                else:
                    mean = try_parse_number(s)
                    if mean is None and '~' in s:
                        parts = s.split('~')
                        try:
                            mi = try_parse_number(parts[0])
                            ma = try_parse_number(parts[1])
                            if mi is not None and ma is not None:
                                mean = (mi + ma) / 2.0
                        except Exception:
                            mean = None
                    if mean and mean > 1:
                        points = generate_litter_curve(mean)
                        if points:
                            # preserve Inherit="False" from original if present on concrete def
                            inherit = self.extract_litter_inherit(original_root, def_name, is_abstract=False)
                            ops.append(self.create_litter_curve_replace(def_name, points, is_abstract=False, inherit_false=inherit))
                    
        # SpecialTrainables
        special_str = get_row_value(row, 'specialTrainables')
        # Если родитель (abstract) уже обеспечил specialTrainables -> child должен дать safe-remove
        if parent and parent_common_map and parent in parent_common_map:
            parent_common = parent_common_map[parent]['common_values']
            if 'specialTrainables' in parent_common:
                # parent owns the field -> create remove for child so parent remains authoritative
                ops.append(self.create_safe_remove(def_name, 'race', 'specialTrainables'))
            else:
                # parent не владеет — обрабатываем значение у ребёнка как обычно ниже
                if special_str is not None and str(special_str).strip() != '':
                    s = str(special_str).strip()
                    if s.lower() in ('no', 'none', ''):
                        ops.append(self.create_safe_remove(def_name, 'race', 'specialTrainables'))
                    else:
                        items = [t.strip() for t in re.split(r',|\n', s) if t.strip()]
                        if items:
                            ops.extend(self.create_special_trainables_patch(def_name, items, is_abstract=False))
        else:
            # нет родителя/информации — обрабатываем напрямую
            if special_str is not None and str(special_str).strip() != '':
                s = str(special_str).strip()
                if s.lower() in ('no', 'none', ''):
                    ops.append(self.create_safe_remove(def_name, 'race', 'specialTrainables'))
                else:
                    items = [t.strip() for t in re.split(r',|\n', s) if t.strip()]
                    if items:
                        ops.extend(self.create_special_trainables_patch(def_name, items, is_abstract=False))
                        
        # TradeTags
        trade_str = get_row_value(row, 'TradeTags')
        # Если у родителя одинаковые TradeTags, то родитель уже создал add и child должен получить remove из абстракта.
        skip_tradetags_for_child = False
        if parent and parent_common_map and parent in parent_common_map:
            parent_common = parent_common_map[parent]['common_values']
            if 'TradeTags' in parent_common:
                skip_tradetags_for_child = True

        if skip_tradetags_for_child:
            # Parent provided trade tags -> create safe-remove for this child now (so removal happens
            # at the correct place when child's ops are generated, and only for children that are present)
            ops.append(self.create_safe_remove(def_name, '', 'tradeTags'))
        else:
            if trade_str is not None and str(trade_str).strip() != '':
                tags = [t.strip() for t in re.split(r',|\n', str(trade_str)) if t.strip()]
                if tags:
                    # попробуем сначала найти Inherit на самом child
                    inherit = self.extract_trade_tags_inherit(original_root, def_name, is_abstract=False)
                    # если не нашли — возможно Inherit задан в абстрактном определении родителя — проверить parent (fallback)
                    if not inherit and parent:
                        inherit = self.extract_trade_tags_inherit(original_root, parent, is_abstract=True)

                    ops.extend(self.create_trade_tags_patch(def_name, tags, inherit=inherit))
                    
        # --- ModExtensions for concrete def (add to child unless parent already owns it) ---
        _modext_map_def = {
            'IsMammal': 'ZoologyMod.ModExtension_IsMammal, ZoologyMod',
            'Ectothermic': 'ZoologyMod.ModExtension_Ectothermic, ZoologyMod',
            'AgroAtSlaughter': 'ZoologyMod.ModExtension_AgroAtSlaughter, ZoologyMod',
            'CannotBeAugmented': 'ZoologyMod.ModExtension_CannotBeAugmented, ZoologyMod',
            'CannotBeMutated': 'ZoologyMod.ModExtension_CannotBeMutated, ZoologyMod',
            'NoFlee': 'ZoologyMod.ModExtension_NoFlee, ZoologyMod',
            'CannotChew': 'ZoologyMod.ModExtension_CannotChew, ZoologyMod',
            'TakingCareOfOffspring': 'ZoologyMod.ModExtensiom_Chlidcare, ZoologyMod',
            'IsScavenger': 'ZoologyMod.ModExtension_IsScavenger, ZoologyMod',
        }

        def _is_truthy_def(s):
            if s is None:
                return False
            return str(s).strip().lower() in ('true', '1', 'yes', 'y', 't')

        # get parent common_values if available
        parent_common = None
        if parent and parent_common_map and parent in parent_common_map:
            parent_common = parent_common_map[parent].get('common_values', {})

        for col, class_str in _modext_map_def.items():
            # If parent defines same column for abstract (parent owns it), skip adding on child.
            if parent_common is not None and col in parent_common and parent_common.get(col) not in (None, '', 'No', 'None'):
                # parent already got an add => child should not duplicate (parent is authoritative)
                continue

            # Get child's raw TSV value
            child_val = get_row_value(row, col)
            if child_val is None:
                continue
            s = str(child_val).strip()
            if s == '' or s.lower() in ('no', 'none'):
                continue

            # Special: IsScavenger values Flesh/Bone
            if col == 'IsScavenger':
                sval = s.lower()
                if sval not in ('flesh', 'bone'):
                    continue
                add_op = LET.Element("Operation", Class="PatchOperationAddModExtension")
                # concrete defs use defName attribute in your codebase
                LET.SubElement(add_op, "xpath").text = f'/Defs/ThingDef[defName = \"{def_name}\"]'
                val_node = LET.SubElement(add_op, "value")
                li = LET.SubElement(val_node, "li")
                li.set('Class', class_str)
                if sval == 'bone':
                    allow = LET.SubElement(li, "allowVeryRotten")
                    allow.text = 'true'
                ops.append(add_op)
                continue

            # For boolean-like columns:
            if _is_truthy_def(s):
                add_op = LET.Element("Operation", Class="PatchOperationAddModExtension")
                LET.SubElement(add_op, "xpath").text = f'/Defs/ThingDef[defName = \"{def_name}\"]'
                val_node = LET.SubElement(add_op, "value")
                li = LET.SubElement(val_node, "li")
                li.set('Class', class_str)
                ops.append(add_op)
            else:
                # not truthy -> skip
                continue

        # PawnKindDef
        ops.append(LET.Comment(f" {def_name} PawnKindDef "))

        # Determine pawn parent for this child (if any)
        pawn_parent = ''
        if 'Parrent Pawn kind abstract' in row.index:
            pawn_parent = str(row.get('Parrent Pawn kind abstract', '')).strip()
        elif 'Parent Pawn kind abstract' in row.index:
            pawn_parent = str(row.get('Parent Pawn kind abstract', '')).strip()
        elif 'Parrent Pawn kind' in row.index:
            pawn_parent = str(row.get('Parrent Pawn kind', '')).strip()
        if pawn_parent == 'None':
            pawn_parent = ''

        # Pawn items: if pawn_parent has common value -> child's field should be removed (parent owns it)
        pawn_items = [
            ('combatPower', 'Combat power'),
            ('ecoSystemWeight', 'ecoSystemWeight'),
            ('canArriveManhunter', 'CanArriveManhunter'),
        ]
        for tag, col in pawn_items:
            value = get_row_value(row, col)
            # if pawn_parent defines this field commonly, create remove on child
            if pawn_parent and pawn_parent_common_map and pawn_parent in pawn_parent_common_map:
                pawn_parent_common = pawn_parent_common_map[pawn_parent]['common_values']
                if col in pawn_parent_common:
                    # parent owns this field -> remove from child
                    ops.append(self.create_safe_remove(def_name, '', tag, is_pawn=True))
                    continue
            # else handle child as usual
            if value is not None and str(value).strip() != '':
                if str(value).lower() == 'no':
                    ops.append(self.create_safe_remove(def_name, '', tag, is_pawn=True))
                else:
                    ops.append(self.create_safe_replace(def_name, '', tag, value, is_pawn=True))

        # wildGroupSize — текст формата "3~8" — как PawnKindDef.wildGroupSize
        wild = get_row_value(row, 'Wild group size') or get_row_value(row, 'Wild group size')
        # if pawn_parent common has wild group size -> remove it from child
        if pawn_parent and pawn_parent_common_map and pawn_parent in pawn_parent_common_map and 'Wild group size' in pawn_parent_common_map[pawn_parent]['common_values']:
            ops.append(self.create_safe_remove(def_name, '', 'wildGroupSize', is_pawn=True))
        else:
            if wild is not None and str(wild).strip() != '':
                if str(wild).strip().lower() == 'no':
                    ops.append(self.create_safe_remove(def_name, '', 'wildGroupSize', is_pawn=True))
                else:
                    el = LET.Element('wildGroupSize')
                    el.text = str(wild).strip()
                    ops.append(self.create_safe_replace(def_name, '', 'wildGroupSize', el, is_pawn=True))
                    
        # moveSpeedFactorByTerrainTag per-child handling
        # if pawn_parent defines parent-common -> child must remove it (parent owns it)
        if pawn_parent and pawn_parent_common_map and pawn_parent in pawn_parent_common_map and 'moveSpeedFactorByTerrainTag' in pawn_parent_common_map[pawn_parent].get('common_values', {}):
            ops.append(self.create_safe_remove(def_name, 'moveSpeedFactorByTerrainTag', "li[key = 'Water']", is_pawn=True))
        else:
            # child's own value (from TSV)
            mv = get_row_value(row, 'moveSpeedFactorByTerrainTag') or get_row_value(row, 'moveSpeedFactorByTerrainTag (water)')
            if mv is not None and str(mv).strip() != '':
                s = str(mv).strip()
                if s.lower() in ('', 'no', 'none'):
                    ops.append(self.create_safe_remove(def_name, 'moveSpeedFactorByTerrainTag', "li[key = 'Water']", is_pawn=True))
                else:
                    # ensure container and set/replace Water entry
                    ops.append(self.create_ensure_container(def_name, 'moveSpeedFactorByTerrainTag', is_pawn=True))
                    ops.append(self.create_moveSpeedFactorByTerrainTag_patch(def_name, s, is_pawn=True))

        return ops

    # ==================== Операции ====================
    def create_moveSpeedFactorByTerrainTag_patch(self, def_name, value, is_pawn=False, is_abstract=False):
        """
        Создаёт один Operation (PatchOperationConditional) который:
          - заменяет /moveSpeedFactorByTerrainTag/li[key = 'Water']/value
          - или добавляет li/key/value под moveSpeedFactorByTerrainTag, если контейнера нет
        Параметры:
          def_name - defName / @Name значения
          value - числовое или строковое значение для <value>
          is_pawn - если True, то базовый путь PawnKindDef, иначе ThingDef (обычно True)
          is_abstract - если True, то атрибут @Name вместо defName
        Возвращает LET.Element (одну Operation).
        """
        base_path = 'PawnKindDef' if is_pawn else 'ThingDef'
        attr = '@Name' if is_abstract else 'defName'

        op = LET.Element("Operation", Class="PatchOperationConditional")
        # xpath that checks existence of the li node (we use it as selector)
        xpath = LET.SubElement(op, "xpath")
        xpath.text = f"/Defs/{base_path}[{attr} = \"{def_name}\"]/moveSpeedFactorByTerrainTag/li[key = 'Water']"

        # match -> replace the inner <value>
        match = LET.SubElement(op, "match", Class="PatchOperationReplace")
        LET.SubElement(match, "xpath").text = f"/Defs/{base_path}[{attr} = \"{def_name}\"]/moveSpeedFactorByTerrainTag/li[key = 'Water']/value"
        mval = LET.SubElement(match, "value")
        # construct <value>value</value> (nested element as sample)
        val_el = LET.Element('value')
        val_el.text = str(value)
        mval.append(val_el)

        # nomatch -> add li under moveSpeedFactorByTerrainTag
        nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
        LET.SubElement(nomatch, "xpath").text = f"/Defs/{base_path}[{attr} = \"{def_name}\"]/moveSpeedFactorByTerrainTag"
        nval = LET.SubElement(nomatch, "value")
        li = LET.SubElement(nval, "li")
        LET.SubElement(li, "key").text = "Water"
        LET.SubElement(li, "value").text = str(value)

        return op
    
    def create_ensure_container(self, def_name, container_tag, is_pawn=False, is_abstract=False):
        base_path = 'PawnKindDef' if is_pawn else 'ThingDef'
        attr = '@Name' if is_abstract else 'defName'

        op = LET.Element("Operation", Class="PatchOperationConditional")
        xpath = LET.SubElement(op, "xpath")
        xpath.text = f"/Defs/{base_path}[{attr} = \"{def_name}\"]/{container_tag}"

        nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
        LET.SubElement(nomatch, "xpath").text = f"/Defs/{base_path}[{attr} = \"{def_name}\"]"
        val = LET.SubElement(nomatch, "value")
        LET.SubElement(val, container_tag)

        return op

    def create_attribute_set(self, def_name, attribute, value, is_pawn=False, is_abstract=False):
        base_path = 'PawnKindDef' if is_pawn else 'ThingDef'
        attr = '@Name' if is_abstract else 'defName'
        op = LET.Element("Operation", Class="PatchOperationAttributeSet")
        LET.SubElement(op, "xpath").text = f"/Defs/{base_path}[{attr} = \"{def_name}\"]"
        LET.SubElement(op, "attribute").text = str(attribute)
        LET.SubElement(op, "value").text = str(value)
        return op
    
    def create_safe_replace(self, def_name, path, tag, value, is_abstract=False, is_pawn=False):
        """
        Create Operation PatchOperationConditional with match (replace) and nomatch (add).
        If value is an Element, append deepcopy for match and deepcopy for nomatch to avoid lxml moving node.
        """
        base_path = 'PawnKindDef' if is_pawn else 'ThingDef'
        attr = '@Name' if is_abstract else 'defName'
        op = LET.Element("Operation", Class="PatchOperationConditional")
        xpath = LET.SubElement(op, "xpath")
        full_path = f"/Defs/{base_path}[{attr} = \"{def_name}\"]/{path}/{tag}" if path else f"/Defs/{base_path}[{attr} = \"{def_name}\"]/{tag}"
        xpath.text = full_path

        # match block
        match = LET.SubElement(op, "match", Class="PatchOperationReplace")
        LET.SubElement(match, "xpath").text = full_path
        mval = LET.SubElement(match, "value")
        if isinstance(value, LET._Element):
            mval.append(copy.deepcopy(value))
        else:
            LET.SubElement(mval, tag).text = str(value)

        # nomatch block (add)
        nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
        add_path = f"/Defs/{base_path}[{attr} = \"{def_name}\"]/{path}" if path else f"/Defs/{base_path}[{attr} = \"{def_name}\"]"
        LET.SubElement(nomatch, "xpath").text = add_path
        nval = LET.SubElement(nomatch, "value")
        if isinstance(value, LET._Element):
            nval.append(copy.deepcopy(value))
        else:
            LET.SubElement(nval, tag).text = str(value)

        return op

    def create_safe_remove(self, def_name, path, tag, is_abstract=False, is_pawn=False):
        base_path = 'PawnKindDef' if is_pawn else 'ThingDef'
        attr = '@Name' if is_abstract else 'defName'
        op = LET.Element("Operation", Class="PatchOperationConditional")
        xpath = LET.SubElement(op, "xpath")
        full_path = f"/Defs/{base_path}[{attr} = \"{def_name}\"]/{path}/{tag}" if path else f"/Defs/{base_path}[{attr} = \"{def_name}\"]/{tag}"
        xpath.text = full_path
        match = LET.SubElement(op, "match", Class="PatchOperationRemove")
        LET.SubElement(match, "xpath").text = full_path
        return op
        
    def extract_sounds(self, original_root, def_name, is_abstract=False):
        """
        Возвращает (sounds_dict, inherit_false_flag).
        sounds_dict содержит ключи soundWounded, soundDeath, soundCall, soundAngry (если есть)
        для <ThingDef[defName|@Name=def_name]/race/lifeStageAges/li[def="AnimalAdult"]>.
        Ищем сначала реальные ThingDef, потом — операции (match/nomatch -> value -> lifeStageAges).
        """
        sounds = {}
        inherit_false = False

        # 1) Попробуем найти реальные ThingDef элементы (обычный случай)
        try:
            if is_abstract:
                candidates = original_root.xpath(f"//ThingDef[@Name='{def_name}']")
            else:
                candidates = original_root.xpath(f"//ThingDef[@defName='{def_name}']")
                if not candidates:
                    candidates = original_root.xpath(f"//ThingDef[@Name='{def_name}']")
        except Exception:
            candidates = []

        # Проверяем реальные ThingDef
        for td in candidates:
            race = td.find('race')
            if race is None:
                continue
            lsa = race.find('lifeStageAges')
            if lsa is None:
                continue
            # запомним флаг Inherit, если он явно False
            if lsa.get('Inherit') == 'False':
                inherit_false = True
            for li in lsa.findall('li'):
                d = li.find('def')
                if d is None or d.text is None:
                    continue
                if d.text.strip() == 'AnimalAdult':
                    for tag in ('soundWounded', 'soundDeath', 'soundCall', 'soundAngry'):
                        el = li.find(tag)
                        if el is not None and el.text and el.text.strip():
                            sounds[tag] = el.text.strip()
                    if sounds:
                        return sounds, inherit_false

        # 2) Если не найдено — пробуем искать внутри Operation (match/nomatch -> value -> lifeStageAges)
        for op in original_root.findall(".//Operation"):
            # проверяем все xpath внутрях Operation — может быть несколько xpath элементов
            found_relevant = False
            for xp in op.findall("xpath"):
                txt = (xp.text or '').strip()
                if not txt:
                    continue
                # найти ThingDef[...] с defName или @Name внутри текста xpath
                m = re.search(r'ThingDef\s*\[\s*(?:defName|@Name)\s*=\s*"([^"]+)"\s*\]', txt)
                if not m:
                    m = re.search(r'ThingDef\s*\[\s*(?:defName|@Name)\s*=\s*"([^"]+)"\s*\]', txt.replace(" ", " "))
                if not m:
                    continue
                name_in_xpath = m.group(1)
                if name_in_xpath != def_name:
                    continue
                found_relevant = True
                break
            if not found_relevant:
                continue

            # найден Operation, относящаяся к нашему ThingDef:
            # проверяем match и nomatch блоки — ищем value -> lifeStageAges
            for blk_name in ('match', 'nomatch'):
                blk = op.find(blk_name)
                if blk is None:
                    continue
                # value может быть прямо элементом внутри blk (или глубже), ищем все value узлы
                for val in blk.findall(".//value"):
                    lsa = val.find('lifeStageAges')
                    if lsa is None:
                        continue
                    # запомним Inherit если стоит явно False
                    if lsa.get('Inherit') == 'False':
                        inherit_false = True
                    for li in lsa.findall('li'):
                        d = li.find('def')
                        if d is None or d.text is None:
                            continue
                        if d.text.strip() == 'AnimalAdult':
                            for tag in ('soundWounded', 'soundDeath', 'soundCall', 'soundAngry'):
                                el = li.find(tag)
                                if el is not None and el.text and el.text.strip():
                                    sounds[tag] = el.text.strip()
                            if sounds:
                                return sounds, inherit_false
        return sounds, inherit_false
        
    def extract_trade_tags_inherit(self, original_root, def_name, is_abstract=False):
        """
        Возвращает True, если в оригинальном XML (original_root) для ThingDef(Name/defName=def_name)
        найден узел <tradeTags> с атрибутом Inherit="False".

        Поиск выполняется в нескольких местах:
          1) прямо в ThingDef (child или deeper),
          2) внутри Operation/.../value (например, когда исходник — это патч, добавляющий tradeTags),
          3) общий поиск tradeTags с Inherit="False" и проверка, находится ли он под ThingDef с нужным именем.
        """
        if original_root is None:
            return False

        def _is_inherit_false(node):
            inh = node.get('Inherit')
            return inh is not None and str(inh).strip().lower() == 'false'

        # 1) Проверим прямые ThingDef (Name или defName) и их child tradeTags
        for td in original_root.findall('.//ThingDef'):
            name_val = td.get('Name') or td.get('defName')
            if name_val != def_name:
                continue
            # прямой child
            tt = td.find('tradeTags')
            if tt is not None and _is_inherit_false(tt):
                return True
            # более глубокий поиск внутри ThingDef (на всякий случай)
            for tt2 in td.findall('.//tradeTags'):
                if _is_inherit_false(tt2):
                    return True

        # 2) Поиск внутри Operation (например, Operation/.../value/.../tradeTags Inherit="False")
        for op in original_root.findall('.//Operation'):
            # Если xpath у операции явно ссылается на нужный def — это сильный индикатор
            xpath_el = op.find('xpath')
            xpath_text = xpath_el.text if xpath_el is not None else ''
            op_mentions_def = def_name in xpath_text

            # смотрим внутри value (если есть)
            val = op.find('value')
            if val is not None:
                for tt in val.findall('.//tradeTags'):
                    if _is_inherit_false(tt):
                        # если операция явно относится к нашему дефу — ok, иначе всё равно можно принять (быть толерантным)
                        if op_mentions_def:
                            return True
                        # если op не упоминает def, но в value есть tradeTags Inherit=False, проверим выше/ниже — 
                        # всё равно возвращаем True, т.к. это может быть общий патч для дефов
                        return True

            # дополнительно: иногда операции содержат tradeTags прямо внутри (в match/nomatch)
            for tt in op.findall('.//tradeTags'):
                if _is_inherit_false(tt):
                    if op_mentions_def:
                        return True
                    return True

        # 3) Общий fallback: найдём любые <tradeTags Inherit="False"> и проверим, лежат ли они
        #    под ThingDef с нужным именем (т.е. найти ThingDef, внутри которого этот узел присутствует).
        #    ElementTree не даёт parent напрямую, поэтому будем итеративно обходить ThingDef и искать в их поддереве.
        all_trade_tags = original_root.findall('.//tradeTags')
        if all_trade_tags:
            for tt in all_trade_tags:
                if not _is_inherit_false(tt):
                    continue
                # пробежим все ThingDef — если данный tradeTags встречается в поддереве ThingDef с нашим именем -> True
                for td in original_root.findall('.//ThingDef'):
                    name_val = td.get('Name') or td.get('defName')
                    if name_val != def_name:
                        continue
                    # найдём tt среди элементов td.iter()
                    found = False
                    for el in td.iter():
                        if el is tt:
                            found = True
                            break
                    if found:
                        return True

        return False

    def extract_tools_inherit(self, original_root, def_name, is_abstract=False):
        """
        Robust check for tools Inherit="False" for ThingDef[defName|@Name=def_name].
        Returns True if any matching <tools> has Inherit="False", otherwise False.

        Strategy:
        - Iterate over all <tools> elements in document.
        - If a <tools> has a ThingDef ancestor, compare that ThingDef's defName/@Name with def_name.
        - Otherwise, if <tools> is inside an Operation, find the nearest Operation ancestor and
          inspect its <xpath> children: if any xpath text mentions the def_name, treat this tools
          as belonging to that ThingDef and check Inherit attribute.
        - This is more tolerant to variants in xpath formatting (spacing/quotes) and to tools
          declared inside Operation/value blocks.
        """
        try:
            # iterate all tools elements present in the document
            for tools in original_root.findall('.//tools'):
                # 1) check direct ThingDef ancestor (real ThingDef in file)
                ancestor = tools
                found_td = None
                while ancestor is not None:
                    if ancestor.tag == 'ThingDef':
                        found_td = ancestor
                        break
                    ancestor = ancestor.getparent()  # lxml.etree element has getparent()
                if found_td is not None:
                    # match defName or @Name
                    if (found_td.get('defName') == def_name) or (found_td.get('Name') == def_name):
                        if tools.get('Inherit') == 'False':
                            return True
                        else:
                            # tools exists for this ThingDef but not inherit=false -> continue searching other tools
                            continue

                # 2) not under ThingDef directly -> maybe inside Operation -> value -> tools
                # climb to nearest Operation ancestor
                ancestor = tools
                op_ancestor = None
                while ancestor is not None:
                    if ancestor.tag == 'Operation':
                        op_ancestor = ancestor
                        break
                    ancestor = ancestor.getparent()
                if op_ancestor is not None:
                    # check all <xpath> text nodes under this Operation for mention of the def_name
                    for xp in op_ancestor.findall('.//xpath'):
                        txt = (xp.text or '')
                        if not txt:
                            continue
                        # simple containment test: if def_name appears in xpath text, consider it a match.
                        # This is intentionally permissive to cover variations like defName = "X", @Name='X', spacing differences, etc.
                        if def_name in txt:
                            if tools.get('Inherit') == 'False':
                                return True
                            # else: xpath mentions our def but tools doesn't have inherit=false
                            # continue searching other <tools> instances
                            break
            return False
        except Exception:
            # be conservative on errors: return False (i.e., do not set Inherit unless clearly found)
            return False

    def extract_litter_inherit(self, original_root, def_name, is_abstract=False):
        """
        Robust check for litterSizeCurve Inherit="False" for ThingDef[defName|@Name=def_name].
        Returns True if any matching <litterSizeCurve> has Inherit="False", otherwise False.

        Strategy mirrors extract_tools_inherit:
        - iterate all <litterSizeCurve> elements,
        - if under a real ThingDef ancestor, match defName/@Name,
        - otherwise, if inside an Operation, inspect Operation/xpath texts for def_name mention.
        """
        try:
            for lsc in original_root.findall('.//litterSizeCurve'):
                # 1) check direct ThingDef ancestor
                ancestor = lsc
                found_td = None
                while ancestor is not None:
                    if ancestor.tag == 'ThingDef':
                        found_td = ancestor
                        break
                    ancestor = ancestor.getparent()
                if found_td is not None:
                    if (found_td.get('defName') == def_name) or (found_td.get('Name') == def_name):
                        if lsc.get('Inherit') == 'False':
                            return True
                        else:
                            continue

                # 2) check if inside Operation -> value -> litterSizeCurve
                ancestor = lsc
                op_ancestor = None
                while ancestor is not None:
                    if ancestor.tag == 'Operation':
                        op_ancestor = ancestor
                        break
                    ancestor = ancestor.getparent()
                if op_ancestor is not None:
                    for xp in op_ancestor.findall('.//xpath'):
                        txt = (xp.text or '')
                        if not txt:
                            continue
                        if def_name in txt:
                            if lsc.get('Inherit') == 'False':
                                return True
                            break
            return False
        except Exception:
            return False

    def create_ensure_life_stage_ages(self, def_name, juv_text, adult_text, sounds=None, is_abstract=False, inherit_false=False):
        # build xpath variants depending on abstract or concrete
        attr = '@Name' if is_abstract else 'defName'
        # Condition xpath — наличие /race/lifeStageAges
        cond_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race/lifeStageAges"
        # target xpath for add (we add under /race of the def)
        add_parent_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race"

        op = LET.Element('Operation')
        op.set('Class', 'PatchOperationConditional')
        xp = LET.SubElement(op, 'xpath')
        xp.text = cond_xpath

        # empty <match/> (we only add on nomatch)
        LET.SubElement(op, 'match')

        nomatch = LET.SubElement(op, 'nomatch')
        nomatch.set('Class', 'PatchOperationAdd')

        add_xpath_el = LET.SubElement(nomatch, 'xpath')
        add_xpath_el.text = add_parent_xpath

        value = LET.SubElement(nomatch, 'value')
        lsa = LET.SubElement(value, 'lifeStageAges')
        if inherit_false:
            lsa.set('Inherit', 'False')

        # Baby
        li_baby = LET.SubElement(lsa, 'li')
        d1 = LET.SubElement(li_baby, 'def'); d1.text = 'AnimalBaby'
        m1 = LET.SubElement(li_baby, 'minAge'); m1.text = '0'

        # Juvenile
        li_juv = LET.SubElement(lsa, 'li')
        d2 = LET.SubElement(li_juv, 'def'); d2.text = 'AnimalJuvenile'
        m2 = LET.SubElement(li_juv, 'minAge'); m2.text = str(juv_text)

        # Adult (with optional sounds)
        li_adult = LET.SubElement(lsa, 'li')
        d3 = LET.SubElement(li_adult, 'def'); d3.text = 'AnimalAdult'
        m3 = LET.SubElement(li_adult, 'minAge'); m3.text = str(adult_text)

        # insert sound tags if provided
        if sounds:
            for key in ('soundWounded', 'soundDeath', 'soundCall', 'soundAngry'):
                v = sounds.get(key)
                if v is not None and str(v).strip() != '':
                    el = LET.SubElement(li_adult, key)
                    el.text = str(v)

        return op

    def create_life_stage_replace(self, def_name, stage_def, min_age, sounds=None, is_abstract=False):
        attr = '@Name' if is_abstract else 'defName'
        op = LET.Element("Operation", Class="PatchOperationConditional")
        xpath = LET.SubElement(op, "xpath")
        xpath.text = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race/lifeStageAges/li[def = '{stage_def}']"
        match = LET.SubElement(op, "match", Class="PatchOperationReplace")
        LET.SubElement(match, "xpath").text = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race/lifeStageAges/li[def = '{stage_def}']/minAge"
        LET.SubElement(LET.SubElement(match, "value"), "minAge").text = str(min_age)
        LET.SubElement(op, "nomatch")
        return op

    def create_litter_curve_replace(self, def_name, points, is_abstract=False, inherit_false=False):
        """
        Создаёт PatchOperationConditional:
          - match: заменяет весь узел /race/litterSizeCurve
          - nomatch: добавляет <litterSizeCurve> внутри /race
        Если inherit_false=True, то добавляем атрибут Inherit="False" на создаваемые элементы.
        """
        attr = '@Name' if is_abstract else 'defName'
        base_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race"

        op = LET.Element("Operation", Class="PatchOperationConditional")

        # верхний xpath для операции — указываем на сам узел litterSizeCurve
        xpath = LET.SubElement(op, "xpath")
        xpath.text = base_xpath + "/litterSizeCurve"

        # match: заменяем весь узел litterSizeCurve
        match = LET.SubElement(op, "match", Class="PatchOperationReplace")
        LET.SubElement(match, "xpath").text = base_xpath + "/litterSizeCurve"
        val_match = LET.SubElement(match, "value")
        lsc_match = LET.SubElement(val_match, "litterSizeCurve")
        if inherit_false:
            lsc_match.set('Inherit', 'False')
        pts_match = LET.SubElement(lsc_match, "points")
        for x, y in points:
            LET.SubElement(pts_match, "li").text = f"({x:.4f}, {y})"

        # nomatch: добавляем litterSizeCurve внутрь /race
        nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
        LET.SubElement(nomatch, "xpath").text = base_xpath
        val_nomatch = LET.SubElement(nomatch, "value")
        lsc_nom = LET.SubElement(val_nomatch, "litterSizeCurve")
        if inherit_false:
            lsc_nom.set('Inherit', 'False')
        pts_nom = LET.SubElement(lsc_nom, "points")
        for x, y in points:
            LET.SubElement(pts_nom, "li").text = f"({x:.4f}, {y})"

        return op

    def create_trade_tags_patch(self, def_name, tags, is_abstract=False, inherit=False):
        attr = '@Name' if is_abstract else 'defName'
        # верхний xpath на сам узел tradeTags (для условия добавления)
        cond_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/tradeTags"
        # путь для добавления tradeTags внутрь ThingDef (если его нет)
        add_parent_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]"

        ops = []

        # 1) Сначала безопасно удаляем перечисленные возможные tradeTags/li (если они есть).
        # ВСЕ возможные animal trade tags — удаляем их заранее (безопасно, если есть)
        all_animal_tags = [
            "AnimalCommon", "AnimalUncommon", "AnimalExotic",
            "AnimalFighter", "AnimalInsect", "AnimalPet", "AnimalFarm"
        ]
        cond_text = " or ".join([f'text()=\"{t}\"' for t in all_animal_tags])
        remove_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/tradeTags/li[{cond_text}]"
        rem_op = LET.Element("Operation", Class="PatchOperationConditional")
        LET.SubElement(rem_op, "xpath").text = remove_xpath
        rem_match = LET.SubElement(rem_op, "match", Class="PatchOperationRemove")
        LET.SubElement(rem_match, "xpath").text = remove_xpath
        ops.append(rem_op)

        # 2) Затем стандартная логика: если tradeTags уже есть -> add <li> внутрь, иначе -> add whole tradeTags node
        add_op = LET.Element("Operation", Class="PatchOperationConditional")
        LET.SubElement(add_op, "xpath").text = cond_xpath

        # match: когда tradeTags уже есть — добавляем li внутрь tradeTags
        match = LET.SubElement(add_op, "match", Class="PatchOperationAdd")
        LET.SubElement(match, "xpath").text = cond_xpath
        mval = LET.SubElement(match, "value")
        for t in tags:
            li = LET.SubElement(mval, "li")
            li.text = str(t)

        # nomatch: когда tradeTags отсутствует — добавляем сам tradeTags в ThingDef
        nomatch = LET.SubElement(add_op, "nomatch", Class="PatchOperationAdd")
        LET.SubElement(nomatch, "xpath").text = add_parent_xpath
        nval = LET.SubElement(nomatch, "value")
        trade = LET.SubElement(nval, "tradeTags")
        # Если оригинал имел Inherit="False" — проставим атрибут и в создаваемом узле
        if inherit:
            trade.set('Inherit', 'False')
        for t in tags:
            li = LET.SubElement(trade, "li")
            li.text = str(t)

        ops.append(add_op)
        return ops
        
    def create_special_trainables_patch(self, def_name, trainables, is_abstract=False):
        """
        Создаёт операции:
        1) Conditional remove для возможных ванильных specialTrainables (безопасная предочистка).
        2) Conditional Add — если /race/specialTrainables уже есть, добавляет <li MayRequire="Ludeon.RimWorld.Odyssey">…</li>;
           иначе добавляет целиком /race/specialTrainables под ThingDef/…/race.
        Возвращает список операций (ops).
        """
        attr = '@Name' if is_abstract else 'defName'
        ops = []

        # 1) Сначала безопасно удаляем возможные ванильные значения (если они есть)
        all_vanilla = [
            "SludgeSpew", "EggSpew", "TerrorRoar", "WarTrumpet",
            "Forage", "Comfort", "AttackTarget", "Dig"
        ]
        cond_text = " or ".join([f'text()=\"{t}\"' for t in all_vanilla])
        remove_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race/specialTrainables/li[{cond_text}]"
        rem_op = LET.Element("Operation", Class="PatchOperationConditional")
        LET.SubElement(rem_op, "xpath").text = remove_xpath
        rem_match = LET.SubElement(rem_op, "match", Class="PatchOperationRemove")
        LET.SubElement(rem_match, "xpath").text = remove_xpath
        ops.append(rem_op)

        # If no trainables provided -> only cleanup was requested
        if not trainables:
            return ops

        # 2) Затем стандартная логика: если specialTrainables уже есть -> add <li> внутрь,
        #    иначе -> add whole specialTrainables node under /race
        cond_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race/specialTrainables"
        add_parent_xpath = f"/Defs/ThingDef[{attr} = \"{def_name}\"]/race"

        add_op = LET.Element("Operation", Class="PatchOperationConditional")
        LET.SubElement(add_op, "xpath").text = cond_xpath

        # match: когда specialTrainables уже есть — добавляем li внутрь
        match = LET.SubElement(add_op, "match", Class="PatchOperationAdd")
        LET.SubElement(match, "xpath").text = cond_xpath
        mval = LET.SubElement(match, "value")
        for t in trainables:
            li = LET.SubElement(mval, "li")
            # атрибут, который вы просили
            li.set("MayRequire", "Ludeon.RimWorld.Odyssey")
            li.text = str(t)

        # nomatch: когда specialTrainables отсутствует — добавляем сам specialTrainables в race
        nomatch = LET.SubElement(add_op, "nomatch", Class="PatchOperationAdd")
        LET.SubElement(nomatch, "xpath").text = add_parent_xpath
        nval = LET.SubElement(nomatch, "value")
        special = LET.SubElement(nval, "specialTrainables")
        for t in trainables:
            li = LET.SubElement(special, "li")
            li.set("MayRequire", "Ludeon.RimWorld.Odyssey")
            li.text = str(t)

        ops.append(add_op)
        return ops

    def generate_ce_block(self, def_to_row, original_root, def_mod_conflicts=None):
        """
        Generate Combat Extended specific patches.
        """
        if self.ce_df is None or self.ce_df.empty:
            return LET.Comment(" No CE table provided or empty ")
        if def_mod_conflicts is None:
            def_mod_conflicts = {}

        # Build quick lookup of CE rows (all CE TSV rows) by def name
        ce_rows_all = {}
        for _, row in self.ce_df.iterrows():
            xml_name = row.get('XML name', '').strip() if 'XML name' in row.index else (row.iloc[0].strip() if len(row.index) > 0 else '')
            m = re.search(r'<li>(.*?)</li>', xml_name)
            ce_def = m.group(1) if m else xml_name
            ce_rows_all[ce_def] = row

        # Build vanilla map for tool signatures (use all vanilla rows)
        vanilla_rows_all = {}
        if self.vanilla_df is not None:
            for _, row in self.vanilla_df.iterrows():
                xml_name = row.get('XML name', '').strip() if 'XML name' in row.index else (row.iloc[0].strip() if len(row.index) > 0 else '')
                m = re.search(r'<li>(.*?)</li>', xml_name)
                def_name = m.group(1) if m else xml_name
                vanilla_rows_all[def_name] = row

        # Build all_abstract_groups from vanilla rows (so decision about parent coverage uses ALL children)
        all_abstract_groups = defaultdict(list)
        if self.vanilla_df is not None:
            for _, row in self.vanilla_df.iterrows():
                xml_name = row.get('XML name', '').strip() if 'XML name' in row.index else (row.iloc[0].strip() if len(row.index) > 0 else '')
                m = re.search(r'<li>(.*?)</li>', xml_name)
                def_name = m.group(1) if m else xml_name
                parent = row.get('Parrent abstract', '') if 'Parrent abstract' in row.index else row.get('Parent abstract', '') if 'Parent abstract' in row.index else ''
                parent = '' if parent is None else str(parent).strip()
                if parent and parent != 'None':
                    all_abstract_groups[parent].append(def_name)

        # CE fields to inspect for commonness
        ce_stat_fields = ['MeleeDodgeChance', 'MeleeCritChance', 'MeleeParryChance', 'ArmorRating_Sharp', 'ArmorRating_Blunt']

        # compute ce_parent_common_map using ALL children (so absence in current XML won't change decision)
        ce_parent_common_map = {}
        for abstract, children in all_abstract_groups.items():
            common_values = {}
            differing_cols = []

            # CE stat fields
            for stat in ce_stat_fields:
                vals = []
                for c in children:
                    r = ce_rows_all.get(c)
                    vals.append(norm(r.get(stat, '')) if r is not None else '')
                vals_non_empty = [v for v in vals if v != '']
                if len(vals_non_empty) == 0:
                    continue
                if all(v == vals_non_empty[0] for v in vals_non_empty):
                    # take non-normalized from first child that has it
                    first_val = None
                    for c in children:
                        r = ce_rows_all.get(c)
                        if r is not None and str(r.get(stat)).strip() != '':
                            first_val = r.get(stat)
                            break
                    if first_val is not None:
                        common_values[stat] = first_val
                else:
                    differing_cols.append(stat)

            # ArmorDurability at parent level (strict CE column)
            vals = []
            for c in children:
                r = ce_rows_all.get(c)
                if r is None:
                    vals.append('')
                else:
                    vals.append(norm(r.get('ArmorDurability', '') if 'ArmorDurability' in r.index else ''))
            vals_non_empty = [v for v in vals if v != '']
            if len(vals_non_empty) > 0:
                if all(v == vals_non_empty[0] for v in vals_non_empty):
                    first_val = None
                    for c in children:
                        r = ce_rows_all.get(c)
                        if r is not None and r.get('ArmorDurability') is not None and str(r.get('ArmorDurability')).strip() != '':
                            first_val = str(r.get('ArmorDurability')).strip()
                            break
                    if first_val is not None:
                        common_values['ArmorDurability'] = first_val
                else:
                    differing_cols.append('ArmorDurability')
                    
            # Body shape (Combat Extended specific extension value)
            # read from CE TSV rows (ce_rows_all)
            vals = []
            for c in children:
                r = ce_rows_all.get(c)
                if r is None:
                    vals.append('')
                else:
                    # get value if present
                    vals.append(norm(r.get('Body shape', '') if 'Body shape' in r.index else ''))
            vals_non_empty = [v for v in vals if v != '']
            if len(vals_non_empty) > 0:
                if all(v == vals_non_empty[0] for v in vals_non_empty):
                    # take original (non-normalized) value from first child that has it
                    first_val = None
                    for c in children:
                        r = ce_rows_all.get(c)
                        if r is not None and r.get('Body shape') is not None and str(r.get('Body shape')).strip() != '':
                            first_val = str(r.get('Body shape')).strip()
                            break
                    if first_val is not None:
                        common_values['Body shape'] = first_val
                else:
                    differing_cols.append('Body shape')

            # tool signature from vanilla rows
            tool_cols = ['Head damage', 'Poke/leg claws damage', 'Horn/Antler/Tusks damage', 'Bite damage', 'Paw claw/punch damage']
            tool_vals = []
            for c in children:
                r = vanilla_rows_all.get(c)
                if r is None:
                    tool_vals.append(tuple('' for _ in tool_cols))
                else:
                    tool_vals.append(tuple(norm(get_row_value(r, col)) for col in tool_cols))
            tool_sig = tool_vals[0] if len(set(tool_vals)) == 1 else None

            ce_parent_common_map[abstract] = {'common_values': common_values, 'differing_cols': differing_cols, 'tool_signature': tool_sig, 'children': children}

        # Determine first child for each abstract (TSV order preserved in all_abstract_groups)
        first_child_of_abstract = {}
        for abstract, children in all_abstract_groups.items():
            if children:
                first_child_of_abstract[abstract] = children[0]

        # present_defs = defs we are going to emit CE patches for
        present_defs = set(def_to_row.keys())

        # We'll collect child-level removals here and then emit them inside child's block.
        # Maps: child -> set(stat names), child -> tools_remove_flag, child -> comps_remove_flag
        child_stat_removals = defaultdict(set)
        child_tools_remove = set()
        child_comp_remove = set()
        # CE-specific: if parent owns Body shape, children must remove their CE bodyShape entry
        child_bodyshape_remove = set()

        # build CE operation block
        ce_op = LET.Element("Operation", Class="PatchOperationFindMod")
        mods = LET.SubElement(ce_op, "mods")
        LET.SubElement(mods, "li").text = "Combat Extended"
        match = LET.SubElement(ce_op, "match", Class="PatchOperationSequence")
        operations = LET.SubElement(match, "operations")

        # --- Parent-level CE patches (when appropriate) ---
        for abstract, info in ce_parent_common_map.items():
            children_all = info.get('children', [])
            if not children_all:
                continue

            # decide whether to emit parent-level CE patches in THIS XML:
            first_child = first_child_of_abstract.get(abstract)
            emit_parent_here = bool(first_child and first_child in present_defs)

            common_values = info.get('common_values', {})
            tool_signature = info.get('tool_signature')

            # Regardless of emit_parent_here, RECORD removals for present children when a field is common.
            # This ensures children in files WITHOUT parent still get safe_remove in their own block.
            if common_values:
                for stat in ce_stat_fields:
                    if stat in common_values:
                        for child in info.get('children', []):
                            if child in present_defs:
                                child_stat_removals[child].add(stat)
                # ArmorDurability: record child comp removal if parent covers it
                if 'ArmorDurability' in common_values:
                    for child in info.get('children', []):
                        if child in present_defs:
                            child_comp_remove.add(child)
                # Body shape: if parent covers Body shape -> children must remove their CE bodyShape entry
                if 'Body shape' in common_values:
                    for child in info.get('children', []):
                        if child in present_defs:
                            child_bodyshape_remove.add(child)
                # Tools: if tools common -> children must remove their own tools (regardless of emit_parent)
                if tool_signature is not None:
                    for child in info.get('children', []):
                        if child in present_defs:
                            child_tools_remove.add(child)

            # If we are supposed to emit parent-level patches here (first child present) — do it.
            if emit_parent_here:
                 # Parent-level CE tools: build from CE TSV (using representative child) if tools signature common
                if tool_signature is not None:
                    # choose representative present child name (prefer CE TSV row, then def_to_row, then vanilla_rows_all)
                    rep_child_name = None
                    for c in children_all:
                        if c in ce_rows_all:
                            rep_child_name = c
                            break
                        if c in def_to_row:
                            rep_child_name = c
                            break
                        if c in vanilla_rows_all:
                            rep_child_name = c
                            break

                    ce_built_tools = None
                    if rep_child_name is not None:
                        # build CE tools using the representative child's CE row (and vanilla labels if available)
                        ce_built_tools = self.build_tools_ce(rep_child_name, vanilla_row=vanilla_rows_all.get(rep_child_name))

                    if ce_built_tools is not None:
                        operations.append(LET.Comment(""))
                        operations.append(LET.Comment(f" {abstract} CE tools (parent-level) "))

                        # Build consistent xpath (use @Name for abstract parent to match original abstract entries)
                        parent_tools_xpath = f"/Defs/ThingDef[@Name=\"{abstract}\"]/tools"

                        # remove existing tools at abstract level (conditional remove)
                        remove_op = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(remove_op, "xpath").text = parent_tools_xpath
                        LET.SubElement(LET.SubElement(remove_op, "match", Class="PatchOperationRemove"), "xpath").text = parent_tools_xpath
                        operations.append(remove_op)

                        # add built CE tools under abstract (add to the abstract node)
                        # preserve Inherit if original abstract had it
                        inherit = self.extract_tools_inherit(original_root, abstract, is_abstract=True)
                        ce_built_copy = copy.deepcopy(ce_built_tools)
                        if inherit:
                            ce_built_copy.set('Inherit', 'False')

                        add_op = LET.Element("li", Class="PatchOperationAdd")
                        LET.SubElement(add_op, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]"
                        val_node = LET.SubElement(add_op, "value")
                        val_node.append(ce_built_copy)
                        operations.append(add_op)

                # Parent-level CE stat fields
                if common_values:
                    # Ensure statBases exists on the abstract before touching its children stats
                    ensure_statbases = LET.Element("li", Class="PatchOperationConditional")
                    LET.SubElement(ensure_statbases, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/statBases"
                    nomatch_ensure = LET.SubElement(ensure_statbases, "nomatch", Class="PatchOperationAdd")
                    # when statBases is missing, add an empty <statBases/> under the ThingDef
                    LET.SubElement(nomatch_ensure, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]"
                    val_node_sb = LET.SubElement(nomatch_ensure, "value")
                    LET.SubElement(val_node_sb, "statBases")
                    operations.append(ensure_statbases)

                    # Ensure race exists on the abstract before touching race fields
                    ensure_race = LET.Element("li", Class="PatchOperationConditional")
                    LET.SubElement(ensure_race, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/race"
                    nomatch_race = LET.SubElement(ensure_race, "nomatch", Class="PatchOperationAdd")
                    LET.SubElement(nomatch_race, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]"
                    val_node_r = LET.SubElement(nomatch_race, "value")
                    LET.SubElement(val_node_r, "race")
                    operations.append(ensure_race)
                    operations.append(LET.Comment(""))
                    operations.append(LET.Comment(f" {abstract} CE parent properties "))
                    for stat in ce_stat_fields:
                        if stat in common_values:
                            val = common_values[stat]
                            if val is not None and str(val).strip() != '':
                                # use abstract @Name paths consistently
                                li = LET.Element("li", Class="PatchOperationConditional")
                                parent_stat_xpath = f"Defs/ThingDef[@Name=\"{abstract}\"]/statBases/{stat}"
                                LET.SubElement(li, "xpath").text = parent_stat_xpath
                                match_li = LET.SubElement(li, "match", Class="PatchOperationReplace")
                                LET.SubElement(match_li, "xpath").text = parent_stat_xpath
                                LET.SubElement(LET.SubElement(match_li, "value"), stat).text = str(val)
                                nomatch_li = LET.SubElement(li, "nomatch", Class="PatchOperationAdd")
                                LET.SubElement(nomatch_li, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/statBases"
                                LET.SubElement(LET.SubElement(nomatch_li, "value"), stat).text = str(val)
                                operations.append(li)
                        elif stat in info.get('differing_cols', []):
                            # children differ -> ensure parent does not force a value (remove from parent)
                            rm = LET.Element("li", Class="PatchOperationConditional")
                            parent_rm_xpath = f"Defs/ThingDef[@Name=\"{abstract}\"]/statBases/{stat}"
                            LET.SubElement(rm, "xpath").text = parent_rm_xpath
                            LET.SubElement(LET.SubElement(rm, "match", Class="PatchOperationRemove"), "xpath").text = parent_rm_xpath
                            operations.append(rm)

                # ArmorDurability at parent level
                if 'ArmorDurability' in common_values:
                    val = common_values['ArmorDurability']
                    if val is not None and str(val).strip() != '':
                        # ensure comps exists (for parent)
                        ensure_comps = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(ensure_comps, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/comps"
                        nomatch_ensure = LET.SubElement(ensure_comps, "nomatch", Class="PatchOperationAdd")
                        LET.SubElement(nomatch_ensure, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]"
                        val_node = LET.SubElement(nomatch_ensure, "value")
                        LET.SubElement(val_node, "comps")
                        operations.append(ensure_comps)

                        # replace Durability if comp exists, otherwise add comp li under /comps for parent
                        cond_comp = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(cond_comp, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]"
                        match_rep = LET.SubElement(cond_comp, "match", Class="PatchOperationReplace")
                        LET.SubElement(match_rep, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]/Durability"
                        match_value = LET.SubElement(match_rep, "value")
                        LET.SubElement(match_value, "Durability").text = str(val)

                        nomatch_add = LET.SubElement(cond_comp, "nomatch", Class="PatchOperationAdd")
                        LET.SubElement(nomatch_add, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/comps"
                        val_add = LET.SubElement(nomatch_add, "value")
                        li_comp = LET.SubElement(val_add, "li", Class="CombatExtended.CompProperties_ArmorDurability")
                        LET.SubElement(li_comp, "Durability").text = str(val)
                        LET.SubElement(li_comp, "Regenerates").text = "true"
                        LET.SubElement(li_comp, "RegenInterval").text = "600"
                        LET.SubElement(li_comp, "RegenValue").text = "5"
                        LET.SubElement(li_comp, "MinArmorPct").text = "0.5"
                        operations.append(cond_comp)
                else:
                    # if ArmorDurability differs among children -> ensure parent doesn't force it
                    if 'ArmorDurability' in info.get('differing_cols', []):
                        rm = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(rm, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]"
                        LET.SubElement(LET.SubElement(rm, "match", Class="PatchOperationRemove"), "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]"
                        operations.append(rm)
                        
                # --- Body shape at parent level (Combat Extended mod extension) ---
                if 'Body shape' in common_values:
                    body_val = str(common_values['Body shape']).strip()
                    if body_val != '':
                        parent_body_li = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(parent_body_li, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]"

                        # match -> sequence of operations (replace/add bodyShape under existing extension)
                        match_seq = LET.SubElement(parent_body_li, "match", Class="PatchOperationSequence")
                        ops_node = LET.SubElement(match_seq, "operations")

                        inner_li = LET.SubElement(ops_node, "li", Class="PatchOperationConditional")
                        LET.SubElement(inner_li, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"

                        # match replace
                        match_rep = LET.SubElement(inner_li, "match", Class="PatchOperationReplace")
                        LET.SubElement(match_rep, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
                        val_node = LET.SubElement(match_rep, "value")
                        LET.SubElement(val_node, "bodyShape").text = body_val

                        # nomatch add (add bodyShape under existing extension)
                        nomatch_add = LET.SubElement(inner_li, "nomatch", Class="PatchOperationAdd")
                        LET.SubElement(nomatch_add, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]"
                        val_node2 = LET.SubElement(nomatch_add, "value")
                        LET.SubElement(val_node2, "bodyShape").text = body_val

                        # IMPORTANT: if the CE extension is absent, create it (AddModExtension) as the outer nomatch
                        nomatch_addmod = LET.SubElement(parent_body_li, "nomatch", Class="PatchOperationAddModExtension")
                        LET.SubElement(nomatch_addmod, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]"
                        val_addmod = LET.SubElement(nomatch_addmod, "value")
                        li_ce = LET.SubElement(val_addmod, "li", Class="CombatExtended.RacePropertiesExtensionCE")
                        LET.SubElement(li_ce, "bodyShape").text = body_val

                        operations.append(parent_body_li)
                else:
                    # if Body shape differs among children -> ensure parent doesn't force it (remove parent's bodyShape if present)
                    if 'Body shape' in info.get('differing_cols', []):
                        rm_bs = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(rm_bs, "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
                        LET.SubElement(LET.SubElement(rm_bs, "match", Class="PatchOperationRemove"), "xpath").text = f"Defs/ThingDef[@Name=\"{abstract}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
                        operations.append(rm_bs)

        # --- Per-child CE patches (only for present defs) ---
        for def_name, vanilla_row in def_to_row.items():
            child_start_idx = len(operations)
            ce_row = ce_rows_all.get(def_name)
            operations.append(LET.Comment(f" {def_name} CE patches "))

            # First: emit any recorded removals for this child (so they live inside child's block)
            #  - stat removals
            stats_to_remove = sorted(child_stat_removals.get(def_name, []))
            for stat in stats_to_remove:
                rm = LET.Element("li", Class="PatchOperationConditional")
                LET.SubElement(rm, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/statBases/{stat}"
                LET.SubElement(LET.SubElement(rm, "match", Class="PatchOperationRemove"), "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/statBases/{stat}"
                operations.append(rm)

            # - tools removal (if flagged)
            if def_name in child_tools_remove:
                rm_tools = LET.Element("li", Class="PatchOperationConditional")
                LET.SubElement(rm_tools, "xpath").text = f"/Defs/ThingDef[defName=\"{def_name}\"]/tools"
                LET.SubElement(LET.SubElement(rm_tools, "match", Class="PatchOperationRemove"), "xpath").text = f"/Defs/ThingDef[defName=\"{def_name}\"]/tools"
                operations.append(rm_tools)

            # - comp removal for ArmorDurability (if flagged)
            if def_name in child_comp_remove:
                rm_comp = LET.Element("li", Class="PatchOperationConditional")
                LET.SubElement(rm_comp, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]"
                LET.SubElement(LET.SubElement(rm_comp, "match", Class="PatchOperationRemove"), "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]"
                operations.append(rm_comp)
                
            # - bodyShape removal for CE extension (if parent's common covers it)
            if def_name in child_bodyshape_remove:
                rm_bs_child = LET.Element("li", Class="PatchOperationConditional")
                LET.SubElement(rm_bs_child, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
                LET.SubElement(LET.SubElement(rm_bs_child, "match", Class="PatchOperationRemove"), "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
                operations.append(rm_bs_child)

            # Determine parent abstract for this def (from vanilla rows)
            parent = ''
            vrow = vanilla_rows_all.get(def_name)
            if vrow is not None:
                parent = vrow.get('Parrent abstract', '') if 'Parrent abstract' in vrow.index else vrow.get('Parent abstract', '') if 'Parent abstract' in vrow.index else ''
                parent = '' if parent is None else str(parent).strip()
            parent_info = ce_parent_common_map.get(parent) if parent else None

            # CE stat fields: if parent common covers the stat -> skip child's replacement (parent already defines it)
            for stat in ['MeleeDodgeChance', 'MeleeCritChance', 'MeleeParryChance', 'ArmorRating_Sharp', 'ArmorRating_Blunt']:
                if parent_info and stat in parent_info.get('common_values', {}):
                    continue
                if ce_row is not None:
                    value = ce_row.get(stat)
                    if value and str(value).strip() != '':
                        li = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(li, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/statBases/{stat}"
                        match_li = LET.SubElement(li, "match", Class="PatchOperationReplace")
                        LET.SubElement(match_li, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/statBases/{stat}"
                        LET.SubElement(LET.SubElement(match_li, "value"), stat).text = str(value)
                        nomatch_li = LET.SubElement(li, "nomatch", Class="PatchOperationAdd")
                        LET.SubElement(nomatch_li, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/statBases"
                        LET.SubElement(LET.SubElement(nomatch_li, "value"), stat).text = str(value)
                        operations.append(li)

            # ArmorDurability per-child (if parent doesn't define it)
            if not (parent_info and 'ArmorDurability' in parent_info.get('common_values', {})):
                if ce_row is not None and 'ArmorDurability' in ce_row.index and str(ce_row.get('ArmorDurability')).strip() != '':
                    dur_val = str(ce_row.get('ArmorDurability')).strip()
                    ensure_comps = LET.Element("li", Class="PatchOperationConditional")
                    LET.SubElement(ensure_comps, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/comps"
                    nomatch_ensure = LET.SubElement(ensure_comps, "nomatch", Class="PatchOperationAdd")
                    LET.SubElement(nomatch_ensure, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]"
                    val_node = LET.SubElement(nomatch_ensure, "value")
                    LET.SubElement(val_node, "comps")
                    operations.append(ensure_comps)

                    cond_comp = LET.Element("li", Class="PatchOperationConditional")
                    LET.SubElement(cond_comp, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]"
                    match_rep = LET.SubElement(cond_comp, "match", Class="PatchOperationReplace")
                    LET.SubElement(match_rep, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]/Durability"
                    match_value = LET.SubElement(match_rep, "value")
                    LET.SubElement(match_value, "Durability").text = dur_val

                    nomatch_add = LET.SubElement(cond_comp, "nomatch", Class="PatchOperationAdd")
                    LET.SubElement(nomatch_add, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/comps"
                    val_add = LET.SubElement(nomatch_add, "value")
                    li_comp = LET.SubElement(val_add, "li", Class="CombatExtended.CompProperties_ArmorDurability")
                    LET.SubElement(li_comp, "Durability").text = dur_val
                    LET.SubElement(li_comp, "Regenerates").text = "true"
                    LET.SubElement(li_comp, "RegenInterval").text = "600"
                    LET.SubElement(li_comp, "RegenValue").text = "5"
                    LET.SubElement(li_comp, "MinArmorPct").text = "0.5"
                    operations.append(cond_comp)
                    
            # --- Body shape per-child (CE mod extension) ---
            if not (parent_info and 'Body shape' in parent_info.get('common_values', {})):
                if ce_row is not None and 'Body shape' in ce_row.index:
                    bs_val = ce_row.get('Body shape')
                    bs_val_s = None if bs_val is None else str(bs_val).strip()
                    if bs_val_s is None or bs_val_s == '' or bs_val_s.lower() in ('no', 'none'):
                        # explicit "no" -> remove child's bodyShape entry if present
                        rm_bs_child = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(rm_bs_child, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
                        LET.SubElement(LET.SubElement(rm_bs_child, "match", Class="PatchOperationRemove"), "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
                        operations.append(rm_bs_child)
                    else:
                        child_body_li = LET.Element("li", Class="PatchOperationConditional")
                        LET.SubElement(child_body_li, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]"

                        match_seq = LET.SubElement(child_body_li, "match", Class="PatchOperationSequence")
                        ops_node = LET.SubElement(match_seq, "operations")

                        inner_li = LET.SubElement(ops_node, "li", Class="PatchOperationConditional")
                        LET.SubElement(inner_li, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"

                        match_rep = LET.SubElement(inner_li, "match", Class="PatchOperationReplace")
                        LET.SubElement(match_rep, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
                        val_node = LET.SubElement(match_rep, "value")
                        LET.SubElement(val_node, "bodyShape").text = bs_val_s

                        nomatch_add = LET.SubElement(inner_li, "nomatch", Class="PatchOperationAdd")
                        LET.SubElement(nomatch_add, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]"
                        val_node2 = LET.SubElement(nomatch_add, "value")
                        LET.SubElement(val_node2, "bodyShape").text = bs_val_s

                        # outer nomatch: if CE extension missing entirely, add it under modExtensions (AddModExtension)
                        nomatch_addmod_child = LET.SubElement(child_body_li, "nomatch", Class="PatchOperationAddModExtension")
                        LET.SubElement(nomatch_addmod_child, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]"
                        val_addmod_child = LET.SubElement(nomatch_addmod_child, "value")
                        li_ce_child = LET.SubElement(val_addmod_child, "li", Class="CombatExtended.RacePropertiesExtensionCE")
                        LET.SubElement(li_ce_child, "bodyShape").text = bs_val_s

                        operations.append(child_body_li)

            # Tools per-child: skip if parent covers tool_signature (parent-level tools were created somewhere)
            skip_child_tools = False
            if parent and parent in ce_parent_common_map:
                pinfo = ce_parent_common_map[parent]
                if pinfo.get('tool_signature') is not None:
                    skip_child_tools = True

            if not skip_child_tools:
                # Build CE tools from CE TSV for this concrete def (use vanilla labels/caps if available)
                vanilla_row_for_labels = vanilla_rows_all.get(def_name)
                ce_built_tools_child = self.build_tools_ce(def_name, vanilla_row=vanilla_row_for_labels)

                if ce_built_tools_child is not None:
                    # remove existing tools at ThingDef level (match remove)
                    remove_op = LET.Element("li", Class="PatchOperationConditional")
                    LET.SubElement(remove_op, "xpath").text = f"/Defs/ThingDef[defName=\"{def_name}\"]/tools"
                    LET.SubElement(LET.SubElement(remove_op, "match", Class="PatchOperationRemove"), "xpath").text = f"/Defs/ThingDef[defName=\"{def_name}\"]/tools"
                    operations.append(remove_op)

                    # add built CE tools under this concrete def (preserve Inherit if present)
                    inherit = self.extract_tools_inherit(original_root, def_name, is_abstract=False)
                    child_copy = copy.deepcopy(ce_built_tools_child)
                    if inherit:
                        child_copy.set('Inherit', 'False')

                    add_op = LET.Element("li", Class="PatchOperationAdd")
                    LET.SubElement(add_op, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]"
                    val_node = LET.SubElement(add_op, "value")
                    val_node.append(child_copy)
                    operations.append(add_op)
                else:
                    # No CE data or CE TSV explicitly says no -> remove tools node (child-level)
                    rm = LET.Element("li", Class="PatchOperationConditional")
                    LET.SubElement(rm, "xpath").text = f"/Defs/ThingDef[defName=\"{def_name}\"]/tools"
                    LET.SubElement(LET.SubElement(rm, "match", Class="PatchOperationRemove"), "xpath").text = f"/Defs/ThingDef[defName=\"{def_name}\"]/tools"
                    operations.append(rm)

            # ArmorRating_Heat removal: if parent handles it already, skip; otherwise emit per-child removal
            parent_handles_heat = False
            if parent and parent in ce_parent_common_map:
                parent_common = ce_parent_common_map[parent].get('common_values', {})
                parent_diff = ce_parent_common_map[parent].get('differing_cols', [])
                if 'ArmorRating_Heat' in parent_common or 'ArmorRating_Heat' in parent_diff:
                    parent_handles_heat = True

            if not parent_handles_heat:
                rem_heat = LET.Element("li", Class="PatchOperationConditional")
                LET.SubElement(rem_heat, "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/statBases/ArmorRating_Heat"
                LET.SubElement(LET.SubElement(rem_heat, "match", Class="PatchOperationRemove"), "xpath").text = f"Defs/ThingDef[defName=\"{def_name}\"]/statBases/ArmorRating_Heat"
                operations.append(rem_heat)

            # If this animal has ModConflict, run its CE child block only when conflicting mod is absent.
            conflict_mods = def_mod_conflicts.get(def_name)
            if self._split_mod_list(conflict_mods):
                child_nodes = [operations[idx] for idx in range(child_start_idx, len(operations))]
                has_real_ops = any(hasattr(node, 'tag') and isinstance(node.tag, str) for node in child_nodes)
                if has_real_ops:
                    while len(operations) > child_start_idx:
                        operations.remove(operations[child_start_idx])
                    for wrapped in self._wrap_ops_in_mod_conflict(child_nodes, conflict_mods, as_li=True):
                        operations.append(wrapped)

        return ce_op

    # --- helper to check whether a name likely represents an abstract (@Name) or concrete (defName)
    def is_abstract_name(self, name):
        # heuristic: abstract names often contain spaces or capitalized words, but we won't rely on that.
        # keep it simple: return True if name in original XML as @Name was used; fallback False.
        # Since calling context in generate_ce_block uses @Name path for parent-level ops, this helper is only a minor convenience.
        return True

    def validate_xml(self, path):
        try:
            LET.parse(path)
            return True
        except LET.XMLSyntaxError as e:
            print(f"XML validation failed: {e}")
            return False

# ---------------- GUI ----------------
class GeneratorApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("RimWorld Patch Generator/Fixer")
        self.geometry("950x600")
        self.cfg = load_config()
        script_dir = os.path.dirname(os.path.abspath(sys.argv[0])) or os.getcwd()
        default_stats_path = os.path.join(script_dir, "AnimalStats.xlsx")
        default_vanilla_source = self.cfg.get('vanilla_tsv', '')
        if not default_vanilla_source and os.path.exists(default_stats_path):
            default_vanilla_source = default_stats_path
        self.vanilla_tsv = tk.StringVar(value=default_vanilla_source)
        self.ce_tsv = tk.StringVar(value=self.cfg.get('ce_tsv', ''))
        self.xml_paths, changed, dropped = self._normalize_xml_paths(self.cfg.get('xmls', []))
        if changed or dropped:
            self.cfg['xmls'] = self.xml_paths
            save_config(self.cfg)
            if dropped:
                print(f"Warning: removed {len(dropped)} missing XML path(s) from config.")
        self.replace_in_place = tk.BooleanVar(value=bool(self.cfg.get('replace_in_place', False)))
        self._build_ui()
        self.report_dir = self._ensure_report_dir()
        self.status.set(f"Output folder: {self.report_dir}")

    def _normalize_xml_paths(self, paths):
        script_dir = os.path.dirname(os.path.abspath(sys.argv[0])) or os.getcwd()
        normalized = []
        changed = False
        dropped = []
        for p in paths:
            if not p:
                continue
            p_norm = os.path.normpath(p)
            if os.path.exists(p_norm):
                normalized.append(p_norm)
                if p_norm != p:
                    changed = True
                continue
            parts = [part for part in re.split(r'[\\/]+', p_norm) if part]
            parts_lower = [part.lower() for part in parts]
            candidate = None
            if "checker" in parts_lower:
                idx = parts_lower.index("checker")
                tail = parts[idx + 1:]
                if tail:
                    candidate = os.path.join(script_dir, *tail)
                    if os.path.exists(candidate):
                        normalized.append(candidate)
                        changed = True
                        continue
            if "generated_patches" in parts_lower:
                idx = parts_lower.index("generated_patches")
                tail = parts[idx:]
                if tail:
                    candidate = os.path.join(script_dir, *tail)
                    if os.path.exists(candidate):
                        normalized.append(candidate)
                        changed = True
                        continue
            candidate = os.path.join(script_dir, os.path.basename(p_norm))
            if os.path.exists(candidate):
                normalized.append(candidate)
                changed = True
                continue
            dropped.append(p)
        return normalized, changed, dropped

    def _ensure_report_dir(self):
        script_dir = os.path.dirname(os.path.abspath(sys.argv[0])) or os.getcwd()
        r = os.path.join(script_dir, REPORT_DIRNAME)
        os.makedirs(r, exist_ok=True)
        return r

    def _build_ui(self):
        main = ttk.Frame(self)
        main.pack(fill='both', expand=True, padx=10, pady=10)
        v_frame = ttk.Frame(main)
        v_frame.pack(fill='x')
        ttk.Label(v_frame, text="Vanilla table (TSV/XLSX):").pack(side='left')
        ttk.Entry(v_frame, textvariable=self.vanilla_tsv, width=80).pack(side='left', padx=6)
        ttk.Button(v_frame, text="Browse", command=self.pick_vanilla_tsv).pack(side='left')
        c_frame = ttk.Frame(main)
        c_frame.pack(fill='x', pady=5)
        ttk.Label(c_frame, text="CE table (TSV/XLSX, optional):").pack(side='left')
        ttk.Entry(c_frame, textvariable=self.ce_tsv, width=80).pack(side='left', padx=6)
        ttk.Button(c_frame, text="Browse", command=self.pick_ce_tsv).pack(side='left')
        mid = ttk.Frame(main)
        mid.pack(fill='both', expand=True)
        ttk.Label(mid, text="XML files to fix/generate:").pack(anchor='w')
        self.xml_listbox = tk.Listbox(mid, selectmode=tk.SINGLE, height=12)
        self.xml_listbox.pack(side='left', fill='both', expand=True)
        for p in self.xml_paths:
            self.xml_listbox.insert(tk.END, p)
        ctrl = ttk.Frame(mid)
        ctrl.pack(side='left', fill='y', padx=8)
        ttk.Button(ctrl, text="Add XML(s)", command=self.add_xml).pack(fill='x', pady=2)
        ttk.Button(ctrl, text="Add Folder", command=self.add_folder).pack(fill='x', pady=2)
        ttk.Button(ctrl, text="Remove Selected", command=self.remove_selected).pack(fill='x', pady=2)
        ttk.Button(ctrl, text="Clear", command=self.clear_xmls).pack(fill='x', pady=2)
        ttk.Separator(ctrl, orient='horizontal').pack(fill='x', pady=6)
        ttk.Checkbutton(
            ctrl,
            text="Replace original XML files in place",
            variable=self.replace_in_place,
            command=self.on_replace_mode_changed
        ).pack(fill='x', pady=2)
        ttk.Separator(ctrl, orient='horizontal').pack(fill='x', pady=6)
        ttk.Button(ctrl, text="Generate/Fix Patches", command=self.run_generation).pack(fill='x', pady=6)
        ttk.Button(ctrl, text="Generate Biome Patches", command=self.run_biome_generation).pack(fill='x', pady=2)
        ttk.Button(ctrl, text="Open Output Folder", command=self.open_output).pack(fill='x', pady=2)
        self.status = tk.StringVar(value='')
        ttk.Label(main, textvariable=self.status).pack(fill='x', pady=(8,0))

    def pick_vanilla_tsv(self):
        p = filedialog.askopenfilename(
            title="Select Vanilla table (TSV or AnimalStats.xlsx)",
            filetypes=[("Tables", "*.tsv *.xlsx *.xlsm *.xls"), ("TSV", "*.tsv"), ("Excel", "*.xlsx *.xlsm *.xls"), ("All", "*.*")]
        )
        if p:
            self.vanilla_tsv.set(p)
            self.cfg['vanilla_tsv'] = p
            save_config(self.cfg)

    def pick_ce_tsv(self):
        p = filedialog.askopenfilename(
            title="Select CE table (TSV or AnimalStats.xlsx)",
            filetypes=[("Tables", "*.tsv *.xlsx *.xlsm *.xls"), ("TSV", "*.tsv"), ("Excel", "*.xlsx *.xlsm *.xls"), ("All", "*.*")]
        )
        if p:
            self.ce_tsv.set(p)
            self.cfg['ce_tsv'] = p
            save_config(self.cfg)

    def add_xml(self):
        paths = filedialog.askopenfilenames(title="Select XML(s)", filetypes=[("XML","*.xml"),("All","*.*")])
        if paths:
            for p in paths:
                if p not in self.xml_paths:
                    self.xml_paths.append(p)
                    self.xml_listbox.insert(tk.END, p)
            self.cfg['xmls'] = self.xml_paths
            save_config(self.cfg)

    def add_folder(self):
        folder = filedialog.askdirectory(title="Select Folder with XML files")
        if not folder:
            return
        found = []
        for root, _, files in os.walk(folder):
            for name in files:
                if name.lower().endswith('.xml'):
                    found.append(os.path.join(root, name))
        found.sort()
        added = 0
        for p in found:
            if p not in self.xml_paths:
                self.xml_paths.append(p)
                self.xml_listbox.insert(tk.END, p)
                added += 1
        self.cfg['xmls'] = self.xml_paths
        save_config(self.cfg)
        self.status.set(f"Added {added} XML file(s) from folder.")

    def on_replace_mode_changed(self):
        self.cfg['replace_in_place'] = bool(self.replace_in_place.get())
        save_config(self.cfg)

    def _make_unique_output_path(self, source_xml, used_names):
        base = os.path.splitext(os.path.basename(source_xml))[0]
        n = used_names.get(base, 0) + 1
        used_names[base] = n
        out_name = f"{base}.xml" if n == 1 else f"{base}_{n}.xml"
        return os.path.join(self.report_dir, out_name)

    def remove_selected(self):
        sel = self.xml_listbox.curselection()
        if sel:
            i = sel[0]
            val = self.xml_listbox.get(i)
            self.xml_listbox.delete(i)
            self.xml_paths.remove(val)
            self.cfg['xmls'] = self.xml_paths
            save_config(self.cfg)

    def clear_xmls(self):
        self.xml_listbox.delete(0, tk.END)
        self.xml_paths.clear()
        self.cfg['xmls'] = []
        save_config(self.cfg)

    def open_output(self):
        folder = self.report_dir
        try:
            if sys.platform == 'darwin':
                os.system(f'open "{folder}"')
            elif sys.platform == 'win32':
                os.startfile(folder)
            else:
                os.system(f'xdg-open "{folder}"')
        except Exception as e:
            messagebox.showerror("Error", str(e))

    def run_generation(self):
        v_source = self.vanilla_tsv.get()
        c_source = self.ce_tsv.get() or None
        if not v_source or not os.path.exists(v_source):
            messagebox.showerror("Error", "Vanilla table missing or not found (TSV/XLSX).")
            return
        if not self.xml_paths:
            messagebox.showerror("Error", "No XML files selected.")
            return
        replace_in_place = bool(self.replace_in_place.get())
        if replace_in_place:
            proceed = messagebox.askyesno(
                "Confirm In-Place Replace",
                "This will overwrite selected XML files in their original folders. Continue?"
            )
            if not proceed:
                return
        try:
            generator = PatchGenerator(v_source, c_source, self.xml_paths)
            results = []
            used_names = {}
            for xml in self.xml_paths:
                out_xml = xml if replace_in_place else self._make_unique_output_path(xml, used_names)
                success = generator.generate_fixed_xml(xml, out_xml)
                results.append((xml, out_xml, success))
            if replace_in_place:
                msg = "\n".join([f"{os.path.basename(x)} ({'OK' if s else 'FAILED'})" for x, _, s in results])
                messagebox.showinfo("Done", f"Updated files in place:\n{msg}")
                self.status.set(f"Processed {len(results)} file(s) in place.")
            else:
                msg = "\n".join([f"{os.path.basename(x)} → {os.path.basename(o)} ({'OK' if s else 'FAILED'})" for x, o, s in results])
                messagebox.showinfo("Done", f"Generated patches:\n{msg}\n\nFolder: {self.report_dir}")
                self.status.set(f"Processed {len(results)} file(s).")
        except Exception as e:
            traceback.print_exc()
            messagebox.showerror("Error", str(e))

    def run_biome_generation(self):
        v_source = self.vanilla_tsv.get()
        if not v_source or not os.path.exists(v_source):
            messagebox.showerror("Error", "Vanilla table missing or not found (TSV/XLSX).")
            return
        try:
            generator = PatchGenerator(v_source, self.ce_tsv.get() or None, self.xml_paths)
            created = generator.generate_biome_patch_files(self.report_dir)
            if created:
                preview = "\n".join([os.path.basename(p) for p in created[:20]])
                more = "" if len(created) <= 20 else f"\n... and {len(created) - 20} more"
                messagebox.showinfo(
                    "Done",
                    f"Generated biome patches: {len(created)}\n\n{preview}{more}\n\nFolder: {self.report_dir}"
                )
                self.status.set(f"Generated biome patch files: {len(created)}.")
            else:
                messagebox.showinfo("Done", f"No biome patches generated.\n\nFolder: {self.report_dir}")
                self.status.set("No biome patches generated.")
        except Exception as e:
            traceback.print_exc()
            messagebox.showerror("Error", str(e))

if __name__ == "__main__":
    app = GeneratorApp()
    app.mainloop()
