"""Refresh owned reproductive XML fields from AnimalStats EggMilkWool without rewriting hand-authored logic.

Data-flow is deliberately narrow: an existing, reviewed XML Patch remains at its
exact LoadFolders destination; only documented numeric/string values are changed.
Unsupported/ambiguous targets are preserved and reported, never guessed.
"""
from __future__ import annotations

from decimal import Decimal, InvalidOperation
from pathlib import Path
import json
import os
import re
import tempfile
from lxml import etree as E

from core.static_stats import read_table
from core.xml_routing import digest
from core.checker_config import ROOT

# Spreadsheet tab name; the stable legacy manifest marker 'EggMilk' is NOT a sheet name.
REPRODUCTION_SHEET = 'EggMilkWool'

# Exact previously hand-authored locations (not filename-derived routing).

KINDS={'eggLayIntervalDays':'egg','eggCountRange':'egg','hatcherDaystoHatch':'hatch',
       'milkAmount':'milk','woolAmount':'wool'}
PATH_NAME=re.compile(r'\bdefName\s*=\s*["\']([^"\']+)["\']')
TAG=re.compile(r'(?:/|^)('+'|'.join(KINDS)+r')(?:/text\(\))?\s*$')
DEFNAME=re.compile(r'^<li>\s*([^<>]+)\s*</li>$')
RANGE=re.compile(r'^[1-9]\d*(?:~[1-9]\d*)?$')


def _value(cell, *, integer=False):
    if cell is None or str(cell).strip() in ('','No','None'):
        raise ValueError('Missing required calculated value')
    val=str(cell).strip()
    if val.startswith(('#ERROR','#[A-Z]')):
        raise ValueError(f'Formula error in AnimalStats: {val}')
    try: n=Decimal(val.replace(',','.'))
    except InvalidOperation: raise ValueError(f'Invalid numeric result: {val!r}')
    if not n.is_finite() or n<=0: raise ValueError(f'Expected positive value: {val!r}')
    if integer and n!=n.to_integral_value(): raise ValueError(f'Expected an integer: {val!r}')
    return n


def _format(value):
    if value == value.to_integral_value(): return format(value.quantize(Decimal('1')),'f')
    return format(value.normalize(),'f')


def _defname(xml):
    matched=DEFNAME.fullmatch(str(xml).strip())
    return matched[1] if matched else None


def _index(tables):
    egg,animals=tables['EggMilk'],tables['Animals']
    by_egg={}; by_common={}; milk={}; wool={}
    for _,r in animals.iterrows():
        name=str(r.get('Common name','')).strip().casefold(); slug=_defname(r.get('XML name',''))
        if name and slug: by_common.setdefault(name,set()).add(slug)
    for _,r in egg.iterrows():
        slug=_defname(r.get('F',''))
        if slug:
            if slug in by_egg: raise ValueError(f'Duplicate EggMilkWool Def name: {slug}')
            by_egg[slug]=dict(r)
        for namecol,target in [('K',milk),('P',wool)]:
            name=str(r.get(namecol,'')).strip().casefold()
            if not name or name in ('none','no','nan'): continue
            if name in target: raise ValueError(f'Duplicate EggMilkWool name: {name}')
            target[name]=dict(r)
    resolved={}; ambiguous=[]
    for name,r in milk.items():
        slugs=by_common.get(name,())
        if len(slugs)!=1: ambiguous.append(('milk',name,sorted(slugs))); continue
        resolved[next(iter(slugs))]=r
    resolved_wool={}
    for name,r in wool.items():
        slugs=by_common.get(name,())
        if len(slugs)!=1: ambiguous.append(('wool',name,sorted(slugs))); continue
        resolved_wool[next(iter(slugs))]=r
    return by_egg,resolved,resolved_wool,ambiguous


def _hatcher_animal(defname, egg_map):
    # Actual fertilized egg DefNames use varying prefixes. Remove 'Egg' and
    # 'Fertilized' only, then require a single exact case-insensitive match.
    normalized=re.sub('(?i)fertilized$','',defname)
    normalized=re.sub('(?i)egg','',normalized)
    candidates=[name for name in egg_map if name.casefold()==normalized.casefold()]
    return candidates[0] if len(candidates)==1 else None


def _value_nodes(op,field):
    """All match/nomatch values, including vanilla text() and full li replacements."""
    found=[]
    for value in op.xpath('./match/value | ./nomatch/value'):
        if len(value)==0: found.append(value)
        found.extend(value.xpath('.//*[local-name()=$name]',name=field))
    return found


def refresh_tree(tree,tables):
    eggs,milk,wool,ambiguous=_index(tables)
    changes=[];warnings=[]; touched=set()
    if ambiguous: warnings.extend(f'Ambiguous {kind} {name}: matching Animals defs={names}' for kind,name,names in ambiguous)
    for op in tree.getroot().findall('.//Operation'):
        xpath=op.findtext('xpath') or ''
        hit=TAG.search(xpath)
        # A full CompProperties_* replacement may contain fields but no field name
        # in the XPath. Handle its contained <li> using the same source.
        kind=hit[1] if hit else None
        names=PATH_NAME.findall(xpath)
        if len(set(names))!=1: continue
        original=names[0]
        refs=sorted({f for val in op.xpath('./match/value | ./nomatch/value')
                     for f in KINDS if val.xpath('.//*[local-name()=$name]', name=f)})
        fields=[kind] if kind else refs
        if not fields: continue
        for field in fields:
            if field not in KINDS: continue
            name=original
            if KINDS[field]=='hatch':
                name=_hatcher_animal(original,eggs)
                if not name:
                    warnings.append(f'{original}: hatcher egg name not uniquely mapped to EggMilkWool; retained')
                    continue
            lookup={'egg':eggs,'hatch':eggs,'milk':milk,'wool':wool}[KINDS[field]]
            row=lookup.get(name)
            if row is None:
                warnings.append(f'{original}: {field} not covered in EggMilkWool; retained')
                continue
            try:
                if field=='eggLayIntervalDays': desired=_format(_value(row.get('D')))
                elif field=='eggCountRange':
                    desired=str(row.get('E','')).strip()
                    if not RANGE.fullmatch(desired): raise ValueError(f'Invalid eggCountRange: {desired!r}')
                elif field=='hatcherDaystoHatch': desired=_format(_value(row.get('C'),integer=True))
                elif field=='milkAmount':
                    # Chemfuel-producing and other exotic milkable comps are NOT
                    # ordinary milk; their values are intentionally hand-maintained.
                    if any(v.xpath('.//*[local-name()="milkDef" and text()!="Milk"]') for v in op.xpath('./match/value | ./nomatch/value')):
                        warnings.append(f'{original}: special non-milk production retained')
                        continue
                    interval_nodes=op.xpath('./match/value//*[local-name()="milkIntervalDays"] | ./nomatch/value//*[local-name()="milkIntervalDays"]')
                    if not interval_nodes:
                        # Separate milkAmount replacement uses one game day interval;
                        # determine from separate explicit XML operation if available.
                        matching=tree.xpath('//Operation[xpath[contains(text(), $prefix) and contains(text(), "milkIntervalDays")]]',prefix=f'defName="{original}"')
                        if not matching:
                            warnings.append(f'{original}: milk interval not explicit in same patch; retained')
                            continue
                        values=[v.text for x in matching for v in _value_nodes(x,'milkIntervalDays')]
                    else: values=[v.text for v in interval_nodes]
                    if not values or len(set(values))!=1: raise ValueError('Ambiguous milk interval')
                    desired=_format(_value(row.get('N'))*_value(values[0]))
                else:
                    matching=op.xpath('./match/value//*[local-name()="shearIntervalDays"] | ./nomatch/value//*[local-name()="shearIntervalDays"]')
                    values=[n.text for n in matching]
                    if not values or len(set(values))!=1:
                        warnings.append(f'{original}: missing explicit shear interval; retained')
                        continue
                    desired=_format(_value(row.get('T'))*_value(values[0]))
            except ValueError as exc:
                raise ValueError(f'{REPRODUCTION_SHEET} {name}, {field}: {exc}') from exc
            nodes=_value_nodes(op,field)
            if not nodes:
                warnings.append(f'{original}: {field} XPath matched but no replacement value; retained')
                continue
            for node in nodes:
                old=(node.text or '').strip()
                if field!='eggCountRange':
                    try:
                        if Decimal(old)==Decimal(desired): continue
                    except InvalidOperation: pass
                elif old==desired: continue
                node.text=desired
                changes.append({'defName':original,'field':field,'old':old,'new':desired})
            touched.add((original,field))
    return changes,warnings,sorted(f'{name}:{field}' for name,field in touched)


def _read_repro(source,sheet):
    from core.animalstats_source import is_multisheet_source
    if is_multisheet_source(source): return read_table(source,sheet)
    if str(source).lower().endswith('.tsv'):
        import pandas as pd
        return pd.read_csv(source,sep='\t',dtype=str,keep_default_na=False)
    raise ValueError(f'{sheet}: requires an XLSX/Google source or an explicit TSV')


def render_reproduction(source,root,egg_milk_source=None,animals_source=None,animals_frame=None,*,destinations):
    root=Path(root).resolve();mod=root/'Zoology'
    if not (mod/'LoadFolders.xml').is_file(): raise ValueError('Missing Zoology/LoadFolders.xml')
    # Banners are absent here, EggMilkWool has 3 disjoint grids on one worksheet.
    frame=_read_repro(egg_milk_source or source,REPRODUCTION_SHEET)
    # Reuse the already validated Animals table during Generate All. Re-reading
    # the large workbook can take disproportionately long and duplicates data.
    animals=animals_frame if animals_frame is not None else _read_repro(animals_source or source,'Animals')
    if not {'Common name','XML name'}.issubset(animals.columns):
        raise ValueError('Animals table must contain Common name and XML name columns')
    # Pandas assigns 'Unnamed: 9' and 'Unnamed: 14' to separator columns.
    egg=pd_egg_map(frame)
    outputs=[];report=[]
    for dest in destinations:
        original=mod/dest
        if not original.exists():
            report.append({'target':dest,'status':'missing','warnings':['File absent; no new patch synthesized']});continue
        tree=E.parse(str(original),E.XMLParser(remove_blank_text=False))
        changes,warnings,touched=refresh_tree(tree,{'EggMilk':egg,'Animals':animals})
        if changes:
            outputs.append((dest,E.tostring(tree,encoding='utf-8',xml_declaration=True,pretty_print=False)))
        report.append({'target':dest,'status':'changed' if changes else 'unchanged',
                       'source_sha256':digest(original),'changes':changes,'warnings':sorted(set(warnings)),
                       'matched':touched})
    return outputs,report


def pd_egg_map(frame):
    """Map the three independent EggMilkWool header grids onto stable Excel letters.

    read_excel uses the first row as names; source field references are the
    actual observed Excel column positions, *not* invented semantic columns.
    """
    cols=list(frame.columns)
    required={0:'Name',2:'hatcherDaystoHatch',3:'eggLayIntervalDays',4:'eggCountRange',
              5:'Def name',13:'Milk yield',
              17:'Wool type',19:'Wool yield per day'}
    if len(cols)<20 or any(str(cols[i]).strip()!=name for i,name in required.items()):
        raise ValueError(f'{REPRODUCTION_SHEET} layout changed; review column mapping before generation')
    from string import ascii_uppercase
    index=[ascii_uppercase[i] if i<26 else '' for i in range(len(cols))]
    return frame.rename(columns={cols[i]:index[i] for i in range(len(cols))})


