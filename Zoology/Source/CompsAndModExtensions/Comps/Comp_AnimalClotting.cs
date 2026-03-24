using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

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
        private const int MinCheckInterval = 60;
        private static readonly FloatRange DefaultTendingQuality = new FloatRange(0.2f, 0.7f);

        private int checkInterval = 360;
        private FloatRange tendingQuality = DefaultTendingQuality;

        private CompProperties_AnimalClotting Props => (CompProperties_AnimalClotting)this.props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            checkInterval = Math.Max(MinCheckInterval, Props?.checkInterval ?? 360);
            tendingQuality = Props?.tendingQuality ?? DefaultTendingQuality;
        }

        public override void CompTick()
        {
            ZoologyModSettings settings = ZoologyModSettings.Instance;
            if (settings != null && (settings.DisableAllRuntimePatches || !settings.EnableAnimalClottingComp))
            {
                return;
            }

            if (!parent.IsHashIntervalTick(checkInterval)) return;

            Pawn pawn = parent as Pawn;
            if (pawn == null) return;
            if (pawn.Dead) return;
            if (pawn.health?.hediffSet == null) return;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            if (hediffs == null || hediffs.Count == 0) return;

            float quality = tendingQuality.RandomInRange;
            float maxQuality = tendingQuality.TrueMax;

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
