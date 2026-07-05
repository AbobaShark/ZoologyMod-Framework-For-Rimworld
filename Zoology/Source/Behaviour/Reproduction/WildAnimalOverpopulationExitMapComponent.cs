using System;
using System.Collections.Generic;
using Verse;

namespace ZoologyMod
{
    public sealed class WildAnimalOverpopulationExitMapComponent : MapComponent
    {
        private static readonly IntRange InitialCheckDelayTicks = new IntRange(2500, 5000);

        private const int NormalCheckIntervalTicks = 60000;
        private const int MildOverloadExitIntervalTicks = 60000;
        private const int SevereOverloadExitIntervalTicks = 2500;
        private const float SevereOverloadRatio = 2f;

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
            int now = GenTicks.TicksGame;
            if (nextExitCheckTick < now || nextExitCheckTick - now > NormalCheckIntervalTicks)
            {
                ScheduleInitialCheck(now);
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
                ScheduleInitialCheck(now);
                return;
            }

            if (nextExitCheckTick - now > NormalCheckIntervalTicks)
            {
                ScheduleInitialCheck(now);
                return;
            }

            if (now < nextExitCheckTick)
            {
                return;
            }

            try
            {
                if (map?.CanEverExit != true
                    || !WildAnimalEcosystemUtility.TryGetOverloadStatus(
                        map,
                        settings.WildAnimalReproductionEcosystemLimitFactor,
                        out _,
                        out _,
                        out float overloadRatio))
                {
                    ScheduleNormalCheck(now);
                    return;
                }

                Pawn pawn = FindRandomEligiblePawn();
                if (pawn != null)
                {
                    ChildcareFamilyExitMapUtility.TryStartForcedWildExit(pawn);
                }

                ScheduleOverloadedCheck(now, overloadRatio);
            }
            catch (Exception ex)
            {
                ScheduleNormalCheck(now);
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

        private void ScheduleInitialCheck(int now)
        {
            nextExitCheckTick = now + InitialCheckDelayTicks.RandomInRange;
        }

        private void ScheduleNormalCheck(int now)
        {
            nextExitCheckTick = now + NormalCheckIntervalTicks;
        }

        private void ScheduleOverloadedCheck(int now, float overloadRatio)
        {
            nextExitCheckTick = now + (overloadRatio >= SevereOverloadRatio
                ? SevereOverloadExitIntervalTicks
                : MildOverloadExitIntervalTicks);
        }
    }
}
