using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public sealed class ZoologyEnergeticsCatalogDef : Def
    {
        public List<ZoologyReproductiveProfile> profiles = new List<ZoologyReproductiveProfile>();
    }

    public sealed class ZoologyReproductiveProfile
    {
        public string animalDefName;
        public float expectedBaseHungerRate;
        public float growthExponent;
        public float growthEfficiency;
        public float gestationDays;
        public float gestationBudget;
        public float meanLitter;
        public float meanClutch;
        public float formationNutritionPerOvum;
        public float femaleBaseFeed;
        public float pregnancyStage1;
        public float pregnancyStage2;
        public float pregnancyStage3;
        public float eggIntervalDays;
        public float clutchBudget;
        public float captiveClutchBudget = -1f;
        public float gravidExtra;
        public float eggNutrition;
        public bool aquatic;

        internal float PregnancyExtra(int stage)
        {
            return stage == 0 ? pregnancyStage1 : stage == 1 ? pregnancyStage2 : pregnancyStage3;
        }
    }

    internal static class ZoologyReproductiveEnergetics
    {
        private sealed class EggStat
        {
            internal ThingDef def;
            internal StatModifier modifier;
            internal float previous;
            internal float nutrition;
            internal bool inserted;
        }

        private static readonly Dictionary<ThingDef, ZoologyReproductiveProfile> Profiles =
            new Dictionary<ThingDef, ZoologyReproductiveProfile>();
        private static readonly List<EggStat> EggStats = new List<EggStat>();
        private static AccessTools.FieldRef<CompEggLayer, float> progressRef;
        private static Func<CompEggLayer, bool> eggLayerActive;
        private static Func<CompEggLayer, bool> eggLayerStopped;
        private static HediffDef gravidDef;
        private static bool installed;
        private static bool eggsApplied;
        private static bool initializedSettings;
        private static bool attemptedProgressBinding;
        private static float pregnancyNormalization = 1f;
        private static bool validPregnancyStages;

        internal static bool CanReadEggProgress
        {
            get
            {
                if (attemptedProgressBinding) return progressRef != null;
                attemptedProgressBinding = true;
                FieldInfo progressField = AccessTools.Field(typeof(CompEggLayer), "eggProgress");
                if (progressField == null || progressField.FieldType != typeof(float)) return false;
                progressRef = AccessTools.FieldRefAccess<CompEggLayer, float>("eggProgress");
                eggLayerActive = BindEggPredicate("Active");
                eggLayerStopped = BindEggPredicate("ProgressStoppedBecauseUnfertilized");
                if (eggLayerActive == null || eggLayerStopped == null)
                    Log.WarningOnce("[Zoology] Native egg development eligibility is unavailable; clutch energy costs cannot be applied safely.", 192751485);
                return true;
            }
        }

        private static Func<CompEggLayer, bool> BindEggPredicate(string property)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(CompEggLayer), property);
            if (getter == null || getter.ReturnType != typeof(bool)) return null;
            return (Func<CompEggLayer, bool>)Delegate.CreateDelegate(typeof(Func<CompEggLayer, bool>), getter, false);
        }

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            gravidDef = DefDatabase<HediffDef>.GetNamedSilentFail("Zoology_Gravid");
            var eggValues = new Dictionary<ThingDef, float>();
            var conflictingEggs = new HashSet<ThingDef>();
            foreach (var catalog in DefDatabase<ZoologyEnergeticsCatalogDef>.AllDefsListForReading)
            {
                foreach (var profile in catalog.profiles)
                {
                    ThingDef race = DefDatabase<ThingDef>.GetNamedSilentFail(profile.animalDefName);
                    if (race?.race?.Animal != true) continue;
                    if (Math.Abs(race.race.baseHungerRate - profile.expectedBaseHungerRate) > 0.001f)
                    {
                        Log.Warning("[Zoology] Energetics baseline differs for " + race.defName);
                        continue;
                    }
                    if (Profiles.ContainsKey(race))
                    {
                        Log.Warning("[Zoology] Duplicate energetics profile: " + race.defName);
                        continue;
                    }
                    if (profile.eggIntervalDays > 0f)
                    {
                        var eggs = race.GetCompProperties<CompProperties_EggLayer>();
                        if (eggs == null)
                        {
                            Log.Warning("[Zoology] No supported CompEggLayer for " + race.defName);
                            continue;
                        }
                        float interval = AquaticEggLayerUtility.InstalledProfile(race)?.eggLayIntervalDays
                            ?? eggs.eggLayIntervalDays;
                        if (Math.Abs(interval - profile.eggIntervalDays) > 0.05f)
                        {
                            Log.Warning("[Zoology] Egg interval differs for " + race.defName);
                            continue;
                        }
                        if (!profile.aquatic && profile.eggNutrition > 0f)
                        {
                            RegisterEgg(eggs.eggUnfertilizedDef, profile.eggNutrition, eggValues, conflictingEggs);
                            RegisterEgg(eggs.eggFertilizedDef, profile.eggNutrition, eggValues, conflictingEggs);
                        }
                    }
                    Profiles.Add(race, profile);
                }
            }
            foreach (var pair in eggValues)
            {
                if (conflictingEggs.Contains(pair.Key))
                {
                    Log.Warning("[Zoology] Conflicting nutrition for shared egg " + pair.Key.defName);
                    continue;
                }
                ThingDef def = pair.Key;
                if (def.statBases == null) def.statBases = new List<StatModifier>();
                StatModifier stat = def.statBases.Find(s => s.stat == StatDefOf.Nutrition);
                bool inserted = stat == null;
                if (inserted) stat = new StatModifier { stat = StatDefOf.Nutrition };
                EggStats.Add(new EggStat { def = def, modifier = stat, previous = stat.value,
                    inserted = inserted, nutrition = pair.Value });
            }
            var stages = HediffDefOf.Pregnant?.stages;
            if (stages != null && stages.Count == 3)
            {
                float first = stages[1].minSeverity;
                float second = stages[2].minSeverity;
                float weighted = first + 3f * (second - first) + 5f * (1f - second);
                validPregnancyStages = first >= 0f && second >= first && second <= 1f && weighted > 0f;
                if (validPregnancyStages) pregnancyNormalization = 3f / weighted;
            }
            if (!CanReadEggProgress)
                Log.Warning("[Zoology] CompEggLayer progress is unavailable; gravid metabolism is disabled.");
            SyncSettings();
        }

        private static void RegisterEgg(ThingDef egg, float nutrition,
            Dictionary<ThingDef, float> values, HashSet<ThingDef> conflicts)
        {
            if (egg == null) return;
            if (values.TryGetValue(egg, out float previous))
            {
                if (Math.Abs(previous - nutrition) > 0.00001f) conflicts.Add(egg);
            }
            else values.Add(egg, nutrition);
        }

        internal static void SyncSettings(bool? enabledOverride = null)
        {
            if (!installed) return;
            bool enabled = enabledOverride ?? ModConstants.AdvancedReproduction;
            if (initializedSettings && enabled == eggsApplied) return;
            initializedSettings = true;
            eggsApplied = enabled;
            foreach (EggStat stat in EggStats)
            {
                if (enabled)
                {
                    if (stat.inserted && !stat.def.statBases.Contains(stat.modifier))
                        stat.def.statBases.Add(stat.modifier);
                    stat.modifier.value = stat.nutrition;
                }
                else if (stat.inserted) stat.def.statBases.Remove(stat.modifier);
                else stat.modifier.value = stat.previous;
            }
        }

        private const float ReproductionBaseFeedShare = 0.5f;

        private static float ExtraProductionBudget(float grossBudget, float ordinaryFeedPerDay, float days) =>
            Math.Max(0f, grossBudget - ReproductionBaseFeedShare * ordinaryFeedPerDay * days);

        private static float ClutchBudget(Pawn pawn, ZoologyReproductiveProfile profile)
        {
            if (profile.meanClutch > 0f && profile.formationNutritionPerOvum > 0f
                && pawn.TryGetComp<CompEggLayer>() is CompEggLayer layer)
            {
                float count = AnimalReproductionProfiles.ClutchMean(pawn, profile.meanClutch);
                float days = AnimalSeasonalReproduction.EggFormationDays(layer);
                return ExtraProductionBudget(count * profile.formationNutritionPerOvum,
                    profile.femaleBaseFeed, days);
            }
            return AnimalSeasonalReproduction.AcceleratedCaptive(pawn) && profile.captiveClutchBudget >= 0f
                ? profile.captiveClutchBudget : profile.clutchBudget;
        }

        private static bool TryProfile(Pawn pawn, out ZoologyReproductiveProfile profile)
        {
            profile = null;
            return ModConstants.AdvancedReproduction && pawn?.RaceProps?.Animal == true
                && pawn.gender != Gender.Male
                && Profiles.TryGetValue(pawn.def, out profile)
                && (pawn.gender == Gender.Female || profile.eggIntervalDays > 0f);
        }

        internal static float EggProgress(CompEggLayer layer) => progressRef == null ? 0f : progressRef(layer);

        internal static float WriteEggProgress(CompEggLayer layer, float value)
        {
            ref float progress = ref progressRef(layer);
            float previous = progress;
            progress = value;
            return previous;
        }

        private static bool Developing(CompEggLayer layer)
        {
            if (layer == null || progressRef == null) return false;
            float progress = progressRef(layer);
            return progress > 0.5f && progress < 1f;
        }

        internal static bool CarriesEggs(Pawn pawn)
        {
            return TryProfile(pawn, out var profile)
                && Developing(pawn.TryGetComp<CompEggLayer>()) && ClutchBudget(pawn, profile) > 0f;
        }

        internal static void SyncGravid(CompEggLayer layer)
        {
            Pawn pawn = layer.parent as Pawn;
            if (!TryProfile(pawn, out var profile)
                || progressRef == null || gravidDef == null || pawn.health == null) return;
            bool developing = Developing(layer) && ClutchBudget(pawn, profile) > 0f;
            Hediff marker = pawn.health.hediffSet.GetFirstHediffOfDef(gravidDef);
            if (developing && marker == null) pawn.health.AddHediff(gravidDef);
            else if (!developing && marker != null) pawn.health.RemoveHediff(marker);
        }

        private static float ExtraPerDay(Hediff hediff, ZoologyReproductiveProfile profile)
        {
            if (hediff == null) return 0f;
            Pawn pawn = hediff.pawn;
            if (hediff.def == HediffDefOf.Pregnant && profile.gestationBudget > 0f)
            {
                if (AnimalSeasonalReproduction.InDiapause(hediff)) return 0f;
                float actualDays = AnimalSeasonalReproduction.GestationDays(pawn);
                if (actualDays <= 0f) return 0f;
                float countRatio = profile.meanLitter > 0f
                    ? AnimalReproductionProfiles.LitterMean(pawn, profile.meanLitter) / profile.meanLitter : 1f;
                float budget = ExtraProductionBudget(countRatio * profile.gestationBudget,
                    BaselinePerDay(pawn, includeGrowthExtra: false), actualDays);
                if (budget <= 0f) return 0f;
                if (!validPregnancyStages) return budget / actualDays;
                return budget / profile.gestationBudget * profile.PregnancyExtra(hediff.CurStageIndex)
                    * pregnancyNormalization * profile.gestationDays / actualDays;
            }
            if (hediff.def == gravidDef && profile.eggIntervalDays > 0f)
            {
                CompEggLayer layer = pawn.TryGetComp<CompEggLayer>();
                if (!Developing(layer) || eggLayerActive?.Invoke(layer) != true
                    || eggLayerStopped?.Invoke(layer) != false
                    || !AnimalSeasonalReproduction.AllowsEggProgress(layer)) return 0f;
                float days = AnimalSeasonalReproduction.EggFormationDays(layer);
                return days > 0f ? 2f * ClutchBudget(pawn, profile) / days : 0f;
            }
            return 0f;
        }

        private static float BaselinePerDay(Pawn pawn, bool includeGrowthExtra = true)
        {
            if (pawn?.ageTracker == null) return 0f;
            float baseline = 1.6f * pawn.RaceProps.baseHungerRate * pawn.ageTracker.CurLifeStage.hungerRateFactor;
            if (ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled)
                baseline *= ZoologyAnimalGrowth.HungerRatio(pawn, includeGrowthExtra);
            return baseline * AnimalWinterDormancy.MaintenanceHungerFactor(pawn);
        }

        // Species-specific additive Hediff offset in the native health-to-food calculation.
        internal static float HungerOffset(HediffSet set, HediffDef ignore)
        {
            if (set == null || set.hediffs.Count == 0
                || !TryProfile(set.pawn, out var profile)) return 0f;
            Hediff reproductive = null;
            if (profile.gestationBudget > 0f && ignore != HediffDefOf.Pregnant)
                reproductive = set.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            else if (profile.eggIntervalDays > 0f && gravidDef != null && ignore != gravidDef)
                reproductive = set.GetFirstHediffOfDef(gravidDef);
            float extra = ExtraPerDay(reproductive, profile);
            if (extra <= 0f) return 0f;
            float baseline = BaselinePerDay(set.pawn);
            return baseline > 0f ? extra / baseline : 0f;
        }

        internal static string Tooltip(Hediff hediff)
        {
            if (hediff == null) return "";
            if (hediff.def == HediffDefOf.Pregnant)
            {
                int remaining = AnimalSeasonalReproduction.DiapauseUntil(hediff) - GenTicks.TicksGame;
                if (remaining > 0)
                {
                    int days = 1 + (remaining - 1) / GenDate.TicksPerDay;
                    return "Zoology_EmbryonicDiapause".Translate(days).ToString();
                }
            }
            if (!TryProfile(hediff.pawn, out var profile)) return "";
            float extra = ExtraPerDay(hediff, profile);
            if (extra <= 0f) return "";
            float baseline = BaselinePerDay(hediff.pawn);
            if (baseline <= 0f) return "";
            return "Zoology_ReproductionExtraNutrition".Translate(
                (100f * extra / baseline).ToString("0.#"), extra.ToString("0.###")).ToString();
        }
    }

    public sealed class Hediff_ZoologyGravid : HediffWithComps
    {
        public override bool ShouldRemove => base.ShouldRemove || !ZoologyReproductiveEnergetics.CarriesEggs(pawn);
    }

    [HarmonyPatch(typeof(HediffSet), "GetHungerRateFactor", new Type[] { typeof(HediffDef) })]
    internal static class Patch_ZoologyReproductiveHunger
    {
        private static bool Prepare() => ModConstants.AdvancedReproduction;

        private static void Postfix(HediffSet __instance, HediffDef __0, ref float __result)
        {
            __result += ZoologyReproductiveEnergetics.HungerOffset(__instance, __0);
        }
    }

    // Synchronize the marker at changes to native egg progress, not on
    // every CompTick (including inactive males and immature birds).
    internal static class EggProgressWrite
    {
        internal static IEnumerable<CodeInstruction> Transpile(
            IEnumerable<CodeInstruction> instructions, bool interval)
        {
            var code = new List<CodeInstruction>(instructions);
            FieldInfo field = AccessTools.Field(typeof(CompEggLayer), "eggProgress");
            MethodInfo setter = AccessTools.Method(typeof(EggProgressWrite),
                interval ? nameof(SetInterval) : nameof(SetTick));
            if (field == null || setter == null
                || !code.Exists(i => i.opcode == OpCodes.Stfld && Equals(i.operand, field)))
                return code; // A wrapper that delegates to the other tick method has no write.

            var result = new List<CodeInstruction>(code.Count + 4);
            foreach (var instruction in code)
            {
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, field))
                {
                    if (interval)
                    {
                        var load = new CodeInstruction(OpCodes.Ldarg_1);
                        load.labels.AddRange(instruction.labels);
                        instruction.labels.Clear();
                        load.blocks.AddRange(instruction.blocks);
                        instruction.blocks.Clear();
                        result.Add(load);
                    }
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = setter;
                }
                result.Add(instruction);
            }
            return result;
        }

        private static void SetTick(CompEggLayer layer, float value) => SetInterval(layer, value, 1);

        private static void SetInterval(CompEggLayer layer, float value, int interval)
        {
            float previous = ZoologyReproductiveEnergetics.WriteEggProgress(layer, value);
            if (previous <= 0f && value > 0f)
                AnimalReproductionProfiles.BeginClutch(layer);
            bool wasDeveloping = previous > 0.5f && previous < 1f;
            bool developing = value > 0.5f && value < 1f;
            // Reconcile loaded saves and runtime setting changes on the same
            // existing 250-tick schedule, only while a clutch is developing.
            if (wasDeveloping != developing
                || (developing && layer.parent is Pawn pawn && pawn.IsHashIntervalTick(250, interval)))
                ZoologyReproductiveEnergetics.SyncGravid(layer);
        }
    }

    [HarmonyPatch]
    internal static class Patch_ZoologyEggProgress
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompTick", Type.EmptyTypes);

        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress && TargetMethod() != null;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            EggProgressWrite.Transpile(instructions, false);
    }

    [HarmonyPatch]
    internal static class Patch_ZoologyEggProgressInterval
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompTickInterval", new[] { typeof(int) });

        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress && TargetMethod() != null;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            EggProgressWrite.Transpile(instructions, true);
    }

    [HarmonyPatch]
    internal static class Patch_ZoologyEggLaid
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "ProduceEgg", Type.EmptyTypes);

        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress && TargetMethod() != null;

        private static void Postfix(CompEggLayer __instance)
        {
            ZoologyReproductiveEnergetics.SyncGravid(__instance);
        }
    }

    [HarmonyPatch]
    internal static class Patch_ZoologyReproductiveHediffTooltip
    {
        private static bool Prepare() => ModConstants.AdvancedReproduction;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo normal = AccessTools.PropertyGetter(typeof(HediffWithComps), "TipStringExtra");
            Type pregnant = AccessTools.TypeByName("RimWorld.Hediff_Pregnant") ?? AccessTools.TypeByName("Verse.Hediff_Pregnant");
            MethodInfo specialized = pregnant != null ? AccessTools.PropertyGetter(pregnant, "TipStringExtra") : null;
            if (normal != null) yield return normal;
            if (specialized != null && specialized != normal) yield return specialized;
        }

        private static void Postfix(Hediff __instance, ref string __result)
        {
            if (__instance?.def != HediffDefOf.Pregnant && !(__instance is Hediff_ZoologyGravid)) return;
            string extra = ZoologyReproductiveEnergetics.Tooltip(__instance);
            if (!string.IsNullOrEmpty(extra) && (__result == null || !__result.Contains(extra)))
                __result = string.IsNullOrEmpty(__result) ? extra : __result + "\n" + extra;
        }
    }
}
