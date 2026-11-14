"""Refresh owned reproductive XML fields from AnimalStats EggMilkWool without rewriting hand-authored logic.

Data-flow is deliberately narrow: an existing, reviewed XML Patch remains at its
exact LoadFolders destination; only documented numeric/string values are changed.
Unsupported/ambiguous targets are preserved and reported, never guessed.
"""
from __future__ import annotations

from copy import deepcopy
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
from pathlib import Path
import json
import os
import re
import tempfile
from lxml import etree as E

from core.static_stats import read_table
from core.xml_routing import digest
from core.checker_config import ROOT
from core.amphibian_reproduction import (
    AMBI_DEFS, AMBI_PATCH, EGG_FIELDS, external_roe_rows, render_roe_defs, render_roe_patch,
    _find_section, _normalize_header,
)

# Spreadsheet tab name; the stable legacy manifest marker 'EggMilk' is NOT a sheet name.
REPRODUCTION_SHEET = 'EggMilkWool'
EGG_COUNTS_DESTINATION = 'Core/Patches/Core/Misc/EggCounts_Generated.xml'

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


def _integer_comp_quantity(value):
    """Serialize a calculated production quantity into a RimWorld Int32.

    The AnimalStats daily rate and the reproduction interval retain their
    fractional precision; only the discrete item count is rounded.
    Decimal ROUND_HALF_UP avoids Python's banker's rounding at half-items.
    """
    amount = value.to_integral_value(rounding=ROUND_HALF_UP)
    if amount > 2147483647:
        raise ValueError(f'Comp production amount exceeds Int32: {value!r}')
    return _format(amount)


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
        slug=_defname(r.get('Def name',''))
        if slug:
            if slug in by_egg: raise ValueError(f'Duplicate EggMilkWool Def name: {slug}')
            by_egg[slug]=dict(r)
        for namecol,target in [('Milk animal',milk),('Wool animal',wool)]:
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
    """Replacement values, including guarded branches, text() and full comps."""
    found=[]
    for value in op.xpath('./value | ./match/value | ./nomatch/value'):
        if len(value)==0: found.append(value)
        found.extend(value.xpath('.//*[local-name()=$name]',name=field))
    return found


def _production_operations(tree):
    """Expand direct replacement operations whose race predicate is a pure DefName union."""
    selector = './/Operation[xpath] | .//li[@Class and xpath]'
    for op in list(tree.getroot().xpath(selector)):
        xpath = op.findtext('xpath') or ''
        names = PATH_NAME.findall(xpath)
        if len(set(names)) <= 1:
            continue
        if op.get('Class') != 'PatchOperationReplace' or not TAG.search(xpath):
            continue
        predicate = re.search(r'(?:ThingDef)\[([^\]]+)\]', xpath)
        if not predicate:
            continue
        clauses = re.split(r'\s+or\s+', predicate[1].strip())
        if len(clauses) != len(names) or not all(PATH_NAME.fullmatch(clause.strip()) for clause in clauses):
            continue
        parent = op.getparent()
        ix = parent.index(op)
        for offset, clause in enumerate(clauses):
            clone = deepcopy(op)
            clone.find('xpath').text = xpath[:predicate.start(1)] + clause.strip() + xpath[predicate.end(1):]
            parent.insert(ix + offset, clone)
        parent.remove(op)
    return tree.getroot().xpath(selector)


def refresh_tree(tree,tables):
    eggs,milk,wool,ambiguous=_index(tables)
    changes=[];warnings=[]; touched=set()
    if ambiguous: warnings.extend(f'Ambiguous {kind} {name}: matching Animals defs={names}' for kind,name,names in ambiguous)
    for op in _production_operations(tree):
        xpath=op.findtext('xpath') or ''
        hit=TAG.search(xpath)
        # A full CompProperties_* replacement may contain fields but no field name
        # in the XPath. Handle its contained <li> using the same source.
        kind=hit[1] if hit else None
        names=PATH_NAME.findall(xpath)
        if len(set(names))!=1: continue
        original=names[0]
        refs=sorted({f for val in op.xpath('./value | ./match/value | ./nomatch/value')
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
                if field=='eggLayIntervalDays': desired=_format(_value(row.get('eggLayIntervalDays')))
                elif field=='eggCountRange':
                    desired=str(row.get('eggCountRange','')).strip()
                    if not RANGE.fullmatch(desired): raise ValueError(f'Invalid eggCountRange: {desired!r}')
                    bounds = [int(x) for x in desired.split('~')]
                    if bounds[-1] < bounds[0] or bounds[-1] > 2147483647:
                        raise ValueError(f'Invalid eggCountRange: {desired!r}')
                elif field=='hatcherDaystoHatch': desired=_format(_value(row.get('hatcherDaystoHatch'),integer=True))
                elif field=='milkAmount':
                    # Chemfuel-producing and other exotic milkable comps are NOT
                    # ordinary milk; their values are intentionally hand-maintained.
                    if any(v.xpath('.//*[local-name()="milkDef" and text()!="Milk"]') for v in op.xpath('./value | ./match/value | ./nomatch/value')):
                        warnings.append(f'{original}: special non-milk production retained')
                        continue
                    interval_nodes=op.xpath('./value//*[local-name()="milkIntervalDays"] | ./match/value//*[local-name()="milkIntervalDays"] | ./nomatch/value//*[local-name()="milkIntervalDays"]')
                    if not interval_nodes:
                        # Separate milkAmount replacement uses one game day interval;
                        # determine from separate explicit XML operation if available.
                        matching=[candidate for candidate in tree.getroot().xpath('.//Operation[xpath] | .//li[@Class and xpath]')
                                  if original in PATH_NAME.findall(candidate.findtext('xpath') or '')
                                  and 'milkIntervalDays' in (candidate.findtext('xpath') or '')]
                        if not matching:
                            warnings.append(f'{original}: milk interval not explicit in same patch; retained')
                            continue
                        values=[v.text for x in matching for v in _value_nodes(x,'milkIntervalDays')]
                    else: values=[v.text for v in interval_nodes]
                    if not values or len(set(values))!=1: raise ValueError('Ambiguous milk interval')
                    desired=_integer_comp_quantity(_value(row.get('Milk yield'))*_value(values[0]))
                else:
                    matching=op.xpath('./value//*[local-name()="shearIntervalDays"] | ./match/value//*[local-name()="shearIntervalDays"] | ./nomatch/value//*[local-name()="shearIntervalDays"]')
                    values=[n.text for n in matching]
                    if not values or len(set(values))!=1:
                        warnings.append(f'{original}: missing explicit shear interval; retained')
                        continue
                    desired=_integer_comp_quantity(_value(row.get('Wool yield per day'))*_value(values[0]))
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


def render_missing_egg_counts(egg, covered):
    root = E.Element('Patch')
    seen = set()
    generated = []
    for _, row in egg.iterrows():
        name = _defname(row.get('Def name', ''))
        if not name:
            continue
        if not re.fullmatch(r'[A-Za-z_][A-Za-z_0-9.]*', name) or name in seen:
            raise ValueError(f'Invalid or duplicate egg-layer DefName: {name!r}')
        seen.add(name)
        if name in covered:
            continue
        raw = str(row.get('eggCountRange', '')).strip()
        if not RANGE.fullmatch(raw):
            raw = _format(_value(raw, integer=True))
        bounds = [int(x) for x in raw.split('~')]
        if bounds[0] < 1 or bounds[-1] < bounds[0] or bounds[-1] > 2147483647:
            raise ValueError(f'{name}: invalid eggCountRange {raw!r}')
        xpath = f'/Defs/ThingDef[defName="{name}"]/comps/li[@Class="CompProperties_EggLayer" or @Class="RimWorld.CompProperties_EggLayer"]'
        outer = E.SubElement(root, 'Operation', Class='PatchOperationConditional')
        E.SubElement(outer, 'xpath').text = xpath
        inner = E.SubElement(outer, 'match', Class='PatchOperationConditional')
        E.SubElement(inner, 'xpath').text = xpath + '/eggCountRange'
        for tag, operation, target in (
            ('match', 'PatchOperationReplace', xpath + '/eggCountRange'),
            ('nomatch', 'PatchOperationAdd', xpath),
        ):
            branch = E.SubElement(inner, tag, Class=operation)
            E.SubElement(branch, 'xpath').text = target
            E.SubElement(E.SubElement(branch, 'value'), 'eggCountRange').text = raw
        generated.append(name)
    return E.tostring(root, encoding='utf-8', xml_declaration=True, pretty_print=True), generated


def render_reproduction(source,root,egg_milk_source=None,animals_source=None,animals_frame=None,*,destinations,include_missing_egg_counts=False):
    root=Path(root).resolve();mod=root/'Zoology'
    if not (mod/'LoadFolders.xml').is_file(): raise ValueError('Missing Zoology/LoadFolders.xml')
    # The egg/milk/wool tables and the amphibian subtable have independent
    # row indices. Their join is performed strictly by animal DefName.
    frame=_read_repro(egg_milk_source or source,REPRODUCTION_SHEET)
    # Reuse the already validated Animals table during Generate All. Re-reading
    # the large workbook can take disproportionately long and duplicates data.
    animals=animals_frame if animals_frame is not None else _read_repro(animals_source or source,'Animals')
    if not {'Common name','XML name'}.issubset(animals.columns):
        raise ValueError('Animals table must contain Common name and XML name columns')
    # Pandas assigns 'Unnamed: 9' and 'Unnamed: 14' to separator columns.
    egg=pd_egg_map(frame)
    amphibians=(external_roe_rows(frame)
                if {AMBI_DEFS, AMBI_PATCH} & set(destinations) else {})
    outputs=[];report=[]
    for dest in destinations:
        original=mod/dest
        if not original.exists():
            report.append({'target':dest,'status':'missing','warnings':['File absent; no new patch synthesized']});continue
        tree=E.parse(str(original),E.XMLParser(remove_blank_text=False))
        if dest in (AMBI_DEFS, AMBI_PATCH):
            generated=(render_roe_defs(tree, amphibians) if dest==AMBI_DEFS
                       else render_roe_patch(amphibians))
            payload=E.tostring(generated,encoding='utf-8',xml_declaration=True,pretty_print=True)
            changed=payload!=original.read_bytes()
            if changed:
                outputs.append((dest,payload))
            report.append({'target':dest,
                           'status':'changed' if changed else 'unchanged',
                           'source_sha256':digest(original),
                           'generated_species':sorted(amphibians),
                           'warnings':[], 'matched':[]})
            continue
        changes,warnings,touched=refresh_tree(tree,{'EggMilk':egg,'Animals':animals})
        if changes:
            outputs.append((dest,E.tostring(tree,encoding='utf-8',xml_declaration=True,pretty_print=False)))
        report.append({'target':dest,'status':'changed' if changes else 'unchanged',
                       'source_sha256':digest(original),'changes':changes,'warnings':sorted(set(warnings)),
                       'matched':touched})
    if include_missing_egg_counts:
        covered = set(amphibians)
        for item in report:
            for pair in item.get('matched', ()):
                name, field = pair.rsplit(':', 1)
                if field == 'eggCountRange':
                    covered.add(name)
        payload, generated = render_missing_egg_counts(egg, covered)
        outputs.append((EGG_COUNTS_DESTINATION, payload))
        report.append({'target': EGG_COUNTS_DESTINATION, 'status': 'generated',
                       'guarded_standard_layers': generated, 'warnings': [], 'matched': []})
    return outputs,report


def pd_egg_map(frame):
    """Resolve independent production tables by semantic section headers."""
    headers = [_normalize_header(value) for value in frame.columns]
    egg = _find_section(headers, EGG_FIELDS)
    milk = _find_section(headers, (
        'Name', 'Food consumption (Nutrition/day)', 'Milk fraction', 'Milk yield',
    ))
    wool = _find_section(headers, (
        'Name', 'Food consumption (Nutrition/day)', 'Wool type', 'Wool fraction',
        'Wool yield per day', 'Wool yield per 10 days',
    ))
    positions = {
        'Def name': egg['Def name'],
        'hatcherDaystoHatch': egg['hatcherDaystoHatch'],
        'eggLayIntervalDays': egg['eggLayIntervalDays'],
        'eggCountRange': egg['eggCountRange'],
        'Milk animal': milk['Name'],
        'Milk yield': milk['Milk yield'],
        'Wool animal': wool['Name'],
        'Wool yield per day': wool['Wool yield per day'],
    }
    return frame.iloc[:, list(positions.values())].set_axis(list(positions), axis=1)
