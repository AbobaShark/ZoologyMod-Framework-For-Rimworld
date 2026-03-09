using System;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    
    public class CompProperties_AnimalRegeneration : CompProperties
    {
        
        public HediffDef hediffBaby;
        public HediffDef hediffJuvenile;
        public HediffDef hediffAdult;

        
        public float babyFraction = 0.2f;
        public float juvenileFraction = 0.5f;
        public float adultFraction = 1.0f;

        
        public int checkIntervalTicks = 720;

        public CompProperties_AnimalRegeneration()
        {
            this.compClass = typeof(Comp_AnimalRegeneration);
        }
    }

    
    public class Comp_AnimalRegeneration : ThingComp
    {
        private CompProperties_AnimalRegeneration Props => (CompProperties_AnimalRegeneration)this.props;
        private HediffDef hediffBaby;
        private HediffDef hediffJuvenile;
        private HediffDef hediffAdult;
        private int checkIntervalTicks = 720;
        private float babyThreshold;
        private float juvenileThreshold;
        private float adultThreshold;

        
        private const float DefaultBabyFraction = 0.2f;
        private const float DefaultJuvenileFraction = 0.5f;
        private const float DefaultAdultFraction = 1.0f;
        private const int MinIntervalTicks = 60;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            ValidateAndFixFractions();
            CacheRegenerationConfig();
        }

        
        private void ValidateAndFixFractions()
        {
            if (Props == null) return;

            bool needFix = false;

            float b = Props.babyFraction;
            float j = Props.juvenileFraction;
            float a = Props.adultFraction;

            
            if (!(b > 0f) || !(j > 0f) || !(a > 0f))
            {
                needFix = true;
            }

            
            if (!(b < j && j < a))
            {
                needFix = true;
            }

            if (needFix)
            {
                Log.Warning($"[Zoology] Comp_AnimalRegeneration: invalid fraction(s) in CompProperties for {parent?.def?.defName ?? "unknown"}. Replacing with defaults ({DefaultBabyFraction}, {DefaultJuvenileFraction}, {DefaultAdultFraction}).");
                Props.babyFraction = DefaultBabyFraction;
                Props.juvenileFraction = DefaultJuvenileFraction;
                Props.adultFraction = DefaultAdultFraction;
            }
        }

        private void CacheRegenerationConfig()
        {
            if (Props == null) return;

            if (Props.checkIntervalTicks < MinIntervalTicks)
            {
                Props.checkIntervalTicks = MinIntervalTicks;
            }

            hediffBaby = Props.hediffBaby;
            hediffJuvenile = Props.hediffJuvenile;
            hediffAdult = Props.hediffAdult;
            checkIntervalTicks = Props.checkIntervalTicks;

            float baseBodySize = parent?.def?.race?.baseBodySize ?? 1.0f;
            if (baseBodySize <= 0f)
            {
                baseBodySize = 1.0f;
            }

            babyThreshold = baseBodySize * Props.babyFraction;
            juvenileThreshold = baseBodySize * Props.juvenileFraction;
            adultThreshold = baseBodySize * Props.adultFraction;
        }

        public override void CompTick()
        {
            base.CompTick();

            if (Props == null) return;
            if (!parent.IsHashIntervalTick(checkIntervalTicks)) return;

            Pawn pawn = parent as Pawn;
            if (pawn == null) return;
            if (pawn.Dead) return;
            if (pawn.health?.hediffSet == null) return;

            float currentBodySize = pawn.BodySize;

            
            HediffDef targetDef = null;
            if (hediffAdult != null && currentBodySize >= adultThreshold)
            {
                targetDef = hediffAdult;
            }
            else if (hediffJuvenile != null && currentBodySize >= juvenileThreshold)
            {
                targetDef = hediffJuvenile;
            }
            else if (hediffBaby != null && currentBodySize >= babyThreshold)
            {
                targetDef = hediffBaby;
            }
            else
            {
                targetDef = null;
            }

            
            Hediff current = GetCurrentRegenerationHediff(pawn);

            
            if ((current == null && targetDef != null) || (current != null && current.def != targetDef))
            {
                if (current != null)
                {
                    try
                    {
                        pawn.health.RemoveHediff(current);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[Zoology] Comp_AnimalRegeneration: failed to remove existing regen hediff {current.def?.defName} from {pawn.LabelShort}: {e}");
                    }
                }

                if (targetDef != null)
                {
                    try
                    {
                        var newH = HediffMaker.MakeHediff(targetDef, pawn);
                        pawn.health.AddHediff(newH);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[Zoology] Comp_AnimalRegeneration: failed to add regen hediff {targetDef.defName} to {pawn.LabelShort}: {e}");
                    }
                }
            }

            
        }

        
        private Hediff GetCurrentRegenerationHediff(Pawn pawn)
        {
            if (pawn == null || pawn.health?.hediffSet == null) return null;

            var hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null || hediffs.Count == 0) return null;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                HediffDef def = h?.def;
                if (def != null && (def == hediffBaby || def == hediffJuvenile || def == hediffAdult))
                {
                    return h;
                }
            }

            return null;
        }
    }
}
