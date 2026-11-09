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
        private const long TicksPerBiologicalDay = 60000L;
        private const double TicksPerBiologicalYear = 3600000d;

        private sealed class FactorPair
        {
            internal readonly float from;
            internal readonly float to;

            internal FactorPair(float from, float to)
            {
                this.from = from;
                this.to = to;
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
            internal float graphicProgress;
            internal float bodyRatio = 1f;
            internal float healthRatio = 1f;
            internal float hungerRatio = 1f;
            internal float marketRatio = 1f;
            internal Vector2 drawRatio = Vector2.one;
            internal Vector2 interpolatedDrawRatio = Vector2.one;
            internal bool boundsReady;
            internal Gender graphicGender;
        }

        private static readonly Dictionary<ThingDef, Segment[]> RaceSegments =
            new Dictionary<ThingDef, Segment[]>();
        private static readonly ConditionalWeakTable<Pawn, Snapshot> Snapshots =
            new ConditionalWeakTable<Pawn, Snapshot>();

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
                // LifeStageDef is applied before a StatPart. A zero current
                // factor cannot be corrected using a multiplicative ratio.
                float first = GetDefinedStatFactor(from, stat);
                float second = GetDefinedStatFactor(to, stat);
                if (!Positive(first) || !FiniteNonnegative(second)
                    || Math.Abs(first - second) < 0.000001f)
                    continue;
                result.Add(stat, new FactorPair(first, second));
                affected.Add(stat);
            }
            return result;
        }

        private static float GetDefinedStatFactor(LifeStageDef stage, StatDef stat)
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
                || (ZoologyMod.Settings != null && ZoologyMod.Settings.DisableAllRuntimePatches))
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
                || snapshot.graphicGender != pawn.gender)
            {
                snapshot.segment = segment;
                snapshot.stageIndex = stageIndex;
                snapshot.biologicalDay = biologicalDay;
                snapshot.kind = pawn.kindDef;
                snapshot.graphicGender = pawn.gender;
                snapshot.boundsReady = false;

                long sampled = biologicalDay * TicksPerBiologicalDay;
                float t = (float)((double)(sampled - segment.startTick)
                    / (segment.endTick - segment.startTick));
                snapshot.progress = Mathf.Clamp01(t);
                // The last biological day must be the visual endpoint,
                // not an arbitrary fraction below it. Keep the original
                // progress for biological stats and same-artwork growth.
                snapshot.graphicProgress = segment.lastSampleTick > segment.firstSampleTick
                    ? Mathf.Clamp01((float)((double)(sampled - segment.firstSampleTick)
                        / (segment.lastSampleTick - segment.firstSampleTick)))
                    : snapshot.progress;
                float fraction = snapshot.progress;

                snapshot.bodyRatio = LerpRatio(segment.from.bodySizeFactor,
                    segment.to.bodySizeFactor, fraction);
                snapshot.healthRatio = LerpRatio(segment.from.healthScaleFactor,
                    segment.to.healthScaleFactor, fraction);
                snapshot.hungerRatio = LerpRatio(segment.from.hungerRateFactor,
                    segment.to.hungerRateFactor, fraction);
                snapshot.marketRatio = LerpRatio(segment.from.marketValueFactor,
                    segment.to.marketValueFactor, fraction);
                snapshot.drawRatio = GraphicScale(pawn, stageIndex, segment, fraction);
            }
            return true;
        }

        private static Vector2 GraphicScale(Pawn pawn, int index, Segment segment, float t)
        {
            var stages = pawn.kindDef?.lifeStages;
            var ages = pawn.RaceProps.lifeStageAges;
            if (stages == null || ages == null || index < 0
                || index + 1 >= stages.Count || stages.Count != ages.Count)
                return Vector2.one;

            var currentGraphic = pawn.gender == Gender.Female
                ? stages[index]?.femaleGraphicData ?? stages[index]?.bodyGraphicData
                : stages[index]?.bodyGraphicData;
            var nextGraphic = pawn.gender == Gender.Female
                ? stages[index + 1]?.femaleGraphicData ?? stages[index + 1]?.bodyGraphicData
                : stages[index + 1]?.bodyGraphicData;
            if (currentGraphic == null || nextGraphic == null
                || !Positive(currentGraphic.drawSize.x) || !Positive(currentGraphic.drawSize.y)
                || !Positive(nextGraphic.drawSize.x) || !Positive(nextGraphic.drawSize.y)
                || !Positive(segment.from.bodySizeFactor) || !Positive(segment.to.bodySizeFactor))
                return Vector2.one;

            // Interpolate the footprint, not just the stage BodySize relative
            // to adult art: that approach cancelled each stage's drawSize.
            // Factoring drawSize by sqrt(BodySize) preserves the biological
            // growth curve while honoring independently authored graphic sizes.
            // At t=0 this transform is exactly (1,1); at t=1 the resulting
            // footprint exactly matches the next graphic's native drawSize.
            // Consequently a stage switch cannot add an artificial size jump.
            float currentBody = Mathf.Lerp(segment.from.bodySizeFactor,
                segment.to.bodySizeFactor, t);
            float bodyScale = Mathf.Sqrt(currentBody);
            float fromScale = 1f / Mathf.Sqrt(segment.from.bodySizeFactor);
            float toScale = 1f / Mathf.Sqrt(segment.to.bodySizeFactor);
            return new Vector2(
                bodyScale * Mathf.Lerp(currentGraphic.drawSize.x * fromScale,
                    nextGraphic.drawSize.x * toScale, t) / currentGraphic.drawSize.x,
                bodyScale * Mathf.Lerp(currentGraphic.drawSize.y * fromScale,
                    nextGraphic.drawSize.y * toScale, t) / currentGraphic.drawSize.y);
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

            if (ZoologyMod.Settings?.DisableAllRuntimePatches == true)
                return "runtime Harmony patches are disabled in Zoology settings";

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

        // Debug-only access to the unbounded daily scale for comparison
        // with the visible bounds of the next stage.
        internal static Vector2 RawDrawRatio(Pawn pawn)
        {
            Snapshot state;
            return TryGetSnapshot(pawn, out state) ? state.drawRatio : Vector2.one;
        }

        internal static float GraphicProgress(Pawn pawn)
        {
            Snapshot state;
            return TryGetSnapshot(pawn, out state) ? state.graphicProgress : 0f;
        }

        internal static float BodyRatio(Pawn pawn)
        {
            Snapshot state;
            return TryGetSnapshot(pawn, out state) ? state.bodyRatio : 1f;
        }

        internal static float HealthRatio(Pawn pawn)
        {
            Snapshot state;
            return TryGetSnapshot(pawn, out state) ? state.healthRatio : 1f;
        }

        internal static float HungerRatio(Pawn pawn)
        {
            Snapshot state;
            return TryGetSnapshot(pawn, out state) ? state.hungerRatio : 1f;
        }

        internal static float MarketRatio(Pawn pawn)
        {
            Snapshot state;
            return TryGetSnapshot(pawn, out state) ? state.marketRatio : 1f;
        }

        internal static Vector2 DrawRatio(Pawn pawn)
        {
            Snapshot state;
            if (!TryGetSnapshot(pawn, out state))
                return Vector2.one;

            if (!state.boundsReady || state.graphicGender != pawn.gender)
            {
                state.interpolatedDrawRatio = ZoologyAnimalGraphicBounds.Interpolate(
                    pawn.kindDef, state.stageIndex, pawn.gender == Gender.Female,
                    state.drawRatio, state.graphicProgress);
                state.graphicGender = pawn.gender;
                state.boundsReady = true;
            }
            return state.interpolatedDrawRatio;
        }

        internal static bool TryGetStatRatio(Pawn pawn, StatDef stat, out float ratio)
        {
            ratio = 1f;
            Snapshot state;
            if (stat == null || !TryGetSnapshot(pawn, out state))
                return false;

            FactorPair pair;
            if (!state.segment.statFactors.TryGetValue(stat, out pair))
                return false;
            ratio = Mathf.Lerp(pair.from, pair.to, state.progress) / pair.from;
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
            LifeStageDef from, to;
            float t;
            if (!TryGetStages(pawn, out from, out to, out t))
                return false;
            float first, second;
            ZoologyLifeStageProfileInstaller.TryGetArmorOffset(from, sharp, out first);
            ZoologyLifeStageProfileInstaller.TryGetArmorOffset(to, sharp, out second);
            offset = Mathf.Lerp(first, second, t);
            return true;
        }

        internal static bool TryGetDurability(Pawn pawn, out float factor)
        {
            factor = 1f;
            LifeStageDef from, to;
            float t;
            if (!TryGetStages(pawn, out from, out to, out t))
                return false;
            float first, second;
            if (!ZoologyLifeStageProfileInstaller.TryGetArmorDurabilityFactor(from, out first))
                first = 1f;
            if (!ZoologyLifeStageProfileInstaller.TryGetArmorDurabilityFactor(to, out second))
                second = 1f;
            factor = Mathf.Lerp(first, second, t);
            return true;
        }

        internal static bool TryGetToolFactor(
            Pawn pawn, Tool tool, GrowthToolFactor kind, out float factor)
        {
            factor = 1f;
            LifeStageDef from, to;
            float t;
            if (!TryGetStages(pawn, out from, out to, out t))
                return false;

            ZoologyLifeStageToolFactor first, second;
            bool hasFirst = ZoologyLifeStageProfileInstaller.TryGetToolFactor(from, tool, out first);
            bool hasSecond = ZoologyLifeStageProfileInstaller.TryGetToolFactor(to, tool, out second);
            if (!hasFirst && !hasSecond)
                return false;

            float a = hasFirst ? SelectToolFactor(first, kind) : 1f;
            float b = hasSecond ? SelectToolFactor(second, kind) : 1f;
            factor = Mathf.Lerp(a, b, t);
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
