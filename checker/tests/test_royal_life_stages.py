"""Five original Royal stages keep their identities without generic size factors."""
import unittest
import pandas as pd
from lxml import etree as E
from core.life_stages import ROYAL_STAGES
from core.life_stage_species_profiles import render_species_life_stage_profiles


class RoyalLifeStageTests(unittest.TestCase):
    def test_five_mass_profiles_are_serialized_individually(self):
        # Synthetic values test identity and numerical transport, not biology.
        adult = pd.DataFrame([{"XML name":"<li>VAERoy_Megachicken</li>",
                               "baseBodySize":.35,"baseHungerRate":1}])
        masses = (.25, 10, 25, 65, 100)
        factors = (.1, .6, 1, 1.4, 2)
        profiles = pd.DataFrame([{
            "XML name":"<li>VAERoy_Megachicken</li>",
            "LifeStageDef":stage[0],
            "BiologicalStageMass_kg":mass,
            "MassDerivedStageBodySize":.35*factor,
            "ExpectedBodySizeFactor":factor,
            "StageHealthScaleFactor":factor,
            "HungerRateFactor":factor,
            "FoodMaxFactor":mass/25,
            "SpeedFactor":factor,
            "CombatPowerFactor":factor,
            "MarketValueFactor":factor,
            "CE_ArmorDurabilityFactor":factor,
            "CalculationStatus":"READY",
            "ToolStatus":"READY",
            "ToolCount":0,
            "ToolRecordsV2":"",
        } for stage,mass,factor in zip(ROYAL_STAGES,masses,factors)])
        out, diagnostic = render_species_life_stage_profiles(profiles, profiles, adult, ce=False)
        root = E.fromstring(out)
        stages=root.xpath("./ZoologyLifeStageProfileCatalogDef/stages/li")
        self.assertEqual(len(stages),5)
        self.assertEqual(diagnostic["species_rows_ready"],5)
        self.assertEqual([x.findtext("sourceStageDefName") for x in stages],[s[0] for s in ROYAL_STAGES])
        self.assertEqual([float(x.findtext("bodySizeFactor")) for x in stages],list(factors))
        self.assertEqual(root.xpath(".//expectedBodySizeFactor"),[])

if __name__ == "__main__":
    unittest.main()
