"""Stage spreadsheet-driven bionic damage and LifeStageDef factors for review.

This tool does not modify the mod. It never infers missing spreadsheet cells,
CE bionic mechanics or hand-edited LifeStage rules.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
import pandas as pd
from lxml import etree as E
from core.animalstats_source import is_multisheet_source, read_google_sheet, read_excel_cached
from core.xml_routing import find_project_root, digest
import core.checker_config as checker_config

LIFE_ROWS={
 'AnimalAdult':0,'EusocialInsectAdult':0,
 'AnimalJuvenile':1,'EusocialInsectJuvenile':1,
 'AnimalBaby':2,'EusocialInsectLarva':2,'AnimalBabyTiny':3,
}
LIFE_BASE={'bodySizeFactor':'bodySizeFactor','marketValueFactor':'marketValueFactor',
 'healthScaleFactor':'healthScaleFactor','hungerRateFactor':'hungerRateFactor',
 'meleeDamageFactor':'meleeDamageFactor'}
SHEET_REQUIRED={'Bionics':('Weapon','Damage','Attack interval'),
                'LifeStages':('Life stage','bodySizeFactor','meleeDamageFactor')}


def read_table(source,sheet):
    if not is_multisheet_source(source):
        raise ValueError(f'{sheet} requires a multisheet XLSX or Google Sheets source; a single TSV cannot supply both')
    is_cloud = str(source).startswith(('https://','http://','gsheet:')) or (len(str(source))>20 and not Path(str(source)).exists() and '.' not in str(source))
    if is_cloud:
        frame=read_google_sheet(source,sheet_name=sheet)
        if sheet=='Bionics':
            # Bionics uses a banner row before the actual column names.
            if frame.empty: raise ValueError('Bionics sheet has no headers')
            frame.columns=[str(x).strip() for x in frame.iloc[0].tolist()]
            frame=frame.iloc[1:].copy()
    else:
        frame=read_excel_cached(source,sheet_name=sheet,header=1 if sheet=='Bionics' else 0,
                            dtype=str,keep_default_na=False)
    missing=set(SHEET_REQUIRED.get(sheet,()))-set(frame.columns)
    if missing: raise ValueError(f'{sheet}: missing required columns: {sorted(missing)}')
    return frame.fillna('')


def text_value(value):
    if value is None: return None
    v=str(value).strip()
    if v.lower() in ('','no','none','nan'): return None
    try: return float(v.replace(',','.'))
    except ValueError: raise ValueError(f'Invalid numeric spreadsheet value {v!r}')


def display_number(value):
    return format(value,'.12g')


def update_bionics(tree,table):
    """Update only Vanilla damage/cooldown, preserving recipe/body part metadata."""
    defs={elem.findtext('defName'):elem for elem in tree.findall('.//HediffDef') if elem.findtext('defName')}
    changes=[]; warnings=[]; seen=set()
    for _,record in table.iterrows():
        label=str(record.get('Weapon','')).strip()
        match=re.fullmatch(r'(BionicArm|BionicLeg|BionicJaw|ArchotechArm|ArchotechLeg)_(VerySmall|Small|Medium|Large|VeryLarge|Huge)',label)
        if not match: continue
        name=f'{match[1]}_Animal_{match[2]}'
        if name in seen: raise ValueError(f'Duplicate Bionics row: {label}')
        seen.add(name)
        if name not in defs: raise ValueError(f'Bionics sheet references missing HediffDef: {name}')
        tools=defs[name].xpath('./comps/li/tools/li')
        if len(tools)!=1: raise ValueError(f'{name}: expected exactly one attack tool')
        for column,tag in (('Damage','power'),('Attack interval','cooldownTime')):
            expected=text_value(record.get(column,''))
            if expected is None:
                warnings.append(f'{name}: blank/No {column}; existing value retained')
                continue
            node=tools[0].find(tag)
            if node is None: raise ValueError(f'{name}: missing {tag}')
            actual=text_value(node.text)
            if actual is None or abs(actual-expected)>1e-7:
                changes.append({'defName':name,'field':tag,'old':node.text,'new':display_number(expected)})
                node.text=display_number(expected)
    missing=set(defs).intersection({f'{kind}_Animal_{size}' for kind in ('BionicArm','BionicLeg','BionicJaw','ArchotechArm','ArchotechLeg') for size in ('VerySmall','Small','Medium','Large','VeryLarge','Huge')})-seen
    if missing: raise ValueError(f'Spreadsheet has no entries for existing bionic variants: {sorted(missing)}')
    return changes,warnings


def update_life_stages(tree,table,ce=False):
    rows={}
    for _,record in table.iterrows():
        key=str(record.get('Life stage','')).strip()
        for stage in LIFE_ROWS:
            if re.search(r'(?<![A-Za-z])'+re.escape(stage)+r'(?![A-Za-z])',key):
                if stage in rows: raise ValueError(f'Duplicate LifeStages row for {stage}')
                rows[stage]=record
    if not rows: raise ValueError('No recognized LifeStages rows')
    changes=[]; warnings=[]
    # Only modify operations targeting exactly the same stage group for a
    # spreadsheet column. Do not rewrite arbitrary hand-authored XML/XPaths.
    for operation in tree.getroot().findall('Operation'):
        xpath=operation.findtext('xpath') or ''
        fields={'meleeDamageFactor':'meleeDamageFactor(CE)'} if ce else LIFE_BASE
        match=re.search(r'/('+ '|'.join(re.escape(x) for x in fields)+r')\s*$',xpath)
        if not match: continue
        field=match[1]; names=set(re.findall(r'defName\s*=\s*[\"\']([A-Za-z]+)[\"\']',xpath))
        if not names or any(name not in rows for name in names): continue
        values=[text_value(rows[name].get(fields[field],'')) for name in names]
        if any(v is None for v in values):
            warnings.append(f'LifeStages {field} {sorted(names)}: empty source cell; original retained')
            continue
        if len(set(values))!=1:
            raise ValueError(f'LifeStages {field} {sorted(names)}: grouped XML uses different spreadsheet values {values}')
        expected=values[0]
        for node in operation.xpath('./match/value/* | ./nomatch/value/*'):
            if node.tag!=field: continue
            old=text_value(node.text)
            if old is None or abs(old-expected)>1e-7:
                changes.append({'stages':sorted(names),'field':field,'old':node.text,'new':display_number(expected)})
                node.text=display_number(expected)
    return changes,warnings


