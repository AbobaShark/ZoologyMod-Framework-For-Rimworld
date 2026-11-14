using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // One food-access decision for unified burrows, bird nests and soil graves.
    // Existing per-pawn replanning budgets and the map spatial index are reused.
    internal static class AnimalBurrowHunt
    {
        [ThreadStatic] internal static Pawn EvaluatingOccupant;

        // Digging a live prey animal out of a refuge remains a specialist
        // Burrower ability, independent of maternity denning.
        internal static bool CanDig(Pawn pawn) =>
            pawn != null && DefModExtensionCache<ModExtension_Burrower>.Has(pawn);

        // Excavating a grave requires sufficient strength/size and the ability
        // to move soil. Foxes fail the actual BodySize threshold; wolves do
        // not have Burrower or bear-style winter denning.
        internal static bool CanExcavateGrave(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.BodySize < 1f
                || !Building_AnimalBurrow.CanConstructShelter(pawn))
                return false;
            WinterDormancyMode winter = AnimalWinterDormancy.For(pawn.def);
            return CanDig(pawn) || winter == WinterDormancyMode.DENNING
                || winter == WinterDormancyMode.MATERNITY_DENNING;
        }

        internal static bool Eligible(Pawn predator, Building_AnimalBurrow burrow)
            => FindPrey(predator, burrow) != null;

        internal static Pawn FindPrey(Pawn predator, Building_AnimalBurrow burrow)
        {
            if (burrow == null) return null;
            for (int i = 0; i < burrow.ContentsCount; i++)
            {
                Pawn occupant = burrow.PawnAt(i);
                if (occupant != null && CanHuntOccupant(predator, burrow, occupant))
                    return occupant;
            }
            return null;
        }

        private static bool EvaluateHeldPrey(Pawn predator, Pawn prey)
        {
            Pawn previous = EvaluatingOccupant;
            try
            {
                EvaluatingOccupant = prey;
                return FoodUtility.IsAcceptablePreyFor(predator, prey);
            }
            finally { EvaluatingOccupant = previous; }
        }

        internal static bool CanHuntOccupant(Pawn predator, Building_AnimalBurrow burrow, Pawn prey)
        {
            if (predator?.Spawned != true || !predator.RaceProps.predator || predator.Downed
                || !CanDig(predator)
                || burrow?.Spawned != true || burrow.Map != predator.Map || burrow.IsForbidden(predator)
                || prey == null || !burrow.Contains(prey) || prey.Dead || prey == predator
                || burrow.ProtectedByMotherFrom(predator, prey)) return false;
            if (predator.Faction != null && (prey.Faction != null || prey.HostFaction != null)
                && !predator.HostileTo(prey)) return false;
            return EvaluateHeldPrey(predator, prey);
        }

        internal static bool CanHuntNurseryYoung(Pawn predator, Building_AnimalNursery nursery, Pawn prey)
        {
            if (predator?.Spawned != true || !predator.RaceProps.predator || predator.Downed
                || nursery?.Spawned != true || !nursery.PredatorCanOpen(predator)
                || prey == null || !nursery.Contains(prey) || prey.Dead || prey == predator)
                return false;
            if (predator.Faction != null && (prey.Faction != null || prey.HostFaction != null)
                && !predator.HostileTo(prey)) return false;
            return EvaluateHeldPrey(predator, prey);
        }

        private static Corpse CorpseInGrave(Building_Grave grave)
        {
            // Sarcophagi derive from Building_Grave but must never be opened.
            if (grave == null || grave is Building_Sarcophagus || grave.GetType() != typeof(Building_Grave))
                return null;
            ThingOwner held = (grave as IThingHolder)?.GetDirectlyHeldThings();
            return held != null && held.Count > 0 ? held[0] as Corpse : null;
        }

        internal static Corpse EdibleGraveCorpse(Pawn digger, Building_Grave grave)
        {
            if (!CanExcavateGrave(digger) || digger.Downed
                || grave?.Spawned != true || grave.Map != digger.Map
                || grave.IsForbidden(digger))
                return null;
            Corpse corpse = CorpseInGrave(grave);
            if (corpse?.InnerPawn?.RaceProps?.IsFlesh != true || corpse.Bugged)
                return null;
            ModExtension_IsScavenger scavenger =
                DefModExtensionCache<ModExtension_IsScavenger>.Get(digger.def);
            // A buried corpse is not yet Spawned, so an ordinary WillEat(corpse)
            // check is not the same as the scavenger's spawned-corpse evaluation.
            // Check its final ingestible def and rot permissions without
            // touching the corpse or allocating a temporary food job.
            if (corpse.GetRotStage() == RotStage.Dessicated
                && scavenger?.allowVeryRotten != true)
                return null;
            ThingDef foodDef = FoodUtility.GetFinalIngestibleDef(corpse, false);
            return foodDef != null && foodDef.IsNutritionGivingIngestible
                && digger.WillEat(foodDef, digger, true, false) ? corpse : null;
        }

        internal static Thing FindFood(Pawn predator, Thing shelter)
        {
            if (shelter is Building_AnimalBurrow burrow)
                return FindPrey(predator, burrow);
            if (shelter is Building_AnimalNursery nursery)
                return nursery.PredatorCanOpen(predator) ? nursery.FindFoodFor(predator) : null;
            return shelter is Building_Grave grave ? EdibleGraveCorpse(predator, grave) : null;
        }

        internal static bool CanOpen(Pawn predator, Thing shelter, Thing food)
        {
            return food != null && FindFood(predator, shelter) == food;
        }

        internal static int OpeningTicks(Thing shelter)
        {
            if (shelter is Building_AnimalBurrow)
                return Building_AnimalBurrow.Settings.digTicks;
            if (shelter is Building_AnimalNursery nest && nest.Nest)
                return 400; // Existing nest construction interaction length.
            return Building_AnimalBurrow.Settings.digTicks;
        }

        internal static void BeginOpening(Pawn predator, Thing shelter)
        {
            if (shelter is Building_AnimalBurrow burrow)
            {
                burrow.SetThreat(predator);
                burrow.NotifyAttacked(predator);
            }
            else if (shelter is Building_AnimalNursery nursery)
                nursery.NotifyAttacked(predator);
        }

        internal static void Open(Pawn predator, Thing shelter)
        {
            if (shelter is Building_AnimalBurrow burrow) burrow.Excavate(predator);
            else if (shelter is Building_AnimalNursery nursery) nursery.OpenForPredator(predator);
            else if (shelter is Building_Grave grave
                && grave is not Building_Sarcophagus && grave.GetType() == typeof(Building_Grave))
                grave.EjectContents();
        }
    }

    public class JobGiver_ExcavateAnimalBurrow : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            // For predators the food JobGiver compares shelters, map food,
            // live prey and fishing in a single decision. Do not preempt it
            // from Animal_PreMain, which has no common food score.
            if (PredatorFoodChoiceUtility.EnabledFor(pawn))
                return null;
            return TryPlanFoodJob(pawn, out _);
        }

        internal static Job TryPlanFoodJob(Pawn pawn, out float selectedScore)
        {
            selectedScore = float.NegativeInfinity;
            bool unified = PredatorFoodChoiceUtility.EnabledFor(pawn);
            bool refugeEnabled = ModConstants.AdvancedThreatAvoidance;
            bool nestsEnabled = ModConstants.AdvancedReproduction;
            if (!refugeEnabled && !nestsEnabled) return null;
            if (pawn?.Spawned != true || pawn.Downed || pawn.InMentalState
                || pawn.needs?.food?.CurLevelPercentage >= 0.3f
                || (!pawn.RaceProps.predator && !AnimalBurrowHunt.CanDig(pawn))
                || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)
                || PredatorPreyPairGameComponent.Instance?.GetPairedCorpse(pawn) != null)
                return null;
            CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
            AnimalRefugeMapComponent index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (state == null || index == null
                || state.nextBurrowHuntPlan > GenTicks.TicksGame || !index.TryPlan())
                return null;
            state.nextBurrowHuntPlan = GenTicks.TicksGame + 600;
            Thing bestShelter = null, bestFood = null;
            float bestRank = float.NegativeInfinity;
            int checkedCandidates = 0;
            bool checkedNormalFood = false, normalFoodExists = false;
            foreach (Thing shelter in index.Nearby(pawn.Position,
                (int)Building_AnimalBurrow.Settings.searchRadius))
            {
                if (shelter is not Building_AnimalBurrow
                    && shelter is not Building_AnimalNursery
                    && shelter is not Building_Grave)
                    continue;
                if (!refugeEnabled && shelter is not Building_AnimalNursery
                    || !nestsEnabled && shelter is Building_AnimalNursery)
                    continue;
                float distance = (shelter.Position - pawn.Position).LengthHorizontalSquared;
                if (shelter is Building_Grave)
                {
                    if (!AnimalBurrowHunt.CanExcavateGrave(pawn)) continue;
                    // Do not excavate food when ordinary foraging has an
                    // available meal. Run this bounded vanilla search once.
                    if (!unified && !checkedNormalFood)
                    {
                        checkedNormalFood = true;
                        normalFoodExists = FoodUtility.TryFindBestFoodSourceFor(
                            pawn, pawn, false, out _, out _);
                    }
                    if (!unified && normalFoodExists) continue;
                }
                Thing food = AnimalBurrowHunt.FindFood(pawn, shelter);
                if (food == null) continue;
                float rank = unified
                    ? PredatorFoodChoiceUtility.ScoreShelterFood(pawn, shelter, food)
                    : -distance;
                if (rank <= bestRank)
                    continue;
                if (++checkedCandidates > ZoologyTickLimiter.Refuges.MaxPathCandidates)
                    break;
                if (!pawn.CanReserveAndReach(shelter, PathEndMode.Touch, Danger.Some))
                    continue;
                bestRank = rank;
                bestShelter = shelter;
                bestFood = food;
            }
            if (bestShelter == null) return null;
            selectedScore = bestRank;
            string defName = bestShelter is Building_Grave
                ? "Zoology_ExcavateGrave"
                : bestShelter is Building_AnimalNursery
                    ? "Zoology_RaidAnimalNest"
                    : "Zoology_ExcavateBurrow";
            return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(defName),
                bestShelter, bestFood);
        }
    }

    public class JobDriver_ExcavateAnimalBurrow : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => (TargetA.Thing is Building_AnimalNursery
                        ? !ModConstants.AdvancedReproduction
                        : !ModConstants.AdvancedThreatAvoidance)
                || pawn.Downed || pawn.InMentalState
                || pawn.stances.stunner.Stunned
                || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)
                || !AnimalBurrowHunt.CanOpen(pawn, TargetA.Thing, TargetB.Thing));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
                AnimalBurrowHunt.BeginOpening(pawn, TargetA.Thing));
            Toil opening = Toils_General.Wait(
                    AnimalBurrowHunt.OpeningTicks(TargetA.Thing), TargetIndex.A)
                .WithProgressBarToilDelay(TargetIndex.A)
                .FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            // Reassess the same known attacker during long excavations, not
            // only after the animals have been exposed. Existing defense
            // cooldowns and job checks prevent attack-job churn.
            opening.AddPreTickAction(() =>
            {
                if (pawn.IsHashIntervalTick(60))
                    AnimalBurrowHunt.BeginOpening(pawn, TargetA.Thing);
            });
            yield return opening;
            yield return Toils_General.Do(() =>
            {
                Thing food = TargetB.Thing;
                AnimalBurrowHunt.Open(pawn, TargetA.Thing);
                if (food is Pawn prey && prey.Spawned && !prey.Dead
                    && FoodUtility.IsAcceptablePreyFor(pawn, prey))
                    pawn.jobs.jobQueue.EnqueueFirst(
                        JobMaker.MakeJob(JobDefOf.PredatorHunt, prey));
                // The same scavenger code evaluates an exposed corpse through
                // normal ingestion; its rot state and identity are preserved.
            });
        }

        public override void Notify_DamageTaken(DamageInfo dinfo)
        {
            if (dinfo.Amount > 0f) EndJobWith(JobCondition.InterruptForced);
        }
    }
}
