using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    public static class CEPatches_Melee
    {
        private const int ERR_REG = 12345682;
        private const int ERR_PREFIX = 12345683;

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
                {
                    harmony.Patch(sharpGetter, prefix: new HarmonyMethod(typeof(CEPatches_Melee).GetMethod(nameof(Prefix_ArmorPenetrationSharp), BindingFlags.Static | BindingFlags.NonPublic)));
                }

                if (bluntGetter != null)
                {
                    harmony.Patch(bluntGetter, prefix: new HarmonyMethod(typeof(CEPatches_Melee).GetMethod(nameof(Prefix_ArmorPenetrationBlunt), BindingFlags.Static | BindingFlags.NonPublic)));
                }
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

        private static bool Prefix_ArmorPenetrationSharp(object __instance, ref float __result)
        {
            try { return Prefix_ArmorPenetrationGeneric(__instance, ref __result, true); }
            catch (Exception ex) { Log.ErrorOnce($"[Zoology] Exception Sharp prefix: {ex}", ERR_PREFIX); return true; }
        }

        private static bool Prefix_ArmorPenetrationBlunt(object __instance, ref float __result)
        {
            try { return Prefix_ArmorPenetrationGeneric(__instance, ref __result, false); }
            catch (Exception ex) { Log.ErrorOnce($"[Zoology] Exception Blunt prefix: {ex}", ERR_PREFIX); return true; }
        }

        private static bool Prefix_ArmorPenetrationGeneric(object verbInstance, ref float __result, bool isSharp)
        {
            var settings = ZoologyModSettings.Instance;
            if (settings == null || !settings.EnableOverrideCEPenetration)
            {
                return true;
            }

            if (verbInstance == null)
            {
                return true;
            }

            Pawn caster = CEReflectionUtility.GetCasterPawn(verbInstance);
            if (caster == null)
            {
                try
                {
                    if (Find.Selector?.SingleSelectedThing is Pawn selectedPawn)
                    {
                        caster = selectedPawn;
                    }
                }
                catch
                {
                    caster = null;
                }
            }

            if (!CEReflectionUtility.TryGetVerbTool(verbInstance, out object toolObj))
            {
                return true;
            }

            float toolAP;
            bool hasToolAp = isSharp
                ? CEReflectionUtility.TryReadSharpToolPenetration(toolObj, out toolAP)
                : CEReflectionUtility.TryReadBluntToolPenetration(toolObj, out toolAP);
            if (!hasToolAp)
            {
                return true;
            }

            float extFactor = 1f;
            try
            {
                var lifeDef = LifeStageUtility.GetPenetrationDefForPawn(caster);
                if (lifeDef != null)
                {
                    extFactor = isSharp ? lifeDef.meleePenetrationSharpFactor : lifeDef.meleePenetrationBluntFactor;
                }
            }
            catch
            {
                extFactor = 1f;
            }

            float statPow = CEReflectionUtility.GetMeleeDamageFactorStatPow(caster);
            float skillMult = CEReflectionUtility.GetPenetrationSkillMultiplier(verbInstance);
            float equipmentMult = CEReflectionUtility.GetEquipmentPenetrationFactor(CEReflectionUtility.GetEquipmentSource(verbInstance));

            __result = toolAP * statPow * extFactor * skillMult * equipmentMult;
            return false;
        }

        private static MethodInfo TryGetPropertyGetter(Type type, string propName)
        {
            try
            {
                var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null) return prop.GetGetMethod(true);
            }
            catch
            {
            }

            return null;
        }

        private static MethodInfo FindMethodByNameCandidates(Type type, string[] candidates)
        {
            try
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    var method = type.GetMethod(candidates[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (method != null && (method.ReturnType == typeof(float) || method.ReturnType == typeof(Single)))
                    {
                        return method;
                    }
                }

                return type
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                        (method.ReturnType == typeof(float) || method.ReturnType == typeof(Single))
                        && (method.Name.IndexOf("Penetration", StringComparison.OrdinalIgnoreCase) >= 0
                            || method.Name.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0));
            }
            catch
            {
                return null;
            }
        }
    }
}
