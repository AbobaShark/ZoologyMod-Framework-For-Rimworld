using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public enum WinterDormancyMode
    {
        NONE, HIBERNATION, WINTER_REST, DENNING, MATERNITY_DENNING
    }

    public class ModExtension_WinterDormancy : DefModExtension
    {
        public WinterDormancyMode mode;
    }

    internal static class AnimalWinterDormancy
    {
        // Mammalian deep torpor typically suppresses basal metabolism to
        // 2–5%, while a denning bear suppresses it to ~25%. Neither is
        // appropriate for merely resting animals that remain normally active.
        // References: Carey et al., Physiol Rev 2003; Tøien et al., Science 2011.
        private const float DeepTorporBasalFraction = 0.04f;
        private const float BearDenningBasalFraction = 0.25f;

        internal static WinterDormancyMode For(ThingDef def)
        {
            WinterDormancyMode mode =
                DefModExtensionCache<ModExtension_WinterDormancy>.Get(def)?.mode
                    ?? WinterDormancyMode.NONE;
            return mode != WinterDormancyMode.NONE && ModConstants.WinterDormancy
                ? mode : WinterDormancyMode.NONE;
        }

        internal static bool Winter(Map map)
        {
            if (!ModConstants.WinterDormancy || map == null) return false;
            Season season = GenLocalDate.Season(map);
            return season == Season.Winter || season == Season.PermanentWinter;
        }

        internal static bool CanEnter(Pawn pawn)
        {
            return ModConstants.WinterDormancy && pawn?.Spawned == true && !pawn.Downed
                && !pawn.InMentalState && pawn.needs?.food?.CurLevelPercentage >= 0.8f
                && CanUseWinterDen(pawn);
        }

        internal static bool CanUseWinterDen(Pawn pawn)
        {
            // Only genuinely wild animals may make winter dens and dormancy
            // jobs. Tamed and other faction-owned animals remain active.
            if (!ModConstants.WinterDormancy || pawn == null || pawn.Dead || pawn.Destroyed
                || pawn.Faction != null
                || pawn.MapHeld == null || !Winter(pawn.MapHeld))
                return false;

            WinterDormancyMode mode = For(pawn.def);
            if (mode == WinterDormancyMode.NONE)
                return false;
            if (mode == WinterDormancyMode.MATERNITY_DENNING)
            {
                if (pawn.gender != Gender.Female || pawn.health?.hediffSet == null)
                    return false;
                bool pregnant = false;
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                    if (hediff is Hediff_Pregnant) { pregnant = true; break; }
                bool dependentYoung = false;
                if (!pregnant && pawn.relations != null)
                    foreach (Pawn child in pawn.relations.Children)
                        if (child != null && !child.Dead && !child.Destroyed
                            && child.MapHeld == pawn.MapHeld
                            && AnimalLifeStageUtility.IsAnimalInfantLifeStage(
                                child.ageTracker?.CurLifeStage))
                        {
                            dependentYoung = true;
                            break;
                        }
                if (!pregnant && !dependentYoung) return false;
            }

            CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
            return state != null && state.lastWinterWakeYear != GenLocalDate.Year(pawn.MapHeld);
        }

        internal static bool IsDormant(Pawn pawn)
        {
            if (!ModConstants.WinterDormancy || pawn?.Dead != false) return false;
            CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
            return state?.inWinterDormancy == true
                && ThingOwnerUtility.GetAnyParent<Building_AnimalBurrow>(pawn)
                    is Building_AnimalBurrow;
        }

        internal static float BasalHungerFactor(Pawn pawn)
        {
            if (!IsDormant(pawn)) return 1f;
            switch (For(pawn.def))
            {
                case WinterDormancyMode.HIBERNATION:
                    return DeepTorporBasalFraction;
                case WinterDormancyMode.DENNING:
                case WinterDormancyMode.MATERNITY_DENNING:
                    return BearDenningBasalFraction;
                default:
                    return 1f;
            }
        }

        internal static void Enter(Pawn pawn, Building_AnimalBurrow burrow)
        {
            CompAnimalStrategyState state = pawn?.TryGetComp<CompAnimalStrategyState>();
            if (state == null) return;
            state.inWinterDormancy = burrow != null
                && Winter(pawn.MapHeld) && CanUseWinterDen(pawn)
                && !ChildcareUtility.IsAnimalChild(pawn);
        }

        internal static void Wake(Pawn pawn, Map map)
        {
            CompAnimalStrategyState state = pawn?.TryGetComp<CompAnimalStrategyState>();
            if (state?.inWinterDormancy != true) return;
            state.inWinterDormancy = false;
            if (map != null && For(pawn.def) != WinterDormancyMode.MATERNITY_DENNING)
                state.lastWinterWakeYear = GenLocalDate.Year(map);
        }
    }

    // Planning occurs only on an idle ThinkTree decision. Winter checks are
    // capped at one per animal per game day; no GameComponent map scan/tick.
    public sealed class JobGiver_AnimalWinterDormancy : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ModConstants.WinterDormancy) return null;
            if (pawn?.Spawned != true || pawn.Map == null)
                return null;

            CompAnimalStrategyState state = pawn.TryGetComp<CompAnimalStrategyState>();
            if (state == null || state.nextWinterDecision > GenTicks.TicksGame)
                return null;
            state.nextWinterDecision = GenTicks.TicksGame + GenDate.TicksPerDay;

            if (!AnimalWinterDormancy.CanEnter(pawn)
                || !AnimalDefense.CanUseShelter(pawn)
                || ZoologyFleeSafetyUtility.IsPawnUnderMeleeAttack(pawn)
                || pawn.mindState?.enemyTarget != null
                || pawn.mindState?.meleeThreat != null)
                return null;

            AnimalRefugeMapComponent index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (index == null || !index.TryPlan())
                return null;

            // Any physically compatible free burrow can serve as winter
            // shelter. Dormancy depends on the occupant, not a building flag.
            foreach (Thing refuge in index.Nearby(pawn.Position, 16))
                if (refuge is Building_AnimalBurrow burrow
                    && burrow.Accepts(pawn)
                    && !burrow.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(burrow, PathEndMode.OnCell, Danger.None,
                        int.MaxValue, 0))
                    return JobMaker.MakeJob(
                        DefDatabase<JobDef>.GetNamed("Zoology_EnterBurrow"), burrow);

            // Newly excavated shelters use the creator's BodySize and
            // a matching 1x1, 2x2 or 3x3 footprint.
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 8f, true))
                if (JobGiver_AnimalBurrow.Diggable(pawn, cell)
                    && pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None))
                {
                    // Dig completion can immediately schedule entering the
                    // resulting den, without another full-day search delay.
                    state.nextWinterDecision = GenTicks.TicksGame + 250;
                    return JobMaker.MakeJob(
                        DefDatabase<JobDef>.GetNamed("Zoology_DigBurrow"), cell);
                }
            return null;
        }
    }
}
