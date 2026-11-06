using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Def-time work only. No per-tick scans, formula evaluation, or new pawn comps.
    internal static class ZoologyLifeStageProfileInstaller
    {
        private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
            "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        // RimWorld's allocator takes the already-allocated hash set as its third argument.
        private static readonly MethodInfo GiveShortHashMethod = typeof(ShortHashGiver).GetMethod(
            "GiveShortHash", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
            null, new[] { typeof(Def), typeof(Type), typeof(HashSet<ushort>) }, null);
        private static readonly FieldInfo TakenHashesField = typeof(ShortHashGiver).GetField(
            "takenHashesPerDeftype", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly Dictionary<LifeStageDef, Dictionary<Tool, ZoologyLifeStageToolFactor>>
            toolFactorsByStage = new Dictionary<LifeStageDef, Dictionary<Tool, ZoologyLifeStageToolFactor>>();

        private static bool applied;
        private static int mappedNaturalTools;

        public static void ApplyOnce()
        {
            if (applied)
            {
                return;
            }

            applied = true;
            var catalogs = DefDatabase<ZoologyLifeStageProfileCatalogDef>.AllDefsListForReading;
            if (catalogs.Count == 0)
            {
                return;
            }

            var timer = Stopwatch.StartNew();
            int installed = 0;
            int skipped = 0;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int c = 0; c < catalogs.Count; c++)
            {
                var catalog = catalogs[c];
                if (catalog?.stages == null)
                {
                    continue;
                }

                for (int i = 0; i < catalog.stages.Count; i++)
                {
                    var profile = catalog.stages[i];
                    if (profile == null || string.IsNullOrEmpty(profile.animalDefName)
                        || string.IsNullOrEmpty(profile.sourceStageDefName))
                    {
                        skipped++;
                        continue;
                    }

                    string key = profile.animalDefName + "\n" + profile.sourceStageDefName;
                    if (!keys.Add(key))
                    {
                        Log.Warning("[Zoology] Duplicate generated life-stage profile: " + key.Replace('\n', '/'));
                        skipped++;
                        continue;
                    }

                    try
                    {
                        if (TryInstall(profile))
                        {
                            installed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        Log.Warning("[Zoology] Cannot install life-stage profile "
                            + key.Replace('\n', '/') + ": " + ex);
                    }
                }
            }

            timer.Stop();
            Log.Message("[Zoology] Installed " + installed + " species life stages, "
                + mappedNaturalTools + " individually mapped natural tools in "
                + timer.ElapsedMilliseconds + " ms (" + skipped
                + " invalid catalog entries; absent optional species and neutral stages are not errors).");
        }

        public static bool TryGetToolFactor(Pawn pawn, Tool tool, out ZoologyLifeStageToolFactor factor)
        {
            factor = null;
            if (pawn == null || tool == null || pawn.RaceProps?.Animal != true)
            {
                return false;
            }

            var stage = pawn.ageTracker?.CurLifeStage;
            Dictionary<Tool, ZoologyLifeStageToolFactor> byTool;
            return stage != null
                && toolFactorsByStage.TryGetValue(stage, out byTool)
                && byTool.TryGetValue(tool, out factor);
        }

        private static bool TryInstall(ZoologyLifeStageProfile profile)
        {
            var animal = DefDatabase<ThingDef>.GetNamedSilentFail(profile.animalDefName);
            if (animal?.race?.Animal != true || animal.race.lifeStageAges == null)
            {
                return false; // Optional mod absent, or not an animal.
            }

            if (profile.expectedBaseBodySize > 0f
                && Math.Abs(animal.race.baseBodySize - profile.expectedBaseBodySize) > 0.001f)
            {
                Log.Warning("[Zoology] " + animal.defName
                    + ": BodySize differs from the generated AnimalStats profile; skipping age scaling.");
                return false;
            }

            if (profile.expectedBaseHungerRate > 0f
                && Math.Abs(animal.race.baseHungerRate - profile.expectedBaseHungerRate) > 0.001f)
            {
                Log.Warning("[Zoology] " + animal.defName
                    + ": hunger baseline differs from AnimalStats; skipping age scaling.");
                return false;
            }

            var ages = animal.race.lifeStageAges;
            int stageIndex = -1;
            LifeStageDef original = null;
            for (int i = 0; i < ages.Count; i++)
            {
                var current = ages[i]?.def;
                if (current == null)
                {
                    continue;
                }

                var provenance = current.GetModExtension<ZoologyLifeStageOrigin>();
                var source = provenance?.sourceStage ?? current;
                if (string.Equals(source.defName, profile.sourceStageDefName, StringComparison.Ordinal))
                {
                    stageIndex = i;
                    original = source;
                    break;
                }
            }

            if (original == null)
            {
                return false; // Do not manufacture stages the race does not use.
            }

            if (profile.expectedBodySizeFactor > 0f
                && Math.Abs(original.bodySizeFactor - profile.expectedBodySizeFactor) > 0.0001f)
            {
                Log.Warning("[Zoology] " + animal.defName + "/" + original.defName
                    + ": life-stage bodySizeFactor changed after Checker generation.");
                return false;
            }

            string generatedName = "ZoologyStage_" + animal.defName + "_" + original.defName;
            if (DefDatabase<LifeStageDef>.GetNamedSilentFail(generatedName) != null)
            {
                Log.Warning("[Zoology] Duplicate generated life-stage DefName: " + generatedName);
                return false;
            }

            var replacement = ShallowClone(original);
            replacement.defName = generatedName;
            replacement.generated = true;
            replacement.shortHash = 0;
            replacement.index = ushort.MaxValue;
            replacement.statFactors = new List<StatModifier>();
            if (original.statFactors != null)
            {
                for (int i = 0; i < original.statFactors.Count; i++)
                {
                    var modifier = original.statFactors[i];
                    if (modifier != null)
                    {
                        replacement.statFactors.Add(new StatModifier
                        {
                            stat = modifier.stat,
                            value = modifier.value
                        });
                    }
                }
            }

            replacement.modExtensions = original.modExtensions == null
                ? new List<DefModExtension>()
                : new List<DefModExtension>(original.modExtensions);
            replacement.modExtensions.Add(new ZoologyLifeStageOrigin { sourceStage = original });

            bool changed = false;
            if (IsFinitePositive(profile.combatPowerFactor))
            {
                var existingCombat = replacement.GetModExtension<LifeStageCombatPowerExtension>();
                if (existingCombat == null
                    || Math.Abs(existingCombat.combatPowerFactor - profile.combatPowerFactor) > 0.000001f)
                {
                    // Clone the extension instead of modifying the source Def's
                    // shared instance (MemberwiseClone does not deep-copy it).
                    if (existingCombat != null)
                    {
                        replacement.modExtensions.Remove(existingCombat);
                    }
                    replacement.modExtensions.Add(new LifeStageCombatPowerExtension
                    {
                        combatPowerFactor = profile.combatPowerFactor
                    });
                    changed = true;
                }
            }
            if (IsFinitePositive(profile.hungerRateFactor)
                && Math.Abs(replacement.hungerRateFactor - profile.hungerRateFactor) > 0.000001f)
            {
                replacement.hungerRateFactor = profile.hungerRateFactor;
                changed = true;
            }
            if (IsFinitePositive(profile.marketValueFactor)
                && Math.Abs(replacement.marketValueFactor - profile.marketValueFactor) > 0.000001f)
            {
                replacement.marketValueFactor = profile.marketValueFactor;
                changed = true;
            }

            bool hasCe = CEChecker.IsCEInstalled();
            if (profile.statFactors != null)
            {
                foreach (var entry in profile.statFactors)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.stat)
                        || entry.ceOnly && !hasCe || !IsFiniteNonnegative(entry.factor))
                    {
                        continue;
                    }

                    var stat = DefDatabase<StatDef>.GetNamedSilentFail(entry.stat);
                    if (stat == null)
                    {
                        continue;
                    }

                    StatModifier existing = null;
                    for (int i = 0; i < replacement.statFactors.Count; i++)
                    {
                        if (replacement.statFactors[i].stat == stat)
                        {
                            existing = replacement.statFactors[i];
                            break;
                        }
                    }

                    float oldValue = existing?.value ?? 1f;
                    if (Math.Abs(oldValue - entry.factor) < 0.000001f)
                    {
                        continue;
                    }

                    if (existing != null)
                    {
                        existing.value = entry.factor;
                    }
                    else
                    {
                        replacement.statFactors.Add(new StatModifier { stat = stat, value = entry.factor });
                    }
                    changed = true;
                }
            }

            Dictionary<Tool, ZoologyLifeStageToolFactor> selectedTools = null;
            // Checker compiles exact per-Tool coefficients into this generated
            // source file. The XML catalog controls which life stages exist,
            // while the compiled registry owns the independent attack values.
            var generatedTools = ZoologyGeneratedLifeStageAttacks.For(
                profile.animalDefName, profile.sourceStageDefName);
            if (generatedTools != null && animal.tools != null)
            {
                for (int i = 0; i < generatedTools.Count; i++)
                {
                    var entry = generatedTools[i];
                    if (entry == null || !IsValidToolFactor(entry))
                    {
                        continue;
                    }

                    int toolIndex = hasCe ? entry.ceToolIndex : entry.vanillaToolIndex;
                    if (toolIndex < 0)
                    {
                        continue; // Attack is absent in the selected mode.
                    }
                    if (toolIndex >= animal.tools.Count)
                    {
                        Log.Warning("[Zoology] " + animal.defName + "/" + original.defName
                            + ": tool #" + toolIndex + " is out of range; skipping it.");
                        continue;
                    }
                    Tool tool = animal.tools[toolIndex];
                    if (!MatchesToolSignature(tool, entry))
                    {
                        Log.Warning("[Zoology] " + animal.defName + "/" + original.defName
                            + ": tool #" + toolIndex + " differs from Checker; skipping it.");
                        continue;
                    }
                    if (!entry.HasChanges())
                    {
                        continue;
                    }

                    if (selectedTools == null)
                    {
                        selectedTools = new Dictionary<Tool, ZoologyLifeStageToolFactor>();
                    }
                    if (selectedTools.ContainsKey(tool))
                    {
                        Log.Warning("[Zoology] Duplicate tool modifier " + animal.defName
                            + "/" + original.defName + " #" + toolIndex);
                        continue;
                    }
                    selectedTools.Add(tool, entry);
                    changed = true;
                }
            }

            if (!changed)
            {
                return false; // Keep the vanilla def reference when nothing changes.
            }

            var copiedAges = new List<LifeStageAge>(ages);
            var copiedAge = ShallowClone(ages[stageIndex]);
            copiedAge.def = replacement;
            copiedAges[stageIndex] = copiedAge;

            // Do not expose a new Def to a race unless it has a valid hash.
            int nextIndex = DefDatabase<LifeStageDef>.AllDefsListForReading.Count;
            if (nextIndex >= ushort.MaxValue)
            {
                Log.Warning("[Zoology] LifeStageDef database index capacity exhausted.");
                return false;
            }
            replacement.index = (ushort)nextIndex;
            if (!AssignShortHash(replacement))
            {
                Log.Error("[Zoology] Cannot register life-stage shortHash for " + generatedName
                    + "; retaining the original stage.");
                return false;
            }
            DefDatabase<LifeStageDef>.Add(replacement);
            animal.race.lifeStageAges = copiedAges;
            if (selectedTools != null)
            {
                toolFactorsByStage.Add(replacement, selectedTools);
                mappedNaturalTools += selectedTools.Count;
            }
            return true;
        }

        private static T ShallowClone<T>(T source) where T : class
        {
            if (MemberwiseCloneMethod == null)
            {
                throw new MissingMethodException("System.Object.MemberwiseClone");
            }
            return (T)MemberwiseCloneMethod.Invoke(source, null);
        }

        private static bool AssignShortHash(LifeStageDef stage)
        {
            if (GiveShortHashMethod == null || TakenHashesField == null)
            {
                Log.Error("[Zoology] RimWorld's ShortHashGiver signature changed.");
                return false;
            }
            try
            {
                var byType = TakenHashesField.GetValue(null)
                    as Dictionary<Type, HashSet<ushort>>;
                HashSet<ushort> taken;
                if (byType == null || !byType.TryGetValue(typeof(LifeStageDef), out taken)
                    || taken == null)
                {
                    Log.Error("[Zoology] LifeStageDef shortHash registry is unavailable.");
                    return false;
                }
                GiveShortHashMethod.Invoke(null,
                    new object[] { stage, typeof(LifeStageDef), taken });
                return stage.shortHash != 0 && taken.Contains(stage.shortHash);
            }
            catch (Exception ex)
            {
                Log.Warning("[Zoology] Cannot assign short hash for " + stage.defName + ": " + ex);
                return false;
            }
        }

        private static bool MatchesToolSignature(Tool tool, ZoologyLifeStageToolFactor entry)
        {
            if (tool == null || entry == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(entry.label)
                && !string.Equals(NormalizeToolLabel(entry.label), NormalizeToolLabel(tool.label),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(entry.linkedBodyPartsGroup)
                && !string.Equals(entry.linkedBodyPartsGroup, tool.linkedBodyPartsGroup?.defName,
                    StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(entry.capacities))
            {
                var expected = entry.capacities.Split(',');
                if (tool.capacities == null || tool.capacities.Count != expected.Length)
                {
                    return false;
                }
                for (int i = 0; i < expected.Length; i++)
                {
                    if (!string.Equals(expected[i].Trim(), tool.capacities[i]?.defName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static string NormalizeToolLabel(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty
                : string.Join(" ", value.Split(
                    new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool IsFiniteNonnegative(float value)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsValidToolFactor(ZoologyLifeStageToolFactor factor)
        {
            return IsFiniteNonnegative(factor.vanillaDamageFactor)
                && IsFinitePositive(factor.vanillaCooldownFactor)
                && IsFiniteNonnegative(factor.ceDamageFactor)
                && IsFinitePositive(factor.ceCooldownFactor)
                && IsFiniteNonnegative(factor.ceSharpPenetrationFactor)
                && IsFiniteNonnegative(factor.ceBluntPenetrationFactor);
        }
    }
}
