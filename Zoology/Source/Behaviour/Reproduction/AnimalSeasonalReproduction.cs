using System;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public sealed class ModExtension_SeasonalReproduction : DefModExtension
    {
        public int matingSeasonMask = 15;
        public int embryonicDiapauseTicks;
        public int reproductivePauseTicks;
        public int birthSeasonMask;
        public float thermalBreedingMinimum = float.NaN;
        public float dormancyThreshold = float.NaN;
        public float captiveGestationDays = -1f;
        public float captiveDiapauseDays = -1f;
        public float captiveRealDiapauseDays = -1f;
        public float captiveEggFormationDays = -1f;
        public float captiveRealEggFormationDays = -1f;
        public float captiveReproductivePauseDays = -1f;
        public float captiveIncubationDays = -1f;
        public float realisticLitterMean = -1f;
        public IntRange realisticEggCountRange;
        private SimpleCurve realisticLitterCurve;

        internal SimpleCurve RealisticLitterCurve
        {
            get
            {
                if (realisticLitterCurve != null) return realisticLitterCurve;
                float mean = realisticLitterMean;
                if (mean < 1f) return null;
                if (mean == 1f)
                    return realisticLitterCurve = new SimpleCurve
                    {
                        new CurvePoint(0.5f, 0f), new CurvePoint(1f, 1f), new CurvePoint(1.5f, 0f)
                    };
                float width = Math.Min(0.8f * mean, mean - 0.5f);
                return realisticLitterCurve = new SimpleCurve
                {
                    new CurvePoint(mean - width, 0f),
                    new CurvePoint(mean - 0.75f * width, 0.2f),
                    new CurvePoint(mean - 0.25f * width, 1f),
                    new CurvePoint(mean + 0.25f * width, 1f),
                    new CurvePoint(mean + 0.75f * width, 0.2f),
                    new CurvePoint(mean + width, 0f)
                };
            }
        }
    }

    internal static class AnimalSeasonalReproduction
    {
        private sealed class Calendar
        {
            internal long longitudeOffset;
            internal bool southern;
            internal float[] monthlyMeans;
        }

        // Lazy per-map cache; no map or pawn ticking component.
        private static readonly ConditionalWeakTable<Map, Calendar> calendars = new();
        private static AccessTools.FieldRef<Hediff, int> pregnancyId;
        private static bool bindingAttempted;

        internal static bool CanTrackPregnancy
        {
            get
            {
                if (bindingAttempted) return pregnancyId != null;
                bindingAttempted = true;
                FieldInfo loadIdField = AccessTools.Field(typeof(Hediff), "loadID");
                bool hasTick = AccessTools.DeclaredMethod(typeof(Hediff_Pregnant), "TickInterval", new[] { typeof(int) }) != null
                    || AccessTools.DeclaredMethod(typeof(Hediff_Pregnant), "Tick", Type.EmptyTypes) != null;
                if (hasTick && loadIdField?.FieldType == typeof(int))
                    pregnancyId = AccessTools.FieldRefAccess<Hediff, int>("loadID");
                else
                    Log.WarningOnce("[Zoology] The native pregnancy timer is unavailable; embryonic diapause cannot be applied.", 192751484);
                return pregnancyId != null;
            }
        }

        internal static ModExtension_SeasonalReproduction Profile(Pawn pawn) =>
            DefModExtensionCache<ModExtension_SeasonalReproduction>.Get(pawn?.def);

        private const int YearTicks = 60 * GenDate.TicksPerDay;
        private const int SeasonTicks = 15 * GenDate.TicksPerDay;
        private const int MonthTicks = 5 * GenDate.TicksPerDay;

        private static Calendar ForMap(Map map) =>
            calendars.GetValue(map, m =>
            {
                var location = Find.WorldGrid.LongLatOf(m.Tile);
                return new Calendar
                {
                    longitudeOffset = (long)GenDate.TimeZoneAt(location.x) * 2500,
                    southern = location.y < 0f
                };
            });

        private static int YearPhase(Map map, bool local = true)
        {
            Calendar calendar = ForMap(map);
            long ticks = Find.TickManager.TicksAbs + calendar.longitudeOffset;
            if (local && calendar.southern) ticks += YearTicks / 2;
            return (int)((ticks % YearTicks + YearTicks) % YearTicks);
        }

        private static int BitAt(long phase) =>
            1 << (int)(((phase % YearTicks + YearTicks) % YearTicks) / SeasonTicks);

        internal static int SeasonBit(Map map) => map == null ? 15 : BitAt(YearPhase(map));

        private static Calendar Climate(Map map)
        {
            Calendar calendar = ForMap(map);
            if (calendar.monthlyMeans != null) return calendar;
            calendar.monthlyMeans = new float[12];
            for (int i = 0; i < 12; i++)
                calendar.monthlyMeans[i] = GenTemperature.AverageTemperatureAtTileForTwelfth(map.Tile, (Twelfth)i);
            return calendar;
        }

        private static float SeasonalMeanAt(Map map, long ticksFromNow)
        {
            long ticks = (long)Find.TickManager.TicksAbs + ticksFromNow;
            return Climate(map).monthlyMeans[(int)((ticks % YearTicks + YearTicks) % YearTicks) / MonthTicks];
        }

        internal static float SeasonalMean(Map map) => SeasonalMeanAt(map, 0);

        internal static bool Managed(Pawn pawn) => pawn?.Faction != null;

        internal static bool RealisticCaptive => ModConstants.Settings?.RealisticCaptiveGestation == true;

        internal static bool AcceleratedCaptive(Pawn pawn) =>
            ModConstants.AdvancedReproduction && Managed(pawn) && !RealisticCaptive;

        private static int ToTicks(float days) =>
            days <= 0f ? 0 : (int)Math.Min(int.MaxValue,
                Math.Round((double)days * GenDate.TicksPerDay, MidpointRounding.AwayFromZero));

        internal static float GestationDays(Pawn pawn)
        {
            float days = pawn.RaceProps.gestationPeriodDays;
            if (!ModConstants.AdvancedReproduction || !Managed(pawn) || RealisticCaptive) return days;
            float captive = Profile(pawn)?.captiveGestationDays ?? -1f;
            return captive > 0f ? captive : days;
        }

        internal static float EggFormationDays(CompEggLayer layer)
        {
            float days = layer.Props.eggLayIntervalDays;
            if (layer.parent is not Pawn pawn || !ModConstants.AdvancedReproduction || !Managed(pawn))
                return days;
            var profile = Profile(pawn);
            if (profile == null) return days;
            if (profile.captiveRealEggFormationDays > 0f)
            {
                float duration = RealisticCaptive ? profile.captiveRealEggFormationDays
                    : profile.captiveEggFormationDays;
                if (duration > 0f)
                {
                    IntRange standardRange = layer.Props.eggCountRange;
                    float standard = (standardRange.min + (float)standardRange.max) * 0.5f;
                    float count = AnimalReproductionProfiles.ClutchMean(pawn, standard,
                        beginning: ZoologyReproductiveEnergetics.EggProgress(layer) <= 0f);
                    return standard > 0f && count > 0f ? duration * count / standard : duration;
                }
            }
            return !RealisticCaptive && profile.captiveEggFormationDays > 0f
                ? profile.captiveEggFormationDays : days;
        }

        private static int LayingMask(ModExtension_SeasonalReproduction profile) =>
            profile.birthSeasonMask != 0 ? profile.birthSeasonMask
                : !float.IsNaN(profile.thermalBreedingMinimum) ? 15 : profile.matingSeasonMask;

        private static bool InThermalBreedingWindow(Pawn pawn, ModExtension_SeasonalReproduction profile)
        {
            Map map = pawn.MapHeld;
            return Managed(pawn) || map == null || float.IsNaN(profile.thermalBreedingMinimum)
                || SeasonalMean(map) >= profile.thermalBreedingMinimum;
        }

        private static bool InMatingWindow(Pawn pawn, ModExtension_SeasonalReproduction profile)
        {
            Map map = pawn.MapHeld;
            if (Managed(pawn) || map == null) return true;
            if (!float.IsNaN(profile.thermalBreedingMinimum))
                return InThermalBreedingWindow(pawn, profile);
            return (profile.matingSeasonMask & SeasonBit(map)) != 0;
        }

        internal static bool HasDiapauseProfile(Pawn pawn) => HasDiapauseProfile(Profile(pawn));

        private static bool HasDiapauseProfile(ModExtension_SeasonalReproduction profile) =>
            profile != null && (profile.embryonicDiapauseTicks > 0
                || profile.captiveDiapauseDays > 0f || profile.captiveRealDiapauseDays > 0f);

        private static int DiapauseTicks(Pawn pawn, ModExtension_SeasonalReproduction profile)
        {
            if (Managed(pawn))
            {
                float days = RealisticCaptive ? profile.captiveRealDiapauseDays : profile.captiveDiapauseDays;
                if (days >= 0f) return ToTicks(days);
            }
            return profile.embryonicDiapauseTicks;
        }

        private static int PauseTicks(Pawn pawn, ModExtension_SeasonalReproduction profile)
        {
            if (Managed(pawn))
            {
                if (profile.captiveRealEggFormationDays > 0f) return 0;
                if (!RealisticCaptive && profile.captiveReproductivePauseDays >= 0f)
                    return ToTicks(profile.captiveReproductivePauseDays);
            }
            return profile.reproductivePauseTicks;
        }

        private static bool Paused(Pawn pawn, ModExtension_SeasonalReproduction profile) =>
            PauseTicks(pawn, profile) > 0 && pawn.gender != Gender.Male
                && pawn.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState state
                && state.reproductivePauseUntil > GenTicks.TicksGame;

        private static int GestationTicks(Pawn pawn) => ToTicks(GestationDays(pawn));

        internal static float PregnancyDurationForInterval(RaceProperties race, Hediff_Pregnant pregnancy, int delta)
        {
            if (pregnancy?.pawn?.RaceProps?.Animal != true) return race.gestationPeriodDays;
            float days = GestationDays(pregnancy.pawn);
            int until = DiapauseUntil(pregnancy);
            if (until <= 0 || delta <= 0) return days;
            int activeTicks = Math.Min(delta, Math.Max(0, GenTicks.TicksGame - until));
            if (activeTicks == 0) return float.PositiveInfinity;
            return days * ((float)delta / activeTicks);
        }

        internal static float PregnancyDurationForTick(RaceProperties race, Hediff_Pregnant pregnancy) =>
            PregnancyDurationForInterval(race, pregnancy, 1);

        internal static float EggDurationForTick(CompProperties_EggLayer props, CompEggLayer layer) =>
            AllowsEggProgress(layer) ? EggFormationDays(layer) : float.PositiveInfinity;

        internal static float IncubationDays(float calendarDays, ThingDef race, bool managed)
        {
            if (!ModConstants.AdvancedReproduction || !managed || RealisticCaptive || race == null)
                return calendarDays;
            float captive = DefModExtensionCache<ModExtension_SeasonalReproduction>.Get(race)?.captiveIncubationDays ?? -1f;
            return captive > 0f ? Math.Min(calendarDays, captive) : calendarDays;
        }

        internal static float HatcherDuration(CompProperties_Hatcher props, CompHatcher hatcher)
        {
            if (hatcher.hatcheeFaction == null || !ModConstants.AdvancedReproduction || RealisticCaptive)
                return props.hatcherDaystoHatch;
            return IncubationDays(props.hatcherDaystoHatch,
                hatcher.hatcheeParent?.def ?? props.hatcherPawn?.race, managed: true);
        }

        private static int NextSeasonalFormationDelay(int phase, int formation, int mask, int pause)
        {
            long cycle = (long)pause + formation;
            long years = (cycle + YearTicks / 2) / YearTicks;
            if (mask == 15 || years < 1 || Math.Abs(cycle - years * YearTicks) > 1)
                return pause;

            int index = phase / SeasonTicks;
            if ((mask & (1 << index)) == 0) return pause;

            // Anchor whole-year cycles to the same contiguous laying window.
            int elapsed = phase % SeasonTicks;
            for (int i = 1; i < 4; i++)
            {
                if ((mask & (1 << ((index - i) & 3))) == 0) break;
                elapsed += SeasonTicks;
            }
            long delay = years * YearTicks - elapsed - formation;
            return (int)Math.Min(int.MaxValue, Math.Max(0L, delay));
        }

        internal static bool AllowsMating(Pawn pawn)
        {
            if (!ModConstants.AdvancedReproduction || pawn?.RaceProps?.Animal != true)
                return true;
            if (!pawn.Spawned && AnimalWinterDormancy.IsDormant(pawn)) return false;
            var profile = Profile(pawn);
            if (profile == null) return true;
            if (Paused(pawn, profile) || !InMatingWindow(pawn, profile)) return false;
            Map map = pawn.MapHeld;
            if (map != null && pawn.gender == Gender.Female
                && !Managed(pawn) && profile.birthSeasonMask != 0 && profile.birthSeasonMask != 15
                && pawn.RaceProps.gestationPeriodDays > 0f
                && pawn.def.GetCompProperties<CompProperties_EggLayer>() == null)
                return (profile.birthSeasonMask & BitAt((long)YearPhase(map) + GestationTicks(pawn) + DiapauseTicks(pawn, profile))) != 0;
            return true;
        }

        internal static void BeginDiapause(Pawn female, Hediff pregnancy)
        {
            if (!ModConstants.AdvancedReproduction || female?.RaceProps?.Animal != true
                || pregnancy == null || !CanTrackPregnancy) return;
            var profile = Profile(female);
            if (profile == null || female.TryGetComp<CompAnimalStrategyState>() is not CompAnimalStrategyState state)
                return;
            int delay = DiapauseTicks(female, profile);
            state.diapausePregnancyId = pregnancyId(pregnancy);
            state.embryonicDiapauseDuration = delay;
            state.embryonicDiapauseUntil = delay > 0 ? Deadline(delay) : 0;
        }

        private static int Deadline(int duration) =>
            (int)Math.Min(int.MaxValue, (long)GenTicks.TicksGame + duration);

        internal static int RescaleDiapauseRemaining(int remaining, int previousDuration, int duration)
        {
            if (remaining <= 0 || duration <= 0) return 0;
            if (previousDuration <= 0) return Math.Min(remaining, duration);
            long scaled = (long)Math.Min(remaining, previousDuration) * duration;
            return (int)Math.Max(1L, (scaled + previousDuration / 2L) / previousDuration);
        }

        internal static int DiapauseUntil(Hediff pregnancy)
        {
            if (!ModConstants.AdvancedReproduction || pregnancy?.pawn?.RaceProps?.Animal != true) return 0;
            Pawn pawn = pregnancy.pawn;
            var profile = Profile(pawn);
            if (!HasDiapauseProfile(profile) || !CanTrackPregnancy) return 0;
            var state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null || state.embryonicDiapauseUntil <= 0
                || state.diapausePregnancyId != pregnancyId(pregnancy)) return 0;

            int now = GenTicks.TicksGame;
            int remaining = state.embryonicDiapauseUntil - now;
            if (remaining <= 0) return state.embryonicDiapauseUntil;

            int duration = DiapauseTicks(pawn, profile);
            int previousDuration = state.embryonicDiapauseDuration;
            if (previousDuration <= 0)
                previousDuration = Math.Max(remaining, duration);

            if (state.embryonicDiapauseDuration != duration)
            {
                int adjusted = RescaleDiapauseRemaining(remaining, previousDuration, duration);
                state.embryonicDiapauseUntil = Deadline(adjusted);
                state.embryonicDiapauseDuration = duration;
            }
            return state.embryonicDiapauseUntil;
        }

        internal static bool InDiapause(Hediff pregnancy) =>
            DiapauseUntil(pregnancy) > GenTicks.TicksGame;

        internal static bool AllowsEggProgress(CompEggLayer layer)
        {
            if (!ModConstants.AdvancedReproduction || layer?.parent is not Pawn pawn
                || pawn.gender == Gender.Male || pawn.RaceProps?.Animal != true) return true;
            if (!pawn.Spawned && AnimalWinterDormancy.IsDormant(pawn)) return false;
            var profile = Profile(pawn);
            if (profile == null) return true;
            if (Paused(pawn, profile)) return false;
            if (ZoologyReproductiveEnergetics.EggProgress(layer) > 0f) return true;
            Map map = pawn.MapHeld;
            if (map == null || Managed(pawn)) return true;
            return CanStartEggFormationInSeason(layer, map, profile);
        }

        private static bool CanStartEggFormationInSeason(CompEggLayer layer, Map map,
            ModExtension_SeasonalReproduction profile)
        {
            int mask = LayingMask(profile);
            if (mask == 15 && float.IsNaN(profile.thermalBreedingMinimum)) return true;
            int formation = ToTicks(EggFormationDays(layer));
            return (mask & BitAt((long)YearPhase(map) + formation)) != 0
                && (float.IsNaN(profile.thermalBreedingMinimum)
                    || SeasonalMeanAt(map, formation) >= profile.thermalBreedingMinimum);
        }

        private static bool CanLayInSeason(Pawn pawn, Map map, ModExtension_SeasonalReproduction profile) =>
            InThermalBreedingWindow(pawn, profile) && (LayingMask(profile) & SeasonBit(map)) != 0;

        internal static bool EggSeasonPaused(CompEggLayer layer)
        {
            if (!ModConstants.AdvancedReproduction || layer?.parent is not Pawn pawn
                || pawn.gender != Gender.Female || pawn.RaceProps?.Animal != true || Managed(pawn))
                return false;
            var profile = Profile(pawn);
            Map map = pawn.MapHeld;
            if (profile == null || map == null) return false;
            float progress = ZoologyReproductiveEnergetics.EggProgress(layer);
            if (progress <= 0f) return !CanStartEggFormationInSeason(layer, map, profile);
            return progress >= 1f && !CanLayInSeason(pawn, map, profile);
        }

        internal static bool AllowsLaying(CompEggLayer layer)
        {
            if (!ModConstants.AdvancedReproduction || layer?.parent is not Pawn pawn
                || pawn.RaceProps?.Animal != true || Managed(pawn)) return true;
            if (AnimalWinterDormancy.IsDormant(pawn)) return false;
            var profile = Profile(pawn);
            Map map = pawn.MapHeld;
            if (profile == null || map == null) return true;
            return CanLayInSeason(pawn, map, profile);
        }

        internal static void EggLaid(CompEggLayer layer)
        {
            if (!ModConstants.AdvancedReproduction || layer?.parent is not Pawn pawn) return;
            var profile = Profile(pawn);
            if (profile == null || pawn.TryGetComp<CompAnimalStrategyState>() is not CompAnimalStrategyState state) return;
            int pause = PauseTicks(pawn, profile);
            if (pause <= 0) { state.reproductivePauseUntil = 0; return; }
            if (pawn.MapHeld is Map map && !Managed(pawn) && LayingMask(profile) != 15)
            {
                int formation = ToTicks(EggFormationDays(layer));
                pause = NextSeasonalFormationDelay(YearPhase(map), formation, LayingMask(profile), pause);
            }
            state.reproductivePauseUntil = Deadline(pause);
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.Mated))]
    internal static class Patch_SeasonalMated
    {
        private struct Conception
        {
            internal bool tracked;
            internal Hediff previous;
        }

        private static bool Prepare() => ModConstants.AdvancedReproduction;

        private static bool Prefix(Pawn male, Pawn female, out Conception __state)
        {
            __state = default;
            if (!AnimalSeasonalReproduction.AllowsMating(male)
                || !AnimalSeasonalReproduction.AllowsMating(female)) return false;
            if (female?.RaceProps?.Animal == true
                && (AnimalSeasonalReproduction.HasDiapauseProfile(female)
                    || AnimalSeasonalReproduction.Profile(female)?.realisticLitterMean >= 1f))
            {
                __state.tracked = true;
                __state.previous = female.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            }
            return true;
        }

        private static void Postfix(Pawn female, Conception __state)
        {
            if (!__state.tracked) return;
            Hediff pregnancy = female.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant);
            if (pregnancy != null && pregnancy != __state.previous)
            {
                AnimalReproductionProfiles.BeginPregnancy(pregnancy);
                if (AnimalSeasonalReproduction.HasDiapauseProfile(female))
                    AnimalSeasonalReproduction.BeginDiapause(female, pregnancy);
            }
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.FertileMateTarget))]
    internal static class Patch_SeasonalFertileMateTarget
    {
        private static bool Prepare() => ModConstants.AdvancedReproduction;
        private static void Postfix(Pawn __0, Pawn __1, ref bool __result)
        {
            if (__result && (!AnimalSeasonalReproduction.AllowsMating(__0)
                || !AnimalSeasonalReproduction.AllowsMating(__1))) __result = false;
        }
    }

    [HarmonyPatch]
    internal static class Patch_SeasonalVanillaMateJob
    {
        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("RimWorld.JobGiver_Mate");
            return type == null ? null : AccessTools.DeclaredMethod(type, "TryGiveJob", new[] { typeof(Pawn) });
        }
        private static bool Prepare() => ModConstants.AdvancedReproduction && TargetMethod() != null;
        private static bool Prefix(Pawn __0, ref Verse.AI.Job __result)
        {
            if (AnimalSeasonalReproduction.AllowsMating(__0)) return true;
            __result = null;
            return false;
        }
    }

    // Only replace the per-pawn duration load. RaceProperties and comp props
    // remain immutable during simulation; vanilla retains its birth/laying logic.
    internal static class ReproductiveDurationIL
    {
        internal static IEnumerable<CodeInstruction> ReplaceField(IEnumerable<CodeInstruction> instructions,
            FieldInfo field, MethodInfo replacement, MethodBase original, bool withDelta = false)
        {
            bool found = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (field != null && instruction.opcode == OpCodes.Ldfld && object.Equals(instruction.operand, field))
                {
                    found = true;
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    load.blocks.AddRange(instruction.blocks);
                    yield return load;
                    if (withDelta) yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                else yield return instruction;
            }
            if (!found) Log.WarningOnce("[Zoology] Reproductive duration field not found in " + original.Name,
                original.MetadataToken);
        }

        internal static IEnumerable<CodeInstruction> Pregnancy(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            ReplaceField(instructions, AccessTools.Field(typeof(RaceProperties), "gestationPeriodDays"),
                AccessTools.Method(typeof(AnimalSeasonalReproduction),
                    original.GetParameters().Length == 1
                        ? nameof(AnimalSeasonalReproduction.PregnancyDurationForInterval)
                        : nameof(AnimalSeasonalReproduction.PregnancyDurationForTick)),
                original, original.GetParameters().Length == 1);

        internal static IEnumerable<CodeInstruction> Egg(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            ReplaceField(instructions, AccessTools.Field(typeof(CompProperties_EggLayer), "eggLayIntervalDays"),
                AccessTools.Method(typeof(AnimalSeasonalReproduction), nameof(AnimalSeasonalReproduction.EggDurationForTick)), original);
    }

    [HarmonyPatch]
    internal static class Patch_SeasonalPregnancyInterval
    {
        internal static MethodBase Method() =>
            AccessTools.DeclaredMethod(typeof(Hediff_Pregnant), "TickInterval", new[] { typeof(int) });
        private static MethodBase TargetMethod() => Method();
        private static bool Prepare() => ModConstants.AdvancedReproduction
            && AnimalSeasonalReproduction.CanTrackPregnancy && Method() != null;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) =>
            ReproductiveDurationIL.Pregnancy(instructions, __originalMethod);

    }

    [HarmonyPatch]
    internal static class Patch_SeasonalPregnancyTick
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(Hediff_Pregnant), "Tick", Type.EmptyTypes);
        private static bool Prepare() => ModConstants.AdvancedReproduction
            && AnimalSeasonalReproduction.CanTrackPregnancy
            && Patch_SeasonalPregnancyInterval.Method() == null && TargetMethod() != null;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) =>
            ReproductiveDurationIL.Pregnancy(instructions, __originalMethod);

    }

    [HarmonyPatch]
    internal static class Patch_SeasonalEggProgress
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompTick", Type.EmptyTypes);
        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress && TargetMethod() != null;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) =>
            ReproductiveDurationIL.Egg(instructions, __originalMethod);

    }

    [HarmonyPatch]
    internal static class Patch_SeasonalEggProgressInterval
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompTickInterval", new[] { typeof(int) });
        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress && TargetMethod() != null;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) =>
            ReproductiveDurationIL.Egg(instructions, __originalMethod);

    }

    [HarmonyPatch(typeof(CompEggLayer), nameof(CompEggLayer.CanLayNow), MethodType.Getter)]
    internal static class Patch_SeasonalCanLayNow
    {
        private static bool Prepare() => ModConstants.AdvancedReproduction;
        private static void Postfix(CompEggLayer __instance, ref bool __result)
        {
            if (__result && !AnimalSeasonalReproduction.AllowsLaying(__instance))
                __result = false;
        }
    }

    [HarmonyPatch]
    internal static class Patch_SeasonalEggInspectString
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "CompInspectStringExtra", Type.EmptyTypes);

        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress && TargetMethod() != null;

        private static void Postfix(CompEggLayer __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || !AnimalSeasonalReproduction.EggSeasonPaused(__instance))
                return;
            __result += " (" + "Zoology_EggSeasonPaused".Translate().ToString() + ")";
        }
    }

    [HarmonyPatch(typeof(CompEggLayer), "ProduceEgg")]
    internal static class Patch_SeasonalEggLaid
    {
        private static bool Prepare() => ModConstants.AdvancedReproduction
            && ZoologyReproductiveEnergetics.CanReadEggProgress;
        private static void Postfix(CompEggLayer __instance, Thing __result)
        {
            if (__result != null) AnimalSeasonalReproduction.EggLaid(__instance);
        }
    }
}
