using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    internal static class ZoologyNotificationUtility
    {
        private const int CalmNotificationCooldownTicks = 300;

        private static readonly Dictionary<long, int> lastCalmNotificationTickByPairKey = new Dictionary<long, int>(128);
        public static LookTargets CreateLookTargets(IReadOnlyList<Pawn> pawns, Thing extraThing = null)
        {
            List<Thing> lookTargets = new List<Thing>((pawns?.Count ?? 0) + (extraThing != null ? 1 : 0));

            if (pawns != null)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn == null || pawn.Destroyed)
                    {
                        continue;
                    }

                    lookTargets.Add(pawn);
                }
            }

            if (extraThing != null && !extraThing.Destroyed)
            {
                lookTargets.Add(extraThing);
            }

            return new LookTargets(lookTargets);
        }

        public static string GetCollectiveAnimalLabel(Pawn exemplar, int count)
        {
            if (exemplar == null)
            {
                return string.Empty;
            }

            if (count > 1)
            {
                string plural = exemplar.kindDef?.labelPlural;
                if (plural.NullOrEmpty())
                {
                    plural = exemplar.kindDef?.label ?? exemplar.def?.label;
                }

                if (!plural.NullOrEmpty())
                {
                    return plural.CapitalizeFirst();
                }
            }

            return exemplar.LabelShortCap;
        }

        public static void TryNotifyProtectionEnded(Pawn protector, Pawn aggressor, Thing protectedThing)
        {
            if (protector == null
                || protector.Dead
                || protector.Destroyed
                || aggressor?.Faction != Faction.OfPlayer)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            long pairKey = MakePairKey(aggressor, protectedThing);
            if (pairKey != 0L
                && currentTick > 0
                && lastCalmNotificationTickByPairKey.TryGetValue(pairKey, out int lastTick)
                && currentTick - lastTick < CalmNotificationCooldownTicks)
            {
                return;
            }

            if (pairKey != 0L && currentTick > 0)
            {
                lastCalmNotificationTickByPairKey[pairKey] = currentTick;
            }

            string text = "Zoology_AnimalCalmsDown_Message".Translate(protector.Named("ANIMAL"));
            if (text.NullOrEmpty() || text.Contains("Zoology_AnimalCalmsDown_Message"))
            {
                text = $"{protector.LabelShortCap} calms down.";
            }

            Messages.Message(text.CapitalizeFirst(), protector, MessageTypeDefOf.NeutralEvent, false);
        }

        private static long MakePairKey(Pawn aggressor, Thing protectedThing)
        {
            if (aggressor == null || protectedThing == null)
            {
                return 0L;
            }

            return ((long)(uint)aggressor.thingIDNumber << 32) | (uint)protectedThing.thingIDNumber;
        }
    }
}
