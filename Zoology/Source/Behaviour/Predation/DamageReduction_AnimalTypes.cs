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
        private static readonly Type ScratchWorkerType = AccessTools.TypeByName("DamageWorker_Scratch");
        private static readonly Type BiteWorkerType = AccessTools.TypeByName("DamageWorker_Bite");
        private static readonly Type BluntWorkerType = AccessTools.TypeByName("DamageWorker_Blunt");

        private static bool ShouldBeEnabledNow()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || (!settings.DisableAllRuntimePatches && settings.EnableAnimalDamageReduction);
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
            }
            catch (Exception ex)
            {
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
            }
            catch (Exception ex)
            {
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
                    return;

                if (dinfo.Def == null) return;

                Pawn victim = __instance as Pawn;
                if (victim == null) return;

                Thing instigator = dinfo.Instigator;
                if (instigator == null) return;

                if (!victim.IsAnimal)
                {
                    return;
                }

                if (!IsSupportedAnimalDamageType(dinfo)) return;

                Pawn attackerPawn = instigator as Pawn;
                if (attackerPawn == null) return;

                bool instigatorIsAnimal = attackerPawn.IsAnimal;
                bool isNaturalToolAttack = IsPawnNaturalToolAttack(attackerPawn, dinfo);
                bool isHumanNaturalToolAttack = attackerPawn.RaceProps?.Humanlike == true && isNaturalToolAttack;

                if (!instigatorIsAnimal && !isHumanNaturalToolAttack) return;

                // Skip non-natural attacks (equipment/synthetic sources).
                if (!isNaturalToolAttack) return;

                bool victimIsPredator = victim.RaceProps?.predator ?? false;
                bool attackerIsPredator = false;

                float victimBodySizeActual = victim.BodySize;
                float attackerBodySizeActual = 1f;

                float victimBaseSize = 1f;
                float attackerBaseSize = 1f;

                if (victim.def?.race != null)
                    victimBaseSize = victim.def.race.baseBodySize;

                if (attackerPawn != null)
                {
                    attackerBodySizeActual = attackerPawn.BodySize;
                    attackerIsPredator = attackerPawn.RaceProps?.predator ?? false;
                    if (isHumanNaturalToolAttack)
                    {
                        attackerIsPredator = false;
                    }
                    if (attackerPawn.def?.race != null)
                        attackerBaseSize = attackerPawn.def.race.baseBodySize;
                }
                else
                {
                    ThingDef attackerDef = instigator.def;
                    if (attackerDef != null)
                    {
                        attackerIsPredator = attackerDef.race?.predator ?? false;
                        attackerBaseSize = attackerDef.race?.baseBodySize ?? 1f;
                        attackerBodySizeActual = attackerDef.race?.baseBodySize ?? 1f;
                    }
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
                        dinfo = new DamageInfo(
                            dinfo.Def,
                            newAmount,
                            dinfo.ArmorPenetrationInt,
                            dinfo.Angle,
                            dinfo.Instigator,
                            dinfo.HitPart,
                            dinfo.Weapon
                        );
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

        private static bool IsSupportedAnimalDamageType(DamageInfo dinfo)
        {
            if (dinfo.Def == null)
            {
                return false;
            }

            Type workerClass = dinfo.Def.workerClass;
            bool isScratch = ScratchWorkerType != null
                && workerClass != null
                && (workerClass == ScratchWorkerType || workerClass.IsSubclassOf(ScratchWorkerType));
            bool isBite = BiteWorkerType != null
                && workerClass != null
                && (workerClass == BiteWorkerType || workerClass.IsSubclassOf(BiteWorkerType));
            bool isBlunt = BluntWorkerType != null
                && workerClass != null
                && (workerClass == BluntWorkerType || workerClass.IsSubclassOf(BluntWorkerType));

            if (isScratch || isBite || isBlunt)
            {
                return true;
            }

            // CE-legacy fallback: some old saves carry non-vanilla worker class.
            if (dinfo.Def.isRanged)
            {
                return false;
            }

            DamageArmorCategoryDef armorCategory = dinfo.Def.armorCategory;
            return armorCategory == DamageArmorCategoryDefOf.Sharp
                || (armorCategory != null && string.Equals(armorCategory.defName, "Blunt", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPawnNaturalToolAttack(Pawn attackerPawn, DamageInfo dinfo)
        {
            if (attackerPawn == null)
            {
                return false;
            }

            // Bionic or hediff-based tool (e.g., bionic arms/jaws) should not be reduced.
            if (dinfo.WeaponLinkedHediff != null)
            {
                return false;
            }

            Tool tool = dinfo.Tool;
            if (tool != null)
            {
                var tools = attackerPawn.def?.tools;
                if (tools != null && tools.Contains(tool))
                {
                    return true;
                }
            }

            ThingDef weapon = dinfo.Weapon;
            if (weapon != null && weapon == attackerPawn.def)
            {
                return true;
            }

            // CE-legacy fallback for animal natural melee where weapon/source is stale.
            if (attackerPawn.IsAnimal
                && attackerPawn.equipment?.Primary == null
                && dinfo.Def != null
                && !dinfo.Def.isRanged)
            {
                DamageArmorCategoryDef armorCategory = dinfo.Def.armorCategory;
                if (armorCategory == DamageArmorCategoryDefOf.Sharp
                    || (armorCategory != null && string.Equals(armorCategory.defName, "Blunt", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
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
