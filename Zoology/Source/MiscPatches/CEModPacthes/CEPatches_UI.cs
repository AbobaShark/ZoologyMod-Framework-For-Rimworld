

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using HarmonyLib;
using System.Text;

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
            var t = AccessTools.TypeByName("CombatExtended.StatWorker_MeleeArmorPenetration");
            if (t == null) return null;
            return AccessTools.Method(t, "GetExplanationUnfinalized", new Type[] { typeof(StatRequest), typeof(ToStringNumberSense) });
        }

        [HarmonyPrefix]
        static bool Prefix_GetExplanationUnfinalized(StatRequest req, ToStringNumberSense numberSense, ref string __result, object __instance)
        {
            try
            {
                
                if (ZoologyModSettings.Instance == null || !ZoologyModSettings.Instance.EnableOverrideCEPenetration)
                {
                    return true; 
                }

                if (__instance == null) { __result = ""; return false; }

                ThingDef thingDef = req.Def as ThingDef;
                List<Tool> list = (thingDef != null) ? thingDef.tools : null;
                if (list.NullOrEmpty()) { __result = ""; return false; }

                StringBuilder sb = new StringBuilder();

                
                float penetrationFactor = InvokePrivateFloat(__instance, "GetPenetrationFactor", new object[] { req }, 1f);
                float skillFactor = InvokePrivateFloat(__instance, "GetSkillFactor", new object[] { req }, 1f);

                sb.AppendLine("CE_WeaponPenetrationFactor".Translate() + ": " + penetrationFactor.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute));
                if (Math.Abs(skillFactor - 1f) > 0.001f)
                    sb.AppendLine("CE_WeaponPenetrationSkillFactor".Translate() + ": " + skillFactor.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute));
                sb.AppendLine();

                
                float totalChance = list.Aggregate(0f, (acc, t) => acc + (t?.chanceFactor ?? 0f));
                if (totalChance <= 0f) totalChance = 1f;

                
                Pawn pawn = null;
                if (req.Thing != null) pawn = req.Thing as Pawn;
                if (pawn == null && req.Thing != null)
                {
                    var holder = req.Thing.ParentHolder;
                    pawn = (holder as Pawn_EquipmentTracker)?.pawn;
                }

                float lifeDFPow = 1f;
                float statPow = 1f;
                try { var ls = pawn?.ageTracker?.CurLifeStage; lifeDFPow = (ls != null) ? Mathf.Pow(ls.meleeDamageFactor, 0.75f) : 1f; } catch { lifeDFPow = 1f; }
                try { statPow = Mathf.Pow(pawn?.GetStatValue(StatDefOf.MeleeDamageFactor, true, -1) ?? 1f, 0.75f); } catch { statPow = 1f; }
                float currentOtherMult = lifeDFPow * statPow;

                
                float otherSourcesMult = statPow;

                
                if (pawn != null && Math.Abs(otherSourcesMult - 1f) > 0.001f)
                {
                    sb.AppendLine("   " + "CE_WeaponPenetrationOtherFactors".Translate() + ": " + otherSourcesMult.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute));
                }

                
                float extSharp = 1f, extBlunt = 1f;
                if (pawn != null)
                {
                    var lifeDef = LifeStageUtility.GetPenetrationDefForPawn(pawn);
                    if (lifeDef != null) { extSharp = lifeDef.meleePenetrationSharpFactor; extBlunt = lifeDef.meleePenetrationBluntFactor; }
                }

                
                
                float newOtherSharp = otherSourcesMult * extSharp;
                float newOtherBlunt = otherSourcesMult * extBlunt;

                
                if (Math.Abs(extSharp - 1f) > 1e-6f || Math.Abs(extBlunt - 1f) > 1e-6f)
                {
                    sb.AppendLine();
                    sb.AppendLine("Zoology_AdjustedOtherFactors".Translate()); 
                    sb.AppendLine($"    { "CE_DescSharpPenetration".Translate() }: {extSharp.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute)}");
                    sb.AppendLine($"    { "CE_DescBluntPenetration".Translate() }: {extBlunt.ToStringByStyle(ToStringStyle.PercentZero, ToStringNumberSense.Absolute)}");
                    sb.AppendLine();
                }

                
                
                foreach (var toolObj in list)
                {
                    if (toolObj == null) continue;
                    Tool tool = toolObj;
                    IEnumerable<ManeuverDef> mans = from d in DefDatabase<ManeuverDef>.AllDefsListForReading
                                                   where tool.capacities?.Contains(d.requiredCapacity) == true
                                                   select d;
                    string maneuvers = "(" + string.Join("/", mans.Select(m => m.ToString())) + ")";
                    sb.AppendLine("  " + "Tool".Translate() + ": " + tool.ToString() + " " + maneuvers);

                    float toolAPSharp = ReadFloatFromToolMember(tool, new[] { "armorPenetrationSharp", "armorPenetration" }, 0f);
                    float toolAPBlunt = ReadFloatFromToolMember(tool, new[] { "armorPenetrationBlunt", "armorPenetration" }, 0f);

                    
                    float calcSharp = toolAPSharp * penetrationFactor * skillFactor * otherSourcesMult * extSharp;
                    sb.Append(string.Format("    {0}: {1} x {2}", "CE_DescSharpPenetration".Translate(),
                                            toolAPSharp.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute),
                                            penetrationFactor.ToStringByStyle(ToStringStyle.FloatMaxThree, ToStringNumberSense.Absolute)));
                    if (Math.Abs(skillFactor - 1f) > 0.001f) sb.Append(string.Format(" x {0}", skillFactor.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    if (Math.Abs(otherSourcesMult - 1f) > 0.001f) sb.Append(string.Format(" x {0}", otherSourcesMult.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    if (Math.Abs(extSharp - 1f) > 0.001f) sb.Append(string.Format(" x {0}", extSharp.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    sb.AppendLine(string.Format(" = {0} {1}", calcSharp.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute), "CE_mmRHA".Translate()));

                    
                    float calcBlunt = toolAPBlunt * penetrationFactor * skillFactor * otherSourcesMult * extBlunt;
                    sb.Append(string.Format("    {0}: {1} x {2}", "CE_DescBluntPenetration".Translate(),
                                            toolAPBlunt.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute),
                                            penetrationFactor.ToStringByStyle(ToStringStyle.FloatMaxThree, ToStringNumberSense.Absolute)));
                    if (Math.Abs(skillFactor - 1f) > 0.001f) sb.Append(string.Format(" x {0}", skillFactor.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    if (Math.Abs(otherSourcesMult - 1f) > 0.001f) sb.Append(string.Format(" x {0}", otherSourcesMult.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    if (Math.Abs(extBlunt - 1f) > 0.001f) sb.Append(string.Format(" x {0}", extBlunt.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute)));
                    sb.AppendLine(string.Format(" = {0} {1}", calcBlunt.ToStringByStyle(ToStringStyle.FloatMaxTwo, ToStringNumberSense.Absolute), "CE_MPa".Translate()));

                    sb.AppendLine();
                }

                __result = sb.ToString();

                return false; 
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Zoology/UI] Exception in Explanation prefix: {ex}", 876543210);
                return true; 
            }
        }

        
        private static float InvokePrivateFloat(object instance, string methodName, object[] args, float fallback)
        {
            if (instance == null) return fallback;
            try
            {
                var mi = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi != null)
                {
                    var val = mi.Invoke(instance, args);
                    if (val is float f) return f;
                    if (val is double d) return (float)d;
                    if (val != null) return Convert.ToSingle(val);
                }
            }
            catch { }
            return fallback;
        }

        private static float ReadFloatFromToolMember(object toolObj, string[] candidateNames, float defaultValue)
        {
            if (toolObj == null || candidateNames == null) return defaultValue;
            Type t = toolObj.GetType();
            foreach (var name in candidateNames)
            {
                try
                {
                    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null)
                    {
                        var v = f.GetValue(toolObj);
                        if (v != null) return Convert.ToSingle(v);
                    }
                    var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.GetGetMethod(true) != null)
                    {
                        var v2 = p.GetValue(toolObj, null);
                        if (v2 != null) return Convert.ToSingle(v2);
                    }
                }
                catch { }
            }
            return defaultValue;
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
            var t = AccessTools.TypeByName("CombatExtended.StatWorker_MeleeArmorPenetration");
            if (t == null) return null;
            return AccessTools.Method(t, "GetFinalDisplayValue", new Type[] { typeof(StatRequest) });
        }

        [HarmonyPrefix]
        static bool Prefix_GetFinalDisplayValue(object __instance, StatRequest optionalReq, ref string __result)
        {
            try
            {
                
                if (ZoologyModSettings.Instance == null || !ZoologyModSettings.Instance.EnableOverrideCEPenetration)
                {
                    return true; 
                }

                ThingDef thingDef = optionalReq.Def as ThingDef;
                List<Tool> list = (thingDef != null) ? thingDef.tools : null;
                if (list.NullOrEmpty()) { __result = ""; return false; }

                
                float totalChance = list.Aggregate(0f, (acc, t) => acc + (t?.chanceFactor ?? 0f));
                if (totalChance <= 0f) totalChance = 1f;

                Pawn pawn = null;
                if (optionalReq.Thing != null) pawn = optionalReq.Thing as Pawn;
                if (pawn == null && optionalReq.Thing != null)
                {
                    var holder = optionalReq.Thing.ParentHolder;
                    pawn = (holder as Pawn_EquipmentTracker)?.pawn;
                }

                float lifeDFPow = 1f;
                float statPow = 1f;
                try { var ls = pawn?.ageTracker?.CurLifeStage; lifeDFPow = (ls != null) ? Mathf.Pow(ls.meleeDamageFactor, 0.75f) : 1f; } catch { lifeDFPow = 1f; }
                try { statPow = Mathf.Pow(pawn?.GetStatValue(StatDefOf.MeleeDamageFactor, true, -1) ?? 1f, 0.75f); } catch { statPow = 1f; }
                float currentOtherMult = lifeDFPow * statPow;

                float extSharp = 1f, extBlunt = 1f;
                if (pawn != null)
                {
                    var lifeDef = LifeStageUtility.GetPenetrationDefForPawn(pawn);
                    if (lifeDef != null) { extSharp = lifeDef.meleePenetrationSharpFactor; extBlunt = lifeDef.meleePenetrationBluntFactor; }
                }

                float weightedSharp = 0f, weightedBlunt = 0f;
                foreach (Tool tool in list)
                {
                    if (tool == null) continue;
                    float share = (totalChance > 0f) ? (tool.chanceFactor / totalChance) : 1f;
                    float toolAPSharp = ReadFloatFromToolMember(tool, new[] { "armorPenetrationSharp", "armorPenetration" }, 0f);
                    float toolAPBlunt = ReadFloatFromToolMember(tool, new[] { "armorPenetrationBlunt", "armorPenetration" }, 0f);

                    
                    float otherSourcesMult = statPow;
                    float newOtherSharp = otherSourcesMult * extSharp;
                    float newOtherBlunt = otherSourcesMult * extBlunt;

                    weightedSharp += share * toolAPSharp * newOtherSharp;
                    weightedBlunt += share * toolAPBlunt * newOtherBlunt;
                }

                float penetrationFactor = InvokePrivateFloat(__instance, "GetPenetrationFactor", new object[] { optionalReq }, 1f);
                float skillFactor = InvokePrivateFloat(__instance, "GetSkillFactor", new object[] { optionalReq }, 1f);

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

        private static float InvokePrivateFloat(object instance, string methodName, object[] args, float fallback)
        {
            if (instance == null) return fallback;
            try
            {
                var mi = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (mi != null)
                {
                    var val = mi.Invoke(instance, args);
                    if (val is float f) return f;
                    if (val is double d) return (float)d;
                    if (val != null) return Convert.ToSingle(val);
                }
            }
            catch { }
            return fallback;
        }

        private static float ReadFloatFromToolMember(object toolObj, string[] candidateNames, float defaultValue)
        {
            if (toolObj == null || candidateNames == null) return defaultValue;
            Type t = toolObj.GetType();
            foreach (var name in candidateNames)
            {
                try
                {
                    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null)
                    {
                        var v = f.GetValue(toolObj);
                        if (v != null) return Convert.ToSingle(v);
                    }
                    var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.GetGetMethod(true) != null)
                    {
                        var v2 = p.GetValue(toolObj, null);
                        if (v2 != null) return Convert.ToSingle(v2);
                    }
                }
                catch { }
            }
            return defaultValue;
        }
    }
}