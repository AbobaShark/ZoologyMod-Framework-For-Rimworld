"""Nest diet, food selection and parental defense integration."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "Zoology/Source/Behaviour"


class NestRaidingTests(unittest.TestCase):
    def setUp(self):
        self.hunt = (ROOT / "Predation/AnimalBurrowHunt.cs").read_text(encoding="utf-8")
        self.food = (ROOT / "Predation/PredatorFoodChoiceUtility.cs").read_text(encoding="utf-8")
        self.nest = (ROOT / "Childcare/AnimalNursery.cs").read_text(encoding="utf-8")
        self.defense = (ROOT / "Childcare/ChildcareDefenseUtility.cs").read_text(encoding="utf-8")
        self.owners = (ROOT / "Childcare/EggClutchDefenseGameComponent.cs").read_text(encoding="utf-8")

    def test_egg_diet_independent_of_predator_flag(self):
        self.assertIn("race.race.CanEverEat(egg)", self.hunt)
        self.assertIn("eggs.Add(layer.eggFertilizedDef)", self.hunt)
        self.assertIn("eggs.Add(layer.eggUnfertilizedDef)", self.hunt)
        self.assertIn("EggEatingRaces.GetOrAdd(pawn.def, EatsAnyEgg)", self.hunt)
        self.assertIn("AnimalBurrowHunt.UsesUnifiedShelterFood(pawn)", self.food)
        target = self.nest.split("internal bool CanRaidTarget(", 1)[1].split("internal Thing FindFoodFor(", 1)[0]
        self.assertIn("eater.WillEat(food)", target)
        self.assertIn("AnimalBurrowHunt.CanHuntNurseryYoung", target)
        self.assertNotIn("RaceProps.predator", target)

    def test_uses_same_guarded_food_evaluation(self):
        self.assertIn("ChildcareDefenseUtility.GetYoungPreyScoreDelta(predator, prey)", self.food)
        self.assertIn("FoodUtility.FoodOptimality(predator, source, foodDef", self.food)
        self.assertIn("TryAddUniqueProtector(childcareOwnerScratch, nursery.Mother)", self.defense)
        self.assertIn("TryAddUniqueProtector(childcareOwnerScratch, nursery.Father)", self.defense)
        self.assertIn("ChildcareUtility.GetYoungProtectionFather(young)", self.defense)
        self.assertIn("IsPotentialFoodOptimalityProtector(nursery.Father, eater, anchorMap)", self.owners)
        self.assertIn("ScoreShelterFood(eater, this, chick)", self.nest)
        self.assertIn("ScoreShelterFood(eater, this, egg) > bestScore", self.nest)

    def test_actual_opening_not_planning_triggers_defense(self):
        self.assertIn("nursery.NotifyAttacked(predator, food)", self.hunt)
        self.assertIn("AnimalBurrowHunt.BeginOpening(pawn, TargetA.Thing, TargetB.Thing)", self.hunt)
        self.assertIn("ChildcareDefenseUtility.TryOrderProtection(biologicalMother ?? mother, attacker, child)", self.nest)
        self.assertIn("ChildcareDefenseUtility.TryTriggerEggProtection(attacker, protectedThing)", self.nest)
        self.assertIn("AnimalDefense.CanClimbTree(predator, anchorTree)", self.nest)

    def test_exposed_eggs_are_eaten_and_chicks_hunted(self):
        self.assertIn("AnimalBurrowHunt.Open(pawn, TargetA.Thing, food)", self.hunt)
        self.assertIn("JobMaker.MakeJob(JobDefOf.Ingest, food)", self.hunt)
        self.assertIn("FoodUtility.WillIngestStackCountOf(pawn, foodDef", self.hunt)
        self.assertIn("JobMaker.MakeJob(JobDefOf.PredatorHunt, prey)", self.hunt)


if __name__ == "__main__":
    unittest.main()
