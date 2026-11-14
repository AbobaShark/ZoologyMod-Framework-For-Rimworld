using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // All adult numbers are exported by Checker; no pawn comp, mass formula,
    // mutable LifeStageDef or second biological-age schedule is introduced.
    public sealed class ZoologySexAdultProfileCatalogDef : Def
    {
        public List<ZoologySexAdultProfile> profiles = new List<ZoologySexAdultProfile>();
    }

    public sealed class ZoologySexAdultProfile
    {
        public string animalDefName;
        public string sourceStageDefName;
        public string sex;
        public float adultMass = -1f;
        public float baselineMass = -1f;
        public float bodySizeFactor = 1f;
        public float healthScaleFactor = 1f;
        public float hungerRateFactor = 1f;
        // Exported only for species whose canonical Base stats explicitly
        // enables sex-specific market pricing.
        public bool useSexSpecificMarketValue;
        public float marketValueFactor = 1f;
        public float combatPowerFactor = 1f;
        public float ceArmorDurabilityFactor = 1f;
        public List<ZoologyLifeStageStatFactor> statFactors;
        public List<ZoologyLifeStageStatOffset> statOffsets;
        public List<ZoologyLifeStageToolFactor> tools;
    }

    internal static class ZoologySexAdultProfiles
    {
        // Cache successful AND failed ToolCE-copy mappings. A missing
        // anatomical match must not trigger another full Tool comparison
        // on every attack.
        private sealed class ToolResolution
        {
            internal ZoologyLifeStageToolFactor factor;
        }

        private sealed class Endpoint
        {
            internal ZoologySexAdultProfile profile;
            internal readonly Dictionary<StatDef, float> stats = new Dictionary<StatDef, float>();
            internal readonly Dictionary<Tool, ZoologyLifeStageToolFactor> tools =
                new Dictionary<Tool, ZoologyLifeStageToolFactor>();
            internal readonly ConditionalWeakTable<Tool, ToolResolution> resolvedCopies =
                new ConditionalWeakTable<Tool, ToolResolution>();
            internal float sharpOffset;
            internal float bluntOffset;
        }

        private sealed class SexPair
        {
            internal Endpoint male;
            internal Endpoint female;
            internal int stageIndex = -1;

            internal Endpoint For(Gender gender)
            {
                return gender == Gender.Male ? male
                    : gender == Gender.Female ? female : null;
            }
        }

        private static readonly Dictionary<ThingDef, SexPair> ByRace =
            new Dictionary<ThingDef, SexPair>();
        private static bool installed;
        internal static int GraphicVersion => ZoologyAnimalGraphics.GraphicVersion;

        internal static bool Enabled
        {
            get
            {
                var settings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
                return settings != null && !settings.DisableAllRuntimePatches
                    && settings.EnableSexualDimorphism;
            }
        }

        internal static void Install()
        {
            if (installed)
                return;
            installed = true;
            var catalogs = DefDatabase<ZoologySexAdultProfileCatalogDef>.AllDefsListForReading;
            var affected = new HashSet<StatDef>();
            bool offsets = false;
            bool ce = CEChecker.IsCEInstalled();
            int loaded = 0;
            foreach (var catalog in catalogs)
            {
                if (catalog?.profiles == null)
                    continue;
                foreach (var record in catalog.profiles)
                {
                    if (record == null || string.IsNullOrEmpty(record.animalDefName)
                        || (record.sex != "Male" && record.sex != "Female"))
                        continue;
                    var race = DefDatabase<ThingDef>.GetNamedSilentFail(record.animalDefName);
                    if (race?.race?.Animal != true)
                        continue;
                    SexPair pair;
                    if (!ByRace.TryGetValue(race, out pair))
                    {
                        pair = new SexPair();
                        ByRace.Add(race, pair);
                    }
                    var entry = new Endpoint { profile = record };
                    if (record.sex == "Male")
                    {
                        if (pair.male != null)
                            throw new InvalidOperationException("Duplicate male endpoint: " + race.defName);
                        pair.male = entry;
                    }
                    else
                    {
                        if (pair.female != null)
                            throw new InvalidOperationException("Duplicate female endpoint: " + race.defName);
                        pair.female = entry;
                    }

                    if (record.statFactors != null)
                    {
                        var ceOverrides = new HashSet<string>(StringComparer.Ordinal);
                        if (ce)
                            foreach (var stat in record.statFactors)
                                if (stat != null && stat.ceOnly)
                                    ceOverrides.Add(stat.stat);
                        foreach (var stat in record.statFactors)
                        {
                            if (stat == null || string.IsNullOrEmpty(stat.stat)
                                || stat.ceOnly && !ce
                                || !stat.ceOnly && ce && ceOverrides.Contains(stat.stat))
                                continue;
                            var definition = DefDatabase<StatDef>.GetNamedSilentFail(stat.stat);
                            if (definition == null)
                                continue;
                            if (!Valid(stat.factor) || entry.stats.ContainsKey(definition))
                                throw new InvalidOperationException("Invalid or duplicate sexual stat: "
                                    + race.defName + "/" + stat.stat);
                            entry.stats.Add(definition, stat.factor);
                            affected.Add(definition);
                        }
                    }
                    if (record.statOffsets != null)
                        foreach (var offset in record.statOffsets)
                        {
                            if (offset == null || offset.ceOnly != ce)
                                continue;
                            if (!Valid(offset.offset))
                                throw new InvalidOperationException("Invalid sexual armor offset");
                            if (offset.stat == "ArmorRating_Sharp")
                                entry.sharpOffset = offset.offset;
                            else if (offset.stat == "ArmorRating_Blunt")
                                entry.bluntOffset = offset.offset;
                            else
                                throw new InvalidOperationException("Unsupported sexual offset " + offset.stat);
                            offsets |= offset.offset > 0f;
                        }
                    loaded++;
                }
            }

            foreach (var item in ByRace)
            {
                var pair = item.Value;
                if (pair.male == null || pair.female == null
                    || !string.Equals(pair.male.profile.sourceStageDefName,
                        pair.female.profile.sourceStageDefName, StringComparison.Ordinal))
                    throw new InvalidOperationException("Incomplete sex endpoints: " + item.Key.defName);
                pair.stageIndex = FindStageIndex(item.Key, pair.male.profile.sourceStageDefName);
                if (pair.stageIndex < 0)
                {
                    Log.Warning("[Zoology] Sexual endpoint stage not found: " + item.Key.defName);
                    continue;
                }
                ResolveTools(item.Key, pair.male, ce);
                ResolveTools(item.Key, pair.female, ce);
            }
            ZoologyAnimalGrowthStatPart.Install(affected);
            if (offsets)
                ZoologyLifeStageArmorStatPart.Install();
            ZoologyAnimalGraphics.Install();
            SyncGraphics(Enabled);
            Log.Message("[Zoology] Loaded " + loaded + " sex-specific adult endpoints for "
                + ByRace.Count + " animal races.");
        }

        internal static int FindStageIndex(ThingDef race, string sourceStages)
        {
            var ages = race.race?.lifeStageAges;
            if (ages == null)
                return -1;
            var names = sourceStages.Split(new[] { " or " }, StringSplitOptions.None);
            for (int i = 0; i < ages.Count; i++)
            {
                var stage = ages[i]?.def;
                var source = stage?.GetModExtension<ZoologyLifeStageOrigin>()?.sourceStage ?? stage;
                if (source == null)
                    continue;
                for (int n = 0; n < names.Length; n++)
                    if (source.defName == names[n]
                        || names[n] == "AnimalBaby" && source.defName == "AnimalBabyTiny")
                        return i;
            }
            return -1;
        }

        // Adult graphics use the existing mass endpoints. The youth scaler
        // deliberately never reads these factors.
        internal static bool TryGraphicSexScale(ThingDef race,
            out int stageIndex, out bool smallerFemale, out float scale)
        {
            stageIndex = -1;
            smallerFemale = false;
            scale = 1f;
            SexPair pair;
            if (race == null || !ByRace.TryGetValue(race, out pair)
                || pair.male?.profile == null || pair.female?.profile == null
                || pair.stageIndex < 0
                || !(pair.male.profile.adultMass > 0f)
                || !(pair.female.profile.adultMass > 0f))
                return false;
            stageIndex = pair.stageIndex;
            smallerFemale = pair.female.profile.adultMass
                < pair.male.profile.adultMass;
            double smaller = Math.Min(pair.male.profile.adultMass,
                pair.female.profile.adultMass);
            double larger = Math.Max(pair.male.profile.adultMass,
                pair.female.profile.adultMass);
            scale = (float)Math.Pow(smaller / larger, 1d / 6d);
            return scale < 1f - 0.000001f;
        }

        private static void ResolveTools(ThingDef race, Endpoint endpoint, bool ce)
        {
            var generated = endpoint.profile.tools;
            if (generated == null || race.tools == null)
                return;
            for (int i = 0; i < generated.Count; i++)
            {
                var factor = generated[i];
                if (factor == null)
                    continue;
                int index = ce ? factor.ceToolIndex : factor.vanillaToolIndex;
                if (index < 0)
                    continue;
                Tool tool = ZoologyLifeStageProfileInstaller.ResolveNaturalTool(
                    race.tools, factor, index, endpoint.tools);
                if (tool == null)
                {
                    Log.Warning("[Zoology] Unresolved sexual natural tool "
                        + race.defName + "/" + factor.label);
                    continue;
                }
                endpoint.tools.Add(tool, factor);
            }
        }

        private static Endpoint Select(Pawn pawn, bool next)
        {
            if (!Enabled || pawn?.ageTracker == null || pawn.def == null)
                return null;
            SexPair pair;
            if (!ByRace.TryGetValue(pawn.def, out pair))
                return null;
            int wanted = pawn.ageTracker.CurLifeStageIndex + (next ? 1 : 0);
            return wanted == pair.stageIndex ? pair.For(pawn.gender) : null;
        }

        internal static bool TryGet(Pawn pawn, bool next, out ZoologySexAdultProfile profile)
        {
            var selected = Select(pawn, next);
            profile = selected?.profile;
            return profile != null;
        }

        internal static bool TryGetStat(Pawn pawn, StatDef stat, bool next, out float factor)
        {
            factor = 1f;
            var selected = Select(pawn, next);
            return selected != null && stat != null && selected.stats.TryGetValue(stat, out factor);
        }

        internal static bool TryGetOffset(Pawn pawn, bool sharp, bool next, out float offset)
        {
            offset = 0f;
            var selected = Select(pawn, next);
            if (selected == null)
                return false;
            offset = sharp ? selected.sharpOffset : selected.bluntOffset;
            return true;
        }

        internal static bool TryGetTool(Pawn pawn, Tool tool, bool next,
            out ZoologyLifeStageToolFactor factor)
        {
            factor = null;
            var selected = Select(pawn, next);
            if (selected == null || tool == null)
                return false;
            if (selected.tools.TryGetValue(tool, out factor))
                return true;
            ToolResolution cached;
            if (selected.resolvedCopies.TryGetValue(tool, out cached))
            {
                factor = cached.factor;
                return factor != null;
            }

            // CE may create ToolCE copies. Resolve a copy once by anatomical
            // identity; subsequent combat queries are simple dictionary reads.
            ZoologyLifeStageToolFactor candidate = null;
            int best = 0;
            bool tied = false;
            foreach (var item in selected.tools)
            {
                int score = ZoologyLifeStageProfileInstaller.CompareActualTools(tool, item.Key);
                if (score > best)
                {
                    best = score;
                    candidate = item.Value;
                    tied = false;
                }
                else if (score > 0 && score == best)
                    tied = true;
            }
            factor = best > 0 && !tied ? candidate : null;
            selected.resolvedCopies.Add(tool, new ToolResolution { factor = factor });
            return factor != null;
        }

        internal static void SyncGraphics(bool enabled)
        {
            ZoologyAnimalGraphics.Sync(enabled, ZoologyAnimalGraphics.Enabled);
        }

        private static bool Valid(float value)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
