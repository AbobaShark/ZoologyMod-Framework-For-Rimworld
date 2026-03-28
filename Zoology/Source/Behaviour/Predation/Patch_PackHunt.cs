using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    
    
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class HerdPredatorHuntPatch
    {
        private sealed class HerdCandidateCache
        {
            public int Tick = -1;
            public readonly List<Pawn> Candidates = new List<Pawn>(32);
        }

        private static readonly Dictionary<int, HerdCandidateCache> herdCandidatesByMapId = new Dictionary<int, HerdCandidateCache>(8);

        public static bool Prepare()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnablePackHunt;
        }

        
        internal const float HerdRadius = 35f;

        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
                var s = ZoologyModSettings.Instance;
                if (s != null && !s.EnablePackHunt) return;

                if (pawn == null || __result == null) return;
                if (__result.def != JobDefOf.PredatorHunt) return;

                
                Thing targetThing = null;
                try { targetThing = __result.targetA.Thing; } catch { targetThing = null; }
                if (targetThing == null) return;
                Pawn preyPawn = targetThing as Pawn;
                if (preyPawn == null) return;

                
                if (!pawn.RaceProps.herdAnimal) return;

                var map = pawn.Map;
                if (map == null) return;

                
                IntVec3 pawnPosition = pawn.Position;
                ThingDef pawnDef = pawn.def;
                int herdRadiusSq = (int)(HerdRadius * HerdRadius);
                int currentTick = Find.TickManager?.TicksGame ?? 0;

                IReadOnlyList<Pawn> candidates = GetHerdCandidates(map, currentTick);
                if (candidates == null || candidates.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    Pawn candidate = candidates[i];
                    if (candidate == null) continue;
                    if (candidate == pawn) continue; 
                    if (candidate.Downed) continue;
                    if (candidate.InMentalState) continue;
                    if (candidate.RaceProps?.predator != true) continue;
                    
                    if ((candidate.Position - pawnPosition).LengthHorizontalSquared > herdRadiusSq) continue;

                    
                    bool relatedToPack = false;
                    try
                    {
                        relatedToPack = candidate.def == pawnDef || ZoologyCacheUtility.AreCrossbreedRelated(candidate.def, pawnDef);
                    }
                    catch { relatedToPack = false; }

                    if (!relatedToPack) continue;

                    
                    try
                    {
                        var curJob = candidate.CurJob;
                        if (curJob != null && curJob.def == JobDefOf.PredatorHunt)
                        {
                            Thing curTarget = null;
                            try { curTarget = curJob.targetA.Thing; } catch { curTarget = null; }
                            if (curTarget == targetThing) continue; 
                        }
                    }
                    catch { /*ignore*/ }

                    
                    try
                    {
                        Job recruitJob = JobMaker.MakeJob(JobDefOf.PredatorHunt, preyPawn);
                        recruitJob.killIncappedTarget = true;

                        
                        bool given = false;
                        try
                        {
                            recruitJob.playerForced = false;
                            candidate.jobs.StartJob(recruitJob, JobCondition.InterruptForced, null, false, false, null);

                            Job newCurJob = candidate.CurJob;
                            given = newCurJob != null
                                && newCurJob.def == JobDefOf.PredatorHunt
                                && newCurJob.targetA.HasThing
                                && newCurJob.GetTarget(TargetIndex.A).Thing == preyPawn;
                        }
                        catch
                        {
                            try
                            {
                                given = candidate.jobs.TryTakeOrderedJob(recruitJob);
                            }
                            catch
                            {
                                given = false;
                            }
                        }

                        
                        if (given)
                        {
                            try { candidate.Map.attackTargetsCache.UpdateTarget(candidate); } catch { }
                        }
                    }
                    catch (Exception inner)
                    {
                        Log.Warning($"[Zoology] HerdPredatorHuntPatch: error giving job to candidate {candidate}: {inner}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] HerdPredatorHuntPatch.Postfix error: {ex}");
            }
        }

        internal static IReadOnlyList<Pawn> GetHerdCandidates(Map map, int currentTick)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
            {
                return null;
            }

            int mapId = map.uniqueID;
            if (!herdCandidatesByMapId.TryGetValue(mapId, out HerdCandidateCache cache))
            {
                cache = new HerdCandidateCache();
                herdCandidatesByMapId[mapId] = cache;
            }

            if (currentTick > 0 && cache.Tick == currentTick)
            {
                return cache.Candidates;
            }

            cache.Tick = currentTick;
            List<Pawn> candidates = cache.Candidates;
            candidates.Clear();

            var all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn candidate = all[i];
                if (candidate == null) continue;
                if (!candidate.Spawned || candidate.Dead || candidate.Destroyed) continue;
                if (!candidate.RaceProps.herdAnimal) continue;
                if (candidate.RaceProps.predator != true) continue;
                if (candidate.Faction != null) continue;
                candidates.Add(candidate);
            }

            return candidates;
        }

        internal static bool HasPackSupport(Pawn predator, Pawn prey)
        {
            try
            {
                var s = ZoologyModSettings.Instance;
                if (s != null && !s.EnablePackHunt) return false;

                if (predator == null || prey == null) return false;
                if (!predator.Spawned || predator.Dead || predator.Destroyed) return false;
                if (predator.Downed || predator.InMentalState) return false;
                if (predator.Faction != null) return false;
                if (predator.RaceProps?.predator != true) return false;
                if (!predator.RaceProps.herdAnimal) return false;

                Map map = predator.Map;
                if (map == null) return false;

                int currentTick = Find.TickManager?.TicksGame ?? 0;
                IReadOnlyList<Pawn> candidates = GetHerdCandidates(map, currentTick);
                if (candidates == null || candidates.Count == 0) return false;

                IntVec3 predatorPos = predator.Position;
                int herdRadiusSq = (int)(HerdRadius * HerdRadius);
                ThingDef predatorDef = predator.def;

                for (int i = 0; i < candidates.Count; i++)
                {
                    Pawn candidate = candidates[i];
                    if (candidate == null) continue;
                    if (candidate == predator) continue;
                    if (candidate.Downed || candidate.InMentalState) continue;
                    if (candidate.RaceProps?.predator != true) continue;
                    if ((candidate.Position - predatorPos).LengthHorizontalSquared > herdRadiusSq) continue;

                    bool relatedToPack = false;
                    try
                    {
                        relatedToPack = candidate.def == predatorDef || ZoologyCacheUtility.AreCrossbreedRelated(candidate.def, predatorDef);
                    }
                    catch
                    {
                        relatedToPack = false;
                    }

                    if (!relatedToPack) continue;

                    try
                    {
                        Job curJob = candidate.CurJob;
                        if (curJob != null && curJob.def == JobDefOf.PredatorHunt)
                        {
                            Thing curTarget = null;
                            try { curTarget = curJob.targetA.Thing; } catch { curTarget = null; }
                            if (curTarget != prey) continue;
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
