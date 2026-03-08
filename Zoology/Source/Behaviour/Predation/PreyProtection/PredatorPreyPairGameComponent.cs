using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;
using Verse.AI;
using System.Reflection;
using RimWorld.Planet;
using Verse.AI.Group;

namespace ZoologyMod
{
    public class PredatorPreyPairGameComponent : GameComponent
    {
        private const int DEFAULT_PAIR_TICKS = 60 * 60 * 2; 
        private const int HERD_RADIUS = 35; 
        private const long INACCESSIBLE_REMOVE_TICKS = 3600; 
        private const int TICK_CHECK_INTERVAL = 250;

        private const int NOTIFICATION_SUPPRESSION_TICKS = 600; 
        private static readonly Dictionary<int, long> notificationSuppressedUntil = new Dictionary<int, long>();

        
        private static Dictionary<long, long> pairsUntil = new Dictionary<long, long>();

        
        private Dictionary<int, int> runtimePredatorToCorpse = new Dictionary<int, int>();
        private Dictionary<int, HashSet<int>> runtimeCorpseToPredators = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, Pawn> pawnLookupCache = new Dictionary<int, Pawn>();
        private readonly Dictionary<int, Corpse> corpseLookupCache = new Dictionary<int, Corpse>();
        private readonly Dictionary<int, long> corpseNegativeLookupUntil = new Dictionary<int, long>();
        private readonly List<KeyValuePair<long, long>> pairSnapshotBuffer = new List<KeyValuePair<long, long>>(128);
        private readonly List<long> pairRemovalBuffer = new List<long>(32);
        private readonly List<int> activePredatorIdBuffer = new List<int>(8);
        private readonly List<int> ownerPredatorIdBuffer = new List<int>(4);
        private readonly List<Pawn> predatorsToRegisterBuffer = new List<Pawn>(8);
        private readonly List<DefendCandidate> defendCandidateBuffer = new List<DefendCandidate>(8);

        
        private static Dictionary<long, long> inaccessibleSince = new Dictionary<long, long>();
        
        private static Dictionary<long, long> lastTriggerAttempt = new Dictionary<long, long>();
        private const int TRIGGER_COOLDOWN_TICKS = 250; 

        private Game owningGame;
        private static object dictLock = new object();
        private static PredatorPreyPairGameComponent singleton;
        private const int NEGATIVE_LOOKUP_COOLDOWN_TICKS = TICK_CHECK_INTERVAL;

        public static PredatorPreyPairGameComponent Instance {
            get {
                if (singleton == null) {
                    if (Current.Game != null) singleton = Current.Game.GetComponent<PredatorPreyPairGameComponent>();
                }
                return singleton;
            }
        }

        public PredatorPreyPairGameComponent(Game game)
        {
            this.owningGame = game;
            if (singleton == null || singleton.owningGame != this.owningGame)
            {
                singleton = this;
                lock (dictLock)
                {
                    pairsUntil = new Dictionary<long,long>();
                    inaccessibleSince = new Dictionary<long,long>();
                }
                runtimePredatorToCorpse.Clear();
                runtimeCorpseToPredators.Clear();
                ClearLookupCaches();
            }
        }

        
        public PredatorPreyPairGameComponent() { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            this.owningGame = Current.Game;
            singleton = this;
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            lock (dictLock)
            {
                pairsUntil = new Dictionary<long,long>();
                inaccessibleSince = new Dictionary<long,long>();
            }
            runtimePredatorToCorpse.Clear();
            runtimeCorpseToPredators.Clear();
            ClearLookupCaches();
            singleton = this;
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            
            singleton = this;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            try
            {
                
                lock (dictLock)
                {
                    if (pairsUntil == null) pairsUntil = new Dictionary<long, long>();
                    Scribe_Collections.Look(ref pairsUntil, "Zoology_pairsUntil", LookMode.Value, LookMode.Value);
                }

                
                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    if (pairsUntil == null) pairsUntil = new Dictionary<long, long>();

                    runtimePredatorToCorpse.Clear();
                    runtimeCorpseToPredators.Clear();
                    inaccessibleSince = new Dictionary<long, long>();
                    ClearLookupCaches();

                    long now = Find.TickManager?.TicksGame ?? 0L;

                    
                    
                    FillPairSnapshot(pairSnapshotBuffer);
                    for (int i = 0; i < pairSnapshotBuffer.Count; i++)
                    {
                        var kv = pairSnapshotBuffer[i];
                        long key = kv.Key;
                        long until = kv.Value;
                        if (until < now) continue;
                        int pid = (int)((uint)(key >> 32));
                        int cid = (int)((uint)(key & 0xFFFFFFFF));
                        if (pid > 0 && cid > 0)
                        {
                            AddRuntimePairMapping(pid, cid);
                        }
                    }

                    pairSnapshotBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: ExposeData failed for PredatorPreyPairGameComponent: {ex}");
            }
        }

        private long PairKeyFor(int predatorId, int corpseId)
        {
            if (predatorId <= 0 || corpseId <= 0) return 0L;
            uint p = (uint)predatorId;
            uint c = (uint)corpseId;
            return (((long)p) << 32) | (long)c;
        }

        private static int PredatorIdFromKey(long key)
        {
            return (int)((uint)(key >> 32));
        }

        private static int CorpseIdFromKey(long key)
        {
            return (int)((uint)(key & 0xFFFFFFFF));
        }

        private void ClearLookupCaches()
        {
            pawnLookupCache.Clear();
            corpseLookupCache.Clear();
            corpseNegativeLookupUntil.Clear();
        }

        private void AddRuntimePairMapping(int predatorId, int corpseId)
        {
            if (predatorId <= 0 || corpseId <= 0) return;

            if (runtimePredatorToCorpse.TryGetValue(predatorId, out int previousCorpseId) && previousCorpseId != corpseId)
            {
                RemoveRuntimePairMapping(predatorId, previousCorpseId);
            }

            runtimePredatorToCorpse[predatorId] = corpseId;

            if (!runtimeCorpseToPredators.TryGetValue(corpseId, out var predators))
            {
                predators = new HashSet<int>();
                runtimeCorpseToPredators[corpseId] = predators;
            }
            predators.Add(predatorId);
        }

        private void RemoveRuntimePairMapping(int predatorId, int corpseId)
        {
            if (predatorId > 0)
            {
                if (corpseId <= 0)
                {
                    runtimePredatorToCorpse.Remove(predatorId);
                }
                else if (runtimePredatorToCorpse.TryGetValue(predatorId, out int mappedCorpseId) && mappedCorpseId == corpseId)
                {
                    runtimePredatorToCorpse.Remove(predatorId);
                }
            }

            if (corpseId > 0 && runtimeCorpseToPredators.TryGetValue(corpseId, out var predators))
            {
                predators.Remove(predatorId);
                if (predators.Count == 0)
                {
                    runtimeCorpseToPredators.Remove(corpseId);
                }
            }
        }

        public List<KeyValuePair<int, int>> GetRuntimePredatorToCorpseSnapshot()
        {
            var result = new List<KeyValuePair<int, int>>();
            FillRuntimePredatorToCorpseSnapshot(result);
            return result;
        }

        public void FillRuntimePredatorToCorpseSnapshot(List<KeyValuePair<int, int>> result)
        {
            if (result == null)
            {
                return;
            }

            result.Clear();
            long now = Find.TickManager?.TicksGame ?? 0L;
            lock (dictLock)
            {
                foreach (var kv in runtimePredatorToCorpse)
                {
                    long key = PairKeyFor(kv.Key, kv.Value);
                    if (key == 0) continue;
                    if (pairsUntil.TryGetValue(key, out long until) && until >= now)
                    {
                        result.Add(kv);
                    }
                }
            }
        }

        

        public void RegisterPairFromKill(Pawn predator, Pawn killedPawn, int durationTicks = DEFAULT_PAIR_TICKS)
        {
            try
            {
                if (ZoologyModSettings.Instance != null && !ZoologyModSettings.Instance.EnablePredatorDefendCorpse) return;
                if (predator == null || killedPawn == null) return;

                try
                {
                    var ectoExt = DefModExtensionCache<ModExtension_Ectothermic>.Get(predator.def);
                    if (ectoExt != null) return;
                }
                catch { }

                Corpse corpse = killedPawn.Corpse ?? PredationLookupUtility.FindSpawnedCorpseForInnerPawn(killedPawn);

                if (corpse == null)
                {
                    Log.Warning($"Zoology: RegisterPairFromKill: corpse for killedPawn {killedPawn?.LabelShort ?? "null"} not found; skipping registration.");
                    return;
                }

                RegisterPairFromKill(predator, corpse, durationTicks);
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: RegisterPairFromKill (pawn) exception: {ex}");
            }
        }

        public void RegisterPairFromKill(Pawn predator, Corpse corpse, int durationTicks = DEFAULT_PAIR_TICKS)
        {
            try
            {
                if (predator == null || corpse == null) return;
                if (predator.Map == null || corpse.Map == null || predator.Map != corpse.Map) return;

                int corpseId = corpse.thingIDNumber;
                long now = Find.TickManager?.TicksGame ?? 0L;
                long until = now + durationTicks;

                FillPredatorsToRegister(predator, predatorsToRegisterBuffer);

                lock (dictLock)
                {
                    for (int i = 0; i < predatorsToRegisterBuffer.Count; i++)
                    {
                        var pred = predatorsToRegisterBuffer[i];
                        long key = PairKeyFor(pred.thingIDNumber, corpseId);
                        if (key == 0) continue;
                        pairsUntil[key] = until;
                        AddRuntimePairMapping(pred.thingIDNumber, corpseId);
                        if (inaccessibleSince.ContainsKey(key)) inaccessibleSince.Remove(key);
                    }
                }

                if (corpseId > 0)
                {
                    corpseLookupCache[corpseId] = corpse;
                    corpseNegativeLookupUntil.Remove(corpseId);
                }

                predatorsToRegisterBuffer.Clear();
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: RegisterPairFromKill (corpse) exception: {ex}");
                predatorsToRegisterBuffer.Clear();
            }
        }

        private void FillPredatorsToRegister(Pawn predator, List<Pawn> result)
        {
            result.Clear();
            if (predator == null) return;

            if (predator.RaceProps.herdAnimal)
            {
                var all = predator.Map.mapPawns.AllPawnsSpawned;
                long radSq = (long)HERD_RADIUS * HERD_RADIUS;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p == null) continue;
                    if (!p.RaceProps.predator) continue;
                    if (p.def != predator.def) continue;
                    if (p.Faction != predator.Faction) continue;
                    if ((p.Position - predator.Position).LengthHorizontalSquared <= radSq)
                        result.Add(p);
                }
            }
            else
            {
                result.Add(predator);
            }
        }

        

        private Pawn FindPawnById(int pid)
        {
            if (pid <= 0) return null;

            if (pawnLookupCache.TryGetValue(pid, out var cachedPawn))
            {
                if (cachedPawn != null && !cachedPawn.Destroyed && cachedPawn.Spawned && cachedPawn.Map != null && cachedPawn.thingIDNumber == pid)
                {
                    return cachedPawn;
                }
                pawnLookupCache.Remove(pid);
            }

            var maps = Find.Maps;
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var pawns = maps[mi].mapPawns.AllPawnsSpawned;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    var p = pawns[pi];
                    if (p != null && p.thingIDNumber == pid)
                    {
                        pawnLookupCache[pid] = p;
                        return p;
                    }
                }
            }

            pawnLookupCache.Remove(pid);
            return null;
        }

        private Corpse FindCorpseById(int cid, out bool foundNonSpawned)
        {
            foundNonSpawned = false;
            if (cid <= 0) return null;

            long now = Find.TickManager?.TicksGame ?? 0L;
            if (corpseNegativeLookupUntil.TryGetValue(cid, out long cooldownUntil) && cooldownUntil > now)
            {
                return null;
            }

            if (corpseLookupCache.TryGetValue(cid, out var cachedCorpse))
            {
                if (cachedCorpse != null && !cachedCorpse.Destroyed && cachedCorpse.thingIDNumber == cid)
                {
                    foundNonSpawned = !cachedCorpse.Spawned;
                    return cachedCorpse;
                }
                corpseLookupCache.Remove(cid);
            }

            var maps = Find.Maps;

            
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var corpses = maps[mi].listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                if (corpses == null)
                {
                    continue;
                }

                for (int ci = 0; ci < corpses.Count; ci++)
                {
                    if (corpses[ci] is Corpse corpse && corpse.thingIDNumber == cid)
                    {
                        corpseLookupCache[cid] = corpse;
                        corpseNegativeLookupUntil.Remove(cid);
                        return corpse;
                    }
                }
            }

            
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var pawns = maps[mi].mapPawns.AllPawnsSpawned;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    var p = pawns[pi];
                    if (p == null) continue;
                    try
                    {
                        var ct = p.carryTracker;
                        if (ct != null)
                        {
                            var carried = ct.CarriedThing;
                            if (carried != null && carried.thingIDNumber == cid)
                            {
                                foundNonSpawned = true;
                                if (carried is Corpse cCarried)
                                {
                                    corpseLookupCache[cid] = cCarried;
                                    corpseNegativeLookupUntil.Remove(cid);
                                    return cCarried;
                                }
                                return null;
                            }
                        }
                        var inv = p.inventory?.innerContainer;
                        if (inv != null)
                        {
                            for (int j = 0; j < inv.Count; j++)
                            {
                                var t2 = inv[j];
                                if (t2 != null && t2.thingIDNumber == cid)
                                {
                                    foundNonSpawned = true;
                                    if (t2 is Corpse cInv)
                                    {
                                        corpseLookupCache[cid] = cInv;
                                        corpseNegativeLookupUntil.Remove(cid);
                                        return cInv;
                                    }
                                    return null;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            corpseNegativeLookupUntil[cid] = now + NEGATIVE_LOOKUP_COOLDOWN_TICKS;
            return null;
        }

        public Pawn GetSpawnedPawnById(int pawnId)
        {
            return FindPawnById(pawnId);
        }

        public Corpse GetCorpseById(int corpseId, out bool foundNonSpawned)
        {
            return FindCorpseById(corpseId, out foundNonSpawned);
        }

        private bool IsCorpseSuitableForPredator(Pawn pred, Corpse corpse)
        {
            if (pred == null || corpse == null) return false;
            bool ingestible = false;
            try { ingestible = corpse.IngestibleNow; } catch { ingestible = false; }
            if (!ingestible) return false;

            bool willEat = false;
            try { willEat = pred.WillEat(corpse); } catch { willEat = false; }
            return willEat;
        }

        private void ClearInaccessibleFlagIfPresent(long key)
        {
            if (inaccessibleSince.ContainsKey(key)) inaccessibleSince.Remove(key);
        }

        private void FillPairSnapshot(List<KeyValuePair<long, long>> result)
        {
            result.Clear();
            lock (dictLock)
            {
                foreach (var kv in pairsUntil)
                {
                    result.Add(kv);
                }
            }
        }

        private void FillActivePredatorIdsForCorpse(int corpseId, long now, List<int> result)
        {
            result.Clear();
            if (corpseId <= 0) return;

            lock (dictLock)
            {
                if (!runtimeCorpseToPredators.TryGetValue(corpseId, out var predators) || predators == null || predators.Count == 0)
                {
                    return;
                }

                foreach (var pid in predators)
                {
                    long key = PairKeyFor(pid, corpseId);
                    if (key == 0) continue;
                    if (pairsUntil.TryGetValue(key, out long until) && until >= now)
                    {
                        result.Add(pid);
                    }
                }
            }
        }

        

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            try
            {
                if (ZoologyModSettings.Instance != null && !ZoologyModSettings.Instance.EnablePredatorDefendCorpse)
                {
                    return;
                }

                long now = Find.TickManager?.TicksGame ?? 0L;
                if (now % TICK_CHECK_INTERVAL != 0) return;

                pairRemovalBuffer.Clear();

                PredatorPresenceManager.TickPresence();

                FillPairSnapshot(pairSnapshotBuffer);

                for (int ei = 0; ei < pairSnapshotBuffer.Count; ei++)
                {
                    var kv = pairSnapshotBuffer[ei];
                    long key = kv.Key;
                    long until = kv.Value;

                    int cid = (int)((uint)(key & 0xFFFFFFFF));
                    int pid = (int)((uint)(key >> 32));

                    Pawn pred = FindPawnById(pid);
                    bool foundPred = pred != null;
                    bool predDead = pred != null && pred.Dead;

                    bool foundNonSpawned = false;
                    Corpse corpse = FindCorpseById(cid, out foundNonSpawned);
                    bool foundCorpse = corpse != null || foundNonSpawned;

                    bool remove = false;

                    if (!foundPred || predDead || !foundCorpse)
                    {
                        remove = true;
                    }
                    else if (corpse != null && corpse.IsDessicated())
                    {
                        remove = true;
                    }
                    else
                    {
                        if (foundNonSpawned && corpse != null)
                        {
                            ClearInaccessibleFlagIfPresent(key);
                            remove = false;
                        }
                        else
                        {
                            bool canReach = true;
                            try { canReach = pred.CanReach(corpse, PathEndMode.Touch, Danger.Deadly); } catch { canReach = true; }

                            if (!canReach)
                            {
                                
                                
                                try
                                {
                                    if (pred.InMentalState)
                                    {
                                        
                                        remove = false;
                                    }
                                    else
                                    {
                                        if (!inaccessibleSince.ContainsKey(key))
                                        {
                                            inaccessibleSince[key] = now;
                                            remove = false;
                                        }
                                        else
                                        {
                                            long firstTick = inaccessibleSince[key];
                                            long delta = now - firstTick;
                                            if (delta >= INACCESSIBLE_REMOVE_TICKS) remove = true;
                                            else remove = false;
                                        }
                                    }
                                }
                                catch
                                {
                                    
                                    if (!inaccessibleSince.ContainsKey(key))
                                    {
                                        inaccessibleSince[key] = now;
                                        remove = false;
                                    }
                                    else
                                    {
                                        long firstTick = inaccessibleSince[key];
                                        long delta = now - firstTick;
                                        if (delta >= INACCESSIBLE_REMOVE_TICKS) remove = true;
                                        else remove = false;
                                    }
                                }
                            }
                            else
                            {
                                bool suitable = IsCorpseSuitableForPredator(pred, corpse);
                                if (!suitable) remove = true;
                                else { ClearInaccessibleFlagIfPresent(key); remove = false; }
                            }
                        }
                    }

                    if (!remove && until < now)
                    {
                        if (foundNonSpawned || (corpse != null && IsCorpseSuitableForPredator(pred, corpse) && pred.Map == corpse.Map))
                        {
                            lock (dictLock)
                            {
                                pairsUntil[key] = now + DEFAULT_PAIR_TICKS;
                            }
                            ClearInaccessibleFlagIfPresent(key);
                            continue;
                        }
                        else
                        {
                            remove = true;
                        }
                    }

                    
                    if (!remove)
                    {
                        try
                        {
                            lock (dictLock)
                            {
                                AddRuntimePairMapping(pid, cid);
                            }
                        }
                        catch { /* fail-safe: не мешаем основной логике при ошибках */ }
                    }

                    if (remove)
                    {
                        pairRemovalBuffer.Add(key);
                        ClearInaccessibleFlagIfPresent(key);
                    }
                }

                if (pairRemovalBuffer.Count > 0)
                {
                    lock (dictLock)
                    {
                        for (int i = 0; i < pairRemovalBuffer.Count; i++)
                        {
                            var k = pairRemovalBuffer[i];
                            pairsUntil.Remove(k);
                            int pid = PredatorIdFromKey(k);
                            int cid = CorpseIdFromKey(k);
                            RemoveRuntimePairMapping(pid, cid);
                            if (inaccessibleSince.ContainsKey(k)) inaccessibleSince.Remove(k);
                        }
                    }
                }

                pairSnapshotBuffer.Clear();
                pairRemovalBuffer.Clear();
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: GameComponentTick exception: {ex}");
            }
        }

        private bool SafeWillEat(Pawn pred, Corpse c)
        {
            try { return pred.WillEat(c); } catch { return false; }
        }

        private bool SafeIngestibleNow(Corpse c)
        {
            try { return c.IngestibleNow; } catch { return false; }
        }

        

        public bool IsPaired(Pawn predator, Corpse corpse)
        {
            try
            {
                if (predator == null || corpse == null) return false;
                long key = PairKeyFor(predator.thingIDNumber, corpse.thingIDNumber);
                if (key == 0) return false;
                long now = Find.TickManager?.TicksGame ?? 0L;
                lock (dictLock)
                {
                    return pairsUntil.TryGetValue(key, out long until) && now <= until;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: IsPaired exception: {ex}");
                return false;
            }
        }

        public Corpse GetPairedCorpse(Pawn predator)
        {
            try
            {
                if (predator == null) return null;
                int predId = predator.thingIDNumber;
                int corpseId = 0;
                lock (dictLock)
                {
                    runtimePredatorToCorpse.TryGetValue(predId, out corpseId);
                }

                if (corpseId > 0)
                {
                    bool foundNonSpawned;
                    return FindCorpseById(corpseId, out foundNonSpawned);
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: GetPairedCorpse exception: {ex}");
                return null;
            }
        }

        public void RemovePair(Pawn predator, Corpse corpse)
        {
            try
            {
                if (predator == null || corpse == null) return;
                int pid = predator.thingIDNumber;
                int cid = corpse.thingIDNumber;
                long key = PairKeyFor(pid, cid);
                if (key == 0) return;
                lock (dictLock)
                {
                    if (pairsUntil.ContainsKey(key)) pairsUntil.Remove(key);
                    RemoveRuntimePairMapping(pid, cid);
                    if (inaccessibleSince.ContainsKey(key)) inaccessibleSince.Remove(key);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: RemovePair exception: {ex}");
            }
        }

        public Pawn GetOwnerOfCorpse(Corpse corpse)
        {
            try
            {
                if (corpse == null) return null;
                int cid = corpse.thingIDNumber;
                long now = Find.TickManager?.TicksGame ?? 0L;
                ownerPredatorIdBuffer.Clear();
                FillActivePredatorIdsForCorpse(cid, now, ownerPredatorIdBuffer);
                for (int i = 0; i < ownerPredatorIdBuffer.Count; i++)
                {
                    var owner = FindPawnById(ownerPredatorIdBuffer[i]);
                    if (owner != null)
                    {
                        ownerPredatorIdBuffer.Clear();
                        return owner;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: GetOwnerOfCorpse exception: {ex}");
            }
            ownerPredatorIdBuffer.Clear();
            return null;
        }

        

        private bool AreFactionsEffectivelySame(Pawn a, Pawn b)
        {
            try
            {
                if (a?.Faction == null || b?.Faction == null) return false;
                var fa = a.Faction;
                var fb = b.Faction;
                if (!object.ReferenceEquals(fa, fb)) return false;

                var defName = fa.def?.defName;
                if (!string.IsNullOrEmpty(defName) && string.Equals(defName, "Photonozoa", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool AreSameSpeciesOrCrossbreed(Pawn a, Pawn b)
        {
            try
            {
                if (a == null || b == null) return false;

                if (a.def != null && b.def != null && a.def == b.def) return true;

                var aRace = a.kindDef?.race;
                var bRace = b.kindDef?.race;
                if (aRace != null && bRace != null && aRace == bRace) return true;

                try
                {
                    var aCan = a.def?.race?.canCrossBreedWith;
                    if (aCan != null)
                    {
                        for (int i = 0; i < aCan.Count; i++)
                        {
                            var td = aCan[i];
                            if (td == null) continue;
                            if (td == b.def || string.Equals(td.defName, b.def?.defName, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                }
                catch { }

                try
                {
                    var bCan = b.def?.race?.canCrossBreedWith;
                    if (bCan != null)
                    {
                        for (int i = 0; i < bCan.Count; i++)
                        {
                            var td = bCan[i];
                            if (td == null) continue;
                            if (td == a.def || string.Equals(td.defName, a.def?.defName, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                }
                catch { }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool IsCorpseEffectivelyUnownedFor(Pawn eater, Corpse corpse)
        {
            try
            {
                if (corpse == null) return true;
                
                
                try
                {
                    if (eater != null)
                    {
                        if (IsPaired(eater, corpse)) return true;
                    }
                }
                catch { /* fail-safe: продолжим обычную логику при ошибках */ }

                var owner = GetOwnerOfCorpse(corpse);
                if (owner == null) return true;
                if (eater != null && owner == eater) return true;

                try { if (AreFactionsEffectivelySame(owner, eater)) return true; } catch { }

            
            try
            {
                
                float sizeMultiplier = 5f;
                try
                {
                    var settings = ZoologyModSettings.Instance;
                    if (settings != null && settings.CorpseUnownedSizeMultiplier > 0f)
                        sizeMultiplier = settings.CorpseUnownedSizeMultiplier;
                }
                catch { /* ignore settings errors */ }

                if (eater != null)
                {
                    float ownerSize = (owner?.BodySize ?? 1f);
                    float eaterSize = eater.BodySize;
                    if (ownerSize <= 0f) ownerSize = 1f;
                    if (eaterSize <= 0f) eaterSize = 1f;

                    if (eaterSize * sizeMultiplier <= ownerSize)
                    {
                        return true;
                    }
                }
            }
            catch (Exception exSize)
            {
                Log.Warning($"Zoology: IsCorpseEffectivelyUnownedFor size-check failed: {exSize}");
                
            }

            try
            {
                if (owner?.RaceProps?.herdAnimal == true)
                {
                    ThingDef ownerKindRace = owner.kindDef?.race;
                    ThingDef eaterKindRace = eater?.kindDef?.race;
                    if (ownerKindRace != null && eaterKindRace != null && ownerKindRace == eaterKindRace)
                    {
                        return true;
                    }
                    if (owner.def != null && eater?.def != null && owner.def == eater.def)
                    {
                        return true;
                    }
                    var ownerDefRaceProp = owner.def?.race;
                    var eaterDefRaceProp = eater?.def?.race;
                    if (ownerDefRaceProp != null && eaterDefRaceProp != null && ownerDefRaceProp == eaterDefRaceProp)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                
            }

                try
                {
                    var innerDef = corpse.InnerPawn?.def;
                    if (innerDef != null && eater != null)
                    {
                        var canList = innerDef.race?.canCrossBreedWith;
                        if (canList != null)
                        {
                            for (int i = 0; i < canList.Count; i++)
                            {
                                var el = canList[i];
                                if (el == null) continue;
                                if (el == eater.def) return true;
                                if (string.Equals(el.defName, eater.def?.defName, StringComparison.OrdinalIgnoreCase)) return true;
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    float ownerPower = 1f, eaterPower = 1f;
                    if (owner != null && owner.kindDef != null) ownerPower = owner.kindDef.combatPower;
                    if (eater != null && eater.kindDef != null) eaterPower = eater.kindDef.combatPower;
                    if (ownerPower <= 0f) ownerPower = 1f;
                    if (eaterPower <= 0f) eaterPower = 1f;
                    if (eaterPower >= ownerPower * 1.3f) return true;
                }
                catch { }

                try
                {
                    if (owner?.RaceProps?.herdAnimal == true && AreSameSpeciesOrCrossbreed(owner, eater))
                        return true;
                }
                catch { /* ignore */ }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: IsCorpseEffectivelyUnownedFor exception: {ex}");
                return true;
            }
        }

        
        public static void MarkProtectionNotificationSentForCorpse(int corpseThingID)
        {
            try
            {
                long now = Find.TickManager?.TicksGame ?? 0L;
                lock (dictLock)
                {
                    notificationSuppressedUntil[corpseThingID] = now + NOTIFICATION_SUPPRESSION_TICKS;
                }
            }
            catch { }
        }

        public static bool IsProtectionNotificationSuppressedForCorpse(int corpseThingID)
        {
            try
            {
                long now = Find.TickManager?.TicksGame ?? 0L;
                lock (dictLock)
                {
                    long until;
                    if (notificationSuppressedUntil.TryGetValue(corpseThingID, out until))
                    {
                        if (until >= now) return true;
                        notificationSuppressedUntil.Remove(corpseThingID);
                    }
                }
            }
            catch { }
            return false;
        }

        private static readonly System.Reflection.PropertyInfo RacePropsIsMechanoidProperty =
            AccessTools.Property(typeof(RaceProperties), "IsMechanoid");

        private static bool IsHumanlikeOrMechanoid(Pawn pawn)
        {
            if (pawn?.RaceProps == null)
            {
                return false;
            }

            if (pawn.RaceProps.Humanlike)
            {
                return true;
            }

            if (RacePropsIsMechanoidProperty == null)
            {
                return false;
            }

            try
            {
                object value = RacePropsIsMechanoidProperty.GetValue(pawn.RaceProps, null);
                return value is bool isMechanoid && isMechanoid;
            }
            catch
            {
                return false;
            }
        }

        public void TryTriggerDefendFor(Corpse corpse, Pawn interrupter)
        {
            try
            {
                ZoologyModSettings settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnablePredatorDefendCorpse) return;
                if (corpse == null) return;
                if (interrupter == null || !interrupter.Spawned || interrupter.Dead) return;

                bool humanlikeOrMechanoidInterrupter = IsHumanlikeOrMechanoid(interrupter);
                if (humanlikeOrMechanoidInterrupter && settings != null && !settings.EnablePredatorDefendPreyFromHumansAndMechanoids)
                {
                    return;
                }

                
                if (interrupter != null && IsCorpseEffectivelyUnownedFor(interrupter, corpse))
                    return;

                int cid = corpse.thingIDNumber;
                long now = Find.TickManager?.TicksGame ?? 0L;
                int triggerRangeSquared = PreyProtectionUtility.GetProtectionRangeSquared();

                if (!PreyProtectionUtility.TryGetProtectionAnchor(corpse, interrupter, out Map corpseMap, out IntVec3 protectionAnchor))
                {
                    return;
                }

                if (!PreyProtectionUtility.IsPawnWithinProtectionRange(interrupter, corpseMap, protectionAnchor, triggerRangeSquared))
                {
                    return;
                }

                FillActivePredatorIdsForCorpse(cid, now, activePredatorIdBuffer);
                if (activePredatorIdBuffer.Count == 0) return;

                defendCandidateBuffer.Clear();
                var protectJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_ProtectPrey");

                for (int i = 0; i < activePredatorIdBuffer.Count; i++)
                {
                    int pairPid = activePredatorIdBuffer[i];
                    long pairKey = PairKeyFor(pairPid, cid);

                    Pawn pred = null;
                    try { pred = FindPawnById(pairPid); } catch { pred = null; }
                    if (pred == null) continue;
                    if (interrupter != null && pred == interrupter) continue;
                    if (humanlikeOrMechanoidInterrupter && !ZoologyModSettings.CanPredatorDefendPreyFromHumansAndMechanoidsNow(pred)) continue;

                    try
                    {
                        
                        if (interrupter != null)
                        {
                            if (AreFactionsEffectivelySame(pred, interrupter)) continue;
                            if (pred?.RaceProps?.herdAnimal == true && AreSameSpeciesOrCrossbreed(pred, interrupter)) continue;
                        }
                    }
                    catch { /* ignore */ }

                    try
                    {
                        
                        if (pred.Dead || pred.Destroyed || pred.Downed || pred.InMentalState || !pred.Spawned || pred.GetLord() != null) continue;

                        
                        if (corpseMap != null && pred.Map != corpseMap) continue;
                        if (!PreyProtectionUtility.IsPawnWithinProtectionRange(pred, corpseMap, protectionAnchor, triggerRangeSquared)) continue;

                        
                        if (pairKey != 0)
                        {
                            long lastAttempt = 0;
                            if (lastTriggerAttempt.TryGetValue(pairKey, out lastAttempt))
                            {
                                if (now - lastAttempt < TRIGGER_COOLDOWN_TICKS) continue; 
                            }
                        }

                        
                        bool canReachInterrupter = true;
                        try { canReachInterrupter = pred.CanReach(interrupter, PathEndMode.Touch, Danger.Deadly); } catch { canReachInterrupter = true; }
                        if (!canReachInterrupter) continue;

                        
                        var cur = pred.CurJob;
                        var curDriver = pred.jobs?.curDriver;
                        if (cur != null)
                        {
                            if (cur.def == JobDefOf.AttackMelee) continue;
                            if (protectJobDef != null && cur.def == protectJobDef) continue;
                            if (curDriver != null && curDriver.GetType().Name == "JobDriver_ProtectPrey") continue;
                        }

                        defendCandidateBuffer.Add(new DefendCandidate(pairPid, pred, pairKey));
                    }
                    catch (Exception innerEx)
                    {
                        Log.Warning($"Zoology: TryTriggerDefendFor inner loop error for pid={pairPid}: {innerEx}");
                    }
                }

                if (defendCandidateBuffer.Count == 0) return;

                
                
                bool needAggregateNotification = defendCandidateBuffer.Count > 1 && interrupter != null && interrupter.Faction == Faction.OfPlayer;

                if (needAggregateNotification)
                {
                    try
                    {
                        Pawn exemplar = defendCandidateBuffer[0].Predator;
                        string label = "LetterLabelPredatorProtectingPreyPack".Translate(exemplar.GetKindLabelPlural(), exemplar.Named("PREDATOR"));
                        string text = "LetterPredatorProtectingPreyPack".Translate(exemplar.GetKindLabelPlural(), interrupter.LabelDefinite(), exemplar.Named("PREDATOR"), interrupter.Named("PREY"));

                        
                        if (label.NullOrEmpty() || label.Contains("LetterLabelPredatorProtectingPreyPack"))
                            label = $"{exemplar.GetKindLabelPlural()} is protecting its prey";
                        if (text.NullOrEmpty() || text.Contains("LetterPredatorProtectingPreyPack"))
                            text = $"{exemplar.GetKindLabelPlural()} are protecting their prey and are attacking {interrupter.LabelDefinite()}.";

                        if (interrupter.RaceProps.Humanlike)
                        {
                            Find.LetterStack.ReceiveLetter(label.CapitalizeFirst(), text.CapitalizeFirst(), LetterDefOf.ThreatBig, exemplar, null, null, null, null, 0, true);
                        }
                        else
                        {
                            Messages.Message(text.CapitalizeFirst(), exemplar, MessageTypeDefOf.ThreatBig, true);
                        }

                        
                        MarkProtectionNotificationSentForCorpse(cid);
                    }
                    catch (Exception exNotify)
                    {
                        Log.Warning($"Zoology: aggregated notify failed: {exNotify}");
                    }
                }

                
                for (int i = 0; i < defendCandidateBuffer.Count; i++)
                {
                    int pid = defendCandidateBuffer[i].PredatorId;
                    Pawn pred = defendCandidateBuffer[i].Predator;
                    long pairKey = defendCandidateBuffer[i].PairKey;

                    try
                    {
                        bool taken = false;
                        if (protectJobDef != null)
                        {
                            Job protectJob = JobMaker.MakeJob(protectJobDef, interrupter, corpse);
                            taken = pred.jobs.TryTakeOrderedJob(protectJob);
                        }

                        if (!taken)
                        {
                            Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, interrupter);
                            pred.jobs.TryTakeOrderedJob(attackJob);
                        }
                    }
                    catch (Exception exJob)
                    {
                        Log.Warning($"Zoology: TryTriggerDefendFor: failed to order job for predator (pid={pid}): {exJob}");
                    }

                    
                    try
                    {
                        if (pairKey != 0)
                        {
                            lastTriggerAttempt[pairKey] = now;
                        }
                    }
                    catch { }
                }

                activePredatorIdBuffer.Clear();
                defendCandidateBuffer.Clear();
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: TryTriggerDefendFor exception: {ex}");
                activePredatorIdBuffer.Clear();
                defendCandidateBuffer.Clear();
            }
        }

        
        
        
        
        public List<Corpse> GetActivePairedCorpses(Pawn predator)
        {
            var result = new List<Corpse>();
            if (predator == null) return result;

            int pid = predator.thingIDNumber;
            int cid = 0;
            lock (dictLock)
            {
                runtimePredatorToCorpse.TryGetValue(pid, out cid);
            }

            if (cid > 0)
            {
                bool foundNonSpawned;
                var c = FindCorpseById(cid, out foundNonSpawned);
                if (c != null) result.Add(c);
            }

            return result;
        }

        private readonly struct DefendCandidate
        {
            public readonly int PredatorId;
            public readonly Pawn Predator;
            public readonly long PairKey;

            public DefendCandidate(int predatorId, Pawn predator, long pairKey)
            {
                PredatorId = predatorId;
                Predator = predator;
                PairKey = pairKey;
            }
        }
    }
}
