using System;
using System.Collections.Generic;
using Verse;

namespace ZoologyMod
{
    public sealed class WildAnimalOverpopulationExitMapComponent : MapComponent
    {
        private static readonly IntRange ExitIntervalTicks = new IntRange(60000, 120000);

        private int nextExitCheckTick = -1;

        public WildAnimalOverpopulationExitMapComponent(Map map)
            : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextExitCheckTick, "Zoology_nextOverpopulationWildExitTick", -1);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (nextExitCheckTick < GenTicks.TicksGame)
            {
                ScheduleNext(GenTicks.TicksGame);
            }
        }

        public override void MapComponentTick()
        {
            ZoologyModSettings settings = ModConstants.Settings;
            if (!ShouldRun(settings))
            {
                return;
            }

            int now = GenTicks.TicksGame;
            if (nextExitCheckTick < 0)
            {
                ScheduleNext(now);
                return;
            }

            if (now < nextExitCheckTick)
            {
                return;
            }

            ScheduleNext(now);

            try
            {
                if (map?.CanEverExit != true
                    || !WildAnimalEcosystemUtility.IsOverAllowedEcosystemWeight(map, settings.WildAnimalReproductionEcosystemLimitFactor))
                {
                    return;
                }

                Pawn pawn = FindRandomEligiblePawn();
                if (pawn != null)
                {
                    ChildcareFamilyExitMapUtility.TryStartForcedWildExit(pawn);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Zoology: forced wild animal ecosystem exit failed: {ex}");
            }
        }

        private static bool ShouldRun(ZoologyModSettings settings)
        {
            return settings != null
                && !settings.DisableAllRuntimePatches
                && settings.EnableWildAnimalReproduction
                && settings.ForceWildAnimalsToLeaveOnEcosystemOverload;
        }

        private Pawn FindRandomEligiblePawn()
        {
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return null;
            }

            Pawn chosen = null;
            int eligibleCount = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!IsEligibleForcedExitCandidate(pawn))
                {
                    continue;
                }

                eligibleCount++;
                if (Rand.Chance(1f / eligibleCount))
                {
                    chosen = pawn;
                }
            }

            return chosen;
        }

        private bool IsEligibleForcedExitCandidate(Pawn pawn)
        {
            return WildAnimalEcosystemUtility.IsWildAnimal(pawn)
                && ChildcareFamilyExitMapUtility.CanJoinFamilyExit(pawn, map)
                && !ChildcareFamilyExitMapUtility.IsProtectedByGuardingEggMother(pawn);
        }

        private void ScheduleNext(int now)
        {
            nextExitCheckTick = now + ExitIntervalTicks.RandomInRange;
        }
    }
}
