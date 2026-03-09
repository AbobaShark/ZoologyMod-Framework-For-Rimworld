using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace ZoologyMod
{
    internal static class PredationLookupUtility
    {
        private static readonly Dictionary<Type, FieldInfo> carryTrackerPawnFieldByType = new Dictionary<Type, FieldInfo>();

        public static Corpse FindSpawnedCorpseForInnerPawn(Pawn innerPawn)
        {
            if (innerPawn == null)
            {
                return null;
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
    }
}
