"""The export must preserve exact sex endpoints and reject ungrounded stages."""
import unittest
import pandas as pd
from lxml import etree as E

from core.sex_adult_export import render_sex_adult_profiles


class TestSexAdultExport(unittest.TestCase):
    def test_export_preserves_tool_identity_and_armor(self):
        animals = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>", "baseBodySize": 1.,
            "ArmorRating_Sharp": 0., "ArmorRating_Blunt": 0.,
        }])
        animals_ce = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>",
            "ArmorRating_Sharp": 0.2, "ArmorRating_Blunt": 0.,
        }])
        sexes = []
        profiles = []
        for sex, mass, factor in (("Male", 12., 1.2), ("Female", 8., 0.8)):
            profiles.append({
                "XML name": "<li>TestAnimal</li>", "Sex": sex,
                "AdultMass_kg": mass, "BaselineMass_kg": 10.,
                "AdultBodySize": factor, "BodySizeFactor": factor,
                "HealthScaleFactor": factor, "SpeedFactor": 1.,
                "CE_SpeedFactor": 1., "HungerRateFactor": 1.,
                "FilthRateFactor": 1., "CE_ArmorDurabilityFactor": 1.,
                "LinearScale": 1., "CombatPowerFactor": factor,
                "MarketValueFactor": factor,
                "CE_ArmorSharp": 0.3,
                "CE_ArmorBlunt": 0., "ToolCount": 1,
                "ToolRecords9": "0\thead\tHeadAttackTool\t1.2\t1\t1.3\t1\t1\t1",
                "CalculationStatus": "READY", "GroupName": "Test group",
            })
            sexes.append({
                "XML name": "<li>TestAnimal</li>", "SexualDimorphism": True,
                "MaleMean_kg": 12., "FemaleMean_kg": 8.,
                "BaselineMass_kg": 10., "MaleMeanBodySize": 1.2,
                "FemaleMeanBodySize": .8, "MaleMeanHealthFactor": 1.2,
                "FemaleMeanHealthFactor": .8, "MaleMeanSpeedFactor": 1.,
                "FemaleMeanSpeedFactor": 1., "MaleMeanHungerRateFactor": 1.,
                "FemaleMeanHungerRateFactor": 1., "MaleMeanFilthRateFactor": 1.,
                "FemaleMeanFilthRateFactor": 1.,
                "MaleMeanCE_ArmorDurabilityFactor": 1.,
                "FemaleMeanCE_ArmorDurabilityFactor": 1.,
                "MaleMeanLinearScale": 1., "FemaleMeanLinearScale": 1.,
            })
        # The validator takes one SexDimorphism row per species.
        life = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>",
            "LifeStageDef": "AnimalAdult or EusocialInsectAdult",
            "BiologicalStageMass_kg": 10.,
            "ToolRecordsV2": "0\t0\thead\tHeadAttackTool\tBlunt\t1\t1\t1\t1\t1\t1",
        }])
        payload, info = render_sex_adult_profiles(
            pd.DataFrame(profiles), pd.DataFrame(sexes[:1]),
            animals, animals_ce, life)
        doc = E.fromstring(payload)
        self.assertEqual(info["endpoints"], 2)
        self.assertEqual(len(doc.xpath("//profiles/li")), 2)
        self.assertEqual(doc.xpath("string(//profiles/li[1]/tools/li/ceToolIndex)"), "0")
        self.assertEqual(doc.xpath("string(//profiles/li[1]/statFactors/li[stat='ArmorRating_Sharp']/factor)"), "1.5")
        # Without an explicit Base stats switch there is no sex price modifier.
        self.assertEqual(doc.xpath("count(//profiles/li/marketValueFactor)"), 0)
        # CE baseline blunt armor 0 remains exactly 0; no invented offset.
        self.assertEqual(doc.xpath("count(//statOffsets/li)"), 0)

    def test_opt_in_sex_market_price_is_exported(self):
        animals = pd.DataFrame([{"XML name": "<li>TestAnimal</li>",
                                 "baseBodySize": 1., "ArmorRating_Sharp": 0.,
                                 "ArmorRating_Blunt": 0.}])
        ce = pd.DataFrame([{"XML name": "<li>TestAnimal</li>",
                            "ArmorRating_Sharp": 0., "ArmorRating_Blunt": 0.}])
        sexes = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>", "Latin name": "Test animalicus",
            "SexualDimorphism": True, "BaselineMass_kg": 10.,
            "MaleMean_kg": 12., "FemaleMean_kg": 8.,
            "MaleMeanBodySize": 1.2, "FemaleMeanBodySize": .8,
            "MaleMeanHealthFactor": 1.2, "FemaleMeanHealthFactor": .8,
            "MaleMeanSpeedFactor": 1., "FemaleMeanSpeedFactor": 1.,
            "MaleMeanHungerRateFactor": 1., "FemaleMeanHungerRateFactor": 1.,
            "MaleMeanFilthRateFactor": 1., "FemaleMeanFilthRateFactor": 1.,
            "MaleMeanCE_ArmorDurabilityFactor": 1.,
            "FemaleMeanCE_ArmorDurabilityFactor": 1.,
            "MaleMeanLinearScale": 1., "FemaleMeanLinearScale": 1.,
        }])
        rows = []
        for gender, size, price in (("Male", 1.2, 1.2),
                                    ("Female", .8, .8)):
            rows.append({
                "XML name": "<li>TestAnimal</li>", "Sex": gender,
                "AdultMass_kg": 10 * size, "BaselineMass_kg": 10.,
                "AdultBodySize": size, "BodySizeFactor": size,
                "HealthScaleFactor": size, "SpeedFactor": 1.,
                "CE_SpeedFactor": 1., "HungerRateFactor": 1.,
                "FilthRateFactor": 1., "CE_ArmorDurabilityFactor": 1.,
                "LinearScale": 1., "CombatPowerFactor": size,
                "MarketValueFactor": price, "CE_ArmorSharp": 0.,
                "CE_ArmorBlunt": 0., "ToolCount": 1,
                "ToolRecords9": "0\thead\tHeadAttackTool\t1\t1\t1\t1\t1\t1",
                "CalculationStatus": "READY", "GroupName": "Test group",
            })
        life = pd.DataFrame([{
            "XML name": "<li>TestAnimal</li>",
            "LifeStageDef": "AnimalAdult or EusocialInsectAdult",
            "BiologicalStageMass_kg": 10.,
            "ToolRecordsV2": "0\t0\thead\tHeadAttackTool\tBlunt\t1\t1\t1\t1\t1\t1",
        }])
        for enabled in (False, True):
            baseline = pd.DataFrame([{
                "Species": "Test animalicus",
                "UseSexSpecificMarketValue": enabled,
            }])
            profiles = pd.DataFrame(rows)
            if not enabled:
                profiles["MarketValueFactor"] = 1.
            payload, _ = render_sex_adult_profiles(
                profiles, sexes, animals, ce, life, base_stats=baseline)
            doc = E.fromstring(payload)
            self.assertEqual(int(doc.xpath("count(//profiles/li/marketValueFactor)")),
                             2 if enabled else 0)
            self.assertEqual(int(doc.xpath("count(//profiles/li/useSexSpecificMarketValue)")),
                             2 if enabled else 0)
        # Reject a stale/non-neutral Sheet value when the canonical switch is OFF.
        with self.assertRaisesRegex(ValueError, "market value must be 1"):
            render_sex_adult_profiles(
                pd.DataFrame(rows), sexes, animals, ce, life,
                base_stats=pd.DataFrame([{"Species": "Test animalicus",
                                          "UseSexSpecificMarketValue": False}]))

    def test_no_large_adult_misapplication(self):
        animals = pd.DataFrame([{"XML name": "<li>TestAnimal</li>", "baseBodySize": 1.}])
        ce = pd.DataFrame([{"XML name": "<li>TestAnimal</li>"}])
        sex = pd.DataFrame([{"XML name": "<li>TestAnimal</li>", "SexualDimorphism": True,
                            "BaselineMass_kg": 10., "MaleMean_kg": 12., "FemaleMean_kg": 8.,
                            "MaleMeanBodySize": 1.2, "FemaleMeanBodySize": .8,
                            "MaleMeanHealthFactor": 1.2, "FemaleMeanHealthFactor": .8,
                            "MaleMeanSpeedFactor": 1., "FemaleMeanSpeedFactor": 1.,
                            "MaleMeanLinearScale": 1., "FemaleMeanLinearScale": 1.,
                            "MaleMeanHungerRateFactor": 1., "FemaleMeanHungerRateFactor": 1.,
                            "MaleMeanFilthRateFactor": 1., "FemaleMeanFilthRateFactor": 1.,
                            "MaleMeanCE_ArmorDurabilityFactor": 1.,
                            "FemaleMeanCE_ArmorDurabilityFactor": 1.}])
        profiles = pd.DataFrame([{"XML name": "<li>TestAnimal</li>", "Sex": gender,
                     "AdultMass_kg": mass, "BaselineMass_kg": 10.,
                     "AdultBodySize": factor, "BodySizeFactor": factor,
                     "HealthScaleFactor": factor, "SpeedFactor": 1.,
                     "CE_SpeedFactor": 1., "HungerRateFactor": 1.,
                     "FilthRateFactor": 1., "CE_ArmorDurabilityFactor": 1.,
                     "LinearScale": 1., "CombatPowerFactor": factor,
                     "MarketValueFactor": factor,
                     "CE_ArmorSharp": 0.,
                     "CE_ArmorBlunt": 0., "ToolCount": 1,
                     "ToolRecords9": "0\thead\tHeadAttackTool\t1\t1\t1\t1\t1\t1",
                     "CalculationStatus": "READY", "GroupName": "Test group"}
                    for gender, mass, factor in [("Male", 12., 1.2), ("Female", 8., .8)]])
        life = pd.DataFrame([{"XML name": "<li>TestAnimal</li>",
                    "LifeStageDef": "VAERoy_AnimalAdult",
                    "BiologicalStageMass_kg": 1500.,
                    "ToolRecordsV2": "0\t0\thead\tHeadAttackTool\tBlunt\t1\t1\t1\t1\t1\t1"}])
        with self.assertRaisesRegex(ValueError, "disagrees with SexAdultProfiles baseline"):
            render_sex_adult_profiles(profiles, sex, animals, ce, life)


if __name__ == "__main__":
    unittest.main()
