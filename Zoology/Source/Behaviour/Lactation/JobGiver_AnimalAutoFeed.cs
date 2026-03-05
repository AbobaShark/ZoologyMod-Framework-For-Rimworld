

using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace ZoologyMod
{
    public class JobGiver_AnimalAutoFeed : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            string reason;
            if (!AnimalChildcareUtility.CanMotherFeed(pawn, out reason)) return null;

            if (!AnimalChildcareUtility.CanAttemptFeedNow(pawn)) return null;

            Pawn targetPup = FindNearestHungryPup(pawn);
            if (targetPup == null) return null;

            var youngSuckleDef = DefDatabase<JobDef>.GetNamedSilentFail("Zoology_YoungSuckle");
            if (targetPup.CurJob != null && (targetPup.CurJob.def == youngSuckleDef || targetPup.CurJob.def == JobDefOf.Wait))
                return null;

            Job job = AnimalChildcareUtility.MakeAnimalBreastfeedJob(targetPup, pawn);
            if (job == null) return null;

            try
            {
                if (!pawn.Reserve(targetPup, job, 1, -1, null, false))
                {
                    return null;
                }
                if (!pawn.Reserve(pawn, job, 1, -1, null, false))
                {
                    
                    try
                    {
                        var rm = targetPup.Map?.reservationManager;
                        if (rm != null) rm.Release(new LocalTargetInfo(targetPup), pawn, job);
                    }
                    catch { }
                    return null;
                }
            }
            catch
            {
                return null;
            }

            AnimalChildcareUtility.RecordFeedAttempt(pawn);

            return job;
        }

        private Pawn FindNearestHungryPup(Pawn mom)
        {
            if (mom == null || mom.Map == null) return null;
            Pawn best = null;
            float bestFoodPerc = 1f; 
            float bestDistSqr = float.MaxValue;
            int bestMalStage = -1; 

            foreach (Pawn p in mom.Map.mapPawns.AllPawnsSpawned)
            {
                if (p == mom || p.Dead) continue;

                if (p.Faction != mom.Faction && p.HostFaction != mom.Faction) continue;

                if (!AnimalChildcareUtility.IsCrossBreedCompatible(mom, p)) continue;

                if (!p.IsMammal()) continue;

                var curStage = p.ageTracker?.CurLifeStage;
                if (curStage == null) continue;
                if (!string.Equals(curStage.defName, "AnimalBaby", System.StringComparison.OrdinalIgnoreCase)) continue;

                if (p.InMentalState) continue;

                if (!AnimalChildcareUtility.ChildWantsSuckle(p)) continue;

                
                if (!mom.CanReserve(p)) continue;
                if (!mom.CanReach(p, PathEndMode.Touch, Danger.Deadly, false, false, TraverseMode.ByPawn)) continue;

                float foodPerc = (p.needs?.food != null) ? p.needs.food.CurLevelPercentage : 1f;
                float distSqr = (p.Position - mom.Position).LengthHorizontalSquared;

                
                int malStage = 0;
                try
                {
                    var mal = p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Malnutrition, false);
                    if (mal != null && mal.def != null && mal.def.stages != null && mal.def.stages.Count > 0)
                    {
                        
                        for (int si = 0; si < mal.def.stages.Count; si++)
                        {
                            var s = mal.def.stages[si];
                            
                            if (mal.Severity >= s.minSeverity)
                                malStage = si;
                        }
                    }
                }
                catch
                {
                    
                    malStage = 0;
                }

                
                
                
                
                
                bool isBetter = false;
                const float eps = 1e-6f;
                bool candidateEmpty = foodPerc <= eps;
                bool bestEmpty = best != null && bestFoodPerc <= eps;

                if (candidateEmpty && !bestEmpty)
                {
                    
                    isBetter = true;
                }
                else if (candidateEmpty && bestEmpty)
                {
                    
                    if (malStage > bestMalStage) isBetter = true;
                    else if (malStage == bestMalStage && distSqr < bestDistSqr) isBetter = true;
                }
                else if (!candidateEmpty && !bestEmpty)
                {
                    
                    if (foodPerc + eps < bestFoodPerc) isBetter = true;
                    else if (Mathf.Abs(foodPerc - bestFoodPerc) <= eps && distSqr < bestDistSqr) isBetter = true;
                }
                else
                {
                    
                    isBetter = false;
                }

                if (isBetter)
                {
                    bestFoodPerc = foodPerc;
                    bestDistSqr = distSqr;
                    best = p;
                    bestMalStage = malStage;
                }
            }

            return best;
        }
    }
}