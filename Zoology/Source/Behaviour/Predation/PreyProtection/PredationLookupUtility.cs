using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace ZoologyMod
{
    internal static class PredationLookupUtility
    {
        private static readonly Dictionary<Type, FieldInfo> carryTrackerPawnFieldByType = new Dictionary<Type, FieldInfo>();
        private readonly struct HolderCacheEntry
        {
            public HolderCacheEntry(Pawn holder, int tick)
            {
                Holder = holder;
                Tick = tick;
            }

            public Pawn Holder { get; }
            public int Tick { get; }
        }

        private static readonly Dictionary<int, HolderCacheEntry> holderCacheByThingId = new Dictionary<int, HolderCacheEntry>(128);
        private const int HolderCacheDurationTicks = 1;

        public static Corpse FindSpawnedCorpseForInnerPawn(Pawn innerPawn)
        {
            if (innerPawn == null)
            {
                return null;
            }

            Corpse directCorpse = innerPawn.Corpse;
            if (directCorpse != null && directCorpse.Spawned)
            {
                return directCorpse;
            }

            Map pawnMap = innerPawn.Map;
            if (pawnMap != null)
            {
                var localCorpses = pawnMap.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                if (localCorpses != null)
                {
                    for (int ci = 0; ci < localCorpses.Count; ci++)
                    {
                        if (localCorpses[ci] is Corpse corpse && corpse.InnerPawn == innerPawn)
                        {
                            return corpse;
                        }
                    }
                }
            }

            var maps = Find.Maps;
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var corpses = maps[mi].listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
                if (corpses == null)
                {
                    continue;
                }

                for (int ci = 0; ci < corpses.Count; ci++)
                {
                    if (corpses[ci] is Corpse corpse && corpse.InnerPawn == innerPawn)
                    {
                        return corpse;
                    }
                }
            }

            return null;
        }

        public static Pawn TryGetCarrierPawn(object trackerInstance)
        {
            if (trackerInstance == null)
            {
                return null;
            }

            try
            {
                var trackerType = trackerInstance.GetType();
                if (!carryTrackerPawnFieldByType.TryGetValue(trackerType, out var pawnField))
                {
                    pawnField = trackerType.GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    carryTrackerPawnFieldByType[trackerType] = pawnField;
                }

                return pawnField?.GetValue(trackerInstance) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        public static Pawn FindPawnHoldingThing(int thingId)
        {
            if (thingId <= 0)
            {
                return null;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick > 0
                && holderCacheByThingId.TryGetValue(thingId, out HolderCacheEntry cached)
                && currentTick - cached.Tick <= HolderCacheDurationTicks)
            {
                if (IsPawnHoldingThing(cached.Holder, thingId))
                {
                    return cached.Holder;
                }

                holderCacheByThingId.Remove(thingId);
            }

            var maps = Find.Maps;
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var pawns = maps[mi].mapPawns.AllPawnsSpawned;
                for (int pi = 0; pi < pawns.Count; pi++)
                {
                    var pawn = pawns[pi];
                    if (pawn == null)
                    {
                        continue;
                    }

                    try
                    {
                        var carriedThing = pawn.carryTracker?.CarriedThing;
                        if (carriedThing != null && carriedThing.thingIDNumber == thingId)
                        {
                            if (currentTick > 0)
                            {
                                holderCacheByThingId[thingId] = new HolderCacheEntry(pawn, currentTick);
                            }
                            return pawn;
                        }

                        var inventory = pawn.inventory?.innerContainer;
                        if (inventory == null)
                        {
                            continue;
                        }

                        for (int ii = 0; ii < inventory.Count; ii++)
                        {
                            var item = inventory[ii];
                            if (item != null && item.thingIDNumber == thingId)
                            {
                                if (currentTick > 0)
                                {
                                    holderCacheByThingId[thingId] = new HolderCacheEntry(pawn, currentTick);
                                }
                                return pawn;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static bool IsPawnHoldingThing(Pawn pawn, int thingId)
        {
            if (pawn == null)
            {
                return false;
            }

            try
            {
                var carriedThing = pawn.carryTracker?.CarriedThing;
                if (carriedThing != null && carriedThing.thingIDNumber == thingId)
                {
                    return true;
                }

                var inventory = pawn.inventory?.innerContainer;
                if (inventory == null)
                {
                    return false;
                }

                for (int i = 0; i < inventory.Count; i++)
                {
                    var item = inventory[i];
                    if (item != null && item.thingIDNumber == thingId)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
