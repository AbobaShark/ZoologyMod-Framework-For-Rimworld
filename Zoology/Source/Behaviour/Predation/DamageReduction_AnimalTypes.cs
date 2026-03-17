using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod.Patches
{
    [HarmonyPatch]
    public static class DamageReduction_AnimalTypes_PawnTakeDamage
    {
        private const string AnimalThingBaseDefName = "AnimalThingBase";

        private static readonly Type ScratchWorkerType = AccessTools.TypeByName("DamageWorker_Scratch");
        private static readonly Type BiteWorkerType = AccessTools.TypeByName("DamageWorker_Bite");
        private static readonly Type BluntWorkerType = AccessTools.TypeByName("DamageWorker_Blunt");

        private static readonly FieldInfo ParentNameField =
            AccessTools.Field(typeof(ThingDef), "parentName") ?? AccessTools.Field(typeof(Def), "parentName");

        private static readonly PropertyInfo ParentNameProperty =
            ParentNameField == null
                ? AccessTools.Property(typeof(ThingDef), "ParentName") ?? AccessTools.Property(typeof(Def), "ParentName")
                : null;

        private static readonly Dictionary<ThingDef, bool> strictAnimalCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<Type, bool> supportedWorkerCache = new Dictionary<Type, bool>();

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableAnimalDamageReduction;
        }

        static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(typeof(Pawn), "TakeDamage", new Type[] { typeof(DamageInfo) });
            if (method == null)
            {
                Log.Error("[ZoologyMod] DamageReduction_AnimalTypes: не найден целевой метод Pawn.TakeDamage(DamageInfo). Патч не будет применён.");
            }

            return method;
        }

        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings == null || !settings.EnableAnimalDamageReduction)
                    return;

                var damageDef = dinfo.Def;
                if (damageDef == null) return;

                Type workerClass = damageDef.workerClass;
                if (!IsSupportedWorkerClass(workerClass)) return;

                Pawn victim = __instance;
                if (victim == null) return;

                Thing instigator = dinfo.Instigator;
                if (instigator == null) return;

                if (!IsThingAnimalStrict(victim)) return;
                if (!IsThingAnimalStrict(instigator)) return;

                Pawn attackerPawn = instigator as Pawn;

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

        private static bool IsSupportedWorkerClass(Type workerClass)
        {
            if (workerClass == null)
            {
                return false;
            }

            if (supportedWorkerCache.TryGetValue(workerClass, out bool cached))
            {
                return cached;
            }

            bool isScratch = ScratchWorkerType != null && (workerClass == ScratchWorkerType || workerClass.IsSubclassOf(ScratchWorkerType));
            bool isBite = BiteWorkerType != null && (workerClass == BiteWorkerType || workerClass.IsSubclassOf(BiteWorkerType));
            bool isBlunt = BluntWorkerType != null && (workerClass == BluntWorkerType || workerClass.IsSubclassOf(BluntWorkerType));

            bool result = isScratch || isBite || isBlunt;
            supportedWorkerCache[workerClass] = result;
            return result;
        }

        private static bool IsThingAnimalStrict(Thing thing)
        {
            return IsThingAnimalStrict(thing?.def);
        }

        private static bool IsThingAnimalStrict(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (strictAnimalCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }

            bool result = false;
            try
            {
                ThingDef current = def;
                while (current != null)
                {
                    if (string.Equals(current.defName, AnimalThingBaseDefName, StringComparison.Ordinal))
                    {
                        result = true;
                        break;
                    }

                    string parentName = GetParentName(current);
                    if (string.IsNullOrEmpty(parentName))
                    {
                        break;
                    }

                    current = DefDatabase<ThingDef>.GetNamedSilentFail(parentName);
                }

                if (!result && def.race != null)
                {
                    string thinkMainName = def.race.thinkTreeMain?.defName;
                    if (!string.IsNullOrEmpty(thinkMainName)
                        && thinkMainName.IndexOf("Animal", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result = true;
                    }
                }
            }
            catch
            {
                result = false;
            }

            strictAnimalCache[def] = result;
            return result;
        }

        private static string GetParentName(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }

            if (ParentNameField != null)
            {
                return ParentNameField.GetValue(def) as string;
            }

            if (ParentNameProperty != null)
            {
                return ParentNameProperty.GetValue(def) as string;
            }

            return null;
        }
    }
}
