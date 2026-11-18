using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    [StaticConstructorOnStartup]
    public static class CEPatches_Melee
    {
        private static bool patched;
        private const int ERR_REG = 12345682;
        private const int ERR_PREFIX = 12345683;
        private static PropertyInfo nativeOtherMultipliersProperty;
        private static StatDef meleePenetrationFactorStat;

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
                if (!(ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled))
                    return;

                Type ceVerbType = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE");
                if (ceVerbType == null)
                    return;

                var harmony = new Harmony("com.abobashark.zoology.mod.melee");
                nativeOtherMultipliersProperty = AccessTools.Property(ceVerbType, "PenetrationOtherMultipliers");
                meleePenetrationFactorStat = DefDatabase<StatDef>.GetNamedSilentFail("MeleePenetrationFactor");
                if (nativeOtherMultipliersProperty == null || meleePenetrationFactorStat == null)
                {
                    Log.ErrorOnce("[Zoology] Cannot resolve CE melee penetration factors; leaving CE melee AP unchanged.", ERR_REG + 2);
                    return;
                }

                MethodInfo sharpGetter = TryGetPropertyGetter(ceVerbType, "ArmorPenetrationSharp")
                    ?? FindMethodByNameCandidates(ceVerbType, new[] { "get_ArmorPenetrationSharp" });
                MethodInfo bluntGetter = TryGetPropertyGetter(ceVerbType, "ArmorPenetrationBlunt")
                    ?? FindMethodByNameCandidates(ceVerbType, new[] { "get_ArmorPenetrationBlunt" });

                bool sharpPatched = sharpGetter != null && EnsureSinglePostfix(harmony,
                    sharpGetter, typeof(CEPatches_Melee).GetMethod(
                        nameof(Postfix_ArmorPenetrationSharp), BindingFlags.Static | BindingFlags.NonPublic));
                bool bluntPatched = bluntGetter != null && EnsureSinglePostfix(harmony,
                    bluntGetter, typeof(CEPatches_Melee).GetMethod(
                        nameof(Postfix_ArmorPenetrationBlunt), BindingFlags.Static | BindingFlags.NonPublic));

                patched = sharpPatched || bluntPatched;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Zoology] Error registering melee AP patches: {ex}", ERR_REG);
            }
        }

        private static bool EnsureSinglePostfix(Harmony harmony, MethodInfo target, MethodInfo postfix)
        {
            if (target == null || postfix == null)
                return false;

            var patchInfo = Harmony.GetPatchInfo(target);
            int copies = patchInfo?.Postfixes?.Count(p => p.PatchMethod == postfix) ?? 0;
            if (copies == 1)
                return true;

            if (copies > 1)
                harmony.Unpatch(target, postfix);

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            return true;
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
            if (!(ZoologyAnimalGrowth.Enabled || ZoologySexAdultProfiles.Enabled))
                return;

            float nativePenetration, stageFactor, expectedPenetration;
            if (TryCalculateNaturalPenetration(verbInstance, sharp,
                    out nativePenetration, out stageFactor, out expectedPenetration))
            {
                // Assign the absolute result rather than multiplying __result:
                // repeated Harmony postfixes must not compound the age factor.
                // The native CE factors still include body-part efficiency,
                // skill, equipment stats and other CE verb modifiers.
                result = expectedPenetration;
            }
        }

        internal static bool TryCalculateNaturalPenetration(object verbInstance,
            bool sharp, out float nativePenetration, out float stageFactor,
            out float expectedPenetration)
        {
            nativePenetration = 0f;
            stageFactor = 1f;
            expectedPenetration = 0f;

            if (verbInstance is not Verb verb)
                return false;

            Pawn caster = CEReflectionUtility.GetCasterPawn(verbInstance);
            if (caster?.RaceProps?.Animal != true)
                return false;

            object equipment = CEReflectionUtility.GetEquipmentSource(verbInstance);
            if (equipment != null && !ReferenceEquals(equipment, caster))
                return false;

            if (!CEReflectionUtility.TryGetVerbTool(verbInstance, out object toolObj)
                || toolObj is not Tool tool)
                return false;

            float basePenetration;
            if (!(sharp
                    ? CEReflectionUtility.TryReadSharpToolPenetration(toolObj, out basePenetration)
                    : CEReflectionUtility.TryReadBluntToolPenetration(toolObj, out basePenetration)))
                return false;

            float interpolated;
            if (ZoologyAnimalGrowth.TryGetToolFactor(caster, tool,
                    sharp ? ZoologyAnimalGrowth.GrowthToolFactor.CeSharpPenetration
                          : ZoologyAnimalGrowth.GrowthToolFactor.CeBluntPenetration,
                    out interpolated))
                stageFactor = interpolated;
            else if (ZoologyLifeStageProfileInstaller.TryGetToolFactor(caster,
                         tool, out ZoologyLifeStageToolFactor profile))
                stageFactor = sharp ? profile.ceSharpPenetrationFactor
                                    : profile.ceBluntPenetrationFactor;
            else
                return false;

            PropertyInfo otherProperty = nativeOtherMultipliersProperty
                ?? AccessTools.Property(verbInstance.GetType(), "PenetrationOtherMultipliers");
            if (otherProperty == null)
                return false;

            float other = Convert.ToSingle(otherProperty.GetValue(verbInstance, null));
            float skill = CEReflectionUtility.GetPenetrationSkillMultiplier(verbInstance);
            float equipmentFactor = equipment is Thing thing
                ? thing.GetStatValue(meleePenetrationFactorStat
                    ?? DefDatabase<StatDef>.GetNamed("MeleePenetrationFactor"))
                : 1f;

            nativePenetration = basePenetration * other * skill * equipmentFactor;
            expectedPenetration = nativePenetration * stageFactor;
            return !float.IsNaN(expectedPenetration)
                && !float.IsInfinity(expectedPenetration)
                && expectedPenetration >= 0f;
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
