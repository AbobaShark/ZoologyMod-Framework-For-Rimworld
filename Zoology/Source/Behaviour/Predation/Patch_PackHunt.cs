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
        private static readonly Dictionary<long, int> lastPackHuntNotifyTickByPairKey = new Dictionary<long, int>(128);
        private static readonly List<Pawn> notifiedPackScratch = new List<Pawn>(16);
        private const int PackHuntNotificationCooldownTicks = 300;

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

                bool shouldNotifyPackHunt = preyPawn.Faction == Faction.OfPlayer;
                if (shouldNotifyPackHunt)
                {
                    notifiedPackScratch.Clear();
                    notifiedPackScratch.Add(pawn);
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

                    if (!CanCandidateReachPackPrey(candidate, preyPawn)) continue;

                    
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
                            if (shouldNotifyPackHunt)
                            {
                                notifiedPackScratch.Add(candidate);
                            }
                        }
                    }
                    catch (Exception inner)
                    {
                        Log.Warning($"[Zoology] HerdPredatorHuntPatch: error giving job to candidate {candidate}: {inner}");
                    }
                }

                if (shouldNotifyPackHunt)
                {
                    TryNotifyPackHunt(pawn, preyPawn, currentTick);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] HerdPredatorHuntPatch.Postfix error: {ex}");
            }
            finally
            {
                notifiedPackScratch.Clear();
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
                if (MammalBabyCache.ShouldUseBabyFoodRules(candidate)) continue;
                if (candidate.Faction != null) continue;
                candidates.Add(candidate);
            }

            return candidates;
        }

        private static bool CanCandidateReachPackPrey(Pawn candidate, Pawn prey)
        {
            if (candidate == null || prey == null)
            {
                return false;
            }

            if (!candidate.Spawned || candidate.Map == null || prey.Map == null || candidate.Map != prey.Map)
            {
                return false;
            }

            try
            {
                return candidate.CanReach(prey, PathEndMode.Touch, Danger.Deadly);
            }
            catch
            {
                return false;
            }
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

                    if (!CanCandidateReachPackPrey(candidate, prey)) continue;

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

        private static void TryNotifyPackHunt(Pawn leader, Pawn prey, int currentTick)
        {
            if (leader == null
                || prey == null
                || notifiedPackScratch.Count <= 1)
            {
                return;
            }

            long pairKey = PairKey(leader, prey);
            if (pairKey == 0L)
            {
                return;
            }

            if (lastPackHuntNotifyTickByPairKey.TryGetValue(pairKey, out int lastTick)
                && currentTick - lastTick < PackHuntNotificationCooldownTicks)
            {
                return;
            }

            lastPackHuntNotifyTickByPairKey[pairKey] = currentTick;
            MarkPackMembersAsAlreadyNotified(currentTick);

            try
            {
                string packLabel = ZoologyNotificationUtility.GetCollectiveAnimalLabel(leader, notifiedPackScratch.Count);
                string preyLabel = prey.LabelDefinite();
                string label = $"{packLabel.CapitalizeFirst()} are hunting";
                string text = $"{packLabel.CapitalizeFirst()} have started a pack hunt on {preyLabel}.";
                LookTargets lookTargets = ZoologyNotificationUtility.CreateLookTargets(notifiedPackScratch, prey);

                if (PawnThreatUtility.IsHumanlikeOrMechanoid(prey))
                {
                    Find.LetterStack.ReceiveLetter(label, text.CapitalizeFirst(), LetterDefOf.ThreatBig, lookTargets);
                }
                else
                {
                    Messages.Message(text.CapitalizeFirst(), lookTargets, MessageTypeDefOf.ThreatBig, true);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] HerdPredatorHuntPatch: pack hunt notification failed: {ex}");
            }
        }

        private static void MarkPackMembersAsAlreadyNotified(int currentTick)
        {
            for (int i = 0; i < notifiedPackScratch.Count; i++)
            {
                Pawn hunter = notifiedPackScratch[i];
                if (hunter?.mindState == null)
                {
                    continue;
                }

                hunter.mindState.lastPredatorHuntingPlayerNotificationTick = currentTick;
            }
        }

        private static long PairKey(Pawn predator, Pawn prey)
        {
            if (predator == null || prey == null)
            {
                return 0L;
            }

            return ((long)(uint)predator.thingIDNumber << 32) | (uint)prey.thingIDNumber;
        }
    }
}
