using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public static class ScavengerEatingContext
    {
        [ThreadStatic] private static Dictionary<Pawn, Thing> pawnToTarget;
        [ThreadStatic] private static Dictionary<int, Pawn> corpseToPawn;
        [ThreadStatic] private static Dictionary<int, HandFeedEntry> handFedByCorpseId;
        [ThreadStatic] private static List<int> handCleanupBuffer;
        [ThreadStatic] private static HashSet<int> recoveredMapIds;
        [ThreadStatic] private static Pawn forcePawn;
        [ThreadStatic] private static int forceCorpseId;
        [ThreadStatic] private static int forceTick;
        private static Game runtimeGame;
        private static int runtimeLastTick = -1;
        private static int changeVersion = 1;

        private struct HandFeedEntry
        {
            public Pawn Pawn;
            public int Tick;
        }

        private static Dictionary<Pawn, Thing> SelfMap => pawnToTarget ?? (pawnToTarget = new Dictionary<Pawn, Thing>());
        private static Dictionary<int, Pawn> CorpseMap => corpseToPawn ?? (corpseToPawn = new Dictionary<int, Pawn>(64));
        private static Dictionary<int, HandFeedEntry> HandMap => handFedByCorpseId ?? (handFedByCorpseId = new Dictionary<int, HandFeedEntry>(32));
        private static List<int> HandCleanup => handCleanupBuffer ?? (handCleanupBuffer = new List<int>(8));
        private static HashSet<int> RecoveredMaps => recoveredMapIds ?? (recoveredMapIds = new HashSet<int>());
        public static int ChangeVersion => changeVersion;

        private static void MarkChanged()
        {
            if (changeVersion == int.MaxValue)
            {
                changeVersion = 1;
            }
            else
            {
                changeVersion++;
            }
        }

        private static void ResetRuntimeState(Game currentGame, int currentTick)
        {
            pawnToTarget?.Clear();
            corpseToPawn?.Clear();
            handFedByCorpseId?.Clear();
            handCleanupBuffer?.Clear();
            recoveredMapIds?.Clear();
            forcePawn = null;
            forceCorpseId = 0;
            forceTick = 0;
            runtimeGame = currentGame;
            runtimeLastTick = currentTick;
            MarkChanged();
        }

        private static void EnsureRuntimeState()
        {
            Game currentGame = Current.Game;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            bool gameChanged = !ReferenceEquals(runtimeGame, currentGame);
            bool tickRewound = currentTick > 0 && runtimeLastTick > 0 && currentTick < runtimeLastTick;
            if (gameChanged || tickRewound)
            {
                ResetRuntimeState(currentGame, currentTick);
                return;
            }

            if (currentTick > 0)
            {
                runtimeLastTick = currentTick;
            }
        }

        private static Corpse TryGetCorpseTarget(Job job)
        {
            if (job == null)
            {
                return null;
            }

            if (job.targetA.Thing is Corpse corpseA)
            {
                return corpseA;
            }

            if (job.targetB.Thing is Corpse corpseB)
            {
                return corpseB;
            }

            if (job.targetC.Thing is Corpse corpseC)
            {
                return corpseC;
            }

            return null;
        }

        public static void EnsureMapRecovered(Map map)
        {
            try
            {
                EnsureRuntimeState();
                if (map == null)
                {
                    return;
                }

                HashSet<int> recovered = RecoveredMaps;
                if (recovered.Contains(map.uniqueID))
                {
                    return;
                }

                IReadOnlyList<Pawn> pawns = map.mapPawns?.AllPawnsSpawned;
                if (pawns == null || pawns.Count == 0)
                {
                    return;
                }

                bool anyRecovered = false;
                var selfMap = SelfMap;
                var reverseMap = CorpseMap;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null || pawn.Dead || pawn.Destroyed)
                    {
                        continue;
                    }

                    if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out _))
                    {
                        continue;
                    }

                    Job job = pawn.CurJob;
                    if (job == null || job.def != JobDefOf.Ingest)
                    {
                        continue;
                    }

                    Corpse corpse = TryGetCorpseTarget(job);
                    if (corpse == null || corpse.Destroyed || corpse.Bugged)
                    {
                        continue;
                    }

                    if (selfMap.TryGetValue(pawn, out Thing oldTarget) && oldTarget is Corpse oldCorpse)
                    {
                        if (reverseMap.TryGetValue(oldCorpse.thingIDNumber, out Pawn oldPawn) && ReferenceEquals(oldPawn, pawn))
                        {
                            reverseMap.Remove(oldCorpse.thingIDNumber);
                        }
                    }

                    selfMap[pawn] = corpse;
                    reverseMap[corpse.thingIDNumber] = pawn;
                    anyRecovered = true;
                }

                if (anyRecovered)
                {
                    MarkChanged();
                }

                recovered.Add(map.uniqueID);
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.EnsureMapRecovered: " + e);
            }
        }

        public static bool HasAnyActiveEatingContext()
        {
            try
            {
                EnsureRuntimeState();
                int now = Find.TickManager?.TicksGame ?? 0;
                if (forcePawn != null && !forcePawn.Dead && !forcePawn.Destroyed && now - forceTick <= 1)
                {
                    return true;
                }

                if (corpseToPawn != null && corpseToPawn.Count > 0)
                {
                    return true;
                }

                if (handFedByCorpseId != null && handFedByCorpseId.Count > 0)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.HasAnyActiveEatingContext: " + e);
            }

            return false;
        }

        public static void SetEating(Pawn pawn, Thing target)
        {
            try
            {
                EnsureRuntimeState();
                if (pawn == null) return;
                if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out _)) return;

                var map = SelfMap;
                var reverse = CorpseMap;
                if (map.TryGetValue(pawn, out Thing oldTarget) && oldTarget is Corpse oldCorpse)
                {
                    if (reverse.TryGetValue(oldCorpse.thingIDNumber, out Pawn oldPawn) && ReferenceEquals(oldPawn, pawn))
                    {
                        reverse.Remove(oldCorpse.thingIDNumber);
                    }
                }

                map[pawn] = target;
                if (target is Corpse corpse)
                {
                    reverse[corpse.thingIDNumber] = pawn;
                }

                MarkChanged();
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.SetEating: " + e);
            }
        }

        public static void SetHandFeeding(Pawn pawn, Thing target)
        {
            try
            {
                EnsureRuntimeState();
                if (pawn == null) return;
                if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out _)) return;

                var corpse = target as Corpse;
                if (corpse == null) return;

                int now = Find.TickManager?.TicksGame ?? 0;
                HandMap[corpse.thingIDNumber] = new HandFeedEntry { Pawn = pawn, Tick = now };
                MarkChanged();
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.SetHandFeeding: " + e);
            }
        }

        public static void SetForceIngestible(Pawn pawn, Thing target)
        {
            try
            {
                EnsureRuntimeState();
                if (pawn == null) return;
                if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out _)) return;

                var corpse = target as Corpse;
                if (corpse == null) return;

                forcePawn = pawn;
                forceCorpseId = corpse.thingIDNumber;
                forceTick = Find.TickManager?.TicksGame ?? 0;
                MarkChanged();
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.SetForceIngestible: " + e);
            }
        }

        public static bool TryGetForcedPawnForCorpse(Corpse corpse, out Pawn pawn)
        {
            pawn = null;
            try
            {
                EnsureRuntimeState();
                if (corpse == null) return false;
                int now = Find.TickManager?.TicksGame ?? 0;
                if (forcePawn == null || forceCorpseId != corpse.thingIDNumber) return false;
                if (now - forceTick > 1) return false;
                if (forcePawn.Dead || forcePawn.Destroyed) return false;

                pawn = forcePawn;
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.TryGetForcedPawnForCorpse: " + e);
                pawn = null;
                return false;
            }
        }

        public static void Clear(Pawn pawn)
        {
            try
            {
                EnsureRuntimeState();
                if (pawn == null) return;
                var map = pawnToTarget;
                if (map == null) return;
                if (map.TryGetValue(pawn, out Thing oldTarget) && oldTarget is Corpse oldCorpse)
                {
                    var reverse = corpseToPawn;
                    if (reverse != null
                        && reverse.TryGetValue(oldCorpse.thingIDNumber, out Pawn oldPawn)
                        && ReferenceEquals(oldPawn, pawn))
                    {
                        reverse.Remove(oldCorpse.thingIDNumber);
                    }
                }

                if (map.Remove(pawn))
                {
                    MarkChanged();
                }
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.Clear: " + e);
            }
        }

        public static void ClearHandFeeding(Pawn pawn)
        {
            try
            {
                EnsureRuntimeState();
                if (pawn == null) return;
                var map = handFedByCorpseId;
                if (map == null || map.Count == 0) return;

                var buffer = HandCleanup;
                buffer.Clear();
                foreach (var kv in map)
                {
                    if (ReferenceEquals(kv.Value.Pawn, pawn))
                    {
                        buffer.Add(kv.Key);
                    }
                }

                for (int i = 0; i < buffer.Count; i++)
                {
                    map.Remove(buffer[i]);
                }

                if (buffer.Count > 0)
                {
                    MarkChanged();
                }

                buffer.Clear();
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.ClearHandFeeding: " + e);
            }
        }

        public static void ClearForceIngestible(Pawn pawn)
        {
            try
            {
                EnsureRuntimeState();
                if (pawn == null) return;
                if (ReferenceEquals(forcePawn, pawn))
                {
                    forcePawn = null;
                    forceCorpseId = 0;
                    forceTick = 0;
                    MarkChanged();
                }
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.ClearForceIngestible: " + e);
            }
        }

        public static Pawn GetEatingPawnForCorpse(Corpse corpse)
        {
            try
            {
                EnsureRuntimeState();
                if (corpse == null) return null;

                int corpseId = corpse.thingIDNumber;
                int now = Find.TickManager?.TicksGame ?? 0;
                if (forcePawn != null && forceCorpseId == corpseId && now - forceTick <= 1)
                {
                    if (forcePawn.Dead || forcePawn.Destroyed)
                    {
                        ClearForceIngestible(forcePawn);
                    }
                    else
                    {
                        return forcePawn;
                    }
                }
                var handMap = handFedByCorpseId;
                if (handMap != null && handMap.TryGetValue(corpseId, out HandFeedEntry entry))
                {
                    if (entry.Pawn == null || entry.Pawn.Dead || entry.Pawn.Destroyed || now - entry.Tick > 1)
                    {
                        handMap.Remove(corpseId);
                    }
                    else
                    {
                        return entry.Pawn;
                    }
                }

                var map = pawnToTarget;
                if (map == null || map.Count == 0) return null;
                var reverseMap = corpseToPawn;
                if (reverseMap == null || !reverseMap.TryGetValue(corpseId, out Pawn cachedPawn))
                {
                    return null;
                }

                if (cachedPawn == null || cachedPawn.Dead || cachedPawn.Destroyed)
                {
                    reverseMap.Remove(corpseId);
                    return null;
                }

                if (!map.TryGetValue(cachedPawn, out Thing cachedTarget) || !ReferenceEquals(cachedTarget, corpse))
                {
                    reverseMap.Remove(corpseId);
                    return null;
                }

                try
                {
                    Job cj = cachedPawn.CurJob;
                    if (cj == null || cj.def != JobDefOf.Ingest)
                    {
                        Clear(cachedPawn);
                        return null;
                    }

                    if (!ReferenceEquals(cj.targetA.Thing, cachedTarget)
                        && !ReferenceEquals(cj.targetB.Thing, cachedTarget)
                        && !ReferenceEquals(cj.targetC.Thing, cachedTarget))
                    {
                        Clear(cachedPawn);
                        return null;
                    }

                    return cachedPawn;
                }
                catch
                {
                    Clear(cachedPawn);
                    return null;
                }
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.GetEatingPawnForCorpse: " + e);
            }
            return null;
        }
    }
}
