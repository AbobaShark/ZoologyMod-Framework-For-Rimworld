using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    // Reuse the map's existing 16-cell water index; nothing ticks or scans continuously.
    public partial class AnimalRefugeMapComponent
    {
        private const int HerdAnchorMaxDistance = 35;
        private const int HerdAnchorLifetime = 1200;
        private const int WaterSearchRetryTicks = 3000;
        private const int WaterPathCheckBudget = 24;

        private sealed class HerdWaterExitAnchor
        {
            public ThingDef race;
            public Faction faction;
            public IntVec3 waterOrigin;
            public IntVec3 dryAnchor;
            public int expiresAt;
        }

        private struct WaterCandidate
        {
            public IntVec3 cell;
            public int distanceSquared;
        }

        private readonly List<HerdWaterExitAnchor> herdWaterExitAnchors = new List<HerdWaterExitAnchor>(12);
        private readonly Dictionary<int, int> waterSearchRetry = new Dictionary<int, int>(32);
        private readonly Dictionary<int, int> herdEscapeRetry = new Dictionary<int, int>(32);
        private readonly List<WaterCandidate> waterCandidates = new List<WaterCandidate>(32);

        internal bool TryHerdWaterExit(Pawn pawn, out IntVec3 destination)
        {
            destination = IntVec3.Invalid;
            if (pawn?.Map != map || !pawn.Position.GetTerrain(map).IsWater)
                return false;

            int now = GenTicks.TicksGame;
            int pawnId = pawn.thingIDNumber;
            if (herdEscapeRetry.TryGetValue(pawnId, out int retryAt) && retryAt > now)
                return false;
            IntVec3 dryAnchor = IntVec3.Invalid;
            for (int i = herdWaterExitAnchors.Count - 1; i >= 0; i--)
            {
                HerdWaterExitAnchor entry = herdWaterExitAnchors[i];
                if (entry.expiresAt <= now || !entry.dryAnchor.InBounds(map)
                    || entry.dryAnchor.GetTerrain(map).IsWater)
                {
                    herdWaterExitAnchors.RemoveAt(i);
                    continue;
                }

                if (entry.race != pawn.def || entry.faction != pawn.Faction
                    || (pawn.Position - entry.waterOrigin).LengthHorizontalSquared
                        > HerdAnchorMaxDistance * HerdAnchorMaxDistance
                    || entry.dryAnchor.IsForbidden(pawn)
                    || !pawn.CanReach(entry.dryAnchor, PathEndMode.OnCell, Danger.Some))
                    continue;

                dryAnchor = entry.dryAnchor;
                break;
            }

            if (!dryAnchor.IsValid)
            {
                if (!TryFindDryShore(pawn, out dryAnchor))
                {
                    if (herdEscapeRetry.Count >= 256)
                        herdEscapeRetry.Clear();
                    herdEscapeRetry[pawnId] = now + 1500;
                    return false;
                }
                herdEscapeRetry.Remove(pawnId);
                if (herdWaterExitAnchors.Count == 32)
                    herdWaterExitAnchors.RemoveAt(0);
                herdWaterExitAnchors.Add(new HerdWaterExitAnchor
                {
                    race = pawn.def,
                    faction = pawn.Faction,
                    waterOrigin = pawn.Position,
                    dryAnchor = dryAnchor,
                    expiresAt = now + HerdAnchorLifetime
                });
            }

            // Share the shore, not an identical reserved destination cell.
            if (TryDryCellNear(pawn, dryAnchor, out destination))
                return true;
            destination = dryAnchor;
            return !destination.IsForbidden(pawn)
                && pawn.CanReach(destination, PathEndMode.OnCell, Danger.Some);
        }

        private bool TryFindDryShore(Pawn pawn, out IntVec3 shore)
        {
            shore = IntVec3.Invalid;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 48f, true))
            {
                if (!IsSuitableDryCell(pawn, cell)
                    || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                    continue;
                shore = cell;
                return true;
            }
            return false;
        }

        internal bool TryDryCellNear(Pawn pawn, IntVec3 root, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            int start = 1 + (pawn.thingIDNumber & 15);
            for (int i = 0; i < 24; i++)
            {
                IntVec3 cell = root + GenRadial.RadialPattern[1 + ((start + i) % 24)];
                if (cell == pawn.Position || !IsSuitableDryCell(pawn, cell)
                    || !map.pawnDestinationReservationManager.CanReserve(cell, pawn, false)
                    || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                    continue;
                result = cell;
                return true;
            }
            return false;
        }

        private bool IsSuitableDryCell(Pawn pawn, IntVec3 cell)
        {
            return cell.InBounds(map) && cell.Standable(map)
                && !cell.GetTerrain(map).IsWater && !cell.IsForbidden(pawn)
                && !PawnUtility.KnownDangerAt(cell, map, pawn);
        }

        internal bool HasNearbyAccessibleWater(Pawn pawn)
        {
            return TryWaterCell(pawn, pawn.Position, 6f, out IntVec3 nearby)
                && !nearby.IsForbidden(pawn)
                && pawn.CanReach(nearby, PathEndMode.OnCell, Danger.Some);
        }

        internal bool TryFindWaterHabitat(Pawn pawn, out IntVec3 destination)
        {
            destination = IntVec3.Invalid;
            if (pawn?.Map != map)
                return false;

            int now = GenTicks.TicksGame;
            int id = pawn.thingIDNumber;
            if (waterSearchRetry.TryGetValue(id, out int nextTry) && nextTry > now)
                return false;
            if (!TryPlan())
                return false;

            // One nearest water cell per bucket; expensive reachability checks
            // are bounded and made only on idle job decisions.
            waterCandidates.Clear();
            PathGrid grid = map.pathing.For(pawn).pathGrid;
            foreach (KeyValuePair<int, HashSet<IntVec3>> entry in waterBuckets)
            {
                IntVec3 closest = IntVec3.Invalid;
                int distanceSquared = int.MaxValue;
                foreach (IntVec3 cell in entry.Value)
                {
                    int distance = (cell - pawn.Position).LengthHorizontalSquared;
                    if (distance >= distanceSquared || !grid.WalkableFast(cell)
                        || cell.IsForbidden(pawn))
                        continue;
                    closest = cell;
                    distanceSquared = distance;
                }
                if (closest.IsValid)
                    waterCandidates.Add(new WaterCandidate { cell = closest, distanceSquared = distanceSquared });
            }

            waterCandidates.Sort((a, b) => a.distanceSquared.CompareTo(b.distanceSquared));
            int checks = Math.Min(WaterPathCheckBudget, waterCandidates.Count);
            for (int i = 0; i < checks; i++)
            {
                IntVec3 cell = waterCandidates[i].cell;
                if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                    continue;
                destination = cell;
                waterCandidates.Clear();
                waterSearchRetry.Remove(id);
                return true;
            }

            waterCandidates.Clear();
            if (waterSearchRetry.Count >= 256)
                waterSearchRetry.Clear();
            waterSearchRetry[id] = now + WaterSearchRetryTicks;
            return false;
        }
    }

    public sealed class JobGiver_DryHerdWaterEscape : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!AnimalWaterHabitat.CanEscapeWater(pawn)
                || !pawn.Map.GetComponent<AnimalRefugeMapComponent>()
                    .TryHerdWaterExit(pawn, out IntVec3 destination))
                return null;
            return new Job(JobDefOf.GotoWander, destination)
            {
                locomotionUrgency = LocomotionUrgency.Walk,
                expiryInterval = -1,
                checkOverrideOnExpire = true
            };
        }
    }

    public sealed class JobGiver_WaterSeekingHabitat : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!AnimalWaterHabitat.CanSeekWater(pawn))
                return null;
            AnimalRefugeMapComponent index = pawn.Map.GetComponent<AnimalRefugeMapComponent>();
            if (pawn.Position.GetTerrain(pawn.Map).IsWater || index.HasNearbyAccessibleWater(pawn)
                || !index.TryFindWaterHabitat(pawn, out IntVec3 destination))
                return null;
            return new Job(JobDefOf.GotoWander, destination)
            {
                locomotionUrgency = LocomotionUrgency.Walk,
                expiryInterval = -1,
                checkOverrideOnExpire = true
            };
        }
    }

    // A long, fully ordinary Wait_Wander, not a new uninterruptible activity.
    public sealed class JobGiver_EctothermicBasking : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!AnimalWaterHabitat.IsIdleAnimal(pawn)
                || !ZoologyCacheUtility.HasEctothermicExtension(pawn.def)
                || pawn.Position.GetTerrain(pawn.Map).IsWater
                || (ModConstants.Settings is ZoologyModSettings settings
                    && (settings.DisableAllRuntimePatches || !settings.EnableEctothermicPatch))
                || Rand.Value >= 0.7f)
                return null;
            return new Job(JobDefOf.Wait_Wander)
            {
                expiryInterval = Rand.RangeInclusive(1800, 4200)
            };
        }
    }

    internal static class AnimalWaterHabitat
    {
        internal static bool IsIdleAnimal(Pawn pawn)
        {
            return pawn?.IsAnimal == true && pawn.Spawned && !pawn.Dead
                && !pawn.Downed && !pawn.InMentalState && pawn.Awake()
                && pawn.mindState?.duty == null;
        }

        internal static bool IsDryHerd(Pawn pawn)
        {
            return pawn?.IsAnimal == true && pawn.RaceProps.herdAnimal
                && !pawn.RaceProps.waterSeeker;
        }

        internal static bool CanEscapeWater(Pawn pawn)
        {
            return IsIdleAnimal(pawn) && IsDryHerd(pawn)
                && pawn.Position.GetTerrain(pawn.Map).IsWater
                && ModConstants.WaterHabitatBehavior;
        }

        internal static bool CanSeekWater(Pawn pawn)
        {
            return IsIdleAnimal(pawn) && pawn.Faction == null
                && pawn.RaceProps.waterSeeker && ModConstants.WaterHabitatBehavior;
        }
    }
}
