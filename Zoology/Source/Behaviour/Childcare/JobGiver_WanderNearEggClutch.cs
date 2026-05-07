using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    public class JobGiver_WanderNearEggClutch : JobGiver_Wander
    {
        private static readonly Dictionary<int, int> scansByMapId = new Dictionary<int, int>(4);
        private static int scansTick = int.MinValue;

        public JobGiver_WanderNearEggClutch()
        {
            this.wanderRadius = 5f;
            this.ticksBetweenWandersRange = new IntRange(
                ZoologyTickLimiter.PreyProtection.WanderNearPreyMinTicks,
                ZoologyTickLimiter.PreyProtection.WanderNearPreyMaxTicks);
            this.locomotionUrgency = LocomotionUrgency.Walk;
            this.expiryInterval = -1;
        }

        protected override IntVec3 GetWanderRoot(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.Map == null)
                {
                    return IntVec3.Invalid;
                }

                if (!TryConsumeScanBudget(pawn.Map)
                    || !ChildcareDefenseUtility.IsEggProtectionEnabled
                    || !pawn.IsAnimal
                    || pawn.Dead
                    || pawn.Downed
                    || !pawn.Spawned
                    || pawn.InMentalState
                    || pawn.gender != Gender.Female
                    || !ChildcareUtility.HasChildcareExtension(pawn)
                    || pawn.TryGetComp<CompEggLayer>() == null
                    || IsSleepingOrLyingDown(pawn))
                {
                    return IntVec3.Invalid;
                }

                EggClutchDefenseGameComponent component = EggClutchDefenseGameComponent.Instance;
                Thing egg = component?.TryGetPairedEggForMother(pawn);
                if (egg == null || !egg.Spawned || egg.Map != pawn.Map)
                {
                    return IntVec3.Invalid;
                }

                IntVec3 eggPosition = egg.Position;
                if (!eggPosition.IsValid
                    || !PreyProtectionUtility.IsPawnWithinProtectionRange(
                        pawn,
                        pawn.Map,
                        eggPosition,
                        PreyProtectionUtility.GetProtectionRangeSquared()))
                {
                    return IntVec3.Invalid;
                }

                return eggPosition;
            }
            catch
            {
                return IntVec3.Invalid;
            }
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
                if (pawn == null)
                {
                    return false;
                }

                if (!pawn.Awake())
                {
                    return true;
                }

                Job curJob = pawn.jobs?.curJob;
                if (curJob != null)
                {
                    string defName = curJob.def?.defName ?? string.Empty;
                    if (!string.IsNullOrEmpty(defName) && defName.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    JobDriver driver = pawn.jobs.curDriver;
                    if (driver != null)
                    {
                        string driverName = driver.GetType().Name ?? string.Empty;
                        if (!string.IsNullOrEmpty(driverName) && driverName.IndexOf("LayDown", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
