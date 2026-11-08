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
                    if (current == null || next == null
                        || !IsPhysicalGrowthTransition(current, next)
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

        // Metamorphic stages must not be treated as progressively enlarged sprites.
        private static bool IsPhysicalGrowthTransition(LifeStageDef current, LifeStageDef next)
        {
            string a = current.GetModExtension<ZoologyLifeStageOrigin>()?.sourceStage?.defName
                ?? current.defName;
            string b = next.GetModExtension<ZoologyLifeStageOrigin>()?.sourceStage?.defName
                ?? next.defName;
            return !a.StartsWith("Spectrobionta", StringComparison.Ordinal)
                && !b.StartsWith("Spectrobionta", StringComparison.Ordinal);
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
                || snapshot.biologicalDay != biologicalDay || snapshot.kind != pawn.kindDef)
            {
                snapshot.segment = segment;
                snapshot.stageIndex = stageIndex;
                snapshot.biologicalDay = biologicalDay;
                snapshot.kind = pawn.kindDef;

                long sampled = biologicalDay * TicksPerBiologicalDay;
                float t = (float)((double)(sampled - segment.startTick)
                    / (segment.endTick - segment.startTick));
                snapshot.progress = Mathf.Clamp01(t);
                float fraction = snapshot.progress;

                snapshot.bodyRatio = LerpRatio(segment.from.bodySizeFactor,
                    segment.to.bodySizeFactor, fraction);
                snapshot.healthRatio = LerpRatio(segment.from.healthScaleFactor,
                    segment.to.healthScaleFactor, fraction);
                snapshot.hungerRatio = LerpRatio(segment.from.hungerRateFactor,
                    segment.to.hungerRateFactor, fraction);
                snapshot.marketRatio = LerpRatio(segment.from.marketValueFactor,
                    segment.to.marketValueFactor, fraction);
                snapshot.drawRatio = GraphicScale(pawn.kindDef, stageIndex, fraction);
            }
            return true;
        }

        private static Vector2 GraphicScale(PawnKindDef kind, int index, float t)
        {
            if (kind?.lifeStages == null || index < 0 || index + 1 >= kind.lifeStages.Count)
                return Vector2.one;

            var first = kind.lifeStages[index]?.bodyGraphicData;
            var next = kind.lifeStages[index + 1]?.bodyGraphicData;
            if (first == null || next == null
                || !Positive(first.drawSize.x) || !Positive(first.drawSize.y)
                || !Positive(next.drawSize.x) || !Positive(next.drawSize.y))
                return Vector2.one;

            // Interpolate the actual per-stage art dimensions, not BodySize in kg.
            // This makes the rendered footprint continuous when the art switches.
            return new Vector2(
                Mathf.Lerp(first.drawSize.x, next.drawSize.x, t) / first.drawSize.x,
                Mathf.Lerp(first.drawSize.y, next.drawSize.y, t) / first.drawSize.y);
        }

        private static float LerpRatio(float from, float to, float t)
        {
            return Positive(from) && FiniteNonnegative(to)
                ? Mathf.Lerp(from, to, t) / from : 1f;
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
            return TryGetSnapshot(pawn, out state) ? state.drawRatio : Vector2.one;
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
