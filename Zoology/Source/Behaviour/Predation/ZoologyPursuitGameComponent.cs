// ZoologyPursuitGameComponent.cs

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    public class ZoologyPursuitGameComponent : GameComponent
    {
        private const int CHASE_TIMEOUT = 2500;
        private const int BLOCK_MULTIPLIER = 3;
        private const int SCAN_INTERVAL = 120;
        private const int CLEAN_INTERVAL = 10000;
        private const int MIN_DISTANCE_FOR_MELEE_SQ = 2 * 2;
        private const int TICKS_PER_LONG_TICK = CHASE_TIMEOUT;

        private static readonly object dictLock = new object();

        // persisted dictionaries (ключ -> untilTick)
        private static Dictionary<long, long> allowedUntil = new Dictionary<long, long>();
        private static Dictionary<long, long> blockedUntil = new Dictionary<long, long>();

        // runtime tracking не сериализуемый: key -> chaseStartTick
        private readonly Dictionary<long, long> chaseStart = new Dictionary<long, long>();

        // singleton
        private static ZoologyPursuitGameComponent singleton;

        // ссылка на игру, которой принадлежит этот экземпляр компонента
        private Game owningGame;

        public static ZoologyPursuitGameComponent Instance
        {
            get
            {
                try
                {
                    // если singleton валиден и принадлежит текущей игре — возвращаем
                    if (singleton != null && singleton.owningGame == Current.Game)
                        return singleton;

                    // иначе попробуем получить из Current.Game (если есть)
                    if (Current.Game != null)
                    {
                        var comp = Current.Game.GetComponent<ZoologyPursuitGameComponent>();
                        if (comp != null)
                        {
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

        // Пустой конструктор — соответствует GameComponent API вашей игры
        public ZoologyPursuitGameComponent()
        {
            try
            {
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
                // запомним owningGame если есть
                this.owningGame = game;

                if (singleton == null || singleton.owningGame != this.owningGame)
                {
                    singleton = this;

                    lock (dictLock)
                    {
                        // при создании для новой Game очищаем persisted-данные
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
                // сохраняем ссылку на игру (может быть null в ранних этапах)
                this.owningGame = Current.Game;

                // если singleton отсутствует или относится к другой Game — обновляем и очищаем persisted-словари
                if (singleton == null || singleton.owningGame != this.owningGame)
                {
                    singleton = this;

                    lock (dictLock)
                    {
                        // при смене/создании новой игры — очищаем persisted-данные (чтобы не переносить старые ключи)
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
                // новая игра — явно очищаем состояние
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

        // --- Public API ---
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
                if (blockedUntil.ContainsKey(key)) blockedUntil.Remove(key);
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

            if (Find.TickManager == null) return;
            tickCounter++;
            long now = Find.TickManager.TicksGame;

            if (tickCounter % SCAN_INTERVAL == 0)
            {
                try
                {
                    var maps = Find.Maps;
                    var toBlock = new List<(long key, Pawn predator, Pawn prey)>();

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

                            bool isHuntDriver = curJob.def != null && curJob.def.driverClass != null
                                                && typeof(JobDriver_PredatorHunt).IsAssignableFrom(curJob.def.driverClass);
                            if (!isHuntDriver)
                            {
                                RemoveChaseStartForPredator(predator);
                                continue;
                            }

                            if (!curJob.targetA.HasThing) continue;
                            Thing t = curJob.GetTarget(TargetIndex.A).Thing;
                            if (t is not Pawn prey) continue;
                            if (prey.Dead) continue;

                            long key = KeyFor(predator, prey);
                            if (key == 0L) continue;

                            bool isBlocked;
                            lock (dictLock)
                            {
                                isBlocked = blockedUntil.TryGetValue(key, out long bUntil) && now <= bUntil;
                            }
                            if (isBlocked)
                            {
                                lock (dictLock)
                                {
                                    if (chaseStart.ContainsKey(key)) chaseStart.Remove(key);
                                }
                                continue;
                            }

                            lock (dictLock)
                            {
                                if (!chaseStart.TryGetValue(key, out long startTick) || startTick == 0L)
                                {
                                    chaseStart[key] = now;
                                    continue;
                                }
                            }

                            long elapsedLocal;
                            lock (dictLock)
                            {
                                elapsedLocal = now - chaseStart[key];
                            }

                            if (elapsedLocal >= CHASE_TIMEOUT)
                            {
                                toBlock.Add((key, predator, prey));
                            }
                        }
                    }

                    for (int i = 0; i < toBlock.Count; i++)
                    {
                        var (key, predator, prey) = toBlock[i];
                        long now2 = Find.TickManager.TicksGame;

                        lock (dictLock)
                        {
                            if (blockedUntil.TryGetValue(key, out long exist) && exist > now2)
                            {
                                if (chaseStart.ContainsKey(key)) chaseStart.Remove(key);
                                continue;
                            }

                            blockedUntil[key] = now2 + (long)CHASE_TIMEOUT * BLOCK_MULTIPLIER;
                            if (allowedUntil.ContainsKey(key))
                            {
                                allowedUntil.Remove(key);
                            }
                            if (chaseStart.ContainsKey(key)) chaseStart.Remove(key);
                        }

                        long blockUntil = now2 + (long)CHASE_TIMEOUT * BLOCK_MULTIPLIER;

                        // аккуратно прерываем hunt job если не в melee proximity (вынесено в отдельный метод)
                        StopPredatorHuntIfNeeded(predator, prey);
                    }
                }
                catch (Exception exScan)
                {
                    Log.Error($"[Zoology] GameComponentTick scan error: {exScan}");
                }
            }

            // редкая чистка (оставлена как было)
            if (tickCounter % CLEAN_INTERVAL == 0)
            {
                long now3 = Find.TickManager?.TicksGame ?? 0L;
                lock (dictLock)
                {
                    var removeB = blockedUntil.Where(kv => kv.Value < now3).Select(kv => kv.Key).ToList();
                    foreach (var k in removeB) blockedUntil.Remove(k);

                    var removeA = allowedUntil.Where(kv => kv.Value < now3).Select(kv => kv.Key).ToList();
                    foreach (var k in removeA) allowedUntil.Remove(k);

                    var chaseToRemove = chaseStart.Where(kv => kv.Value + CHASE_TIMEOUT * 5 < now3).Select(kv => kv.Key).ToList();
                    foreach (var k in chaseToRemove) chaseStart.Remove(k);
                }
            }
        }

        private void RemoveChaseStartForPredator(Pawn predator)
        {
            if (predator == null) return;
            lock (dictLock)
            {
                var keysToRemove = chaseStart.Keys
                    .Where(k => (int)((uint)(k >> 32)) == predator.thingIDNumber)
                    .ToList();
                foreach (var k in keysToRemove)
                    chaseStart.Remove(k);
            }
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
                        try { predator.jobs?.jobQueue?.Clear(predator, true); } catch { try { predator.jobs?.jobQueue?.Clear(predator, true); } catch { } }
                        try { predator.pather?.StopDead(); } catch { }
                        try
                        {
                            if (predator.jobs != null)
                            {
                                Job waitJob = JobMaker.MakeJob(JobDefOf.Wait, 250);
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

        // сериализация (оставлена ваша логика)
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
    }
}