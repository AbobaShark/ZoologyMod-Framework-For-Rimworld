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

        private static readonly Dictionary<LifeStageDef, ZoologyLifeStageArmorOffsets>
            armorOffsetsByStage = new Dictionary<LifeStageDef, ZoologyLifeStageArmorOffsets>();
        private static readonly Dictionary<LifeStageDef, float> armorDurabilityByStage =
            new Dictionary<LifeStageDef, float>();
        private static readonly Dictionary<LifeStageDef, Dictionary<Tool, ZoologyLifeStageToolFactor>>
            resolvedToolCopiesByStage = new Dictionary<LifeStageDef, Dictionary<Tool, ZoologyLifeStageToolFactor>>();
        private static int unresolvedToolMappings;
        private static readonly List<string> unresolvedToolExamples = new List<string>();

        private static bool applied;
        private static int mappedNaturalTools;
        private static bool nativeHashAllocatorAvailable = true;
        private static HashSet<ushort> fallbackUsedHashes;
        private static bool warnedFallbackHashes;

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

            if (armorOffsetsByStage.Count != 0)
            {
                ZoologyLifeStageArmorStatPart.Install();
            }

            timer.Stop();
            if (unresolvedToolMappings > 0)
            {
                Log.Warning("[Zoology] " + unresolvedToolMappings
                    + " natural attacks could not be identified unambiguously in the loaded animal defs. "
                    + "Example species/stages: " + string.Join("; ", unresolvedToolExamples));
            }
            Log.Message("[Zoology] Installed " + installed + " species life stages, "
                + mappedNaturalTools + " individually mapped natural tools in "
                + timer.ElapsedMilliseconds + " ms (" + skipped
                + " invalid catalog entries; absent optional species and neutral stages are not errors).");
        }

        public static bool TryGetToolFactor(Pawn pawn, Tool tool, out ZoologyLifeStageToolFactor factor)
        {
            factor = null;
            if (pawn?.RaceProps?.Animal != true || tool == null)
                return false;

            var stage = pawn.ageTracker?.CurLifeStage;
            Dictionary<Tool, ZoologyLifeStageToolFactor> mapped;
            if (stage == null || !toolFactorsByStage.TryGetValue(stage, out mapped))
                return false;

            if (mapped.TryGetValue(tool, out factor))
                return true;

            // Some modded verbs construct a ToolCE copy instead of retaining
            // the ThingDef.tools reference. Resolve once, then cache the copy
            // so no string comparisons occur on subsequent attacks.
            Dictionary<Tool, ZoologyLifeStageToolFactor> copies;
            if (!resolvedToolCopiesByStage.TryGetValue(stage, out copies))
            {
                copies = new Dictionary<Tool, ZoologyLifeStageToolFactor>();
                resolvedToolCopiesByStage.Add(stage, copies);
            }
            if (copies.TryGetValue(tool, out factor))
                return factor != null;

            int bestScore = 0;
            int ties = 0;
            ZoologyLifeStageToolFactor winner = null;
            foreach (var mappedTool in mapped)
            {
                int score = CompareActualTools(tool, mappedTool.Key);
                if (score > bestScore)
                {
                    bestScore = score;
                    winner = mappedTool.Value;
                    ties = 1;
                }
                else if (score == bestScore && score > 0)
                {
                    ties++;
                }
            }
            factor = ties == 1 ? winner : null;
            copies.Add(tool, factor);
            return factor != null;
        }

        internal static bool TryGetArmorDurabilityFactor(Pawn pawn, out float factor)
        {
            factor = 1f;
            var stage = pawn?.ageTracker?.CurLifeStage;
            return stage != null && armorDurabilityByStage.TryGetValue(stage, out factor);
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
            if (IsFinitePositive(profile.bodySizeFactor)
                && Math.Abs(replacement.bodySizeFactor - profile.bodySizeFactor) > 0.000001f)
            {
                replacement.bodySizeFactor = profile.bodySizeFactor;
                changed = true;
            }
            if (IsFinitePositive(profile.healthScaleFactor)
                && Math.Abs(replacement.healthScaleFactor - profile.healthScaleFactor) > 0.000001f)
            {
                replacement.healthScaleFactor = profile.healthScaleFactor;
                changed = true;
            }
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

            // Absolute values are emitted only if the adult's corresponding
            // armor was zero. A multiplier cannot express 0 -> positive.
            // Select the numerical source matching the current combat mode.
            float sharpOffset = 0f;
            float bluntOffset = 0f;
            if (profile.statOffsets != null)
            {
                var seenArmor = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in profile.statOffsets)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.stat)
                        || !IsFiniteNonnegative(entry.offset))
                    {
                        Log.Warning("[Zoology] Invalid absolute life-stage armor offset for "
                            + animal.defName + "/" + original.defName + "; skipping stage.");
                        return false;
                    }
                    if (entry.ceOnly != hasCe)
                    {
                        continue;
                    }
                    if (!seenArmor.Add(entry.stat))
                    {
                        Log.Warning("[Zoology] Duplicate absolute life-stage armor offset for "
                            + animal.defName + "/" + original.defName);
                        return false;
                    }
                    if (entry.stat == "ArmorRating_Sharp")
                    {
                        if (DefDatabase<StatDef>.GetNamedSilentFail(entry.stat) == null)
                            return false;
                        sharpOffset = entry.offset;
                    }
                    else if (entry.stat == "ArmorRating_Blunt")
                    {
                        if (DefDatabase<StatDef>.GetNamedSilentFail(entry.stat) == null)
                            return false;
                        bluntOffset = entry.offset;
                    }
                    else
                    {
                        Log.Warning("[Zoology] Unsupported absolute armor stat: " + entry.stat);
                        return false;
                    }
                }
            }
            if (sharpOffset > 0f || bluntOffset > 0f)
            {
                changed = true;
            }

            if (hasCe && profile.ceArmorDurabilityFactor >= 0f)
            {
                if (!IsFinitePositive(profile.ceArmorDurabilityFactor))
                {
                    Log.Warning("[Zoology] Invalid CE armor durability factor for "
                        + animal.defName + "/" + original.defName);
                    return false;
                }
                if (Math.Abs(profile.ceArmorDurabilityFactor - 1f) > 0.000001f)
                    changed = true;
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
                        continue; // Attack does not exist in this combat mode.

                    Tool tool = ResolveNaturalTool(animal.tools, entry, toolIndex, selectedTools);
                    if (tool == null)
                    {
                        unresolvedToolMappings++;
                        if (unresolvedToolExamples.Count < 12)
                            unresolvedToolExamples.Add(animal.defName + "/" + original.defName
                                + " [" + entry.label + ", " + entry.linkedBodyPartsGroup + "]");
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
                        unresolvedToolMappings++;
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
            if (sharpOffset > 0f || bluntOffset > 0f)
            {
                armorOffsetsByStage.Add(replacement,
                    new ZoologyLifeStageArmorOffsets(sharpOffset, bluntOffset));
            }
            if (hasCe && IsFinitePositive(profile.ceArmorDurabilityFactor)
                && Math.Abs(profile.ceArmorDurabilityFactor - 1f) > 0.000001f)
            {
                armorDurabilityByStage.Add(replacement, profile.ceArmorDurabilityFactor);
            }
            if (selectedTools != null)
            {
                toolFactorsByStage.Add(replacement, selectedTools);
                mappedNaturalTools += selectedTools.Count;
            }
            return true;
        }

        internal static bool TryGetArmorOffset(LifeStageDef stage, bool sharp, out float value)
        {
            value = 0f;
            ZoologyLifeStageArmorOffsets entry;
            if (stage == null || !armorOffsetsByStage.TryGetValue(stage, out entry))
            {
                return false;
            }
            value = sharp ? entry.sharp : entry.blunt;
            return value > 0f;
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
            // Prefer the game's own allocator when its reflected contract is
            // available. On versions with a different private implementation,
            // use a deterministic, collision-checked hash over loaded stages.
            if (nativeHashAllocatorAvailable
                && GiveShortHashMethod != null && TakenHashesField != null)
            {
                try
                {
                    var perType = TakenHashesField.GetValue(null)
                        as Dictionary<Type, HashSet<ushort>>;
                    HashSet<ushort> taken;
                    if (perType != null
                        && perType.TryGetValue(typeof(LifeStageDef), out taken)
                        && taken != null)
                    {
                        GiveShortHashMethod.Invoke(null,
                            new object[] { stage, typeof(LifeStageDef), taken });
                        if (stage.shortHash != 0 && taken.Contains(stage.shortHash))
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[Zoology] Native LifeStageDef shortHash allocator failed: "
                        + ex.GetType().Name + "; using deterministic fallback.");
                }
            }
            nativeHashAllocatorAvailable = false;
            return AssignFallbackShortHash(stage);
        }

        private static bool AssignFallbackShortHash(LifeStageDef stage)
        {
            if (fallbackUsedHashes == null)
            {
                fallbackUsedHashes = new HashSet<ushort>();
                var loaded = DefDatabase<LifeStageDef>.AllDefsListForReading;
                for (int i = 0; i < loaded.Count; i++)
                {
                    var other = loaded[i];
                    if (other != null && other.shortHash != 0)
                    {
                        fallbackUsedHashes.Add(other.shortHash);
                    }
                }
            }

            if (!warnedFallbackHashes)
            {
                warnedFallbackHashes = true;
                Log.Warning("[Zoology] Using stable fallback short hashes for generated "
                    + "LifeStageDefs. Check compatibility with the installed RimWorld version.");
            }

            // FNV-1a over UTF-16 code units is stable across .NET runtimes;
            // string.GetHashCode is randomized and cannot be used for saved Defs.
            uint hash = 2166136261u;
            unchecked
            {
                string name = stage.defName ?? string.Empty;
                for (int i = 0; i < name.Length; i++)
                {
                    hash = (hash ^ name[i]) * 16777619u;
                }
            }
            ushort candidate = (ushort)(hash & 65535u);
            if (candidate == 0)
            {
                candidate = 1;
            }
            for (int attempts = 0; attempts < ushort.MaxValue; attempts++)
            {
                if (fallbackUsedHashes.Add(candidate))
                {
                    stage.shortHash = candidate;
                    return true;
                }
                candidate = candidate == ushort.MaxValue
                    ? (ushort)1 : (ushort)(candidate + 1);
            }
            Log.Error("[Zoology] All LifeStageDef short hashes are taken.");
            return false;
        }

        // Match by meaningful attack identity, not by the index produced by a
        // particular patch ordering. The recorded index is a last tie-breaker
        // only after a biological signature match.
        private static Tool ResolveNaturalTool(List<Tool> tools, ZoologyLifeStageToolFactor entry,
            int preferredIndex, Dictionary<Tool, ZoologyLifeStageToolFactor> used)
        {
            Tool winner = null;
            int best = 0;
            int ties = 0;
            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                if (tool == null || (used != null && used.ContainsKey(tool)))
                    continue;
                int score = MatchProfileToTool(tool, entry);
                if (score <= 0)
                    continue;
                if (i == preferredIndex)
                    score++;
                if (score > best)
                {
                    winner = tool;
                    best = score;
                    ties = 1;
                }
                else if (score == best)
                {
                    ties++;
                }
            }
            return ties == 1 ? winner : null;
        }

        private static int MatchProfileToTool(Tool tool, ZoologyLifeStageToolFactor entry)
        {
            bool group = !string.IsNullOrEmpty(entry.linkedBodyPartsGroup)
                && string.Equals(entry.linkedBodyPartsGroup,
                    tool.linkedBodyPartsGroup?.defName, StringComparison.OrdinalIgnoreCase);
            bool label = !string.IsNullOrEmpty(entry.label)
                && string.Equals(NormalizeToolLabel(entry.label),
                    NormalizeToolLabel(tool.label), StringComparison.OrdinalIgnoreCase);
            bool capacities = MatchesCapacities(entry.capacities, tool.capacities);
            if (!group && !(label && capacities))
                return 0;
            return (group ? 8 : 0) + (label ? 4 : 0) + (capacities ? 2 : 0);
        }

        private static int CompareActualTools(Tool first, Tool second)
        {
            if (first == null || second == null)
                return 0;
            bool group = first.linkedBodyPartsGroup != null
                && first.linkedBodyPartsGroup == second.linkedBodyPartsGroup;
            bool label = string.Equals(NormalizeToolLabel(first.label),
                NormalizeToolLabel(second.label), StringComparison.OrdinalIgnoreCase);
            bool capacities = SameCapacities(first.capacities, second.capacities);
            if (!group && !(label && capacities))
                return 0;
            return (group ? 8 : 0) + (label ? 4 : 0) + (capacities ? 2 : 0);
        }

        private static bool MatchesCapacities(string expected, List<PawnCapacityDef> actual)
        {
            if (string.IsNullOrEmpty(expected) || actual == null)
                return false;
            var components = expected.Split(',');
            if (components.Length != actual.Count)
                return false;
            for (int i = 0; i < components.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < actual.Count; j++)
                {
                    if (string.Equals(components[i].Trim(), actual[j]?.defName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return false;
            }
            return true;
        }

        private static bool SameCapacities(List<PawnCapacityDef> first, List<PawnCapacityDef> second)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;
            for (int i = 0; i < first.Count; i++)
                if (first[i] != null && !second.Contains(first[i]))
                    return false;
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
