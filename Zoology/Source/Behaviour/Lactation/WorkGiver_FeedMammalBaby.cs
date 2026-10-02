using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    [DefOf]
    public static class ZoologyBabyFeedingDefOf
    {
        public static JobDef Zoology_FeedMammalBaby;

        static ZoologyBabyFeedingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ZoologyBabyFeedingDefOf));
        }
    }

    internal static class HandlerBabyFeedingUtility
    {
        public static bool Enabled => LactationSettingsGate.Enabled()
            && (ZoologyModSettings.Instance?.EnableHandlerBabyFeeding ?? ModConstants.DefaultEnableHandlerBabyFeeding);

        public static bool CanFeed(Pawn baby)
        {
            return Enabled && baby != null && baby.Spawned && !baby.Dead && !baby.Destroyed
                && baby.Faction == Faction.OfPlayer && !baby.InMentalState
                && baby.needs?.food != null && MammalBabyCache.ShouldUseBabyFoodRules(baby);
        }

        public static Thing FindFood(Pawn handler, Pawn baby, out ThingDef foodDef)
        {
            // Handlers can already carry animal food. WillEat uses the existing baby food rules.
            Thing food = FoodUtility.BestFoodInInventory(handler, baby,
                FoodPreferability.DesperateOnly, FoodPreferability.MealLavish, allowDrug: false);
            if (food != null)
            {
                foodDef = food.def;
                return food;
            }

            return FoodUtility.BestFoodSourceOnMap(handler, baby, baby.needs.food.Starving, out foodDef,
                allowPlant: false, allowDrug: false, allowCorpse: false,
                allowDispenserFull: false, allowDispenserEmpty: false, allowHarvest: false);
        }
    }

    public sealed class WorkGiver_FeedMammalBaby : WorkGiver_FeedPatient
    {
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !HandlerBabyFeedingUtility.Enabled;
        }

        public override Danger MaxPathDanger(Pawn pawn) => pawn.NormalMaxDanger();

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (!HandlerBabyFeedingUtility.Enabled)
                yield break;

            // Use the map's faction index rather than scanning every pawn or ticking a new component.
            List<Pawn> animals = pawn.Map.mapPawns.PawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < animals.Count; i++)
            {
                Pawn baby = animals[i];
                if (FeedPatientUtility.IsHungry(baby) && HandlerBabyFeedingUtility.CanFeed(baby))
                    yield return baby;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn baby) || !HandlerBabyFeedingUtility.CanFeed(baby)
                || baby.needs.food.NutritionWanted <= 0f
                || (!forced && !FeedPatientUtility.IsHungry(baby))
                || !pawn.CanReserve(baby, 1, -1, null, forced))
                return false;

            PawnCapacityDef missingCapacity = MissingRequiredCapacity(pawn);
            if (missingCapacity != null)
            {
                JobFailReason.Is("CannotMissingHealthActivities".Translate(missingCapacity.label));
                return false;
            }

            if (HandlerBabyFeedingUtility.FindFood(pawn, baby, out _) == null)
            {
                JobFailReason.Is("NoFood".Translate());
                return false;
            }
            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn baby) || !HandlerBabyFeedingUtility.CanFeed(baby))
                return null;

            Thing food = HandlerBabyFeedingUtility.FindFood(pawn, baby, out ThingDef foodDef);
            if (food == null)
                return null;

            Job job = JobMaker.MakeJob(ZoologyBabyFeedingDefOf.Zoology_FeedMammalBaby, food, baby);
            job.count = FoodUtility.WillIngestStackCountOf(baby, foodDef, FoodUtility.GetNutrition(baby, food, foodDef));
            return job;
        }
    }
}
