using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal enum RuntimeAnimalFeatureKind
    {
        ModExtension = 0,
        Comp = 1
    }

    internal sealed class RuntimeAnimalFeatureDefinition
    {
        public RuntimeAnimalFeatureDefinition(
            string id,
            string label,
            string enabledColumnLabel,
            string disabledColumnLabel,
            RuntimeAnimalFeatureKind kind,
            Type entryType,
            Func<ZoologyModSettings, bool> isToggleEnabled)
        {
            Id = id;
            Label = label;
            EnabledColumnLabel = enabledColumnLabel;
            DisabledColumnLabel = disabledColumnLabel;
            Kind = kind;
            EntryType = entryType;
            IsToggleEnabled = isToggleEnabled;
        }

        public string Id { get; }
        public string Label { get; }
        public string EnabledColumnLabel { get; }
        public string DisabledColumnLabel { get; }
        public RuntimeAnimalFeatureKind Kind { get; }
        public Type EntryType { get; }
        public Func<ZoologyModSettings, bool> IsToggleEnabled { get; }
    }

    internal static class ZoologyRuntimeAnimalOverrides
    {
        private const string TrainabilityNoneDefName = "None";
        private const string TrainabilityIntermediateDefName = "Intermediate";
        private const string TrainabilityAdvancedDefName = "Advanced";

        private sealed class RaceDefaults
        {
            public bool Roamer;
            public float? RoamMtbDays;
            public TrainabilityDef Trainability;
        }

        private static readonly List<ThingDef> cachedAnimalDefs = new List<ThingDef>(256);
        private static readonly Dictionary<ThingDef, RaceDefaults> raceDefaultsByDef = new Dictionary<ThingDef, RaceDefaults>(256);
        private static readonly Dictionary<string, RuntimeAnimalFeatureDefinition> featureById = new Dictionary<string, RuntimeAnimalFeatureDefinition>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<ThingDef, bool>> featureDefaultPresenceById = new Dictionary<string, Dictionary<ThingDef, bool>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<ThingDef, object>> featureDefaultEntryById = new Dictionary<string, Dictionary<ThingDef, object>>(StringComparer.Ordinal);
        private static readonly List<RuntimeAnimalFeatureDefinition> allFeatures = new List<RuntimeAnimalFeatureDefinition>
        {
            new RuntimeAnimalFeatureDefinition(
                "modext_childcare",
                "Childcare extension",
                "With childcare extension",
                "Without childcare extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtensiom_Chlidcare),
                s => s != null && s.EnableAnimalChildcare),
            new RuntimeAnimalFeatureDefinition(
                "modext_ectothermic",
                "Ectothermic extension",
                "With ectothermic extension",
                "Without ectothermic extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_Ectothermic),
                s => s != null && s.EnableEctothermicPatch),
            new RuntimeAnimalFeatureDefinition(
                "modext_mammal",
                "Mammal extension",
                "With mammal extension",
                "Without mammal extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_IsMammal),
                s => ZoologyModSettings.EnableMammalLactation),
            new RuntimeAnimalFeatureDefinition(
                "modext_scavenger",
                "Scavenger extension",
                "With scavenger extension",
                "Without scavenger extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_IsScavenger),
                s => s != null && s.EnableScavengering),
            new RuntimeAnimalFeatureDefinition(
                "modext_agro_at_slaughter",
                "Agro-at-slaughter extension",
                "With agro-at-slaughter extension",
                "Without agro-at-slaughter extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_AgroAtSlaughter),
                s => s != null && s.EnableAgroAtSlaughter),
            new RuntimeAnimalFeatureDefinition(
                "modext_cannot_be_mutated",
                "Cannot-be-mutated extension",
                "With cannot-be-mutated extension",
                "Without cannot-be-mutated extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_CannotBeMutated),
                s => s != null && s.EnableCannotBeMutatedProtection),
            new RuntimeAnimalFeatureDefinition(
                "modext_cannot_be_augmented",
                "Cannot-be-augmented extension",
                "With cannot-be-augmented extension",
                "Without cannot-be-augmented extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_CannotBeAugmented),
                s => s != null && s.EnableCannotBeAugmentedProtection),
            new RuntimeAnimalFeatureDefinition(
                "modext_no_flee",
                "No-flee extension",
                "With no-flee extension",
                "Without no-flee extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_NoFlee),
                s => s != null && s.EnableNoFleeExtension),
            new RuntimeAnimalFeatureDefinition(
                "modext_flee_from_carrier",
                "Flee-from-carrier extension",
                "With flee-from-carrier extension",
                "Without flee-from-carrier extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_FleeFromCarrier),
                s => s != null && s.EnableFleeFromCarrier),
            new RuntimeAnimalFeatureDefinition(
                "modext_no_porcupine_quill",
                "No-porcupine-quill extension",
                "With no-porcupine-quill extension",
                "Without no-porcupine-quill extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_NoPorcupineQuill),
                s => s != null && s.EnableNoPorcupineQuillPatch),
            new RuntimeAnimalFeatureDefinition(
                "modext_cannot_chew",
                "Cannot-chew extension",
                "With cannot-chew extension",
                "Without cannot-chew extension",
                RuntimeAnimalFeatureKind.ModExtension,
                typeof(ModExtension_CannotChew),
                s => s != null && s.EnableCannotChewExtension),
            new RuntimeAnimalFeatureDefinition(
                "comp_ageless",
                "Ageless comp",
                "With ageless comp",
                "Without ageless comp",
                RuntimeAnimalFeatureKind.Comp,
                typeof(CompProperties_Ageless),
                s => s != null && s.EnableAgelessPatch),
            new RuntimeAnimalFeatureDefinition(
                "comp_drugs_immune",
                "Drugs-immune comp",
                "With drugs-immune comp",
                "Without drugs-immune comp",
                RuntimeAnimalFeatureKind.Comp,
                typeof(CompProperties_DrugsImmune),
                s => s != null && s.EnableDrugsImmunePatch),
            new RuntimeAnimalFeatureDefinition(
                "comp_animal_regeneration",
                "Animal regeneration comp",
                "With animal regeneration comp",
                "Without animal regeneration comp",
                RuntimeAnimalFeatureKind.Comp,
                typeof(CompProperties_AnimalRegeneration),
                s => s != null && s.EnableAnimalRegenerationComp),
            new RuntimeAnimalFeatureDefinition(
                "comp_animal_clotting",
                "Animal clotting comp",
                "With animal clotting comp",
                "Without animal clotting comp",
                RuntimeAnimalFeatureKind.Comp,
                typeof(CompProperties_AnimalClotting),
                s => s != null && s.EnableAnimalClottingComp)
        };

        private static bool initialized;
        private static List<TrainabilityDef> cachedSupportedTrainabilityDefs;
        private static TrainabilityDef cachedTrainabilityNone;

        public static IReadOnlyList<RuntimeAnimalFeatureDefinition> Features
        {
            get
            {
                EnsureInitialized();
                return allFeatures;
            }
        }

        public static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            BuildAnimalDefCache();
            BuildFeatureMetadata();
        }

        public static IReadOnlyList<ThingDef> GetAnimalDefs()
        {
            EnsureInitialized();
            return cachedAnimalDefs;
        }

        public static bool TryGetFeature(string featureId, out RuntimeAnimalFeatureDefinition feature)
        {
            EnsureInitialized();
            return featureById.TryGetValue(featureId ?? string.Empty, out feature);
        }

        public static bool IsKnownFeatureId(string featureId)
        {
            EnsureInitialized();
            return !string.IsNullOrEmpty(featureId) && featureById.ContainsKey(featureId);
        }

        public static bool IsDefaultRoamer(ThingDef def)
        {
            EnsureInitialized();
            return def != null
                && raceDefaultsByDef.TryGetValue(def, out RaceDefaults defaults)
                && defaults.Roamer;
        }

        public static float? GetDefaultRoamMtbDays(ThingDef def)
        {
            EnsureInitialized();
            return def != null
                && raceDefaultsByDef.TryGetValue(def, out RaceDefaults defaults)
                ? defaults.RoamMtbDays
                : null;
        }

        public static TrainabilityDef GetDefaultTrainability(ThingDef def)
        {
            EnsureInitialized();
            if (def != null && raceDefaultsByDef.TryGetValue(def, out RaceDefaults defaults))
            {
                return defaults.Trainability ?? GetTrainabilityNone();
            }

            return GetTrainabilityNone();
        }

        public static bool GetDefaultFeaturePresence(string featureId, ThingDef def)
        {
            EnsureInitialized();
            if (def == null
                || string.IsNullOrEmpty(featureId)
                || !featureDefaultPresenceById.TryGetValue(featureId, out Dictionary<ThingDef, bool> byDef))
            {
                return false;
            }

            return byDef.TryGetValue(def, out bool enabled) && enabled;
        }

        public static IReadOnlyList<TrainabilityDef> GetSupportedTrainabilityDefs()
        {
            EnsureInitialized();
            if (cachedSupportedTrainabilityDefs != null)
            {
                return cachedSupportedTrainabilityDefs;
            }

            cachedSupportedTrainabilityDefs = new List<TrainabilityDef>(3);
            TryAddTrainability(cachedSupportedTrainabilityDefs, TrainabilityNoneDefName);
            TryAddTrainability(cachedSupportedTrainabilityDefs, TrainabilityIntermediateDefName);
            TryAddTrainability(cachedSupportedTrainabilityDefs, TrainabilityAdvancedDefName);
            return cachedSupportedTrainabilityDefs;
        }

        public static TrainabilityDef GetTrainabilityNone()
        {
            if (cachedTrainabilityNone != null)
            {
                return cachedTrainabilityNone;
            }

            cachedTrainabilityNone = DefDatabase<TrainabilityDef>.GetNamedSilentFail(TrainabilityNoneDefName)
                ?? TrainabilityDefOf.None;
            return cachedTrainabilityNone;
        }

        public static void ApplyAll(ZoologyModSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            EnsureInitialized();

            for (int i = 0; i < cachedAnimalDefs.Count; i++)
            {
                ThingDef def = cachedAnimalDefs[i];
                if (def?.race == null)
                {
                    continue;
                }

                ApplyRoamerAndTrainability(def, settings);
                ApplyFeatureOverrides(def, settings);
            }

            InvalidateCachesAfterApply();
        }

        private static void ApplyRoamerAndTrainability(ThingDef def, ZoologyModSettings settings)
        {
            bool roamer = settings.GetRoamerFor(def);
            if (roamer)
            {
                float roamMtbDays = settings.GetRoamMtbDaysFor(def);
                if (roamMtbDays <= 0f)
                {
                    roamMtbDays = 1f;
                }

                def.race.roamMtbDays = roamMtbDays;
                def.race.trainability = GetTrainabilityNone();
                return;
            }

            def.race.roamMtbDays = null;
            def.race.trainability = settings.GetNonRoamerTrainabilityFor(def) ?? GetTrainabilityNone();
        }

        private static void ApplyFeatureOverrides(ThingDef def, ZoologyModSettings settings)
        {
            for (int i = 0; i < allFeatures.Count; i++)
            {
                RuntimeAnimalFeatureDefinition feature = allFeatures[i];
                bool enabled = settings.GetAnimalFeatureEnabled(feature.Id, def);
                switch (feature.Kind)
                {
                    case RuntimeAnimalFeatureKind.ModExtension:
                        EnsureModExtensionPresence(def, feature, enabled);
                        break;
                    case RuntimeAnimalFeatureKind.Comp:
                        EnsureCompPresence(def, feature, enabled);
                        break;
                }
            }
        }

        private static void EnsureModExtensionPresence(ThingDef def, RuntimeAnimalFeatureDefinition feature, bool enabled)
        {
            List<DefModExtension> extensions = def.modExtensions;
            if (extensions == null)
            {
                if (!enabled)
                {
                    return;
                }

                extensions = new List<DefModExtension>(1);
                def.modExtensions = extensions;
            }

            int firstMatchIndex = -1;
            for (int i = 0; i < extensions.Count; i++)
            {
                DefModExtension extension = extensions[i];
                if (extension == null || !feature.EntryType.IsInstanceOfType(extension))
                {
                    continue;
                }

                if (firstMatchIndex < 0)
                {
                    firstMatchIndex = i;
                }
                else
                {
                    extensions.RemoveAt(i);
                    i--;
                }
            }

            if (enabled)
            {
                if (firstMatchIndex >= 0)
                {
                    return;
                }

                DefModExtension entry = CreateFeatureEntry(feature, def) as DefModExtension;
                if (entry != null)
                {
                    extensions.Add(entry);
                }
            }
            else if (firstMatchIndex >= 0)
            {
                for (int i = extensions.Count - 1; i >= 0; i--)
                {
                    DefModExtension extension = extensions[i];
                    if (extension != null && feature.EntryType.IsInstanceOfType(extension))
                    {
                        extensions.RemoveAt(i);
                    }
                }

                if (extensions.Count == 0)
                {
                    def.modExtensions = null;
                }
            }
        }

        private static void EnsureCompPresence(ThingDef def, RuntimeAnimalFeatureDefinition feature, bool enabled)
        {
            List<CompProperties> comps = def.comps;
            if (comps == null)
            {
                if (!enabled)
                {
                    return;
                }

                comps = new List<CompProperties>(1);
                def.comps = comps;
            }

            int firstMatchIndex = -1;
            for (int i = 0; i < comps.Count; i++)
            {
                CompProperties comp = comps[i];
                if (comp == null || !feature.EntryType.IsInstanceOfType(comp))
                {
                    continue;
                }

                if (firstMatchIndex < 0)
                {
                    firstMatchIndex = i;
                }
                else
                {
                    comps.RemoveAt(i);
                    i--;
                }
            }

            if (enabled)
            {
                if (firstMatchIndex >= 0)
                {
                    return;
                }

                CompProperties entry = CreateFeatureEntry(feature, def) as CompProperties;
                if (entry != null)
                {
                    comps.Add(entry);
                }
            }
            else if (firstMatchIndex >= 0)
            {
                for (int i = comps.Count - 1; i >= 0; i--)
                {
                    CompProperties comp = comps[i];
                    if (comp != null && feature.EntryType.IsInstanceOfType(comp))
                    {
                        comps.RemoveAt(i);
                    }
                }

                if (comps.Count == 0)
                {
                    def.comps = null;
                }
            }
        }

        private static object CreateFeatureEntry(RuntimeAnimalFeatureDefinition feature, ThingDef def)
        {
            if (featureDefaultEntryById.TryGetValue(feature.Id, out Dictionary<ThingDef, object> byDef)
                && byDef.TryGetValue(def, out object defaultEntry))
            {
                return CloneEntryObject(defaultEntry);
            }

            try
            {
                return Activator.CreateInstance(feature.EntryType);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Failed to create runtime feature entry '{feature.Id}' for '{def?.defName ?? "null"}': {ex}");
                return null;
            }
        }

        private static void BuildAnimalDefCache()
        {
            cachedAnimalDefs.Clear();
            raceDefaultsByDef.Clear();

            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            if (allDefs != null)
            {
                for (int i = 0; i < allDefs.Count; i++)
                {
                    ThingDef def = allDefs[i];
                    if (!ZoologyCacheUtility.IsAnimalThingDef(def))
                    {
                        continue;
                    }

                    cachedAnimalDefs.Add(def);
                    raceDefaultsByDef[def] = new RaceDefaults
                    {
                        Roamer = def.race.roamMtbDays.HasValue,
                        RoamMtbDays = def.race.roamMtbDays,
                        Trainability = def.race.trainability
                    };
                }
            }

            cachedAnimalDefs.Sort((left, right) => string.Compare(GetAnimalLabel(left), GetAnimalLabel(right), StringComparison.OrdinalIgnoreCase));
        }

        private static void BuildFeatureMetadata()
        {
            featureById.Clear();
            featureDefaultPresenceById.Clear();
            featureDefaultEntryById.Clear();

            for (int i = 0; i < allFeatures.Count; i++)
            {
                RuntimeAnimalFeatureDefinition feature = allFeatures[i];
                featureById[feature.Id] = feature;

                Dictionary<ThingDef, bool> presenceByDef = new Dictionary<ThingDef, bool>(cachedAnimalDefs.Count);
                Dictionary<ThingDef, object> defaultEntryByDef = new Dictionary<ThingDef, object>(64);

                for (int j = 0; j < cachedAnimalDefs.Count; j++)
                {
                    ThingDef def = cachedAnimalDefs[j];
                    bool present = TryGetFeatureEntryObject(def, feature, out object entry);
                    presenceByDef[def] = present;
                    if (present && entry != null)
                    {
                        object clone = CloneEntryObject(entry);
                        if (clone != null)
                        {
                            defaultEntryByDef[def] = clone;
                        }
                    }
                }

                featureDefaultPresenceById[feature.Id] = presenceByDef;
                featureDefaultEntryById[feature.Id] = defaultEntryByDef;
            }
        }

        private static bool TryGetFeatureEntryObject(ThingDef def, RuntimeAnimalFeatureDefinition feature, out object entry)
        {
            entry = null;
            if (def == null || feature == null)
            {
                return false;
            }

            if (feature.Kind == RuntimeAnimalFeatureKind.ModExtension)
            {
                List<DefModExtension> extensions = def.modExtensions;
                if (extensions == null)
                {
                    return false;
                }

                for (int i = 0; i < extensions.Count; i++)
                {
                    DefModExtension extension = extensions[i];
                    if (extension != null && feature.EntryType.IsInstanceOfType(extension))
                    {
                        entry = extension;
                        return true;
                    }
                }

                return false;
            }

            List<CompProperties> comps = def.comps;
            if (comps == null)
            {
                return false;
            }

            for (int i = 0; i < comps.Count; i++)
            {
                CompProperties comp = comps[i];
                if (comp != null && feature.EntryType.IsInstanceOfType(comp))
                {
                    entry = comp;
                    return true;
                }
            }

            return false;
        }

        private static object CloneEntryObject(object source)
        {
            if (source == null)
            {
                return null;
            }

            Type sourceType = source.GetType();
            object clone;
            try
            {
                clone = Activator.CreateInstance(sourceType);
            }
            catch
            {
                return null;
            }

            CopyInstanceFields(sourceType, source, clone);
            return clone;
        }

        private static void CopyInstanceFields(Type type, object source, object destination)
        {
            Type current = type;
            while (current != null && current != typeof(object))
            {
                FieldInfo[] fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic || field.IsInitOnly)
                    {
                        continue;
                    }

                    object value = field.GetValue(source);
                    field.SetValue(destination, CloneFieldValue(value));
                }

                current = current.BaseType;
            }
        }

        private static object CloneFieldValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            Type valueType = value.GetType();
            if (valueType.IsValueType || valueType == typeof(string))
            {
                return value;
            }

            if (value is Array arrayValue)
            {
                return arrayValue.Clone();
            }

            if (value is IDictionary dictionaryValue)
            {
                IDictionary dictionaryClone = TryCreateDictionaryClone(dictionaryValue);
                return dictionaryClone ?? value;
            }

            if (value is IList listValue)
            {
                IList listClone = TryCreateListClone(listValue);
                return listClone ?? value;
            }

            return value;
        }

        private static IDictionary TryCreateDictionaryClone(IDictionary source)
        {
            if (source == null)
            {
                return null;
            }

            IDictionary clone;
            try
            {
                clone = Activator.CreateInstance(source.GetType()) as IDictionary;
            }
            catch
            {
                return null;
            }

            if (clone == null)
            {
                return null;
            }

            foreach (DictionaryEntry entry in source)
            {
                clone[entry.Key] = entry.Value;
            }

            return clone;
        }

        private static IList TryCreateListClone(IList source)
        {
            if (source == null)
            {
                return null;
            }

            IList clone;
            try
            {
                clone = Activator.CreateInstance(source.GetType()) as IList;
            }
            catch
            {
                return null;
            }

            if (clone == null)
            {
                return null;
            }

            for (int i = 0; i < source.Count; i++)
            {
                clone.Add(source[i]);
            }

            return clone;
        }

        private static void InvalidateCachesAfterApply()
        {
            DefModExtensionCache<ModExtensiom_Chlidcare>.Clear();
            DefModExtensionCache<ModExtension_AgroAtSlaughter>.Clear();
            DefModExtensionCache<ModExtension_CannotBeMutated>.Clear();
            DefModExtensionCache<ModExtension_CannotBeAugmented>.Clear();
            DefModExtensionCache<ModExtension_CannotChew>.Clear();
            DefModExtensionCache<ModExtension_Ectothermic>.Clear();
            DefModExtensionCache<ModExtension_IsMammal>.Clear();
            DefModExtensionCache<ModExtension_IsScavenger>.Clear();
            DefModExtensionCache<ModExtension_NoFlee>.Clear();
            DefModExtensionCache<ModExtension_NoPorcupineQuill>.Clear();
            DefModExtensionCache<ModExtension_FleeFromCarrier>.Clear();

            ZoologyCacheUtility.ClearCaches();
            CannotChewPresenceCache.RebuildFromCurrentMaps();
        }

        private static void TryAddTrainability(List<TrainabilityDef> defs, string defName)
        {
            if (defs == null || string.IsNullOrEmpty(defName))
            {
                return;
            }

            TrainabilityDef trainability = DefDatabase<TrainabilityDef>.GetNamedSilentFail(defName);
            if (trainability == null || defs.Contains(trainability))
            {
                return;
            }

            defs.Add(trainability);
        }

        private static string GetAnimalLabel(ThingDef def)
        {
            if (def == null)
            {
                return "Unknown";
            }

            return def.label.NullOrEmpty() ? def.defName : def.LabelCap.RawText;
        }
    }
}
