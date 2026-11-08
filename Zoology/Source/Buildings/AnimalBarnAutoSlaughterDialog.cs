#if RIMWORLD17
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Include the stored animals in the vanilla auto-slaughter configuration
    // dialog. The vanilla counters are private, so change the boxed records
    // after the ordinary counts and Zoology's existing lactation adjustment.
    [HarmonyPatch(typeof(Dialog_AutoSlaughter), "RecalculateAnimals")]
    [HarmonyPriority(Priority.Last)]
    internal static class Patch_BarnAutoSlaughterDialog
    {
        private static readonly FieldInfo MapField = AccessTools.Field(typeof(Dialog_AutoSlaughter), "map");
        private static readonly FieldInfo CountsField = AccessTools.Field(typeof(Dialog_AutoSlaughter), "animalCounts");
        private static readonly FieldInfo OrderedField = AccessTools.Field(typeof(Dialog_AutoSlaughter), "configsOrdered");
        private static readonly Type RecordType = typeof(Dialog_AutoSlaughter)
            .GetNestedType("AnimalCountRecord", BindingFlags.NonPublic);

        private static readonly FieldInfo Total = RecordType?.GetField("total", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Male = RecordType?.GetField("male", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo MaleYoung = RecordType?.GetField("maleYoung", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Female = RecordType?.GetField("female", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FemaleYoung = RecordType?.GetField("femaleYoung", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Pregnant = RecordType?.GetField("pregnant", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo Bonded = RecordType?.GetField("bonded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool Prepare()
        {
            return MapField != null && CountsField != null && OrderedField != null
                && Total != null && Male != null && MaleYoung != null
                && Female != null && FemaleYoung != null && Pregnant != null && Bonded != null;
        }

        private static void Postfix(Dialog_AutoSlaughter __instance)
        {
            if (!Building_AnimalBarn.Enabled)
            {
                return;
            }

            Map map = MapField.GetValue(__instance) as Map;
            if (map == null || !(CountsField.GetValue(__instance) is IDictionary counts))
            {
                return;
            }

            List<AutoSlaughterConfig> configs = map.autoSlaughterManager?.configs;
            if (configs == null)
            {
                return;
            }

            var animals = new List<Pawn>();
            foreach (Building_AnimalBarn barn in AnimalBarnUtility.BarnsOnMap(map))
            {
                animals.AddRange(barn.Animals);
            }

            if (animals.Count == 0)
            {
                return;
            }

            foreach (AutoSlaughterConfig config in configs)
            {
                if (config?.animal == null || !counts.Contains(config.animal))
                {
                    continue;
                }

                object record = counts[config.animal];
                if (record == null)
                {
                    continue;
                }

                int total = (int)Total.GetValue(record);
                int male = (int)Male.GetValue(record);
                int maleYoung = (int)MaleYoung.GetValue(record);
                int female = (int)Female.GetValue(record);
                int femaleYoung = (int)FemaleYoung.GetValue(record);
                int pregnant = (int)Pregnant.GetValue(record);
                int bonded = (int)Bonded.GetValue(record);

                foreach (Pawn animal in animals)
                {
                    if (animal == null || animal.def != config.animal
                        || !AutoSlaughterManager.CanEverAutoSlaughter(animal))
                    {
                        continue;
                    }

                    bool hasBond = animal.relations?.GetDirectRelationsCount(PawnRelationDefOf.Bond) > 0;
                    if (hasBond)
                    {
                        bonded++;
                        if (!config.allowSlaughterBonded)
                        {
                            continue;
                        }
                    }

                    // Zoology's existing dialog patch excludes lactating
                    // females from the corresponding active slaughter counts.
                    HediffDef lactDef = AnimalLactationUtility.LactatingHediffDef;
                    if (ModConstants.Settings?.AllowSlaughterLactating == false
                        && lactDef != null && animal.health?.hediffSet?.HasHediff(lactDef) == true)
                    {
                        continue;
                    }

                    bool adult = animal.ageTracker?.CurLifeStage?.reproductive == true;
                    if (animal.gender == Gender.Male)
                    {
                        if (adult) male++; else maleYoung++;
                    }
                    else if (animal.gender == Gender.Female)
                    {
                        Hediff condition = adult
                            ? animal.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant)
                            : null;
                        if (condition != null && condition.Visible)
                        {
                            pregnant++;
                            if (!config.allowSlaughterPregnant)
                            {
                                continue;
                            }
                        }

                        if (adult) female++; else femaleYoung++;
                    }

                    total++;
                }

                Total.SetValue(record, total);
                Male.SetValue(record, male);
                MaleYoung.SetValue(record, maleYoung);
                Female.SetValue(record, female);
                FemaleYoung.SetValue(record, femaleYoung);
                Pregnant.SetValue(record, pregnant);
                Bonded.SetValue(record, bonded);
                counts[config.animal] = record;
            }

            if (OrderedField.GetValue(__instance) is List<AutoSlaughterConfig> ordered)
            {
                ordered.Sort((first, second) =>
                {
                    int left = counts.Contains(first.animal) ? (int)Total.GetValue(counts[first.animal]) : 0;
                    int right = counts.Contains(second.animal) ? (int)Total.GetValue(counts[second.animal]) : 0;
                    int byCount = right.CompareTo(left);
                    return byCount != 0 ? byCount : string.CompareOrdinal(first.animal?.label, second.animal?.label);
                });
            }
        }
    }
}
#endif
