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
        [ThreadStatic] private static Dictionary<int, HandFeedEntry> handFedByCorpseId;
        [ThreadStatic] private static List<Pawn> pawnCleanupBuffer;
        [ThreadStatic] private static List<int> handCleanupBuffer;
        [ThreadStatic] private static Pawn forcePawn;
        [ThreadStatic] private static int forceCorpseId;
        [ThreadStatic] private static int forceTick;

        private struct HandFeedEntry
        {
            public Pawn Pawn;
            public int Tick;
        }

        private static Dictionary<Pawn, Thing> SelfMap => pawnToTarget ?? (pawnToTarget = new Dictionary<Pawn, Thing>());
        private static Dictionary<int, HandFeedEntry> HandMap => handFedByCorpseId ?? (handFedByCorpseId = new Dictionary<int, HandFeedEntry>(32));
        private static List<Pawn> PawnCleanup => pawnCleanupBuffer ?? (pawnCleanupBuffer = new List<Pawn>(8));
        private static List<int> HandCleanup => handCleanupBuffer ?? (handCleanupBuffer = new List<int>(8));

        public static void SetEating(Pawn pawn, Thing target)
        {
            try
            {
                if (pawn == null) return;
                if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out _)) return;

                SelfMap[pawn] = target;
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
                if (pawn == null) return;
                if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out _)) return;

                var corpse = target as Corpse;
                if (corpse == null) return;

                int now = Find.TickManager?.TicksGame ?? 0;
                HandMap[corpse.thingIDNumber] = new HandFeedEntry { Pawn = pawn, Tick = now };
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
                if (pawn == null) return;
                if (!DefModExtensionCache<ModExtension_IsScavenger>.TryGet(pawn, out _)) return;

                var corpse = target as Corpse;
                if (corpse == null) return;

                forcePawn = pawn;
                forceCorpseId = corpse.thingIDNumber;
                forceTick = Find.TickManager?.TicksGame ?? 0;
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
                if (pawn == null) return;
                var map = pawnToTarget;
                if (map == null) return;
                map.Remove(pawn);
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
                if (pawn == null) return;
                if (ReferenceEquals(forcePawn, pawn))
                {
                    forcePawn = null;
                    forceCorpseId = 0;
                    forceTick = 0;
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

                var toRemove = PawnCleanup;
                toRemove.Clear();

                foreach (var kv in map)
                {
                    var p = kv.Key;
                    var t = kv.Value;

                    if (p == null || p.Dead || p.Destroyed)
                    {
                        toRemove.Add(p);
                        continue;
                    }

                    if (t == null)
                    {
                        toRemove.Add(p);
                        continue;
                    }

                    try
                    {
                        var cj = p.CurJob;
                        if (cj == null || cj.def != JobDefOf.Ingest)
                        {
                            toRemove.Add(p);
                            continue;
                        }
                        var curTarget = cj.targetA.Thing;
                        if (curTarget == null || curTarget != t)
                        {
                            toRemove.Add(p);
                            continue;
                        }

                        if (t == corpse) return p;
                    }
                    catch
                    {
                        toRemove.Add(p);
                        continue;
                    }
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    try { map.Remove(toRemove[i]); } catch { }
                }
                toRemove.Clear();
            }
            catch (Exception e)
            {
                Log.Error("[Zoology] Error in ScavengerEatingContext.GetEatingPawnForCorpse: " + e);
            }
            return null;
        }
    }
}
