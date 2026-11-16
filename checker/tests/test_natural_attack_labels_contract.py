"""Natural attack labels are assigned only after matching the generated signature."""
from pathlib import Path
from xml.etree import ElementTree as ET
import re
import unittest

ROOT = Path(__file__).resolve().parents[2] / "Zoology"


class NaturalAttackLabelContracts(unittest.TestCase):
    def test_generated_labels_have_translations(self):
        generated = (ROOT / "Source/Behaviour/Shared/ZoologyLifeStageAttackProfiles_Generated.cs").read_text(encoding="utf-8")
        labels = set(re.findall(r'capacities = "[^"]+", label = "([^"]+)"', generated))
        keys = {"Zoology_NaturalAttack_" + label.lower().replace(" ", "_") for label in labels}
        self.assertTrue(keys)
        for lang in ("English", "Russian", "Spanish", "SpanishLatin"):
            root = ET.parse(ROOT / "Languages" / lang / "Keyed/NaturalAttackLabels.xml").getroot()
            self.assertTrue(keys <= {child.tag for child in root})

    def test_normalization_checks_all_tools_before_mutation(self):
        source = (ROOT / "Source/Behaviour/Shared/ZoologyNaturalAttackLabels.cs").read_text(encoding="utf-8")
        self.assertIn("profile.ceToolIndex : profile.vanillaToolIndex", source)
        self.assertIn("!Matches(race.tools[index], profile)", source)
        self.assertIn("Array.Exists(labels, label => label == null)", source)
        self.assertLess(source.index("Array.Exists(labels"), source.index("race.tools[i].label ="))
        self.assertNotIn("Purussaurus", source)
        startup = (ROOT / "Source/ZoologyModInt.cs").read_text(encoding="utf-8")
        self.assertLess(startup.index("ZoologyNaturalAttackLabels.Apply();"),
                        startup.index("ZoologyLifeStageProfileInstaller.ApplyOnce();"))


if __name__ == "__main__":
    unittest.main()
