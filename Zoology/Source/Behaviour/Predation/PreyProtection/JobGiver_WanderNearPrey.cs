using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    
    
    public class JobGiver_WanderNearPrey : JobGiver_Wander
    {
        public JobGiver_WanderNearPrey()
        {
            
            this.wanderRadius = 5f;
            this.ticksBetweenWandersRange = new IntRange(125, 200);
            this.locomotionUrgency = LocomotionUrgency.Walk;
            this.expiryInterval = -1;
        }

        protected override IntVec3 GetWanderRoot(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.Map == null) return IntVec3.Invalid;

                var comp = PredatorPreyPairGameComponent.Instance;
                if (comp == null) return IntVec3.Invalid;

                
                Corpse targetCorpse = null;
                try { targetCorpse = comp.GetPairedCorpse(pawn); } catch { targetCorpse = null; }

                if (targetCorpse == null) return IntVec3.Invalid;

                if (!PreyProtectionUtility.TryGetProtectionAnchor(targetCorpse, out Map corpseMap, out IntVec3 pos))
                {
                    return IntVec3.Invalid;
                }

                if (pawn.Map != corpseMap) return IntVec3.Invalid;

                if (!PreyProtectionUtility.IsPawnWithinProtectionRange(pawn, corpseMap, pos, PreyProtectionUtility.GetProtectionRangeSquared()))
                {
                    return IntVec3.Invalid;
                }

                
                if (IsSleepingOrLyingDown(pawn)) return IntVec3.Invalid;

                
                
                return pos;
            }
            catch { return IntVec3.Invalid; }
        }

        private static bool IsSleepingOrLyingDown(Pawn pawn)
        {
            try
            {
                if (pawn == null) return false;
                if (!pawn.Awake()) return true;

                var curJob = pawn?.jobs?.curJob;
                if (curJob != null)
                {
                    var defName = curJob.def?.defName ?? "";
                    if (!string.IsNullOrEmpty(defName) && defName.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    var driver = pawn.jobs.curDriver;
                    if (driver != null)
                    {
                        var driverName = driver.GetType().Name ?? "";
                        if (!string.IsNullOrEmpty(driverName) && driverName.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }

                return false;
            }
            catch { return false; }
        }
    }
}
