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
    public sealed class CompProperties_AmphibianRoe : CompProperties
    {
        public ThingDef parentRace;
        public ThingDef tadpoleDef;
        public ThingDef fertilizedRoeDef;
        public IntRange hatchlingCountRange;
        // Absolute biological mass/energy of one complete water-bound ovum.
        public int defaultOvaCount;
        public float wetMassPerOvumKg;
        public float grossEnergyPerOvumJ;
        public float joulesPerNutrition;
        public float daysToHatch;
        public bool fertilized;

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
        private int hatchlingTarget;

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
            if (Props.fertilized || Props.fertilizedRoeDef == null
                || parent?.Spawned != true || male?.Spawned != true
                || male.Map != parent.Map || male.Dead || male.Downed
                || male.gender != Gender.Male || male.Sterile()
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
            Scribe_Values.Look(ref hatchlingTarget, "zoologyRoeHatchlingTarget");
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
            progress = Math.Min(1f, progress + 250f / (Math.Max(0.01f, Props.daysToHatch) * GenDate.TicksPerDay));
            if (progress >= 1f) HatchIntoTadpole();
        }

        private void EnsureHatchlingTarget()
        {
            if (hatchlingTarget <= 0)
                hatchlingTarget = Math.Min(Math.Max(1, eggCount),
                    Math.Max(1, Props.hatchlingCountRange.RandomInRange));
        }

        private int ViableOva => hatchlingTarget <= 0 ? eggCount
            : Mathf.Max(hatchlingTarget, eggCount - Mathf.RoundToInt(
                (eggCount - hatchlingTarget) * Mathf.Clamp01(progress)));

        private void HatchIntoTadpole()
        {
            if (!parent.Spawned || !parent.Position.GetTerrain(parent.Map).IsWater
                || Props.tadpoleDef == null) return;
            Map map = parent.Map;
            IntVec3 cell = parent.Position;
            EnsureHatchlingTarget();
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
            "Ova in clutch: " + eggCount +
            (Props.fertilized
                ? "\nViable ova: " + ViableOva
                    + "\nIncubation: " + progress.ToStringPercent()
                : "\nUnfertilized");

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

    public sealed class CompProperties_Tadpole : CompProperties
    {
        public PawnKindDef pawnKind;
        public IntRange frogletCountRange;
        public float hatchMassKg;
        public float metamorphMassKg;
        public float grossEnergyMJPerKg;
        public float joulesPerNutrition;
        public float daysToMetamorphosis;

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
        private int initialTadpoles;
        private int frogletTarget;
        private int frogletsReleased;

        private CompProperties_Tadpole Props => (CompProperties_Tadpole)props;
        internal ThingDef ParentRace => Props.pawnKind?.race;

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

        // Values are gameplay bounds shared by species, not species lists.
        // The initial ovum count always comes from CompEggLayer/EggMilkWool.
        internal void InitializeCohort(int ova, int larvae, Pawn dam, Pawn sire, Faction originalFaction)
        {
            eggCount = Math.Max(1, ova);
            mother = dam;
            father = sire;
            faction = originalFaction;
            initialTadpoles = Mathf.Clamp(larvae, 1, eggCount);
            frogletTarget = Math.Min(initialTadpoles,
                Math.Max(1, Props.frogletCountRange.RandomInRange));
        }

        private void EnsureCohort()
        {
            // Old saves contain eggCount and progress but no cohort counters.
            if (initialTadpoles <= 0 || frogletTarget <= 0)
                InitializeCohort(Math.Max(1, eggCount),
                    Math.Min(Math.Max(1, eggCount),
                        Math.Max(1, Props.frogletCountRange.max)),
                    mother, father, faction);
        }

        private int LivingTadpoles
        {
            get
            {
                if (initialTadpoles <= 0) return 0;
                int surviving = Mathf.Max(frogletTarget,
                    initialTadpoles - Mathf.RoundToInt(
                        (initialTadpoles - frogletTarget) * Mathf.Clamp01(progress)));
                return Mathf.Max(0, surviving - frogletsReleased);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref eggCount, "zoologyTadpoleEggCount");
            Scribe_Values.Look(ref progress, "zoologyTadpoleProgress");
            Scribe_Values.Look(ref initialTadpoles, "zoologyTadpoleInitialCohort");
            Scribe_Values.Look(ref frogletTarget, "zoologyTadpoleFrogletTarget");
            Scribe_Values.Look(ref frogletsReleased, "zoologyTadpoleFrogletsReleased");
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
                if (progress < 1f)
                    progress = Math.Min(1f, progress + 250f /
                        (Math.Max(0.01f, Props.daysToMetamorphosis) * GenDate.TicksPerDay));
                if (progress >= 1f)
                {
                    ReleaseOneFroglet();
                    return;
                }
            }
            if (parent.Spawned && swim) Swim();
        }

        private void Swim()
        {
            Map map = parent.Map;
            IntVec3 origin = parent.Position;
            int start = Rand.Range(0, GenAdj.AdjacentCells.Length);
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 destination = origin + GenAdj.AdjacentCells[(start + i) % GenAdj.AdjacentCells.Length];
                if (!destination.InBounds(map) || !destination.GetTerrain(map).IsWater
                    || destination.GetEdifice(map) != null)
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
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            int count = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn candidate = pawns[i];
                if (candidate.def == Props.pawnKind.race && !candidate.Dead
                    && WildAnimalEcosystemUtility.IsWildAnimal(candidate)
                    && ++count >= settings.WildAnimalSpeciesLimit)
                    return true;
            }
            return false;
        }

        private void ReleaseOneFroglet()
        {
            if (Props.pawnKind == null) return;
            if (frogletsReleased >= frogletTarget || WildSpeciesAtCapacity(parent.Map))
            {
                parent.Destroy(DestroyMode.Vanish);
                return;
            }
            Pawn frog = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                Props.pawnKind, faction, developmentalStages: DevelopmentalStage.Newborn));
            if (!PawnUtility.TrySpawnHatchedOrBornPawn(frog, parent, null))
            {
                Find.WorldPawns.PassToWorld(frog, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                return;
            }
            if (mother != null && frog.RaceProps.IsFlesh)
                frog.relations.AddDirectRelation(PawnRelationDefOf.Parent, mother);
            if (father != null && father != mother && frog.RaceProps.IsFlesh)
                frog.relations.AddDirectRelation(PawnRelationDefOf.Parent, father);
            frogletsReleased++;
            if (frogletsReleased >= frogletTarget)
                parent.Destroy(DestroyMode.Vanish);
        }

        public override string CompInspectStringExtra() =>
            "Tadpole development: " + progress.ToStringPercent() +
            "\nOva in source clutch: " + eggCount +
            "\nLiving tadpoles: " + LivingTadpoles +
            "\nMetamorphosed: " + frogletsReleased;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (Prefs.DevMode && DebugSettings.ShowDevGizmos)
                yield return new Command_Action
                {
                    defaultLabel = "Zoology_DevCompleteMetamorphosis".Translate(),
                    action = () =>
                    {
                        if (!parent.Spawned || !parent.Position.GetTerrain(parent.Map).IsWater)
                            return;
                        EnsureCohort();
                        progress = 1f;
                        ReleaseOneFroglet();
                    }
                };
        }
    }

    [HarmonyPatch(typeof(CompEggLayer), nameof(CompEggLayer.ProduceEgg))]
    internal static class Patch_AmphibianRoeMass
    {
        private static readonly FieldInfo FertilizedByField =
            AccessTools.Field(typeof(CompEggLayer), "fertilizedBy");

        private static void Postfix(CompEggLayer __instance, ref Thing __result)
        {
            if (__result?.TryGetComp<CompAmphibianRoe>() is not CompAmphibianRoe roe
                || !ModConstants.AdvancedReproduction)
                return;
            int originalEggCount = __result.stackCount;
            // Aquatic clutches are externally fertilized. Never create a
            // fertilized egg through vanilla internal mating, even if another
            // mod left the CompEggLayer fertilized counter set.
            if (roe.Props.fertilized
                && __instance.props is CompProperties_EggLayer layer
                && layer.eggUnfertilizedDef != null)
            {
                Thing unfertilized = ThingMaker.MakeThing(layer.eggUnfertilizedDef);
                if (unfertilized.TryGetComp<CompAmphibianRoe>() is CompAmphibianRoe replacement)
                {
                    __result = unfertilized;
                    roe = replacement;
                }
            }
            roe.eggCount = originalEggCount;
            roe.mother = __instance.parent as Pawn;
            roe.father = roe.Props.fertilized
                ? FertilizedByField?.GetValue(__instance) as Pawn : null;
            roe.faction = __instance.parent.Faction;
            __result.stackCount = 1;
        }
    }

    // Neither roe nor tadpole cohorts are food for their own species, at any
    // age. Other species retain normal predator/prey food selection.
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
                && ModConstants.AdvancedReproduction)
            {
                __result = false;
                return;
            }
            if (food.TryGetComp<CompTadpole>() is CompTadpole tadpole
                && p.def == tadpole.ParentRace
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
