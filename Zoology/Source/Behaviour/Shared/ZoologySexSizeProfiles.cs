using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZoologyMod
{
    // Every number in this catalog is evaluated in AnimalStats/SexDimorphism.
    public sealed class ZoologySexSizeCatalogDef : Def
    {
        public List<ZoologySexSizeProfile> profiles = new List<ZoologySexSizeProfile>();
    }

    public sealed class ZoologySexSizeProfile
    {
        public string animalDefName;
        public bool sexualDimorphism;
        public bool prolongedAdultGrowth;
        public float baselineMassKg;
        public ZoologySexSizeCurve male;
        public ZoologySexSizeCurve female;
    }

    public sealed class ZoologySexSizeCurve
    {
        public ZoologySexSizeStage adult;
        public ZoologySexSizeStage largeAdult;
    }

    public sealed class ZoologySexSizeValues
    {
        public float bodySize = 1f;
        public float healthScale = 1f;
        public float moveSpeed = 1f;
        public float hungerRate = 1f;
        public float filthRate = 1f;
        public float armorDurability = 1f;
        public float linearScale = 1f;

        internal static ZoologySexSizeValues Between(ZoologySexSizeValues a,
            ZoologySexSizeValues b, float t)
        {
            return new ZoologySexSizeValues
            {
                bodySize = Mathf.Lerp(a.bodySize, b.bodySize, t),
                healthScale = Mathf.Lerp(a.healthScale, b.healthScale, t),
                moveSpeed = Mathf.Lerp(a.moveSpeed, b.moveSpeed, t),
                hungerRate = Mathf.Lerp(a.hungerRate, b.hungerRate, t),
                filthRate = Mathf.Lerp(a.filthRate, b.filthRate, t),
                armorDurability = Mathf.Lerp(a.armorDurability, b.armorDurability, t),
                linearScale = Mathf.Lerp(a.linearScale, b.linearScale, t)
            };
        }
    }

    public sealed class ZoologySexSizeStage
    {
        public float minMassKg;
        public float meanMassKg;
        public float maxMassKg;
        // Adult extrema can be bounded below the species' lifetime maximum.
        // Physical extreme factors in AnimalStats refer to this mass.
        public float referenceMaxMassKg;
        public ZoologySexSizeValues minimum;
        public ZoologySexSizeValues average;
        public ZoologySexSizeValues maximum;

        internal bool Valid => minimum != null && average != null && maximum != null
            && minMassKg > 0f && minMassKg <= meanMassKg
            && meanMassKg <= maxMassKg && maxMassKg <= referenceMaxMassKg;

        internal ZoologySexSizeValues Evaluate(float quantile, out float actualMass)
        {
            if (!Valid)
            {
                actualMass = meanMassKg;
                return average;
            }
            if (minMassKg == maxMassKg)
            {
                actualMass = meanMassKg;
                return average;
            }

            // The lowest-degree density vanishing at each measured extreme:
            // linearly rising to the mean and falling thereafter. The two
            // tail weights are fixed by the measured mean and bounds, not a
            // guessed "giant" probability. Its integral has exactly the
            // input mean, and extreme individual masses have zero density.
            float lowerWeight = (maxMassKg - meanMassKg) / (maxMassKg - minMassKg);
            quantile = Mathf.Clamp01(quantile);
            if (quantile <= lowerWeight && lowerWeight > 0f)
            {
                float fraction = Mathf.Sqrt(quantile / lowerWeight);
                actualMass = Mathf.Lerp(minMassKg, meanMassKg, fraction);
                return ZoologySexSizeValues.Between(minimum, average, fraction);
            }
            if (lowerWeight >= 1f)
            {
                actualMass = meanMassKg;
                return average;
            }
            float tail = 1f - Mathf.Sqrt((1f - quantile) / (1f - lowerWeight));
            actualMass = Mathf.Lerp(meanMassKg, maxMassKg, tail);
            // Adult effective maximum may be smaller than the lifelong bound;
            // do not prematurely assign the full lifetime extreme's stat.
            float physicalFraction = referenceMaxMassKg > meanMassKg
                ? (actualMass - meanMassKg) / (referenceMaxMassKg - meanMassKg) : 0f;
            return ZoologySexSizeValues.Between(average, maximum,
                Mathf.Clamp01(physicalFraction));
        }
    }

    public sealed class CompProperties_ZoologyIndividualSize : CompProperties
    {
        public CompProperties_ZoologyIndividualSize()
        {
            compClass = typeof(CompZoologyIndividualSize);
        }
    }

    // No CompTick/CompTickRare/CompTickLong: one stable draw per pawn.
    public sealed class CompZoologyIndividualSize : ThingComp
    {
        private float lifetimeQuantile = -1f;

        internal float LifetimeQuantile
        {
            get
            {
                if (lifetimeQuantile < 0f)
                    lifetimeQuantile = Rand.Value;
                return lifetimeQuantile;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Scribe.mode == LoadSaveMode.Saving && lifetimeQuantile < 0f)
                lifetimeQuantile = Rand.Value;
            Scribe_Values.Look(ref lifetimeQuantile, "zoologyLifetimeSizeQuantile", -1f);
        }
    }

    // Stores only already interpolated daily ratios. Fully mature animals
    // are immutable cache entries until sex, kind or stage changes.
    internal static class ZoologySexSizeMechanics
    {
        internal sealed class SizeSnapshot
        {
            internal int ageIndex = -1;
            internal long ageDay = -1;
            internal Gender gender;
            internal float quantile = -1f;
            internal float body = 1f;
            internal float health = 1f;
            internal float hunger = 1f;
            internal float speed = 1f;
            internal float filth = 1f;
            internal float durability = 1f;
            internal float market = 1f;
            internal float draw = 1f;
            internal bool growing;
        }

        private struct Endpoint
        {
            internal float body;
            internal float health;
            internal float hunger;
            internal float speed;
            internal float filth;
            internal float durability;
            internal float draw;
            internal float market;
        }

        private static readonly Dictionary<ThingDef, ZoologySexSizeProfile> Profiles =
            new Dictionary<ThingDef, ZoologySexSizeProfile>();
        private static readonly ConditionalWeakTable<Pawn, SizeSnapshot> Snapshots =
            new ConditionalWeakTable<Pawn, SizeSnapshot>();
        private static bool installed;

        internal static void Install()
        {
            if (installed)
                return;
            installed = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var catalog in DefDatabase<ZoologySexSizeCatalogDef>.AllDefsListForReading)
            {
                if (catalog?.profiles == null)
                    continue;
                foreach (var profile in catalog.profiles)
                {
                    if (profile == null || string.IsNullOrEmpty(profile.animalDefName)
                        || profile.male?.adult?.Valid != true
                        || profile.female?.adult?.Valid != true
                        || !seen.Add(profile.animalDefName))
                        continue;
                    var race = DefDatabase<ThingDef>.GetNamedSilentFail(profile.animalDefName);
                    if (race?.race?.Animal != true)
                        continue;
                    Profiles.Add(race, profile);
                    // Copy the per-ThingDef comp array: never mutate a shared
                    // parent/abstract ThingDef's component collection.
                    race.comps = race.comps == null
                        ? new List<CompProperties>() : new List<CompProperties>(race.comps);
                    if (!race.comps.Exists(p => p is CompProperties_ZoologyIndividualSize))
                        race.comps.Add(new CompProperties_ZoologyIndividualSize());
                }
            }
            if (Profiles.Count > 0)
                ZoologySexSizeStatPart.Install();
            Log.Message("[Zoology] Loaded " + Profiles.Count
                + " persistent individual size profiles.");
        }

        internal static bool HasProfile(ThingDef species,
            out ZoologySexSizeProfile profile)
        {
            return species != null && Profiles.TryGetValue(species, out profile);
        }

        internal static bool TryGet(Pawn pawn, out SizeSnapshot snapshot)
        {
            snapshot = null;
            if (!ZoologyAnimalGrowth.Enabled || pawn?.RaceProps?.Animal != true
                || !Profiles.TryGetValue(pawn.def, out var profile)
                || pawn.ageTracker == null)
                return false;
            var comp = pawn.TryGetComp<CompZoologyIndividualSize>();
            if (comp == null)
                return false;
            int index = pawn.ageTracker.CurLifeStageIndex;
            if (index < 0)
                return false;
            float quantile = comp.LifetimeQuantile;
            long day = pawn.ageTracker.AgeBiologicalTicks / 60000L;
            snapshot = Snapshots.GetValue(pawn, _ => new SizeSnapshot());
            if (snapshot.ageIndex == index && snapshot.gender == pawn.gender
                && snapshot.quantile == quantile
                && (!snapshot.growing || snapshot.ageDay == day))
                return true;

            var curve = pawn.gender == Gender.Female ? profile.female : profile.male;
            LifeStageDef first = pawn.ageTracker.CurLifeStage, next;
            float t;
            bool growing = ZoologyAnimalGrowth.TryGetStages(pawn, out first, out next, out t);
            if (!growing)
            {
                first = pawn.ageTracker.CurLifeStage;
                next = null;
                t = 0f;
            }
            Endpoint a = BuildEndpoint(pawn, curve, first, quantile);
            Endpoint b = growing ? BuildEndpoint(pawn, curve, next, quantile) : a;
            float baseBody = growing ? Mathf.Lerp(first.bodySizeFactor,
                next.bodySizeFactor, t) : first.bodySizeFactor;
            float baseHealth = growing ? Mathf.Lerp(first.healthScaleFactor,
                next.healthScaleFactor, t) : first.healthScaleFactor;
            float baseHunger = growing ? Mathf.Lerp(first.hungerRateFactor,
                next.hungerRateFactor, t) : first.hungerRateFactor;
            float baseSpeed = growing ? Mathf.Lerp(StageStat(first, "MoveSpeed"),
                StageStat(next, "MoveSpeed"), t) : StageStat(first, "MoveSpeed");
            float baseFilth = growing ? Mathf.Lerp(StageStat(first, "FilthRate"),
                StageStat(next, "FilthRate"), t) : StageStat(first, "FilthRate");
            float baseArmor = growing ? Mathf.Lerp(StageArmor(first),
                StageArmor(next), t) : StageArmor(first);

            snapshot.body = Ratio(Mathf.Lerp(a.body, b.body, t), baseBody);
            snapshot.health = Ratio(Mathf.Lerp(a.health, b.health, t), baseHealth);
            snapshot.hunger = Ratio(Mathf.Lerp(a.hunger, b.hunger, t), baseHunger);
            snapshot.speed = Ratio(Mathf.Lerp(a.speed, b.speed, t), baseSpeed);
            snapshot.filth = Ratio(Mathf.Lerp(a.filth, b.filth, t), baseFilth);
            snapshot.durability = Ratio(Mathf.Lerp(a.durability, b.durability, t), baseArmor);
            snapshot.draw = Mathf.Lerp(a.draw, b.draw, t);
            snapshot.market = Mathf.Lerp(a.market, b.market, t);
            snapshot.ageIndex = index;
            snapshot.ageDay = day;
            snapshot.gender = pawn.gender;
            snapshot.quantile = quantile;
            snapshot.growing = growing;
            return true;
        }

        private static Endpoint BuildEndpoint(Pawn pawn, ZoologySexSizeCurve curve,
            LifeStageDef stage, float quantile)
        {
            var result = new Endpoint
            {
                body = stage?.bodySizeFactor ?? 1f,
                health = stage?.healthScaleFactor ?? 1f,
                hunger = stage?.hungerRateFactor ?? 1f,
                speed = StageStat(stage, "MoveSpeed"),
                filth = StageStat(stage, "FilthRate"),
                durability = StageArmor(stage),
                draw = 1f,
                market = 1f
            };
            if (stage == null)
                return result;
            string origin = stage.GetModExtension<ZoologyLifeStageOrigin>()
                ?.sourceStage?.defName ?? stage.defName;
            bool late = stage.defName == "ZoologyStage_" + pawn.def.defName
                + "_AnimalLargeAdult";
            bool ordinaryAdult = origin == "AnimalAdult";
            bool royalAdult = stage.defName.StartsWith("VAERoy_AnimalAdult",
                StringComparison.Ordinal) || origin.StartsWith("VAERoy_AnimalAdult",
                StringComparison.Ordinal);
            if (!late && !ordinaryAdult && !royalAdult)
                return result;

            var model = late ? curve.largeAdult : curve.adult;
            if (model?.Valid != true)
                model = curve.adult;
            if (model?.Valid != true)
                return result;
            float mass;
            var val = model.Evaluate(quantile, out mass);
            float meanMass = model.meanMassKg;
            if (royalAdult)
            {
                // Royal Megachicken's actual age-specific 25-ton mass remains
                // in LifeStageProfiles. Only relative sexual proportions apply.
                float normal = curve.adult.average.bodySize;
                if (normal > 0f)
                    result.body *= val.bodySize / normal;
                result.health *= val.healthScale;
                result.hunger *= val.hungerRate;
                result.speed *= val.moveSpeed;
                result.filth *= val.filthRate;
                result.durability *= val.armorDurability;
            }
            else
            {
                float baseline = pawn.RaceProps.baseBodySize;
                if (baseline > 0f)
                    result.body = val.bodySize / baseline;
                result.health = val.healthScale;
                result.hunger = val.hungerRate;
                result.speed = val.moveSpeed;
                result.filth = val.filthRate;
                result.durability = val.armorDurability;
            }
            result.draw = model.average.linearScale > 0f
                ? val.linearScale / model.average.linearScale : 1f;
            result.market = meanMass > 0f ? mass / meanMass : 1f;
            return result;
        }

        private static float StageStat(LifeStageDef stage, string stat)
        {
            if (stage?.statFactors != null)
                foreach (var modifier in stage.statFactors)
                    if (modifier?.stat?.defName == stat)
                        return modifier.value;
            return 1f;
        }

        private static float StageArmor(LifeStageDef stage)
        {
            float factor;
            return ZoologyLifeStageProfileInstaller.TryGetArmorDurabilityFactor(
                stage, out factor) ? factor : 1f;
        }

        private static float Ratio(float desired, float baseline)
        {
            return baseline > 0f && desired >= 0f ? desired / baseline : 1f;
        }

        internal static float BodyRatio(Pawn pawn)
        {
            return TryGet(pawn, out var s) ? s.body : 1f;
        }

        internal static float HealthRatio(Pawn pawn)
        {
            return TryGet(pawn, out var s) ? s.health : 1f;
        }

        internal static float HungerRatio(Pawn pawn)
        {
            return TryGet(pawn, out var s) ? s.hunger : 1f;
        }

        internal static float DrawRatio(Pawn pawn)
        {
            return TryGet(pawn, out var s) ? s.draw : 1f;
        }

        internal static float DurabilityRatio(Pawn pawn)
        {
            return TryGet(pawn, out var s) ? s.durability : 1f;
        }

        internal static bool TryGetStatRatio(Pawn pawn, StatDef stat, out float ratio)
        {
            ratio = 1f;
            if (!TryGet(pawn, out var s) || stat == null)
                return false;
            switch (stat.defName)
            {
                case "MoveSpeed": ratio = s.speed; return true;
                case "FilthRate": ratio = s.filth; return true;
                case "MarketValue": ratio = s.market; return true;
                default: return false;
            }
        }
    }

    public sealed class ZoologySexSizeStatPart : StatPart
    {
        internal static void Install()
        {
            foreach (var name in new[] { "MoveSpeed", "FilthRate", "MarketValue" })
            {
                var stat = DefDatabase<StatDef>.GetNamedSilentFail(name);
                if (stat == null)
                    continue;
                if (stat.parts == null)
                    stat.parts = new List<StatPart>();
                if (stat.parts.Exists(p => p is ZoologySexSizeStatPart))
                    continue;
                stat.parts.Add(new ZoologySexSizeStatPart { parentStat = stat });
            }
        }

        public override void TransformValue(StatRequest request, ref float value)
        {
            if (request.Thing is Pawn pawn
                && ZoologySexSizeMechanics.TryGetStatRatio(pawn, parentStat, out float ratio))
                value *= ratio;
        }

        public override string ExplanationPart(StatRequest request)
        {
            if (request.Thing is Pawn pawn
                && ZoologySexSizeMechanics.TryGetStatRatio(pawn, parentStat, out float ratio)
                && Math.Abs(ratio - 1f) > 0.000001f)
                return "Zoology: individual size x" + ratio.ToString("0.###");
            return null;
        }
    }
}
