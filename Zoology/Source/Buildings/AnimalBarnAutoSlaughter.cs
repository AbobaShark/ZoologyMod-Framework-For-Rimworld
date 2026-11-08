#if RIMWORLD17
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Re-evaluate the actual vanilla auto-slaughter limits over the combined
    // (spawned + contained) herd. We do not make unspawned pawns pretend to be
    // spawned in MapPawns, since most vanilla systems require a valid map cell.
    [HarmonyPatch(typeof(AutoSlaughterManager), "get_AnimalsToSlaughter")]
    internal static class Patch_BarnAutoSlaughter
    {
        private static bool Prepare() => Building_AnimalBarn.Enabled;

        private sealed class Cache
        {
            public int Tick = -1;
            public bool IncludesBarnAnimals;
            public readonly List<Pawn> Targets = new List<Pawn>();
        }

        private static readonly Dictionary<int, Cache> cacheByMap = new Dictionary<int, Cache>();
        private static Game cacheGame;

        private static void Postfix(AutoSlaughterManager __instance, ref List<Pawn> __result)
        {
            if (!Building_AnimalBarn.Enabled || __instance?.map == null)
            {
                return;
            }

            Map map = __instance.map;
            if (cacheGame != Current.Game)
            {
                cacheGame = Current.Game;
                cacheByMap.Clear();
            }

            if (!cacheByMap.TryGetValue(map.uniqueID, out Cache cache))
            {
                cache = new Cache();
                cacheByMap.Add(map.uniqueID, cache);
            }

            int now = GenTicks.TicksGame;
            if (cache.Tick >= 0 && now >= cache.Tick && now - cache.Tick < 250)
            {
                if (cache.IncludesBarnAnimals)
                {
                    __result = cache.Targets;
                }
                return;
            }

            var contained = new List<Pawn>();
            foreach (Building_AnimalBarn barn in AnimalBarnUtility.BarnsOnMap(map))
            {
                contained.AddRange(barn.Animals);
            }

            cache.Tick = now;
            cache.IncludesBarnAnimals = contained.Count > 0;
            if (!cache.IncludesBarnAnimals)
            {
                cache.Targets.Clear();
                return;
            }

            CalculateTargets(map, __instance.configs, contained, cache.Targets);
            __result = cache.Targets;
        }

        private static bool Eligible(Pawn animal, AutoSlaughterConfig config)
        {
            if (animal == null || animal.Dead || animal.Destroyed
                || animal.def != config.animal
                || !AutoSlaughterManager.CanAutoSlaughterNow(animal)
                || !config.allowSlaughterBonded
                    && animal.relations?.GetDirectRelationsCount(PawnRelationDefOf.Bond) > 0)
            {
                return false;
            }

            ZoologyModSettings settings = ModConstants.Settings;
            HediffDef lactation = AnimalLactationUtility.LactatingHediffDef;
            if (settings != null && !settings.AllowSlaughterLactating
                && lactation != null && animal.health.hediffSet.HasHediff(lactation))
            {
                return false;
            }

            return true;
        }

        private static void CalculateTargets(
            Map map, List<AutoSlaughterConfig> configs, List<Pawn> contained, List<Pawn> result)
        {
            result.Clear();
            if (configs == null)
            {
                return;
            }

            var all = new List<Pawn>(map.mapPawns.SpawnedColonyAnimals.Count + contained.Count);
            all.AddRange(map.mapPawns.SpawnedColonyAnimals);
            all.AddRange(contained);

            foreach (AutoSlaughterConfig config in configs)
            {
                if (config?.animal == null || !config.AnyLimit)
                {
                    continue;
                }

                var eligible = new List<Pawn>();
                var male = new List<Pawn>();
                var maleYoung = new List<Pawn>();
                var female = new List<Pawn>();
                var femaleYoung = new List<Pawn>();
                var pregnant = new List<Pawn>();

                foreach (Pawn animal in all)
                {
                    if (!Eligible(animal, config))
                    {
                        continue;
                    }

                    bool adult = animal.ageTracker.CurLifeStage.reproductive;
                    if (animal.gender == Gender.Male)
                    {
                        (adult ? male : maleYoung).Add(animal);
                        eligible.Add(animal);
                    }
                    else if (animal.gender == Gender.Female)
                    {
                        if (adult && animal.health.hediffSet.HasHediff(HediffDefOf.Pregnant))
                        {
                            if (config.allowSlaughterPregnant)
                            {
                                pregnant.Add(animal);
                            }
                        }
                        else
                        {
                            (adult ? female : femaleYoung).Add(animal);
                            eligible.Add(animal);
                        }
                    }
                    else
                    {
                        eligible.Add(animal);
                    }
                }

                Comparison<Pawn> oldestFirst =
                    (a, b) => b.ageTracker.AgeBiologicalTicks.CompareTo(a.ageTracker.AgeBiologicalTicks);
                eligible.Sort(oldestFirst);
                male.Sort(oldestFirst);
                maleYoung.Sort(oldestFirst);
                female.Sort(oldestFirst);
                femaleYoung.Sort(oldestFirst);
                pregnant.Sort((a, b) => b.health.hediffSet
                    .GetFirstHediffOfDef(HediffDefOf.Pregnant).Severity.CompareTo(
                        a.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Pregnant).Severity));
                female.AddRange(pregnant);
                eligible.AddRange(pregnant);

                RemoveExcess(female, config.maxFemales, eligible, result);
                RemoveExcess(femaleYoung, config.maxFemalesYoung, eligible, result);
                RemoveExcess(male, config.maxMales, eligible, result);
                RemoveExcess(maleYoung, config.maxMalesYoung, eligible, result);
                RemoveExcess(eligible, config.maxTotal, null, result);
            }
        }

        private static void RemoveExcess(
            List<Pawn> group, int limit, List<Pawn> all, List<Pawn> result)
        {
            if (limit < 0)
            {
                return;
            }

            while (group.Count > limit)
            {
                Pawn animal = group[0];
                group.RemoveAt(0);
                all?.Remove(animal);
                if (!result.Contains(animal))
                {
                    result.Add(animal);
                }
            }
        }
    }
}
#endif
