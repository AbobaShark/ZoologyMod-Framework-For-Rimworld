using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class AnimalReproductionProfiles
    {
        internal static bool BirthCurveInstalled;
        internal static bool EggRangeInstalled;

        private static bool RealisticCounts => ModConstants.AdvancedReproduction
            && ModConstants.Settings?.RealisticOffspringCounts == true;

        internal static void BeginPregnancy(Hediff pregnancy)
        {
            Pawn mother = pregnancy?.pawn;
            if (mother?.RaceProps?.Animal != true) return;
            var state = mother.TryGetComp<CompAnimalStrategyState>();
            if (state != null)
                state.realisticLitter = RealisticCounts && BirthCurveInstalled
                    && AnimalSeasonalReproduction.Profile(mother)?.realisticLitterMean >= 1f;
        }

        internal static void BeginClutch(CompEggLayer layer)
        {
            if (layer.parent is not Pawn mother) return;
            var state = mother.TryGetComp<CompAnimalStrategyState>();
            if (state != null)
                state.realisticClutch = RealisticCounts && EggRangeInstalled
                    && AnimalSeasonalReproduction.Profile(mother)?.realisticEggCountRange.min > 0;
        }

        internal static SimpleCurve BirthCurve(RaceProperties race, Pawn mother)
        {
            if (ModConstants.AdvancedReproduction && BirthCurveInstalled
                && mother?.RaceProps?.Animal == true
                && mother.TryGetComp<CompAnimalStrategyState>()?.realisticLitter == true)
                return AnimalSeasonalReproduction.Profile(mother)?.RealisticLitterCurve ?? race.litterSizeCurve;
            return race.litterSizeCurve;
        }

        internal static IntRange EggRange(CompProperties_EggLayer props, CompEggLayer layer)
        {
            if (ModConstants.AdvancedReproduction && EggRangeInstalled
                && layer.parent is Pawn mother
                && mother.TryGetComp<CompAnimalStrategyState>()?.realisticClutch == true)
            {
                var profile = AnimalSeasonalReproduction.Profile(mother);
                if (profile != null && profile.realisticEggCountRange.min > 0)
                    return profile.realisticEggCountRange;
            }
            return props.eggCountRange;
        }

        internal static float LitterMean(Pawn mother, float standard)
        {
            if (BirthCurveInstalled && mother.TryGetComp<CompAnimalStrategyState>()?.realisticLitter == true)
            {
                float mean = AnimalSeasonalReproduction.Profile(mother)?.realisticLitterMean ?? -1f;
                if (mean >= 1f) return mean;
            }
            return standard;
        }

        internal static float ClutchMean(Pawn mother, float standard, bool beginning = false)
        {
            if (EggRangeInstalled && mother.TryGetComp<CompAnimalStrategyState>() is CompAnimalStrategyState state
                && (beginning ? RealisticCounts : state.realisticClutch))
            {
                var profile = AnimalSeasonalReproduction.Profile(mother);
                if (profile != null && profile.realisticEggCountRange.min > 0)
                    return (profile.realisticEggCountRange.min + (float)profile.realisticEggCountRange.max) * 0.5f;
            }
            return standard;
        }

        // Replace a field read without altering vanilla birth/egg generation.
        // IntRange is a value type: support both ldfld and ldflda call sites.
        internal static IEnumerable<CodeInstruction> ReplaceCountField(
            IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase original,
            FieldInfo field, MethodInfo selector, int arg, Action<bool> installed)
        {
            var code = new List<CodeInstruction>(instructions);
            bool found = field != null && selector != null && code.Exists(i =>
                Equals(i.operand, field) && (i.opcode == OpCodes.Ldfld || i.opcode == OpCodes.Ldflda));
            installed(found);
            if (!found)
            {
                Log.WarningOnce("[Zoology] Reproductive count field not found in " + original.Name, original.MetadataToken);
                return code;
            }
            var result = new List<CodeInstruction>(code.Count + 8);
            LocalBuilder local = null;
            foreach (CodeInstruction i in code)
            {
                if (Equals(i.operand, field) && (i.opcode == OpCodes.Ldfld || i.opcode == OpCodes.Ldflda))
                {
                    var load = CodeInstruction.LoadArgument(arg);
                    load.labels.AddRange(i.labels);
                    load.blocks.AddRange(i.blocks);
                    result.Add(load);
                    result.Add(new CodeInstruction(OpCodes.Call, selector));
                    if (i.opcode == OpCodes.Ldflda)
                    {
                        local ??= il.DeclareLocal(field.FieldType);
                        result.Add(new CodeInstruction(OpCodes.Stloc, local));
                        result.Add(new CodeInstruction(OpCodes.Ldloca, local));
                    }
                }
                else result.Add(i);
            }
            return result;
        }
    }

    [HarmonyPatch]
    internal static class Patch_AnimalOffspringPregnancyStarted
    {
        private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(Hediff_Pregnant), "PostAdd");
        private static bool Prepare() => ModConstants.AdvancedReproduction && TargetMethod() != null;
        private static void Postfix(Hediff_Pregnant __instance) =>
            AnimalReproductionProfiles.BeginPregnancy(__instance);
    }

    [HarmonyPatch]
    internal static class Patch_AnimalOffspringBirth
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(Hediff_Pregnant), "DoBirthSpawn", new[] { typeof(Pawn), typeof(Pawn) });

        private static bool Prepare() => ModConstants.AdvancedReproduction && TargetMethod() != null;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator, MethodBase __originalMethod) =>
            AnimalReproductionProfiles.ReplaceCountField(instructions, generator, __originalMethod,
                AccessTools.Field(typeof(RaceProperties), "litterSizeCurve"),
                AccessTools.Method(typeof(AnimalReproductionProfiles), nameof(AnimalReproductionProfiles.BirthCurve)),
                0, installed => AnimalReproductionProfiles.BirthCurveInstalled = installed);

        private static void Postfix(Pawn __0)
        {
            var state = __0?.TryGetComp<CompAnimalStrategyState>();
            if (state != null) state.realisticLitter = false;
        }
    }

    [HarmonyPatch]
    internal static class Patch_AnimalOffspringEggCount
    {
        private static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(CompEggLayer), "ProduceEgg", Type.EmptyTypes);
        private static bool Prepare() => ModConstants.AdvancedReproduction && TargetMethod() != null;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator, MethodBase __originalMethod) =>
            AnimalReproductionProfiles.ReplaceCountField(instructions, generator, __originalMethod,
                AccessTools.Field(typeof(CompProperties_EggLayer), "eggCountRange"),
                AccessTools.Method(typeof(AnimalReproductionProfiles), nameof(AnimalReproductionProfiles.EggRange)),
                0, installed => AnimalReproductionProfiles.EggRangeInstalled = installed);

        private static void Postfix(CompEggLayer __instance, Thing __result)
        {
            if (__result == null || __instance.parent is not Pawn mother) return;
            var state = mother.TryGetComp<CompAnimalStrategyState>();
            if (state != null) state.realisticClutch = false;
        }
    }

    [HarmonyPatch]
    internal static class Patch_AnimalHatcherDuration
    {
        private static bool Prepare() => ModConstants.AdvancedReproduction;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Versions may use a wrapper, a rare tick, or an interval tick.
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(CompHatcher)))
                if (!method.IsStatic && (method.Name == "CompTick" || method.Name == "CompTickRare"
                    || method.Name == "CompTickLong" || method.Name == "CompTickInterval"))
                    yield return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            FieldInfo field = AccessTools.Field(typeof(CompProperties_Hatcher), "hatcherDaystoHatch");
            MethodInfo replacement = AccessTools.Method(typeof(AnimalSeasonalReproduction),
                nameof(AnimalSeasonalReproduction.HatcherDuration));
            foreach (CodeInstruction i in instructions)
            {
                if (field != null && replacement != null && i.opcode == OpCodes.Ldfld && Equals(i.operand, field))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(i.labels);
                    load.blocks.AddRange(i.blocks);
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                else yield return i;
            }
        }
    }
}
