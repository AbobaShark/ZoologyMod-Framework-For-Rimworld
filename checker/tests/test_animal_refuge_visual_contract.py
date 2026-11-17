"""Static contracts for natural animal refuges' damage and selection UI."""
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
DEFS = ROOT / "Zoology/Core/Defs/ThingDefs"
BEHAVIOUR = ROOT / "Zoology/Source/Behaviour"


class AnimalRefugeVisualContracts(unittest.TestCase):
    def test_shared_burrow_parent_disables_building_crack_overlays(self):
        xml = ET.parse(DEFS / "AnimalBurrow.xml").getroot()
        base = xml.find("./ThingDef[@Name='Zoology_SharedBurrowBase']")
        self.assertIsNotNone(base)
        self.assertEqual(base.get("Abstract"), "True")
        self.assertEqual(base.findtext("useHitPoints"), "true")
        self.assertEqual(base.findtext("drawDamagedOverlay"), "false")
        actual = [node for node in xml.findall("./ThingDef")
                  if node.get("Abstract") != "True"]
        self.assertEqual(len(actual), 6)
        for node in actual:
            with self.subTest(defName=node.findtext("defName")):
                self.assertEqual(
                    node.get("ParentName"), "Zoology_SharedBurrowBase")
                self.assertIsNone(node.find("drawDamagedOverlay"))

    @staticmethod
    def nest_definitions():
        xml = ET.parse(DEFS / "AnimalNursery.xml").getroot()
        nodes = xml.findall("./ThingDef")
        parents = {node.get("Name"): node for node in nodes if node.get("Name")}
        nests = [node for node in nodes if node.get("Abstract") != "True"]
        return nests, parents

    @staticmethod
    def inherited_element(node, path, parents):
        while node is not None:
            value = node.find(path)
            if value is not None:
                return value
            node = parents.get(node.get("ParentName"))
        return None

    def test_all_nest_defs_hide_cracks_but_keep_hit_points(self):
        nests, parents = self.nest_definitions()
        self.assertEqual({
            node.findtext("defName") for node in nests
        }, {"Zoology_AnimalTreeNest", "Zoology_AnimalGroundNest",
            "Zoology_AnimalGroundNestLarge"})
        for node in nests:
            with self.subTest(defName=node.findtext("defName")):
                for path, expected in (("useHitPoints", "true"),
                                       ("drawDamagedOverlay", "false")):
                    value = self.inherited_element(node, path, parents)
                    self.assertIsNotNone(value)
                    self.assertEqual(value.text, expected)

    def test_no_disabled_reverse_deconstruct_gizmo_on_nests(self):
        nests, parents = self.nest_definitions()
        for node in nests:
            with self.subTest(defName=node.findtext("defName")):
                claimable = self.inherited_element(node, "building/claimable", parents)
                self.assertIsNotNone(claimable)
                self.assertEqual(claimable.text, "false")
                self.assertIsNone(self.inherited_element(
                    node, "building/deconstructible", parents))
        nursery = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(
            encoding="utf-8")
        self.assertIn(
            "public override AcceptanceReport DeconstructibleBy(Faction fac) => false;",
            nursery)
        burrow = (BEHAVIOUR / "Flee/AnimalBurrow.cs").read_text(
            encoding="utf-8")
        self.assertIn(
            "public override AcceptanceReport DeconstructibleBy(Faction fac) => false;",
            burrow)

    def test_single_designated_ground_refuge_opening(self):
        opening = (BEHAVIOUR / "Flee/AnimalRefugeOpening.cs").read_text(encoding="utf-8")
        burrow = (BEHAVIOUR / "Flee/AnimalBurrow.cs").read_text(encoding="utf-8")
        nest = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(encoding="utf-8")
        self.assertIn("Designator_Open.CanDesignateThing", opening)
        self.assertIn("if (!AnimalRefugeOpening.IsRefuge(__0)) return true;", opening)
        self.assertIn("new Designation(thing, AnimalRefugeOpeningDefOf.Zoology_OpenRefuge)", opening)
        self.assertIn("SpawnedDesignationsOfDef(", opening)
        self.assertIn("AnimalBurrowHunt.Open(pawn, target)", opening)
        self.assertIn("designationManager.RemoveDesignation(designation)", opening)
        self.assertEqual(burrow.count("AnimalRefugeOpening.Command(this)"), 1)
        self.assertEqual(nest.count("AnimalRefugeOpening.Command(this)"), 1)
        self.assertIn("if (Spawned && !TreeNest)", nest)
        self.assertNotIn("ChooseOpener", burrow)
        tree_access = nest.split("internal bool PredatorCanOpen(", 1)[1].split("internal void OpenBy(", 1)[0]
        self.assertIn("predator.RaceProps.Animal", tree_access)
        self.assertIn("AnimalDefense.CanClimbTree(predator, anchorTree)", tree_access)

    def test_existing_burrows_are_reused_before_excavation(self):
        refuges = (BEHAVIOUR / "Flee/AnimalRefuges.cs").read_text(encoding="utf-8")
        winter = (BEHAVIOUR / "Shared/AnimalWinterDormancy.cs").read_text(encoding="utf-8")
        nursery = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(encoding="utf-8")
        search = refuges.split("internal Building_AnimalBurrow FindReusableBurrow", 1)[1].split("public bool TryPlan", 1)[0]
        self.assertIn("state.familiarBurrows", search)
        self.assertIn("foreach (Building_AnimalBurrow den in Burrows)", search)
        self.assertIn("den.CanHouseFamily(pawn) : den.Accepts(pawn)", search)
        self.assertIn("pawn.CanReserveAndReach", search)
        self.assertIn("!den.IsForbidden(pawn)", search)
        winter_plan = winter.split("class JobGiver_AnimalWinterDormancy", 1)[1]
        self.assertLess(winter_plan.index("FindReusableBurrow"), winter_plan.index("AnimalShelterDigPurpose.WinterDen"))
        family_plan = nursery.split("private static Job TryFamilyBurrow", 1)[1].split("protected override Job TryGiveJob", 1)[0]
        self.assertLess(family_plan.index("FindReusableBurrow"), family_plan.index("AnimalShelterDigPurpose.FamilyDen"))
        dig = refuges.split("class JobDriver_DigAnimalBurrow", 1)[1].split("class JobDriver_EnterAnimalBurrow", 1)[0]
        self.assertLess(dig.index("FindReusableBurrow"), dig.index("GenSpawn.Spawn(burrow"))
        self.assertIn("state.burrowReentryAfter > GenTicks.TicksGame", winter_plan)

    def test_dormancy_ends_with_season_disturbance_or_physological_need(self):
        burrow = (BEHAVIOUR / "Flee/AnimalBurrow.cs").read_text(encoding="utf-8")
        tick = burrow.split("protected override void Tick()", 1)[1]
        dormant = tick.split("if (AnimalWinterDormancy.IsDormant(pawn))", 1)[1].split(
            "// Treat reachable surface wounds", 1)[0]
        self.assertIn("!AnimalWinterDormancy.Winter(pawn, Map) || hungry", dormant)
        self.assertNotIn("dangers", dormant)
        self.assertIn("ExitFamily(pawn, null, hungry, false)", dormant)
        self.assertIn("if (!urgent) continue;", dormant)
        self.assertIn("AnimalWinterDormancy.Wake(pawn)", dormant)
        wake = burrow.split("internal void WakeDormantOccupants", 1)[1].split("public void NotifyAttacked", 1)[0]
        self.assertLess(wake.index("Drop(resident)"), wake.index("Rand.Chance"))
        self.assertIn("resident.RaceProps.manhunterOnDamageChance", wake)
        self.assertIn("MentalStateDefOf.Manhunter,", wake)
        self.assertLess(wake.index("TryStartMentalState("), wake.index("NotifyProvokedManhunter("))
        self.assertNotIn("ManhunterPermanent", wake)

    def test_tamed_shelter_settings_obey_parent_switches(self):
        constants = (ROOT / "Zoology/Source/ModConstants.cs").read_text(encoding="utf-8")
        settings = (ROOT / "Zoology/Source/ZoologyModSettings.cs").read_text(encoding="utf-8")
        nursery = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(encoding="utf-8")
        refuges = (BEHAVIOUR / "Flee/AnimalRefuges.cs").read_text(encoding="utf-8")
        winter = (BEHAVIOUR / "Shared/AnimalWinterDormancy.cs").read_text(encoding="utf-8")
        burrow = (BEHAVIOUR / "Flee/AnimalBurrow.cs").read_text(encoding="utf-8")
        defaults = {
            "AllowTamedAnimalWinterDormancy": "false",
            "AllowTamedAnimalNests": "true",
            "AllowTamedAnimalBurrows": "true",
            "AllowTamedAnimalFamilyDens": "true",
        }
        for name, default in defaults.items():
            with self.subTest(setting=name):
                self.assertIn(f"Default{name} = {default};", constants)
                self.assertIn(f"Scribe_Values.Look(ref {name},", settings)
                self.assertIn(f"{name} = ModConstants.Default{name};", settings)
        self.assertIn("TamedWinterDormancy => WinterDormancy", constants)
        self.assertIn("TamedNestBuilding => AdvancedReproduction", constants)
        self.assertIn("TamedBurrowDigging => AdvancedThreatAvoidance", constants)
        self.assertIn("TamedFamilyDenBuilding => FamilyDens", constants)
        self.assertIn("FamilyDens => AdvancedReproduction && AdvancedThreatAvoidance", constants)
        self.assertIn("pawn?.Faction == Faction.OfPlayer", constants)
        self.assertIn("!ModConstants.TamedNestBuilding", nursery)
        self.assertIn("!ModConstants.TamedFamilyDenBuilding", nursery)
        self.assertIn("!ModConstants.TamedBurrowDigging", refuges)
        self.assertIn("CanDigFor(pawn, job)", refuges)
        self.assertIn("ModConstants.TamedWinterDormancy", winter)
        self.assertIn("!AnimalWinterDormancy.FactionAllowed(pawn)", burrow)

    def test_nests_are_reused_before_building(self):
        nest = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(encoding="utf-8")
        self.assertIn("foreach (Building_AnimalNursery nest in index.Nurseries)", nest)
        self.assertIn("HasProtectedContents || ActiveClaim(mother) || ActiveClaim(father)", nest)
        self.assertIn("eggs.Accepts(eggLayer.NextEggType())", nest)
        lay = nest.split("internal static class Patch_LayEgg_UseAnimalNursery", 1)[1]
        self.assertLess(lay.index("FindReusableNest"), lay.index("FindLocation"))
        build = nest.split("public sealed class JobDriver_BuildAnimalNursery", 1)[1].split(
            "public sealed class JobDriver_DeliverNurseryBaby", 1)[0]
        self.assertLess(build.index("FindReusableNest"), build.index("ThingMaker.MakeThing(nurseryDef)"))

    def test_normal_decay_and_destruction_preserved(self):
        burrow = (BEHAVIOUR / "Flee/AnimalBurrow.cs").read_text(
            encoding="utf-8")
        nest = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(
            encoding="utf-8")
        self.assertIn("else HitPoints--;", burrow)
        self.assertIn("else Destroy();", nest)
        self.assertIn("EjectEggContents();", nest)
        self.assertIn("EjectContents();", burrow)

    def test_erosion_only_when_empty_and_full_health_lifetimes(self):
        burrow = (BEHAVIOUR / "Flee/AnimalBurrow.cs").read_text(encoding="utf-8")
        nest = (BEHAVIOUR / "Childcare/AnimalNursery.cs").read_text(encoding="utf-8")
        defs = ET.parse(DEFS / "AnimalBurrow.xml").getroot()
        interval = int(defs.findtext("ZoologyMod.AnimalRefugeDef/unusedDecayInterval"))
        hp = int(defs.findtext("ThingDef/statBases/MaxHitPoints"))
        self.assertEqual(interval * hp, 10 * 60000)
        self.assertIn("if (HasAnyContents)", burrow)
        self.assertIn("else if (GenTicks.TicksGame % Settings.unusedDecayInterval < 250)", burrow)
        nest_hp = int(ET.parse(DEFS / "AnimalNursery.xml").getroot().findtext("ThingDef/statBases/MaxHitPoints"))
        self.assertEqual(nest_hp * 2000, 2 * 60000)
        self.assertIn("if (!this.IsHashIntervalTick(2000) || HasProtectedContents) return;", nest)

    def test_forage_validator_is_bounded_before_vanilla_fertility_lookup(self):
        source = (BEHAVIOUR / "Predation/PredatorFoodChoiceUtility.cs").read_text(encoding="utf-8")
        driver = (BEHAVIOUR / "Feeding/JobDriver_WildAnimalForage.cs").read_text(encoding="utf-8")
        self.assertIn("cell => cell.InBounds(map) && (validator == null || validator(cell))", source)
        self.assertIn("Patch_ForageSiteBounds.Installed", source)
        self.assertLess(driver.index("!job.targetA.Cell.InBounds(pawn.Map)"),
                        driver.index("!job.targetA.Cell.GetTerrain(pawn.Map)"))


if __name__ == "__main__":
    unittest.main()
