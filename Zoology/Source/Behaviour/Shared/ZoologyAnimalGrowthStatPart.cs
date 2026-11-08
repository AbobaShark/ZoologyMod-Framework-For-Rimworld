using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Runs within vanilla stat finalization. Only stats with genuinely different
    // neighboring stage factors are patched; full calculations run at most daily.
    public sealed class ZoologyAnimalGrowthStatPart : StatPart
    {
        private bool marketValue;

        internal static void Install(HashSet<StatDef> affected)
        {
            if (affected == null)
                return;

            // Vanilla market value applies its life-stage multiplier in the
            // pawn valuation path rather than through LifeStageDef.statFactors.
            var market = DefDatabase<StatDef>.GetNamedSilentFail("MarketValue");
            if (market != null)
                affected.Add(market);

            foreach (var stat in affected)
            {
                if (stat == null)
                    continue;
                if (stat.parts == null)
                    stat.parts = new List<StatPart>();

                bool exists = false;
                for (int i = 0; i < stat.parts.Count; i++)
                {
                    if (stat.parts[i] is ZoologyAnimalGrowthStatPart)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists)
                    continue;

                // Stage multiplier replacement must precede the generated
                // absolute armor offset StatPart. Otherwise the offset would
                // be multiplied twice as the animal grows.
                stat.parts.Insert(0, new ZoologyAnimalGrowthStatPart
                {
                    parentStat = stat,
                    marketValue = stat == market
                });
            }
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn pawn) || pawn.RaceProps?.Animal != true)
                return;

            float ratio;
            if (marketValue)
                ratio = ZoologyAnimalGrowth.MarketRatio(pawn);
            else if (!ZoologyAnimalGrowth.TryGetStatRatio(pawn, parentStat, out ratio))
                return;

            if (Math.Abs(ratio - 1f) > 0.000001f)
                val *= ratio;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is Pawn pawn) || pawn.RaceProps?.Animal != true)
                return null;
            float ratio;
            if (marketValue)
                ratio = ZoologyAnimalGrowth.MarketRatio(pawn);
            else if (!ZoologyAnimalGrowth.TryGetStatRatio(pawn, parentStat, out ratio))
                return null;

            return Math.Abs(ratio - 1f) > 0.000001f
                ? "Zoology: daily growth x" + ratio.ToString("0.###") : null;
        }
    }
}
