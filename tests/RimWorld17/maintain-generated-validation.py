"""Keep DefInjected entries consistent with the source-selected venom graph."""
from pathlib import Path
from lxml import etree as E

root = Path(__file__).resolve().parents[2]
definitions = {}
for path in (root / 'Zoology/Core/Defs').rglob('*.xml'):
    for node in E.parse(str(path)).getroot():
        name = node.findtext('defName')
        if name:
            definitions.setdefault(node.tag, set()).add(name)
for path in (root / 'Zoology/Languages/Russian/DefInjected').glob('*/AnimalStrategies.xml'):
    doc = E.parse(str(path))
    for entry in list(doc.getroot()):
        if entry.tag.startswith('ZVenom_') and entry.tag.split('.')[0] not in definitions.get(path.parent.name, set()):
            doc.getroot().remove(entry)
    path.write_bytes(E.tostring(doc, encoding='utf-8', xml_declaration=True, pretty_print=True))
