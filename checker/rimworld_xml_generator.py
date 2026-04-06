#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RimWorld animal XML generator from AnimalStats.xlsx.
"""

from __future__ import annotations

import argparse
import copy
import json
import os
import re
import sys
import traceback
from typing import Dict, List, Optional, Tuple

import pandas as pd
from lxml import etree as LET
import tkinter as tk
from tkinter import filedialog, messagebox, simpledialog, ttk


CONFIG_FILE = "rimworld_xml_generator_config.json"
DEFAULT_GAME_ROOT_DIR = r"D:\SteamLibrary\steamapps\common\RimWorld"
DEFAULT_CORE_THINGDEFS_DIR = os.path.join(DEFAULT_GAME_ROOT_DIR, "Data", "Core", "Defs", "ThingDefs_Races")
DEFAULT_ODYSSEY_THINGDEFS_DIR = os.path.join(DEFAULT_GAME_ROOT_DIR, "Data", "Odyssey", "Defs", "ThingDefs_Races")
ODYSSEY_MAYREQUIRE = "Ludeon.RimWorld.Odyssey"


COLUMN_ALIASES = {
    "MarketValue": ["MarketValue", "Market value", "market value", "marketvalue"],
    "MoveSpeed": ["MoveSpeed", "Move speed", "move speed", "movespeed"],
    "Wildness": ["Wildness", "wildness"],
    "FilthRate": ["FilthRate", "Filth rate", "filth rate", "filthrate"],
    "ComfyTemperatureMin": ["ComfyTemperatureMin", "Comfy temperature min", "comfytemperaturemin"],
    "ComfyTemperatureMax": ["ComfyTemperatureMax", "Comfy temperature max", "comfytemperaturemax"],
    "ArmorRating_Blunt": ["ArmorRating_Blunt", "ArmorRating Blunt", "ArmorRating_Blunt"],
    "ArmorRating_Sharp": ["ArmorRating_Sharp", "ArmorRating Sharp", "ArmorRating_Sharp"],
    "ToxicEnvironmentResistance": [
        "ToxicEnvironmentResistance",
        "Toxic Environment Resistance",
        "ToxicEnvironmentResistance",
    ],
    "baseBodySize": ["baseBodySize", "Base body size", "BaseBodySize", "basebodysize"],
    "baseHealthScale": ["baseHealthScale", "Base health scale", "basehealthscale"],
    "baseHungerRate": ["baseHungerRate", "Base hunger rate", "basehungerrate"],
    "lifeExpectancy": ["Lifespan (years)", "lifeExpectancy", "life expectancy", "Lifespan"],
    "gestationPeriodDays": ["Gestation period (days)", "gestationPeriodDays", "Gestation period"],
    "herdAnimal": ["herdAnimal", "HerdAnimal", "herd animal"],
    "herdMigrationAllowed": ["herdMigrationAllowed", "herd migration allowed", "herdMigrationAllowed"],
    "Foodtype": ["Foodtype", "Food type", "foodType", "food type"],
    "manhunterOnTameFailChance": ["manhunterOnTameFailChance", "manhunterOnTameFailChance"],
    "manhunterOnDamageChance": ["manhunterOnDamageChance", "manhunterOnDamageChance"],
    "petness": ["petness", "Petness"],
    "nuzzleMtbHours": ["nuzzleMtbHours", "NuzzleMtbHours"],
    "mateMtbHours": ["mateMtbHours", "MateMtbHours"],
    "trainability": ["trainability", "Trainability"],
    "predator": ["predator", "Predator"],
    "maxPreyBodySize": ["maxPreyBodySize", "MaxPreyBodySize"],
    "nameOnTameChance": ["nameOnTameChance", "NameOnTameChance"],
    "Body": ["Body", "BodyDef"],
    "Juv age (years)": ["Juv age (years)", "Juv age", "JuvAge", "Juvenile age"],
    "Adult age (years)": ["Adult age (years)", "Adult age", "AdultAge"],
    "Litter size": ["Litter size", "LitterSize", "Litter size (avg)"],
    "TradeTags": ["TradeTags", "Trade Tags", "tradeTags", "trade tags"],
    "specialTrainables": [
        "specialTrainables",
        "special Trainables",
        "Special Trainables",
        "SpecialTrainables",
        "specialtrainables",
        "special trainables",
    ],
    "Combat power": ["Combat power", "combatPower", "CombatPower"],
    "ecoSystemWeight": ["ecoSystemWeight", "Eco system weight", "EcoSystemWeight"],
    "WildBiomes": ["WildBiomes", "Wild biomes", "Wild Biomes", "Biomes", "biomes"],
    "Costal": ["Costal", "Coastal", "costal", "coastal"],
    "PackAnimal": ["PackAnimal", "Pack animal", "packAnimal", "Pack Animal"],
    "Eco system number": ["Eco system number", "EcoSystemNumber", "Eco system Number", "ecosystem number"],
    "Toxic eco system number": [
        "Toxic eco system number",
        "Toxic Eco system number",
        "Toxic ecosystem number",
        "ToxicEcoSystemNumber",
    ],
    "MayRequire": ["MayRequire", "May Require", "mayrequire", "May require"],
    "Wild group size": ["Wild group size", "wildGroupSize", "wild group size"],
    "CanArriveManhunter": ["CanArriveManhunter", "canArriveManhunter", "can arrive manhunter"],
    "Head damage": ["Head damage", "HeadDamage", "head damage"],
    "Bite damage": ["Bite damage", "BiteDamage", "bite damage"],
    "Paw claw/punch damage": ["Paw claw/punch damage", "Paw damage", "paw damage"],
    "Poke/leg claws damage": ["Poke/leg claws damage", "Leg damage", "leg damage"],
    "Horn/Antler/Tusks damage": ["Horn/Antler/Tusks damage", "Horn damage"],
    "LeatherDef": ["Leather def", "LeatherDef", "Leather_Def", "Leather"],
    "IsMammal": ["IsMammal", "Is Mammal"],
    "Ectothermic": ["Ectothermic"],
    "AgroAtSlaughter": ["AgroAtSlaughter", "Agro At Slaughter"],
    "CannotBeAugmented": ["CannotBeAugmented", "Cannot Be Augmented"],
    "CannotBeMutated": ["CannotBeMutated", "Cannot Be Mutated"],
    "NoFlee": ["NoFlee", "No Flee"],
    "IsScavenger": ["IsScavenger", "Is Scavenger"],
    "TakingCareOfOffspring": ["TakingCareOfOffspring", "Taking Care Of Offspring"],
    "CannotChew": ["CannotChew", "Cannot Chew"],
}

MOD_EXTENSION_CLASS_MAP = {
    "IsMammal": "ZoologyMod.ModExtension_IsMammal, ZoologyMod",
    "Ectothermic": "ZoologyMod.ModExtension_Ectothermic, ZoologyMod",
    "AgroAtSlaughter": "ZoologyMod.ModExtension_AgroAtSlaughter, ZoologyMod",
    "CannotBeAugmented": "ZoologyMod.ModExtension_CannotBeAugmented, ZoologyMod",
    "CannotBeMutated": "ZoologyMod.ModExtension_CannotBeMutated, ZoologyMod",
    "NoFlee": "ZoologyMod.ModExtension_NoFlee, ZoologyMod",
    "CannotChew": "ZoologyMod.ModExtension_CannotChew, ZoologyMod",
    "TakingCareOfOffspring": "ZoologyMod.ModExtensiom_Chlidcare, ZoologyMod",
    "IsScavenger": "ZoologyMod.ModExtension_IsScavenger, ZoologyMod",
}


def load_config() -> Dict:
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            return {}
    return {}


def save_config(cfg: Dict):
    try:
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            json.dump(cfg, f, indent=2, ensure_ascii=False)
    except Exception:
        pass


class ConsolePromptProvider:
    def text(self, question: str, default: Optional[str] = None, required: bool = False) -> str:
        while True:
            suffix = f" [{default}]" if default not in (None, "") else ""
            raw = input(f"{question}{suffix}: ").strip()
            if raw:
                return raw
            if default not in (None, ""):
                return str(default)
            if not required:
                return ""
            print("Value is required.")

    def yes_no(self, question: str, default: bool = False) -> bool:
        d = "y" if default else "n"
        while True:
            raw = input(f"{question} [y/n, default {d}]: ").strip().lower()
            if not raw:
                return default
            if raw in ("y", "yes"):
                return True
            if raw in ("n", "no"):
                return False
            print("Please answer y or n.")

    def choice(self, question: str, options: List[str], default: Optional[str] = None, required: bool = False) -> str:
        options = [str(o).strip() for o in options if str(o).strip()]
        if not options:
            return self.text(question, default=default, required=required)
        print(question)
        for idx, item in enumerate(options, start=1):
            print(f"  {idx}. {item}")
        while True:
            suffix = f" [{default}]" if default not in (None, "") else ""
            raw = input(f"Choose number or enter value{suffix}: ").strip()
            if not raw:
                if default not in (None, ""):
                    return str(default)
                if required:
                    print("Value is required.")
                    continue
                return ""
            if raw.isdigit():
                n = int(raw)
                if 1 <= n <= len(options):
                    return options[n - 1]
            return raw


class TkPromptProvider:
    def __init__(self, parent: tk.Tk):
        self.parent = parent

    def text(self, question: str, default: Optional[str] = None, required: bool = False) -> str:
        while True:
            raw = simpledialog.askstring("Input", question, initialvalue=(default or ""), parent=self.parent)
            if raw is None:
                if required:
                    messagebox.showwarning("Required", "This field is required.", parent=self.parent)
                    continue
                return default or ""
            raw = raw.strip()
            if raw:
                return raw
            if default not in (None, ""):
                return str(default)
            if not required:
                return ""
            messagebox.showwarning("Required", "This field is required.", parent=self.parent)

    def yes_no(self, question: str, default: bool = False) -> bool:
        msg = f"{question}\n\nDefault: {'Yes' if default else 'No'}"
        return bool(messagebox.askyesno("Input", msg, parent=self.parent))

    def choice(self, question: str, options: List[str], default: Optional[str] = None, required: bool = False) -> str:
        options = [str(o).strip() for o in options if str(o).strip()]
        if not options:
            return self.text(question, default=default, required=required)
        choices = "\n".join(f"{idx}. {item}" for idx, item in enumerate(options, start=1))
        prompt = f"{question}\n\n{choices}\n\nType number or value"
        while True:
            raw = simpledialog.askstring("Select", prompt, initialvalue=(default or ""), parent=self.parent)
            if raw is None:
                if required:
                    messagebox.showwarning("Required", "This field is required.", parent=self.parent)
                    continue
                return default or ""
            raw = raw.strip()
            if not raw:
                if default not in (None, ""):
                    return str(default)
                if not required:
                    return ""
                messagebox.showwarning("Required", "This field is required.", parent=self.parent)
                continue
            if raw.isdigit():
                n = int(raw)
                if 1 <= n <= len(options):
                    return options[n - 1]
            return raw


PROMPTS = ConsolePromptProvider()


def is_excel_source(path: str) -> bool:
    return os.path.splitext(str(path))[1].lower() in (".xlsx", ".xlsm", ".xls")


def infer_game_root(path: Optional[str]) -> Optional[str]:
    if not path:
        return None
    raw = os.path.normpath(str(path).strip())
    if not raw:
        return None
    if os.path.basename(raw).strip().lower() == "rimworld":
        return raw
    m = re.search(r"^(.*?)[\\/]+Data(?:[\\/].*)?$", raw, flags=re.I)
    if m:
        root = os.path.normpath(m.group(1))
        return root if root else None
    return None


def resolve_thingdefs_search_dirs(game_root_dir: Optional[str]) -> List[str]:
    candidates = []
    if game_root_dir and str(game_root_dir).strip():
        primary = os.path.normpath(str(game_root_dir).strip())
        if os.path.basename(primary).strip().lower() == "thingdefs_races":
            candidates.append(primary)

        # Backward-compatible: user may pass either game root or a ThingDefs_Races path.
        candidates.append(os.path.join(primary, "Data", "Core", "Defs", "ThingDefs_Races"))
        candidates.append(os.path.join(primary, "Data", "Odyssey", "Defs", "ThingDefs_Races"))

        inferred_root = infer_game_root(primary)
        if inferred_root:
            candidates.append(os.path.join(inferred_root, "Data", "Core", "Defs", "ThingDefs_Races"))
            candidates.append(os.path.join(inferred_root, "Data", "Odyssey", "Defs", "ThingDefs_Races"))

    candidates.extend([DEFAULT_CORE_THINGDEFS_DIR, DEFAULT_ODYSSEY_THINGDEFS_DIR])

    out = []
    seen = set()
    for p in candidates:
        norm_p = os.path.normpath(p)
        key = norm_p.lower()
        if key in seen:
            continue
        seen.add(key)
        if os.path.exists(norm_p):
            out.append(norm_p)
    return out


def norm(value) -> str:
    if value is None:
        return ""
    return re.sub(r"\s+", " ", str(value).strip()).lower()


def is_no_like(value) -> bool:
    if value is None:
        return True
    s = str(value).strip().lower()
    return s in ("", "no", "none")


def is_truthy(value) -> bool:
    if value is None:
        return False
    return str(value).strip().lower() in ("true", "1", "yes", "y", "t")


def try_parse_number(value) -> Optional[float]:
    try:
        if value is None:
            return None
        s = str(value).strip()
        if s.lower() in ("", "no", "none"):
            return None
        if s.endswith("%"):
            return float(s[:-1]) / 100.0
        s = s.replace(",", ".")
        return float(s)
    except Exception:
        return None


def format_prob_value(value) -> str:
    num = try_parse_number(value)
    if num is None:
        return str(value).strip()
    if num > 1:
        num = num / 100.0
    out = ("{:.6f}".format(num)).rstrip("0").rstrip(".")
    return out if out else "0"


def normalize_trainability_value(value) -> Optional[str]:
    if value is None:
        return None
    s = str(value).strip()
    if s == "":
        return None
    low = s.lower()
    if low == "no":
        return None
    if low == "none":
        return "None"
    return s


def split_and_strip(value) -> List[str]:
    if value is None:
        return []
    if isinstance(value, (list, tuple)):
        return [str(x).strip() for x in value if str(x).strip()]
    s = str(value).strip()
    if not s or s.lower() in ("no", "none"):
        return []
    return [x.strip() for x in re.split(r",\s*|\n+", s) if x.strip()]


def find_alias_in_row(row, candidates: List[str]) -> Optional[str]:
    for c in candidates:
        if c in row.index:
            return c
        for idx in row.index:
            if str(idx).strip().lower() == str(c).strip().lower():
                return idx
    return None


def get_row_value(row, canonical_key: str):
    aliases = COLUMN_ALIASES.get(canonical_key, [canonical_key])
    alias = find_alias_in_row(row, aliases)
    if alias:
        return row.get(alias, "")
    return row.get(canonical_key, "")


def extract_def_name_from_xml_name(xml_name_raw) -> str:
    if xml_name_raw is None:
        return ""
    xml_name = str(xml_name_raw).strip()
    m = re.search(r"<li[^>]*>(.*?)</li>", xml_name)
    if m:
        return m.group(1).strip()
    return xml_name


def get_parent_abstract_from_row(row) -> str:
    parent_abstract = (
        str(row.get("Parrent abstract", "")).strip()
        or str(row.get("Parent abstract", "")).strip()
        or "AnimalThingBase"
    )
    if parent_abstract.lower() in ("", "none"):
        parent_abstract = "AnimalThingBase"
    return parent_abstract


def read_table(source_path: str, sheet_name: Optional[str] = None) -> pd.DataFrame:
    def _normalize_df(df: pd.DataFrame) -> pd.DataFrame:
        num_re = re.compile(r"^[+-]?\d+(?:\.\d+)?$")

        def _cell_to_text(v):
            if v is None:
                return ""
            try:
                if pd.isna(v):
                    return ""
            except Exception:
                pass
            if isinstance(v, float):
                return ("{:.15g}".format(v)).rstrip()
            if isinstance(v, int):
                return str(v)
            s = str(v).strip()
            if num_re.match(s):
                try:
                    if "." in s:
                        return ("{:.6f}".format(float(s))).rstrip("0").rstrip(".")
                    return str(int(float(s)))
                except Exception:
                    return s
            return s

        out = df.apply(lambda col: col.map(_cell_to_text))
        return out.fillna("")

    if not source_path or not os.path.exists(source_path):
        raise RuntimeError(f"Table source not found: {source_path}")
    if is_excel_source(source_path):
        try:
            kwargs = {"dtype": str, "keep_default_na": False}
            if sheet_name:
                kwargs["sheet_name"] = sheet_name
            return _normalize_df(pd.read_excel(source_path, **kwargs))
        except ImportError:
            try:
                kwargs = {"dtype": str, "keep_default_na": False, "engine": "calamine"}
                if sheet_name:
                    kwargs["sheet_name"] = sheet_name
                return _normalize_df(pd.read_excel(source_path, **kwargs))
            except Exception as e:
                raise RuntimeError(
                    "Excel support requires openpyxl (or python-calamine). Install with: pip install openpyxl"
                ) from e
    return _normalize_df(pd.read_csv(source_path, sep="\t", dtype=str, keep_default_na=False))


def generate_litter_curve(mean_litter: float) -> Optional[List[Tuple[float, float]]]:
    if mean_litter is None or mean_litter <= 1:
        return None
    template_points = [(0.5, 0), (1, 0.2), (2, 1), (3, 1), (4, 0.2), (4.5, 0)]
    original_mean = 2.5
    scale = mean_litter / original_mean
    return [(x * scale, y) for x, y in template_points]


def add_litter_curve(parent_el: LET._Element, mean_litter: float):
    points = generate_litter_curve(mean_litter)
    if not points:
        return
    curve = LET.SubElement(parent_el, "litterSizeCurve")
    for x, y in points:
        li = LET.SubElement(curve, "li")
        LET.SubElement(li, "x").text = ("{:.6f}".format(x)).rstrip("0").rstrip(".")
        LET.SubElement(li, "y").text = ("{:.6f}".format(y)).rstrip("0").rstrip(".")


def _infer_label_noloc(label: Optional[str]) -> Optional[str]:
    if not label:
        return None
    parts = str(label).strip().split()
    if len(parts) >= 2 and parts[0].lower() in ("left", "right"):
        return " ".join(parts[1:]).strip()
    if len(parts) > 1:
        return parts[-1]
    return None


def _make_capacities_element(capacities_str) -> Optional[LET._Element]:
    caps = split_and_strip(capacities_str)
    if not caps:
        return None
    caps_el = LET.Element("capacities")
    for c in caps:
        li = LET.SubElement(caps_el, "li")
        li.text = c
    return caps_el


def _make_tool_li(
    *,
    label=None,
    label_nolocation=None,
    capacities=None,
    power=None,
    cooldown=None,
    linked=None,
    ensure_always=False,
    chance_factor=None,
    restricted_gender=None,
    class_attr=None,
    ap_blunt=None,
    ap_sharp=None,
    stun=None,
) -> LET._Element:
    li = LET.Element("li")
    if class_attr:
        li.set("Class", class_attr)
    if label is not None:
        LET.SubElement(li, "label").text = str(label)
    if label_nolocation is not None:
        LET.SubElement(li, "labelNoLocation").text = str(label_nolocation)
    caps_el = _make_capacities_element(capacities)
    if caps_el is not None:
        li.append(caps_el)
    if power is not None:
        LET.SubElement(li, "power").text = str(power)
    if ap_blunt is not None:
        LET.SubElement(li, "armorPenetrationBlunt").text = str(ap_blunt)
    if ap_sharp is not None:
        LET.SubElement(li, "armorPenetrationSharp").text = str(ap_sharp)
    if cooldown is not None:
        LET.SubElement(li, "cooldownTime").text = str(cooldown)
    if linked is not None:
        LET.SubElement(li, "linkedBodyPartsGroup").text = str(linked)
    if ensure_always:
        LET.SubElement(li, "ensureLinkedBodyPartsGroupAlwaysUsable").text = "true"
    if chance_factor is not None:
        LET.SubElement(li, "chanceFactor").text = str(chance_factor)
    if restricted_gender is not None:
        LET.SubElement(li, "restrictedGender").text = str(restricted_gender)
        if li.get("Class") is None:
            li.set("Class", "ZoologyMod.ToolWithGender, ZoologyMod")

    if stun is not None and not is_no_like(stun):
        lab = (str(label).strip().lower() if label is not None else "")
        if lab != "head":
            sa = LET.SubElement(li, "surpriseAttack")
            emd = LET.SubElement(sa, "extraMeleeDamages")
            stun_li = LET.SubElement(emd, "li")
            LET.SubElement(stun_li, "def").text = "Stun"
            LET.SubElement(stun_li, "amount").text = str(stun).strip()
    return li


def _find_attack_interval_raw(row, col_prefix: str):
    cand_names = [f"{col_prefix} attack interval"]
    short = re.sub(r"\bdamage\b", "", col_prefix, flags=re.I).strip()
    if short and short != col_prefix:
        cand_names.append(f"{short} attack interval")
    for n in cand_names:
        alias = find_alias_in_row(row, [n])
        if alias:
            return row.get(alias)
    pref_tokens = [t for t in re.findall(r"\w+", col_prefix.lower()) if t not in ("damage",)]
    for c in row.index:
        low = str(c).lower()
        if "attack" in low and "interval" in low:
            if any(t and t in low for t in pref_tokens):
                return row.get(c)
    return None


def build_tools_vanilla(row) -> Optional[LET._Element]:
    tools = LET.Element("tools")
    stun_val = get_row_value(row, "Stun")

    head_power = get_row_value(row, "Head damage")
    head_interval = row.get("Head attack interval") if "Head attack interval" in row.index else None
    if not is_no_like(head_power):
        tools.append(
            _make_tool_li(
                label="head",
                capacities="Blunt",
                power=head_power,
                cooldown=head_interval,
                linked="HeadAttackTool",
                ensure_always=True,
                chance_factor=0.2,
                stun=stun_val,
            )
        )

    def add_paired(prefix, label_col, linked_col, capacities_col):
        dmg = get_row_value(row, f"{prefix} damage") if not prefix.endswith("damage") else get_row_value(row, prefix)
        if is_no_like(dmg):
            return
        labels = split_and_strip(row.get(label_col))
        linkeds = split_and_strip(row.get(linked_col))
        caps = row.get(capacities_col) if capacities_col in row.index else None
        cd_vals = split_and_strip(_find_attack_interval_raw(row, prefix))
        n = max(len(labels), len(linkeds), 1)
        if not labels:
            labels = [""] * n
        if not linkeds:
            linkeds = [""] * n
        while len(labels) < n:
            labels.append(labels[-1])
        while len(linkeds) < n:
            linkeds.append(linkeds[-1])
        power_val = get_row_value(row, prefix)
        for i in range(n):
            cd = cd_vals[i] if i < len(cd_vals) else (cd_vals[-1] if cd_vals else None)
            lab = labels[i] or None
            tools.append(
                _make_tool_li(
                    label=lab,
                    label_nolocation=_infer_label_noloc(lab),
                    capacities=caps,
                    power=power_val,
                    cooldown=cd,
                    linked=linkeds[i] or None,
                    stun=stun_val,
                )
            )

    add_paired("Poke/leg claws damage", "Poke/leg claws label", "Poke/leg claws linkedBodyPartsGroup", "Poke/leg claws capacities")
    add_paired("Paw claw/punch damage", "Paw claw/punch label", "Paw claw/punch linkedBodyPartsGroup", "Paw claw/punch capacities")

    horn_damage = get_row_value(row, "Horn/Antler/Tusks damage")
    if not is_no_like(horn_damage):
        male_only = row.get("Horn/Antler/Tusks Male only")
        tools.append(
            _make_tool_li(
                label=row.get("Horn/Antler/Tusks label") or None,
                capacities=row.get("Horn/Antler/Tusks capacities") if "Horn/Antler/Tusks capacities" in row.index else None,
                power=horn_damage,
                cooldown=row.get("Horn/Antler/Tusks attack interval"),
                linked=row.get("Horn/Antler/Tusks linkedBodyPartsGroup") or None,
                restricted_gender="Male" if is_truthy(male_only) else None,
                stun=stun_val,
            )
        )

    bite_damage = get_row_value(row, "Bite damage")
    if not is_no_like(bite_damage):
        tools.append(
            _make_tool_li(
                label=row.get("Bite label") or row.get("Bite") or None,
                capacities=row.get("Bite capacities") if "Bite capacities" in row.index else None,
                power=bite_damage,
                cooldown=row.get("Bite attack interval"),
                linked=row.get("Bite linkedBodyPartsGroup") or None,
                stun=stun_val,
            )
        )

    return tools if len(list(tools)) else None


def build_tools_ce(ce_row, vanilla_row=None) -> Optional[LET._Element]:
    if ce_row is None:
        return None

    row_for_labels = vanilla_row if vanilla_row is not None else ce_row
    stun_val = get_row_value(ce_row, "Stun")
    if is_no_like(stun_val):
        stun_val = get_row_value(row_for_labels, "Stun")

    tools = LET.Element("tools")

    head_power = ce_row.get("Head damage")
    head_ap = ce_row.get("Head Blunt AP")
    head_interval = ce_row.get("Head attack interval")
    if not is_no_like(head_power):
        tools.append(
            _make_tool_li(
                label="head",
                capacities="Blunt",
                power=head_power,
                cooldown=head_interval,
                linked="HeadAttackTool",
                ensure_always=True,
                chance_factor=0.2,
                class_attr="CombatExtended.ToolCE",
                ap_blunt=head_ap if not is_no_like(head_ap) else None,
                stun=stun_val,
            )
        )

    def add_paired_ce(prefix, label_col, linked_col, capacities_col):
        dmg = ce_row.get(f"{prefix} damage")
        if is_no_like(dmg):
            return
        labels = split_and_strip(row_for_labels.get(label_col))
        linkeds = split_and_strip(row_for_labels.get(linked_col))
        caps = row_for_labels.get(capacities_col) if capacities_col in row_for_labels.index else None
        cd_vals = split_and_strip(_find_attack_interval_raw(ce_row, prefix))
        if not cd_vals:
            cd_vals = split_and_strip(_find_attack_interval_raw(row_for_labels, prefix))
        n = max(len(labels), len(linkeds), 1)
        if not labels:
            labels = [""] * n
        if not linkeds:
            linkeds = [""] * n
        while len(labels) < n:
            labels.append(labels[-1])
        while len(linkeds) < n:
            linkeds.append(linkeds[-1])
        apb = ce_row.get(f"{prefix} Blunt AP")
        aps = ce_row.get(f"{prefix} Sharp AP")
        for i in range(n):
            cd = cd_vals[i] if i < len(cd_vals) else (cd_vals[-1] if cd_vals else None)
            lab = labels[i] or None
            tools.append(
                _make_tool_li(
                    label=lab,
                    label_nolocation=_infer_label_noloc(lab),
                    capacities=caps,
                    power=dmg,
                    cooldown=cd,
                    linked=linkeds[i] or None,
                    class_attr="CombatExtended.ToolCE",
                    ap_blunt=apb if not is_no_like(apb) else None,
                    ap_sharp=aps if not is_no_like(aps) else None,
                    stun=stun_val,
                )
            )

    add_paired_ce("Paw claw/punch", "Paw claw/punch label", "Paw claw/punch linkedBodyPartsGroup", "Paw claw/punch capacities")
    add_paired_ce("Poke/leg claws", "Poke/leg claws label", "Poke/leg claws linkedBodyPartsGroup", "Poke/leg claws capacities")

    horn_damage = ce_row.get("Horn/Antler/Tusks damage")
    if not is_no_like(horn_damage):
        tools.append(
            _make_tool_li(
                label=row_for_labels.get("Horn/Antler/Tusks label") if "Horn/Antler/Tusks label" in row_for_labels.index else None,
                capacities=row_for_labels.get("Horn/Antler/Tusks capacities") if "Horn/Antler/Tusks capacities" in row_for_labels.index else None,
                power=horn_damage,
                cooldown=ce_row.get("Horn/Antler/Tusks attack interval"),
                linked=row_for_labels.get("Horn/Antler/Tusks linkedBodyPartsGroup")
                if "Horn/Antler/Tusks linkedBodyPartsGroup" in row_for_labels.index
                else None,
                class_attr="CombatExtended.ToolCE",
                ap_blunt=ce_row.get("Horn/Antler/Tusks Blunt AP")
                if not is_no_like(ce_row.get("Horn/Antler/Tusks Blunt AP"))
                else None,
                ap_sharp=ce_row.get("Horn/Antler/Tusks Sharp AP")
                if not is_no_like(ce_row.get("Horn/Antler/Tusks Sharp AP"))
                else None,
                restricted_gender="Male" if is_truthy(row_for_labels.get("Horn/Antler/Tusks Male only")) else None,
                stun=stun_val,
            )
        )

    bite_damage = ce_row.get("Bite damage")
    if not is_no_like(bite_damage):
        tools.append(
            _make_tool_li(
                label=row_for_labels.get("Bite label") if "Bite label" in row_for_labels.index else None,
                capacities=row_for_labels.get("Bite capacities") if "Bite capacities" in row_for_labels.index else None,
                power=bite_damage,
                cooldown=ce_row.get("Bite attack interval"),
                linked=row_for_labels.get("Bite linkedBodyPartsGroup") if "Bite linkedBodyPartsGroup" in row_for_labels.index else None,
                class_attr="CombatExtended.ToolCE",
                ap_blunt=ce_row.get("Bite Blunt AP") if not is_no_like(ce_row.get("Bite Blunt AP")) else None,
                ap_sharp=ce_row.get("Bite Sharp AP") if not is_no_like(ce_row.get("Bite Sharp AP")) else None,
                stun=stun_val,
            )
        )

    return tools if len(list(tools)) else None


def prompt_text(question: str, default: Optional[str] = None, required: bool = False) -> str:
    return PROMPTS.text(question, default=default, required=required)


def prompt_yes_no(question: str, default: bool = False) -> bool:
    return PROMPTS.yes_no(question, default=default)


def prompt_choice(question: str, options: List[str], default: Optional[str] = None, required: bool = False) -> str:
    if hasattr(PROMPTS, "choice"):
        return PROMPTS.choice(question, options, default=default, required=required)
    return prompt_text(question, default=default, required=required)


def find_existing_defs(game_root_dir: str, def_name: str) -> Tuple[Optional[LET._Element], Optional[LET._Element]]:
    search_dirs = resolve_thingdefs_search_dirs(game_root_dir)
    if not search_dirs:
        return None, None
    thing = None
    pawn = None
    for search_root in search_dirs:
        for root_dir, _, files in os.walk(search_root):
            for name in files:
                if not name.lower().endswith(".xml"):
                    continue
                path = os.path.join(root_dir, name)
                try:
                    xml = LET.parse(path).getroot()
                except Exception:
                    continue
                if thing is None:
                    found = xml.xpath(f"//ThingDef[defName='{def_name}']")
                    if found:
                        thing = copy.deepcopy(found[0])
                if pawn is None:
                    found = xml.xpath(f"//PawnKindDef[defName='{def_name}']")
                    if found:
                        pawn = copy.deepcopy(found[0])
                if thing is not None and pawn is not None:
                    return thing, pawn
    return thing, pawn


RACE_SOUND_OPTION_TAGS = ("soundMeleeHitPawn", "soundMeleeHitBuilding", "soundMeleeMiss", "soundEating")
_RACE_SOUND_OPTIONS_CACHE: Dict[str, Dict[str, List[str]]] = {}


def collect_race_sound_options(game_root_dir: str, tags: Tuple[str, ...] = RACE_SOUND_OPTION_TAGS) -> Dict[str, List[str]]:
    search_dirs = resolve_thingdefs_search_dirs(game_root_dir)
    cache_key = "|".join(os.path.normpath(p).lower() for p in search_dirs) + "::" + "|".join(tags)
    if cache_key in _RACE_SOUND_OPTIONS_CACHE:
        return _RACE_SOUND_OPTIONS_CACHE[cache_key]

    found = {t: set() for t in tags}
    for search_root in search_dirs:
        for root_dir, _, files in os.walk(search_root):
            for name in files:
                if not name.lower().endswith(".xml"):
                    continue
                path = os.path.join(root_dir, name)
                try:
                    xml = LET.parse(path).getroot()
                except Exception:
                    continue
                for race in xml.xpath("//ThingDef[race]/race"):
                    for tag in tags:
                        el = race.find(tag)
                        if el is not None and el.text and el.text.strip():
                            found[tag].add(el.text.strip())

    out = {k: sorted(v) for k, v in found.items()}
    _RACE_SOUND_OPTIONS_CACHE[cache_key] = out
    return out


def extract_sound_defaults(existing_thing: Optional[LET._Element], adult_stage_def: str) -> Dict[str, str]:
    defaults = {}
    if existing_thing is None:
        return defaults
    adults = existing_thing.xpath(f"./race/lifeStageAges/li[def='{adult_stage_def}']")
    if not adults:
        adults = existing_thing.xpath("./race/lifeStageAges/li[def='AnimalAdult']")
    if not adults:
        adults = existing_thing.xpath("./race/lifeStageAges/li[def='EusocialInsectAdult']")
    if not adults:
        return defaults
    adult = adults[0]
    for key in ("soundWounded", "soundDeath", "soundCall", "soundAngry"):
        el = adult.find(key)
        if el is not None and el.text and el.text.strip():
            defaults[key] = el.text.strip()
    return defaults


def extract_race_sound_defaults(existing_thing: Optional[LET._Element], tags: Tuple[str, ...] = RACE_SOUND_OPTION_TAGS) -> Dict[str, str]:
    out = {}
    if existing_thing is None:
        return out
    race = existing_thing.find("./race")
    if race is None:
        return out
    for tag in tags:
        el = race.find(tag)
        if el is not None and el.text and el.text.strip():
            out[tag] = el.text.strip()
    return out


def extract_lifestage_graphics_defaults(existing_pawn: Optional[LET._Element]) -> List[Dict[str, str]]:
    out = []
    if existing_pawn is None:
        return out
    life_stages = existing_pawn.find("./lifeStages")
    if life_stages is None:
        return out
    for li in life_stages.findall("./li"):
        item = {}
        label = li.find("label")
        if label is not None and label.text:
            item["label"] = label.text.strip()
        for pref, tag in (("body", "bodyGraphicData"), ("dess", "dessicatedBodyGraphicData"), ("swim", "swimmingGraphicData")):
            gr = li.find(tag)
            if gr is None:
                continue
            tex = gr.find("texPath")
            ds = gr.find("drawSize")
            if tex is not None and tex.text:
                item[f"{pref}_texPath"] = tex.text.strip()
            if ds is not None and ds.text:
                item[f"{pref}_drawSize"] = ds.text.strip()
        out.append(item)
    return out


def build_manual_inputs(
    def_name: str,
    parent_abstract: str,
    existing_thing: Optional[LET._Element],
    existing_pawn: Optional[LET._Element],
    game_root_dir: str,
) -> Dict:
    if parent_abstract.strip().lower() == "baseinsect":
        default_stage_defs = {
            "baby": "EusocialInsectLarva",
            "juvenile": "EusocialInsectJuvenile",
            "adult": "EusocialInsectAdult",
        }
    else:
        default_stage_defs = {
            "baby": "AnimalBaby",
            "juvenile": "AnimalJuvenile",
            "adult": "AnimalAdult",
        }

    print("")
    print(f"Manual data for '{def_name}'")
    print("- sounds")
    print("- race melee/eating sounds")
    print("- race life-stage defs")
    print("- pawn kind life-stage labels")
    print("- graphics for each life stage")
    print("")

    stage_defs = {
        "baby": prompt_text("Race life stage def (baby)", default_stage_defs["baby"], required=True),
        "juvenile": prompt_text("Race life stage def (juvenile)", default_stage_defs["juvenile"], required=True),
        "adult": prompt_text("Race life stage def (adult)", default_stage_defs["adult"], required=True),
    }

    sound_defaults = extract_sound_defaults(existing_thing, stage_defs["adult"])
    if not sound_defaults:
        sound_defaults = extract_sound_defaults(existing_thing, default_stage_defs["adult"])
    if not sound_defaults:
        sound_defaults = {
            "soundWounded": f"Pawn_{def_name}_Wounded",
            "soundDeath": f"Pawn_{def_name}_Death",
            "soundCall": f"Pawn_{def_name}_Call",
            "soundAngry": f"Pawn_{def_name}_Angry",
        }

    sounds = {
        "soundWounded": prompt_text("Adult soundWounded", sound_defaults.get("soundWounded"), required=True),
        "soundDeath": prompt_text("Adult soundDeath", sound_defaults.get("soundDeath"), required=True),
        "soundCall": prompt_text("Adult soundCall", sound_defaults.get("soundCall"), required=True),
        "soundAngry": prompt_text("Adult soundAngry", sound_defaults.get("soundAngry"), required=True),
    }

    race_sound_defaults = extract_race_sound_defaults(existing_thing)
    race_sound_options = collect_race_sound_options(game_root_dir)
    for tag in RACE_SOUND_OPTION_TAGS:
        options = race_sound_options.get(tag, [])
        default = race_sound_defaults.get(tag) or (options[0] if options else "")
        sounds[tag] = prompt_choice(f"Race {tag}", options, default=default, required=True)

    stage_defaults = extract_lifestage_graphics_defaults(existing_pawn)
    while len(stage_defaults) < 3:
        stage_defaults.append({})

    auto_base = f"Things/Pawn/Animal/{def_name}/{def_name}"
    auto_dess = f"Things/Pawn/Animal/{def_name}/Dessicated_{def_name}"
    auto_sizes = ["1.0", "1.5", "2.0"]

    stage_keys = ["baby", "juvenile", "adult"]
    stage_payloads = []
    for idx, key in enumerate(stage_keys):
        d = stage_defaults[idx] if idx < len(stage_defaults) else {}
        label_default = d.get("label")
        label = prompt_text(f"Pawn life stage label ({key}, optional)", label_default, required=False)
        body_tex = prompt_text(f"{key} body texPath", d.get("body_texPath") or auto_base, required=True)
        body_size = prompt_text(f"{key} body drawSize", d.get("body_drawSize") or auto_sizes[idx], required=True)
        dess_tex = prompt_text(f"{key} dessicated texPath", d.get("dess_texPath") or auto_dess, required=True)
        dess_size = prompt_text(f"{key} dessicated drawSize", d.get("dess_drawSize") or auto_sizes[idx], required=True)
        swim_default_enabled = bool(d.get("swim_texPath") or d.get("swim_drawSize"))
        has_swim = prompt_yes_no(f"{key} has swimmingGraphicData?", default=swim_default_enabled)
        swim_tex = ""
        swim_size = ""
        if has_swim:
            swim_tex = prompt_text(f"{key} swimming texPath", d.get("swim_texPath"), required=True)
            swim_size = prompt_text(f"{key} swimming drawSize", d.get("swim_drawSize"), required=True)
        stage_payloads.append(
            {
                "key": key,
                "label": label,
                "body_texPath": body_tex,
                "body_drawSize": body_size,
                "dess_texPath": dess_tex,
                "dess_drawSize": dess_size,
                "has_swim": has_swim,
                "swim_texPath": swim_tex,
                "swim_drawSize": swim_size,
            }
        )

    return {"sounds": sounds, "stage_defs": stage_defs, "stages": stage_payloads}


def add_text_if(parent: LET._Element, tag: str, value):
    if is_no_like(value):
        return
    LET.SubElement(parent, tag).text = str(value).strip()


def _find_direct_child(parent: LET._Element, tag: str) -> Optional[LET._Element]:
    for ch in list(parent):
        if isinstance(ch.tag, str) and ch.tag == tag:
            return ch
    return None


def _ensure_direct_child(parent: LET._Element, tag: str) -> LET._Element:
    found = _find_direct_child(parent, tag)
    if found is not None:
        return found
    return LET.SubElement(parent, tag)


def _remove_direct_children(parent: LET._Element, tag: str):
    for ch in list(parent):
        if isinstance(ch.tag, str) and ch.tag == tag:
            parent.remove(ch)


def _set_direct_text(parent: LET._Element, tag: str, value: str):
    el = _ensure_direct_child(parent, tag)
    el.text = str(value)


def _remove_from_container(container: Optional[LET._Element], tag: str):
    if container is None:
        return
    for ch in list(container):
        if isinstance(ch.tag, str) and ch.tag == tag:
            container.remove(ch)


def _parse_litter_mean(value) -> Optional[float]:
    if is_no_like(value):
        return None
    s = str(value).strip()
    mean = try_parse_number(s)
    if mean is None and "~" in s:
        parts = s.split("~", 1)
        mi = try_parse_number(parts[0])
        ma = try_parse_number(parts[1])
        if mi is not None and ma is not None:
            mean = (mi + ma) / 2.0
    return mean


def _parent_has_key(parent_common: Dict, key: str) -> bool:
    if key in parent_common:
        return True
    low = str(key).strip().lower()
    for k in parent_common.keys():
        if str(k).strip().lower() == low:
            return True
    return False


def should_set_inherit_false(parent_name: str, base_parent_name: str) -> bool:
    return str(parent_name or "").strip().lower() != str(base_parent_name or "").strip().lower()


def append_special_trainable(parent_el: LET._Element, value: str):
    li = LET.SubElement(parent_el, "li")
    li.set("MayRequire", ODYSSEY_MAYREQUIRE)
    li.text = str(value).strip()


def _append_mod_extension(mod_extensions_el: LET._Element, col: str, raw_value) -> bool:
    class_str = MOD_EXTENSION_CLASS_MAP.get(col)
    if not class_str:
        return False
    if raw_value is None:
        return False
    s = str(raw_value).strip()
    if s == "" or s.lower() in ("no", "none"):
        return False
    if col == "IsScavenger":
        sval = s.lower()
        if sval not in ("flesh", "bone"):
            return False
        li = LET.SubElement(mod_extensions_el, "li")
        li.set("Class", class_str)
        if sval == "bone":
            LET.SubElement(li, "allowVeryRotten").text = "true"
        return True
    if is_truthy(s):
        li = LET.SubElement(mod_extensions_el, "li")
        li.set("Class", class_str)
        return True
    return False


def build_mod_extensions_for_parent(common: Dict) -> Optional[LET._Element]:
    mod_extensions = LET.Element("modExtensions")
    any_added = False
    for col in MOD_EXTENSION_CLASS_MAP:
        if col not in common:
            continue
        any_added = _append_mod_extension(mod_extensions, col, common.get(col)) or any_added
    return mod_extensions if any_added else None


def build_mod_extensions_for_child(row, p_common: Dict, include_all: bool) -> Optional[LET._Element]:
    mod_extensions = LET.Element("modExtensions")
    any_added = False
    for col in MOD_EXTENSION_CLASS_MAP:
        val = get_row_value(row, col)
        if not include_all and col in p_common and norm(val) == norm(p_common.get(col)):
            continue
        any_added = _append_mod_extension(mod_extensions, col, val) or any_added
    return mod_extensions if any_added else None


def _tool_signature(row) -> Tuple[str, ...]:
    cols = ["Head damage", "Poke/leg claws damage", "Horn/Antler/Tusks damage", "Bite damage", "Paw claw/punch damage"]
    return tuple(norm(get_row_value(row, c)) for c in cols)


def _common_value_and_differing(rows: List, canonical_key: str) -> Tuple[Optional[str], bool]:
    vals = []
    raw_vals = []
    for r in rows:
        v = get_row_value(r, canonical_key)
        vals.append(norm(v))
        raw_vals.append("" if v is None else str(v).strip())

    vals_non_empty = [v for v in vals if v != ""]
    if not vals_non_empty:
        return None, False
    if all(v == vals_non_empty[0] for v in vals_non_empty):
        for rv, nv in zip(raw_vals, vals):
            if nv != "":
                return rv, False
        return None, False
    return None, True


def _common_value(rows: List, canonical_key: str):
    common, _ = _common_value_and_differing(rows, canonical_key)
    return common


def _collect_group_rows(df: pd.DataFrame, parent_col_names: List[str], parent_value: str) -> List:
    rows = []
    for _, r in df.iterrows():
        pval = ""
        for c in parent_col_names:
            if c in r.index:
                pval = str(r.get(c, "")).strip()
                if pval:
                    break
        if pval == parent_value:
            rows.append(r)
    return rows


def compute_parent_common(animals_df: pd.DataFrame, row) -> Dict:
    parent_abstract = (
        str(row.get("Parrent abstract", "")).strip()
        or str(row.get("Parent abstract", "")).strip()
        or ""
    )
    parent_pawn = (
        str(row.get("Parrent Pawn kind abstract", "")).strip()
        or str(row.get("Parent Pawn kind abstract", "")).strip()
        or str(row.get("Parrent Pawn kind", "")).strip()
        or ""
    )

    out = {
        "thing_parent_name": parent_abstract if parent_abstract and parent_abstract != "None" else "",
        "pawn_parent_name": parent_pawn if parent_pawn and parent_pawn != "None" else "",
        "thing_common": {},
        "thing_differing": [],
        "pawn_common": {},
        "pawn_differing": [],
        "thing_tools_row": None,
        "thing_tools_signature": None,
    }

    if out["thing_parent_name"]:
        rows = _collect_group_rows(animals_df, ["Parrent abstract", "Parent abstract"], out["thing_parent_name"])
        if rows:
            for stat in [
                "MarketValue",
                "MoveSpeed",
                "Wildness",
                "FilthRate",
                "ComfyTemperatureMin",
                "ComfyTemperatureMax",
                "ArmorRating_Blunt",
                "ArmorRating_Sharp",
                "ToxicEnvironmentResistance",
                "baseBodySize",
                "baseHealthScale",
                "baseHungerRate",
                "lifeExpectancy",
                "gestationPeriodDays",
                "herdAnimal",
                "herdMigrationAllowed",
                "Foodtype",
                "roamMtbDays",
                "LeatherDef",
                "manhunterOnTameFailChance",
                "manhunterOnDamageChance",
                "petness",
                "nuzzleMtbHours",
                "mateMtbHours",
                "trainability",
                "PackAnimal",
                "predator",
                "maxPreyBodySize",
                "nameOnTameChance",
                "Body",
                "waterCellCost",
                "waterSeeker",
                "canFishForFood",
                "TradeTags",
                "specialTrainables",
                "IsMammal",
                "Ectothermic",
                "AgroAtSlaughter",
                "CannotBeAugmented",
                "CannotBeMutated",
                "NoFlee",
                "IsScavenger",
                "TakingCareOfOffspring",
                "CannotChew",
            ]:
                cv, is_diff = _common_value_and_differing(rows, stat)
                if cv is not None:
                    out["thing_common"][stat] = cv
                elif is_diff:
                    out["thing_differing"].append(stat)

            sigs = [_tool_signature(r) for r in rows]
            if sigs and all(s == sigs[0] for s in sigs) and any(any(x for x in s) for s in sigs):
                out["thing_tools_signature"] = sigs[0]
                out["thing_tools_row"] = rows[0]

    if out["pawn_parent_name"]:
        rows = _collect_group_rows(
            animals_df,
            ["Parrent Pawn kind abstract", "Parent Pawn kind abstract", "Parrent Pawn kind"],
            out["pawn_parent_name"],
        )
        if rows:
            for key in ["Combat power", "ecoSystemWeight", "CanArriveManhunter", "Wild group size", "moveSpeedFactorByTerrainTag (water)"]:
                cv, is_diff = _common_value_and_differing(rows, key)
                if cv is not None:
                    out["pawn_common"][key] = cv
                elif is_diff:
                    out["pawn_differing"].append(key)

    return out


def find_abstract_parent_name(game_root_dir: str, tag: str, abstract_name: str, default: str) -> str:
    search_dirs = resolve_thingdefs_search_dirs(game_root_dir)
    if not search_dirs:
        return default
    query = f"//{tag}[@Name='{abstract_name}']"
    for search_root in search_dirs:
        for root_dir, _, files in os.walk(search_root):
            for name in files:
                if not name.lower().endswith(".xml"):
                    continue
                path = os.path.join(root_dir, name)
                try:
                    xml = LET.parse(path).getroot()
                except Exception:
                    continue
                found = xml.xpath(query)
                if found:
                    parent_name = found[0].get("ParentName")
                    if parent_name:
                        return parent_name
    return default


def build_parent_thingdef(parent_name: str, common: Dict, tools_row, game_root_dir: str) -> Optional[LET._Element]:
    if not parent_name:
        return None
    parent_parent = find_abstract_parent_name(game_root_dir, "ThingDef", parent_name, "AnimalThingBase")
    allow_inherit_false = should_set_inherit_false(parent_parent, "AnimalThingBase")
    thing = LET.Element("ThingDef", Abstract="True", Name=parent_name, ParentName=parent_parent)

    stat_bases = LET.SubElement(thing, "statBases")
    any_stat = False
    for stat in ["MarketValue", "MoveSpeed", "Wildness", "FilthRate", "ComfyTemperatureMin", "ComfyTemperatureMax", "ArmorRating_Blunt", "ArmorRating_Sharp"]:
        v = common.get(stat)
        if not is_no_like(v):
            LET.SubElement(stat_bases, stat).text = str(v).strip()
            any_stat = True
    tox = common.get("ToxicEnvironmentResistance")
    if not is_no_like(tox) and str(tox).strip().lower() != "standard":
        LET.SubElement(stat_bases, "ToxicEnvironmentResistance").text = str(tox).strip()
        any_stat = True
    if not any_stat:
        thing.remove(stat_bases)

    race = LET.SubElement(thing, "race")
    race_map = [
        ("baseBodySize", "baseBodySize"),
        ("baseHealthScale", "baseHealthScale"),
        ("baseHungerRate", "baseHungerRate"),
        ("lifeExpectancy", "lifeExpectancy"),
        ("gestationPeriodDays", "gestationPeriodDays"),
        ("herdAnimal", "herdAnimal"),
        ("herdMigrationAllowed", "herdMigrationAllowed"),
        ("foodType", "Foodtype"),
        ("roamMtbDays", "roamMtbDays"),
        ("manhunterOnTameFailChance", "manhunterOnTameFailChance"),
        ("manhunterOnDamageChance", "manhunterOnDamageChance"),
        ("petness", "petness"),
        ("nuzzleMtbHours", "nuzzleMtbHours"),
        ("mateMtbHours", "mateMtbHours"),
        ("trainability", "trainability"),
        ("packAnimal", "PackAnimal"),
        ("predator", "predator"),
        ("maxPreyBodySize", "maxPreyBodySize"),
        ("nameOnTameChance", "nameOnTameChance"),
        ("body", "Body"),
        ("waterCellCost", "waterCellCost"),
        ("waterSeeker", "waterSeeker"),
        ("canFishForFood", "canFishForFood"),
    ]
    bool_tags = {"herdAnimal", "herdMigrationAllowed", "packAnimal", "predator", "waterSeeker", "canFishForFood"}
    prob_tags = {"manhunterOnTameFailChance", "manhunterOnDamageChance"}
    any_race = False
    for xml_tag, c in race_map:
        v = common.get(c)
        if xml_tag == "trainability":
            trainability = normalize_trainability_value(v)
            if trainability is None:
                continue
            LET.SubElement(race, xml_tag).text = trainability
            any_race = True
            continue
        if is_no_like(v):
            continue
        s = str(v).strip()
        if xml_tag == "gestationPeriodDays" and re.search(r"\blay\s*egg(s)?\b", s, flags=re.I):
            continue
        if xml_tag in bool_tags:
            s = "true" if is_truthy(s) else "false"
        elif xml_tag in prob_tags:
            s = format_prob_value(s)
        LET.SubElement(race, xml_tag).text = s
        any_race = True
    leather = common.get("LeatherDef")
    if leather and str(leather).strip().lower() not in ("no", "none"):
        LET.SubElement(race, "leatherDef").text = str(leather).strip()
        any_race = True
    special = split_and_strip(common.get("specialTrainables"))
    if special:
        st = LET.SubElement(race, "specialTrainables")
        if allow_inherit_false:
            st.set("Inherit", "False")
        for it in special:
            append_special_trainable(st, it)
        any_race = True
    if not any_race:
        thing.remove(race)

    trade = split_and_strip(common.get("TradeTags"))
    if trade:
        tt = LET.SubElement(thing, "tradeTags")
        if allow_inherit_false:
            tt.set("Inherit", "False")
        for t in trade:
            LET.SubElement(tt, "li").text = t

    if tools_row is not None:
        tools = build_tools_vanilla(tools_row)
        if tools is not None:
            if allow_inherit_false:
                tools.set("Inherit", "False")
            thing.append(tools)

    mod_extensions = build_mod_extensions_for_parent(common)
    if mod_extensions is not None:
        thing.append(mod_extensions)
    return thing


def build_parent_pawnkind(parent_name: str, common: Dict, game_root_dir: str) -> Optional[LET._Element]:
    if not parent_name:
        return None
    parent_parent = find_abstract_parent_name(game_root_dir, "PawnKindDef", parent_name, "AnimalKindBase")
    pawn = LET.Element("PawnKindDef", Abstract="True", Name=parent_name, ParentName=parent_parent)
    any_data = False
    for xml_tag, c in [("combatPower", "Combat power"), ("ecoSystemWeight", "ecoSystemWeight"), ("wildGroupSize", "Wild group size")]:
        v = common.get(c)
        if not is_no_like(v):
            LET.SubElement(pawn, xml_tag).text = str(v).strip()
            any_data = True
    cam = common.get("CanArriveManhunter")
    if not is_no_like(cam):
        LET.SubElement(pawn, "canArriveManhunter").text = "true" if is_truthy(cam) else "false"
        any_data = True
    mv = common.get("moveSpeedFactorByTerrainTag (water)")
    if not is_no_like(mv):
        mftt = LET.SubElement(pawn, "moveSpeedFactorByTerrainTag")
        li = LET.SubElement(mftt, "li")
        LET.SubElement(li, "key").text = "Water"
        LET.SubElement(li, "value").text = str(mv).strip()
        any_data = True
    if not any_data:
        return None
    return pawn


def build_thingdef_from_row(
    row,
    def_name: str,
    manual: Dict,
    parent_common: Optional[Dict] = None,
    include_all: bool = True,
    force_inherit_false_lists: bool = True,
) -> LET._Element:
    parent_abstract = get_parent_abstract_from_row(row)
    allow_inherit_false_base = force_inherit_false_lists and should_set_inherit_false(parent_abstract, "AnimalThingBase")

    thing = LET.Element("ThingDef")
    thing.set("ParentName", parent_abstract)

    LET.SubElement(thing, "defName").text = def_name
    label = str(row.get("Common name", "")).strip().lower()
    if not label:
        label = def_name.lower()
    LET.SubElement(thing, "label").text = label

    stat_order = [
        "MarketValue",
        "MoveSpeed",
        "Wildness",
        "FilthRate",
        "ComfyTemperatureMin",
        "ComfyTemperatureMax",
        "ArmorRating_Blunt",
        "ArmorRating_Sharp",
    ]
    stat_bases = LET.SubElement(thing, "statBases")
    p_common = (parent_common or {}).get("thing_common", {})
    parent_tools_owned = (not include_all) and ((parent_common or {}).get("thing_tools_signature") is not None)
    parent_special_owned = (not include_all) and ("specialTrainables" in p_common)
    parent_trade_owned = (not include_all) and ("TradeTags" in p_common)
    for stat in stat_order:
        val = get_row_value(row, stat)
        if not include_all and stat in p_common and norm(val) == norm(p_common.get(stat)):
            continue
        if not is_no_like(val):
            LET.SubElement(stat_bases, stat).text = str(val).strip()

    tox = get_row_value(row, "ToxicEnvironmentResistance")
    if not include_all and "ToxicEnvironmentResistance" in p_common and norm(tox) == norm(p_common.get("ToxicEnvironmentResistance")):
        tox = None
    if not is_no_like(tox) and str(tox).strip().lower() != "standard":
        LET.SubElement(stat_bases, "ToxicEnvironmentResistance").text = str(tox).strip()

    leather_def_raw = get_row_value(row, "LeatherDef")
    if str(leather_def_raw).strip().lower() == "no":
        LET.SubElement(stat_bases, "LeatherAmount").text = "0"

    race = LET.SubElement(thing, "race")
    race_fields = [
        ("baseBodySize", "baseBodySize"),
        ("baseHealthScale", "baseHealthScale"),
        ("baseHungerRate", "baseHungerRate"),
        ("lifeExpectancy", "lifeExpectancy"),
        ("gestationPeriodDays", "gestationPeriodDays"),
        ("herdAnimal", "herdAnimal"),
        ("herdMigrationAllowed", "herdMigrationAllowed"),
        ("foodType", "Foodtype"),
        ("roamMtbDays", "roamMtbDays"),
        ("manhunterOnTameFailChance", "manhunterOnTameFailChance"),
        ("manhunterOnDamageChance", "manhunterOnDamageChance"),
        ("petness", "petness"),
        ("nuzzleMtbHours", "nuzzleMtbHours"),
        ("mateMtbHours", "mateMtbHours"),
        ("trainability", "trainability"),
        ("packAnimal", "PackAnimal"),
        ("predator", "predator"),
        ("maxPreyBodySize", "maxPreyBodySize"),
        ("nameOnTameChance", "nameOnTameChance"),
        ("body", "Body"),
        ("waterCellCost", "waterCellCost"),
        ("waterSeeker", "waterSeeker"),
        ("canFishForFood", "canFishForFood"),
    ]

    bool_tags = {"herdAnimal", "herdMigrationAllowed", "packAnimal", "predator", "waterSeeker", "canFishForFood"}
    prob_tags = {"manhunterOnTameFailChance", "manhunterOnDamageChance"}
    for xml_tag, col in race_fields:
        val = get_row_value(row, col)
        if not include_all and col in p_common and norm(val) == norm(p_common.get(col)):
            continue
        if xml_tag == "trainability":
            trainability = normalize_trainability_value(val)
            if trainability is None:
                continue
            LET.SubElement(race, xml_tag).text = trainability
            continue
        if is_no_like(val):
            continue
        s = str(val).strip()
        if xml_tag == "gestationPeriodDays":
            if re.search(r"\blay\s*egg(s)?\b", s, flags=re.I):
                continue
            num = try_parse_number(s)
            if num is None:
                continue
            LET.SubElement(race, xml_tag).text = s
            continue
        if xml_tag == "foodType":
            LET.SubElement(race, xml_tag).text = s
            continue
        if xml_tag == "body":
            LET.SubElement(race, xml_tag).text = s
            continue
        if xml_tag in bool_tags:
            LET.SubElement(race, xml_tag).text = "true" if is_truthy(s) else "false"
            continue
        if xml_tag in prob_tags:
            LET.SubElement(race, xml_tag).text = format_prob_value(s)
            continue
        LET.SubElement(race, xml_tag).text = s

    for key in RACE_SOUND_OPTION_TAGS:
        sound_val = manual["sounds"].get(key)
        if not is_no_like(sound_val):
            LET.SubElement(race, key).text = str(sound_val).strip()

    leather_s = str(leather_def_raw).strip()
    if leather_s and leather_s.lower() not in ("no", "none") and not (
        (not include_all) and ("LeatherDef" in p_common) and (norm(leather_s) == norm(p_common.get("LeatherDef")))
    ):
        LET.SubElement(race, "leatherDef").text = leather_s

    juv = get_row_value(row, "Juv age (years)")
    adult = get_row_value(row, "Adult age (years)") or get_row_value(row, "Adult age")
    juv_val = None if is_no_like(juv) else str(juv).strip()
    adult_val = None if is_no_like(adult) else str(adult).strip()
    if juv_val is None:
        juv_val = "0.2"
    if adult_val is None:
        adult_val = "0.5"

    lsa = LET.SubElement(race, "lifeStageAges")
    baby = LET.SubElement(lsa, "li")
    LET.SubElement(baby, "def").text = manual["stage_defs"]["baby"]
    LET.SubElement(baby, "minAge").text = "0"
    juv_li = LET.SubElement(lsa, "li")
    LET.SubElement(juv_li, "def").text = manual["stage_defs"]["juvenile"]
    LET.SubElement(juv_li, "minAge").text = juv_val
    adult_li = LET.SubElement(lsa, "li")
    LET.SubElement(adult_li, "def").text = manual["stage_defs"]["adult"]
    LET.SubElement(adult_li, "minAge").text = adult_val
    for key in ("soundWounded", "soundDeath", "soundCall", "soundAngry"):
        LET.SubElement(adult_li, key).text = manual["sounds"][key]

    special = get_row_value(row, "specialTrainables")
    if not include_all and "specialTrainables" in p_common and norm(special) == norm(p_common.get("specialTrainables")):
        special = None
    special_items = split_and_strip(special)
    if special_items:
        st = LET.SubElement(race, "specialTrainables")
        if allow_inherit_false_base and parent_special_owned:
            st.set("Inherit", "False")
        for it in special_items:
            append_special_trainable(st, it)

    litter_raw = get_row_value(row, "Litter size")
    if not is_no_like(litter_raw):
        lit_text = str(litter_raw).strip()
        mean = try_parse_number(lit_text)
        if mean is None and "~" in lit_text:
            p = lit_text.split("~", 1)
            mi = try_parse_number(p[0])
            ma = try_parse_number(p[1])
            if mi is not None and ma is not None:
                mean = (mi + ma) / 2.0
        if mean is not None and mean > 1:
            add_litter_curve(race, mean)

    trade_tags = split_and_strip(get_row_value(row, "TradeTags"))
    if not include_all and "TradeTags" in p_common and norm(get_row_value(row, "TradeTags")) == norm(p_common.get("TradeTags")):
        trade_tags = []
    if trade_tags:
        tt = LET.SubElement(thing, "tradeTags")
        if allow_inherit_false_base and parent_trade_owned:
            tt.set("Inherit", "False")
        for t in trade_tags:
            LET.SubElement(tt, "li").text = t

    tools = None
    skip_tools = False
    if not include_all:
        p_sig = (parent_common or {}).get("thing_tools_signature")
        if p_sig is not None and _tool_signature(row) == p_sig:
            skip_tools = True
    if not skip_tools:
        tools = build_tools_vanilla(row)
    if tools is not None:
        if allow_inherit_false_base and parent_tools_owned:
            tools.set("Inherit", "False")
        thing.append(tools)

    mod_extensions = build_mod_extensions_for_child(row, p_common, include_all)
    if mod_extensions is not None:
        thing.append(mod_extensions)
    return thing


def build_pawnkind_from_row(
    row,
    def_name: str,
    manual: Dict,
    parent_common: Optional[Dict] = None,
    include_all: bool = True,
    force_inherit_false_lists: bool = True,
) -> LET._Element:
    pawn_parent = (
        str(row.get("Parrent Pawn kind abstract", "")).strip()
        or str(row.get("Parent Pawn kind abstract", "")).strip()
        or str(row.get("Parrent Pawn kind", "")).strip()
        or "AnimalKindBase"
    )
    if pawn_parent.lower() in ("", "none"):
        pawn_parent = "AnimalKindBase"
    pawn = LET.Element("PawnKindDef")
    pawn.set("ParentName", pawn_parent)
    LET.SubElement(pawn, "defName").text = def_name

    label = str(row.get("Common name", "")).strip().lower()
    if not label:
        label = def_name.lower()
    LET.SubElement(pawn, "label").text = label
    LET.SubElement(pawn, "race").text = def_name

    p_common = (parent_common or {}).get("pawn_common", {})
    combat_val = get_row_value(row, "Combat power")
    eco_val = get_row_value(row, "ecoSystemWeight")
    if include_all or norm(combat_val) != norm(p_common.get("Combat power")):
        add_text_if(pawn, "combatPower", combat_val)
    if include_all or norm(eco_val) != norm(p_common.get("ecoSystemWeight")):
        add_text_if(pawn, "ecoSystemWeight", eco_val)

    cam = get_row_value(row, "CanArriveManhunter")
    if not include_all and norm(cam) == norm(p_common.get("CanArriveManhunter")):
        cam = None
    if not is_no_like(cam):
        LET.SubElement(pawn, "canArriveManhunter").text = "true" if is_truthy(cam) else "false"

    wild_group = get_row_value(row, "Wild group size")
    if not include_all and norm(wild_group) == norm(p_common.get("Wild group size")):
        wild_group = None
    if not is_no_like(wild_group):
        LET.SubElement(pawn, "wildGroupSize").text = str(wild_group).strip()

    move_water = get_row_value(row, "moveSpeedFactorByTerrainTag (water)") or get_row_value(row, "moveSpeedFactorByTerrainTag")
    if not include_all and norm(move_water) == norm(p_common.get("moveSpeedFactorByTerrainTag (water)")):
        move_water = None
    if not is_no_like(move_water):
        mftt = LET.SubElement(pawn, "moveSpeedFactorByTerrainTag")
        li = LET.SubElement(mftt, "li")
        LET.SubElement(li, "key").text = "Water"
        LET.SubElement(li, "value").text = str(move_water).strip()

    life_stages = LET.SubElement(pawn, "lifeStages")
    for stage in manual["stages"]:
        li = LET.SubElement(life_stages, "li")
        if stage["label"]:
            LET.SubElement(li, "label").text = stage["label"]

        body = LET.SubElement(li, "bodyGraphicData")
        LET.SubElement(body, "texPath").text = stage["body_texPath"]
        LET.SubElement(body, "drawSize").text = stage["body_drawSize"]

        dess = LET.SubElement(li, "dessicatedBodyGraphicData")
        LET.SubElement(dess, "texPath").text = stage["dess_texPath"]
        LET.SubElement(dess, "drawSize").text = stage["dess_drawSize"]

        if stage["has_swim"]:
            swim = LET.SubElement(li, "swimmingGraphicData")
            LET.SubElement(swim, "texPath").text = stage["swim_texPath"]
            LET.SubElement(swim, "drawSize").text = stage["swim_drawSize"]

    return pawn


def _update_lifestage_min_age(race: LET._Element, stage_def: str, min_age_text: str, create_if_missing: bool = True):
    if is_no_like(min_age_text):
        return
    lsa = _find_direct_child(race, "lifeStageAges")
    if lsa is None:
        if not create_if_missing:
            return
        lsa = _ensure_direct_child(race, "lifeStageAges")
    target = None
    for li in lsa.findall("./li"):
        d = li.find("def")
        if d is not None and norm(d.text) == norm(stage_def):
            target = li
            break
    if target is None:
        if not create_if_missing:
            return
        target = LET.SubElement(lsa, "li")
        LET.SubElement(target, "def").text = stage_def
    _set_direct_text(target, "minAge", str(min_age_text).strip())


def _resolve_age_stage_defs_for_update(race: LET._Element, parent_abstract: str) -> Tuple[str, str, bool]:
    parent_norm = (parent_abstract or "").strip().lower()
    if parent_norm == "baseinsect":
        default_juv = "EusocialInsectJuvenile"
        default_adult = "EusocialInsectAdult"
    else:
        default_juv = "AnimalJuvenile"
        default_adult = "AnimalAdult"

    lsa = _find_direct_child(race, "lifeStageAges")
    if lsa is None:
        return default_juv, default_adult, False

    defs = []
    for li in lsa.findall("./li"):
        d = li.find("def")
        if d is not None and (d.text or "").strip():
            defs.append((d.text or "").strip())
    if not defs:
        return default_juv, default_adult, False

    baby_defaults = {"animalbaby", "eusocialinsectlarva"}
    first_is_default_baby = norm(defs[0]) in baby_defaults

    # Custom staged races (e.g. Predimago/Imago): use the existing stage order
    # instead of forcing AnimalJuvenile/AnimalAdult.
    if len(defs) >= 3 and not first_is_default_baby:
        return defs[1], defs[2], True

    def_norms = {norm(x) for x in defs}
    if norm(default_juv) in def_norms and norm(default_adult) in def_norms:
        return default_juv, default_adult, False

    if len(defs) >= 3:
        return defs[1], defs[2], True
    if len(defs) == 2:
        return defs[0], defs[1], True
    return default_juv, default_adult, False


def _remove_vanilla_age_stages_except(race: LET._Element, keep_defs: Set[str]):
    lsa = _find_direct_child(race, "lifeStageAges")
    if lsa is None:
        return
    keep_norm = {norm(x) for x in keep_defs}
    vanilla_age_defs = {
        "animaljuvenile",
        "animaladult",
        "eusocialinsectjuvenile",
        "eusocialinsectadult",
    }
    for li in list(lsa.findall("./li")):
        d = li.find("def")
        d_norm = norm(d.text if d is not None else "")
        if d_norm in vanilla_age_defs and d_norm not in keep_norm:
            lsa.remove(li)


def _replace_tools_node(thing: LET._Element, tools_el: LET._Element, allow_inherit_false: bool):
    _remove_direct_children(thing, "tools")
    new_tools = copy.deepcopy(tools_el)
    if allow_inherit_false:
        new_tools.set("Inherit", "False")
    else:
        new_tools.attrib.pop("Inherit", None)
    thing.append(new_tools)


def _merge_mod_extensions_node(thing: LET._Element, desired_modext: LET._Element):
    mod_ext = _ensure_direct_child(thing, "modExtensions")
    for wanted in desired_modext.findall("./li"):
        cls = wanted.get("Class", "")
        if not cls:
            continue
        existing = None
        for li in mod_ext.findall("./li"):
            if li.get("Class", "") == cls:
                existing = li
                break
        repl = copy.deepcopy(wanted)
        if existing is None:
            mod_ext.append(repl)
        else:
            idx = list(mod_ext).index(existing)
            mod_ext.remove(existing)
            mod_ext.insert(idx, repl)


def _set_trade_tags(thing: LET._Element, tags: List[str], allow_inherit_false: bool):
    _remove_direct_children(thing, "tradeTags")
    if not tags:
        return
    tt = LET.SubElement(thing, "tradeTags")
    if allow_inherit_false:
        tt.set("Inherit", "False")
    for t in tags:
        LET.SubElement(tt, "li").text = str(t)


_VANILLA_SPECIAL_TRAINABLES = {
    "SludgeSpew",
    "EggSpew",
    "TerrorRoar",
    "WarTrumpet",
    "Forage",
    "Comfort",
    "AttackTarget",
    "Dig",
}


def _update_special_trainables(
    race: LET._Element,
    items: List[str],
    allow_inherit_false: bool,
):
    st = _find_direct_child(race, "specialTrainables")
    if not items:
        if st is not None:
            race.remove(st)
        return
    if st is None:
        st = LET.SubElement(race, "specialTrainables")
    if allow_inherit_false:
        st.set("Inherit", "False")
    else:
        st.attrib.pop("Inherit", None)

    remove_texts = set(items) | _VANILLA_SPECIAL_TRAINABLES
    for li in list(st):
        if not isinstance(li.tag, str) or li.tag != "li":
            continue
        txt = (li.text or "").strip()
        if txt in remove_texts:
            st.remove(li)
    for it in items:
        append_special_trainable(st, it)


def update_thingdef_inplace(
    thing: LET._Element,
    row,
    parent_common: Optional[Dict] = None,
    consider_parent_rules: bool = False,
):
    p_common = (parent_common or {}).get("thing_common", {})
    parent_abstract = get_parent_abstract_from_row(row)
    allow_inherit_false_base = should_set_inherit_false(parent_abstract, "AnimalThingBase")
    parent_tools_owned = consider_parent_rules and ((parent_common or {}).get("thing_tools_signature") is not None)
    parent_special_owned = consider_parent_rules and _parent_has_key(p_common, "specialTrainables")
    parent_trade_owned = consider_parent_rules and _parent_has_key(p_common, "TradeTags")

    thing.set("ParentName", parent_abstract)
    label = str(row.get("Common name", "")).strip().lower()
    if label:
        _set_direct_text(thing, "label", label)

    stat_bases = _find_direct_child(thing, "statBases")
    stat_order = [
        "MarketValue",
        "MoveSpeed",
        "Wildness",
        "FilthRate",
        "ComfyTemperatureMin",
        "ComfyTemperatureMax",
        "ArmorRating_Blunt",
        "ArmorRating_Sharp",
    ]
    for stat in stat_order:
        if consider_parent_rules and _parent_has_key(p_common, stat):
            _remove_from_container(stat_bases, stat)
            continue
        value = get_row_value(row, stat)
        if value is None or str(value).strip() == "":
            continue
        sval = str(value).strip()
        if sval.lower() in ("no", "none"):
            _remove_from_container(stat_bases, stat)
            continue
        stat_bases = _ensure_direct_child(thing, "statBases")
        _set_direct_text(stat_bases, stat, sval)

    toxic = get_row_value(row, "ToxicEnvironmentResistance")
    if consider_parent_rules and _parent_has_key(p_common, "ToxicEnvironmentResistance"):
        _remove_from_container(stat_bases, "ToxicEnvironmentResistance")
    elif toxic is not None and str(toxic).strip() != "":
        sval = str(toxic).strip()
        if sval.lower() in ("standard", "no", "none"):
            _remove_from_container(stat_bases, "ToxicEnvironmentResistance")
        else:
            stat_bases = _ensure_direct_child(thing, "statBases")
            _set_direct_text(stat_bases, "ToxicEnvironmentResistance", sval)

    leather_def_raw = get_row_value(row, "LeatherDef")
    if str(leather_def_raw).strip().lower() == "no":
        skip_leather_amount = False
        if consider_parent_rules and _parent_has_key(p_common, "LeatherDef"):
            pval = p_common.get("LeatherDef")
            if pval is not None and str(pval).strip().lower() == "no":
                skip_leather_amount = True
        if not skip_leather_amount:
            stat_bases = _ensure_direct_child(thing, "statBases")
            _set_direct_text(stat_bases, "LeatherAmount", "0")

    race = _ensure_direct_child(thing, "race")
    race_fields = [
        ("baseBodySize", "baseBodySize"),
        ("baseHealthScale", "baseHealthScale"),
        ("baseHungerRate", "baseHungerRate"),
        ("lifeExpectancy", "lifeExpectancy"),
        ("gestationPeriodDays", "gestationPeriodDays"),
        ("herdAnimal", "herdAnimal"),
        ("herdMigrationAllowed", "herdMigrationAllowed"),
        ("foodType", "Foodtype"),
        ("roamMtbDays", "roamMtbDays"),
        ("manhunterOnTameFailChance", "manhunterOnTameFailChance"),
        ("manhunterOnDamageChance", "manhunterOnDamageChance"),
        ("petness", "petness"),
        ("nuzzleMtbHours", "nuzzleMtbHours"),
        ("mateMtbHours", "mateMtbHours"),
        ("trainability", "trainability"),
        ("packAnimal", "PackAnimal"),
        ("predator", "predator"),
        ("maxPreyBodySize", "maxPreyBodySize"),
        ("nameOnTameChance", "nameOnTameChance"),
        ("body", "Body"),
        ("waterCellCost", "waterCellCost"),
        ("waterSeeker", "waterSeeker"),
        ("canFishForFood", "canFishForFood"),
    ]
    bool_tags = {"herdAnimal", "herdMigrationAllowed", "packAnimal", "predator", "waterSeeker", "canFishForFood"}
    prob_tags = {"manhunterOnTameFailChance", "manhunterOnDamageChance"}

    for xml_tag, col in race_fields:
        if consider_parent_rules and _parent_has_key(p_common, col):
            _remove_from_container(race, xml_tag)
            continue
        value = get_row_value(row, col)
        if value is None or str(value).strip() == "":
            continue
        s = str(value).strip()
        if xml_tag == "trainability":
            trainability = normalize_trainability_value(s)
            if trainability is None:
                _remove_from_container(race, xml_tag)
            else:
                _set_direct_text(race, xml_tag, trainability)
            continue
        if s.lower() in ("no", "none"):
            _remove_from_container(race, xml_tag)
            continue
        if xml_tag == "gestationPeriodDays":
            if re.search(r"\blay\s*egg(s)?\b", s, flags=re.I) or (re.search(r"\begg(s)?\b", s, flags=re.I) and try_parse_number(s) is None):
                _remove_from_container(race, xml_tag)
                continue
            if try_parse_number(s) is None:
                _remove_from_container(race, xml_tag)
                continue
            _set_direct_text(race, xml_tag, s)
            continue
        if xml_tag in bool_tags:
            _set_direct_text(race, xml_tag, "true" if is_truthy(s) else "false")
            continue
        if xml_tag in prob_tags:
            _set_direct_text(race, xml_tag, format_prob_value(s))
            continue
        _set_direct_text(race, xml_tag, s)

    if consider_parent_rules and _parent_has_key(p_common, "LeatherDef"):
        _remove_from_container(race, "leatherDef")
    else:
        leather_s = str(leather_def_raw).strip()
        if leather_s:
            if leather_s.lower() in ("no", "none"):
                _remove_from_container(race, "leatherDef")
            else:
                _set_direct_text(race, "leatherDef", leather_s)

    juv = get_row_value(row, "Juv age (years)")
    adult = get_row_value(row, "Adult age (years)") or get_row_value(row, "Adult age")
    if not is_no_like(juv) or not is_no_like(adult):
        juv_def, adult_def, used_existing_order = _resolve_age_stage_defs_for_update(race, parent_abstract)
        if used_existing_order:
            _remove_vanilla_age_stages_except(race, {juv_def, adult_def})
        if not is_no_like(juv):
            _update_lifestage_min_age(race, juv_def, str(juv).strip(), create_if_missing=False)
        if not is_no_like(adult):
            _update_lifestage_min_age(race, adult_def, str(adult).strip(), create_if_missing=False)

    special_raw = get_row_value(row, "specialTrainables")
    if consider_parent_rules and _parent_has_key(p_common, "specialTrainables"):
        _remove_from_container(race, "specialTrainables")
    elif special_raw is not None and str(special_raw).strip() != "":
        s = str(special_raw).strip()
        if s.lower() in ("no", "none"):
            _update_special_trainables(race, [], allow_inherit_false_base and parent_special_owned)
        else:
            _update_special_trainables(race, split_and_strip(s), allow_inherit_false_base and parent_special_owned)

    litter = get_row_value(row, "Litter size")
    if consider_parent_rules and _parent_has_key(p_common, "Litter size"):
        _remove_from_container(race, "litterSizeCurve")
    elif litter is not None and str(litter).strip() != "":
        mean = _parse_litter_mean(litter)
        _remove_from_container(race, "litterSizeCurve")
        if mean is not None and mean > 1:
            add_litter_curve(race, mean)

    trade_raw = get_row_value(row, "TradeTags")
    if consider_parent_rules and _parent_has_key(p_common, "TradeTags"):
        _remove_from_container(thing, "tradeTags")
    elif trade_raw is not None and str(trade_raw).strip() != "":
        tags = split_and_strip(trade_raw)
        _set_trade_tags(thing, tags, allow_inherit_false_base and parent_trade_owned)

    if consider_parent_rules:
        p_sig = (parent_common or {}).get("thing_tools_signature")
        if p_sig is not None and _tool_signature(row) == p_sig:
            _remove_from_container(thing, "tools")
        else:
            built_tools = build_tools_vanilla(row)
            if built_tools is not None:
                _replace_tools_node(thing, built_tools, allow_inherit_false_base and parent_tools_owned)
    else:
        built_tools = build_tools_vanilla(row)
        if built_tools is not None:
            _replace_tools_node(thing, built_tools, False)

    desired_modext = build_mod_extensions_for_child(row, p_common, include_all=not consider_parent_rules)
    if desired_modext is not None:
        _merge_mod_extensions_node(thing, desired_modext)


def update_pawnkind_inplace(
    pawn: LET._Element,
    row,
    parent_common: Optional[Dict] = None,
    consider_parent_rules: bool = False,
):
    p_common = (parent_common or {}).get("pawn_common", {})
    pawn_parent = (
        str(row.get("Parrent Pawn kind abstract", "")).strip()
        or str(row.get("Parent Pawn kind abstract", "")).strip()
        or str(row.get("Parrent Pawn kind", "")).strip()
        or "AnimalKindBase"
    )
    if pawn_parent.lower() in ("", "none"):
        pawn_parent = "AnimalKindBase"
    pawn.set("ParentName", pawn_parent)

    label = str(row.get("Common name", "")).strip().lower()
    if label:
        _set_direct_text(pawn, "label", label)

    def _remove_direct(tag):
        _remove_from_container(pawn, tag)

    # combatPower
    combat = get_row_value(row, "Combat power")
    if consider_parent_rules and _parent_has_key(p_common, "Combat power"):
        _remove_direct("combatPower")
    elif combat is not None and str(combat).strip() != "":
        s = str(combat).strip()
        if s.lower() in ("no", "none"):
            _remove_direct("combatPower")
        else:
            _set_direct_text(pawn, "combatPower", s)

    eco = get_row_value(row, "ecoSystemWeight")
    if consider_parent_rules and _parent_has_key(p_common, "ecoSystemWeight"):
        _remove_direct("ecoSystemWeight")
    elif eco is not None and str(eco).strip() != "":
        s = str(eco).strip()
        if s.lower() in ("no", "none"):
            _remove_direct("ecoSystemWeight")
        else:
            _set_direct_text(pawn, "ecoSystemWeight", s)

    cam = get_row_value(row, "CanArriveManhunter")
    if consider_parent_rules and _parent_has_key(p_common, "CanArriveManhunter"):
        _remove_direct("canArriveManhunter")
    elif cam is not None and str(cam).strip() != "":
        s = str(cam).strip()
        if s.lower() in ("no", "none"):
            _remove_direct("canArriveManhunter")
        else:
            _set_direct_text(pawn, "canArriveManhunter", "true" if is_truthy(s) else "false")

    group = get_row_value(row, "Wild group size")
    if consider_parent_rules and _parent_has_key(p_common, "Wild group size"):
        _remove_direct("wildGroupSize")
    elif group is not None and str(group).strip() != "":
        s = str(group).strip()
        if s.lower() in ("no", "none"):
            _remove_direct("wildGroupSize")
        else:
            _set_direct_text(pawn, "wildGroupSize", s)

    move_water = get_row_value(row, "moveSpeedFactorByTerrainTag (water)") or get_row_value(row, "moveSpeedFactorByTerrainTag")
    if consider_parent_rules and (
        _parent_has_key(p_common, "moveSpeedFactorByTerrainTag (water)")
        or _parent_has_key(p_common, "moveSpeedFactorByTerrainTag")
    ):
        _remove_direct("moveSpeedFactorByTerrainTag")
    elif move_water is not None and str(move_water).strip() != "":
        s = str(move_water).strip()
        if s.lower() in ("no", "none"):
            _remove_direct("moveSpeedFactorByTerrainTag")
        else:
            mftt = _ensure_direct_child(pawn, "moveSpeedFactorByTerrainTag")
            target_li = None
            for li in mftt.findall("./li"):
                key = li.find("key")
                if key is not None and norm(key.text) == "water":
                    target_li = li
                    break
            if target_li is None:
                target_li = LET.SubElement(mftt, "li")
                LET.SubElement(target_li, "key").text = "Water"
            _set_direct_text(target_li, "value", s)


def _find_thingdef_by_name_attr(root: LET._Element, name_attr: str) -> Optional[LET._Element]:
    found = root.xpath(f"//ThingDef[@Name='{name_attr}']")
    return found[0] if found else None


def _upsert_ce_durability_on_thing(thing: LET._Element, durability: str):
    comps = _ensure_direct_child(thing, "comps")
    comp = None
    for li in comps.findall("./li"):
        if li.get("Class", "") == "CombatExtended.CompProperties_ArmorDurability":
            comp = li
            break
    if comp is None:
        comp = LET.SubElement(comps, "li")
        comp.set("Class", "CombatExtended.CompProperties_ArmorDurability")
    _set_direct_text(comp, "Durability", durability)
    _set_direct_text(comp, "Regenerates", "true")
    _set_direct_text(comp, "RegenInterval", "600")
    _set_direct_text(comp, "RegenValue", "5")
    _set_direct_text(comp, "MinArmorPct", "0.5")


def _remove_ce_durability_on_thing(thing: LET._Element):
    comps = _find_direct_child(thing, "comps")
    if comps is None:
        return
    for li in list(comps):
        if isinstance(li.tag, str) and li.tag == "li" and li.get("Class", "") == "CombatExtended.CompProperties_ArmorDurability":
            comps.remove(li)


def _upsert_ce_bodyshape_on_thing(thing: LET._Element, body_shape: str):
    modext = _ensure_direct_child(thing, "modExtensions")
    ext = None
    for li in modext.findall("./li"):
        if li.get("Class", "") == "CombatExtended.RacePropertiesExtensionCE":
            ext = li
            break
    if ext is None:
        ext = LET.SubElement(modext, "li")
        ext.set("Class", "CombatExtended.RacePropertiesExtensionCE")
    _set_direct_text(ext, "bodyShape", body_shape)


def _remove_ce_bodyshape_on_thing(thing: LET._Element):
    modext = _find_direct_child(thing, "modExtensions")
    if modext is None:
        return
    for li in modext.findall("./li"):
        if li.get("Class", "") == "CombatExtended.RacePropertiesExtensionCE":
            _remove_from_container(li, "bodyShape")


def update_ce_on_thing_inplace(
    root: LET._Element,
    thing: LET._Element,
    row,
    ce_row,
    ce_parent_common: Optional[Dict[str, str]] = None,
    consider_parent_rules: bool = False,
):
    if ce_row is None:
        return

    stat_bases = _find_direct_child(thing, "statBases")
    for stat in ("MeleeDodgeChance", "MeleeCritChance", "MeleeParryChance", "ArmorRating_Sharp", "ArmorRating_Blunt"):
        val = ce_row.get(stat)
        if val is None or str(val).strip() == "":
            continue
        stat_bases = _ensure_direct_child(thing, "statBases")
        _set_direct_text(stat_bases, stat, str(val).strip())

    parent_abstract = get_parent_abstract_from_row(row)
    parent_thing = _find_thingdef_by_name_attr(root, parent_abstract) if consider_parent_rules else None
    ce_parent_common = ce_parent_common or {}

    child_dur = ce_row.get("ArmorDurability")
    parent_dur = ce_parent_common.get("ArmorDurability") if consider_parent_rules else None
    if parent_thing is not None and not is_no_like(parent_dur):
        _upsert_ce_durability_on_thing(parent_thing, str(parent_dur).strip())
        if not is_no_like(child_dur) and norm(child_dur) != norm(parent_dur):
            _upsert_ce_durability_on_thing(thing, str(child_dur).strip())
        else:
            _remove_ce_durability_on_thing(thing)
    elif not is_no_like(child_dur):
        _upsert_ce_durability_on_thing(thing, str(child_dur).strip())

    child_body = ce_row.get("Body shape")
    parent_body = ce_parent_common.get("Body shape") if consider_parent_rules else None
    if parent_thing is not None and not is_no_like(parent_body):
        _upsert_ce_bodyshape_on_thing(parent_thing, str(parent_body).strip())
        if not is_no_like(child_body) and norm(child_body) != norm(parent_body):
            _upsert_ce_bodyshape_on_thing(thing, str(child_body).strip())
        else:
            _remove_ce_bodyshape_on_thing(thing)
    elif not is_no_like(child_body):
        _upsert_ce_bodyshape_on_thing(thing, str(child_body).strip())


def strip_ce_from_base_thingdef(thing: LET._Element):
    # Base (non-CE) Def XML must not contain CE-only stat fields.
    stat_bases = _find_direct_child(thing, "statBases")
    if stat_bases is not None:
        for tag in ("MeleeDodgeChance", "MeleeCritChance", "MeleeParryChance"):
            _remove_from_container(stat_bases, tag)

    # Remove CE-only durability comp from base XML.
    comps = _find_direct_child(thing, "comps")
    if comps is not None:
        for li in list(comps):
            if isinstance(li.tag, str) and li.tag == "li" and li.get("Class", "") == "CombatExtended.CompProperties_ArmorDurability":
                comps.remove(li)

    # Remove CE body-shape extension from base XML.
    modext = _find_direct_child(thing, "modExtensions")
    if modext is not None:
        for li in list(modext):
            if isinstance(li.tag, str) and li.tag == "li" and li.get("Class", "") == "CombatExtended.RacePropertiesExtensionCE":
                modext.remove(li)


def update_parent_thingdef_inplace(thing: LET._Element, common: Dict, tools_row=None, differing: Optional[List[str]] = None):
    parent_name = thing.get("ParentName", "AnimalThingBase")
    allow_inherit_false = should_set_inherit_false(parent_name, "AnimalThingBase")
    differing_set = {str(x).strip() for x in (differing or [])}

    stat_order = [
        "MarketValue",
        "MoveSpeed",
        "Wildness",
        "FilthRate",
        "ComfyTemperatureMin",
        "ComfyTemperatureMax",
        "ArmorRating_Blunt",
        "ArmorRating_Sharp",
    ]
    for stat in stat_order:
        if stat not in common and stat not in differing_set:
            continue
        if stat in differing_set and stat not in common:
            _remove_from_container(_find_direct_child(thing, "statBases"), stat)
            continue
        v = common.get(stat)
        if is_no_like(v):
            _remove_from_container(_find_direct_child(thing, "statBases"), stat)
            continue
        sb = _ensure_direct_child(thing, "statBases")
        _set_direct_text(sb, stat, str(v).strip())

    if "ToxicEnvironmentResistance" in common:
        tox = common.get("ToxicEnvironmentResistance")
        if is_no_like(tox) or str(tox).strip().lower() == "standard":
            _remove_from_container(_find_direct_child(thing, "statBases"), "ToxicEnvironmentResistance")
        else:
            sb = _ensure_direct_child(thing, "statBases")
            _set_direct_text(sb, "ToxicEnvironmentResistance", str(tox).strip())
    elif "ToxicEnvironmentResistance" in differing_set:
        _remove_from_container(_find_direct_child(thing, "statBases"), "ToxicEnvironmentResistance")

    race = _ensure_direct_child(thing, "race")
    race_map = [
        ("baseBodySize", "baseBodySize"),
        ("baseHealthScale", "baseHealthScale"),
        ("baseHungerRate", "baseHungerRate"),
        ("lifeExpectancy", "lifeExpectancy"),
        ("gestationPeriodDays", "gestationPeriodDays"),
        ("herdAnimal", "herdAnimal"),
        ("herdMigrationAllowed", "herdMigrationAllowed"),
        ("foodType", "Foodtype"),
        ("roamMtbDays", "roamMtbDays"),
        ("manhunterOnTameFailChance", "manhunterOnTameFailChance"),
        ("manhunterOnDamageChance", "manhunterOnDamageChance"),
        ("petness", "petness"),
        ("nuzzleMtbHours", "nuzzleMtbHours"),
        ("mateMtbHours", "mateMtbHours"),
        ("trainability", "trainability"),
        ("packAnimal", "PackAnimal"),
        ("predator", "predator"),
        ("maxPreyBodySize", "maxPreyBodySize"),
        ("nameOnTameChance", "nameOnTameChance"),
        ("body", "Body"),
        ("waterCellCost", "waterCellCost"),
        ("waterSeeker", "waterSeeker"),
        ("canFishForFood", "canFishForFood"),
    ]
    bool_tags = {"herdAnimal", "herdMigrationAllowed", "packAnimal", "predator", "waterSeeker", "canFishForFood"}
    prob_tags = {"manhunterOnTameFailChance", "manhunterOnDamageChance"}

    for xml_tag, c in race_map:
        if c not in common and c not in differing_set:
            continue
        if c in differing_set and c not in common:
            _remove_from_container(race, xml_tag)
            continue
        v = common.get(c)
        if xml_tag == "trainability":
            trainability = normalize_trainability_value(v)
            if trainability is None:
                _remove_from_container(race, xml_tag)
            else:
                _set_direct_text(race, xml_tag, trainability)
            continue
        if is_no_like(v):
            _remove_from_container(race, xml_tag)
            continue
        s = str(v).strip()
        if xml_tag == "gestationPeriodDays":
            if re.search(r"\blay\s*egg(s)?\b", s, flags=re.I):
                _remove_from_container(race, xml_tag)
                continue
            if try_parse_number(s) is None:
                _remove_from_container(race, xml_tag)
                continue
        if xml_tag in bool_tags:
            s = "true" if is_truthy(s) else "false"
        elif xml_tag in prob_tags:
            s = format_prob_value(s)
        _set_direct_text(race, xml_tag, s)

    if "LeatherDef" in common:
        leather = common.get("LeatherDef")
        if leather and str(leather).strip().lower() not in ("no", "none"):
            _set_direct_text(race, "leatherDef", str(leather).strip())
        else:
            _remove_from_container(race, "leatherDef")
    elif "LeatherDef" in differing_set:
        _remove_from_container(race, "leatherDef")

    if "specialTrainables" in common:
        special = split_and_strip(common.get("specialTrainables"))
        _update_special_trainables(race, special, allow_inherit_false)
    elif "specialTrainables" in differing_set:
        _update_special_trainables(race, [], allow_inherit_false)

    if "TradeTags" in common:
        trade = split_and_strip(common.get("TradeTags"))
        _set_trade_tags(thing, trade, allow_inherit_false)
    elif "TradeTags" in differing_set:
        _set_trade_tags(thing, [], allow_inherit_false)

    if tools_row is not None:
        tools = build_tools_vanilla(tools_row)
        if tools is not None:
            _replace_tools_node(thing, tools, allow_inherit_false)

    desired_modext = build_mod_extensions_for_parent(common)
    if desired_modext is not None:
        _merge_mod_extensions_node(thing, desired_modext)


def update_parent_pawnkind_inplace(pawn: LET._Element, common: Dict):
    for xml_tag, c in [("combatPower", "Combat power"), ("ecoSystemWeight", "ecoSystemWeight"), ("wildGroupSize", "Wild group size")]:
        if c not in common:
            continue
        v = common.get(c)
        if is_no_like(v):
            continue
        _set_direct_text(pawn, xml_tag, str(v).strip())
    if "CanArriveManhunter" in common:
        cam = common.get("CanArriveManhunter")
        if not is_no_like(cam):
            _set_direct_text(pawn, "canArriveManhunter", "true" if is_truthy(cam) else "false")
    if "moveSpeedFactorByTerrainTag (water)" in common:
        mv = common.get("moveSpeedFactorByTerrainTag (water)")
        if not is_no_like(mv):
            mftt = _ensure_direct_child(pawn, "moveSpeedFactorByTerrainTag")
            target_li = None
            for li in mftt.findall("./li"):
                k = li.find("key")
                if k is not None and norm(k.text) == "water":
                    target_li = li
                    break
            if target_li is None:
                target_li = LET.SubElement(mftt, "li")
                LET.SubElement(target_li, "key").text = "Water"
            _set_direct_text(target_li, "value", str(mv).strip())


def _collect_parent_common_lookups(animals_df: pd.DataFrame) -> Tuple[Dict[str, Dict], Dict[str, Dict]]:
    thing_lookup: Dict[str, Dict] = {}
    pawn_lookup: Dict[str, Dict] = {}
    for _, row in animals_df.iterrows():
        info = compute_parent_common(animals_df, row)
        thing_parent = info.get("thing_parent_name", "")
        if thing_parent and thing_parent != "None" and thing_parent not in thing_lookup:
            thing_lookup[thing_parent] = info
        pawn_parent = info.get("pawn_parent_name", "")
        if pawn_parent and pawn_parent != "None" and pawn_parent not in pawn_lookup:
            pawn_lookup[pawn_parent] = info
    return thing_lookup, pawn_lookup


def _collect_ce_parent_common_lookup(animals_df: pd.DataFrame, ce_df: Optional[pd.DataFrame]) -> Dict[str, Dict[str, str]]:
    out: Dict[str, Dict[str, str]] = {}
    if ce_df is None or ce_df.empty:
        return out
    for _, row in animals_df.iterrows():
        parent = get_parent_abstract_from_row(row)
        if not parent or parent in ("None", "AnimalThingBase"):
            continue
        if parent in out:
            continue
        out[parent] = compute_ce_parent_common(animals_df, ce_df, row)
    return out


def _collect_rows_by_def(df: pd.DataFrame) -> Dict[str, object]:
    out = {}
    for _, row in df.iterrows():
        d = extract_def_name_from_xml_name(row.get("XML name", ""))
        if d:
            out[d] = row
    return out


def _collect_xml_files(path_or_dir: str) -> List[str]:
    p = os.path.normpath(path_or_dir)
    if os.path.isfile(p):
        return [p] if p.lower().endswith(".xml") else []
    out = []
    if os.path.isdir(p):
        for root_dir, _, files in os.walk(p):
            for name in files:
                if name.lower().endswith(".xml"):
                    out.append(os.path.join(root_dir, name))
    out.sort()
    return out


def update_xmls_in_path(
    *,
    xlsx: str,
    xml_input_path: str,
    out_dir: str,
    def_name: Optional[str] = None,
    animals_sheet: str = "Animals",
    animals_ce_sheet: str = "Animals CE",
    overwrite_existing: bool = False,
    emit_ce_patches: bool = False,
) -> Dict[str, object]:
    if not xlsx or not os.path.exists(xlsx):
        raise RuntimeError(f"XLSX/TSV not found: {xlsx}")
    if not xml_input_path or not os.path.exists(xml_input_path):
        raise RuntimeError(f"XML input path not found: {xml_input_path}")

    animals_df = read_table(xlsx, sheet_name=animals_sheet if is_excel_source(xlsx) else None)
    ce_df = None
    try:
        ce_df = read_table(xlsx, sheet_name=animals_ce_sheet if is_excel_source(xlsx) else None)
    except Exception:
        ce_df = None

    rows_by_def = _collect_rows_by_def(animals_df)
    ce_rows_by_def = _collect_rows_by_def(ce_df) if ce_df is not None else {}
    thing_parent_lookup, pawn_parent_lookup = _collect_parent_common_lookups(animals_df)
    ce_parent_lookup = _collect_ce_parent_common_lookup(animals_df, ce_df)

    files = _collect_xml_files(xml_input_path)
    if not files:
        raise RuntimeError("No XML files found in the selected input path.")

    base_in = os.path.normpath(xml_input_path if os.path.isdir(xml_input_path) else os.path.dirname(xml_input_path))
    updated_files = []
    processed_defs = set()
    skipped_files = []

    for src in files:
        try:
            root = LET.parse(src).getroot()
        except Exception:
            skipped_files.append(src)
            continue
        if root.tag != "Defs":
            skipped_files.append(src)
            continue

        changed = False
        target_filter = (def_name or "").strip()

        for thing in root.xpath("//ThingDef[defName]"):
            d_el = thing.find("defName")
            d = d_el.text.strip() if d_el is not None and d_el.text else ""
            if not d:
                continue
            if target_filter and d != target_filter:
                continue
            row = rows_by_def.get(d)
            if row is None:
                continue
            parent_common = compute_parent_common(animals_df, row)
            update_thingdef_inplace(thing, row, parent_common=parent_common, consider_parent_rules=True)
            strip_ce_from_base_thingdef(thing)
            processed_defs.add(d)
            changed = True

        for pawn in root.xpath("//PawnKindDef[defName]"):
            d_el = pawn.find("defName")
            d = d_el.text.strip() if d_el is not None and d_el.text else ""
            if not d:
                continue
            if target_filter and d != target_filter:
                continue
            row = rows_by_def.get(d)
            if row is None:
                continue
            parent_common = compute_parent_common(animals_df, row)
            update_pawnkind_inplace(pawn, row, parent_common=parent_common, consider_parent_rules=True)
            processed_defs.add(d)
            changed = True

        for parent_thing in root.xpath("//ThingDef[@Name]"):
            pname = parent_thing.get("Name", "").strip()
            if not pname:
                continue
            pinfo = thing_parent_lookup.get(pname)
            if pinfo:
                update_parent_thingdef_inplace(
                    parent_thing,
                    pinfo.get("thing_common", {}),
                    pinfo.get("thing_tools_row"),
                    pinfo.get("thing_differing", []),
                )
                strip_ce_from_base_thingdef(parent_thing)
                changed = True

        for parent_pawn in root.xpath("//PawnKindDef[@Name]"):
            pname = parent_pawn.get("Name", "").strip()
            if not pname:
                continue
            pinfo = pawn_parent_lookup.get(pname)
            if pinfo:
                update_parent_pawnkind_inplace(parent_pawn, pinfo.get("pawn_common", {}))
                changed = True

        if not changed:
            continue

        if overwrite_existing:
            out_path = src
        else:
            if not out_dir:
                raise RuntimeError("Output folder is required when overwrite is disabled.")
            rel = os.path.relpath(src, base_in)
            out_path = os.path.join(out_dir, rel)
        write_xml(out_path, root)
        updated_files.append(out_path)

    ce_patch_files = []
    if emit_ce_patches and processed_defs:
        ce_base = out_dir if out_dir else (xml_input_path if os.path.isdir(xml_input_path) else os.path.dirname(xml_input_path))
        ce_dir = os.path.join(ce_base, "ce_patches")
        os.makedirs(ce_dir, exist_ok=True)
        for d in sorted(processed_defs):
            row = rows_by_def.get(d)
            if row is None:
                continue
            ce_row = ce_rows_by_def.get(d)
            parent = get_parent_abstract_from_row(row)
            ce_parent_common = ce_parent_lookup.get(parent, {})
            ce_patch = build_ce_patch(
                d,
                row,
                ce_row,
                parent_abstract=parent,
                ce_parent_common=ce_parent_common,
                generate_parent=True,
            )
            out_ce = os.path.join(ce_dir, f"{d}_CE_patch.xml")
            write_xml(out_ce, ce_patch)
            ce_patch_files.append(out_ce)

    return {
        "updated_files": updated_files,
        "processed_defs": sorted(processed_defs),
        "skipped_files": skipped_files,
        "ce_patch_files": ce_patch_files,
    }


def create_safe_replace(def_name: str, path: str, tag: str, value) -> LET._Element:
    op = LET.Element("Operation", Class="PatchOperationConditional")
    full_path = f"/Defs/ThingDef[defName = \"{def_name}\"]/{path}/{tag}" if path else f"/Defs/ThingDef[defName = \"{def_name}\"]/{tag}"
    LET.SubElement(op, "xpath").text = full_path

    match = LET.SubElement(op, "match", Class="PatchOperationReplace")
    LET.SubElement(match, "xpath").text = full_path
    mval = LET.SubElement(match, "value")
    if isinstance(value, LET._Element):
        mval.append(copy.deepcopy(value))
    else:
        LET.SubElement(mval, tag).text = str(value)

    nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
    add_path = f"/Defs/ThingDef[defName = \"{def_name}\"]/{path}" if path else f"/Defs/ThingDef[defName = \"{def_name}\"]"
    LET.SubElement(nomatch, "xpath").text = add_path
    nval = LET.SubElement(nomatch, "value")
    if isinstance(value, LET._Element):
        nval.append(copy.deepcopy(value))
    else:
        LET.SubElement(nval, tag).text = str(value)
    return op


def create_safe_remove(def_name: str, path: str, tag: str) -> LET._Element:
    op = LET.Element("Operation", Class="PatchOperationConditional")
    full_path = f"/Defs/ThingDef[defName = \"{def_name}\"]/{path}/{tag}" if path else f"/Defs/ThingDef[defName = \"{def_name}\"]/{tag}"
    LET.SubElement(op, "xpath").text = full_path
    match = LET.SubElement(op, "match", Class="PatchOperationRemove")
    LET.SubElement(match, "xpath").text = full_path
    return op


def compute_ce_parent_common(animals_df: pd.DataFrame, ce_df: Optional[pd.DataFrame], row) -> Dict[str, str]:
    out: Dict[str, str] = {}
    if ce_df is None or ce_df.empty:
        return out
    parent_name = get_parent_abstract_from_row(row)
    if parent_name in ("", "None", "AnimalThingBase"):
        return out

    ce_rows_by_def = {}
    for _, ce_r in ce_df.iterrows():
        ce_def = extract_def_name_from_xml_name(ce_r.get("XML name", ""))
        if ce_def:
            ce_rows_by_def[ce_def] = ce_r

    child_defs = []
    for _, ar in animals_df.iterrows():
        if get_parent_abstract_from_row(ar) != parent_name:
            continue
        d = extract_def_name_from_xml_name(ar.get("XML name", ""))
        if d:
            child_defs.append(d)

    if not child_defs:
        return out

    for key in ("ArmorDurability", "Body shape"):
        vals = []
        raws = []
        for child_def in child_defs:
            ce_row = ce_rows_by_def.get(child_def)
            v = ce_row.get(key) if ce_row is not None else ""
            vals.append(norm(v))
            if not is_no_like(v):
                raws.append(str(v).strip())
        vals_non_empty = [v for v in vals if v != ""]
        if vals_non_empty and all(v == vals_non_empty[0] for v in vals_non_empty) and raws:
            out[key] = raws[0]
    return out


def _ce_ensure_comps(target_xpath: str) -> LET._Element:
    op = LET.Element("Operation", Class="PatchOperationConditional")
    LET.SubElement(op, "xpath").text = f"{target_xpath}/comps"
    nomatch = LET.SubElement(op, "nomatch", Class="PatchOperationAdd")
    LET.SubElement(nomatch, "xpath").text = target_xpath
    nval = LET.SubElement(nomatch, "value")
    LET.SubElement(nval, "comps")
    return op


def _ce_set_durability(target_xpath: str, durability: str) -> LET._Element:
    cond = LET.Element("Operation", Class="PatchOperationConditional")
    comp_xpath = f"{target_xpath}/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]"
    LET.SubElement(cond, "xpath").text = comp_xpath
    m = LET.SubElement(cond, "match", Class="PatchOperationReplace")
    LET.SubElement(m, "xpath").text = comp_xpath + "/Durability"
    mv = LET.SubElement(m, "value")
    LET.SubElement(mv, "Durability").text = str(durability).strip()
    nm = LET.SubElement(cond, "nomatch", Class="PatchOperationAdd")
    LET.SubElement(nm, "xpath").text = f"{target_xpath}/comps"
    nval = LET.SubElement(nm, "value")
    li = LET.SubElement(nval, "li", Class="CombatExtended.CompProperties_ArmorDurability")
    LET.SubElement(li, "Durability").text = str(durability).strip()
    LET.SubElement(li, "Regenerates").text = "true"
    LET.SubElement(li, "RegenInterval").text = "600"
    LET.SubElement(li, "RegenValue").text = "5"
    LET.SubElement(li, "MinArmorPct").text = "0.5"
    return cond


def _ce_remove_durability_comp(target_xpath: str) -> LET._Element:
    op = LET.Element("Operation", Class="PatchOperationConditional")
    comp_xpath = f"{target_xpath}/comps/li[@Class=\"CombatExtended.CompProperties_ArmorDurability\"]"
    LET.SubElement(op, "xpath").text = comp_xpath
    match = LET.SubElement(op, "match", Class="PatchOperationRemove")
    LET.SubElement(match, "xpath").text = comp_xpath
    return op


def _ce_set_body_shape(target_xpath: str, body_shape: str) -> LET._Element:
    op = LET.Element("Operation", Class="PatchOperationConditional")
    ext_xpath = f"{target_xpath}/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]"
    LET.SubElement(op, "xpath").text = ext_xpath
    match_seq = LET.SubElement(op, "match", Class="PatchOperationSequence")
    ops = LET.SubElement(match_seq, "operations")

    inner = LET.SubElement(ops, "li", Class="PatchOperationConditional")
    LET.SubElement(inner, "xpath").text = ext_xpath + "/bodyShape"
    im = LET.SubElement(inner, "match", Class="PatchOperationReplace")
    LET.SubElement(im, "xpath").text = ext_xpath + "/bodyShape"
    imv = LET.SubElement(im, "value")
    LET.SubElement(imv, "bodyShape").text = str(body_shape).strip()
    inm = LET.SubElement(inner, "nomatch", Class="PatchOperationAdd")
    LET.SubElement(inm, "xpath").text = ext_xpath
    inv = LET.SubElement(inm, "value")
    LET.SubElement(inv, "bodyShape").text = str(body_shape).strip()

    nm = LET.SubElement(op, "nomatch", Class="PatchOperationAddModExtension")
    LET.SubElement(nm, "xpath").text = target_xpath
    nval = LET.SubElement(nm, "value")
    li = LET.SubElement(nval, "li", Class="CombatExtended.RacePropertiesExtensionCE")
    LET.SubElement(li, "bodyShape").text = str(body_shape).strip()
    return op


def _ce_remove_body_shape(target_xpath: str) -> LET._Element:
    op = LET.Element("Operation", Class="PatchOperationConditional")
    body_xpath = f"{target_xpath}/modExtensions/li[@Class=\"CombatExtended.RacePropertiesExtensionCE\"]/bodyShape"
    LET.SubElement(op, "xpath").text = body_xpath
    match = LET.SubElement(op, "match", Class="PatchOperationRemove")
    LET.SubElement(match, "xpath").text = body_xpath
    return op


def build_ce_patch(
    def_name: str,
    vanilla_row,
    ce_row,
    parent_abstract: str = "AnimalThingBase",
    ce_parent_common: Optional[Dict[str, str]] = None,
    generate_parent: bool = False,
) -> LET._Element:
    patch = LET.Element("Patch")
    patch.append(LET.Comment(f" Combat Extended patch for {def_name} (no FindMod wrapper) "))
    if ce_row is None:
        patch.append(LET.Comment(" No CE row found in Animals CE sheet "))
        return patch

    child_target = f"/Defs/ThingDef[defName = \"{def_name}\"]"
    use_parent = bool(generate_parent and parent_abstract and parent_abstract not in ("None", "AnimalThingBase"))
    parent_target = f"/Defs/ThingDef[@Name = \"{parent_abstract}\"]" if use_parent else ""
    ce_parent_common = ce_parent_common or {}

    for stat in ("MeleeDodgeChance", "MeleeCritChance", "MeleeParryChance", "ArmorRating_Sharp", "ArmorRating_Blunt"):
        val = ce_row.get(stat)
        if not is_no_like(val):
            patch.append(create_safe_replace(def_name, "statBases", stat, str(val).strip()))

    child_dur = ce_row.get("ArmorDurability")
    parent_dur = ce_parent_common.get("ArmorDurability") if use_parent else None
    if not is_no_like(parent_dur):
        patch.append(_ce_ensure_comps(parent_target))
        patch.append(_ce_set_durability(parent_target, str(parent_dur).strip()))
        patch.append(_ce_remove_durability_comp(child_target))
        if not is_no_like(child_dur) and norm(child_dur) != norm(parent_dur):
            patch.append(_ce_ensure_comps(child_target))
            patch.append(_ce_set_durability(child_target, str(child_dur).strip()))
    elif not is_no_like(child_dur):
        patch.append(_ce_ensure_comps(child_target))
        patch.append(_ce_set_durability(child_target, str(child_dur).strip()))

    child_body_shape = ce_row.get("Body shape")
    parent_body_shape = ce_parent_common.get("Body shape") if use_parent else None
    if not is_no_like(parent_body_shape):
        patch.append(_ce_set_body_shape(parent_target, str(parent_body_shape).strip()))
        patch.append(_ce_remove_body_shape(child_target))
        if not is_no_like(child_body_shape) and norm(child_body_shape) != norm(parent_body_shape):
            patch.append(_ce_set_body_shape(child_target, str(child_body_shape).strip()))
    elif not is_no_like(child_body_shape):
        patch.append(_ce_set_body_shape(child_target, str(child_body_shape).strip()))

    ce_tools = build_tools_ce(ce_row, vanilla_row=vanilla_row)
    if ce_tools is not None:
        patch.append(create_safe_remove(def_name, "", "tools"))
        add_tools = LET.Element("Operation", Class="PatchOperationAdd")
        LET.SubElement(add_tools, "xpath").text = f"/Defs/ThingDef[defName = \"{def_name}\"]"
        v = LET.SubElement(add_tools, "value")
        v.append(ce_tools)
        patch.append(add_tools)

    patch.append(create_safe_remove(def_name, "statBases", "ArmorRating_Heat"))
    return patch


def find_row_by_def(df: pd.DataFrame, def_name: str):
    matches = []
    for _, row in df.iterrows():
        xml_name = row.get("XML name", "") if "XML name" in row.index else ""
        parsed = extract_def_name_from_xml_name(xml_name)
        if parsed == def_name:
            matches.append(row)
    return matches[0] if matches else None


def write_xml(path: str, root: LET._Element):
    d = os.path.dirname(path)
    if d:
        os.makedirs(d, exist_ok=True)
    out_root = copy.deepcopy(root)
    try:
        LET.indent(out_root, space="  ")
    except Exception:
        pass
    with open(path, "wb") as f:
        f.write(LET.tostring(out_root, pretty_print=False, encoding="utf-8", xml_declaration=True))


def _infer_single_def_name_from_xml(root: LET._Element) -> Optional[str]:
    thing_defs = []
    for td in root.xpath("//ThingDef[defName]"):
        d = td.find("defName")
        if d is not None and d.text and d.text.strip():
            thing_defs.append(d.text.strip())
    pawn_defs = []
    for pd in root.xpath("//PawnKindDef[defName]"):
        d = pd.find("defName")
        if d is not None and d.text and d.text.strip():
            pawn_defs.append(d.text.strip())

    thing_set = set(thing_defs)
    pawn_set = set(pawn_defs)
    candidates = list(thing_set & pawn_set)
    if len(candidates) == 1:
        return candidates[0]
    union = list(thing_set | pawn_set)
    if len(union) == 1:
        return union[0]
    return None


def update_existing_xml_for_def(
    *,
    xlsx: str,
    xml_path: str,
    out_dir: str,
    def_name: Optional[str] = None,
    animals_sheet: str = "Animals",
    animals_ce_sheet: str = "Animals CE",
    game_root_dir: str = DEFAULT_GAME_ROOT_DIR,
    consider_parent_rules: bool = True,
    overwrite_existing: bool = False,
) -> Tuple[str, str]:
    result = update_xmls_in_path(
        xlsx=xlsx,
        xml_input_path=xml_path,
        out_dir=out_dir,
        def_name=def_name,
        animals_sheet=animals_sheet,
        animals_ce_sheet=animals_ce_sheet,
        overwrite_existing=overwrite_existing,
        emit_ce_patches=False,
    )
    updated_files = result.get("updated_files", [])
    processed_defs = result.get("processed_defs", [])
    if not updated_files:
        raise RuntimeError("No matching defs were updated in the selected XML.")
    resolved_def = processed_defs[0] if processed_defs else (def_name or "")
    return updated_files[0], resolved_def


def generate_for_def(
    *,
    xlsx: str,
    def_name: str,
    out_dir: str,
    animals_sheet: str = "Animals",
    animals_ce_sheet: str = "Animals CE",
    game_root_dir: str = DEFAULT_GAME_ROOT_DIR,
    generate_parent: bool = False,
) -> Tuple[str, str]:
    if not xlsx or not os.path.exists(xlsx):
        raise RuntimeError(f"XLSX not found: {xlsx}")

    animals_df = read_table(xlsx, sheet_name=animals_sheet if is_excel_source(xlsx) else None)
    ce_df = None
    try:
        ce_df = read_table(xlsx, sheet_name=animals_ce_sheet if is_excel_source(xlsx) else None)
    except Exception:
        ce_df = None

    row = find_row_by_def(animals_df, def_name)
    if row is None:
        raise RuntimeError(f"Animal '{def_name}' not found in sheet '{animals_sheet}'.")
    ce_row = find_row_by_def(ce_df, def_name) if ce_df is not None else None

    parent_abstract = get_parent_abstract_from_row(row)

    existing_thing, existing_pawn = find_existing_defs(game_root_dir, def_name)
    manual = build_manual_inputs(def_name, parent_abstract, existing_thing, existing_pawn, game_root_dir)

    parent_common = compute_parent_common(animals_df, row) if generate_parent else None
    ce_parent_common = compute_ce_parent_common(animals_df, ce_df, row) if generate_parent else None

    defs_root = LET.Element("Defs")
    defs_root.append(LET.Comment(f" Generated from {os.path.basename(xlsx)} for {def_name} "))

    if generate_parent and parent_common:
        thing_parent_name = parent_common.get("thing_parent_name", "")
        if thing_parent_name and thing_parent_name not in ("AnimalThingBase", "None"):
            pthing = build_parent_thingdef(
                thing_parent_name,
                parent_common.get("thing_common", {}),
                parent_common.get("thing_tools_row"),
                game_root_dir,
            )
            if pthing is not None:
                # Safety: base XML must stay CE-free; CE data belongs to separate patch.
                strip_ce_from_base_thingdef(pthing)
                defs_root.append(LET.Comment(f" Generated parent abstract ThingDef: {thing_parent_name} "))
                defs_root.append(pthing)

        pawn_parent_name = parent_common.get("pawn_parent_name", "")
        if pawn_parent_name and pawn_parent_name not in ("AnimalKindBase", "None"):
            ppawn = build_parent_pawnkind(
                pawn_parent_name,
                parent_common.get("pawn_common", {}),
                game_root_dir,
            )
            if ppawn is not None:
                defs_root.append(LET.Comment(f" Generated parent abstract PawnKindDef: {pawn_parent_name} "))
                defs_root.append(ppawn)

    thing = build_thingdef_from_row(
        row,
        def_name,
        manual,
        parent_common=parent_common,
        include_all=not generate_parent,
        force_inherit_false_lists=True,
    )
    # Safety: base XML must stay CE-free; CE data belongs to separate patch.
    strip_ce_from_base_thingdef(thing)
    defs_root.append(thing)
    defs_root.append(
        build_pawnkind_from_row(
            row,
            def_name,
            manual,
            parent_common=parent_common,
            include_all=not generate_parent,
            force_inherit_false_lists=True,
        )
    )

    ce_patch_root = build_ce_patch(
        def_name,
        row,
        ce_row,
        parent_abstract=parent_abstract,
        ce_parent_common=ce_parent_common,
        generate_parent=bool(generate_parent),
    )

    out_full = os.path.join(out_dir, f"{def_name}.xml")
    out_ce = os.path.join(out_dir, f"{def_name}_CE_patch.xml")
    write_xml(out_full, defs_root)
    write_xml(out_ce, ce_patch_root)
    return out_full, out_ce


class GeneratorApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("RimWorld XML Generator")
        self.geometry("1080x520")
        self.cfg = load_config()

        script_dir = os.path.dirname(os.path.abspath(__file__))
        default_xlsx = self.cfg.get("xlsx", os.path.join(script_dir, "AnimalStats.xlsx"))
        default_out = self.cfg.get("out_dir", os.path.join(script_dir, "generated_defs"))
        default_game_root = (
            self.cfg.get("game_root_dir")
            or infer_game_root(self.cfg.get("core_thingdefs_dir", ""))
            or DEFAULT_GAME_ROOT_DIR
        )

        self.xlsx = tk.StringVar(value=default_xlsx)
        self.mode = tk.StringVar(value=self.cfg.get("mode", "generate"))
        self.def_name = tk.StringVar(value=self.cfg.get("def_name", ""))
        self.existing_xml = tk.StringVar(value=self.cfg.get("existing_xml", ""))
        self.out_dir = tk.StringVar(value=default_out)
        self.game_root_dir = tk.StringVar(value=default_game_root)
        self.animals_sheet = tk.StringVar(value=self.cfg.get("animals_sheet", "Animals"))
        self.animals_ce_sheet = tk.StringVar(value=self.cfg.get("animals_ce_sheet", "Animals CE"))
        self.generate_parent = tk.BooleanVar(value=bool(self.cfg.get("generate_parent", False)))
        self.overwrite_existing = tk.BooleanVar(value=bool(self.cfg.get("overwrite_existing", False)))
        self.emit_ce_patches = tk.BooleanVar(value=bool(self.cfg.get("emit_ce_patches", True)))
        self.status = tk.StringVar(value="")
        self._build_ui()

    def _save_cfg(self):
        self.cfg.update(
            {
                "xlsx": self.xlsx.get(),
                "mode": self.mode.get(),
                "def_name": self.def_name.get(),
                "existing_xml": self.existing_xml.get(),
                "out_dir": self.out_dir.get(),
                "game_root_dir": self.game_root_dir.get(),
                # Legacy key for backward compatibility with older config files.
                "core_thingdefs_dir": self.game_root_dir.get(),
                "animals_sheet": self.animals_sheet.get(),
                "animals_ce_sheet": self.animals_ce_sheet.get(),
                "generate_parent": bool(self.generate_parent.get()),
                "overwrite_existing": bool(self.overwrite_existing.get()),
                "emit_ce_patches": bool(self.emit_ce_patches.get()),
            }
        )
        save_config(self.cfg)

    def _build_ui(self):
        main = ttk.Frame(self)
        main.pack(fill="both", expand=True, padx=10, pady=10)
        self.ui_rows = {}

        def add_row(key, label, var, browse=None):
            row = ttk.Frame(main)
            row.pack(fill="x", pady=4)
            ttk.Label(row, text=label, width=24).pack(side="left")
            ttk.Entry(row, textvariable=var, width=90).pack(side="left", padx=6, fill="x", expand=True)
            if browse is not None:
                ttk.Button(row, text="Browse", command=browse).pack(side="left")
            self.ui_rows[key] = row
            return row

        mode_row = ttk.Frame(main)
        mode_row.pack(fill="x", pady=4)
        ttk.Label(mode_row, text="Mode:", width=24).pack(side="left")
        ttk.Radiobutton(
            mode_row,
            text="Generate New XML",
            variable=self.mode,
            value="generate",
            command=self._update_mode_ui,
        ).pack(side="left")
        ttk.Radiobutton(
            mode_row,
            text="Update Existing XML",
            variable=self.mode,
            value="update",
            command=self._update_mode_ui,
        ).pack(side="left", padx=12)

        add_row("xlsx", "AnimalStats XLSX:", self.xlsx, self.pick_xlsx)
        add_row("def_name", "DefName (optional in update):", self.def_name, None)

        xml_row = ttk.Frame(main)
        xml_row.pack(fill="x", pady=4)
        ttk.Label(xml_row, text="XML file/folder:", width=24).pack(side="left")
        ttk.Entry(xml_row, textvariable=self.existing_xml, width=90).pack(side="left", padx=6, fill="x", expand=True)
        ttk.Button(xml_row, text="File", command=self.pick_existing_xml_file).pack(side="left")
        ttk.Button(xml_row, text="Folder", command=self.pick_existing_xml_folder).pack(side="left", padx=4)
        self.ui_rows["existing_xml"] = xml_row

        add_row("out_dir", "Output folder:", self.out_dir, self.pick_out_dir)
        add_row("game_root", "RimWorld game root:", self.game_root_dir, self.pick_game_root_dir)
        add_row("animals_sheet", "Animals sheet:", self.animals_sheet, None)
        add_row("animals_ce_sheet", "Animals CE sheet:", self.animals_ce_sheet, None)

        opts = ttk.Frame(main)
        opts.pack(fill="x", pady=8)
        self.opts_frame = opts
        self.chk_generate_parent = ttk.Checkbutton(
            opts,
            text="Generate parent abstracts from whole table (approx. PatchFixer rules)",
            variable=self.generate_parent,
        )
        self.chk_generate_parent.pack(anchor="w")
        self.chk_overwrite_existing = ttk.Checkbutton(
            opts,
            text="Update mode: overwrite source XML files",
            variable=self.overwrite_existing,
        )
        self.chk_overwrite_existing.pack(anchor="w")
        self.chk_emit_ce = ttk.Checkbutton(
            opts,
            text="Update mode: generate CE patch files for processed defs",
            variable=self.emit_ce_patches,
        )
        self.chk_emit_ce.pack(anchor="w")

        actions = ttk.Frame(main)
        actions.pack(fill="x", pady=10)
        ttk.Button(actions, text="Run", command=self.run_generate).pack(side="left")
        ttk.Button(actions, text="Open Output Folder", command=self.open_output).pack(side="left", padx=8)

        ttk.Label(main, textvariable=self.status).pack(fill="x")
        self._update_mode_ui()

    def _show_row(self, key: str, show: bool):
        row = self.ui_rows.get(key)
        if row is None:
            return
        if show:
            row.pack(fill="x", pady=4)
        else:
            row.pack_forget()

    def _update_mode_ui(self):
        mode = self.mode.get().strip() or "generate"
        is_update = mode == "update"
        self._show_row("existing_xml", is_update)
        self._show_row("game_root", not is_update)
        if is_update:
            self.chk_generate_parent.pack_forget()
            self.chk_overwrite_existing.pack(anchor="w")
            self.chk_emit_ce.pack(anchor="w")
        else:
            self.chk_overwrite_existing.pack_forget()
            self.chk_emit_ce.pack_forget()
            self.chk_generate_parent.pack(anchor="w")
        self._save_cfg()

    def pick_xlsx(self):
        p = filedialog.askopenfilename(
            title="Select AnimalStats XLSX/TSV",
            filetypes=[("Tables", "*.xlsx *.xlsm *.xls *.tsv"), ("All", "*.*")],
        )
        if p:
            self.xlsx.set(p)
            self._save_cfg()

    def pick_out_dir(self):
        p = filedialog.askdirectory(title="Select output folder")
        if p:
            self.out_dir.set(p)
            self._save_cfg()

    def pick_existing_xml_file(self):
        p = filedialog.askopenfilename(
            title="Select existing Defs XML file",
            filetypes=[("XML", "*.xml"), ("All", "*.*")],
        )
        if p:
            self.existing_xml.set(p)
            self._save_cfg()

    def pick_existing_xml_folder(self):
        p = filedialog.askdirectory(title="Select folder with Defs XML files")
        if p:
            self.existing_xml.set(p)
            self._save_cfg()

    def pick_game_root_dir(self):
        p = filedialog.askdirectory(title="Select RimWorld game root folder")
        if p:
            self.game_root_dir.set(p)
            self._save_cfg()

    def open_output(self):
        path = self.out_dir.get().strip()
        if not path:
            return
        try:
            if sys.platform == "win32":
                os.startfile(path)
        except Exception as e:
            messagebox.showerror("Error", str(e), parent=self)

    def run_generate(self):
        global PROMPTS
        xlsx = self.xlsx.get().strip()
        def_name = self.def_name.get().strip()
        mode = self.mode.get().strip() or "generate"
        xml_input = self.existing_xml.get().strip()
        out_dir = self.out_dir.get().strip()
        if not xlsx or not os.path.exists(xlsx):
            messagebox.showerror("Error", "XLSX/TSV file not found.", parent=self)
            return

        self._save_cfg()
        try:
            PROMPTS = TkPromptProvider(self)
            if mode == "update":
                if not xml_input or not os.path.exists(xml_input):
                    messagebox.showerror("Error", "XML file/folder for update mode not found.", parent=self)
                    return
                if not bool(self.overwrite_existing.get()) and not out_dir:
                    messagebox.showerror("Error", "Output folder is required unless overwrite is enabled.", parent=self)
                    return
                res = update_xmls_in_path(
                    xlsx=xlsx,
                    xml_input_path=xml_input,
                    out_dir=out_dir,
                    def_name=(def_name or None),
                    animals_sheet=self.animals_sheet.get().strip() or "Animals",
                    animals_ce_sheet=self.animals_ce_sheet.get().strip() or "Animals CE",
                    overwrite_existing=bool(self.overwrite_existing.get()),
                    emit_ce_patches=bool(self.emit_ce_patches.get()),
                )
                updated_count = len(res.get("updated_files", []))
                defs_count = len(res.get("processed_defs", []))
                ce_count = len(res.get("ce_patch_files", []))
                skipped = len(res.get("skipped_files", []))
                self.status.set(
                    f"Updated files: {updated_count}; defs: {defs_count}; CE patches: {ce_count}; skipped files: {skipped}"
                )
                messagebox.showinfo(
                    "Done",
                    f"Updated files: {updated_count}\nDefs processed: {defs_count}\nCE patches: {ce_count}\nSkipped files: {skipped}",
                    parent=self,
                )
            else:
                if not def_name:
                    messagebox.showerror("Error", "DefName is required in Generate mode.", parent=self)
                    return
                if not out_dir:
                    messagebox.showerror("Error", "Output folder is required in Generate mode.", parent=self)
                    return
                out_full, out_ce = generate_for_def(
                    xlsx=xlsx,
                    def_name=def_name,
                    out_dir=out_dir,
                    animals_sheet=self.animals_sheet.get().strip() or "Animals",
                    animals_ce_sheet=self.animals_ce_sheet.get().strip() or "Animals CE",
                    game_root_dir=self.game_root_dir.get().strip(),
                    generate_parent=bool(self.generate_parent.get()),
                )
                self.status.set(f"Generated: {out_full} | {out_ce}")
                messagebox.showinfo("Done", f"Generated:\n{out_full}\n{out_ce}", parent=self)
        except Exception as e:
            traceback.print_exc()
            messagebox.showerror("Error", str(e), parent=self)
        finally:
            PROMPTS = ConsolePromptProvider()


def parse_cli_args():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_xlsx = os.path.join(script_dir, "AnimalStats.xlsx")
    parser = argparse.ArgumentParser(description="Generate or update RimWorld animal Def XML from AnimalStats.xlsx")
    parser.add_argument(
        "--mode",
        choices=("generate", "update"),
        default="generate",
        help="generate: build new XML files; update: update known fields in existing XML file(s)/folder",
    )
    parser.add_argument("--xlsx", default=default_xlsx, help="Path to AnimalStats.xlsx (or TSV for Animals)")
    parser.add_argument("--def-name", help="Animal defName to generate")
    parser.add_argument("--input-path", help="Update mode: XML file or folder with XML files")
    parser.add_argument("--existing-xml", dest="existing_xml_legacy", help=argparse.SUPPRESS)
    parser.add_argument(
        "--out-dir",
        default=os.path.join(script_dir, "generated_defs"),
        help="Output folder for generated/updated xml files",
    )
    parser.add_argument("--animals-sheet", default="Animals", help="Sheet name for vanilla animal table")
    parser.add_argument("--animals-ce-sheet", default="Animals CE", help="Sheet name for CE animal table")
    parser.add_argument(
        "--game-root-dir",
        default=DEFAULT_GAME_ROOT_DIR,
        help="RimWorld game root folder (auto-search uses Data/Core and Data/Odyssey ThingDefs_Races)",
    )
    parser.add_argument("--core-thingdefs-dir", dest="core_thingdefs_dir_legacy", help=argparse.SUPPRESS)
    parser.add_argument(
        "--generate-parent",
        action="store_true",
        help="Generate mode only: generate parent abstracts from whole table",
    )
    parser.add_argument(
        "--overwrite-existing",
        action="store_true",
        help="Update mode only: overwrite source XML file(s) instead of writing to out-dir",
    )
    parser.add_argument(
        "--emit-ce-patches",
        action="store_true",
        help="Update mode: also generate CE patch XML files for processed defs",
    )
    args = parser.parse_args()
    if getattr(args, "existing_xml_legacy", None):
        args.input_path = args.existing_xml_legacy
    if args.input_path and args.mode == "generate":
        args.mode = "update"
    if getattr(args, "core_thingdefs_dir_legacy", None):
        args.game_root_dir = args.core_thingdefs_dir_legacy
    return args


def main():
    global PROMPTS
    if len(sys.argv) == 1:
        app = GeneratorApp()
        app.mainloop()
        return

    args = parse_cli_args()
    PROMPTS = ConsolePromptProvider()
    if args.mode == "update":
        if not args.input_path:
            raise RuntimeError("--input-path is required in update mode.")
        if not args.overwrite_existing and not args.out_dir:
            raise RuntimeError("--out-dir is required unless --overwrite-existing is set.")
        res = update_xmls_in_path(
            xlsx=args.xlsx,
            xml_input_path=args.input_path,
            out_dir=args.out_dir,
            def_name=args.def_name,
            animals_sheet=args.animals_sheet,
            animals_ce_sheet=args.animals_ce_sheet,
            overwrite_existing=bool(args.overwrite_existing),
            emit_ce_patches=bool(args.emit_ce_patches),
        )
        print("")
        print("Updated:")
        print(f"  files: {len(res.get('updated_files', []))}")
        print(f"  defs: {len(res.get('processed_defs', []))}")
        print(f"  CE patches: {len(res.get('ce_patch_files', []))}")
        if res.get("skipped_files"):
            print(f"  skipped files: {len(res.get('skipped_files', []))}")
    else:
        def_name = args.def_name or prompt_text("Enter defName", required=True)
        out_full, out_ce = generate_for_def(
            xlsx=args.xlsx,
            def_name=def_name,
            out_dir=args.out_dir,
            animals_sheet=args.animals_sheet,
            animals_ce_sheet=args.animals_ce_sheet,
            game_root_dir=args.game_root_dir,
            generate_parent=bool(args.generate_parent),
        )
        print("")
        print("Generated:")
        print(f"  {out_full}")
        print(f"  {out_ce}")


if __name__ == "__main__":
    main()
