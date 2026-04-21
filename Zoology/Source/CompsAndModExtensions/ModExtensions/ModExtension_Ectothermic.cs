using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public class ModExtension_Ectothermic : DefModExtension
    {
    }

    [StaticConstructorOnStartup]
    public static class Ectothermic_HarmonyPatches
    {
        private const string OrganicStandardDefName = "OrganicStandard";
        private const string EctothermicOrganicStandardDefName = "Zoology_Ectothermic_OrganicStandard";
        private static bool patched;
        private static readonly Dictionary<ThingDef, List<HediffGiverSetDef>> originalHediffGiverSetsByDef
            = new Dictionary<ThingDef, List<HediffGiverSetDef>>(64);
        private static HediffGiverSetDef ectothermicOrganicStandardSet;

        static Ectothermic_HarmonyPatches()
        {
            EnsurePatched();
        }

        public static void EnsurePatched()
        {
            if (patched)
            {
                return;
            }

            var settings = ModConstants.Settings;
            if (settings != null && settings.DisableAllRuntimePatches)
            {
                return;
            }

            if (settings != null && !settings.EnableEctothermicPatch)
            {
                return;
            }

            try
            {
                ApplyEctothermicHediffGiverOverrides();
                patched = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology.Ectothermic] failed to apply runtime hediff giver overrides: {ex}");
            }
        }

        public static void ResetPatchedState()
        {
            RestoreOriginalHediffGiverSets();
            ectothermicOrganicStandardSet = null;
            patched = false;
        }

        private static void ApplyEctothermicHediffGiverOverrides()
        {
            HediffGiverSetDef organicStandard = DefDatabase<HediffGiverSetDef>.GetNamedSilentFail(OrganicStandardDefName);
            if (organicStandard == null)
            {
                Log.Warning("[Zoology.Ectothermic] OrganicStandard hediff giver set was not found.");
                return;
            }

            HediffGiverSetDef replacement = GetOrCreateEctothermicOrganicStandardSet(organicStandard);
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                if (def?.race?.hediffGiverSets == null || !ZoologyCacheUtility.HasEctothermicExtension(def))
                {
                    continue;
                }

                ReplaceOrganicStandardForThingDef(def, organicStandard, replacement);
            }
        }

        private static void ReplaceOrganicStandardForThingDef(
            ThingDef def,
            HediffGiverSetDef organicStandard,
            HediffGiverSetDef replacement)
        {
            List<HediffGiverSetDef> sourceSets = def?.race?.hediffGiverSets;
            if (sourceSets == null || sourceSets.Count == 0 || replacement == null)
            {
                return;
            }

            bool replacedAny = false;
            List<HediffGiverSetDef> updatedSets = null;
            for (int i = 0; i < sourceSets.Count; i++)
            {
                HediffGiverSetDef current = sourceSets[i];
                if (!ReferenceEquals(current, organicStandard)
                    && !string.Equals(current?.defName, OrganicStandardDefName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (updatedSets == null)
                {
                    updatedSets = new List<HediffGiverSetDef>(sourceSets);
                }

                updatedSets[i] = replacement;
                replacedAny = true;
            }

            if (!replacedAny || updatedSets == null)
            {
                return;
            }

            originalHediffGiverSetsByDef[def] = new List<HediffGiverSetDef>(sourceSets);
            def.race.hediffGiverSets = updatedSets;
        }

        private static void RestoreOriginalHediffGiverSets()
        {
            if (originalHediffGiverSetsByDef.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<ThingDef, List<HediffGiverSetDef>> entry in originalHediffGiverSetsByDef)
            {
                ThingDef def = entry.Key;
                if (def?.race == null)
                {
                    continue;
                }

                def.race.hediffGiverSets = entry.Value != null
                    ? new List<HediffGiverSetDef>(entry.Value)
                    : null;
            }

            originalHediffGiverSetsByDef.Clear();
        }

        private static HediffGiverSetDef GetOrCreateEctothermicOrganicStandardSet(HediffGiverSetDef organicStandard)
        {
            if (ectothermicOrganicStandardSet != null)
            {
                return ectothermicOrganicStandardSet;
            }

            var clone = new HediffGiverSetDef
            {
                defName = EctothermicOrganicStandardDefName,
                hediffGivers = CloneHediffGivers(organicStandard.hediffGivers)
            };

            ectothermicOrganicStandardSet = clone;
            return ectothermicOrganicStandardSet;
        }

        private static List<HediffGiver> CloneHediffGivers(List<HediffGiver> source)
        {
            if (source == null)
            {
                return null;
            }

            var cloned = new List<HediffGiver>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                cloned.Add(CloneHediffGiver(source[i]));
            }

            return cloned;
        }

        private static HediffGiver CloneHediffGiver(HediffGiver source)
        {
            if (source == null)
            {
                return null;
            }

            HediffGiver clone = (HediffGiver)CloneObject(source);
            if (clone is HediffGiver_Hypothermia hypothermia)
            {
                HediffDef ectothermicDef = hypothermia.hediffInsectoid ?? hypothermia.hediff;
                if (ectothermicDef != null)
                {
                    hypothermia.hediff = ectothermicDef;
                }
            }

            return clone;
        }

        private static object CloneObject(object source)
        {
            Type type = source.GetType();
            object clone;
            try
            {
                clone = Activator.CreateInstance(type, true);
            }
            catch
            {
                clone = FormatterServices.GetUninitializedObject(type);
            }

            CopyInstanceFields(type, source, clone);
            return clone;
        }

        private static void CopyInstanceFields(Type type, object source, object target)
        {
            while (type != null && type != typeof(object))
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic)
                    {
                        continue;
                    }

                    field.SetValue(target, field.GetValue(source));
                }

                type = type.BaseType;
            }
        }
    }
}
