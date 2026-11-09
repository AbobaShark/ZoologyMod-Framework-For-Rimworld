using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // CE's PawnCombat AP stat reads ToolCE directly instead of invoking
    // Verb_MeleeAttackCE.ArmorPenetrationSharp/Blunt. The combat getter is
    // already patched separately; this synchronizes its two UI consumers
    // without mutating shared ThingDef.tools or re-deriving species physics.
    internal static class AnimalLifeStageCeApStatDisplay
    {
        private static readonly Type WorkerType =
            AccessTools.TypeByName("CombatExtended.StatWorker_MeleeArmorPenetration");

        internal static readonly MethodInfo FinalDisplayMethod = WorkerType == null
            ? null : AccessTools.Method(WorkerType, "GetStatDrawEntryLabel");
        internal static readonly MethodInfo FinalExplanationMethod = WorkerType == null
            ? null : AccessTools.Method(WorkerType, "GetExplanationFinalizePart");
        internal static readonly MethodInfo ExplanationMethod = WorkerType == null
            ? null : AccessTools.Method(WorkerType, "GetExplanationUnfinalized");

        private static readonly MethodInfo PenetrationFactorMethod = WorkerType == null
            ? null : AccessTools.Method(WorkerType, "GetPenetrationFactor");
        private static readonly MethodInfo SkillFactorMethod = WorkerType == null
            ? null : AccessTools.Method(WorkerType, "GetSkillFactor");
        private static readonly MethodInfo OtherFactorsMethod = WorkerType == null
            ? null : AccessTools.Method(WorkerType, "GetOtherFactors");

        internal static bool CanPatch()
        {
            return CEChecker.IsCEInstalled() && WorkerType != null
                && FinalDisplayMethod != null && FinalExplanationMethod != null
                && ExplanationMethod != null
                && PenetrationFactorMethod != null && SkillFactorMethod != null
                && OtherFactorsMethod != null
                && ZoologyAnimalGrowth.Enabled;
        }

        private sealed class Penetration
        {
            public Tool tool;
            public float originalSharp;
            public float originalBlunt;
            public float sharpFactor;
            public float bluntFactor;
            public float sharp;
            public float blunt;
        }

        internal static bool TryCalculate(object worker, StatRequest request,
            out string label, out string explanation)
        {
            label = null;
            explanation = null;
            if (worker == null || request.Thing is not Pawn pawn
                || pawn.RaceProps?.Animal != true
                || pawn.def?.tools == null || pawn.def.tools.Count == 0)
                return false;

            try
            {
                // Call CE's own private stat factors. The same CE assembly
                // remains the authority for skill, stats and other modifiers.
                float penetrationFactor = (float)PenetrationFactorMethod.Invoke(
                    worker, new object[] { request });
                float skillFactor = (float)SkillFactorMethod.Invoke(
                    worker, new object[] { request });
                float otherFactor = 1f;
                var otherFactors = OtherFactorsMethod.Invoke(
                    worker, new object[] { request }) as IEnumerable<float>;
                if (otherFactors == null)
                    return false;
                foreach (float factor in otherFactors)
                    otherFactor *= factor;

                var entries = new List<Penetration>(pawn.def.tools.Count);
                float weightSum = 0f;
                bool hasLifeStageProfile = false;
                foreach (var tool in pawn.def.tools)
                {
                    float sharp, blunt;
                    if (tool == null || !CEReflectionUtility.TryReadSharpToolPenetration(
                            tool, out sharp)
                        || !CEReflectionUtility.TryReadBluntToolPenetration(
                            tool, out blunt))
                        return false;

                    var entry = new Penetration
                    {
                        tool = tool,
                        originalSharp = sharp,
                        originalBlunt = blunt,
                        sharpFactor = 1f,
                        bluntFactor = 1f
                    };
                    float sharpInterpolated, bluntInterpolated;
                    if (ZoologyAnimalGrowth.TryGetToolFactor(pawn, tool,
                            ZoologyAnimalGrowth.GrowthToolFactor.CeSharpPenetration,
                            out sharpInterpolated)
                        && ZoologyAnimalGrowth.TryGetToolFactor(pawn, tool,
                            ZoologyAnimalGrowth.GrowthToolFactor.CeBluntPenetration,
                            out bluntInterpolated))
                    {
                        entry.sharpFactor = sharpInterpolated;
                        entry.bluntFactor = bluntInterpolated;
                        hasLifeStageProfile = true;
                    }
                    else
                    {
                        ZoologyLifeStageToolFactor stageTool;
                        if (ZoologyLifeStageProfileInstaller.TryGetToolFactor(pawn,
                                tool, out stageTool))
                        {
                            entry.sharpFactor = stageTool.ceSharpPenetrationFactor;
                            entry.bluntFactor = stageTool.ceBluntPenetrationFactor;
                            hasLifeStageProfile = true;
                        }
                    }
                    entries.Add(entry);
                    weightSum += tool.chanceFactor;
                }

                if (!hasLifeStageProfile || !(weightSum > 0f))
                    return false;

                float combined = penetrationFactor * skillFactor * otherFactor;
                float averageSharp = 0f, averageBlunt = 0f;
                foreach (var entry in entries)
                {
                    entry.sharp = entry.originalSharp * combined * entry.sharpFactor;
                    entry.blunt = entry.originalBlunt * combined * entry.bluntFactor;
                    float weight = entry.tool.chanceFactor / weightSum;
                    averageSharp += weight * entry.sharp;
                    averageBlunt += weight * entry.blunt;
                }

                if (!IsFinite(averageSharp) || !IsFinite(averageBlunt))
                    return false;

                label = Format(averageSharp) + " " + "CE_mmRHA".Translate()
                    + ", " + Format(averageBlunt) + " " + "CE_MPa".Translate();

                var sb = new StringBuilder(384);
                sb.Append("CE_WeaponPenetrationFactor".Translate()).Append(": ")
                    .Append(FormatFactor(penetrationFactor)).AppendLine();
                sb.Append("CE_WeaponPenetrationSkillFactor".Translate()).Append(": ")
                    .Append(FormatFactor(skillFactor)).AppendLine();
                sb.Append("CE_WeaponPenetrationOtherFactors".Translate()).Append(": ")
                    .Append(FormatFactor(otherFactor)).AppendLine();
                sb.AppendLine();
                foreach (var entry in entries)
                {
                    sb.Append("  ").Append("Tool".Translate()).Append(": ")
                        .Append(entry.tool.ToString()).AppendLine();
                    sb.Append("    ").Append("CE_DescSharpPenetration".Translate())
                        .Append(": ").Append(Format(entry.originalSharp))
                        .Append(" x ").Append(FormatFactor(entry.sharpFactor))
                        .Append(" (life stage) x ").Append(FormatFactor(combined))
                        .Append(" = ").Append(Format(entry.sharp))
                        .Append(" ").Append("CE_mmRHA".Translate()).AppendLine();
                    sb.Append("    ").Append("CE_DescBluntPenetration".Translate())
                        .Append(": ").Append(Format(entry.originalBlunt))
                        .Append(" x ").Append(FormatFactor(entry.bluntFactor))
                        .Append(" (life stage) x ").Append(FormatFactor(combined))
                        .Append(" = ").Append(Format(entry.blunt))
                        .Append(" ").Append("CE_MPa".Translate()).AppendLine();
                    sb.AppendLine();
                }
                explanation = sb.ToString();
                return true;
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[Zoology] Could not format CE stage-specific "
                    + "melee penetration; leaving CE original output: " + ex,
                    84923113);
                return false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string Format(float value)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }

        private static string FormatFactor(float value)
        {
            return value.ToString("0.##########", CultureInfo.CurrentCulture);
        }
    }

    [HarmonyPatch]
    internal static class Patch_AnimalLifeStageCeApDisplay
    {
        private static bool Prepare() => AnimalLifeStageCeApStatDisplay.CanPatch();

        private static MethodBase TargetMethod()
        {
            return AnimalLifeStageCeApStatDisplay.FinalDisplayMethod;
        }

        private static void Postfix(object __instance, StatRequest optionalReq,
            ref string __result)
        {
            string label, explanation;
            if (AnimalLifeStageCeApStatDisplay.TryCalculate(__instance,
                    optionalReq, out label, out explanation))
                __result = label;
        }
    }

    [HarmonyPatch]
    internal static class Patch_AnimalLifeStageCeApExplanation
    {
        private static bool Prepare() => AnimalLifeStageCeApStatDisplay.CanPatch();

        private static MethodBase TargetMethod()
        {
            return AnimalLifeStageCeApStatDisplay.ExplanationMethod;
        }

        private static void Postfix(object __instance, StatRequest req,
            ref string __result)
        {
            string label, explanation;
            if (AnimalLifeStageCeApStatDisplay.TryCalculate(__instance,
                    req, out label, out explanation))
                __result = explanation;
        }
    }
    [HarmonyPatch]
    internal static class Patch_AnimalLifeStageCeApFinalExplanation
    {
        private static bool Prepare() => AnimalLifeStageCeApStatDisplay.CanPatch();

        private static MethodBase TargetMethod()
        {
            return AnimalLifeStageCeApStatDisplay.FinalExplanationMethod;
        }

        private static void Postfix(object __instance, StatRequest req,
            ref string __result)
        {
            string label, explanation;
            if (AnimalLifeStageCeApStatDisplay.TryCalculate(__instance,
                    req, out label, out explanation))
                __result = "StatsReport_FinalValue".Translate() + ": " + label;
        }
    }

}
