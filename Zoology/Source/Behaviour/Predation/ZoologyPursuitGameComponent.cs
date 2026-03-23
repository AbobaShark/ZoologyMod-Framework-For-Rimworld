using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    public class ZoologyPursuitGameComponent : GameComponent
    {
        private const int CHASE_TIMEOUT = ZoologyTickLimiter.Pursuit.ChaseTimeoutTicks;
        private const int BLOCK_MULTIPLIER = 3;
        private const int SCAN_INTERVAL = ZoologyTickLimiter.Pursuit.ScanIntervalTicks;
        private const int CLEAN_INTERVAL = ZoologyTickLimiter.Pursuit.CleanIntervalTicks;
        private const int MIN_DISTANCE_FOR_MELEE_SQ = 2 * 2;
        private const int TICKS_PER_LONG_TICK = ZoologyTickLimiter.Pursuit.TicksPerLongTick;

        private static readonly object dictLock = new object();

        
        private static Dictionary<long, long> allowedUntil = new Dictionary<long, long>();
        private static Dictionary<long, long> blockedUntil = new Dictionary<long, long>();

        
        private readonly Dictionary<long, long> chaseStart = new Dictionary<long, long>();
        private readonly List<long> keyRemovalBuffer = new List<long>(32);
        private readonly List<PendingBlock> pendingBlocks = new List<PendingBlock>(8);
        private readonly HashSet<int> inactivePredatorIdsBuffer = new HashSet<int>();

        
        private static ZoologyPursuitGameComponent singleton;

        
        private Game owningGame;

        public static ZoologyPursuitGameComponent Instance
        {
            get
            {
                try
                {
                    
                    if (singleton != null && singleton.owningGame == Current.Game)
                        return singleton;

                    
                    if (Current.Game != null)
                    {
                        var comp = Current.Game.GetComponent<ZoologyPursuitGameComponent>();
                        if (comp != null)
                        {
                            comp.owningGame = Current.Game;
                            singleton = comp;
                            return singleton;
                        }
                    }

                    singleton = null;
                    return null;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Zoology] Instance getter exception: {ex}");
                    return null;
                }
            }
        }

        
        public ZoologyPursuitGameComponent()
        {
            try
            {
                this.owningGame = Current.Game;
                if (singleton == null)
                {
                    singleton = this;
                }
                else
                {
                    Log.Message("[Zoology] ZoologyPursuitGameComponent: ctor extra instance detected (will resolve in FinalizeInit).");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] ZoologyPursuitGameComponent ctor exception: {ex}");
            }
        }

        public ZoologyPursuitGameComponent(Game game)
        {
            try
            {
                
                this.owningGame = game;

                if (singleton == null || singleton.owningGame != this.owningGame)
                {
                    singleton = this;

                    lock (dictLock)
                    {
                        
                        allowedUntil = new Dictionary<long, long>();
                        blockedUntil = new Dictionary<long, long>();
                        chaseStart.Clear();
                    }
                }
                else
                {
                    Log.Message("[Zoology] ZoologyPursuitGameComponent: ctor(Game) extra instance detected (same game).");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] ZoologyPursuitGameComponent ctor(Game) exception: {ex}");
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            try
            {
                
                this.owningGame = Current.Game;

                
                if (singleton == null || singleton.owningGame != this.owningGame)
                {
                    singleton = this;

                    lock (dictLock)
                    {
                        
                        allowedUntil = new Dictionary<long, long>();
                        blockedUntil = new Dictionary<long, long>();
                        chaseStart.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] FinalizeInit exception: {ex}");
            }
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            try
            {
                this.owningGame = Current.Game;
                
                lock (dictLock)
                {
                    allowedUntil = new Dictionary<long, long>();
                    blockedUntil = new Dictionary<long, long>();
                    chaseStart.Clear();
                }
                singleton = this;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] StartedNewGame exception: {ex}");
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            try
            {
                this.owningGame = Current.Game;
                singleton = this;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] LoadedGame exception: {ex}");
            }
        }

        
        public long PairKey(Pawn predator, Pawn prey) => KeyFor(predator, prey);

        public bool IsPairAllowedNow(Pawn predator, Pawn prey)
        {
            long key = KeyFor(predator, prey);
            if (key == 0L) return false;
            long now = Find.TickManager?.TicksGame ?? 0L;
            lock (dictLock)
            {
                return allowedUntil.TryGetValue(key, out long aUntil) && now <= aUntil;
            }
        }

        public bool IsPairBlockedNow(Pawn predator, Pawn prey)
        {
            long key = KeyFor(predator, prey);
            if (key == 0L) return false;
            long now = Find.TickManager?.TicksGame ?? 0L;
            lock (dictLock)
            {
                return blockedUntil.TryGetValue(key, out long bUntil) && now <= bUntil;
            }
        }

        public void AllowPursuit(Pawn predator, Pawn prey, int durationLongTicks)
        {
            if (predator == null || prey == null)
            {
                return;
            }
            if (predator.Map == null || prey.Map == null || predator.Map != prey.Map)
            {
                return;
            }

            long key = KeyFor(predator, prey);
            if (key == 0L) return;

            long now = Find.TickManager?.TicksGame ?? 0L;
            long until = now + (long)durationLongTicks * TICKS_PER_LONG_TICK;

            lock (dictLock)
            {
                if (blockedUntil.TryGetValue(key, out long bUntil) && now <= bUntil)
                {
                    return;
                }

                if (allowedUntil.TryGetValue(key, out long existingUntil) && existingUntil > now)
                {
                    return;
                }

                allowedUntil[key] = until;
                blockedUntil.Remove(key);
            }

        }

        public void DumpStatus(int maxItems = 10)
        {
            try
            {
                long now = Find.TickManager?.TicksGame ?? 0L;
                lock (dictLock)
                {
                    int i = 0;
                    foreach (var kv in blockedUntil)
                    {
                        if (i++ >= maxItems) break;
                    }
                    i = 0;
                    foreach (var kv in allowedUntil)
                    {
                        if (i++ >= maxItems) break;
                    }
                    i = 0;
                    foreach (var kv in chaseStart)
                    {
                        if (i++ >= maxItems) break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] DumpStatus failed: {ex}");
            }
        }

        private int tickCounter = 0;

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (ZoologyModSettings.Instance != null && !ZoologyModSettings.Instance.EnablePreyFleeFromPredators)
            {
                return;
            }

            if (Find.TickManager == null) return;
            tickCounter++;
            long now = Find.TickManager.TicksGame;

            if (tickCounter % SCAN_INTERVAL == 0)
            {
                try
                {
                    var maps = Find.Maps;
                    pendingBlocks.Clear();
                    inactivePredatorIdsBuffer.Clear();

                    for (int mi = 0; mi < maps.Count; mi++)
                    {
                        var map = maps[mi];
                        var all = map.mapPawns.AllPawnsSpawned;
                        for (int i = 0; i < all.Count; i++)
                        {
                            Pawn predator = all[i];
                            if (predator == null) continue;
                            if (!predator.RaceProps.predator) continue;

                            Job curJob = predator.CurJob;
                            if (curJob == null) continue;

                            bool isHuntDriver = curJob.def == JobDefOf.PredatorHunt;
                            if (!isHuntDriver)
                            {
                                Type driverClass = curJob.def?.driverClass;
                                isHuntDriver = driverClass != null && typeof(JobDriver_PredatorHunt).IsAssignableFrom(driverClass);
                            }
                            if (!isHuntDriver)
                            {
                                inactivePredatorIdsBuffer.Add(predator.thingIDNumber);
                                continue;
                            }

                            if (!curJob.targetA.HasThing) continue;
                            Thing t = curJob.GetTarget(TargetIndex.A).Thing;
                            if (t is not Pawn prey) continue;
                            if (prey.Dead) continue;

                            long key = KeyFor(predator, prey);
                            if (key == 0L) continue;

                            lock (dictLock)
                            {
                                if (blockedUntil.TryGetValue(key, out long bUntil) && now <= bUntil)
                                {
                                    chaseStart.Remove(key);
                                    continue;
                                }

                                if (!chaseStart.TryGetValue(key, out long startTick) || startTick == 0L)
                                {
                                    chaseStart[key] = now;
                                    continue;
                                }

                                if (now - startTick >= CHASE_TIMEOUT)
                                {
                                    pendingBlocks.Add(new PendingBlock(key, predator, prey));
                                }
                            }
                        }
                    }

                    if (inactivePredatorIdsBuffer.Count > 0)
                    {
                        lock (dictLock)
                        {
                            RemoveChaseStartsForPredatorsLocked(inactivePredatorIdsBuffer);
                        }
                        inactivePredatorIdsBuffer.Clear();
                    }

                    for (int i = 0; i < pendingBlocks.Count; i++)
                    {
                        PendingBlock block = pendingBlocks[i];
                        long now2 = now;
                        bool shouldStopPredator = false;

                        lock (dictLock)
                        {
                            if (blockedUntil.TryGetValue(block.Key, out long exist) && exist > now2)
                            {
                                chaseStart.Remove(block.Key);
                            }
                            else
                            {
                                blockedUntil[block.Key] = now2 + (long)CHASE_TIMEOUT * BLOCK_MULTIPLIER;
                                allowedUntil.Remove(block.Key);
                                chaseStart.Remove(block.Key);
                                shouldStopPredator = true;
                            }
                        }

                        if (shouldStopPredator)
                        {
                            StopPredatorHuntIfNeeded(block.Predator, block.Prey);
                        }
                    }
                }
                catch (Exception exScan)
                {
                    Log.Error($"[Zoology] GameComponentTick scan error: {exScan}");
                    inactivePredatorIdsBuffer.Clear();
                }
            }

            
            if (tickCounter % CLEAN_INTERVAL == 0)
            {
                long now3 = Find.TickManager?.TicksGame ?? 0L;
                lock (dictLock)
                {
                    RemoveExpiredEntries(blockedUntil, now3);
                    RemoveExpiredEntries(allowedUntil, now3);
                    RemoveExpiredChases(now3);
                }
            }
        }

        private void RemoveChaseStartForPredator(Pawn predator)
        {
            if (predator == null) return;
            lock (dictLock)
            {
                RemoveChaseStartForPredatorLocked(predator.thingIDNumber);
            }
        }

        private void RemoveChaseStartForPredatorLocked(int predatorThingId)
        {
            keyRemovalBuffer.Clear();
            foreach (var kv in chaseStart)
            {
                if ((int)((uint)(kv.Key >> 32)) == predatorThingId)
                {
                    keyRemovalBuffer.Add(kv.Key);
                }
            }

            for (int i = 0; i < keyRemovalBuffer.Count; i++)
            {
                chaseStart.Remove(keyRemovalBuffer[i]);
            }

            keyRemovalBuffer.Clear();
        }

        private void RemoveChaseStartsForPredatorsLocked(HashSet<int> predatorThingIds)
        {
            if (predatorThingIds == null || predatorThingIds.Count == 0 || chaseStart.Count == 0)
            {
                return;
            }

            keyRemovalBuffer.Clear();
            foreach (var kv in chaseStart)
            {
                int predatorId = (int)((uint)(kv.Key >> 32));
                if (predatorThingIds.Contains(predatorId))
                {
                    keyRemovalBuffer.Add(kv.Key);
                }
            }

            for (int i = 0; i < keyRemovalBuffer.Count; i++)
            {
                chaseStart.Remove(keyRemovalBuffer[i]);
            }

            keyRemovalBuffer.Clear();
        }

        private void RemoveExpiredEntries(Dictionary<long, long> source, long now)
        {
            keyRemovalBuffer.Clear();
            foreach (var kv in source)
            {
                if (kv.Value < now)
                {
                    keyRemovalBuffer.Add(kv.Key);
                }
            }

            for (int i = 0; i < keyRemovalBuffer.Count; i++)
            {
                source.Remove(keyRemovalBuffer[i]);
            }

            keyRemovalBuffer.Clear();
        }

        private void RemoveExpiredChases(long now)
        {
            keyRemovalBuffer.Clear();
            foreach (var kv in chaseStart)
            {
                if (kv.Value + CHASE_TIMEOUT * 5 < now)
                {
                    keyRemovalBuffer.Add(kv.Key);
                }
            }

            for (int i = 0; i < keyRemovalBuffer.Count; i++)
            {
                chaseStart.Remove(keyRemovalBuffer[i]);
            }

            keyRemovalBuffer.Clear();
        }

        private void StopPredatorHuntIfNeeded(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null) return;
            try
            {
                Job curJob = predator?.CurJob;
                bool doingMelee = curJob != null && curJob.def == JobDefOf.AttackMelee && curJob.targetA.HasThing && curJob.GetTarget(TargetIndex.A).Thing == prey;
                bool targetMatches = curJob != null && curJob.targetA.HasThing && curJob.GetTarget(TargetIndex.A).Thing == prey;
                bool isHuntDriver = curJob != null && curJob.def != null && curJob.def.driverClass != null
                                    && typeof(JobDriver_PredatorHunt).IsAssignableFrom(curJob.def.driverClass);

                if ((targetMatches || isHuntDriver) && !doingMelee)
                {
                    float distSq = (predator.Position - prey.Position).LengthHorizontalSquared;
                    if (distSq > MIN_DISTANCE_FOR_MELEE_SQ)
                    {
                        try { predator.jobs?.EndCurrentJob(JobCondition.InterruptForced); } catch { }
                        try { predator.jobs?.jobQueue?.Clear(predator, true); } catch { }
                        try { predator.pather?.StopDead(); } catch { }
                        try
                        {
                            if (predator.jobs != null)
                            {
                                Job waitJob = JobMaker.MakeJob(JobDefOf.Wait, ZoologyTickLimiter.Pursuit.StopPredatorWaitTicks);
                                predator.jobs.StartJob(waitJob, JobCondition.InterruptForced);
                            }
                        }
                        catch { }
                        try
                        {
                            var stillJob = predator.CurJob;
                            if (stillJob != null && stillJob.targetA.HasThing)
                            {
                                stillJob.targetA = LocalTargetInfo.Invalid;
                                stillJob.targetB = LocalTargetInfo.Invalid;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Auto-block: exception while attempting to stop predator job: {ex}");
            }
        }

        
        public override void ExposeData()
        {
            base.ExposeData();

            List<long> keysAllowed = null;
            List<long> valsAllowed = null;
            List<long> keysBlocked = null;
            List<long> valsBlocked = null;

            lock (dictLock)
            {
                keysAllowed = new List<long>(allowedUntil.Count);
                valsAllowed = new List<long>(allowedUntil.Count);
                foreach (var kv in allowedUntil) { keysAllowed.Add(kv.Key); valsAllowed.Add(kv.Value); }

                keysBlocked = new List<long>(blockedUntil.Count);
                valsBlocked = new List<long>(blockedUntil.Count);
                foreach (var kv in blockedUntil) { keysBlocked.Add(kv.Key); valsBlocked.Add(kv.Value); }
            }

            Scribe_Collections.Look(ref keysAllowed, "Zoology_allowed_keys", LookMode.Value);
            Scribe_Collections.Look(ref valsAllowed, "Zoology_allowed_vals", LookMode.Value);
            Scribe_Collections.Look(ref keysBlocked, "Zoology_blocked_keys", LookMode.Value);
            Scribe_Collections.Look(ref valsBlocked, "Zoology_blocked_vals", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                lock (dictLock)
                {
                    allowedUntil = new Dictionary<long, long>();
                    blockedUntil = new Dictionary<long, long>();

                    if (keysAllowed != null && valsAllowed != null && keysAllowed.Count == valsAllowed.Count)
                    {
                        for (int i = 0; i < keysAllowed.Count; i++)
                            allowedUntil[keysAllowed[i]] = valsAllowed[i];
                    }
                    if (keysBlocked != null && valsBlocked != null && keysBlocked.Count == valsBlocked.Count)
                    {
                        for (int i = 0; i < keysBlocked.Count; i++)
                            blockedUntil[keysBlocked[i]] = valsBlocked[i];
                    }
                }
            }
        }

        private static long KeyFor(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null) return 0L;
            uint pid = (uint)predator.thingIDNumber;
            uint qid = (uint)prey.thingIDNumber;
            return (((long)pid) << 32) | (long)qid;
        }

        private struct PendingBlock
        {
            public readonly long Key;
            public readonly Pawn Predator;
            public readonly Pawn Prey;

            public PendingBlock(long key, Pawn predator, Pawn prey)
            {
                Key = key;
                Predator = predator;
                Prey = prey;
            }
        }
    }
}
