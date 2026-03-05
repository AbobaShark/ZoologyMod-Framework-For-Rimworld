

using System.Collections.Generic;
using Verse;
using RimWorld;

namespace ZoologyMod
{
    
    public class CompProperties_AnimalClotting : CompProperties
    {
        
        public int checkInterval = 360;

        
        public FloatRange tendingQuality = new FloatRange(0.2f, 0.7f);

        public CompProperties_AnimalClotting()
        {
            this.compClass = typeof(Comp_AnimalClotting);
        }
    }

    
    public class Comp_AnimalClotting : ThingComp
    {
        private CompProperties_AnimalClotting Props => (CompProperties_AnimalClotting)this.props;

        public override void CompTick()
        {
            
            if (!parent.IsHashIntervalTick(Props.checkInterval)) return;

            Pawn pawn = parent as Pawn;
            if (pawn == null) return;
            if (pawn.Dead) return;
            if (pawn.health?.hediffSet == null) return;

            
            

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null || hediffs.Count == 0) return;

            
            float quality = Props.tendingQuality.RandomInRange;
            float maxQuality = Props.tendingQuality.TrueMax;

            
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                Hediff h = hediffs[i];
                if (h == null) continue;

                
                if (h.Bleeding)
                {
                    
                    
                    h.Tended(quality, maxQuality, 1);
                }
            }
        }
    }
}