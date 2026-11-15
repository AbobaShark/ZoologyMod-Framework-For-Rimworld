using System;
using System.Collections.Generic;
using System.Reflection;
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
        public float pregnancyStage1;
        public float pregnancyStage2;
        public float pregnancyStage3;
        public float eggIntervalDays;
        public float clutchBudget;
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
                return true;
            }
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
                        if (Math.Abs(eggs.eggLayIntervalDays - profile.eggIntervalDays) > 0.05f)
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

        private static bool TryProfile(Pawn pawn, out ZoologyReproductiveProfile profile)
        {
            profile = null;
            return ModConstants.AdvancedReproduction && pawn?.RaceProps?.Animal == true
                && pawn.gender != Gender.Male
                && Profiles.TryGetValue(pawn.def, out profile)
                && (pawn.gender == Gender.Female || profile.gravidExtra > 0f);
        }

        private static bool Developing(CompEggLayer layer)
        {
            if (layer == null || progressRef == null) return false;
            float progress = progressRef(layer);
            return progress > 0.5f && progress < 1f;
        }

        internal static bool CarriesEggs(Pawn pawn)
        {
            return TryProfile(pawn, out var profile) && profile.gravidExtra > 0f
                && Developing(pawn.TryGetComp<CompEggLayer>());
        }

        internal static void SyncGravid(CompEggLayer layer)
        {
            Pawn pawn = layer.parent as Pawn;
            if (!TryProfile(pawn, out var profile) || profile.gravidExtra <= 0f
                || progressRef == null || gravidDef == null || pawn.health == null) return;
            bool developing = Developing(layer);
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
                float actualDays = pawn.RaceProps.gestationPeriodDays;
                if (actualDays <= 0f) return 0f;
                if (!validPregnancyStages) return profile.gestationBudget / actualDays;
                return profile.PregnancyExtra(hediff.CurStageIndex)
                    * pregnancyNormalization * profile.gestationDays / actualDays;
            }
            if (hediff.def == gravidDef && profile.gravidExtra > 0f)
            {
                CompEggLayer layer = pawn.TryGetComp<CompEggLayer>();
                if (!Developing(layer) || layer.Props.eggLayIntervalDays <= 0f) return 0f;
                return 2f * profile.clutchBudget / layer.Props.eggLayIntervalDays;
            }
            return 0f;
        }

        private static float BaselinePerDay(Pawn pawn)
        {
            if (pawn?.ageTracker == null) return 0f;
            float baseline = 1.6f * pawn.RaceProps.baseHungerRate * pawn.ageTracker.CurLifeStage.hungerRateFactor;
            if (ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled)
                baseline *= ZoologyAnimalGrowth.HungerRatio(pawn);
            return baseline;
        }

        // Species-specific additive Hediff offset in the native health-to-food calculation.
        internal static float HungerOffset(HediffSet set, HediffDef ignore)
        {
            if (set == null || set.hediffs.Count == 0
                || !TryProfile(set.pawn, out var profile)) return 0f;
            Hediff reproductive = null;
            if (profile.gestationBudget > 0f && ignore != HediffDefOf.Pregnant)
                reproductive = set.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            else if (profile.gravidExtra > 0f && gravidDef != null && ignore != gravidDef)
                reproductive = set.GetFirstHediffOfDef(gravidDef);
            float extra = ExtraPerDay(reproductive, profile);
            if (extra <= 0f) return 0f;
            float baseline = BaselinePerDay(set.pawn);
            return baseline > 0f ? extra / baseline : 0f;
        }

        internal static string Tooltip(Hediff hediff)
        {
            if (hediff == null || !TryProfile(hediff.pawn, out var profile)) return "";
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

    [HarmonyPatch]
    internal static class Patch_ZoologyEggProgress
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompTick", Type.EmptyTypes);

        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress && TargetMethod() != null;

        private static void Postfix(CompEggLayer __instance)
        {
            if (__instance.parent is Pawn pawn && pawn.IsHashIntervalTick(250))
                ZoologyReproductiveEnergetics.SyncGravid(__instance);
        }
    }

    [HarmonyPatch]
    internal static class Patch_ZoologyEggProgressInterval
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompTickInterval", new[] { typeof(int) });

        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress && TargetMethod() != null;

        private static void Postfix(CompEggLayer __instance, int __0)
        {
            if (__instance.parent is Pawn pawn && pawn.IsHashIntervalTick(250, __0))
                ZoologyReproductiveEnergetics.SyncGravid(__instance);
        }
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
