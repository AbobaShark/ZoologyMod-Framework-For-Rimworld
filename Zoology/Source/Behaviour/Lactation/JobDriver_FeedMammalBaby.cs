using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Keep vanilla food/recipient reservations, stack limits and reporting.
    public sealed class JobDriver_FeedMammalBaby : JobDriver_FoodFeedPatient
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => !HandlerBabyFeedingUtility.CanFeed(Deliveree)
                || Deliveree.needs.food.NutritionWanted <= 0f);

            // The vanilla patient driver requires medical rest; babies can be fed where they stand.
            Toil takeFromInventory = Toils_Misc.TakeItemFromInventoryToCarrier(pawn, TargetIndex.A);
            Toil goToBaby = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return Toils_Jump.JumpIf(takeFromInventory,
                () => pawn.inventory != null && pawn.inventory.Contains(Food));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnForbidden(TargetIndex.A);
            yield return Toils_Ingest.PickupIngestible(TargetIndex.A, Deliveree);
            yield return Toils_Jump.Jump(goToBaby);
            yield return takeFromInventory;
            yield return goToBaby;

            Toil feed = Toils_Ingest.ChewIngestible(Deliveree, 1.5f, TargetIndex.A)
                .FailOnCannotTouch(TargetIndex.B, PathEndMode.Touch);
            feed.AddPreInitAction(() => PawnUtility.ForceWait(Deliveree,
                UnityEngine.Mathf.RoundToInt(Food.def.ingestible.baseIngestTicks * 1.5f) + 1,
                pawn, maintainPosture: true, maintainSleep: true));
            feed.activeSkill = () => SkillDefOf.Animals;
            yield return feed;
            yield return Toils_Ingest.FinalizeIngest(Deliveree, TargetIndex.A);
        }
    }
}
