using Verse;

namespace ZoologyMod
{
    internal static class PreyProtectionUtility
    {
        private const int RangeCacheIntervalTicks = 60;
        private static int cachedRange = -1;
        private static int cachedRangeSquared = -1;
        private static int lastRangeCacheTick = -RangeCacheIntervalTicks;

        public static int GetProtectionRange()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick > 0
                && cachedRange >= 0
                && currentTick - lastRangeCacheTick < RangeCacheIntervalTicks)
            {
                return cachedRange;
            }

            int range = (ZoologyModSettings.Instance != null && ZoologyModSettings.Instance.EnablePredatorDefendCorpse)
                ? ZoologyModSettings.Instance.PreyProtectionRange
                : 20;

            if (currentTick > 0)
            {
                cachedRange = range;
                cachedRangeSquared = range * range;
                lastRangeCacheTick = currentTick;
            }

            return range;
        }

        public static int GetProtectionRangeSquared()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick > 0
                && cachedRangeSquared >= 0
                && currentTick - lastRangeCacheTick < RangeCacheIntervalTicks)
            {
                return cachedRangeSquared;
            }

            int range = GetProtectionRange();
            return range * range;
        }

        public static bool TryGetProtectionAnchor(Corpse corpse, out Map map, out IntVec3 position)
        {
            map = null;
            position = IntVec3.Invalid;

            if (corpse == null)
            {
                return false;
            }

            try
            {
                map = corpse.MapHeld;
                if (map == null)
                {
                    return false;
                }

                position = corpse.PositionHeld;
                return position.IsValid;
            }
            catch
            {
                map = null;
                position = IntVec3.Invalid;
                return false;
            }
        }

        public static bool IsThingHeldByPawn(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null)
            {
                return false;
            }

            try
            {
                var carriedThing = pawn.carryTracker?.CarriedThing;
                if (carriedThing != null)
                {
                    if (ReferenceEquals(carriedThing, thing) || carriedThing.thingIDNumber == thing.thingIDNumber)
                    {
                        return true;
                    }
                }

                var innerContainer = pawn.inventory?.innerContainer;
                if (innerContainer == null)
                {
                    return false;
                }

                for (int i = 0; i < innerContainer.Count; i++)
                {
                    Thing containedThing = innerContainer[i];
                    if (containedThing == null)
                    {
                        continue;
                    }

                    if (ReferenceEquals(containedThing, thing) || containedThing.thingIDNumber == thing.thingIDNumber)
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

        public static bool TryGetProtectionAnchor(Corpse corpse, Pawn preferredHolder, out Map map, out IntVec3 position)
        {
            if (TryGetProtectionAnchor(corpse, out map, out position))
            {
                return true;
            }

            map = null;
            position = IntVec3.Invalid;

            if (corpse == null || preferredHolder == null || preferredHolder.Map == null)
            {
                return false;
            }

            if (!IsThingHeldByPawn(preferredHolder, corpse))
            {
                return false;
            }

            map = preferredHolder.Map;
            position = preferredHolder.Position;
            return position.IsValid;
        }

        public static bool IsPawnWithinProtectionRange(Pawn pawn, Map anchorMap, IntVec3 anchorPosition, int maxDistanceSquared)
        {
            if (pawn == null || anchorMap == null || !anchorPosition.IsValid)
            {
                return false;
            }

            if (!pawn.Spawned || pawn.Map != anchorMap)
            {
                return false;
            }

            return (pawn.Position - anchorPosition).LengthHorizontalSquared <= maxDistanceSquared;
        }
    }
}
