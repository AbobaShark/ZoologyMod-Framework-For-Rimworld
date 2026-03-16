using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZoologyMod
{
    public static class PredatorPresenceManager
    {
        private const int PRESENCE_CHECK_INTERVAL = ZoologyTickLimiter.PreyProtection.PresenceCheckInterval;

        
        private const int LOG_COOLDOWN_TICKS = ZoologyTickLimiter.PreyProtection.PresenceLogCooldownTicks;

        private static readonly Dictionary<string, long> _lastLogTick = new Dictionary<string, long>();
        private static readonly Dictionary<int, long> _transientBlockTick = new Dictionary<int, long>();
        private static readonly List<KeyValuePair<int, int>> _pairSnapshotBuffer = new List<KeyValuePair<int, int>>(32);
        private static readonly List<int> _transientCleanupBuffer = new List<int>(16);

        public static void TickPresence()
        {
            try
            {
                long now = Find.TickManager?.TicksGame ?? 0L;
                if (now % PRESENCE_CHECK_INTERVAL != 0) return;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp == null) return;

                _pairSnapshotBuffer.Clear();
                comp.FillRuntimePredatorToCorpseSnapshot(_pairSnapshotBuffer);
                if (_pairSnapshotBuffer.Count == 0) return;
                int presenceRangeSquared = PreyProtectionUtility.GetProtectionRangeSquared();

                for (int i = 0; i < _pairSnapshotBuffer.Count; i++)
                {
                    int predatorId = _pairSnapshotBuffer[i].Key;
                    int corpseId = _pairSnapshotBuffer[i].Value;

                    Pawn p = null;
                    try { p = comp.GetSpawnedPawnById(predatorId); } catch { p = null; }
                    if (p == null || !p.RaceProps.predator) continue;

                    Corpse targetCorpse = null;
                    try { targetCorpse = comp.GetCorpseById(corpseId, out _); } catch { targetCorpse = null; }
                    if (targetCorpse == null) continue;

                    string stayReason;
                    if (!ShouldStayAtPrey(p, out stayReason))
                    {
                        LogOnce(p, targetCorpse, $"Will NOT stay at prey: {stayReason}");
                        continue;
                    }

                    if (!PreyProtectionUtility.TryGetProtectionAnchor(targetCorpse, out Map corpseMap, out IntVec3 targetPos))
                    {
                        LogOnce(p, targetCorpse, "Corpse has no valid protection anchor.");
                        continue;
                    }

                    if (p.Map == null || corpseMap == null || p.Map != corpseMap)
                    {
                        LogOnce(p, targetCorpse, $"Different maps (predatorMap {(p.Map == null ? "null" : p.Map.uniqueID.ToString())}, corpseMap {(corpseMap == null ? "null" : corpseMap.uniqueID.ToString())}).");
                        continue;
                    }

                    if (PreyProtectionUtility.IsPawnWithinProtectionRange(p, corpseMap, targetPos, presenceRangeSquared))
                    {
                        continue;
                    }

                    TrySendPredatorToCorpse(p, targetCorpse, targetPos);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: PredatorPresenceManager.TickPresence exception: {ex}");
            }
            finally
            {
                _pairSnapshotBuffer.Clear();
            }
        }

        private static bool ShouldStayAtPrey(Pawn pawn, out string reason)
        {
            reason = null;
            try
            {
                long now = Find.TickManager?.TicksGame ?? 0L;
                if (pawn == null) { reason = "pawn is null"; return false; }

                try
                {
                    int pid = pawn.thingIDNumber;
                    if (_transientBlockTick.TryGetValue(pid, out long blockedTick) && blockedTick == now)
                    {
                        reason = "transient block for this tick";
                        return false;
                    }
                }
                catch { }

                if (!pawn.IsAnimal) { reason = "not an animal"; return false; }

                if (pawn.InMentalState)
                {
                    reason = "in mental state (transient)";
                    RecordTransientBlock(pawn, now);
                    return false;
                }

                if (pawn.IsFighting() || ProtectPreyState.IsActivelyProtectingNearbyPrey(pawn, 20f))
                {
                    reason = "is fighting (transient)";
                    RecordTransientBlock(pawn, now);
                    return false;
                }

                if (pawn.Downed) { reason = "downed"; return false; }
                if (pawn.Dead) { reason = "dead"; return false; }
                if (ThinkNode_ConditionalShouldFollowMaster.ShouldFollowMaster(pawn)) { reason = "should follow master (tame/follower)"; return false; }
                if (HasLord(pawn)) { reason = "has lord"; return false; }
                if (pawn.Faction == Faction.OfPlayer && pawn.Map != null) { reason = "player faction on player home map"; return false; }
                if (pawn.Faction != null && !pawn.Faction.def.animalsFleeDanger) { reason = "faction forbids animals fleeing danger"; return false; }
                if (pawn.CurJob != null && pawn.CurJobDef != null && pawn.CurJobDef.neverFleeFromEnemies) { reason = "current job forbids fleeing from enemies"; return false; }

                if (pawn.jobs?.curJob != null)
                {
                    var cur = pawn.jobs.curJob;
                    if (cur.def == JobDefOf.Flee && cur.startTick == Find.TickManager.TicksGame)
                    {
                        reason = "just started Flee job this tick";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                reason = $"exception in ShouldStayAtPrey: {ex.Message}";
                LogOnce(pawn, null, $"ShouldStayAtPrey exception: {ex}");
                return false;
            }
        }

        private static void RecordTransientBlock(Pawn pawn, long now)
        {
            try
            {
                if (pawn == null) return;
                int pid = pawn.thingIDNumber;
                _transientBlockTick[pid] = now;

                _transientCleanupBuffer.Clear();
                foreach (var kv in _transientBlockTick)
                {
                    if (kv.Value < now) _transientCleanupBuffer.Add(kv.Key);
                }
                for (int i = 0; i < _transientCleanupBuffer.Count; i++)
                {
                    _transientBlockTick.Remove(_transientCleanupBuffer[i]);
                }
                _transientCleanupBuffer.Clear();
            }
            catch { }
        }

        private static bool HasLord(Pawn pawn)
        {
            try
            {
                return pawn != null && pawn.GetLord() != null;
            }
            catch { return false; }
        }

        private static void TrySendPredatorToCorpse(Pawn pred, Corpse corpse, IntVec3 corpsePos)
        {
            try
            {
                if (pred == null || corpse == null)
                {
                    LogOnce(pred, corpse, "TrySendPredatorToCorpse: pred or corpse is null.");
                    return;
                }

                if (pred.Dead || pred.Downed || !pred.Spawned)
                {
                    LogOnce(pred, corpse, $"Predator not valid for ordering (Dead:{pred.Dead}, Downed:{pred.Downed}, Spawned:{pred.Spawned}).");
                    return;
                }

                var curJob = pred.CurJob;
                if (curJob != null)
                {
                    if (curJob.playerForced)
                    {
                        LogOnce(pred, corpse, "Current job is playerForced; will not override.");
                        return;
                    }
                    if (curJob.def == JobDefOf.AttackMelee)
                    {
                        LogOnce(pred, corpse, "Currently performing AttackMelee; will not override.");
                        return;
                    }
                    if (curJob.def != null && string.Equals(curJob.def.defName, "Zoology_ProtectPrey", StringComparison.OrdinalIgnoreCase))
                    {
                        LogOnce(pred, corpse, "Already protecting prey (Zoology_ProtectPrey).");
                        return;
                    }
                }

                IntVec3 dest = FindClosestReachableCellNear(corpsePos, pred.Map, pred, PreyProtectionUtility.GetProtectionRange());

                if (!dest.IsValid || dest == corpsePos)
                {
                    IntVec3 adj;
                    try
                    {
                        if (RCellFinder.TryFindGoodAdjacentSpotToTouch(pred, corpse, out adj))
                        {
                            dest = adj;
                        }
                    }
                    catch { }
                }

                if (!dest.IsValid)
                {
                    try
                    {
                        bool canReach = false;
                        try { canReach = pred.CanReach(corpsePos, PathEndMode.Touch, Danger.Some); } catch { canReach = true; }
                        if (!canReach)
                        {
                            try { canReach = pred.CanReach(corpsePos, PathEndMode.Touch, Danger.Deadly); } catch { canReach = true; }
                        }

                        if (!canReach)
                        {
                            LogOnce(pred, corpse, $"No reachable destination near corpse and cannot reach corpsePos (canReach=false).");
                            return;
                        }

                        var gotoDef = DefDatabase<JobDef>.GetNamedSilentFail("Goto");
                        if (gotoDef != null)
                        {
                            var gotoJob = JobMaker.MakeJob(gotoDef, corpsePos);
                            gotoJob.locomotionUrgency = LocomotionUrgency.Walk;
                            pred.jobs.TryTakeOrderedJob(gotoJob);
                            LogOnce(pred, corpse, $"Ordered Goto directly to corpse position ({corpsePos}).");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogOnce(pred, corpse, $"Fallback reachability/goto exception: {ex.Message}");
                    }
                    return;
                }

                if (pred.pather.Moving && pred.pather.Destination == dest) return;

                try
                {
                    var gotoDef2 = DefDatabase<JobDef>.GetNamedSilentFail("Goto");
                    if (gotoDef2 != null)
                    {
                        var gotoJob2 = JobMaker.MakeJob(gotoDef2, dest);
                        gotoJob2.locomotionUrgency = LocomotionUrgency.Walk;
                        pred.jobs.TryTakeOrderedJob(gotoJob2);
                        LogOnce(pred, corpse, $"Ordered Walk-Goto to corpse area. Dest: {dest} (distance {IntDistance(pred.Position, dest)}).");
                    }
                    else
                    {
                        pred.pather.StartPath(dest, PathEndMode.OnCell);
                        LogOnce(pred, corpse, $"Started path to corpse area (StartPath fallback). Dest: {dest} (distance {IntDistance(pred.Position, dest)}).");
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        var gotoDef3 = DefDatabase<JobDef>.GetNamedSilentFail("Goto");
                        if (gotoDef3 != null)
                        {
                            var j = JobMaker.MakeJob(gotoDef3, dest);
                            j.locomotionUrgency = LocomotionUrgency.Walk;
                            pred.jobs.TryTakeOrderedJob(j);
                            LogOnce(pred, corpse, $"StartPath failed, fallback to Walk-Goto job to {dest} succeeded. Exception: {ex.Message}");
                        }
                        else
                        {
                            LogOnce(pred, corpse, $"StartPath failed and no Goto job available. Exception: {ex.Message}");
                        }
                    }
                    catch (Exception ex2)
                    {
                        LogOnce(pred, corpse, $"Both StartPath and fallback failed: {ex2.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: TrySendPredatorToCorpse exception: {ex}");
            }
        }

        private static IntVec3 FindClosestReachableCellNear(IntVec3 center, Map map, Pawn pawn, int maxDist)
        {
            try
            {
                if (map == null || pawn == null) return IntVec3.Invalid;

                int max = Math.Max(0, maxDist);
                var best = IntVec3.Invalid;
                int bestScore = int.MaxValue;

                var cells = GenRadial.RadialCellsAround(center, max, true);
                foreach (var c in cells)
                {
                    if (!c.InBounds(map)) continue;
                    if (!c.Walkable(map)) continue;
                    if (!c.Standable(map)) continue;
                    if (c == center) continue;

                    try
                    {
                        var terrain = c.GetTerrain(map);
                        if (terrain != null && terrain.avoidWander && (!terrain.IsWater || !pawn.RaceProps.waterSeeker))
                            continue;
                    }
                    catch { }

                    bool canReach = false;
                    try
                    {
                        canReach = map.reachability.CanReachNonLocal(pawn.Position, new TargetInfo(c, map), PathEndMode.OnCell, TraverseParms.For(pawn, Danger.Some, TraverseMode.ByPawn, false));
                    }
                    catch
                    {
                        canReach = pawn.CanReach(c, PathEndMode.OnCell, Danger.Some);
                    }
                    if (!canReach) continue;

                    int score = (pawn.Position - c).LengthHorizontalSquared;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }

                return best;
            }
            catch { }
            return IntVec3.Invalid;
        }

        private static void LogOnce(Pawn pawn, Corpse corpse, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(message)) return;

                string ml = message.ToLowerInvariant();
                bool isImportant = ml.Contains("exception") || ml.Contains("error") || ml.Contains("failed");

                if (!isImportant) return;

                long now = Find.TickManager?.TicksGame ?? 0L;
                int pawnId = pawn?.thingIDNumber ?? 0;
                int corpseId = corpse?.thingIDNumber ?? 0;
                string key = $"{pawnId}_{corpseId}_{message}";

                long last;
                if (_lastLogTick.TryGetValue(key, out last))
                {
                    if (now - last < LOG_COOLDOWN_TICKS) return;
                    _lastLogTick[key] = now;
                }
                else
                {
                    _lastLogTick[key] = now;
                }

                string pawnLabel = pawn != null ? $"{pawn.LabelShort} (id={pawnId})" : "null-pawn";
                string corpseLabel = corpse != null ? $"{(corpse.InnerPawn != null ? corpse.InnerPawn.LabelShort : corpse.ToString())} (id={corpseId})" : "null-corpse";

                string final = $"Zoology: [{pawnLabel}] - [{corpseLabel}] => {message}";
                Log.Warning(final);
            }
            catch { }
        }

        private static int IntDistance(IntVec3 a, IntVec3 b)
        {
            try
            {
                int dx = a.x - b.x;
                int dz = a.z - b.z;
                return (int)Math.Sqrt(dx * dx + dz * dz);
            }
            catch { return 0; }
        }
    }
}
