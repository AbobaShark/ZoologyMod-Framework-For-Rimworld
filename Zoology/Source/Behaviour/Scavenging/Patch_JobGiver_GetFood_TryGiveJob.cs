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
        private static readonly ThingRequest FoodSourceRequest = ThingRequest.ForGroup(ThingRequestGroup.FoodSource);

        static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableScavengering;
        }

        
        private static readonly Dictionary<int, int> lastAssignedTick = new Dictionary<int, int>();

        
        
        private static readonly Dictionary<int, int> corpseAssignedTick = new Dictionary<int, int>();
        private static readonly List<int> corpseCleanupKeys = new List<int>(256);
        private static int lastCorpseCleanupTick = -1;
        private const int CorpseCleanupIntervalTicks = 60;

        private static readonly PropertyInfo FindReservationsProperty =
            typeof(Find).GetProperty("Reservations", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly Dictionary<Type, MethodInfo[]> reserveMethodsByPawnType = new Dictionary<Type, MethodInfo[]>();
        private static readonly Dictionary<Type, MethodInfo[]> reserveMethodsByReservationsType = new Dictionary<Type, MethodInfo[]>();
        private static readonly Dictionary<Type, MethodInfo[]> releaseMethodsByReservationsType = new Dictionary<Type, MethodInfo[]>();
        private static readonly Dictionary<Type, MethodInfo> firstReserverMethodByReservationsType = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, ConstructorInfo> localTargetCtorByType = new Dictionary<Type, ConstructorInfo>();
        private static readonly Dictionary<Type, ConstructorInfo> targetInfoCtorByType = new Dictionary<Type, ConstructorInfo>();
        private static readonly Dictionary<Type, Func<object, Job>> queuedJobGetterByType = new Dictionary<Type, Func<object, Job>>();

        static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableScavengering)
                {
                    
                    return;
                }
                
                if (__result != null && __result.def == JobDefOf.Ingest) return;
                if (pawn == null) return;

                var scav = DefModExtensionCache<ModExtension_IsScavenger>.Get(pawn.def);
                if (scav == null) return; 

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
                    FoodSourceRequest,
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

                
                try
                {
                    corpseAssignedTick[found.thingIDNumber] = currentTick;
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] MarkAssignedTickFailed: " + ex);
                }

                
                if (HasIngestJobForTarget(pawn, found)) return;

                ThingDef fd;
                try { fd = FoodUtility.GetFinalIngestibleDef(found, false); }
                catch
                {
                    
                    return;
                }
                if (fd == null) return;
                if (!pawn.WillEat(fd, pawn, true, false)) return;

                lastAssignedTick[pawnId] = currentTick;

                
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
                    
                    return;
                }

                
                try
                {
                    corpseAssignedTick[found.thingIDNumber] = currentTick;
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] MarkAssignedTickFailed: " + ex);
                }

                
                if (HasIngestJobForTarget(pawn, found)) return;

                
                if (found == null || !found.Spawned || found.Destroyed) return;
                if (!pawn.CanReserveAndReach(found, PathEndMode.OnCell, Danger.Some, 1, -1, null, false))
                {
                    Log.Warning($"[Zoology] Final CanReserveAndReach failed for pawn_id{pawn.thingIDNumber} target_id{found.thingIDNumber} - aborting job assignment");
                    
                    TryReleaseReservationsForTarget(found);
                    return;
                }

                
                lastAssignedTick[pawnId] = currentTick;
                __result = job;

                
                CleanupCorpseAssignedTick(currentTick);
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in JobGiver_GetFood.TryGiveJob postfix: " + e);
            }
        }

        

        private static bool HasIngestJobForTarget(Pawn pawn, Thing target)
        {
            try
            {
                if (pawn?.jobs == null || target == null) return false;

                Job cur = pawn.CurJob;
                if (cur != null && cur.def == JobDefOf.Ingest && cur.targetA.Thing == target)
                    return true;

                var queue = pawn.jobs.jobQueue;
                if (queue == null) return false;

                for (int i = 0; i < queue.Count; i++)
                {
                    object queuedItem = queue[i];
                    if (queuedItem == null) continue;
                    Job queuedJob = GetJobFromQueuedItem(queuedItem);
                    if (queuedJob != null && queuedJob.def == JobDefOf.Ingest && queuedJob.targetA.Thing == target)
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Zoology] HasIngestJobForTarget failed: " + ex);
            }
            return false;
        }

        private static void CleanupCorpseAssignedTick(int currentTick)
        {
            try
            {
                if (corpseAssignedTick.Count == 0) return;
                if (currentTick == lastCorpseCleanupTick) return;
                if (corpseAssignedTick.Count < 1000 && currentTick - lastCorpseCleanupTick < CorpseCleanupIntervalTicks) return;

                lastCorpseCleanupTick = currentTick;
                corpseCleanupKeys.Clear();

                foreach (var kv in corpseAssignedTick)
                {
                    if (kv.Value < currentTick - 1) corpseCleanupKeys.Add(kv.Key);
                }

                for (int i = 0; i < corpseCleanupKeys.Count; i++)
                {
                    corpseAssignedTick.Remove(corpseCleanupKeys[i]);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Zoology] CorpseAssignedTickCleanupFailed: " + ex);
            }
        }

        private static bool AttemptReserveAndLog(Pawn pawn, Thing target, Job job)
        {
            try
            {
                
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
                            
                            TryReleaseReservationsForTarget(target);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Zoology] TryReserveViaPawnMethod threw: " + ex);
                }

                
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

            }
            catch (Exception ex)
            {
                Log.Error("[Zoology] AttemptReserveAndLog failed: " + ex);
            }
            return false;
        }

        private static object GetReservationsManager()
        {
            try
            {
                if (FindReservationsProperty == null) return null;
                return FindReservationsProperty.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static MethodInfo GetFirstReserverMethod(Type reservationsType)
        {
            if (reservationsType == null) return null;
            if (firstReserverMethodByReservationsType.TryGetValue(reservationsType, out var cached)) return cached;

            MethodInfo firstReserverMethod = null;
            var methods = reservationsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
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

            firstReserverMethodByReservationsType[reservationsType] = firstReserverMethod;
            return firstReserverMethod;
        }

        private static Pawn QueryCurrentReserverPawn(Thing target)
        {
            try
            {
                if (target == null) return null;
                var reservationsMgr = GetReservationsManager();
                if (reservationsMgr == null) return null;

                var resMgrType = reservationsMgr.GetType();
                MethodInfo firstReserverMethod = GetFirstReserverMethod(resMgrType);
                if (firstReserverMethod == null) return null;

                object arg = target;
                var paramType = firstReserverMethod.GetParameters()[0].ParameterType;
                if (paramType != typeof(Thing) && paramType.Name.Contains("LocalTargetInfo"))
                {
                    if (!localTargetCtorByType.TryGetValue(paramType, out var ctor))
                    {
                        ctor = paramType.GetConstructor(new Type[] { typeof(Thing) });
                        localTargetCtorByType[paramType] = ctor;
                    }
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

        private static MethodInfo[] GetReleaseMethods(Type reservationsType)
        {
            if (reservationsType == null) return Array.Empty<MethodInfo>();
            if (releaseMethodsByReservationsType.TryGetValue(reservationsType, out var cached)) return cached;

            var methods = reservationsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var found = new List<MethodInfo>(8);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                string name = m.Name.ToLowerInvariant();
                if (name.Contains("release") || name.Contains("remove"))
                {
                    found.Add(m);
                }
            }

            cached = found.ToArray();
            releaseMethodsByReservationsType[reservationsType] = cached;
            return cached;
        }

        private static void TryReleaseReservationsForTarget(Thing target)
        {
            try
            {
                if (target == null) return;
                var reservationsMgr = GetReservationsManager();
                if (reservationsMgr == null) return;

                var methods = GetReleaseMethods(reservationsMgr.GetType());
                for (int mi = 0; mi < methods.Length; mi++)
                {
                    var m = methods[mi];
                    var pars = m.GetParameters();
                    
                    if (pars.Length == 1)
                    {
                        var pType = pars[0].ParameterType;
                        object arg = target;
                        if (pType != typeof(Thing) && pType.Name.Contains("LocalTargetInfo"))
                        {
                            if (!localTargetCtorByType.TryGetValue(pType, out var ctor))
                            {
                                ctor = pType.GetConstructor(new Type[] { typeof(Thing) });
                                localTargetCtorByType[pType] = ctor;
                            }
                            if (ctor != null) arg = ctor.Invoke(new object[] { target });
                        }
                        try { m.Invoke(reservationsMgr, new object[] { arg }); }
                        catch (Exception ex) { Log.Error("[Zoology] TryReleaseReservationsForTarget single-param invoke failed: " + ex); }
                    }
                    else if (pars.Length == 2)
                    {
                        
                        object arg0 = null;
                        object arg1 = target;
                        var pType = pars[1].ParameterType;
                        if (pType != typeof(Thing) && pType.Name.Contains("LocalTargetInfo"))
                        {
                            if (!localTargetCtorByType.TryGetValue(pType, out var ctor))
                            {
                                ctor = pType.GetConstructor(new Type[] { typeof(Thing) });
                                localTargetCtorByType[pType] = ctor;
                            }
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
            
            string name = paramType.Name;
            if (name.Contains("LocalTargetInfo") || name.Contains("TargetInfo"))
            {
                
                if (!localTargetCtorByType.TryGetValue(paramType, out var ctor1))
                {
                    ctor1 = paramType.GetConstructor(new Type[] { typeof(Thing) });
                    localTargetCtorByType[paramType] = ctor1;
                }
                if (ctor1 != null)
                    return ctor1.Invoke(new object[] { target });
                
                if (!targetInfoCtorByType.TryGetValue(paramType, out var ctor2))
                {
                    ctor2 = paramType.GetConstructor(new Type[] { typeof(IntVec3), typeof(Map) });
                    targetInfoCtorByType[paramType] = ctor2;
                }
                if (ctor2 != null)
                    return ctor2.Invoke(new object[] { target.PositionHeld, target.Map });
                
                return target;
            }
            
            if (paramType == typeof(object)) return null;
            return null;
        }

        private static MethodInfo[] GetPawnReserveMethods(Type pawnType)
        {
            if (pawnType == null) return Array.Empty<MethodInfo>();
            if (reserveMethodsByPawnType.TryGetValue(pawnType, out var cached)) return cached;

            var methods = pawnType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var found = new List<MethodInfo>(6);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m.Name == "Reserve") found.Add(m);
            }
            cached = found.ToArray();
            reserveMethodsByPawnType[pawnType] = cached;
            return cached;
        }

        private static MethodInfo[] GetReservationsReserveMethods(Type reservationsType)
        {
            if (reservationsType == null) return Array.Empty<MethodInfo>();
            if (reserveMethodsByReservationsType.TryGetValue(reservationsType, out var cached)) return cached;

            var methods = reservationsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var found = new List<MethodInfo>(10);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m.Name.IndexOf("reserve", StringComparison.OrdinalIgnoreCase) >= 0) found.Add(m);
            }
            cached = found.ToArray();
            reserveMethodsByReservationsType[reservationsType] = cached;
            return cached;
        }

        private static bool TryReserveViaPawnMethod(Pawn pawn, Thing target, Job job)
        {
            if (pawn == null || target == null) return false;

            var methods = GetPawnReserveMethods(pawn.GetType());
            for (int mi = 0; mi < methods.Length; mi++)
            {
                var m = methods[mi];
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
                if (target == null) return false;
                var reservationsMgr = GetReservationsManager();
                if (reservationsMgr == null) return false;

                var methods = GetReservationsReserveMethods(reservationsMgr.GetType());
                for (int mi = 0; mi < methods.Length; mi++)
                {
                    var m = methods[mi];
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

            if (!queuedJobGetterByType.TryGetValue(t, out var getter))
            {
                var p = t.GetProperty("job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? t.GetProperty("Job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    getter = obj => p.GetValue(obj) as Job;
                }
                else
                {
                    var f = t.GetField("job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? t.GetField("Job", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null)
                    {
                        getter = obj => f.GetValue(obj) as Job;
                    }
                    else
                    {
                        getter = _ => null;
                    }
                }

                queuedJobGetterByType[t] = getter;
            }

            return getter(queuedItem);
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
