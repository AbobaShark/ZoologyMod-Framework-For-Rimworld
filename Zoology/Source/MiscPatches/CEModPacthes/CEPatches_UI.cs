using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [HarmonyPatch]
    public static class CEPatches_UI_Explanation
    {
        public static bool Prepare()
        {
            return CEChecker.IsCEInstalled();
        }

        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("CombatExtended.StatWorker_MeleeArmorPenetration");
            if (type == null) return null;
            return AccessTools.Method(type, "GetExplanationUnfinalized", new[] { typeof(StatRequest), typeof(ToStringNumberSense) });
        }

        [HarmonyPrefix]
        static bool Prefix_GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense, ref string __result, object __instance)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !settings.EnableOverrideCEPenetration)
                {
                    return true;
                }

                if (__instance == null)
                {
                    __result = string.Empty;
                    return false;
                }

                var thingDef = req.Def as ThingDef;
                var tools = thingDef?.tools;
                if (tools.NullOrEmpty())
                {
                    __result = string.Empty;
                    return false;
                }

                var builder = new StringBuilder();
                float penetrationFactor = CEReflectionUtility.InvokePrivateFloat(__instance, "GetPenetrationFactor", new object[] { req }, 1f);
                float skillFactor = CEReflectionUtility.InvokePrivateFloat(__instance, "GetSkillFactor", new object[] { req }, 1f);

                builder.AppendLine("CE_WeaponPenetrationFactor".Translate() + ": " + penetrationFactor.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute));
                if (Math.Abs(skillFactor - 1f) > 0.001f)
                {
                    builder.AppendLine("CE_WeaponPenetrationSkillFactor".Translate() + ": " + skillFactor.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute));
                }
                builder.AppendLine();

                Pawn pawn = CEReflectionUtility.GetPawnFromRequestThing(req.Thing);
                float otherSourcesMult = CEReflectionUtility.GetMeleeDamageFactorStatPow(pawn);
                if (pawn != null && Math.Abs(otherSourcesMult - 1f) > 0.001f)
                {
                    builder.AppendLine("   " + "CE_WeaponPenetrationOtherFactors".Translate() + ": " + otherSourcesMult.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute));
                }

                float extSharp = 1f;
                float extBlunt = 1f;
                if (pawn != null)
                {
                    var lifeDef = LifeStageUtility.GetPenetrationDefForPawn(pawn);
                    if (lifeDef != null)
                    {
                        extSharp = lifeDef.meleePenetrationSharpFactor;
                        extBlunt = lifeDef.meleePenetrationBluntFactor;
                    }
                }

                if (Math.Abs(extSharp - 1f) > 1e-6f || Math.Abs(extBlunt - 1f) > 1e-6f)
                {
                    builder.AppendLine();
                    builder.AppendLine("Zoology_AdjustedOtherFactors".Translate());
                    builder.AppendLine($"    {"CE_DescSharpPenetration".Translate()}: {extSharp.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute)}");
                    builder.AppendLine($"    {"CE_DescBluntPenetration".Translate()}: {extBlunt.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute)}");
                    builder.AppendLine();
                }

                for (int i = 0; i < tools.Count; i++)
                {
                    var tool = tools[i];
                    if (tool == null)
                    {
                        continue;
                    }

                    builder.AppendLine("  " + "Tool".Translate() + ": " + tool + " " + CEReflectionUtility.GetManeuverString(tool));

                    float toolAPSharp = CEReflectionUtility.ReadSharpToolPenetration(tool, 0f);
                    float toolAPBlunt = CEReflectionUtility.ReadBluntToolPenetration(tool, 0f);

                    float calcSharp = toolAPSharp * penetrationFactor * skillFactor * otherSourcesMult * extSharp;
                    builder.Append(string.Format("    {0}: {1} x {2}",
                        "CE_DescSharpPenetration".Translate(),
                        toolAPSharp.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute),
                        penetrationFactor.ToStringByStyle(ToStringStyle.FloatMaxThree, ToStringNumberSense.Absolute)));
                    if (Math.Abs(skillFactor - 1f) > 0.001f) builder.Append(string.Format(" x {0}", skillFactor.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    if (Math.Abs(otherSourcesMult - 1f) > 0.001f) builder.Append(string.Format(" x {0}", otherSourcesMult.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    if (Math.Abs(extSharp - 1f) > 0.001f) builder.Append(string.Format(" x {0}", extSharp.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    builder.AppendLine(string.Format(" = {0} {1}", calcSharp.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute), "CE_mmRHA".Translate()));

                    float calcBlunt = toolAPBlunt * penetrationFactor * skillFactor * otherSourcesMult * extBlunt;
                    builder.Append(string.Format("    {0}: {1} x {2}",
                        "CE_DescBluntPenetration".Translate(),
                        toolAPBlunt.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute),
                        penetrationFactor.ToStringByStyle(ToStringStyle.FloatMaxThree, ToStringNumberSense.Absolute)));
                    if (Math.Abs(skillFactor - 1f) > 0.001f) builder.Append(string.Format(" x {0}", skillFactor.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    if (Math.Abs(otherSourcesMult - 1f) > 0.001f) builder.Append(string.Format(" x {0}", otherSourcesMult.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    if (Math.Abs(extBlunt - 1f) > 0.001f) builder.Append(string.Format(" x {0}", extBlunt.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    builder.AppendLine(string.Format(" = {0} {1}", calcBlunt.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute), "CE_MPa".Translate()));
                    builder.AppendLine();
                }

                __result = builder.ToString();
                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Zoology/UI] Exception in Explanation prefix: {ex}", 876543210);
                return true;
            }
        }
    }

    [HarmonyPatch]
    public static class CEPatches_UI_Final
    {
        public static bool Prepare()
        {
            return CEChecker.IsCEInstalled();
        }

        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("CombatExtended.StatWorker_MeleeArmorPenetration");
            if (type == null) return null;
            return AccessTools.Method(type, "GetFinalDisplayValue", new[] { typeof(StatRequest) });
        }

        [HarmonyPrefix]
        static bool Prefix_GetFinalDisplayValue(object __instance, StatRequest optionalReq, ref string __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !settings.EnableOverrideCEPenetration)
                {
                    return true;
                }

                var thingDef = optionalReq.Def as ThingDef;
                var tools = thingDef?.tools;
                if (tools.NullOrEmpty())
                {
                    __result = string.Empty;
                    return false;
                }

                float totalChance = 0f;
                for (int i = 0; i < tools.Count; i++)
                {
                    totalChance += tools[i]?.chanceFactor ?? 0f;
                }
                if (totalChance <= 0f)
                {
                    totalChance = 1f;
                }

                Pawn pawn = CEReflectionUtility.GetPawnFromRequestThing(optionalReq.Thing);
                float otherSourcesMult = CEReflectionUtility.GetMeleeDamageFactorStatPow(pawn);

                float extSharp = 1f;
                float extBlunt = 1f;
                if (pawn != null)
                {
                    var lifeDef = LifeStageUtility.GetPenetrationDefForPawn(pawn);
                    if (lifeDef != null)
                    {
                        extSharp = lifeDef.meleePenetrationSharpFactor;
                        extBlunt = lifeDef.meleePenetrationBluntFactor;
                    }
                }

                float weightedSharp = 0f;
                float weightedBlunt = 0f;
                for (int i = 0; i < tools.Count; i++)
                {
                    var tool = tools[i];
                    if (tool == null)
                    {
                        continue;
                    }

                    float share = tool.chanceFactor / totalChance;
                    float toolAPSharp = CEReflectionUtility.ReadSharpToolPenetration(tool, 0f);
                    float toolAPBlunt = CEReflectionUtility.ReadBluntToolPenetration(tool, 0f);
                    weightedSharp += share * toolAPSharp * otherSourcesMult * extSharp;
                    weightedBlunt += share * toolAPBlunt * otherSourcesMult * extBlunt;
                }

                float penetrationFactor = CEReflectionUtility.InvokePrivateFloat(__instance, "GetPenetrationFactor", new object[] { optionalReq }, 1f);
                float skillFactor = CEReflectionUtility.InvokePrivateFloat(__instance, "GetSkillFactor", new object[] { optionalReq }, 1f);

                string sharpStr = (weightedSharp * penetrationFactor * skillFactor).ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute) + " " + "CE_mmRHA".Translate();
                string bluntStr = (weightedBlunt * penetrationFactor * skillFactor).ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute) + " " + "CE_MPa".Translate();
                __result = sharpStr + ", " + bluntStr;

                return false;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Zoology/UI] Exception in FinalDisplay prefix: {ex}", 123456789);
                return true;
            }
        }
    }
}
