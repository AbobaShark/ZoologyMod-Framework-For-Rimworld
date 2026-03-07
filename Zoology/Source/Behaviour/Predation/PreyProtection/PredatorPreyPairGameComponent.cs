

using System;
using System.Collections.Generic;
using System.Linq;
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

        
        private static Dictionary<long, long> inaccessibleSince = new Dictionary<long, long>();
        
        private static Dictionary<long, long> lastTriggerAttempt = new Dictionary<long, long>();
        private const int TRIGGER_COOLDOWN_TICKS = 250; 
        private static int TRIGGER_MAX_DISTANCE => (ZoologyModSettings.Instance != null && ZoologyModSettings.Instance.EnablePredatorDefendCorpse) ? ZoologyModSettings.Instance.PreyProtectionRange : 20;
        private static float TRIGGER_MAX_DISTANCE_SQ => (float)TRIGGER_MAX_DISTANCE * (float)TRIGGER_MAX_DISTANCE;

        private Game owningGame;
        private static object dictLock = new object();
        private static PredatorPreyPairGameComponent singleton;

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
                    inaccessibleSince = new Dictionary<long, long>();

                    long now = Find.TickManager?.TicksGame ?? 0L;

                    
                    
                    KeyValuePair<long,long>[] entries;
                    lock (dictLock)
                    {
                        entries = pairsUntil.ToArray();
                    }

                    for (int i = 0; i < entries.Length; i++)
                    {
                        var kv = entries[i];
                        long key = kv.Key;
                        long until = kv.Value;
                        if (until < now) continue;
                        int pid = (int)((uint)(key >> 32));
                        int cid = (int)((uint)(key & 0xFFFFFFFF));
                        if (pid > 0 && cid > 0)
                        {
                            runtimePredatorToCorpse[pid] = cid;
                        }
                    }
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

        

        public void RegisterPairFromKill(Pawn predator, Pawn killedPawn, int durationTicks = DEFAULT_PAIR_TICKS)
        {
            try
            {
                if (ZoologyModSettings.Instance != null && !ZoologyModSettings.Instance.EnablePredatorDefendCorpse) return;
                if (predator == null || killedPawn == null) return;

                try
                {
                    var ectoExt = predator.def.GetModExtension<ModExtension_Ectothermic>();
                    if (ectoExt != null) return;
                }
                catch { }

                Corpse corpse = killedPawn.Corpse;
                if (corpse == null)
                {
                    var maps = Find.Maps;
                    for (int mi = 0; mi < maps.Count; mi++)
                    {
                        var all = maps[mi].listerThings.AllThings;
                        for (int ti = 0; ti < all.Count; ti++)
                        {
                            var c = all[ti] as Corpse;
                            if (c != null && c.InnerPawn == killedPawn) { corpse = c; break; }
                        }
                        if (corpse != null) break;
                    }
                }

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

                List<Pawn> predatorsToRegister = CollectPredatorsToRegister(predator);

                lock (dictLock)
                {
                    for (int i = 0; i < predatorsToRegister.Count; i++)
                    {
                        var pred = predatorsToRegister[i];
                        long key = PairKeyFor(pred.thingIDNumber, corpseId);
                        if (key == 0) continue;
                        pairsUntil[key] = until;
                        runtimePredatorToCorpse[pred.thingIDNumber] = corpseId;
                        if (inaccessibleSince.ContainsKey(key)) inaccessibleSince.Remove(key);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: RegisterPairFromKill (corpse) exception: {ex}");
            }
        }

        private List<Pawn> CollectPredatorsToRegister(Pawn predator)
        {
            var list = new List<Pawn>();
            if (predator == null) return list;

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
                        list.Add(p);
                }
            }
            else
            {
                list.Add(predator);
            }

            return list;
        }

        

        private Pawn FindPawnById(int pid)
        {
            if (pid <= 0) return null;
            var maps = Find.Maps;
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var pawns = maps[mi].mapPawns.AllPawnsSpawned;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    var p = pawns[pi];
                    if (p != null && p.thingIDNumber == pid) return p;
                }
            }
            return null;
        }

        private Corpse FindCorpseById(int cid, out bool foundNonSpawned)
        {
            foundNonSpawned = false;
            if (cid <= 0) return null;

            var maps = Find.Maps;

            
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var all = maps[mi].listerThings.AllThings;
                for (int ti = 0; ti < all.Count; ti++)
                {
                    var t = all[ti];
                    if (t != null && t.thingIDNumber == cid && t is Corpse c) return c;
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
                                if (carried is Corpse cCarried) return cCarried;
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
                                    if (t2 is Corpse cInv) return cInv;
                                    return null;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
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

                List<long> toRemove = new List<long>();

                PredatorPresenceManager.TickPresence();

                KeyValuePair<long, long>[] entries;
                lock (dictLock)
                {
                    entries = pairsUntil.ToArray(); 
                }

                for (int ei = 0; ei < entries.Length; ei++)
                {
                    var kv = entries[ei];
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
                                
                                if (!runtimePredatorToCorpse.ContainsKey(pid) || runtimePredatorToCorpse[pid] != cid)
                                    runtimePredatorToCorpse[pid] = cid;
                            }
                        }
                        catch { /* fail-safe: не мешаем основной логике при ошибках */ }
                    }

                    if (remove)
                    {
                        toRemove.Add(key);
                        ClearInaccessibleFlagIfPresent(key);
                    }
                }

                if (toRemove.Count > 0)
                {
                    lock (dictLock)
                    {
                        for (int i = 0; i < toRemove.Count; i++)
                        {
                            var k = toRemove[i];
                            pairsUntil.Remove(k);
                            int pid = (int)((uint)(k >> 32));
                            if (runtimePredatorToCorpse.ContainsKey(pid)) runtimePredatorToCorpse.Remove(pid);
                            if (inaccessibleSince.ContainsKey(k)) inaccessibleSince.Remove(k);
                        }
                    }
                }
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
                if (runtimePredatorToCorpse.TryGetValue(predId, out int corpseId))
                {
                    var maps = Find.Maps;
                    for (int mi = 0; mi < maps.Count; mi++)
                    {
                        var all = maps[mi].listerThings.AllThings;
                        for (int ti = 0; ti < all.Count; ti++)
                        {
                            var t = all[ti];
                            if (t != null && t.thingIDNumber == corpseId && t is Corpse c) return c;
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
                                if (ct != null && ct.CarriedThing != null && ct.CarriedThing.thingIDNumber == corpseId)
                                {
                                    if (ct.CarriedThing is Corpse cCarried) return cCarried;
                                }
                                var inv = p.inventory?.innerContainer;
                                if (inv != null)
                                {
                                    for (int j = 0; j < inv.Count; j++)
                                    {
                                        var t2 = inv[j];
                                        if (t2 != null && t2.thingIDNumber == corpseId && t2 is Corpse cInv) return cInv;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                long now = Find.TickManager?.TicksGame ?? 0L;
                KeyValuePair<long,long>[] snapshot;
                lock (dictLock)
                {
                    snapshot = pairsUntil.ToArray();
                }

                for (int k = 0; k < snapshot.Length; k++)
                {
                    var kv = snapshot[k];
                    long key = kv.Key;
                    int pid = (int)((uint)(key >> 32));
                    int cid = (int)((uint)(key & 0xFFFFFFFF));
                    if (pid == predId && kv.Value >= now)
                    {
                        var maps = Find.Maps;
                        for (int mi = 0; mi < maps.Count; mi++)
                        {
                            var all = maps[mi].listerThings.AllThings;
                            for (int ti = 0; ti < all.Count; ti++)
                            {
                                var t = all[ti];
                                if (t != null && t.thingIDNumber == cid && t is Corpse cCorpse) return cCorpse;
                            }

                            var pawns = maps[mi].mapPawns.AllPawnsSpawned;
                            for (int pi = 0; pi < pawns.Count; pi++)
                            {
                                var p = pawns[pi];
                                if (p == null) continue;
                                try
                                {
                                    var ct = p.carryTracker;
                                    if (ct != null && ct.CarriedThing != null && ct.CarriedThing.thingIDNumber == cid)
                                    {
                                        if (ct.CarriedThing is Corpse cCarried) return cCarried;
                                    }
                                    var inv = p.inventory?.innerContainer;
                                    if (inv != null)
                                    {
                                        for (int j = 0; j < inv.Count; j++)
                                        {
                                            var it = inv[j];
                                            if (it != null && it.thingIDNumber == cid && it is Corpse cInv) return cInv;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
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
                long key = PairKeyFor(predator.thingIDNumber, corpse.thingIDNumber);
                if (key == 0) return;
                lock (dictLock)
                {
                    if (pairsUntil.ContainsKey(key)) pairsUntil.Remove(key);
                    if (runtimePredatorToCorpse.ContainsKey(predator.thingIDNumber)) runtimePredatorToCorpse.Remove(predator.thingIDNumber);
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
                KeyValuePair<long,long>[] snapshot;
                lock (dictLock)
                {
                    snapshot = pairsUntil.ToArray();
                }

                for (int i = 0; i < snapshot.Length; i++)
                {
                    var kv = snapshot[i];
                    long key = kv.Key;
                    long until = kv.Value;
                    if (until < now) continue; 
                    int pairCid = (int)((uint)(key & 0xFFFFFFFF));
                    if (pairCid != cid) continue;
                    int pairPid = (int)((uint)(key >> 32));
                    var owner = FindPawnById(pairPid);
                    if (owner != null) return owner;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: GetOwnerOfCorpse exception: {ex}");
            }
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

        public void TryTriggerDefendFor(Corpse corpse, Pawn interrupter)
        {
            try
            {
                if (ZoologyModSettings.Instance != null && !ZoologyModSettings.Instance.EnablePredatorDefendCorpse) return;
                if (corpse == null) return;

                
                if (interrupter != null && IsCorpseEffectivelyUnownedFor(interrupter, corpse))
                    return;

                int cid = corpse.thingIDNumber;
                long now = Find.TickManager?.TicksGame ?? 0L;

                Map corpseMap = corpse.Map;
                if (corpseMap == null)
                {
                    var maps = Find.Maps;
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
                                if (ct != null && ct.CarriedThing != null && ct.CarriedThing.thingIDNumber == cid) { corpseMap = p.Map; break; }
                                var inv = p.inventory?.innerContainer;
                                if (inv != null)
                                {
                                    for (int j = 0; j < inv.Count; j++)
                                    {
                                        var t = inv[j];
                                        if (t != null && t.thingIDNumber == cid) { corpseMap = p.Map; break; }
                                    }
                                    if (corpseMap != null) break;
                                }
                            }
                            catch { }
                        }
                        if (corpseMap != null) break;
                    }
                }

                if (corpseMap == null && interrupter != null && interrupter.Map != null) corpseMap = interrupter.Map;

                KeyValuePair<long,long>[] snapshot;
                lock (dictLock) { snapshot = pairsUntil.ToArray(); }

                var candidatePredatorPairs = new List<Tuple<int, Pawn, long>>(); 

                
                for (int i = 0; i < snapshot.Length; i++)
                {
                    var kv = snapshot[i];
                    long key = kv.Key;
                    long until = kv.Value;
                    if (until < now) continue;
                    int pairCid = (int)((uint)(key & 0xFFFFFFFF));
                    if (pairCid != cid) continue;
                    int pairPid = (int)((uint)(key >> 32));

                    Pawn pred = null;
                    try { pred = FindPawnById(pairPid); } catch { pred = null; }
                    if (pred == null) continue;
                    if (interrupter != null && pred == interrupter) continue;

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

                        
                        long pairKey = PairKeyFor(pairPid, cid);
                        if (pairKey != 0)
                        {
                            long lastAttempt = 0;
                            if (lastTriggerAttempt.TryGetValue(pairKey, out lastAttempt))
                            {
                                if (now - lastAttempt < TRIGGER_COOLDOWN_TICKS) continue; 
                            }
                        }

                        
                        bool distanceOk = false;
                        try
                        {
                            if (corpse != null && corpse.Spawned)
                                distanceOk = pred.Position.DistanceToSquared(corpse.Position) <= TRIGGER_MAX_DISTANCE_SQ;
                            else if (interrupter != null && interrupter.Spawned)
                                distanceOk = pred.Position.DistanceToSquared(interrupter.Position) <= TRIGGER_MAX_DISTANCE_SQ;
                            else
                                distanceOk = false;
                        }
                        catch { distanceOk = false; }

                        if (!distanceOk) continue;

                        
                        bool canReachInterrupter = true;
                        try { canReachInterrupter = pred.CanReach(interrupter, PathEndMode.Touch, Danger.Deadly); } catch { canReachInterrupter = true; }
                        if (!canReachInterrupter) continue;

                        
                        var cur = pred.CurJob;
                        var curDriver = pred.jobs?.curDriver;
                        var protectJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_ProtectPrey");
                        if (cur != null)
                        {
                            if (cur.def == JobDefOf.AttackMelee) continue;
                            if (protectJobDef != null && cur.def == protectJobDef) continue;
                            if (curDriver != null && curDriver.GetType().Name == "JobDriver_ProtectPrey") continue;
                        }

                        
                        long storedPairKey = PairKeyFor(pairPid, cid);
                        candidatePredatorPairs.Add(new Tuple<int, Pawn, long>(pairPid, pred, storedPairKey));
                    }
                    catch (Exception innerEx)
                    {
                        Log.Warning($"Zoology: TryTriggerDefendFor inner loop error for pid={pairPid}: {innerEx}");
                    }
                }

                if (candidatePredatorPairs.Count == 0) return;

                
                
                bool needAggregateNotification = candidatePredatorPairs.Count > 1 && interrupter != null && interrupter.Faction == Faction.OfPlayer;

                if (needAggregateNotification)
                {
                    try
                    {
                        Pawn exemplar = candidatePredatorPairs[0].Item2;
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

                
                for (int i = 0; i < candidatePredatorPairs.Count; i++)
                {
                    int pid = candidatePredatorPairs[i].Item1;
                    Pawn pred = candidatePredatorPairs[i].Item2;
                    long pairKey = candidatePredatorPairs[i].Item3;

                    try
                    {
                        bool taken = false;
                        var protectJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_ProtectPrey");
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
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: TryTriggerDefendFor exception: {ex}");
            }
        }

        
        
        
        
        public List<Corpse> GetActivePairedCorpses(Pawn predator)
        {
            var result = new List<Corpse>();
            if (predator == null) return result;

            int pid = predator.thingIDNumber;
            long now = Find.TickManager?.TicksGame ?? 0L;

            var idList = new List<KeyValuePair<int, long>>(8); 

            lock (dictLock)
            {
                foreach (var kv in pairsUntil)
                {
                    long pairKey = kv.Key;
                    long until = kv.Value;
                    if (until < now) continue;
                    int pairPid = (int)((uint)(pairKey >> 32));
                    if (pairPid != pid) continue;
                    int cid = (int)((uint)(pairKey & 0xFFFFFFFF));
                    idList.Add(new KeyValuePair<int, long>(cid, until));
                }
            }

            if (idList.Count == 0) return result;

            idList.Sort((a, b) => a.Value.CompareTo(b.Value));

            for (int i = 0; i < idList.Count; i++)
            {
                bool foundNonSpawned;
                var c = FindCorpseById(idList[i].Key, out foundNonSpawned);
                if (c != null) result.Add(c);
            }

            return result;
        }
    }
}
