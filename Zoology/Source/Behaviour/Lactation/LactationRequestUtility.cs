using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class LactationRequestUtility
    {
        private readonly struct SuckleRequestEntry
        {
            public SuckleRequestEntry(Pawn pup, Pawn mom, int tick, int mapId)
            {
                Pup = pup;
                Mom = mom;
                Tick = tick;
                MapId = mapId;
            }

            public Pawn Pup { get; }
            public Pawn Mom { get; }
            public int Tick { get; }
            public int MapId { get; }
        }

        private readonly struct MotherRequestCacheEntry
        {
            public MotherRequestCacheEntry(Pawn pup, int tick)
            {
                Pup = pup;
                Tick = tick;
            }

            public Pawn Pup { get; }
            public int Tick { get; }
        }

        private static readonly Dictionary<int, SuckleRequestEntry> requestByPupId = new Dictionary<int, SuckleRequestEntry>(128);
        private static readonly Dictionary<int, MotherRequestCacheEntry> cachedPupByMomId = new Dictionary<int, MotherRequestCacheEntry>(128);

        public static void ResetRuntimeCachesForLoad()
        {
            requestByPupId.Clear();
            cachedPupByMomId.Clear();
        }

        public static bool TryRequestSuckle(Pawn pup)
        {
            if (!AnimalLactationUtility.ChildWantsSuckle(pup))
            {
                ClearRequest(pup);
                return false;
            }

            Pawn mom = AnimalLactationUtility.FindNearestReachableMotherForPup(pup);
            if (mom == null)
            {
                return false;
            }

            return TryRequestSuckle(pup, mom);
        }

        public static bool TryRequestSuckle(Pawn pup, Pawn mom)
        {
            if (!IsValidPup(pup) || !IsValidMom(mom, pup))
            {
                ClearRequest(pup);
                return false;
            }

            if (!AnimalLactationUtility.ChildWantsSuckle(pup))
            {
                ClearRequest(pup);
                return false;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            int pupId = pup.thingIDNumber;
            if (requestByPupId.TryGetValue(pupId, out SuckleRequestEntry existing))
            {
                if (now > 0
                    && now - existing.Tick < ZoologyTickLimiter.Lactation.SuckleRequestCooldownTicks
                    && ReferenceEquals(existing.Mom, mom))
                {
                    return false;
                }
            }

            requestByPupId[pupId] = new SuckleRequestEntry(pup, mom, now, pup.Map?.uniqueID ?? -1);
            if (mom != null)
            {
                cachedPupByMomId.Remove(mom.thingIDNumber);
            }
            return true;
        }

        public static bool TryGetRequestingPup(Pawn mom, out Pawn pup)
        {
            pup = null;
            if (mom == null || mom.Dead || mom.Destroyed || mom.Map == null)
            {
                return false;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            int momId = mom.thingIDNumber;

            if (now > 0
                && cachedPupByMomId.TryGetValue(momId, out MotherRequestCacheEntry cached)
                && now - cached.Tick <= ZoologyTickLimiter.Lactation.SuckleRequestCacheDurationTicks)
            {
                if (IsRequestValid(cached.Pup, mom, now))
                {
                    pup = cached.Pup;
                    return true;
                }

                cachedPupByMomId.Remove(momId);
            }

            Pawn best = null;
            float bestFoodPerc = 1f;
            float bestDistSqr = float.MaxValue;
            List<int> stale = null;

            foreach (KeyValuePair<int, SuckleRequestEntry> entry in requestByPupId)
            {
                SuckleRequestEntry request = entry.Value;
                if (now > 0 && now - request.Tick > ZoologyTickLimiter.Lactation.SuckleRequestDurationTicks)
                {
                    if (stale == null)
                    {
                        stale = new List<int>(8);
                    }
                    stale.Add(entry.Key);
                    continue;
                }

                if (!ReferenceEquals(request.Mom, mom))
                {
                    continue;
                }

                Pawn candidate = request.Pup;
                if (!IsRequestValid(candidate, mom, now))
                {
                    if (stale == null)
                    {
                        stale = new List<int>(8);
                    }
                    stale.Add(entry.Key);
                    continue;
                }

                float foodPerc = candidate.needs?.food?.CurLevelPercentage ?? 1f;
                float distSqr = (candidate.Position - mom.Position).LengthHorizontalSquared;
                const float eps = 1e-6f;

                if (best == null
                    || foodPerc + eps < bestFoodPerc
                    || (Math.Abs(foodPerc - bestFoodPerc) <= eps && distSqr < bestDistSqr))
                {
                    best = candidate;
                    bestFoodPerc = foodPerc;
                    bestDistSqr = distSqr;
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    requestByPupId.Remove(stale[i]);
                }
            }

            if (best != null)
            {
                cachedPupByMomId[momId] = new MotherRequestCacheEntry(best, now);
                pup = best;
                return true;
            }

            return false;
        }

        public static void ClearRequest(Pawn pup)
        {
            if (pup == null)
            {
                return;
            }

            int pupId = pup.thingIDNumber;
            if (requestByPupId.TryGetValue(pupId, out SuckleRequestEntry entry))
            {
                requestByPupId.Remove(pupId);
                if (entry.Mom != null)
                {
                    cachedPupByMomId.Remove(entry.Mom.thingIDNumber);
                }
            }
        }

        private static bool IsRequestValid(Pawn pup, Pawn mom, int now)
        {
            if (!IsValidPup(pup))
            {
                return false;
            }

            if (!IsValidMom(mom, pup))
            {
                return false;
            }

            return AnimalLactationUtility.ChildWantsSuckle(pup);
        }

        private static bool IsValidPup(Pawn pup)
        {
            return pup != null
                && pup.Spawned
                && !pup.Dead
                && !pup.Destroyed
                && !pup.Downed
                && !pup.InMentalState
                && pup.Map != null;
        }

        private static bool IsValidMom(Pawn mom, Pawn pup)
        {
            if (mom == null || pup == null)
            {
                return false;
            }

            if (!AnimalLactationUtility.CanMotherFeed(mom))
            {
                return false;
            }

            if (!AnimalLactationUtility.IsCrossBreedCompatible(mom, pup))
            {
                return false;
            }

            if (!IsFactionCompatible(mom, pup))
            {
                return false;
            }

            if (mom.Map != pup.Map)
            {
                return false;
            }

            return pup.CanReach(mom, PathEndMode.Touch, Danger.Deadly, false, false, TraverseMode.ByPawn);
        }

        private static bool IsFactionCompatible(Pawn mom, Pawn pup)
        {
            if (mom == null || pup == null)
            {
                return false;
            }

            Faction pupFaction = pup.Faction;
            Faction pupHost = pup.HostFaction;

            if (pupFaction == null && pupHost == null)
            {
                return mom.Faction == null && mom.HostFaction == null;
            }

            return mom.Faction == pupFaction
                || mom.Faction == pupHost
                || mom.HostFaction == pupFaction
                || mom.HostFaction == pupHost;
        }
    }
}
