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
        private static bool patched;
        private const int ERR_REG = 12345682;
        private const int ERR_PREFIX = 12345683;

        static CEPatches_Melee()
        {
            EnsurePatched();
        }

        public static void EnsurePatched()
        {
            if (patched)
            {
                return;
            }

            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && settings.DisableAllRuntimePatches)
                {
                    return;
                }

                Type ceVerbType = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE");
                if (ceVerbType == null)
                    return;

                var harmony = new Harmony("com.abobashark.zoology.mod.melee");

                MethodInfo sharpGetter = TryGetPropertyGetter(ceVerbType, "ArmorPenetrationSharp")
                    ?? FindMethodByNameCandidates(ceVerbType, new[] { "get_ArmorPenetrationSharp" });
                MethodInfo bluntGetter = TryGetPropertyGetter(ceVerbType, "ArmorPenetrationBlunt")
                    ?? FindMethodByNameCandidates(ceVerbType, new[] { "get_ArmorPenetrationBlunt" });

                if (sharpGetter != null)
                    harmony.Patch(sharpGetter, postfix: new HarmonyMethod(
                        typeof(CEPatches_Melee).GetMethod(nameof(Postfix_ArmorPenetrationSharp),
                            BindingFlags.Static | BindingFlags.NonPublic)));

                if (bluntGetter != null)
                    harmony.Patch(bluntGetter, postfix: new HarmonyMethod(
                        typeof(CEPatches_Melee).GetMethod(nameof(Postfix_ArmorPenetrationBlunt),
                            BindingFlags.Static | BindingFlags.NonPublic)));

                patched = sharpGetter != null || bluntGetter != null;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Zoology] Error registering melee AP patches: {ex}", ERR_REG);
            }
        }

        public static void ResetPatchedState()
        {
            patched = false;
        }

        public static bool Prepare()
        {
            return CEChecker.IsCEInstalled();
        }

        private static void Postfix_ArmorPenetrationSharp(object __instance, ref float __result)
        {
            try { ApplyArmorPenetration(__instance, ref __result, true); }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology] Exception in sharp natural-attack AP: " + ex, ERR_PREFIX);
            }
        }

        private static void Postfix_ArmorPenetrationBlunt(object __instance, ref float __result)
        {
            try { ApplyArmorPenetration(__instance, ref __result, false); }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Zoology] Exception in blunt natural-attack AP: " + ex, ERR_PREFIX + 1);
            }
        }

        private static void ApplyArmorPenetration(object verbInstance, ref float result, bool sharp)
        {
            if (verbInstance == null)
                return;

            var settings = ZoologyModSettings.Instance;
            if (settings != null && settings.DisableAllRuntimePatches)
                return;

            Pawn caster = CEReflectionUtility.GetCasterPawn(verbInstance);
            if (caster?.RaceProps?.Animal != true)
                return;

            // CE may use CasterPawn as the equipment source for natural verbs.
            // Actual equipped weapons and Hediff-granted attacks are excluded.
            object equipment = CEReflectionUtility.GetEquipmentSource(verbInstance);
            if (equipment != null && !ReferenceEquals(equipment, caster))
                return;

            object toolObj;
            if (!CEReflectionUtility.TryGetVerbTool(verbInstance, out toolObj))
                return;

            float interpolated;
            if (ZoologyAnimalGrowth.TryGetToolFactor(caster, toolObj as Tool,
                    sharp ? ZoologyAnimalGrowth.GrowthToolFactor.CeSharpPenetration
                          : ZoologyAnimalGrowth.GrowthToolFactor.CeBluntPenetration,
                    out interpolated))
            {
                result *= interpolated;
                return;
            }

            ZoologyLifeStageToolFactor factors;
            if (ZoologyLifeStageProfileInstaller.TryGetToolFactor(caster,
                    toolObj as Tool, out factors))
            {
                // The CE getter's native computation already includes natural
                // pawn stat, skill and any other external modifiers. Apply
                // AnimalStats' final stage/adult Tool ratio exactly once.
                result *= sharp ? factors.ceSharpPenetrationFactor
                    : factors.ceBluntPenetrationFactor;
                return;
            }

            // Optional legacy generic penetration override for attacks without
            // generated species profiles. It must not affect generated attacks.
            if (settings == null || !settings.EnableOverrideCEPenetration)
                return;

            float toolAp;
            bool hasAp = sharp
                ? CEReflectionUtility.TryReadSharpToolPenetration(toolObj, out toolAp)
                : CEReflectionUtility.TryReadBluntToolPenetration(toolObj, out toolAp);
            if (!hasAp)
                return;

            var lifeDef = LifeStageUtility.GetPenetrationDefForPawn(caster);
            if (lifeDef == null)
                return;

            float factor = sharp ? lifeDef.meleePenetrationSharpFactor
                : lifeDef.meleePenetrationBluntFactor;
            if (!float.IsNaN(factor) && !float.IsInfinity(factor) && factor >= 0f)
                result *= factor;
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
