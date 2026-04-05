using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobGiver_WanderNearMother : JobGiver_Wander
    {
        private static readonly Dictionary<int, int> scansByMapId = new Dictionary<int, int>(4);
        private static int scansTick = int.MinValue;

        public JobGiver_WanderNearMother()
        {
            this.wanderRadius = 5f;
            this.ticksBetweenWandersRange = new IntRange(
                ZoologyTickLimiter.Childcare.WanderNearMotherMinTicks,
                ZoologyTickLimiter.Childcare.WanderNearMotherMaxTicks);
            this.locomotionUrgency = LocomotionUrgency.Walk;
            this.expiryInterval = -1;
        }

        protected override IntVec3 GetWanderRoot(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.Map == null) return IntVec3.Invalid;
                if (!TryConsumeScanBudget(pawn.Map)) return IntVec3.Invalid;
                if (!ChildcareUtility.IsChildcareEnabled) return IntVec3.Invalid;
                if (!pawn.IsAnimal) return IntVec3.Invalid;
                if (pawn.Dead || pawn.Downed || !pawn.Spawned) return IntVec3.Invalid;

                if (!ChildcareUtility.IsAnimalChild(pawn)) return IntVec3.Invalid;
                if (!ChildcareUtility.HasChildcareExtension(pawn)) return IntVec3.Invalid;

                if (pawn.InMentalState) return IntVec3.Invalid;
                if (IsSleepingOrLyingDown(pawn)) return IntVec3.Invalid;

                if (!ChildcareUtility.TryGetBiologicalMother(pawn, out Pawn mother)) return IntVec3.Invalid;
                if (mother == null || mother.Dead || mother.Destroyed || !mother.Spawned) return IntVec3.Invalid;
                if (mother.Map != pawn.Map) return IntVec3.Invalid;

                return mother.Position;
            }
            catch { return IntVec3.Invalid; }
        }

        private static bool TryConsumeScanBudget(Map map)
        {
            if (map == null)
            {
                return false;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (scansTick != currentTick)
            {
                scansTick = currentTick;
                scansByMapId.Clear();
            }

            int mapId = map.uniqueID;
            scansByMapId.TryGetValue(mapId, out int used);
            if (used >= ZoologyTickLimiter.Childcare.WanderNearMotherScanBudgetPerTickPerMap)
            {
                return false;
            }

            scansByMapId[mapId] = used + 1;
            return true;
        }

        private static bool IsSleepingOrLyingDown(Pawn pawn)
        {
            try
            {
                if (pawn == null) return false;
                if (!pawn.Awake()) return true;

                var curJob = pawn?.jobs?.curJob;
                if (curJob != null)
                {
                    var defName = curJob.def?.defName ?? "";
                    if (!string.IsNullOrEmpty(defName) && defName.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;

                    var driver = pawn.jobs.curDriver;
                    if (driver != null)
                    {
                        var driverName = driver.GetType().Name ?? "";
                        if (!string.IsNullOrEmpty(driverName) && driverName.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }

                return false;
            }
            catch { return false; }
        }
    }
}
