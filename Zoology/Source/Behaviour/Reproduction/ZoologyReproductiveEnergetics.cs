using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        private sealed class EggState
        {
            internal CompEggLayer layer;
            internal float progress;
            internal bool active;
            internal bool initialized;
            internal bool markerKnown;
            internal bool markerPresent;
            internal int lastTick = -1;
        }

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
        private static ConditionalWeakTable<Pawn, EggState> states =
            new ConditionalWeakTable<Pawn, EggState>();
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
                FieldInfo field = AccessTools.Field(typeof(CompEggLayer), "eggProgress");
                if (field == null || field.FieldType != typeof(float)) return false;
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
                    if (profile.gravidExtra > 0f)
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

        internal static void SyncSettings()
        {
            if (!installed) return;
            bool enabled = ModConstants.AdvancedReproduction;
            if (initializedSettings && enabled == eggsApplied) return;
            initializedSettings = true;
            eggsApplied = enabled;
            states = new ConditionalWeakTable<Pawn, EggState>();
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

        private static EggState State(Pawn pawn)
        {
            return states.GetValue(pawn, p => new EggState { layer = p.TryGetComp<CompEggLayer>() });
        }

        private static bool TryProfile(Pawn pawn, out ZoologyReproductiveProfile profile)
        {
            profile = null;
            return ModConstants.AdvancedReproduction && pawn?.gender == Gender.Female
                && Profiles.TryGetValue(pawn.def, out profile);
        }

        internal static float ExtraPerDay(Pawn pawn)
        {
            if (!TryProfile(pawn, out var profile)) return 0f;
            if (profile.gestationBudget > 0f)
            {
                Hediff pregnancy = pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant);
                if (pregnancy == null) return 0f;
                float actualDays = pawn.RaceProps.gestationPeriodDays;
                if (actualDays <= 0f) return 0f;
                if (!validPregnancyStages) return profile.gestationBudget / actualDays;
                return profile.PregnancyExtra(pregnancy.CurStageIndex)
                    * pregnancyNormalization * profile.gestationDays / actualDays;
            }
            if (profile.gravidExtra <= 0f || progressRef == null) return 0f;
            EggState state = State(pawn);
            if (state.layer == null) return 0f;
            float p = progressRef(state.layer);
            return p > 0.5f && p < 1f && (!state.initialized || state.active)
                ? profile.gravidExtra : 0f;
        }

        internal static bool CarriesEggs(Pawn pawn)
        {
            if (!TryProfile(pawn, out var profile) || profile.gravidExtra <= 0f || progressRef == null)
                return false;
            CompEggLayer layer = State(pawn).layer;
            return layer != null && progressRef(layer) > 0.5f;
        }

        internal static void NotifyEggProgress(CompEggLayer layer)
        {
            Pawn pawn = layer.parent as Pawn;
            if (!TryProfile(pawn, out var profile) || profile.gravidExtra <= 0f
                || progressRef == null || gravidDef == null || pawn.health == null) return;
            EggState state = State(pawn);
            float progress = progressRef(layer);
            int now = GenTicks.TicksGame;
            if (state.initialized && state.lastTick == now && state.progress == progress) return;
            state.active = progress > 0.5f && progress < 1f
                && (!state.initialized || progress > state.progress);
            state.progress = progress;
            state.initialized = true;
            state.lastTick = now;
            bool carrying = progress > 0.5f;
            if (state.markerKnown && state.markerPresent == carrying) return;
            Hediff marker = pawn.health.hediffSet.GetFirstHediffOfDef(gravidDef);
            if (carrying && marker == null) pawn.health.AddHediff(gravidDef);
            else if (!carrying && marker != null) pawn.health.RemoveHediff(marker);
            state.markerKnown = true;
            state.markerPresent = carrying;
        }

        internal static string Tooltip(Pawn pawn)
        {
            float extra = ExtraPerDay(pawn);
            if (extra <= 0f || pawn?.ageTracker == null) return "";
            float baseline = 1.6f * pawn.RaceProps.baseHungerRate * pawn.ageTracker.CurLifeStage.hungerRateFactor;
            if (ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled)
                baseline *= ZoologyAnimalGrowth.HungerRatio(pawn);
            baseline *= AnimalWinterDormancy.BasalHungerFactor(pawn);
            if (baseline <= 0f) return "";
            return "Zoology_ReproductionExtraNutrition".Translate(
                (100f * extra / baseline).ToString("0.#"), extra.ToString("0.###")).ToString();
        }
    }

    public sealed class Hediff_ZoologyGravid : HediffWithComps
    {
        public override bool ShouldRemove => base.ShouldRemove || !ZoologyReproductiveEnergetics.CarriesEggs(pawn);
    }

    [HarmonyPatch]
    internal static class Patch_ZoologyEggProgress
    {
        private static bool Prepare() => ModConstants.AdvancedReproduction && ZoologyReproductiveEnergetics.CanReadEggProgress;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo tick = AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompTick", Type.EmptyTypes);
            MethodInfo interval = AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompTickInterval", new[] { typeof(int) });
            MethodInfo produce = AccessTools.DeclaredMethod(typeof(CompEggLayer), "ProduceEgg", Type.EmptyTypes);
            if (tick != null) yield return tick;
            if (interval != null) yield return interval;
            if (produce != null) yield return produce;
        }

        private static void Postfix(CompEggLayer __instance)
        {
            ZoologyReproductiveEnergetics.NotifyEggProgress(__instance);
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
            string extra = ZoologyReproductiveEnergetics.Tooltip(__instance.pawn);
            if (!string.IsNullOrEmpty(extra) && (__result == null || !__result.Contains(extra)))
                __result = string.IsNullOrEmpty(__result) ? extra : __result + "\n" + extra;
        }
    }
}
