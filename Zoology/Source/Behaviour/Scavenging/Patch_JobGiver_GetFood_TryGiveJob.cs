// Patch_JobGiver_GetFood_TryGiveJob.cs

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace ZoologyMod.HarmonyPatches
{
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class Patch_JobGiver_GetFood_TryGiveJob
    {
        // Последний тик, когда конкретному pawn'у уже было назначено задание (чтобы не назначять несколько раз в одном тике)
        private static readonly Dictionary<int, int> lastAssignedTick = new Dictionary<int, int>();

        // Новая таблица: труп.thingIDNumber -> tick, когда он был назначен другому pawn'у.
        // Это уменьшает вероятность одновременного назначения одного трупа нескольким pawns в пределах одного тика.
        private static readonly Dictionary<int, int> corpseAssignedTick = new Dictionary<int, int>();

        static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableScavengering)
                {
                    // если мод-опция выключена — вообще не вмешиваемся, позволяем ванили работать
                    return;
                }
                // если ваниль уже назначила Ingest — не мешаем
                if (__result != null && __result.def == JobDefOf.Ingest) return;
                if (pawn == null) return;

                var scav = pawn.def.GetModExtension<ModExtension_IsScavenger>();
                if (scav == null) return; // только падальщики обрабатываем

                var hunger = pawn.needs?.food;
                if (hunger == null) return;
                if (hunger.CurLevelPercentage > 0.85f) return;

                int pawnId = pawn.thingIDNumber;
                int currentTick = Find.TickManager.TicksGame;
                if (lastAssignedTick.TryGetValue(pawnId, out int lastTick) && lastTick == currentTick) return;

                Predicate<Thing> validator = (Thing t) =>
                {
                    var corpse = t as Corpse;
                    if (corpse == null) return false;

                    try
                    {
                        // Если в этой мапе уже кто-то пометил труп в этом тике — считаем его недоступным.
                        if (corpseAssignedTick.TryGetValue(corpse.thingIDNumber, out int assignedTick) && assignedTick == currentTick)
                        {
                            return false;
                        }

                        if (corpse.InnerPawn == null) return false;
                        if (corpse.Map == null) return false;
                        if (corpse.Bugged) return false;
                        if (!corpse.InnerPawn.RaceProps.IsFlesh) return false;

                        ThingDef finalDef;
                        try { finalDef = FoodUtility.GetFinalIngestibleDef(corpse, false); }
                        catch { return false; }
                        if (finalDef == null) return false;

                        if (!pawn.WillEat(finalDef, pawn, true, false)) return false;
                        if (!finalDef.IsNutritionGivingIngestible) return false;
                        if (corpse.IsForbidden(pawn)) return false;

                        // dessicated проверка
                        if (!scav.allowVeryRotten)
                        {
                            var rotComp = corpse.TryGetComp<CompRottable>();
                            if (rotComp != null)
                            {
                                if (rotComp.Stage == RotStage.Dessicated) return false;
                            }
                            else if (corpse.IsDessicated()) return false;
                        }

                        if (!pawn.Map.reachability.CanReachNonLocal(pawn.Position, new TargetInfo(corpse.PositionHeld, corpse.Map, false),
                            PathEndMode.OnCell, TraverseParms.For(pawn, Danger.Some, TraverseMode.ByPawn, false, false, false, true)))
                        {
                            return false;
                        }

                        // Делать проверку резервации так же, как ваниль для еды:
                        if (!pawn.CanReserveAndReach(corpse, PathEndMode.OnCell, Danger.Some, 1, -1, null, false)) return false;

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[Zoology] ValidatorException in JobGiver_GetFood.TryGiveJob: " + ex);
                        return false;
                    }
                };

                int maxRegionsToScan = GetMaxRegionsToScan_Local(pawn, forceScanWholeMap: false);

                Thing found = GenClosest.ClosestThingReachable(
                    pawn.Position,
                    pawn.Map,
                    ThingRequest.ForGroup(ThingRequestGroup.FoodSource),
                    PathEndMode.OnCell,
                    TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false, false, false, true),
                    9999f,
                    validator,
                    null,
                    0,
                    maxRegionsToScan,
                    false,
                    RegionType.Set_Passable,
                    false,
                    false
                );

                if (found == null) return;

                // Помечаем труп как назначенный в этом тике
                try
                {
                    corpseAssignedTick[found.thingIDNumber] = currentTick;
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] MarkAssignedTickFailed: " + ex);
                }

                // не ставим дубликатную Ingest
                if (pawn.jobs != null)
                {
                    Job cur = pawn.CurJob;
                    if (cur != null && cur.def == JobDefOf.Ingest && cur.targetA.Thing == found) return;

                    var queue = pawn.jobs.jobQueue;
                    if (queue != null)
                    {
                        for (int i = 0; i < queue.Count; i++)
                        {
                            object queuedItem = queue[i];
                            if (queuedItem == null) continue;
                            Job queuedJob = GetJobFromQueuedItem(queuedItem);
                            if (queuedJob != null && queuedJob.def == JobDefOf.Ingest && queuedJob.targetA.Thing == found) return;
                        }
                    }
                }

                ThingDef fd;
                try { fd = FoodUtility.GetFinalIngestibleDef(found, false); }
                catch
                {
                    // silent reject
                    return;
                }
                if (fd == null) return;
                if (!pawn.WillEat(fd, pawn, true, false)) return;

                lastAssignedTick[pawnId] = currentTick;

                // Создаём job и выставляем count по аналогии с ванилью
                Job job = JobMaker.MakeJob(JobDefOf.Ingest, found);
                try
                {
                    float nutrition = FoodUtility.GetNutrition(pawn, found, fd);
                    job.count = FoodUtility.WillIngestStackCountOf(pawn, fd, nutrition);
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] WillIngestStackCountOfFailed: " + ex);
                }

                // Пытаемся явной резервировать цель НЕМЕДЛЕННО через разные механизмы (reflection, fallback'ы).
                bool reserved = false;
                try
                {
                    reserved = AttemptReserveAndLog(pawn, found, job);
                    if (!reserved)
                    {
                        Log.Warning($"[Zoology] ReservationAttemptFailed pawn_id{pawn.thingIDNumber} target_id{found.thingIDNumber}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] AttemptReserveAndLogFailed: " + ex);
                }

                if (!reserved)
                {
                    // не выставляем job, чтобы избежать гонки — попробует позднее ваниль/AI
                    return;
                }

                // Помечаем труп как назначенный в этом тике ТОЛЬКО ПОСЛЕ УСПЕШНОГО RESERVE
                try
                {
                    corpseAssignedTick[found.thingIDNumber] = currentTick;
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] MarkAssignedTickFailed: " + ex);
                }

                // не ставим дубликатную Ingest
                if (pawn.jobs != null)
                {
                    Job cur = pawn.CurJob;
                    if (cur != null && cur.def == JobDefOf.Ingest && cur.targetA.Thing == found) return;

                    var queue = pawn.jobs.jobQueue;
                    if (queue != null)
                    {
                        for (int i = 0; i < queue.Count; i++)
                        {
                            object queuedItem = queue[i];
                            if (queuedItem == null) continue;
                            Job queuedJob = GetJobFromQueuedItem(queuedItem);
                            if (queuedJob != null && queuedJob.def == JobDefOf.Ingest && queuedJob.targetA.Thing == found) return;
                        }
                    }
                }

                // защитные проверки перед выдачей job
                if (found == null || !found.Spawned || found.Destroyed) return;
                if (!pawn.CanReserveAndReach(found, PathEndMode.OnCell, Danger.Some, 1, -1, null, false))
                {
                    Log.Warning($"[Zoology] Final CanReserveAndReach failed for pawn_id{pawn.thingIDNumber} target_id{found.thingIDNumber} - aborting job assignment");
                    // попытка убрать некорректные резервации
                    TryReleaseReservationsForTarget(found);
                    return;
                }

                // Записываем lastAssignedTick и возвращаем job
                lastAssignedTick[pawnId] = currentTick;
                __result = job;

                // --- очистка старых записей в corpseAssignedTick, чтобы словарь не разрастался
                try
                {
                    if (corpseAssignedTick.Count > 1000)
                    {
                        var keys = new List<int>(corpseAssignedTick.Keys);
                        foreach (var k in keys)
                        {
                            if (corpseAssignedTick.TryGetValue(k, out int t) && t < currentTick - 1)
                                corpseAssignedTick.Remove(k);
                        }
                    }
                    else
                    {
                        var toRemove = new List<int>();
                        foreach (var kv in corpseAssignedTick)
                        {
                            if (kv.Value < currentTick - 1) toRemove.Add(kv.Key);
                        }
                        foreach (var k in toRemove) corpseAssignedTick.Remove(k);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] CorpseAssignedTickCleanupFailed: " + ex);
                }
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in JobGiver_GetFood.TryGiveJob postfix: " + e);
            }
        }

        // ------------------ Helpers ------------------

        private static bool AttemptReserveAndLog(Pawn pawn, Thing target, Job job)
        {
            try
            {
                // 1) Попытаться pawn.Reserve(...) через reflection (много перегрузок)
                try
                {
                    bool ok = TryReserveViaPawnMethod(pawn, target, job);
                    if (ok)
                    {
                        Pawn current = QueryCurrentReserverPawn(target);
                        if (current == pawn)
                        {
                            return true;
                        }
                        else
                        {
                            // попытка очистить некорректные резервации (фоллбэк)
                            TryReleaseReservationsForTarget(target);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] TryReserveViaPawnMethod threw: " + ex);
                }

                // 2) Попытаться Find.Reservations.Reserve(...) через reflection
                try
                {
                    bool ok = TryReserveViaFindReservations(pawn, target, job);
                    if (ok)
                    {
                        Pawn current = QueryCurrentReserverPawn(target);
                        if (current == pawn)
                        {
                            return true;
                        }
                        else
                        {
                            TryReleaseReservationsForTarget(target);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] TryReserveViaFindReservations threw: " + ex);
                }

                // 3) ничего не получилось — silent final check
                try
                {
                    Pawn cur = QueryCurrentReserverPawn(target);
                    if (cur != null)
                    {
                        // ничего (оставляем без спама)
                    }
                    else
                    {
                        // ничего
                    }
                }
                catch { }

            }
            catch (Exception ex)
            {
                Log.Error("[Zoology] AttemptReserveAndLog failed: " + ex);
            }
            return false;
        }

        /// <summary>
        /// Возвращает pawn, который сейчас резервирует target (или null). Использует reflection — аналог LogCurrentReserverIfAny, но возвращает Pawn.
        /// </summary>
        private static Pawn QueryCurrentReserverPawn(Thing target)
        {
            try
            {
                var findType = typeof(Find);
                var prop = findType.GetProperty("Reservations", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null) return null;
                var reservationsMgr = prop.GetValue(null);
                if (reservationsMgr == null) return null;

                var resMgrType = reservationsMgr.GetType();
                MethodInfo firstReserverMethod = null;
                foreach (var m in resMgrType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!(m.ReturnType == typeof(Pawn) || m.ReturnType == typeof(object))) continue;
                    var pars = m.GetParameters();
                    if (pars.Length != 1) continue;
                    var pType = pars[0].ParameterType;
                    if (pType == typeof(Thing) || pType.Name.Contains("LocalTargetInfo") || pType.Name.Contains("TargetInfo"))
                    {
                        string name = m.Name.ToLowerInvariant();
                        if (name.Contains("firstreserver") || name.Contains("firstreserverof") || name.Contains("reserver"))
                        {
                            firstReserverMethod = m;
                            break;
                        }
                        if (firstReserverMethod == null) firstReserverMethod = m;
                    }
                }
                if (firstReserverMethod == null) return null;

                object arg = target;
                var paramType = firstReserverMethod.GetParameters()[0].ParameterType;
                if (paramType != typeof(Thing) && paramType.Name.Contains("LocalTargetInfo"))
                {
                    var ctor = paramType.GetConstructor(new Type[] { typeof(Thing) });
                    if (ctor != null) arg = ctor.Invoke(new object[] { target });
                }

                var reserverObj = firstReserverMethod.Invoke(reservationsMgr, new object[] { arg });
                return reserverObj as Pawn;
            }
            catch (Exception ex)
            {
                Log.Error("[Zoology] QueryCurrentReserverPawn failed: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Попытка через reflection вызвать ReleaseAllForTarget / Release(...) для данного target (фоллбэк/ремонт).
        /// </summary>
        private static void TryReleaseReservationsForTarget(Thing target)
        {
            try
            {
                var findType = typeof(Find);
                var prop = findType.GetProperty("Reservations", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null) return;
                var reservationsMgr = prop.GetValue(null);
                if (reservationsMgr == null) return;

                var methods = reservationsMgr.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var m in methods)
                {
                    string name = m.Name.ToLowerInvariant();
                    if (!name.Contains("release") && !name.Contains("remove")) continue;
                    var pars = m.GetParameters();
                    // ищем метод (Thing/LocalTargetInfo) или (Pawn, Thing/LocalTargetInfo) или (LocalTargetInfo)
                    if (pars.Length == 1)
                    {
                        var pType = pars[0].ParameterType;
                        object arg = target;
                        if (pType != typeof(Thing) && pType.Name.Contains("LocalTargetInfo"))
                        {
                            var ctor = pType.GetConstructor(new Type[] { typeof(Thing) });
                            if (ctor != null) arg = ctor.Invoke(new object[] { target });
                        }
                        try { m.Invoke(reservationsMgr, new object[] { arg }); }
                        catch (Exception ex) { Log.Error("[Zoology] TryReleaseReservationsForTarget single-param invoke failed: " + ex); }
                    }
                    else if (pars.Length == 2)
                    {
                        // try (Pawn, LocalTargetInfo) or (Pawn, Thing) -> pass null for pawn to release all
                        object arg0 = null;
                        object arg1 = target;
                        var pType = pars[1].ParameterType;
                        if (pType != typeof(Thing) && pType.Name.Contains("LocalTargetInfo"))
                        {
                            var ctor = pType.GetConstructor(new Type[] { typeof(Thing) });
                            if (ctor != null) arg1 = ctor.Invoke(new object[] { target });
                        }
                        try { m.Invoke(reservationsMgr, new object[] { arg0, arg1 }); }
                        catch (Exception ex) { Log.Error("[Zoology] TryReleaseReservationsForTarget two-param invoke failed: " + ex); }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Zoology] TryReleaseReservationsForTarget failed: " + ex);
            }
        }

        private static object MakeArgForParam(Type paramType, Thing target, Pawn pawn, Job job)
        {
            if (paramType == typeof(Pawn)) return pawn;
            if (paramType == typeof(Job)) return job;
            if (paramType == typeof(Thing)) return target;
            if (paramType == typeof(int)) return 1;
            if (paramType == typeof(float)) return 1f;
            // попытка создать LocalTargetInfo/TargetInfo если требуется
            string name = paramType.Name;
            if (name.Contains("LocalTargetInfo") || name.Contains("TargetInfo"))
            {
                // конструктор (Thing)
                var ctor1 = paramType.GetConstructor(new Type[] { typeof(Thing) });
                if (ctor1 != null)
                    return ctor1.Invoke(new object[] { target });
                // конструктор (IntVec3, Map)
                var ctor2 = paramType.GetConstructor(new Type[] { typeof(IntVec3), typeof(Map) });
                if (ctor2 != null)
                    return ctor2.Invoke(new object[] { target.PositionHeld, target.Map });
                // fallback — возвращаем Thing
                return target;
            }
            // если ожидается object или неизвестный — передаём null
            if (paramType == typeof(object)) return null;
            return null;
        }

        private static bool TryReserveViaPawnMethod(Pawn pawn, Thing target, Job job)
        {
            var methods = pawn.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var m in methods)
            {
                if (m.Name != "Reserve") continue;
                var pars = m.GetParameters();
                object[] args = new object[pars.Length];

                try
                {
                    for (int i = 0; i < pars.Length; i++)
                    {
                        args[i] = MakeArgForParam(pars[i].ParameterType, target, pawn, job);
                    }
                    m.Invoke(pawn, args);
                    // попытка убедиться, что резервация прошла: позже вы логируете текущего резерватора
                    return true;
                }
                catch
                {
                    continue;
                }
            }
            return false;
        }

        private static bool TryReserveViaFindReservations(Pawn pawn, Thing target, Job job)
        {
            try
            {
                var findType = typeof(Find);
                var prop = findType.GetProperty("Reservations", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null) return false;
                var reservationsMgr = prop.GetValue(null);
                if (reservationsMgr == null) return false;

                var methods = reservationsMgr.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var m in methods)
                {
                    if (!m.Name.ToLowerInvariant().Contains("reserve")) continue;
                    var pars = m.GetParameters();
                    object[] args = new object[pars.Length];

                    try
                    {
                        for (int i = 0; i < pars.Length; i++)
                        {
                            args[i] = MakeArgForParam(pars[i].ParameterType, target, pawn, job);
                        }

                        m.Invoke(reservationsMgr, args);
                        return true;
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Zoology] TryReserveViaFindReservations top-level error: " + ex);
            }
            return false;
        }

        private static Job GetJobFromQueuedItem(object queuedItem)
        {
            if (queuedItem == null) return null;
            Type t = queuedItem.GetType();

            var p = t.GetProperty("job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? t.GetProperty("Job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(queuedItem) as Job;

            var f = t.GetField("job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? t.GetField("Job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(queuedItem) as Job;

            return null;
        }

        private static int GetMaxRegionsToScan_Local(Pawn getter, bool forceScanWholeMap)
        {
            if (getter.RaceProps.Humanlike) return -1;
            if (forceScanWholeMap) return -1;
            if (getter.Faction == Faction.OfPlayer)
            {
                if (getter.Roamer && AnimalPenUtility.GetFixedAnimalFilter().Allows(getter))
                {
                    CompAnimalPenMarker currentPenOf = AnimalPenUtility.GetCurrentPenOf(getter, false);
                    if (currentPenOf != null)
                        return Mathf.Min(currentPenOf.PenState.ConnectedRegions.Count, 100);
                }
                return 100;
            }
            return 30;
        }
    }
}