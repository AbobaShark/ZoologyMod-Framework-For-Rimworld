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

        // Vanilla may issue Wait_Wander before asking for a wander root.
        // Check eligibility first so unrelated animals retain their own wander AI.
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ChildcareUtility.IsChildcareEnabled
                || pawn?.IsAnimal != true || !pawn.Spawned || pawn.Dead || pawn.Downed
                || pawn.InMentalState || !pawn.Awake()
                || !ChildcareUtility.IsAnimalChild(pawn)
                || !ChildcareUtility.HasChildcareExtension(pawn))
            {
                return null;
            }

            if (!ChildcareUtility.TryGetBiologicalMother(pawn, out Pawn mother)
                || mother == null || !mother.Spawned || mother.Map != pawn.Map)
            {
                return null;
            }

            // Juveniles keep their own area assignment. When it differs from
            // the mother's, let the normal animal wander job giver take over.
            if (ChildcareUtility.IsAnimalJuvenileLifeStage(pawn.ageTracker?.CurLifeStage))
            {
                Area juvenileArea = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                Area motherArea = mother.playerSettings?.AreaRestrictionInPawnCurrentMap;
                if (!object.ReferenceEquals(juvenileArea, motherArea)
                    || (juvenileArea != null && !juvenileArea[mother.Position]))
                {
                    return null;
                }
            }

            // A mother across impassable doors or fences is not a viable wander root.
            // Let the ordinary animal wander logic handle this child instead.
            if (!TryConsumeScanBudget(pawn.Map)
                || !ChildcareMotherReachability.CanFollow(pawn, mother))
            {
                return null;
            }

            return base.TryGiveJob(pawn);
        }

        protected override IntVec3 GetWanderRoot(Pawn pawn)
        {
            if (!ChildcareUtility.TryGetBiologicalMother(pawn, out Pawn mother)
                || mother == null || !mother.Spawned || mother.Map != pawn.Map)
            {
                return IntVec3.Invalid;
            }

            Area ownArea = pawn.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap;
            return ownArea != null && !ownArea[mother.Position] ? pawn.Position : mother.Position;
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

    }
}
