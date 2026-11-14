using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    public sealed class Thing_ColossusToadTadpoles : ThingWithComps
    {
        public override IEnumerable<Thing> ButcherProducts(Pawn butcher, float efficiency)
        {
            CompTadpole cohort = this.TryGetComp<CompTadpole>();
            ThingDef meat = cohort?.ParentRace?.race?.meatDef;
            if (meat == null || efficiency <= 0f) yield break;
            float nutrition = meat.GetStatValueAbstract(StatDefOf.Nutrition);
            if (nutrition <= 0f) yield break;
            int remaining = GenMath.RoundRandom(cohort.CohortNutrition * efficiency / nutrition);
            while (remaining > 0)
            {
                Thing product = ThingMaker.MakeThing(meat);
                product.stackCount = System.Math.Min(remaining, System.Math.Max(1, meat.stackLimit));
                remaining -= product.stackCount;
                yield return product;
            }
        }
    }
}
