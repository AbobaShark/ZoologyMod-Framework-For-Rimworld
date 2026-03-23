using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod.Patches
{
    [HarmonyPatch]
    public static class DamageReduction_AnimalTypes_PawnTakeDamage
    {
        private static readonly Type ScratchWorkerType = AccessTools.TypeByName("DamageWorker_Scratch");
        private static readonly Type BiteWorkerType = AccessTools.TypeByName("DamageWorker_Bite");
        private static readonly Type BluntWorkerType = AccessTools.TypeByName("DamageWorker_Blunt");

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableAnimalDamageReduction;
        }

        static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(typeof(Pawn), "TakeDamage", new Type[] { typeof(DamageInfo) });
            if (method != null)
            {
                return method;
            }

            method = AccessTools.Method(typeof(Thing), "TakeDamage", new Type[] { typeof(DamageInfo) });
            if (method == null)
            {
                Log.Error("[ZoologyMod] DamageReduction_AnimalTypes: не найден целевой метод Pawn/Thing.TakeDamage(DamageInfo). Патч не будет применён.");
            }

            return method;
        }

        [HarmonyPrefix]
        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableAnimalDamageReduction)
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

                Type workerClass = dinfo.Def.workerClass;
                bool isScratch = ScratchWorkerType != null && (workerClass == ScratchWorkerType || workerClass.IsSubclassOf(ScratchWorkerType));
                bool isBite = BiteWorkerType != null && (workerClass == BiteWorkerType || workerClass.IsSubclassOf(BiteWorkerType));
                bool isBlunt = BluntWorkerType != null && (workerClass == BluntWorkerType || workerClass.IsSubclassOf(BluntWorkerType));
                if (!isScratch && !isBite && !isBlunt) return;

                Pawn attackerPawn = instigator as Pawn;
                if (attackerPawn == null) return;

                bool instigatorIsAnimal = attackerPawn.IsAnimal;
                bool isHumanNaturalToolAttack = IsHumanNaturalToolAttack(attackerPawn, dinfo);

                if (!instigatorIsAnimal && !isHumanNaturalToolAttack) return;

                // Skip reduction for attacks done with weapons or hediff-based tools (bionics).
                if (attackerPawn != null)
                {
                    if (dinfo.WeaponLinkedHediff != null)
                    {
                        return;
                    }
                    if (dinfo.Weapon != null && dinfo.Weapon != attackerPawn.def)
                    {
                        return;
                    }
                }

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

        private static bool IsHumanNaturalToolAttack(Pawn attackerPawn, DamageInfo dinfo)
        {
            if (attackerPawn == null)
            {
                return false;
            }

            var raceProps = attackerPawn.RaceProps;
            if (raceProps == null || !raceProps.Humanlike)
            {
                return false;
            }

            return IsPawnNaturalToolAttack(attackerPawn, dinfo);
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

            return false;
        }


    }
}
