using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // These are absolute physical values, not multipliers on vanilla stack
    // statistics. Only the two amphibian ThingComp types are affected.
    public sealed class ZoologyAmphibianCohortStatPart : StatPart
    {
        private bool mass;

        internal static void Install(StatDef stat, bool forMass)
        {
            if (stat == null)
                return;
            if (stat.parts == null)
                stat.parts = new List<StatPart>();
            for (int i = 0; i < stat.parts.Count; i++)
                if (stat.parts[i] is ZoologyAmphibianCohortStatPart)
                    return;
            stat.parts.Add(new ZoologyAmphibianCohortStatPart
            {
                parentStat = stat,
                mass = forMass
            });
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is ThingWithComps thing))
                return;

            CompAmphibianRoe roe = thing.TryGetComp<CompAmphibianRoe>();
            if (roe != null)
            {
                val = mass ? roe.CohortMassKg : roe.CohortNutrition;
                return;
            }
            CompTadpole tadpoles = thing.TryGetComp<CompTadpole>();
            if (tadpoles != null)
                val = mass ? tadpoles.CohortMassKg : tadpoles.CohortNutrition;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!(req.Thing is ThingWithComps thing)
                || (thing.TryGetComp<CompAmphibianRoe>() == null
                    && thing.TryGetComp<CompTadpole>() == null))
                return null;
            return mass
                ? "Total wet biomass of the aquatic cohort"
                : "Total chemical energy of the aquatic cohort";
        }
    }

    [StaticConstructorOnStartup]
    internal static class ZoologyAmphibianCohortStatInstaller
    {
        static ZoologyAmphibianCohortStatInstaller()
        {
            ZoologyAmphibianCohortStatPart.Install(StatDefOf.Mass, true);
            ZoologyAmphibianCohortStatPart.Install(StatDefOf.Nutrition, false);
        }
    }
}
