using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public sealed class ModExtension_AquaticEggLayer : DefModExtension
    {
        public float eggLayIntervalDays;
        public IntRange eggCountRange;
        public int eggFertilizationCountMax;
        public ThingDef eggUnfertilizedDef;
        public ThingDef eggFertilizedDef;
    }

    internal static class AquaticEggLayerUtility
    {
        private sealed class Binding
        {
            internal CompProperties_EggLayer layer;
            internal ModExtension_AquaticEggLayer profile;
            internal float nativeInterval;
            internal IntRange nativeCount;
            internal int nativeFertilizationCount;
            internal ThingDef nativeUnfertilized;
            internal ThingDef nativeFertilized;
        }

        private static readonly List<Binding> Bindings = new List<Binding>();
        private static readonly Dictionary<ThingDef, Binding> BindingsByRace = new Dictionary<ThingDef, Binding>();
        private static bool installed;

        internal static ModExtension_AquaticEggLayer Profile(ThingDef race) =>
            DefModExtensionCache<ModExtension_AquaticEggLayer>.Get(race);

        internal static ModExtension_AquaticEggLayer InstalledProfile(ThingDef race) =>
            race != null && BindingsByRace.TryGetValue(race, out Binding binding) ? binding.profile : null;

        private static readonly HashSet<ThingDef> AdultEmergenceRaces = new HashSet<ThingDef>();

        internal static bool UsesAdultEmergence(ThingDef race) =>
            ModConstants.AdvancedReproduction && race != null && AdultEmergenceRaces.Contains(race);

        internal static ThingDef AquaticOutput(CompEggLayer layer, ThingDef produced)
        {
            if (!ModConstants.AdvancedReproduction || layer?.parent?.def == null
                || !BindingsByRace.TryGetValue(layer.parent.def, out Binding binding)) return null;
            if (produced == binding.nativeFertilized) return binding.profile.eggFertilizedDef;
            if (produced != null && produced == binding.nativeUnfertilized) return binding.profile.eggUnfertilizedDef;
            return null;
        }

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            foreach (ThingDef race in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (race.race?.Animal != true) continue;
                ModExtension_AquaticEggLayer profile = Profile(race);
                if (profile == null) continue;
                CompProperties_EggLayer layer = race.GetCompProperties<CompProperties_EggLayer>();
                if (layer == null || layer.eggFertilizedDef == null
                    || profile.eggFertilizedDef == null || profile.eggUnfertilizedDef == null
                    || profile.eggLayIntervalDays <= 0f || profile.eggCountRange.min <= 0)
                {
                    Log.Error("[Zoology] Invalid aquatic egg layer on " + race.defName);
                    continue;
                }
                var binding = new Binding
                {
                    layer = layer,
                    profile = profile,
                    nativeInterval = layer.eggLayIntervalDays,
                    nativeCount = layer.eggCountRange,
                    nativeFertilizationCount = layer.eggFertilizationCountMax,
                    nativeUnfertilized = layer.eggUnfertilizedDef,
                    nativeFertilized = layer.eggFertilizedDef
                };
                Bindings.Add(binding);
                BindingsByRace.Add(race, binding);
            }
            // Both external and internal fertilization routes use the actual
            // linked aquatic cohort definition as the emergence contract.
            foreach (ThingDef race in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (race.race?.Animal != true) continue;
                ThingDef egg = InstalledProfile(race)?.eggFertilizedDef
                    ?? race.GetCompProperties<CompProperties_EggLayer>()?.eggFertilizedDef;
                var larva = egg?.GetCompProperties<CompProperties_AmphibianRoe>()
                    ?.tadpoleDef?.GetCompProperties<CompProperties_Tadpole>();
                if (larva?.emergeAsAdult == true && larva.pawnKind?.race == race)
                    AdultEmergenceRaces.Add(race);
            }
            SyncSettings();
        }

        internal static void SyncSettings(bool? enabledOverride = null)
        {
            if (!installed) return;
            bool enabled = enabledOverride ?? ModConstants.AdvancedReproduction;
            foreach (Binding entry in Bindings)
            {
                var layer = entry.layer;
                var profile = entry.profile;
                layer.eggLayIntervalDays = enabled ? profile.eggLayIntervalDays : entry.nativeInterval;
                layer.eggCountRange = enabled ? profile.eggCountRange : entry.nativeCount;
                layer.eggFertilizationCountMax = enabled ? profile.eggFertilizationCountMax
                    : entry.nativeFertilizationCount;
                // ProduceEgg keeps its native hatcher and fertilization bookkeeping.
                // The actual clutch Thing is selected after successful production.
            }
        }

        // Only mature cohorts need a shore route. Search connected water
        // locally and return one swimming step, without a Pawn pathfinder.
        internal static bool TryStepTowardShore(Map map, IntVec3 origin, out IntVec3 step)
        {
            step = IntVec3.Invalid;
            var queue = new Queue<IntVec3>();
            var firstSteps = new Dictionary<IntVec3, IntVec3>();
            firstSteps.Add(origin, origin);
            queue.Enqueue(origin);
            while (queue.Count > 0)
            {
                IntVec3 cell = queue.Dequeue();
                if (cell != origin && TryFindShore(map, cell, out _))
                {
                    step = firstSteps[cell];
                    return true;
                }
                foreach (IntVec3 offset in GenAdj.AdjacentCells)
                {
                    IntVec3 next = cell + offset;
                    if (!next.InBounds(map) || !next.InHorDistOf(origin, 14f)
                        || firstSteps.ContainsKey(next) || !next.GetTerrain(map).IsWater
                        || next.GetEdifice(map) != null)
                        continue;
                    firstSteps.Add(next, cell == origin ? next : firstSteps[cell]);
                    queue.Enqueue(next);
                }
            }
            return false;
        }

        internal static bool TryFindShore(Map map, IntVec3 water, out IntVec3 cell)
        {
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 candidate = water + GenAdj.AdjacentCells[i];
                if (candidate.InBounds(map) && candidate.Standable(map)
                    && !candidate.GetTerrain(map).IsWater)
                {
                    cell = candidate;
                    return true;
                }
            }
            cell = IntVec3.Invalid;
            return false;
        }
    }
}
