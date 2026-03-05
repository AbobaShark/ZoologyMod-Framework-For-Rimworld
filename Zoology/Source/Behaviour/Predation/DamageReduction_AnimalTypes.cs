// DamageReduction_AnimalTypes.cs

using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using RimWorld;

namespace ZoologyMod.Patches
{
    [HarmonyPatch]
    public static class DamageReduction_AnimalTypes_PawnTakeDamage
    {
        private static readonly Type ScratchWorkerType = AccessTools.TypeByName("DamageWorker_Scratch");
        private static readonly Type BiteWorkerType = AccessTools.TypeByName("DamageWorker_Bite");
        private static readonly Type BluntWorkerType = AccessTools.TypeByName("DamageWorker_Blunt");

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
                if (ZoologyModSettings.Instance == null || !ZoologyModSettings.Instance.EnableAnimalDamageReduction)
                    return;

                if (dinfo.Def == null) return;

                var workerClass = dinfo.Def.workerClass;
                bool isScratch = ScratchWorkerType != null && (workerClass == ScratchWorkerType || workerClass.IsSubclassOf(ScratchWorkerType));
                bool isBite = BiteWorkerType != null && (workerClass == BiteWorkerType || workerClass.IsSubclassOf(BiteWorkerType));
                bool isBlunt = BluntWorkerType != null && (workerClass == BluntWorkerType || workerClass.IsSubclassOf(BluntWorkerType));
                if (!isScratch && !isBite && !isBlunt) return;

                Pawn victim = __instance;
                if (victim == null) return;

                Thing instigator = dinfo.Instigator;
                if (instigator == null) return; // требуем атакующего

                // ---- новая, более надёжная проверка животного по родителю с диагностикой ----
                bool IsThingAnimalStrictByParent(Thing t, out string diagnostic)
                {
                    diagnostic = "";
                    if (t == null || t.def == null)
                    {
                        diagnostic = "null/undef";
                        return false;
                    }

                    try
                    {
                        // 1) Пройти цепочку ParentName через DefDatabase (если возможно)
                        ThingDef current = t.def;
                        string chain = current.defName ?? "(no defName)";
                        while (current != null)
                        {
                            if (!string.IsNullOrEmpty(current.defName) && current.defName == "AnimalThingBase")
                            {
                                diagnostic = "found_by_parent_chain: " + chain;
                                return true;
                            }

                            // Попытка получить поле parentName или свойство ParentName (через рефлексию)
                            string parentName = null;
                            var field = typeof(ThingDef).GetField("parentName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (field != null)
                                parentName = field.GetValue(current) as string;
                            else
                            {
                                var prop = typeof(ThingDef).GetProperty("ParentName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (prop != null)
                                    parentName = prop.GetValue(current) as string;
                            }

                            if (string.IsNullOrEmpty(parentName))
                            {
                                // не удалось получить parentName — прервём попытку и перейдём к fallback
                                chain += " -> (no parentName)";
                                break;
                            }

                            chain += " -> " + parentName;
                            var parentDef = DefDatabase<ThingDef>.GetNamedSilentFail(parentName);
                            if (parentDef == null)
                            {
                                chain += " (parent not found)";
                                break;
                            }

                            current = parentDef;
                        }

                        // 2) Если цепочка parentName не дала result, делаем осторожный fallback:
                        //    считаем "животным" если у def.race есть thinkTreeMain с "Animal" в имени
                        if (t.def.race != null)
                        {
                            var thinkMainName = t.def.race.thinkTreeMain?.defName;
                            if (!string.IsNullOrEmpty(thinkMainName)
                                && thinkMainName.IndexOf("Animal", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                diagnostic = "found_by_race_thinkTreeMain: " + thinkMainName + " (chain: " + chain + ")";
                                return true;
                            }
                        }

                        // Никто не подтвердил как Animal
                        diagnostic = "not_animal (chain: " + chain + (t.def.race != null ? ", race.thinkTreeMain=" + (t.def.race.thinkTreeMain?.defName ?? "(null)") : ", race=null") + ")";
                        return false;
                    }
                    catch (Exception ex)
                    {
                        diagnostic = "exception: " + ex.Message;
                        return false;
                    }
                }
                // ---- конец проверки ----

                // Выполняем проверку для victim и instigator, и собираем диагностический текст если что-то не так
                if (!IsThingAnimalStrictByParent(victim, out string diagVictim))
                {
                    return;
                }

                if (!IsThingAnimalStrictByParent(instigator, out string diagInst))
                {
                    return;
                }

                // далее — прежняя логика расчёта размера/предаторности/факторов
                Pawn attackerPawn = instigator as Pawn;

                bool victimIsPredator = victim.RaceProps?.predator ?? false;
                bool attackerIsPredator = false;

                float victimBodySizeActual = victim.BodySize;
                float attackerBodySizeActual = 1f;

                float victimBaseSize = 1f;
                float attackerBaseSize = 1f;

                if (victim.def != null && victim.def.race != null)
                    victimBaseSize = victim.def.race.baseBodySize;

                if (attackerPawn != null)
                {
                    attackerBodySizeActual = attackerPawn.BodySize;
                    attackerIsPredator = attackerPawn.RaceProps?.predator ?? false;
                    if (attackerPawn.def != null && attackerPawn.def.race != null)
                        attackerBaseSize = attackerPawn.def.race.baseBodySize;
                }
                else
                {
                    var idef = instigator.def;
                    if (idef != null)
                    {
                        attackerIsPredator = idef.race?.predator ?? false;
                        attackerBaseSize = idef.race?.baseBodySize ?? 1f;
                        attackerBodySizeActual = idef.race?.baseBodySize ?? 1f;
                    }
                }

                float beforeAmount;
                try { beforeAmount = dinfo.Amount; } catch { beforeAmount = float.NaN; }

                bool sizeThreshold = victimBodySizeActual >= 1.2f * attackerBodySizeActual;
                float factor = 1f;
                bool applied = false;

                if (!attackerIsPredator && victimIsPredator)
                {
                    if (attackerBaseSize >= victimBaseSize * 2f)
                    {
                        applied = false;
                    }
                    else
                    {
                        if (attackerBaseSize > victimBaseSize)
                        {
                            factor = 0.5f;
                            applied = true;
                        }
                        else
                        {
                            if (victimBaseSize > 0f)
                            {
                                factor = attackerBaseSize / victimBaseSize;
                                applied = true;
                            }
                        }
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
                else
                {
                    applied = false;
                }

                if (!applied) return;

                if (factor > 1f) factor = 1f;
                if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f) return;

                float newAmount = beforeAmount * factor;

                DamageInfo newDinfo;
                try
                {
                    newDinfo = new DamageInfo(
                        dinfo.Def,
                        newAmount,
                        dinfo.ArmorPenetrationInt,
                        dinfo.Angle,
                        dinfo.Instigator,
                        dinfo.HitPart,
                        dinfo.Weapon
                    );
                }
                catch (Exception)
                {
                    try
                    {
                        newDinfo = (DamageInfo)Activator.CreateInstance(
                            typeof(DamageInfo),
                            new object[] {
                                dinfo.Def,
                                newAmount,
                                dinfo.ArmorPenetrationInt,
                                dinfo.Instigator,
                                dinfo.HitPart,
                                dinfo.Weapon
                            }
                        );
                    }
                    catch
                    {
                        try
                        {
                            dinfo.SetAmount(newAmount);
                            newDinfo = dinfo;
                        }
                        catch
                        {
                            Log.Warning("[ZoologyMod] AnimalDamageReduction: не удалось создать/изменить DamageInfo для редукции урона. Отказываемся от редукции в этом случае.");
                            return;
                        }
                    }
                }

                dinfo = newDinfo;

                string attackerLabel = instigator != null ? instigator.LabelShort : "(null instigator)";
                string victimLabel = victim != null ? victim.LabelShort : "(null victim)";
                string workerType = workerClass != null ? workerClass.Name : "(unknown worker)";
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Exception in DamageReduction_AnimalTypes_Prefix: {ex}");
            }
        }
    }
}