"""Dynamic guards for animal leatherDef values: no optional-material fallback defs."""
from lxml import etree as E
from core.pipeline import _guard_leather_assignments
from core.rimworld_patch_apply import PatchApplier


def _sample():
    return b'''<Patch>
      <Operation Class="PatchOperationReplace">
        <xpath>/Defs/ThingDef[defName="Example"]/race/leatherDef</xpath>
        <value><leatherDef>Leather_Armadillo</leatherDef></value>
      </Operation>
      <Operation Class="PatchOperationConditional">
        <xpath>/Defs/ThingDef[defName="Example"]/race/useLeatherFrom</xpath>
        <match Class="PatchOperationRemove">
          <xpath>/Defs/ThingDef[defName="Example"]/race/useLeatherFrom</xpath>
        </match>
      </Operation>
    </Patch>'''


def _animal(with_material):
    root = E.Element('Defs')
    animal = E.SubElement(root, 'ThingDef')
    E.SubElement(animal, 'defName').text = 'Example'
    race = E.SubElement(animal, 'race')
    E.SubElement(race, 'leatherDef').text = 'Leather_Plain'
    E.SubElement(race, 'useLeatherFrom').text = 'SomeAnimal'
    if with_material:
        material = E.SubElement(root, 'ThingDef')
        E.SubElement(material, 'defName').text = 'Leather_Armadillo'
    return root


def test_guard_reassigns_only_when_material_exists():
    guarded = E.fromstring(_guard_leather_assignments(_sample()))
    assert len(guarded) == 1
    assert guarded.xpath('string(./Operation/xpath)') == (
        '/Defs/ThingDef[defName="Leather_Armadillo"]')
    assert len(guarded.xpath('./Operation/match/operations/li')) == 2
    for exists in (False, True):
        xml = _animal(exists)
        applier = PatchApplier(active_mods=[], record_missing=True)
        assert applier.apply_patch_root(guarded, xml)
        assert not applier.missing_targets
        assert xml.xpath('string(ThingDef[defName="Example"]/race/leatherDef)') == (
            'Leather_Armadillo' if exists else 'Leather_Plain')
        assert bool(xml.xpath('ThingDef[defName="Example"]/race/useLeatherFrom')) == (not exists)


def test_guard_is_idempotent():
    original = _guard_leather_assignments(_sample())
    assert original == _guard_leather_assignments(original)
