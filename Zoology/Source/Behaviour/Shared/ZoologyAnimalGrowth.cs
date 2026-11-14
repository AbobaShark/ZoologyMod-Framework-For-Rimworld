using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // Runtime interpolation consumes the already installed species-specific LifeStageDefs.
    // No defs are mutated after installation; one snapshot per biological day per young pawn.
    internal static class ZoologyAnimalGrowth
    {
        // A single gate for daily interpolation and optional age-specific
        // runtime modifiers. Authored LifeStageDefs stay installed.
        internal static bool Enabled
        {
            get
            {
                var settings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
                return settings == null ||
                    (!settings.DisableAllRuntimePatches && settings.EnableDynamicAnimalGrowth);
            }
        }

        private const long TicksPerBiologicalDay = 60000L;
        private const double TicksPerBiologicalYear = 3600000d;

        private sealed class FactorPair
        {
            internal readonly float from;
            internal readonly float to;
            internal readonly float installedFrom;

            internal FactorPair(float from, float to, float installedFrom)
            {
                this.from = from;
                this.to = to;
                this.installedFrom = installedFrom;
            }
        }

        private sealed class Segment
        {
            internal long startTick;
            internal long endTick;
            internal long firstSampleTick;
            internal long lastSampleTick;
            internal LifeStageDef from;
            internal LifeStageDef to;
            internal Dictionary<StatDef, FactorPair> statFactors;
        }

        private sealed class Snapshot
        {
            internal Segment segment;
            internal int stageIndex = -1;
            internal long biologicalDay = long.MinValue;
            internal PawnKindDef kind;
            internal float progress;
            internal float bodyRatio = 1f;
            internal float healthRatio = 1f;
            internal float hungerRatio = 1f;
            internal float marketRatio = 1f;
            internal Vector2 drawRatio = Vector2.one;
            internal bool drawRatioReady;
            internal Gender graphicGender;
            internal int graphicVersion;
            internal ZoologySexAdultProfile sexTarget;
        }

        private static readonly Dictionary<ThingDef, Segment[]> RaceSegments =
            new Dictionary<ThingDef, Segment[]>();
        private static readonly ConditionalWeakTable<Pawn, Snapshot> Snapshots =
            new ConditionalWeakTable<Pawn, Snapshot>();

        internal static bool HandlesRace(ThingDef race)
        {
            return race != null && RaceSegments.ContainsKey(race);
        }

        internal static void Install(List<ZoologyLifeStageProfileCatalogDef> catalogs)
        {
            if (catalogs == null)
                return;

            var species = new HashSet<ThingDef>();
            for (int c = 0; c < catalogs.Count; c++)
            {
                var profiles = catalogs[c]?.stages;
                if (profiles == null)
                    continue;
                for (int i = 0; i < profiles.Count; i++)
                {
                    string name = profiles[i]?.animalDefName;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var race = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (race?.race?.Animal == true)
                        species.Add(race);
                }
            }

            var affectedStats = new HashSet<StatDef>();
            int segments = 0;
            foreach (var race in species)
            {
                var ages = race.race.lifeStageAges;
                if (ages == null || ages.Count < 2)
                    continue;

                var intervals = new Segment[ages.Count];
                for (int i = 0; i + 1 < ages.Count; i++)
                {
                    var current = ages[i]?.def;
                    var next = ages[i + 1]?.def;
                    // Only consecutive stages of the same race are considered.
                    // Metamorphosis into a different race/PawnKind is handled
                    // outside this sequence and never interpolated here.
                    if (current == null || next == null
                        || !Positive(current.bodySizeFactor) || !Positive(next.bodySizeFactor)
                        || !Positive(current.healthScaleFactor) || !Positive(next.healthScaleFactor))
                        continue;

                    long start = (long)Math.Round(ages[i].minAge * TicksPerBiologicalYear);
                    long end = (long)Math.Round(ages[i + 1].minAge * TicksPerBiologicalYear);
                    if (end <= start)
                        continue;

                    var segment = new Segment
                    {
                        startTick = start,
                        endTick = end,
                        firstSampleTick = ((start + TicksPerBiologicalDay - 1L)
                            / TicksPerBiologicalDay) * TicksPerBiologicalDay,
                        lastSampleTick = ((end - 1L) / TicksPerBiologicalDay)
                            * TicksPerBiologicalDay,
                        from = current,
                        to = next,
                        statFactors = BuildStatFactors(current, next, affectedStats)
                    };
                    intervals[i] = segment;
                    segments++;
                }
                RaceSegments[race] = intervals;
            }

            ZoologyLifeStageProfileInstaller.AddZeroStatFactors(affectedStats);
            ZoologyAnimalGrowthStatPart.Install(affectedStats);
            Log.Message("[Zoology] Daily growth initialized for " + RaceSegments.Count
                + " animal races and " + segments + " growth intervals.");
        }

        private static Dictionary<StatDef, FactorPair> BuildStatFactors(
            LifeStageDef from, LifeStageDef to, HashSet<StatDef> affected)
        {
            var result = new Dictionary<StatDef, FactorPair>();
            var candidates = new HashSet<StatDef>();
            if (from.statFactors != null)
                foreach (var factor in from.statFactors)
                    if (factor?.stat != null)
                        candidates.Add(factor.stat);
            if (to.statFactors != null)
                foreach (var factor in to.statFactors)
                    if (factor?.stat != null)
                        candidates.Add(factor.stat);

            foreach (var stat in candidates)
            {
                float first = GetDefinedStatFactor(from, stat);
                float second = GetDefinedStatFactor(to, stat);
                // A stage authored with zero is installed with one and the
                // required exact zero is applied by our StatPart instead.
                float installed = GetInstalledStatFactor(from, stat);
                if (!FiniteNonnegative(first) || !FiniteNonnegative(second)
                    || !Positive(installed)
                    || (Math.Abs(first - second) < 0.000001f
                        && Math.Abs(first - installed) < 0.000001f))
                    continue;
                result.Add(stat, new FactorPair(first, second, installed));
                affected.Add(stat);
            }
            return result;
        }

        private static float GetDefinedStatFactor(LifeStageDef stage, StatDef stat)
        {
            if (ZoologyLifeStageProfileInstaller.HasZeroStatFactor(stage, stat))
                return 0f;
            return GetInstalledStatFactor(stage, stat);
        }

        private static float GetInstalledStatFactor(LifeStageDef stage, StatDef stat)
        {
            if (stage?.statFactors != null)
                for (int i = 0; i < stage.statFactors.Count; i++)
                    if (stage.statFactors[i]?.stat == stat)
                        return stage.statFactors[i].value;
            return 1f;
        }

        private static bool TryGetSnapshot(Pawn pawn, out Snapshot snapshot)
        {
            snapshot = null;
            if (pawn?.ageTracker == null || pawn.def == null
                || !Enabled)
                return false;

            Segment[] intervals;
            if (!RaceSegments.TryGetValue(pawn.def, out intervals))
                return false;

            int stageIndex = pawn.ageTracker.CurLifeStageIndex;
            if (stageIndex < 0 || stageIndex >= intervals.Length)
                return false;
            Segment segment = intervals[stageIndex];
            if (segment == null || pawn.ageTracker.CurLifeStage != segment.from)
                return false;

            snapshot = Snapshots.GetValue(pawn, _ => new Snapshot());
            long ticks = pawn.ageTracker.AgeBiologicalTicks;
            long biologicalDay = ticks >= 0 ? ticks / TicksPerBiologicalDay : 0L;
            if (snapshot.segment != segment || snapshot.stageIndex != stageIndex
                || snapshot.biologicalDay != biologicalDay || snapshot.kind != pawn.kindDef
                || snapshot.graphicGender != pawn.gender
                || snapshot.graphicVersion != ZoologySexAdultProfiles.GraphicVersion)
            {
                snapshot.segment = segment;
                snapshot.stageIndex = stageIndex;
                snapshot.biologicalDay = biologicalDay;
                snapshot.kind = pawn.kindDef;
                snapshot.graphicGender = pawn.gender;
                snapshot.graphicVersion = ZoologySexAdultProfiles.GraphicVersion;
                ZoologySexAdultProfile target;
                snapshot.sexTarget = ZoologySexAdultProfiles.TryGet(pawn, true, out target)
                    ? target : null;

                long sampled = biologicalDay * TicksPerBiologicalDay;
                float t = (float)((double)(sampled - segment.startTick)
                    / (segment.endTick - segment.startTick));
                // All age-dependent values use the same daily samples,
                // including their last day before the next LifeStageDef.
                // This avoids the visual size reaching the next stage while
                // speed, BodySize, health, hunger or combat stats lag behind.
                float dailyProgress = segment.lastSampleTick > segment.firstSampleTick
                    ? Mathf.Clamp01((float)((double)(sampled - segment.firstSampleTick)
                        / (segment.lastSampleTick - segment.firstSampleTick)))
                    : Mathf.Clamp01(t);
                snapshot.progress = dailyProgress;
                float fraction = dailyProgress;

                var sexEnd = snapshot.sexTarget;
                snapshot.bodyRatio = LerpRatio(segment.from.bodySizeFactor,
                    sexEnd != null ? sexEnd.bodySizeFactor : segment.to.bodySizeFactor, fraction);
                snapshot.healthRatio = LerpRatio(segment.from.healthScaleFactor,
                    sexEnd != null ? sexEnd.healthScaleFactor : segment.to.healthScaleFactor, fraction);
                snapshot.hungerRatio = LerpRatio(segment.from.hungerRateFactor,
                    sexEnd != null ? sexEnd.hungerRateFactor : segment.to.hungerRateFactor, fraction);
                // Follow the canonical age price unless the species explicitly
                // opts into sex-specific market pricing in Base stats.
                snapshot.marketRatio = LerpRatio(segment.from.marketValueFactor,
                    sexEnd != null && sexEnd.useSexSpecificMarketValue
                        ? sexEnd.marketValueFactor : segment.to.marketValueFactor, fraction);
                // Do not touch sprite assets from physiological/stat queries.
                // Visible size is resolved lazily by the render-only DrawRatio
                // getter, never by BodySize, hunger, combat or market value.
                snapshot.drawRatioReady = false;
            }
            return true;
        }

        private static float LerpRatio(float from, float to, float t)
        {
            return Positive(from) && FiniteNonnegative(to)
                ? Mathf.Lerp(from, to, t) / from : 1f;
        }

        // Developer-mode diagnosis only. This method is never called by
        // tick, stat or rendering hooks.
        internal static string GrowthUnavailableReason(Pawn pawn)
        {
            if (pawn?.ageTracker == null || pawn.def == null)
                return "pawn has no initialized biological age";

            if (!Enabled)
                return "dynamic animal growth is disabled in Zoology settings";

            if (RaceSegments.Count == 0)
                return "no calculated growth intervals installed; verify the generated "
                    + "LifeStageProfiles catalog and a freshly compiled ZoologyMod.dll";

            Segment[] intervals;
            if (!RaceSegments.TryGetValue(pawn.def, out intervals))
                return "this animal race has no generated LifeStageProfiles intervals";

            int stageIndex = pawn.ageTracker.CurLifeStageIndex;
            if (stageIndex < 0 || stageIndex >= intervals.Length)
                return "life-stage index outside generated interval bounds";

            Segment segment = intervals[stageIndex];
            if (segment == null)
                return stageIndex == intervals.Length - 1
                    ? "final adult life stage (no growth interval)"
                    : "this life-stage transition has no valid calculated endpoints";

            if (pawn.ageTracker.CurLifeStage != segment.from)
                return "current LifeStageDef differs from the definition "
                    + "registered when growth intervals were initialized";

            return null;
        }

        internal static float BodyRatio(Pawn pawn)
        {
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
                return state.bodyRatio;
            ZoologySexAdultProfile sex;
            return ZoologySexAdultProfiles.TryGet(pawn, false, out sex)
                && Positive(pawn.ageTracker.CurLifeStage.bodySizeFactor)
                ? sex.bodySizeFactor / pawn.ageTracker.CurLifeStage.bodySizeFactor : 1f;
        }

        internal static float HealthRatio(Pawn pawn)
        {
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
                return state.healthRatio;
            ZoologySexAdultProfile sex;
            return ZoologySexAdultProfiles.TryGet(pawn, false, out sex)
                && Positive(pawn.ageTracker.CurLifeStage.healthScaleFactor)
                ? sex.healthScaleFactor / pawn.ageTracker.CurLifeStage.healthScaleFactor : 1f;
        }

        internal static float HungerRatio(Pawn pawn)
        {
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
                return state.hungerRatio;
            ZoologySexAdultProfile sex;
            return ZoologySexAdultProfiles.TryGet(pawn, false, out sex)
                && Positive(pawn.ageTracker.CurLifeStage.hungerRateFactor)
                ? sex.hungerRateFactor / pawn.ageTracker.CurLifeStage.hungerRateFactor : 1f;
        }

        internal static float MarketRatio(Pawn pawn)
        {
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
                return state.marketRatio;
            ZoologySexAdultProfile sex;
            return ZoologySexAdultProfiles.TryGet(pawn, false, out sex)
                && sex.useSexSpecificMarketValue
                && Positive(pawn.ageTracker.CurLifeStage.marketValueFactor)
                ? sex.marketValueFactor / pawn.ageTracker.CurLifeStage.marketValueFactor
                : 1f;
        }

        internal static bool IsSexSpecificMarketValue(Pawn pawn)
        {
            ZoologySexAdultProfile sex;
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
                return state.sexTarget != null
                    && state.sexTarget.useSexSpecificMarketValue;
            return ZoologySexAdultProfiles.TryGet(pawn, false, out sex)
                && sex.useSexSpecificMarketValue;
        }

        // UI explanation only: distinguish an age-only adjustment from
        // interpolation toward a sex-specific adult stat endpoint.
        internal static bool IsSexSpecificStat(Pawn pawn, StatDef stat)
        {
            if (stat == null)
                return false;
            float factor;
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
                return state.sexTarget != null
                    && ZoologySexAdultProfiles.TryGetStat(pawn, stat, true, out factor);
            return ZoologySexAdultProfiles.TryGetStat(pawn, stat, false, out factor);
        }

        internal static Vector2 DrawRatio(Pawn pawn)
        {
            Snapshot state;
            if (!TryGetSnapshot(pawn, out state))
                return Vector2.one;
            if (!state.drawRatioReady)
            {
                state.drawRatio = ZoologyAnimalGraphicBounds.Interpolate(
                    pawn.kindDef, state.stageIndex, pawn.gender == Gender.Female,
                    state.progress);
                state.drawRatioReady = true;
            }
            return state.drawRatio;
        }

        internal static bool TryGetStatRatio(Pawn pawn, StatDef stat, out float ratio)
        {
            ratio = 1f;
            if (stat == null)
                return false;
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
            {
                float adult;
                if (state.sexTarget != null
                    && ZoologySexAdultProfiles.TryGetStat(pawn, stat, true, out adult))
                {
                    float installed = GetInstalledStatFactor(state.segment.from, stat);
                    if (!Positive(installed))
                        return false;
                    float first = GetDefinedStatFactor(state.segment.from, stat);
                    ratio = Mathf.Lerp(first, adult, state.progress) / installed;
                    return true;
                }
                FactorPair pair;
                if (!state.segment.statFactors.TryGetValue(stat, out pair))
                    return false;
                ratio = Mathf.Lerp(pair.from, pair.to, state.progress) / pair.installedFrom;
                return true;
            }
            float value;
            if (!ZoologySexAdultProfiles.TryGetStat(pawn, stat, false, out value))
                return false;
            float current = GetInstalledStatFactor(pawn.ageTracker?.CurLifeStage, stat);
            if (!Positive(current))
                return false;
            ratio = value / current;
            return true;
        }

        internal static bool TryGetStages(Pawn pawn,
            out LifeStageDef current, out LifeStageDef next, out float progress)
        {
            current = next = null;
            progress = 0f;
            Snapshot state;
            if (!TryGetSnapshot(pawn, out state))
                return false;
            current = state.segment.from;
            next = state.segment.to;
            progress = state.progress;
            return true;
        }

        internal static bool TryGetArmorOffset(Pawn pawn, bool sharp, out float offset)
        {
            offset = 0f;
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
            {
                float first, second;
                ZoologyLifeStageProfileInstaller.TryGetArmorOffset(state.segment.from, sharp, out first);
                if (state.sexTarget != null)
                    ZoologySexAdultProfiles.TryGetOffset(pawn, sharp, true, out second);
                else
                    ZoologyLifeStageProfileInstaller.TryGetArmorOffset(state.segment.to, sharp, out second);
                offset = Mathf.Lerp(first, second, state.progress);
                return true;
            }
            return ZoologySexAdultProfiles.TryGetOffset(pawn, sharp, false, out offset);
        }

        internal static bool TryGetDurability(Pawn pawn, out float factor)
        {
            factor = 1f;
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
            {
                float first, second;
                if (!ZoologyLifeStageProfileInstaller.TryGetArmorDurabilityFactor(
                        state.segment.from, out first))
                    first = 1f;
                if (state.sexTarget != null)
                    second = state.sexTarget.ceArmorDurabilityFactor;
                else if (!ZoologyLifeStageProfileInstaller.TryGetArmorDurabilityFactor(
                        state.segment.to, out second))
                    second = 1f;
                factor = Mathf.Lerp(first, second, state.progress);
                return true;
            }
            ZoologySexAdultProfile sex;
            if (!ZoologySexAdultProfiles.TryGet(pawn, false, out sex))
                return false;
            factor = sex.ceArmorDurabilityFactor;
            return true;
        }

        internal static bool TryGetToolFactor(
            Pawn pawn, Tool tool, GrowthToolFactor kind, out float factor)
        {
            factor = 1f;
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
            {
                ZoologyLifeStageToolFactor first, second;
                bool hasFirst = ZoologyLifeStageProfileInstaller.TryGetToolFactor(
                    state.segment.from, tool, out first);
                bool hasSecond = state.sexTarget != null
                    ? ZoologySexAdultProfiles.TryGetTool(pawn, tool, true, out second)
                    : ZoologyLifeStageProfileInstaller.TryGetToolFactor(
                        state.segment.to, tool, out second);
                if (!hasFirst && !hasSecond)
                    return false;
                float a = hasFirst ? SelectToolFactor(first, kind) : 1f;
                float b = hasSecond ? SelectToolFactor(second, kind) : 1f;
                factor = Mathf.Lerp(a, b, state.progress);
                return true;
            }
            ZoologyLifeStageToolFactor adult;
            if (!ZoologySexAdultProfiles.TryGetTool(pawn, tool, false, out adult))
                return false;
            factor = SelectToolFactor(adult, kind);
            return true;
        }

        internal enum GrowthToolFactor
        {
            VanillaDamage,
            VanillaCooldown,
            CeDamage,
            CeCooldown,
            CeSharpPenetration,
            CeBluntPenetration
        }

        private static float SelectToolFactor(ZoologyLifeStageToolFactor factors, GrowthToolFactor kind)
        {
            switch (kind)
            {
                case GrowthToolFactor.VanillaDamage: return factors.vanillaDamageFactor;
                case GrowthToolFactor.VanillaCooldown: return factors.vanillaCooldownFactor;
                case GrowthToolFactor.CeDamage: return factors.ceDamageFactor;
                case GrowthToolFactor.CeCooldown: return factors.ceCooldownFactor;
                case GrowthToolFactor.CeSharpPenetration: return factors.ceSharpPenetrationFactor;
                case GrowthToolFactor.CeBluntPenetration: return factors.ceBluntPenetrationFactor;
                default: return 1f;
            }
        }

        internal static bool TryGetProgress(Pawn pawn, out float progress)
        {
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
            {
                progress = state.progress;
                return true;
            }
            progress = 0f;
            return false;
        }

        private static bool Positive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool FiniteNonnegative(float value)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
