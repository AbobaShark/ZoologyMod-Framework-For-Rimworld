

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;
using RimWorld;
using Verse.AI;

namespace ZoologyMod
{
    
    
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class HerdPredatorHuntPatch
    {
        
        private const float HerdRadius = 35f;

        public static void Postfix(Pawn pawn, ref Job __result)
        {
            try
            {
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

                
                var all = map.mapPawns.AllPawnsSpawned; 
                int herdRadiusSq = (int)(HerdRadius * HerdRadius);

                for (int i = 0; i < all.Count; i++)
                {
                    Pawn candidate = all[i];
                    if (candidate == null) continue;
                    if (candidate == pawn) continue; 
                    if (candidate.Downed) continue;
                    if (candidate.InMentalState) continue;
                    if (!candidate.RaceProps.herdAnimal) continue;
                    if (candidate.Faction != null) continue; 
                    
                    if ((candidate.Position - pawn.Position).LengthHorizontalSquared > herdRadiusSq) continue;

                    
                    bool sameDef = candidate.def == pawn.def;
                    bool crossbreed = false;
                    try
                    {
                        if (candidate.def?.race?.canCrossBreedWith != null)
                        {
                            for (int k = 0; k < candidate.def.race.canCrossBreedWith.Count; k++)
                            {
                                ThingDef td = candidate.def.race.canCrossBreedWith[k];
                                if (td == null) continue;
                                if (td == pawn.def || string.Equals(td.defName, pawn.def?.defName, StringComparison.OrdinalIgnoreCase))
                                {
                                    crossbreed = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { crossbreed = false; }

                    if (!sameDef && !crossbreed) continue;

                    
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
                            
                            given = candidate.jobs.TryTakeOrderedJob(recruitJob);
                        }
                        catch
                        {
                            
                            try
                            {
                                candidate.jobs.StartJob(recruitJob, JobCondition.None, null, false, true, null);
                                given = true;
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
    }
}