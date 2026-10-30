"""Combat Extended animal-bionic ToolCE patches from the Bionics sheet.

The CE-only patch has dedicated damage, penetration, and cooldown values; it
preserves existing tool labels, capacities, and sounds from bionic HediffDefs.
"""
from decimal import Decimal,InvalidOperation
from pathlib import Path
import re
from copy import deepcopy
from lxml import etree as E

VARIANT=re.compile(r'(BionicArm|BionicLeg|BionicJaw|ArchotechArm|ArchotechLeg)_(VerySmall|Small|Medium|Large|VeryLarge|Huge)')


def numeric(value,where,optional=False):
    text=str(value or '').strip()
    if optional and text.lower() in ('','no','none','nan'):return None
    try: num=Decimal(text.replace(',','.'))
    except InvalidOperation:raise ValueError(f'{where}: nonnumeric value {value!r}')
    if not num.is_finite() or num<0:raise ValueError(f'{where}: invalid numeric value {value!r}')
    return format(num.normalize(),'f')


def render_ce_bionics(table,hediffs):
    required={'Weapon','CE Damage','CE Blunt AP','CE Sharp AP','CE Attack interval'}
    if not required.issubset(table.columns):raise ValueError(f'Bionics CE columns missing: {sorted(required-set(table.columns))}')
    defs={e.findtext('defName'):e for e in E.parse(str(hediffs)).findall('.//HediffDef') if e.findtext('defName')}
    patch=E.Element('Patch');seen=set()
    for _,row in table.iterrows():
        key=str(row['Weapon']).strip()
        if not VARIANT.fullmatch(key):continue
        name=key.replace('_','_Animal_',1)
        if name in seen:raise ValueError(f'Duplicate Bionics row {key}')
        seen.add(name)
        if name not in defs:raise ValueError(f'Unknown Bionic HediffDef {name}')
        tools=defs[name].xpath('./comps/li[@Class="HediffCompProperties_VerbGiver"]/tools/li')
        if len(tools)!=1:raise ValueError(f'{name}: requires exactly one source tool')
        tool=deepcopy(tools[0]);tool.set('Class','CombatExtended.ToolCE')
        for k,field in [('power','CE Damage'),('cooldownTime','CE Attack interval')]:
            node=tool.find(k)
            if node is None:raise ValueError(f'{name}: missing {k}')
            val=numeric(row[field],f'{key}/{field}')
            if k=='cooldownTime' and Decimal(val)==0:raise ValueError(f'{key}: attack interval is zero')
            node.text=val
        for field,tag in [('CE Blunt AP','armorPenetrationBlunt'),('CE Sharp AP','armorPenetrationSharp')]:
            val=numeric(row[field],f'{key}/{field}',optional=True)
            existing=tool.find(tag)
            if existing is not None:tool.remove(existing)
            if val is not None:E.SubElement(tool,tag).text=val
        xpath=f'/Defs/HediffDef[defName="{name}"]/comps/li[@Class="HediffCompProperties_VerbGiver"]/tools/li'
        op=E.SubElement(patch,'Operation',Class='PatchOperationConditional')
        E.SubElement(op,'xpath').text=xpath
        match=E.SubElement(op,'match',Class='PatchOperationReplace')
        E.SubElement(match,'xpath').text=xpath
        E.SubElement(match,'value').append(tool)
    if len(seen)!=30:raise ValueError(f'Expected 30 bionic categories; found {len(seen)}')
    return E.tostring(patch,encoding='utf8',pretty_print=True,xml_declaration=True)
