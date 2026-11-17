using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class ModExtension_WinterDormancy : DefModExtension
    {
        public bool ordinaryDormancy;
        public int maternitySeasonMask;
    }

    internal static class AnimalWinterDormancy
    {
        // Food capacity in RimWorld represents a much shorter reserve than
        // adipose stores in a naturally overwintering animal.
        internal const float DormantMaintenanceFraction = 0.025f;

        internal static bool For(ThingDef def) =>
            ModConstants.WinterDormancy
            && DefModExtensionCache<ModExtension_WinterDormancy>.Get(def) != null;

        internal static bool MaternityDenning(Pawn pawn)
        {
            if (pawn?.gender != Gender.Female) return false;
            if (!For(pawn.def)) return false;
            var profile = DefModExtensionCache<ModExtension_WinterDormancy>.Get(pawn.def);
            if (profile == null || profile.maternitySeasonMask == 0) return false;
            if (pawn.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) == true) return true;
            if (pawn.relations != null)
                foreach (Pawn child in pawn.relations.Children)
                    if (child != null && !child.Dead && !child.Destroyed
                        && child.MapHeld == pawn.MapHeld
                        && AnimalLifeStageUtility.IsAnimalInfantLifeStage(child.ageTracker?.CurLifeStage))
                        return true;
            return false;
        }

        internal static bool FactionAllowed(Pawn pawn) =>
            pawn != null && (pawn.Faction == null
                || (ModConstants.IsTamedAnimal(pawn) && ModConstants.TamedWinterDormancy));

        internal static bool Winter(Pawn pawn, Map map)
        {
            if (!ModConstants.WinterDormancy || map == null || !FactionAllowed(pawn)) return false;
            var winterProfile = DefModExtensionCache<ModExtension_WinterDormancy>.Get(pawn.def);
            if (winterProfile == null) return false;
            if (winterProfile.maternitySeasonMask != 0
                && (winterProfile.maternitySeasonMask & AnimalSeasonalReproduction.SeasonBit(map)) != 0
                && MaternityDenning(pawn))
                return true;
            if (!winterProfile.ordinaryDormancy) return false;
            var profile = AnimalSeasonalReproduction.Profile(pawn);
            if (profile != null && !float.IsNaN(profile.dormancyThreshold))
                return AnimalSeasonalReproduction.SeasonalMean(map) < profile.dormancyThreshold;
            Season local = GenLocalDate.Season(map);
            return local == Season.Winter || local == Season.PermanentWinter;
        }

        internal static bool CanEnter(Pawn pawn)
        {
            return ModConstants.WinterDormancy && pawn?.Spawned == true && !pawn.Downed
                && !pawn.InMentalState && pawn.needs?.food?.CurLevelPercentage >= 0.8f
                && pawn.health.hediffSet.BleedRateTotal <= 0f
                && pawn.mindState?.enemyTarget == null && pawn.mindState?.meleeThreat == null
                && !ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)
                && CanUseWinterDen(pawn);
        }

        internal static bool CanUseWinterDen(Pawn pawn)
        {
            if (!ModConstants.WinterDormancy || pawn == null || pawn.Dead || pawn.Destroyed
                || !FactionAllowed(pawn)
                || pawn.MapHeld == null || !Winter(pawn, pawn.MapHeld))
                return false;

            return pawn.TryGetComp<CompAnimalStrategyState>() != null
                && !ChildcareUtility.IsAnimalChild(pawn);
        }

        internal static bool IsDormant(Pawn pawn)
        {
            if (!ModConstants.WinterDormancy || pawn?.Spawned != false
                || pawn.Dead || !FactionAllowed(pawn)) return false;
            CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
            return state?.inWinterDormancy == true
                && ThingOwnerUtility.GetAnyParent<Building_AnimalBurrow>(pawn)
                    is Building_AnimalBurrow;
        }

        internal static float MaintenanceHungerFactor(Pawn pawn) =>
            IsDormant(pawn) ? DormantMaintenanceFraction : 1f;

        internal static void Enter(Pawn pawn, Building_AnimalBurrow burrow, bool eligibleAtEntry)
        {
            CompAnimalStrategyState state = pawn?.TryGetComp<CompAnimalStrategyState>();
            if (state == null) return;
            state.inWinterDormancy = eligibleAtEntry && burrow != null
                && pawn.needs?.food?.CurLevelPercentage >= 0.8f
                && CanUseWinterDen(pawn);
        }

        internal static void Wake(Pawn pawn)
        {
            CompAnimalStrategyState state = pawn?.TryGetComp<CompAnimalStrategyState>();
            if (state?.inWinterDormancy != true) return;
            state.inWinterDormancy = false;
            state.nextWinterDecision = 0;
        }
    }

    // Planning occurs only on an idle ThinkTree decision. Winter checks are
    // capped at one per animal per game day; no GameComponent map scan/tick.
    public sealed class JobGiver_AnimalWinterDormancy : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ModConstants.WinterDormancy) return null;
            if (pawn?.Spawned != true || pawn.Map == null || !AnimalWinterDormancy.FactionAllowed(pawn)
                || !(pawn.needs?.food?.CurLevelPercentage >= 0.8f)
                || !AnimalWinterDormancy.For(pawn.def))
                return null;

            CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null || state.nextWinterDecision > GenTicks.TicksGame
                || state.burrowReentryAfter > GenTicks.TicksGame)
                return null;
            state.nextWinterDecision = GenTicks.TicksGame + GenDate.TicksPerDay;

            if (!AnimalWinterDormancy.CanEnter(pawn)
                || !AnimalDefense.CanUseShelter(pawn))
                return null;

            AnimalRefugeMapComponent index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (index == null || !index.TryPlan())
                return null;

            Building_AnimalBurrow reusable = index.FindReusableBurrow(
                pawn, state, false, Danger.None);
            if (reusable != null)
                return JobMaker.MakeJob(
                    DefDatabase<JobDef>.GetNamed("Zoology_EnterBurrow"), reusable);

            // Newly excavated shelters use the creator's BodySize and
            // a matching 1x1, 2x2 or 3x3 footprint.
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 8f, true))
                if (JobGiver_AnimalBurrow.Diggable(pawn, cell)
                    && pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None))
                {
                    // Dig completion can immediately schedule entering the
                    // resulting den, without another full-day search delay.
                    state.nextWinterDecision = GenTicks.TicksGame + 250;
                    return JobGiver_AnimalBurrow.DigJob(cell, AnimalShelterDigPurpose.WinterDen);
                }
            return null;
        }
    }
}
