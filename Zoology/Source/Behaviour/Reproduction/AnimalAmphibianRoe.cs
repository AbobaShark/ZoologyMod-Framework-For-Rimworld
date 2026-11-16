using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Each clutch is one water-bound item. Its actual ovum count is retained
    // independently of RimWorld's stackCount, avoiding thousands of Pawns.
    public enum AquaticFertilizationMode
    {
        External,
        Internal
    }

    public sealed class CompProperties_AmphibianRoe : CompProperties
    {
        public ThingDef parentRace;
        public ThingDef tadpoleDef;
        public ThingDef fertilizedRoeDef;
        public float embryoSurvival;
        // Absolute biological mass/energy of one complete water-bound ovum.
        public int defaultOvaCount;
        public float wetMassPerOvumKg;
        public float grossEnergyPerOvumJ;
        public float joulesPerNutrition;
        public float daysToHatch;
        public bool fertilized;
        public AquaticFertilizationMode fertilizationMode = AquaticFertilizationMode.External;
        public bool protectFromConspecifics = true;

        public CompProperties_AmphibianRoe()
        {
            compClass = typeof(CompAmphibianRoe);
        }
    }

    public sealed class CompAmphibianRoe : ThingComp
    {
        public int eggCount;
        public Pawn mother;
        public Pawn father;
        public Faction faction;
        private float progress;
        private int hatchlingTarget = -1;

        public CompProperties_AmphibianRoe Props => (CompProperties_AmphibianRoe)props;

        // With one Thing per clutch, all count-dependent values are queried
        // from the real ovum count. The hydrated jelly contributes physical
        // mass but no chemical energy.
        internal int OvaRemaining => Props.fertilized
            ? Math.Max(0, ViableOva) : Math.Max(0, eggCount);

        internal float CohortMassKg => OvaRemaining * Props.wetMassPerOvumKg;
        internal float CohortNutrition => Props.joulesPerNutrition > 0f
            ? OvaRemaining * Props.grossEnergyPerOvumJ / Props.joulesPerNutrition
            : 0f;

        // External fertilization is performed on a previously laid clutch.
        // The fertilized target is defined in XML, never inferred from race names.
        internal bool CanBeFertilizedBy(Pawn male)
        {
            if (Props.fertilizationMode != AquaticFertilizationMode.External
                || Props.fertilized || Props.fertilizedRoeDef == null
                || parent?.Spawned != true || male?.Spawned != true
                || male.Map != parent.Map || male.Dead || male.Downed
                || male.gender != Gender.Male || male.Sterile()
                || !AnimalSeasonalReproduction.AllowsMating(male)
                || male.def != Props.parentRace
                || !parent.Position.GetTerrain(parent.Map).IsWater
                || parent.IsForbidden(male))
                return false;
            return mother == null || (!male.HostileTo(mother)
                && !WildAnimalEcosystemUtility.ShouldBlockWildWildMating(male, mother));
        }

        internal bool Fertilize(Pawn male)
        {
            if (!ModConstants.AdvancedReproduction || !CanBeFertilizedBy(male)) return false;
            Thing result = ThingMaker.MakeThing(Props.fertilizedRoeDef);
            CompAmphibianRoe fertilized = result.TryGetComp<CompAmphibianRoe>();
            if (fertilized == null || !fertilized.Props.fertilized) return false;
            fertilized.eggCount = Math.Max(1, eggCount);
            fertilized.mother = mother;
            fertilized.father = male;
            fertilized.faction = faction ?? male.Faction;
            result.HitPoints = Math.Min(result.MaxHitPoints, parent.HitPoints);
            Map map = parent.Map;
            IntVec3 position = parent.Position;
            // The new entity inherits the entire biological clutch; the old
            // unfertilized Thing is destroyed only after successful placement.
            Thing spawned = GenSpawn.Spawn(result, position, map);
            if (spawned != result) return false;
            parent.Destroy(DestroyMode.Vanish);
            return true;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // New hand-spawned or saved empty clutches still carry a genuine
            // AnimalStats-derived mean ovum count. Normal laying replaces it.
            if (eggCount <= 0)
                eggCount = Math.Max(1, Props.defaultOvaCount);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref eggCount, "zoologyRoeEggCount");
            Scribe_Values.Look(ref progress, "zoologyRoeProgress");
            Scribe_Values.Look(ref hatchlingTarget, "zoologyRoeHatchlingTarget", -1);
            Scribe_References.Look(ref mother, "zoologyRoeMother");
            Scribe_References.Look(ref father, "zoologyRoeFather");
            Scribe_References.Look(ref faction, "zoologyRoeFaction");
        }

        public override void CompTick()
        {
            if (!Props.fertilized || !parent.Spawned || !parent.IsHashIntervalTick(250)
                || !ModConstants.AdvancedReproduction || !parent.Position.GetTerrain(parent.Map).IsWater)
                return;
            EnsureHatchlingTarget();
            float days = AnimalSeasonalReproduction.IncubationDays(Props.daysToHatch,
                Props.parentRace, faction != null);
            progress = Math.Min(1f, progress + 250f / (Math.Max(0.01f, days) * GenDate.TicksPerDay));
            if (progress >= 1f) HatchIntoTadpole();
        }

        private void EnsureHatchlingTarget()
        {
            if (hatchlingTarget < 0)
                hatchlingTarget = AmphibianSurvival.SurvivorCount(eggCount, Props.embryoSurvival);
        }

        private int ViableOva => hatchlingTarget < 0 ? eggCount
            : Mathf.Max(hatchlingTarget, eggCount - Mathf.RoundToInt(
                (eggCount - hatchlingTarget) * Mathf.Clamp01(progress)));

        private void HatchIntoTadpole()
        {
            if (!parent.Spawned || !parent.Position.GetTerrain(parent.Map).IsWater
                || Props.tadpoleDef == null) return;
            Map map = parent.Map;
            IntVec3 cell = parent.Position;
            EnsureHatchlingTarget();
            if (hatchlingTarget == 0)
            {
                parent.Destroy(DestroyMode.Vanish);
                return;
            }
            Thing hatchling = ThingMaker.MakeThing(Props.tadpoleDef);
            if (hatchling.TryGetComp<CompTadpole>() is not CompTadpole tadpole)
                return;
            tadpole.InitializeCohort(Math.Max(1, eggCount), hatchlingTarget,
                mother, father, faction);
            if (GenSpawn.Spawn(hatchling, cell, map) != hatchling)
                return;
            parent.Destroy(DestroyMode.Vanish);
        }

        public override string CompInspectStringExtra() =>
            "Zoology_AquaticOva".Translate(eggCount) +
            (Props.fertilized
                ? "\n" + "Zoology_AquaticViableOva".Translate(ViableOva)
                    + "\n" + "Zoology_AquaticIncubation".Translate(progress.ToStringPercent())
                : "\n" + "Zoology_AquaticUnfertilized".Translate());

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (Prefs.DevMode && DebugSettings.ShowDevGizmos && Props.fertilized)
                yield return new Command_Action
                {
                    defaultLabel = "Zoology_DevHatchRoe".Translate(),
                    action = () => { progress = 1f; HatchIntoTadpole(); }
                };
        }
    }

    internal static class AmphibianSurvival
    {
        internal static int SurvivorCount(int initialCount, float survival)
        {
            if (initialCount <= 0 || survival <= 0f || float.IsNaN(survival))
                return 0;
            if (survival >= 1f)
                return initialCount;
            return (int)Math.Round(initialCount * (double)survival);
        }
    }

    public enum AquaticLarvalForm
    {
        Tadpole,
        Nymph
    }

    public sealed class CompProperties_Tadpole : CompProperties
    {
        public PawnKindDef pawnKind;
        public float tadpoleSurvival;
        public ThingDef sourceRoeDef;
        public float hatchMassKg;
        public float metamorphMassKg;
        public float grossEnergyMJPerKg;
        public float joulesPerNutrition;
        public float daysToMetamorphosis;
        public int biologicalMetamorphsPerPawn = 100;
        public bool emergeAsAdult;
        public AquaticLarvalForm larvalForm = AquaticLarvalForm.Tadpole;
        public bool protectFromConspecifics = true;

        public CompProperties_Tadpole()
        {
            compClass = typeof(CompTadpole);
        }
    }

    // One swimming Thing represents a group of larvae. Neither eggs nor
    // tadpoles instantiate hundreds of individual RimWorld Pawns.
    public sealed class CompTadpole : ThingComp
    {
        public int eggCount;
        public Pawn mother;
        public Pawn father;
        public Faction faction;
        private float progress;
        private int initialTadpoles = -1;
        private int frogletTarget = -1;
        private int frogletsReleased;
        private int recruitmentTarget = -1;
        private int biologicalMetamorphsReleased = -1;
        private int BiologicalMetamorphsPerPawn => Math.Max(1, Props.biologicalMetamorphsPerPawn);

        private CompProperties_Tadpole Props => (CompProperties_Tadpole)props;
        internal ThingDef ParentRace => Props.pawnKind?.race;
        internal bool ProtectFromConspecifics => Props.protectFromConspecifics;

        // Size (the cube root of mass) increases linearly; mass therefore
        // follows geometrically similar somatic growth between the measured
        // early larva and the post-resorption newborn LifeStageProfiles mass.
        // Exactly one group Thing holds the surviving cohort throughout.
        internal float IndividualMassKg
        {
            get
            {
                float initial = Mathf.Max(0f, Props.hatchMassKg);
                float terminal = Mathf.Max(0f, Props.metamorphMassKg);
                float lengthStart = Mathf.Pow(initial, 1f / 3f);
                float lengthEnd = Mathf.Pow(terminal, 1f / 3f);
                float length = Mathf.Lerp(lengthStart, lengthEnd, Mathf.Clamp01(progress));
                return length * length * length;
            }
        }

        internal float CohortMassKg => LivingTadpoles * IndividualMassKg;
        internal float CohortNutrition => Props.joulesPerNutrition > 0f
            ? CohortMassKg * Props.grossEnergyMJPerKg * 1000000f
                / Props.joulesPerNutrition
            : 0f;

        // A cohort's terminal number follows its actual hatchling count.
        internal void InitializeCohort(int ova, int larvae, Pawn dam, Pawn sire, Faction originalFaction)
        {
            eggCount = Math.Max(1, ova);
            mother = dam;
            father = sire;
            faction = originalFaction;
            initialTadpoles = Mathf.Clamp(larvae, 0, eggCount);
            frogletTarget = AmphibianSurvival.SurvivorCount(initialTadpoles, Props.tadpoleSurvival);
        }

        private void EnsureCohort()
        {
            if (initialTadpoles < 0)
            {
                CompProperties_AmphibianRoe roe = Props.sourceRoeDef?.GetCompProperties<CompProperties_AmphibianRoe>();
                int ova = eggCount > 0 ? eggCount : Math.Max(0, roe?.defaultOvaCount ?? 0);
                int larvae = AmphibianSurvival.SurvivorCount(ova, roe?.embryoSurvival ?? 0f);
                InitializeCohort(ova, larvae, mother, father, faction);
            }
            else if (frogletTarget < 0)
                frogletTarget = AmphibianSurvival.SurvivorCount(initialTadpoles, Props.tadpoleSurvival);
        }

        private int LivingTadpoles
        {
            get
            {
                if (initialTadpoles <= 0) return 0;
                int surviving = Mathf.Max(frogletTarget,
                    initialTadpoles - Mathf.RoundToInt(
                        (initialTadpoles - frogletTarget) * Mathf.Clamp01(progress)));
                int releasedBiological = biologicalMetamorphsReleased < 0 ? frogletsReleased : biologicalMetamorphsReleased;
                return Mathf.Max(0, surviving - releasedBiological);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref eggCount, "zoologyTadpoleEggCount");
            Scribe_Values.Look(ref progress, "zoologyTadpoleProgress");
            Scribe_Values.Look(ref initialTadpoles, "zoologyTadpoleInitialCohort", -1);
            Scribe_Values.Look(ref frogletTarget, "zoologyTadpoleFrogletTarget", -1);
            Scribe_Values.Look(ref frogletsReleased, "zoologyTadpoleFrogletsReleased");
            Scribe_Values.Look(ref recruitmentTarget, "zoologyTadpoleRecruitmentTarget", -1);
            Scribe_Values.Look(ref biologicalMetamorphsReleased, "zoologyTadpoleBiologicalReleased", -1);
            Scribe_References.Look(ref mother, "zoologyTadpoleMother");
            Scribe_References.Look(ref father, "zoologyTadpoleFather");
            Scribe_References.Look(ref faction, "zoologyTadpoleFaction");
        }

        public override void CompTick()
        {
            if (!parent.Spawned) return;
            bool grow = parent.IsHashIntervalTick(250);
            bool swim = parent.IsHashIntervalTick(1500);
            if ((!grow && !swim) || !ModConstants.AdvancedReproduction
                || !parent.Position.GetTerrain(parent.Map).IsWater)
                return;
            if (grow)
            {
                EnsureCohort();
                if (initialTadpoles == 0)
                {
                    parent.Destroy(DestroyMode.Vanish);
                    return;
                }
                if (progress < 1f)
                    progress = Math.Min(1f, progress + 250f /
                        (Math.Max(0.01f, Props.daysToMetamorphosis) * GenDate.TicksPerDay));
                if (progress >= 1f)
                {
                    ReleaseOneAnimal(swim);
                    return;
                }
            }
            if (parent.Spawned && swim) Swim();
        }

        private void Swim()
        {
            Map map = parent.Map;
            IntVec3 origin = parent.Position;
            bool stayNearShore = Props.emergeAsAdult && progress >= 1f
                && AquaticEggLayerUtility.TryFindShore(map, origin, out _);
            int start = Rand.Range(0, GenAdj.AdjacentCells.Length);
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 destination = origin + GenAdj.AdjacentCells[(start + i) % GenAdj.AdjacentCells.Length];
                if (!destination.InBounds(map) || !destination.GetTerrain(map).IsWater
                    || destination.GetEdifice(map) != null
                    || (stayNearShore && !AquaticEggLayerUtility.TryFindShore(map, destination, out _)))
                    continue;
                // Keep this Thing spawned: DeSpawn clears its selection and
                // re-registers the whole cohort on every swimming step.
                // Position updates the map's spatial index without either effect.
                parent.Position = destination;
                return;
            }
        }

        private bool WildSpeciesAtCapacity(Map map)
        {
            ZoologyModSettings settings = ModConstants.Settings;
            if (faction != null || !WildAnimalEcosystemUtility.IsSpeciesLimitActive(settings)
                || Props.pawnKind?.race == null)
                return false;
            List<Thing> candidates = map.listerThings.ThingsOfDef(Props.pawnKind.race);
            int count = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] is Pawn candidate && candidate.Spawned && !candidate.Dead
                    && WildAnimalEcosystemUtility.IsWildAnimal(candidate)
                    && ++count >= settings.WildAnimalSpeciesLimit)
                    return true;
            }
            return false;
        }

        private void ReleaseOneAnimal(bool swim = false)
        {
            if (Props.pawnKind == null) return;
            if (recruitmentTarget < 0)
            {
                if (biologicalMetamorphsReleased < 0) biologicalMetamorphsReleased = frogletsReleased;
                int remaining = Math.Max(0, frogletTarget - biologicalMetamorphsReleased);
                recruitmentTarget = frogletsReleased + GenMath.RoundRandom(remaining / (float)BiologicalMetamorphsPerPawn);
            }
            if (frogletsReleased >= recruitmentTarget || WildSpeciesAtCapacity(parent.Map))
            {
                parent.Destroy(DestroyMode.Vanish);
                return;
            }
            IntVec3? spawnCell = null;
            PawnGenerationRequest request;
            if (Props.emergeAsAdult)
            {
                // Imago recruitment takes place on land at the adult stage
                // boundary. The cohort accounts for pre-emergence growth.
                var stages = Props.pawnKind.race?.race?.lifeStageAges;
                if (stages == null || stages.Count == 0) return;
                if (!AquaticEggLayerUtility.TryFindShore(parent.Map, parent.Position, out IntVec3 shore))
                {
                    if (swim)
                    {
                        if (AquaticEggLayerUtility.TryStepTowardShore(parent.Map, parent.Position, out IntVec3 step))
                            parent.Position = step;
                        else
                            Swim();
                    }
                    return;
                }
                spawnCell = shore;
                float ticksPerYear = 60f * GenDate.TicksPerDay;
                float age = (Mathf.Ceil(stages[stages.Count - 1].minAge * ticksPerYear) + 1f)
                    / ticksPerYear;
                request = new PawnGenerationRequest(Props.pawnKind, faction,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowPregnant: false,
                    fixedBiologicalAge: age, developmentalStages: DevelopmentalStage.Adult);
            }
            else
                request = new PawnGenerationRequest(Props.pawnKind, faction,
                    developmentalStages: DevelopmentalStage.Newborn);
            Pawn frog = PawnGenerator.GeneratePawn(request);
            if (frog == null) return;
            if (!PawnUtility.TrySpawnHatchedOrBornPawn(frog, parent, spawnCell))
            {
                Find.WorldPawns.PassToWorld(frog, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                return;
            }
            if (mother != null && frog.RaceProps.IsFlesh)
                frog.relations.AddDirectRelation(PawnRelationDefOf.Parent, mother);
            if (father != null && father != mother && frog.RaceProps.IsFlesh)
                frog.relations.AddDirectRelation(PawnRelationDefOf.Parent, father);
            frogletsReleased++;
            biologicalMetamorphsReleased = Math.Min(frogletTarget, biologicalMetamorphsReleased + BiologicalMetamorphsPerPawn);
            if (frogletsReleased >= recruitmentTarget)
                parent.Destroy(DestroyMode.Vanish);
        }

        public override string CompInspectStringExtra() =>
            (Props.larvalForm == AquaticLarvalForm.Nymph ? "Zoology_AquaticNymphDevelopment" : "Zoology_AquaticTadpoleDevelopment")
                .Translate(progress.ToStringPercent()) +
            "\n" + "Zoology_AquaticSourceOva".Translate(eggCount) +
            "\n" + (Props.larvalForm == AquaticLarvalForm.Nymph ? "Zoology_AquaticLivingNymphs" : "Zoology_AquaticLivingTadpoles")
                .Translate(LivingTadpoles) +
            "\n" + (Props.larvalForm == AquaticLarvalForm.Nymph ? "Zoology_AquaticEmerged" : "Zoology_AquaticMetamorphosed")
                .Translate(frogletsReleased);

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (Prefs.DevMode && DebugSettings.ShowDevGizmos)
                yield return new Command_Action
                {
                    defaultLabel = (Props.larvalForm == AquaticLarvalForm.Nymph
                        ? "Zoology_DevCompleteEmergence" : "Zoology_DevCompleteMetamorphosis").Translate(),
                    action = () =>
                    {
                        if (!parent.Spawned || !parent.Position.GetTerrain(parent.Map).IsWater)
                            return;
                        EnsureCohort();
                        progress = 1f;
                        ReleaseOneAnimal();
                    }
                };
        }
    }

    [HarmonyPatch(typeof(CompEggLayer), nameof(CompEggLayer.ProduceEgg))]
    internal static class Patch_AmphibianRoeMass
    {
        private static readonly FieldInfo FertilizedByField =
            AccessTools.Field(typeof(CompEggLayer), "fertilizedBy");

        private static void Prefix(CompEggLayer __instance, out Pawn __state)
        {
            __state = null;
            if (!ModConstants.AdvancedReproduction
                || AquaticEggLayerUtility.InstalledProfile(__instance.parent?.def) == null)
                return;
            __state = FertilizedByField?.GetValue(__instance) as Pawn;
        }

        private static void Postfix(CompEggLayer __instance, ref Thing __result, Pawn __state)
        {
            if (__result == null) return;
            int originalEggCount = __result.stackCount;
            ThingDef aquaticDef = AquaticEggLayerUtility.AquaticOutput(__instance, __result.def);
            if (aquaticDef != null)
            {
                Thing clutch = ThingMaker.MakeThing(aquaticDef);
                if (clutch.TryGetComp<CompAmphibianRoe>() != null)
                {
                    __result.Destroy(DestroyMode.Vanish);
                    __result = clutch;
                }
                else
                    clutch.Destroy(DestroyMode.Vanish);
            }
            // A cohort is represented by one Thing with its biological count.
            if (__result.TryGetComp<CompAmphibianRoe>() is not CompAmphibianRoe roe)
                return;
            // External fertilization must take place after laying. Internally
            // fertilized species retain the result of native mating.
            if (ModConstants.AdvancedReproduction
                && roe.Props.fertilizationMode == AquaticFertilizationMode.External
                && roe.Props.fertilized
                && __instance.props is CompProperties_EggLayer layer
                && layer.eggUnfertilizedDef != null)
            {
                Thing unfertilized = ThingMaker.MakeThing(layer.eggUnfertilizedDef);
                if (unfertilized.TryGetComp<CompAmphibianRoe>() is CompAmphibianRoe replacement)
                {
                    __result.Destroy(DestroyMode.Vanish);
                    __result = unfertilized;
                    roe = replacement;
                }
            }
            roe.eggCount = Math.Max(1, originalEggCount);
            roe.mother = __instance.parent as Pawn;
            roe.father = roe.Props.fertilized
                ? __state ?? FertilizedByField?.GetValue(__instance) as Pawn : null;
            roe.faction = __instance.parent.Faction;
            __result.stackCount = 1;
        }
    }

    // Some aquatic offspring are protected from conspecific consumption.
    // Other species retain normal predator/prey food selection.
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat),
        new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    internal static class Patch_AmphibianOffspringConspecificFood
    {
        private static void Postfix(Pawn p, Thing food, ref bool __result)
        {
            if (!__result || p == null || food == null)
                return;
            if (food.TryGetComp<CompAmphibianRoe>() is CompAmphibianRoe roe
                && p.def == roe.Props.parentRace
                && roe.Props.protectFromConspecifics
                && ModConstants.AdvancedReproduction)
            {
                __result = false;
                return;
            }
            if (food.TryGetComp<CompTadpole>() is CompTadpole tadpole
                && p.def == tadpole.ParentRace
                && tadpole.ProtectFromConspecifics
                && ModConstants.AdvancedReproduction)
                __result = false;
        }
    }

    // Water is not an unroofed storage yard: do not let rain or open-sky
    // deterioration consume immersed clutches. Land remains vulnerable.
    [HarmonyPatch(typeof(SteadyEnvironmentEffects),
        nameof(SteadyEnvironmentEffects.FinalDeteriorationRate),
        new[] { typeof(Thing), typeof(List<string>) })]
    internal static class Patch_AmphibianRoeWaterDeterioration
    {
        private static void Postfix(Thing t, List<string> reasons, ref float __result)
        {
            if (__result > 0f && t?.Spawned == true
                && t.TryGetComp<CompAmphibianRoe>() != null
                && ModConstants.AdvancedReproduction
                && t.Position.GetTerrain(t.Map).IsWater)
            {
                __result = 0f;
                reasons?.Clear();
            }
        }
    }
}
