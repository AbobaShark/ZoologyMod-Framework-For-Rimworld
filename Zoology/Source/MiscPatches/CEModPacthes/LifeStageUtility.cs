
using RimWorld;
using Verse;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;

namespace ZoologyMod
{
    public static class LifeStageUtility
    {
        
        private static bool warnedNoDefsOnce = false;

        
        
        
        
        
        public static LifeStagePenetrationDef GetPenetrationDefForPawn(Pawn pawn)
        {
            if (pawn == null) return null;
            var stage = pawn.ageTracker?.CurLifeStage;
            if (stage == null) return null;
            string stageName = stage.defName;

            
            var all = DefDatabase<LifeStagePenetrationDef>.AllDefsListForReading;
            if (all.Count == 0)
            {
                
                if (Prefs.DevMode && !warnedNoDefsOnce)
                {
                    warnedNoDefsOnce = true;
                    Log.Warning("[Zoology] No LifeStagePenetrationDef defs found in DefDatabase. Zoology will not replace life-stage penetration multipliers until XML defs are provided.");
                }
                return null;
            }

            
            var byDef = all.FirstOrDefault(d => string.Equals(d.defName, stageName, StringComparison.Ordinal));
            if (byDef != null) return byDef;

            
            var byDefIgnore = all.FirstOrDefault(d => string.Equals(d.defName, stageName, StringComparison.OrdinalIgnoreCase));
            if (byDefIgnore != null) return byDefIgnore;

            
            var byLabel = all.FirstOrDefault(d => string.Equals(d.label, stageName, StringComparison.OrdinalIgnoreCase));
            if (byLabel != null) return byLabel;

            
            var partial = all.FirstOrDefault(d =>
                (!string.IsNullOrEmpty(d.defName) && stageName.IndexOf(d.defName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(stageName) && d.defName.IndexOf(stageName, StringComparison.OrdinalIgnoreCase) >= 0));
            if (partial != null) return partial;

            
            return null;
        }
    }
}