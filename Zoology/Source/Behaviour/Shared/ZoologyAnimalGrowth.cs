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
        private const double GrowthBaseFeedShare = 0.5d;

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

        private sealed class GrowthEnergetics
        {
            internal float exponent;
            internal float efficiency;
            internal float baseSize;
            internal double baseShape;
            internal double adultFeed;
            internal double nutritionPerSize;
        }

        private static readonly Dictionary<ThingDef, GrowthEnergetics> Energetics =
            new Dictionary<ThingDef, GrowthEnergetics>();

        private sealed class Segment
        {
            internal long startTick;
            internal long endTick;
            internal long firstSampleTick;
            internal long lastSampleTick;
            internal LifeStageDef from;
            internal LifeStageDef to;
            internal Dictionary<StatDef, FactorPair> statFactors;
            internal GrowthEnergetics energetics;
            internal float commonGrowthExtra;
            internal float maleGrowthExtra;
            internal float femaleGrowthExtra;
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
            internal float nominalHungerRatio = 1f;
            internal bool hasGrowthExtra;
            internal float marketRatio = 1f;
            internal Vector2 drawRatio = Vector2.one;
            internal bool drawRatioReady;
            internal Gender graphicGender;
            internal bool sexEnabled;
            internal int graphicVersion;
            internal ZoologySexAdultProfile sexTarget;
        }

        private static readonly Dictionary<ThingDef, Segment[]> RaceSegments =
            new Dictionary<ThingDef, Segment[]>();
        [ThreadStatic] private static ConditionalWeakTable<Pawn, Snapshot> snapshots;

        // Bounded, thread-local front cache for the two hottest ratio queries.
        // The biological day and life stage are the same sampling keys used
        // by the complete growth snapshot.
        private const int HotCacheSize = 4096;
        private struct HotRatio
        {
            internal Pawn pawn;
            internal ThingDef race;
            internal PawnKindDef kind;
            internal LifeStageDef stage;
            internal long day;
            internal Gender gender;
            internal int version;
            internal int mode;
            internal bool bodyReady;
            internal bool drawReady;
            internal float body;
            internal Vector2 draw;
        }

        [ThreadStatic] private static HotRatio[] hotRatios;
        [ThreadStatic] private static Game hotGame;

        private static ref HotRatio RatioEntry(Pawn pawn)
        {
            var entries = hotRatios;
            if (entries == null)
                entries = hotRatios = new HotRatio[HotCacheSize];
            int slot = pawn.thingIDNumber & (HotCacheSize - 1);
            ref HotRatio entry = ref entries[slot];
            var settings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
            int mode = settings == null ? 1 : settings.DisableAllRuntimePatches ? 0
                : (settings.EnableDynamicAnimalGrowth ? 1 : 0)
                    | (settings.EnableSexualDimorphism ? 2 : 0);
            LifeStageDef stage = pawn.ageTracker.CurLifeStage;
            long day = pawn.ageTracker.AgeBiologicalTicks / TicksPerBiologicalDay;
            int version = ZoologySexAdultProfiles.GraphicVersion;
            if (entry.pawn == pawn && entry.race == pawn.def && entry.kind == pawn.kindDef
                && entry.stage == stage && entry.day == day
                && entry.gender == pawn.gender && entry.version == version && entry.mode == mode)
                return ref entry;

            Game game = Current.Game;
            if (hotGame != game)
            {
                Array.Clear(entries, 0, entries.Length);
                hotGame = game;
            }
            entry = new HotRatio { pawn = pawn, race = pawn.def, kind = pawn.kindDef,
                stage = stage, day = day, gender = pawn.gender, version = version, mode = mode };
            return ref entry;
        }

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

            foreach (var catalog in DefDatabase<ZoologyEnergeticsCatalogDef>.AllDefsListForReading)
            {
                foreach (var profile in catalog.profiles)
                {
                    if (profile.growthExponent <= 0f || profile.growthEfficiency <= 0f
                        || profile.growthEfficiency > 1f) continue;
                    var race = DefDatabase<ThingDef>.GetNamedSilentFail(profile.animalDefName);
                    if (race?.race?.Animal != true) continue;
                    if (Math.Abs(race.race.baseHungerRate - profile.expectedBaseHungerRate) > 0.001f)
                        continue;
                    Energetics[race] = new GrowthEnergetics
                    {
                        exponent = profile.growthExponent,
                        efficiency = profile.growthEfficiency,
                        baseSize = race.race.baseBodySize,
                        baseShape = HungerShape(race.race.baseBodySize, profile.growthExponent),
                        adultFeed = 1.6d * race.race.baseHungerRate,
                        nutritionPerSize = BodyNutrition(race, 1f)
                    };
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

            snapshot = (snapshots ??= new ConditionalWeakTable<Pawn, Snapshot>())
                .GetValue(pawn, _ => new Snapshot());
            long ticks = pawn.ageTracker.AgeBiologicalTicks;
            long biologicalDay = ticks >= 0 ? ticks / TicksPerBiologicalDay : 0L;
            if (snapshot.segment != segment || snapshot.stageIndex != stageIndex
                || snapshot.biologicalDay != biologicalDay || snapshot.kind != pawn.kindDef
                || snapshot.graphicGender != pawn.gender
                || snapshot.sexEnabled != ZoologySexAdultProfiles.Enabled
                || snapshot.graphicVersion != ZoologySexAdultProfiles.GraphicVersion)
            {
                snapshot.segment = segment;
                snapshot.stageIndex = stageIndex;
                snapshot.biologicalDay = biologicalDay;
                snapshot.kind = pawn.kindDef;
                snapshot.graphicGender = pawn.gender;
                snapshot.sexEnabled = ZoologySexAdultProfiles.Enabled;
                snapshot.graphicVersion = ZoologySexAdultProfiles.GraphicVersion;
                ZoologySexAdultProfile target;
                snapshot.sexTarget = ZoologySexAdultProfiles.TryGet(pawn, true, out target)
                    ? target : null;

                long sampled = biologicalDay * TicksPerBiologicalDay;
                float fraction = SampleProgress(segment, sampled);
                snapshot.progress = fraction;

                var sexEnd = snapshot.sexTarget;
                snapshot.bodyRatio = LerpRatio(segment.from.bodySizeFactor,
                    sexEnd != null ? sexEnd.bodySizeFactor : segment.to.bodySizeFactor, fraction);
                snapshot.healthRatio = LerpRatio(segment.from.healthScaleFactor,
                    sexEnd != null ? sexEnd.healthScaleFactor : segment.to.healthScaleFactor, fraction);
                snapshot.hungerRatio = LerpRatio(segment.from.hungerRateFactor,
                    sexEnd != null ? sexEnd.hungerRateFactor : segment.to.hungerRateFactor, fraction);
                snapshot.nominalHungerRatio = snapshot.hungerRatio;
                snapshot.hasGrowthExtra = false;

                var energetics = segment.energetics;
                if (energetics != null && Positive(segment.from.hungerRateFactor))
                {
                    float endpoint = sexEnd != null ? sexEnd.bodySizeFactor : segment.to.bodySizeFactor;
                    float current = Mathf.Lerp(segment.from.bodySizeFactor, endpoint, fraction)
                        * energetics.baseSize;
                    float nominal = (float)(HungerShape(current, energetics.exponent) / energetics.baseShape);
                    float extra = sexEnd == null ? segment.commonGrowthExtra
                        : pawn.gender == Gender.Male ? segment.maleGrowthExtra : segment.femaleGrowthExtra;
                    snapshot.nominalHungerRatio = nominal / segment.from.hungerRateFactor;
                    snapshot.hungerRatio = (nominal + extra) / segment.from.hungerRateFactor;
                    snapshot.hasGrowthExtra = extra > 0f;
                }
                // Follow the canonical age price unless the species explicitly
                // opts into sex-specific market pricing in Base stats.
                snapshot.marketRatio = LerpRatio(segment.from.marketValueFactor,
                    sexEnd != null && sexEnd.useSexSpecificMarketValue
                        ? sexEnd.marketValueFactor : segment.to.marketValueFactor, fraction);
                // Resolve the interpolated draw ratio only for rendering.
                // Its graphic endpoints are prepared by ZoologyAnimalGraphics
                // before the parallel renderer accesses them.
                snapshot.drawRatioReady = false;
            }
            return true;
        }

        private static float SampleProgress(Segment segment, long sampled)
        {
            return segment.lastSampleTick > segment.firstSampleTick
                ? Mathf.Clamp01((float)((double)(sampled - segment.firstSampleTick)
                    / (segment.lastSampleTick - segment.firstSampleTick)))
                : Mathf.Clamp01((float)((double)(sampled - segment.startTick)
                    / (segment.endTick - segment.startTick)));
        }

        private static double HungerShape(float size, float exponent) =>
            Math.Pow(size, exponent) / HungerSizeDivisor(size);

        // Install after sex-specific endpoints have been loaded. Cost is
        // calculated once for each interval and endpoint, not for each pawn.
        internal static void PrepareGrowthBudgets()
        {
            foreach (var pair in RaceSegments)
            {
                if (!Energetics.TryGetValue(pair.Key, out var energetics)
                    || !(energetics.adultFeed > 0d) || !(energetics.baseShape > 0d))
                    continue;
                Segment[] intervals = pair.Value;
                for (int index = 0; index < intervals.Length; index++)
                {
                    Segment segment = intervals[index];
                    if (segment == null) continue;
                    float common = segment.to.bodySizeFactor;
                    float male = ZoologySexAdultProfiles.GetForStage(pair.Key, index + 1, Gender.Male)
                        ?.bodySizeFactor ?? common;
                    float female = ZoologySexAdultProfiles.GetForStage(pair.Key, index + 1, Gender.Female)
                        ?.bodySizeFactor ?? common;
                    segment.commonGrowthExtra = StageGrowthExtra(segment, energetics, common);
                    segment.maleGrowthExtra = male == common ? segment.commonGrowthExtra
                        : StageGrowthExtra(segment, energetics, male);
                    segment.femaleGrowthExtra = female == common ? segment.commonGrowthExtra
                        : female == male ? segment.maleGrowthExtra : StageGrowthExtra(segment, energetics, female);
                    segment.energetics = energetics;
                }
            }
        }

        private static float StageGrowthExtra(Segment segment, GrowthEnergetics energetics, float endpoint)
        {
            // The accumulated ordinary ration uses exactly the same day
            // samples as BodySize, including partial first and last days.
            long start = segment.startTick, end = segment.endTick;
            double ordinary = 0d;
            for (long day = start / TicksPerBiologicalDay * TicksPerBiologicalDay;
                day < end; day += TicksPerBiologicalDay)
            {
                long elapsed = Math.Min(end, day + TicksPerBiologicalDay) - Math.Max(start, day);
                float size = Mathf.Lerp(segment.from.bodySizeFactor, endpoint,
                    SampleProgress(segment, day)) * energetics.baseSize;
                ordinary += energetics.adultFeed * HungerShape(size, energetics.exponent)
                    / energetics.baseShape * elapsed / TicksPerBiologicalDay;
            }

            // The whole transition is funded even for a one-day stage or
            // a change in size on the exact life-stage boundary.
            double gain = Math.Max(0d, (endpoint - segment.from.bodySizeFactor)
                * (double)energetics.baseSize * energetics.nutritionPerSize) / energetics.efficiency;
            double extra = Math.Max(0d, gain - GrowthBaseFeedShare * ordinary);
            return (float)(extra / ((end - start) / (double)TicksPerBiologicalDay) / energetics.adultFeed);
        }

        private static float HungerSizeDivisor(float size)
        {
            return Mathf.Abs(size - 3.5f) < 0.00001f ? 1.5f : size < 3.5f ? 2f : 1.25f;
        }

        private static float BodyNutrition(ThingDef race, float size)
        {
            StatDef stat = StatDefOf.MeatAmount;
            float raw = stat.defaultBaseValue;
            if (race.statBases != null)
                for (int i = 0; i < race.statBases.Count; i++)
                    if (race.statBases[i].stat == stat)
                    {
                        raw = race.statBases[i].value;
                        break;
                    }
            return 0.05f * Mathf.Max(0f, raw * size);
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
            if (pawn?.ageTracker == null) return 1f;
            ref HotRatio entry = ref RatioEntry(pawn);
            if (!entry.bodyReady)
            {
                entry.body = CalculateBodyRatio(pawn);
                entry.bodyReady = true;
            }
            return entry.body;
        }

        private static float CalculateBodyRatio(Pawn pawn)
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

        internal static float HungerRatio(Pawn pawn, bool includeGrowthExtra = true)
        {
            Snapshot state;
            if (TryGetSnapshot(pawn, out state))
                return !includeGrowthExtra || (state.hasGrowthExtra && Hediff_GrowthArrest.IsActive(pawn))
                    ? state.nominalHungerRatio : state.hungerRatio;
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
            if (pawn?.ageTracker == null) return Vector2.one;
            ref HotRatio entry = ref RatioEntry(pawn);
            if (!entry.drawReady)
            {
                entry.draw = CalculateDrawRatio(pawn);
                entry.drawReady = true;
            }
            return entry.draw;
        }

        private static Vector2 CalculateDrawRatio(Pawn pawn)
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
