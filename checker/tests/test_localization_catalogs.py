"""Localization coverage and RimWorld DefInjected directory contracts."""
import re
import unittest
from collections import defaultdict
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
LANGUAGES = ROOT / "Zoology" / "Languages"
TRANSLATED = ("Russian", "Spanish", "SpanishLatin")
ALL_LANGUAGES = ("English",) + TRANSLATED


def read_catalog(language, folder):
    catalog = defaultdict(dict)
    for path in (LANGUAGES / language / folder).rglob("*.xml"):
        root = ET.parse(path).getroot()
        if root.tag != "LanguageData":
            raise AssertionError(f"Unexpected localization root: {path}")
        category = path.parent.name if folder == "DefInjected" else "Keyed"
        for node in root:
            if node.tag in catalog[category]:
                raise AssertionError(
                    f"Duplicate {language}/{category} key {node.tag} in {path}"
                )
            catalog[category][node.tag] = node.text or ""
    return dict(catalog)


class LocalizationCatalogTests(unittest.TestCase):
    def test_localization_xml_parses_and_uses_def_type_directories(self):
        allowed_types = {
            "DamageDef", "HediffDef", "JobDef", "RecipeDef",
            "SpecialThingFilterDef", "ThingDef", "ToolCapacityDef",
            "TrainableDef", "WorkGiverDef",
        }
        for language in ALL_LANGUAGES:
            root = LANGUAGES / language / "DefInjected"
            for file in root.rglob("*.xml"):
                with self.subTest(path=file):
                    self.assertIn(file.parent.name, allowed_types)
                    self.assertEqual(ET.parse(file).getroot().tag, "LanguageData")

    def test_keyed_translations_match_english_keys_and_arguments(self):
        original = read_catalog("English", "Keyed")["Keyed"]
        args = lambda value: sorted(re.findall(r"\{[^{}]+\}", value))
        for language in TRANSLATED:
            translated = read_catalog(language, "Keyed")["Keyed"]
            with self.subTest(language=language):
                self.assertEqual(set(translated), set(original))
            for key, english_text in original.items():
                with self.subTest(language=language, key=key):
                    self.assertEqual(args(translated[key]), args(english_text))

    def test_def_injected_coverage_matches_across_translations(self):
        reference = read_catalog("Russian", "DefInjected")
        for language in ("Spanish", "SpanishLatin"):
            translated = read_catalog(language, "DefInjected")
            with self.subTest(language=language):
                self.assertEqual(set(translated), set(reference))
            for def_type, original in reference.items():
                with self.subTest(language=language, def_type=def_type):
                    self.assertEqual(set(translated[def_type]), set(original))

    def test_no_abstract_bionic_or_unused_dog_meat_injections(self):
        for language in TRANSLATED:
            entries = read_catalog(language, "DefInjected")
            for def_type, catalog in entries.items():
                for key in catalog:
                    with self.subTest(language=language, key=key):
                        self.assertNotIn("_Animal_Base.", key)
                        self.assertFalse(key.startswith("Zoology_DogMeat"))


if __name__ == "__main__":
    unittest.main()
