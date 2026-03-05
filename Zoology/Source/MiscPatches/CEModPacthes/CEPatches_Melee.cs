// CEPatches_Melee.cs

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using System.Text;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    public static class CEPatches_Melee
    {
        private const int ERR_REG = 12345682;
        private const int ERR_PREFIX = 12345683;
        private const int ERR_RUNTIME = 12345684;

        static CEPatches_Melee()
        {
            try
            {
                var harmony = new Harmony("com.abobashark.zoology.mod.melee");
                Type ceVerbType = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE");

                if (ceVerbType == null)
                {
                    return;
                }

                MethodInfo sharpGetter = TryGetPropertyGetter(ceVerbType, "ArmorPenetrationSharp")
                    ?? FindMethodByNameCandidates(ceVerbType, new[] { "get_ArmorPenetrationSharp" });

                MethodInfo bluntGetter = TryGetPropertyGetter(ceVerbType, "ArmorPenetrationBlunt")
                    ?? FindMethodByNameCandidates(ceVerbType, new[] { "get_ArmorPenetrationBlunt" });

                if (sharpGetter != null)
                    harmony.Patch(sharpGetter, prefix: new HarmonyMethod(typeof(CEPatches_Melee).GetMethod(nameof(Prefix_ArmorPenetrationSharp), BindingFlags.Static | BindingFlags.NonPublic)));
                if (bluntGetter != null)
                    harmony.Patch(bluntGetter, prefix: new HarmonyMethod(typeof(CEPatches_Melee).GetMethod(nameof(Prefix_ArmorPenetrationBlunt), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Zoology] Error registering melee AP patches: {ex}", ERR_REG);
            }
        }
        public static bool Prepare()
        {
            return CEChecker.IsCEInstalled();
        }

        static bool Prefix_ArmorPenetrationSharp(object __instance, ref float __result)
        {
            try { return Prefix_ArmorPenetrationGeneric(__instance, ref __result, true); }
            catch (Exception ex) { Log.ErrorOnce($"[Zoology] Exception Sharp prefix: {ex}", ERR_PREFIX); return true; }
        }

        static bool Prefix_ArmorPenetrationBlunt(object __instance, ref float __result)
        {
            try { return Prefix_ArmorPenetrationGeneric(__instance, ref __result, false); }
            catch (Exception ex) { Log.ErrorOnce($"[Zoology] Exception Blunt prefix: {ex}", ERR_PREFIX); return true; }
        }

        private static bool Prefix_ArmorPenetrationGeneric(object verbInstance, ref float __result, bool isSharp)
        {
            // Respect user setting: если override выключен или Settings ещё не инициализированы => не вмешиваемся
            try
            {
                if (ZoologyModSettings.Instance == null || !ZoologyModSettings.Instance.EnableOverrideCEPenetration)
                {
                    return true; // run original CE logic
                }
            }
            catch
            {
                // any error -> fallback to original
                return true;
            }

            if (verbInstance == null) return true;
            Type verbType = verbInstance.GetType();

            // Получаем Pawn-атакующего (несколько fallbacks)
            Pawn caster = GetCasterPawnFromVerbInstance(verbInstance);

            if (caster == null)
            {
                // try selected pawn as a best-effort fallback for inspect/tooltips
                try
                {
                    var sel = Find.Selector?.SingleSelectedThing;
                    if (sel is Pawn sp) caster = sp;
                }
                catch { caster = null; }
            }

            // Получим tool (ToolCE или Tool)
            object toolObj = null;
            try
            {
                var propTool = verbType.GetProperty("ToolCE", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (propTool != null) toolObj = propTool.GetValue(verbInstance);
                else
                {
                    var baseType = verbType;
                    while (baseType != null)
                    {
                        var f = baseType.GetField("tool", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (f != null) { toolObj = f.GetValue(verbInstance); break; }
                        baseType = baseType.BaseType;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Zoology] Error getting toolObj: {ex}", ERR_RUNTIME);
                return true;
            }

            if (toolObj == null)
            {
                return true;
            }

            // Считываем AP из tool
            float toolAP = 0f;
            try
            {
                string fieldName = isSharp ? "armorPenetrationSharp" : "armorPenetrationBlunt";
                var fAP = toolObj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fAP != null) toolAP = Convert.ToSingle(fAP.GetValue(toolObj));
                else
                {
                    var pAP = toolObj.GetType().GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pAP != null) toolAP = Convert.ToSingle(pAP.GetValue(toolObj));
                    else { return true; }
                }
            }
            catch (Exception ex) { Log.ErrorOnce($"[Zoology] Error reading tool AP: {ex}", ERR_RUNTIME); return true; }

            // skillMult (как CE делает)
            float skillMult = 1f;
            try { var prop = verbType.GetProperty("PenetrationSkillMultiplier", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); if (prop != null) skillMult = Convert.ToSingle(prop.GetValue(verbInstance)); }
            catch { skillMult = 1f; }

            // equipmentMult (как раньше)
            float equipmentMult = 1f;
            try
            {
                var propEq = verbType.BaseType?.GetProperty("EquipmentSource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                object equipmentSource = propEq?.GetValue(verbInstance);
                if (equipmentSource != null)
                {
                    var statDef = DefDatabase<StatDef>.GetNamedSilentFail("MeleePenetrationFactor");
                    if (statDef != null)
                    {
                        var getStat = equipmentSource.GetType().GetMethod("GetStatValue", new Type[] { typeof(StatDef), typeof(bool), typeof(int) });
                        if (getStat != null)
                        {
                            var val = getStat.Invoke(equipmentSource, new object[] { statDef, true, -1 });
                            if (val != null) equipmentMult = Convert.ToSingle(val);
                        }
                        else
                        {
                            var g2 = equipmentSource.GetType().GetMethod("GetStatValue", new Type[] { typeof(StatDef), typeof(bool) });
                            if (g2 != null)
                            {
                                var val2 = g2.Invoke(equipmentSource, new object[] { statDef, true });
                                if (val2 != null) equipmentMult = Convert.ToSingle(val2);
                            }
                        }
                    }
                }
            }
            catch { equipmentMult = 1f; }

            // --- ВАЖНО: вычисляем currentOtherMult как CE делает, но разделяем вклад lifeStage и прочих источников ---
            float lifeDamageFactor = 1f;
            string lifeStageName = "<null>";
            try
            {
                var ls = caster?.ageTracker?.CurLifeStage;
                if (ls != null) { lifeDamageFactor = ls.meleeDamageFactor; lifeStageName = ls.defName; }
            }
            catch { lifeDamageFactor = 1f; }

            // statValue: агрегированный MeleeDamageFactor (включая lifeStage и прочие источники)
            float totalStatMeleeDF = 1f;
            try
            {
                totalStatMeleeDF = caster != null ? caster.GetStatValue(StatDefOf.MeleeDamageFactor, true, -1) : 1f;
            }
            catch { totalStatMeleeDF = 1f; }

            // pow'ed versions (как CE: ^0.75)
            float lifeDFPow = 1f;
            try { lifeDFPow = Mathf.Pow(lifeDamageFactor, 0.75f); } catch { lifeDFPow = 1f; }
            float statPow = 1f;
            try { statPow = Mathf.Pow(totalStatMeleeDF, 0.75f); } catch { statPow = 1f; }

            // currentOtherMult по логике CE = lifeDFPow * statPow (если pawn != null)
            float currentOtherMult = 1f;
            if (caster != null)
            {
                currentOtherMult = (float)((double)lifeDFPow * (double)statPow);
            }
            else
            {
                currentOtherMult = 1f;
            }

            // Получаем наш zoology extFactor (из LifeStagePenetrationDef / fallback)
            LifeStagePenetrationDef lifeDef = null;
            float extFactor = 1f;
            try
            {
                lifeDef = LifeStageUtility.GetPenetrationDefForPawn(caster);
                if (lifeDef != null) extFactor = isSharp ? lifeDef.meleePenetrationSharpFactor : lifeDef.meleePenetrationBluntFactor;
            }
            catch { lifeDef = null; extFactor = 1f; }

            // --- Основная логика: заменяем именно вклад lifeStage на extFactor, оставляя прочие stat-вклады ---
            float newOtherMult = currentOtherMult;
            try
            {
                if (caster != null && Math.Abs(lifeDFPow - 1f) > 1e-6f)
                {
                    float divisor = lifeDFPow;
                    if (divisor > 1e-8f) newOtherMult = (currentOtherMult / divisor) * extFactor;
                    else newOtherMult = currentOtherMult * extFactor;
                }
                else
                {
                    newOtherMult = currentOtherMult * extFactor;
                }
            }
            catch { newOtherMult = currentOtherMult * extFactor; }

            float result = toolAP * newOtherMult * skillMult * equipmentMult;
            __result = result;

            return false;
        }

        // ---- вспомогалки ----
        private static MethodInfo TryGetPropertyGetter(Type type, string propName)
        {
            try
            {
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null) return prop.GetGetMethod(true);
            }
            catch { }
            return null;
        }

        private static MethodInfo FindMethodByNameCandidates(Type type, string[] candidates)
        {
            try
            {
                foreach (var name in candidates)
                {
                    var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (m != null && (m.ReturnType == typeof(float) || m.ReturnType == typeof(Single))) return m;
                }

                var fallback = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                   .FirstOrDefault(mi => (mi.ReturnType == typeof(float) || mi.ReturnType == typeof(Single)) &&
                                                         (mi.Name.IndexOf("Penetration", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                          mi.Name.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0));
                return fallback;
            }
            catch { }
            return null;
        }

        private static Pawn GetCasterPawnFromVerbInstance(object verbInstance)
        {
            if (verbInstance == null) return null;
            try
            {
                var type = verbInstance.GetType();
                string[] tryNames = { "CasterPawn", "casterPawn", "Caster", "caster", "owner", "CasterThing" };

                foreach (var name in tryNames)
                {
                    var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        var val = p.GetValue(verbInstance);
                        if (val is Pawn pa) return pa;
                    }
                    var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        var val = f.GetValue(verbInstance);
                        if (val is Pawn pa2) return pa2;
                    }
                }

                var verbPropsProp = type.GetProperty("verbProps", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (verbPropsProp != null)
                {
                    var vp = verbPropsProp.GetValue(verbInstance);
                    if (vp != null)
                    {
                        var ownerProp = vp.GetType().GetProperty("owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (ownerProp != null)
                        {
                            var ownerVal = ownerProp.GetValue(vp);
                            if (ownerVal is Pawn p3) return p3;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Zoology] Error reflecting caster pawn: {ex}", ERR_RUNTIME);
            }
            return null;
        }
    }
}