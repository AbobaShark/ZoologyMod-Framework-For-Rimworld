using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod.Patches
{
    public static class DamageReduction_AnimalTypes_PawnTakeDamage
    {
        private const string DamageReductionHarmonyId = "com.abobashark.zoology.damage_reduction";
        private static bool isPatched;

        private static bool ShouldBeEnabledNow()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || (!settings.DisableAllRuntimePatches && settings.EnableAnimalDamageReduction);
        }

        public static void ResetPatchedState()
        {
            isPatched = false;
        }

        public static void SyncPatchState()
        {
            if (ShouldBeEnabledNow())
            {
                EnsurePatched();
            }
            else
            {
                EnsureUnpatched();
            }
        }

        public static void EnsurePatched()
        {
            if (isPatched)
            {
                return;
            }

            try
            {
                MethodInfo prefixMethod = AccessTools.Method(typeof(DamageReduction_AnimalTypes_PawnTakeDamage), nameof(Prefix));
                if (prefixMethod == null)
                {
                    return;
                }

                List<MethodBase> targetMethods = GetTargetMethods();
                if (targetMethods.Count == 0)
                {
                    Log.Error("[ZoologyMod] DamageReduction_AnimalTypes: target methods not found. Patch will stay disabled.");
                    return;
                }

                var harmony = new Harmony(DamageReductionHarmonyId);
                for (int targetIndex = 0; targetIndex < targetMethods.Count; targetIndex++)
                {
                    MethodBase targetMethod = targetMethods[targetIndex];
                    bool alreadyPatched = false;
                    HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(targetMethod);
                    if (patchInfo?.Prefixes != null)
                    {
                        for (int i = 0; i < patchInfo.Prefixes.Count; i++)
                        {
                            if (patchInfo.Prefixes[i].PatchMethod == prefixMethod)
                            {
                                alreadyPatched = true;
                                break;
                            }
                        }
                    }

                    if (!alreadyPatched)
                    {
                        harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                    }
                }

                isPatched = true;
            }
            catch (Exception ex)
            {
                isPatched = false;
                Log.Warning($"[ZoologyMod] DamageReduction_AnimalTypes EnsurePatched failed: {ex}");
            }
        }

        public static void EnsureUnpatched()
        {
            try
            {
                MethodInfo prefixMethod = AccessTools.Method(typeof(DamageReduction_AnimalTypes_PawnTakeDamage), nameof(Prefix));
                if (prefixMethod == null)
                {
                    return;
                }

                var harmony = new Harmony("com.abobashark.zoology.unpatcher");
                List<MethodBase> targetMethods = GetTargetMethods();
                for (int i = 0; i < targetMethods.Count; i++)
                {
                    harmony.Unpatch(targetMethods[i], prefixMethod);
                }

                isPatched = false;
            }
            catch (Exception ex)
            {
                isPatched = false;
                Log.Warning($"[ZoologyMod] DamageReduction_AnimalTypes EnsureUnpatched failed: {ex}");
            }
        }

        private static List<MethodBase> GetTargetMethods()
        {
            var result = new List<MethodBase>(2);

            MethodBase thingTakeDamage = AccessTools.DeclaredMethod(typeof(Thing), "TakeDamage", new Type[] { typeof(DamageInfo) });
            if (thingTakeDamage != null)
            {
                result.Add(thingTakeDamage);
            }

            MethodBase pawnTakeDamage = AccessTools.DeclaredMethod(typeof(Pawn), "TakeDamage", new Type[] { typeof(DamageInfo) });
            if (pawnTakeDamage != null && !result.Contains(pawnTakeDamage))
            {
                result.Add(pawnTakeDamage);
            }

            if (result.Count == 0)
            {
                // Fallback for unexpected method layout in modified environments.
                MethodBase fallback = AccessTools.Method(typeof(Thing), "TakeDamage", new Type[] { typeof(DamageInfo) });
                if (fallback != null)
                {
                    result.Add(fallback);
                }
            }

            return result;
        }

        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            try
            {
                if (!ShouldBeEnabledNow())
                {
                    return;
                }

                if (dinfo.Def == null)
                {
                    return;
                }

                Pawn victim = __instance as Pawn;
                if (victim == null || !victim.IsAnimal)
                {
                    return;
                }

                Pawn attackerPawn = dinfo.Instigator as Pawn;
                if (attackerPawn == null)
                {
                    return;
                }

                bool instigatorIsAnimal = attackerPawn.IsAnimal;
                bool isHumanNaturalAttack = !instigatorIsAnimal && attackerPawn.RaceProps?.Humanlike == true;
                if (!instigatorIsAnimal && !isHumanNaturalAttack)
                {
                    return;
                }

                if (!IsSupportedNaturalPawnAttack(attackerPawn, dinfo))
                {
                    return;
                }

                bool victimIsPredator = victim.RaceProps?.predator ?? false;
                bool attackerIsPredator = false;

                float victimBodySizeActual = victim.BodySize;
                float attackerBodySizeActual = 1f;

                float victimBaseSize = 1f;
                float attackerBaseSize = 1f;

                if (victim.def?.race != null)
                    victimBaseSize = victim.def.race.baseBodySize;

                attackerBodySizeActual = attackerPawn.BodySize;
                attackerIsPredator = attackerPawn.RaceProps?.predator ?? false;
                if (isHumanNaturalAttack)
                {
                    attackerIsPredator = false;
                }
                if (attackerPawn.def?.race != null)
                {
                    attackerBaseSize = attackerPawn.def.race.baseBodySize;
                }

                float beforeAmount = dinfo.Amount;

                bool sizeThreshold = victimBodySizeActual >= 1.2f * attackerBodySizeActual;
                float factor = 1f;
                bool applied = false;

                if (!attackerIsPredator && victimIsPredator)
                {
                    if (attackerBaseSize >= victimBaseSize * 2f)
                    {
                        applied = false;
                    }
                    else if (attackerBaseSize > victimBaseSize)
                    {
                        factor = 0.5f;
                        applied = true;
                    }
                    else if (victimBaseSize > 0f)
                    {
                        factor = attackerBaseSize / victimBaseSize;
                        applied = true;
                    }
                }
                else if (attackerIsPredator == victimIsPredator && sizeThreshold)
                {
                    if (victimBaseSize > 0f)
                    {
                        factor = attackerBaseSize / victimBaseSize;
                        applied = true;
                    }
                }

                if (!applied) return;

                if (factor > 1f) factor = 1f;
                if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f) return;

                float newAmount = beforeAmount * factor;
                try
                {
                    dinfo.SetAmount(newAmount);
                }
                catch
                {
                    try
                    {
                        DamageInfo updatedDamageInfo = new DamageInfo(dinfo);
                        updatedDamageInfo.SetAmount(newAmount);
                        dinfo = updatedDamageInfo;
                    }
                    catch
                    {
                        Log.Warning("[ZoologyMod] AnimalDamageReduction: не удалось создать/изменить DamageInfo для редукции урона. Отказываемся от редукции в этом случае.");
                    }
                }

            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Exception in DamageReduction_AnimalTypes_Prefix: {ex}");
            }
        }

        private static bool IsSupportedNaturalPawnAttack(Pawn attackerPawn, DamageInfo dinfo)
        {
            if (attackerPawn == null || dinfo.Def == null)
            {
                return false;
            }

            if (!attackerPawn.IsAnimal && attackerPawn.RaceProps?.Humanlike != true)
            {
                return false;
            }

            // Skip hediff-driven body attacks such as bionics/implants.
            if (dinfo.WeaponLinkedHediff != null)
            {
                return false;
            }

            ThingDef weapon = dinfo.Weapon;
            if (weapon != null && weapon != attackerPawn.def)
            {
                return false;
            }

            Tool tool = dinfo.Tool;
            if (tool != null)
            {
                List<Tool> naturalTools = attackerPawn.def?.tools;
                if (naturalTools != null && naturalTools.Contains(tool))
                {
                    return true;
                }
            }

            if (weapon == attackerPawn.def)
            {
                return true;
            }

            if (attackerPawn.equipment?.Primary != null)
            {
                return false;
            }

            // Fallback for natural unarmed melee where a modded verb did not preserve tool/weapon metadata.
            return !dinfo.Def.isRanged;
        }

    }

    [StaticConstructorOnStartup]
    internal static class DamageReduction_AnimalTypes_Bootstrap
    {
        static DamageReduction_AnimalTypes_Bootstrap()
        {
            LongEventHandler.ExecuteWhenFinished(DamageReduction_AnimalTypes_PawnTakeDamage.SyncPatchState);
        }
    }
}
