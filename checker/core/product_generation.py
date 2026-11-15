"""Generate animal product records from the evaluated, independent workbook tables."""
from decimal import Decimal
from lxml import etree as E

from core.production_stats import _value, _format, _defname, RANGE
from core.amphibian_reproduction import _normalize_header, _find_section, EGG_FIELDS

DESTINATION = "Core/Patches/Core/Misc/AnimalProducts_Generated.xml"


def render_products(frame, animals, amphibians):
    headers = [_normalize_header(value) for value in frame.columns]
    egg = _find_section(headers, EGG_FIELDS + ('Nutrition (per ovum)', 'Hatchling XML name'))
    milk = _find_section(headers, ('Name', 'Food consumption (Nutrition/day)', 'Milk fraction', 'Milk yield'),
                         optional=('Milk XML name',))
    wool = _find_section(headers, ('Name', 'Food consumption (Nutrition/day)', 'Wool type', 'Wool fraction',
                                  'Wool yield per day', 'Wool yield per 10 days'),
                         optional=('Wool XML name',))
    nutrition_col = egg['Nutrition (per ovum)']
    hatchling_col = egg['Hatchling XML name']
    by_name = {}
    for _, row in animals.iterrows():
        name = str(row.get('Common name', '')).strip().casefold()
        slug = _defname(row.get('XML name', ''))
        if name and slug:
            by_name.setdefault(name, set()).add(slug)
    records = {}

    def add(name, **values):
        rec = records.setdefault(name, {})
        for key, value in values.items():
            if key in rec:
                raise ValueError(f'{name}: duplicate product field {key}')
            rec[key] = value

    def resolve(name, index, identity_col):
        # An explicit XML identity disambiguates equal common names.
        explicit = _defname(frame.iat[index, identity_col]) if identity_col is not None else None
        choices = by_name.get(name.casefold(), set())
        if explicit:
            if explicit not in choices:
                raise ValueError(f'{name}: invalid product identity {explicit}')
            return explicit
        if len(choices) != 1:
            raise ValueError(f'{name}: ambiguous product identity {sorted(choices)}')
        return next(iter(choices))

    for i in range(len(frame)):
        name = _defname(frame.iat[i, egg['Def name']])
        if name and name not in amphibians:
            hatchling = _defname(frame.iat[i, hatchling_col])
            if not hatchling:
                raise ValueError(f'{name}: missing hatchling identity')
            raw = str(frame.iat[i, egg['eggCountRange']]).strip()
            if not RANGE.fullmatch(raw):
                raw = _format(_value(raw, integer=True))
            bounds = list(map(int, raw.split('~')))
            if bounds[0] > bounds[-1] or bounds[-1] > 2147483647:
                raise ValueError(f'{name}: invalid egg count range')
            add(name, eggInterval=_format(_value(frame.iat[i, egg['eggLayIntervalDays']])),
                eggCount=raw, hatchDays=_format(_value(frame.iat[i, egg['hatcherDaystoHatch']], integer=True)),
                hatchling=hatchling, eggNutrition=_format(_value(frame.iat[i, nutrition_col])))
        for section, field, tag, identity in (
                (milk, 'Milk yield', 'milkPerDay', 'Milk XML name'),
                (wool, 'Wool yield per day', 'woolPerDay', 'Wool XML name')):
            common = str(frame.iat[i, section['Name']]).strip()
            if not common or common.casefold() in ('nan', 'none', 'no'):
                continue
            slug = resolve(common, i, section.get(identity))
            add(slug, **{tag: _format(_value(frame.iat[i, section[field]]))})
    if not records:
        raise ValueError('EggMilkWool: no product records')
    root = E.Element('Patch')
    op = E.SubElement(root, 'Operation', Class='ZoologyMod.PatchOperation_AnimalProducts')
    items = E.SubElement(op, 'products')
    for name, fields in sorted(records.items()):
        node = E.SubElement(items, 'li')
        E.SubElement(node, 'animal').text = name
        for field, value in fields.items():
            E.SubElement(node, field).text = value
    return E.tostring(root, encoding='utf-8', xml_declaration=True, pretty_print=True), records
