// Patch_PawnUtility_IsFighting.cs

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [HarmonyPatch(typeof(Faction), "Notify_MemberTookDamage")]
    public static class Patch_Faction_Notify_MemberTookDamage
    {
        static void Postfix(Faction __instance, Pawn member, DamageInfo dinfo)
        {
            try
            {
                // DamageInfo — struct, поэтому dinfo не может быть null. Проверяем Instigator.
                if (dinfo.Instigator == null) return;
                var pawn = dinfo.Instigator as Pawn;
                if (pawn == null) return;

                bool alreadyHandled = false;
                try
                {
                    // если ваниль уже обработал случай с PredatorHunt, он бы вызвал TookDamageFromPredator earlier;
                    // но чтобы не дублировать — проверим curJob.def
                    if (pawn.CurJob?.def == JobDefOf.PredatorHunt)
                        alreadyHandled = true;
                }
                catch { alreadyHandled = false; }

                if (alreadyHandled) return;

                // Если instigator выполняет ProtectPrey — вызвать TookDamageFromPredator
                var driver = pawn.jobs?.curDriver;
                bool isProtectPrey = false;
                if (driver != null)
                {
                    var t = driver.GetType();
                    if (t.Name == "JobDriver_ProtectPrey" || t.FullName?.EndsWith(".JobDriver_ProtectPrey") == true
                        || typeof(JobDriver_ProtectPrey).IsAssignableFrom(t))
                    {
                        isProtectPrey = true;
                    }
                }
                if (!isProtectPrey)
                {
                    if (!string.IsNullOrEmpty(pawn.CurJob?.def?.defName) && pawn.CurJob.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                        isProtectPrey = true;
                }

                if (isProtectPrey)
                {
                    // вызываем приватный метод TookDamageFromPredator через reflection
                    try
                    {
                        MethodInfo mi = AccessTools.Method(typeof(Faction), "TookDamageFromPredator", new Type[] { typeof(Pawn) });
                        if (mi == null) // попытка без сигнатуры, на случай разных сборок/обфускации
                            mi = AccessTools.Method(typeof(Faction), "TookDamageFromPredator");

                        if (mi != null)
                        {
                            mi.Invoke(__instance, new object[] { pawn });
                        }
                        else
                        {
                            Log.Warning("[ZoologyMod] TookDamageFromPredator method not found via reflection — predatorThreats не обновлён.");
                        }
                    }
                    catch (Exception ex2)
                    {
                        Log.Error($"[ZoologyMod] Error calling TookDamageFromPredator for ProtectPrey instigator {pawn}: {ex2}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_Faction_Notify_MemberTookDamage Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(GenHostility), "GetPreyOfMyFaction", new Type[] { typeof(Pawn), typeof(Faction) })]
    public static class Patch_GenHostility_GetPreyOfMyFaction
    {
        static void Postfix(Pawn predator, Faction myFaction, ref Pawn __result)
        {
            try
            {
                if (__result != null) return;
                if (predator == null) return;
                var curJob = predator.CurJob;
                if (curJob == null) return;

                // Если ванильная проверка не сработала, но драйвер — наш ProtectPrey, вернуть target
                var driver = predator.jobs?.curDriver;
                bool isProtectPrey = false;
                if (driver != null)
                {
                    var t = driver.GetType();
                    if (t.Name == "JobDriver_ProtectPrey" || t.FullName?.EndsWith(".JobDriver_ProtectPrey") == true
                        || typeof(JobDriver_ProtectPrey).IsAssignableFrom(t))
                    {
                        // также убедимся, что драйвер не ended (как у ванили)
                        var endedProp = t.GetProperty("ended");
                        bool ended = false;
                        try { if (endedProp != null) ended = (bool)endedProp.GetValue(driver); } catch { ended = false; }
                        if (!ended) isProtectPrey = true;
                    }
                }

                if (!isProtectPrey)
                {
                    // fallback по defName (на случай, если job def называется по-другому)
                    if (!string.IsNullOrEmpty(curJob.def?.defName) && curJob.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                    {
                        isProtectPrey = true;
                    }
                }

                if (!isProtectPrey) return;

                // Получаем цель (TargetIndex.A для вашего драйвера)
                try
                {
                    var t = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                    if (t != null && !t.Dead && t.Faction == myFaction)
                    {
                        __result = t;
                    }
                }
                catch { /* безопасно игнорируем */ }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_GenHostility_GetPreyOfMyFaction Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_ReactToCloseMeleeThreat), "IsHunting", new Type[] { typeof(Pawn), typeof(Pawn) })]
    public static class Patch_ReactToCloseMeleeThreat_IsHunting
    {
        // Важно: второй параметр должен называться 'prey' (именно так в ванильном методе)
        static void Postfix(Pawn pawn, Pawn prey, ref bool __result)
        {
            try
            {
                if (__result) return; // уже true — ничего не делаем
                if (pawn?.jobs?.curDriver == null) return;

                var driver = pawn.jobs.curDriver;
                var driverType = driver.GetType();

                // Попытка найти тип по имени — безопаснее, чем typeof(...) если тип из другой сборки
                Type protectPreyType = Type.GetType("YourModNamespace.JobDriver_ProtectPrey, YourModAssemblyName");
                // Если точное получение не сработало, попробуем обнаружить по имени/FullName
                if (protectPreyType == null)
                {
                    // сравниваем по имени и по окончанию FullName (на случай пространств имён)
                    if (driverType.Name.Equals("JobDriver_ProtectPrey", StringComparison.Ordinal)
                        || (driverType.FullName != null && driverType.FullName.EndsWith(".JobDriver_ProtectPrey", StringComparison.Ordinal)))
                    {
                        __result = true;
                        return;
                    }
                }
                else
                {
                    // если нашли Type, можно безопасно проверить наследование
                    if (protectPreyType.IsAssignableFrom(driverType))
                    {
                        __result = true;
                        return;
                    }
                }

                // резервный вариант — сравнение дефина работы (на случай, если JobDriver нестандартный)
                var defName = pawn.CurJob?.def?.defName;
                if (!string.IsNullOrEmpty(defName) && defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                {
                    __result = true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_ReactToCloseMeleeThreat_IsHunting Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PawnUtility), "IsFighting", new Type[] { typeof(Pawn) })]
    public static class Patch_PawnUtility_IsFighting
    {
        static void Postfix(Pawn pawn, ref bool __result)
        {
            try
            {
                if (__result) return; // уже true — ничего не делаем
                if (pawn == null || pawn.CurJob == null) return;

                var curJob = pawn.CurJob;
                var driver = pawn.jobs?.curDriver;

                // helper: проверяет, является ли текущий драйвер нашим ProtectPrey (с безопасной детекцией)
                bool DriverIsProtectPrey(Type driverType)
                {
                    if (driverType == null) return false;
                    if (driverType.Name == "JobDriver_ProtectPrey") return true;
                    if (driverType.FullName != null && driverType.FullName.EndsWith(".JobDriver_ProtectPrey")) return true;
                    try
                    {
                        // безопасная проверка наследования (если тип доступен)
                        var protectType = typeof(JobDriver_ProtectPrey);
                        if (protectType.IsAssignableFrom(driverType)) return true;
                    }
                    catch { }
                    return false;
                }

                // Если у pawn стоит ProtectPrey-драйвер — считаем его "воюющим" только когда это локальная угроза:
                if (driver != null)
                {
                    var dType = driver.GetType();
                    if (DriverIsProtectPrey(dType))
                    {
                        try
                        {
                            // Получим целевой Pawn (TargetIndex.A) из curJob и применим фильтры
                            LocalTargetInfo targInfo = curJob.GetTarget(TargetIndex.A);
                            var targetPawn = targInfo.Thing as Pawn;
                            if (targetPawn != null && targetPawn.Spawned)
                            {
                                // Ограничение дистанции: считаем угрозой только если хищник и цель находятся рядом друг с другом.
                                // Подбирайте радиус по балансу; 20 тайлов — разумный компромисс.
                                const float THREAT_RADIUS = 20f;
                                if (pawn.Position.InHorDistOf(targetPawn.Position, THREAT_RADIUS))
                                {
                                    __result = true;
                                }
                            }
                        }
                        catch
                        {
                            // в случае каких-то ошибок — не ставим true, чтобы не вызывать false-положительные flee
                        }
                        return;
                    }
                }

                // Fallback: если job.def называется нашим ProtectPrey — применим те же дополнительные фильтры
                var defName = curJob.def?.defName;
                if (!string.IsNullOrEmpty(defName) && defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        LocalTargetInfo targInfo = curJob.GetTarget(TargetIndex.A);
                        var targetPawn = targInfo.Thing as Pawn;
                        if (targetPawn != null && targetPawn.Spawned)
                        {
                            const float THREAT_RADIUS = 20f;
                            if (pawn.Position.InHorDistOf(targetPawn.Position, THREAT_RADIUS))
                            {
                                __result = true;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_PawnUtility_IsFighting Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Faction), "HasPredatorRecentlyAttackedAnyone", new Type[] { typeof(Pawn) })]
    public static class Patch_Faction_HasPredatorRecentlyAttackedAnyone
    {
        static void Postfix(Faction __instance, Pawn predator, ref bool __result)
        {
            try
            {
                if (__result) return; // ваниль уже считает, что predator недавно нападал

                if (predator == null || __instance == null) return;

                // безопасная детекция нашего драйвера ProtectPrey
                var driver = predator.jobs?.curDriver;
                bool isProtectPrey = false;
                if (driver != null)
                {
                    var t = driver.GetType();
                    if (t.Name == "JobDriver_ProtectPrey" || (t.FullName != null && t.FullName.EndsWith(".JobDriver_ProtectPrey")))
                        isProtectPrey = true;
                }

                // fallback по defName — пригодится, если JobDriver находится в другой сборке/обфускован
                if (!isProtectPrey)
                {
                    try
                    {
                        var cur = predator.CurJob;
                        if (cur != null && !string.IsNullOrEmpty(cur.def?.defName)
                            && cur.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                        {
                            isProtectPrey = true;
                        }
                    }
                    catch { /* ignore */ }
                }

                if (!isProtectPrey) return;

                // Если ProtectPrey — считаем, что он "недавно нападал" на эту фракцию,
                // но только если цель действительно принадлежит этой фракции (чтобы не давать ложные alarms)
                try
                {
                    var curJob = predator.CurJob;
                    if (curJob == null) return;

                    var targ = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                    if (targ != null && !targ.Dead && targ.Faction == __instance)
                    {
                        __result = true;
                    }
                }
                catch
                {
                    // не ломаем ничего при ошибках чтения таргета
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_Faction_HasPredatorRecentlyAttackedAnyone Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(GenHostility), "IsPredatorHostileTo", new Type[] { typeof(Pawn), typeof(Faction) })]
    public static class Patch_GenHostility_IsPredatorHostileTo
    {
        static void Postfix(Pawn predator, Faction toFaction, ref bool __result)
        {
            try
            {
                if (__result) return; // уже признано угрозой ванилью

                if (predator == null || toFaction == null) return;

                var curJob = predator.CurJob;
                if (curJob == null) return;

                // безопасная детекция нашего драйвера ProtectPrey
                var driver = predator.jobs?.curDriver;
                bool isProtectPrey = false;
                if (driver != null)
                {
                    var t = driver.GetType();
                    if (t.Name == "JobDriver_ProtectPrey" || (t.FullName != null && t.FullName.EndsWith(".JobDriver_ProtectPrey")))
                        isProtectPrey = true;
                }
                if (!isProtectPrey)
                {
                    if (!string.IsNullOrEmpty(curJob.def?.defName) && curJob.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                        isProtectPrey = true;
                }

                if (!isProtectPrey) return;

                // Если ProtectPrey — считаем угрозой для этой фракции, если цель действительно принадлежит ей
                try
                {
                    var targ = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                    if (targ != null && !targ.Dead && targ.Faction == toFaction)
                    {
                        __result = true;
                    }
                }
                catch { /* игнорируем ошибки чтения таргета */ }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_GenHostility_IsPredatorHostileTo Postfix error: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(AttackTargetsCache), "GetPotentialTargetsFor", new Type[] { typeof(IAttackTargetSearcher) })]
    public static class Patch_AttackTargetsCache_GetPotentialTargetsFor
    {
        // Важно: имя параметра должно совпадать с оригинальным — "th"
        static void Postfix(AttackTargetsCache __instance, IAttackTargetSearcher th, ref List<IAttackTarget> __result)
        {
            try
            {
                if (th == null) return;
                if (__result == null) __result = new List<IAttackTarget>();

                // Попробуем получить Pawn-ищущий (обычно th — Pawn)
                Pawn searcherPawn = null;
                try
                {
                    searcherPawn = th as Pawn;
                    if (searcherPawn == null)
                    {
                        var thingProp = th.GetType().GetProperty("Thing");
                        if (thingProp != null)
                        {
                            var thingVal = thingProp.GetValue(th, null) as Thing;
                            searcherPawn = thingVal as Pawn;
                        }
                    }
                }
                catch { /* ignore */ }

                if (searcherPawn == null || searcherPawn.Map == null) return;

                const float ADD_RADIUS = 30f;
                var pawns = searcherPawn.Map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (p == null) continue;
                    if (!p.RaceProps.predator) continue;
                    if (!p.Spawned) continue;
                    if (!p.Position.InHorDistOf(searcherPawn.Position, ADD_RADIUS)) continue;

                    bool already = false;
                    for (int j = 0; j < __result.Count; j++)
                    {
                        if (__result[j]?.Thing == p) { already = true; break; }
                    }
                    if (already) continue;

                    bool isProtectPrey = false;
                    try
                    {
                        var driver = p.jobs?.curDriver;
                        if (driver != null)
                        {
                            var dt = driver.GetType();
                            if (dt.Name == "JobDriver_ProtectPrey" || (dt.FullName != null && dt.FullName.EndsWith(".JobDriver_ProtectPrey")))
                                isProtectPrey = true;
                            else
                            {
                                try { if (typeof(JobDriver_ProtectPrey).IsAssignableFrom(dt)) isProtectPrey = true; } catch { }
                            }
                        }

                        if (!isProtectPrey)
                        {
                            var cur = p.CurJob;
                            if (cur != null && !string.IsNullOrEmpty(cur.def?.defName)
                                && cur.def.defName.Equals("Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                            {
                                isProtectPrey = true;
                            }
                        }
                    }
                    catch { }

                    if (!isProtectPrey) continue;

                    Pawn protectedPawn = null;
                    try
                    {
                        var curJob = p.CurJob;
                        if (curJob != null)
                        {
                            var targ = curJob.GetTarget(TargetIndex.A).Thing as Pawn;
                            if (targ != null && !targ.Dead) protectedPawn = targ;
                        }
                    }
                    catch { }

                    if (protectedPawn == null) continue;

                    if (searcherPawn.Faction != null && protectedPawn.Faction == searcherPawn.Faction)
                    {
                        __result.Add(p as IAttackTarget);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[ZoologyMod] Patch_AttackTargetsCache_GetPotentialTargetsFor Postfix error: {ex}");
            }
        }
    }
}
