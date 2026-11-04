using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    public sealed class WildAnimalOverpopulationExitMapComponent : MapComponent
    {
        private static readonly IntRange InitialCheckDelayTicks = new IntRange(2500, 5000);

        private const int NormalCheckIntervalTicks = 60000;
        private const int MildOverloadExitIntervalTicks = 60000;
        private const int SevereOverloadExitIntervalTicks = 2500;
        private const float SevereOverloadRatio = 2f;

        private int nextExitCheckTick = -1;

        public WildAnimalOverpopulationExitMapComponent(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextExitCheckTick, "Zoology_nextOverpopulationWildExitTick", -1);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            int now = GenTicks.TicksGame;
            if (nextExitCheckTick < now || nextExitCheckTick - now > NormalCheckIntervalTicks)
            {
                ScheduleInitialCheck(now);
            }
        }

        public override void MapComponentTick()
        {
            ZoologyModSettings settings = ModConstants.Settings;
            if (!ShouldRun(settings))
            {
                return;
            }

            int now = GenTicks.TicksGame;
            if (nextExitCheckTick < 0 || nextExitCheckTick - now > NormalCheckIntervalTicks)
            {
                ScheduleInitialCheck(now);
                return;
            }

            if (now < nextExitCheckTick)
            {
                return;
            }

            try
            {
                if (map?.CanEverExit != true)
                {
                    ScheduleNormalCheck(now);
                    return;
                }

                IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                {
                    ScheduleNormalCheck(now);
                    return;
                }

                // Animals already committed to leaving are still on the map,
                // but dispatching more of their species would overshoot the
                // cap when several successive exit scans run during travel.
                // Count only the remaining wild population for both limits.
                var counts = new Dictionary<ThingDef, int>();
                float remainingWeight = 0f;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn animal = pawns[i];
                    if (!WildAnimalEcosystemUtility.IsWildAnimal(animal)
                        || animal.Dead || animal.Destroyed || animal.def == null
                        || ChildcareFamilyExitMapUtility.IsLeavingMap(animal.CurJob))
                    {
                        continue;
                    }

                    counts.TryGetValue(animal.def, out int population);
                    counts[animal.def] = population + 1;
                    if (animal.kindDef != null)
                    {
                        remainingWeight += animal.kindDef.ecoSystemWeight;
                    }
                }

                // Forced ecological departure is independent of whether
                // Zoology's optional wild mating system is enabled.
                float allowedWeight = settings.ForceWildAnimalsToLeaveOnEcosystemOverload
                    ? WildAnimalEcosystemUtility.AllowedEcosystemWeight(
                        map, settings.WildAnimalReproductionEcosystemLimitFactor)
                    : 0f;
                float ecosystemRatio = allowedWeight > 0f ? remainingWeight / allowedWeight : 0f;
                bool ecosystemOver = allowedWeight > 0f
                    && remainingWeight >= allowedWeight;

                int limit = settings.WildAnimalSpeciesLimit;
                bool speciesOver = false;
                float worstSpeciesRatio = 0f;
                if (settings.LimitWildAnimalsPerSpecies)
                {
                    foreach (int count in counts.Values)
                    {
                        if (count <= limit)
                        {
                            continue;
                        }

                        speciesOver = true;
                        worstSpeciesRatio = Mathf.Max(worstSpeciesRatio, (float)count / limit);
                    }
                }

                if (!ecosystemOver && !speciesOver)
                {
                    ScheduleNormalCheck(now);
                    return;
                }

                // Species count takes precedence, but cannot prevent ecosystem
                // recovery when no eligible group of the overpopulated species
                // can leave (for example, due to dependent mixed-species young).
                List<Pawn> candidates = FindBestExitGroup(pawns, counts, speciesOver,
                    ecosystemOver && !speciesOver, allowedWeight, limit);
                bool started = candidates != null
                    && ChildcareFamilyExitMapUtility.TryStartForcedWildExit(candidates[0], candidates);

                if (!started && speciesOver && ecosystemOver)
                {
                    candidates = FindBestExitGroup(pawns, counts, false, true, allowedWeight, limit);
                    if (candidates != null)
                    {
                        ChildcareFamilyExitMapUtility.TryStartForcedWildExit(candidates[0], candidates);
                    }
                }

                ScheduleOverloadedCheck(now, Mathf.Max(worstSpeciesRatio, ecosystemRatio));
            }
            catch (Exception ex)
            {
                ScheduleNormalCheck(now);
                Log.Warning($"Zoology: forced wild animal population exit failed: {ex}");
            }
        }

        private static bool ShouldRun(ZoologyModSettings settings)
        {
            return settings != null
                && !settings.DisableAllRuntimePatches
                && (settings.LimitWildAnimalsPerSpecies
                    || settings.ForceWildAnimalsToLeaveOnEcosystemOverload);
        }

        private List<Pawn> FindBestExitGroup(
            IReadOnlyList<Pawn> pawns,
            Dictionary<ThingDef, int> counts,
            bool speciesOver,
            bool ecosystemOver,
            float allowedWeight,
            int limit)
        {
            if (pawns == null)
            {
                return null;
            }

            // Resolve families once per check: evaluating every child for every candidate
            // would otherwise turn population control into a quadratic map-wide scan.
            var childrenByMother = new Dictionary<Pawn, List<Pawn>>();
            var linkedChildren = new HashSet<Pawn>();
            bool childcare = ChildcareUtility.IsChildcareEnabled;
            if (childcare)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn child = pawns[i];
                    if (!WildAnimalEcosystemUtility.IsWildAnimal(child)
                        || ChildcareFamilyExitMapUtility.IsLeavingMap(child.CurJob)
                        || !ChildcareUtility.IsAnimalChild(child)
                        || !ChildcareUtility.TryGetBiologicalMother(child, out Pawn mother)
                        || mother == null || mother.Map != map
                        || !ChildcareUtility.HasChildcareExtension(mother))
                    {
                        continue;
                    }

                    linkedChildren.Add(child);
                    if (!childrenByMother.TryGetValue(mother, out List<Pawn> children))
                    {
                        children = new List<Pawn>();
                        childrenByMother.Add(mother, children);
                    }

                    children.Add(child);
                }
            }

            Dictionary<ThingDef, float> expected = ecosystemOver && !speciesOver
                ? WildAnimalEcosystemUtility.GetExpectedSpeciesWeights(map, allowedWeight)
                : null;
            var actualWeights = new Dictionary<ThingDef, float>();
            if (ecosystemOver)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (!WildAnimalEcosystemUtility.IsWildAnimal(pawn) || pawn.kindDef == null
                        || ChildcareFamilyExitMapUtility.IsLeavingMap(pawn.CurJob))
                    {
                        continue;
                    }

                    actualWeights.TryGetValue(pawn.def, out float weight);
                    actualWeights[pawn.def] = weight + Mathf.Max(0f, pawn.kindDef.ecoSystemWeight);
                }
            }

            List<Pawn> best = null;
            var group = new List<Pawn>(8);
            int bestExcess = -1;
            int bestCoverage = -1;
            int bestWasted = int.MaxValue;
            float bestEcologyScore = float.NegativeInfinity;
            int ties = 0;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn root = pawns[i];
                // Dependent young stay with their mothers, while orphaned
                // or otherwise unlinked wild juveniles can leave on their own.
                if (!IsEligible(root) || linkedChildren.Contains(root))
                {
                    continue;
                }

                group.Clear();
                group.Add(root);
                bool canLeaveTogether = true;
                if (childrenByMother.TryGetValue(root, out List<Pawn> children))
                {
                    for (int j = 0; j < children.Count; j++)
                    {
                        Pawn child = children[j];
                        if (!IsEligible(child))
                        {
                            canLeaveTogether = false;
                            break;
                        }

                        group.Add(child);
                    }
                }

                if (!canLeaveTogether)
                {
                    continue;
                }

                bool preferred;
                bool equivalent;
                if (speciesOver)
                {
                    // The species cap controls head count, not ecosystem
                    // weight. Only a species with population above the
                    // configured threshold may be selected for this reason.
                    if (!counts.TryGetValue(root.def, out int population)
                        || population <= limit)
                    {
                        continue;
                    }

                    // Never evict a different species just because it is a
                    // dependent member of a mixed-species family. Such a
                    // family is left intact; other eligible groups can go.
                    bool oneSpecies = true;
                    for (int j = 1; j < group.Count; j++)
                    {
                        if (group[j].def != root.def)
                        {
                            oneSpecies = false;
                            break;
                        }
                    }
                    if (!oneSpecies)
                    {
                        continue;
                    }

                    int excess = population - limit;
                    int coverage = Math.Min(group.Count, excess);
                    int wasted = group.Count - coverage;
                    // Among groups of the most overpopulated species,
                    // minimize departures beyond the excess before favoring
                    // a larger reduction of the remaining overpopulation.
                    preferred = excess > bestExcess
                        || excess == bestExcess && wasted < bestWasted
                        || excess == bestExcess && wasted == bestWasted && coverage > bestCoverage;
                    equivalent = excess == bestExcess && wasted == bestWasted && coverage == bestCoverage;
                    if (preferred)
                    {
                        bestExcess = excess;
                        bestCoverage = coverage;
                        bestWasted = wasted;
                    }
                }
                else
                {
                    float score = 0f;
                    for (int j = 0; j < group.Count; j++)
                    {
                        Pawn member = group[j];
                        if (member.kindDef == null
                            || !actualWeights.TryGetValue(member.def, out float weight)
                            || weight <= 0f)
                        {
                            continue;
                        }

                        expected.TryGetValue(member.def, out float expectedWeight);
                        float surplus = Mathf.Max(0f, weight - expectedWeight);
                        // Avoid evicting a lone, non-native visitor before a large native population.
                        if (expectedWeight <= 0f && counts.TryGetValue(member.def, out int count) && count <= 1)
                        {
                            continue;
                        }

                        score += Mathf.Min(surplus, Mathf.Max(0f, member.kindDef.ecoSystemWeight))
                            * surplus / weight;
                    }

                    // When only ineligible animals cause the excess, allow a normal
                    // departure instead of leaving the whole population frozen.
                    if (score <= 0f)
                    {
                        for (int j = 0; j < group.Count; j++)
                        {
                            score += Mathf.Max(0f, group[j].kindDef?.ecoSystemWeight ?? 0f) * 0.00001f;
                        }
                    }

                    preferred = score > bestEcologyScore + 0.000001f;
                    equivalent = Math.Abs(score - bestEcologyScore) <= 0.000001f;
                    if (preferred)
                    {
                        bestEcologyScore = score;
                    }
                }

                if (preferred)
                {
                    best = new List<Pawn>(group);
                    ties = 1;
                }
                else if (equivalent && Rand.Chance(1f / ++ties))
                {
                    best = new List<Pawn>(group);
                }
            }

            return best;
        }

        private bool IsEligible(Pawn pawn)
        {
            return ChildcareFamilyExitMapUtility.CanJoinFamilyExit(pawn, map)
                && !pawn.IsQuestLodger()
                && !ChildcareFamilyExitMapUtility.IsProtectedByGuardingEggMother(pawn);
        }

        private void ScheduleInitialCheck(int now)
        {
            nextExitCheckTick = now + InitialCheckDelayTicks.RandomInRange;
        }

        private void ScheduleNormalCheck(int now)
        {
            nextExitCheckTick = now + NormalCheckIntervalTicks;
        }

        private void ScheduleOverloadedCheck(int now, float overloadRatio)
        {
            nextExitCheckTick = now + (overloadRatio >= SevereOverloadRatio
                ? SevereOverloadExitIntervalTicks
                : MildOverloadExitIntervalTicks);
        }
    }
}
