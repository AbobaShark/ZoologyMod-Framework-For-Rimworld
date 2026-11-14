using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

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

        public static bool IsActiveProtection(Pawn protector, Pawn aggressor, Thing protectedThing)
        {
            if (protector == null || aggressor == null || protectedThing == null
                || !protector.Spawned || protector.Dead || protector.Downed
                || protector.Destroyed || protector.InMentalState
                || !aggressor.Spawned || aggressor.Dead || aggressor.Downed
                || protector.Map != aggressor.Map)
            {
                return false;
            }

            Job job = protector.CurJob;
            if (job == null || !ReferenceEquals(job.GetTarget(TargetIndex.A).Thing, aggressor))
                return false;

            Thing currentProtectedThing = job.GetTarget(TargetIndex.B).Thing;
            if (currentProtectedThing == null || currentProtectedThing.Destroyed
                || currentProtectedThing.MapHeld != protector.Map
                || (currentProtectedThing is Pawn child && child.Dead))
                return false;

            if (protectedThing is Corpse)
            {
                return ReferenceEquals(currentProtectedThing, protectedThing)
                    && (job.def == JobDefOf.AttackMelee || ProtectPreyState.IsProtectPreyJob(protector));
            }

            // A parent already confronting this attacker is defending the same family.
            return !(currentProtectedThing is Corpse)
                && (job.def == JobDefOf.AttackMelee || ProtectYoungUtility.IsProtectYoungJob(job, protector));
        }

        public static void TryNotifyProtectionStarted(
            Pawn aggressor, Thing protectedThing, IReadOnlyList<Pawn> protectors,
            bool alreadyActive, bool eggClutch = false)
        {
            if (alreadyActive || aggressor?.Faction != Faction.OfPlayer
                || protectedThing == null || protectors == null || protectors.Count == 0)
                return;

            try
            {
                Pawn exemplar = protectors[0];
                bool pack = protectors.Count > 1;
                bool prey = protectedThing is Corpse;
                string key = prey ? "PredatorProtectingPrey"
                    : eggClutch ? "AnimalProtectingEggs" : "MotherProtectingYoung";
                string labelKey = "LetterLabel" + key + (pack ? "Pack" : "");
                string textKey = "Letter" + key + (pack ? "Pack" : "");
                string collectiveLabel = GetCollectiveAnimalLabel(exemplar, protectors.Count);
                string subjectName = prey ? "PREDATOR" : "PARENT";
                string aggressorName = prey || eggClutch ? "PREY" : "ATTACKER";
                string label = pack
                    ? labelKey.Translate(collectiveLabel)
                    : labelKey.Translate(exemplar.LabelShort, aggressor.LabelDefinite(),
                        exemplar.Named(subjectName), aggressor.Named(aggressorName));
                string text = pack
                    ? textKey.Translate(collectiveLabel, aggressor.LabelDefinite())
                    : textKey.Translate(exemplar.LabelIndefinite(), aggressor.LabelDefinite(),
                        exemplar.Named(subjectName), aggressor.Named(aggressorName));
                string subject = pack ? collectiveLabel : exemplar.LabelShort;
                string protectedLabel = prey ? (pack ? "their prey" : "its prey")
                    : eggClutch ? (pack ? "a clutch" : "its clutch")
                    : (pack ? "their young" : "its young");
                string fallback = subject + (pack ? " are protecting " : " is protecting ") + protectedLabel;
                if (label.NullOrEmpty() || label.Contains(labelKey))
                    label = fallback;
                if (text.NullOrEmpty() || text.Contains(textKey))
                    text = fallback + (pack ? " and are attacking " : " and is attacking ") + aggressor.LabelDefinite() + ".";

                LookTargets lookTargets = CreateLookTargets(protectors, protectedThing);
                if (PawnThreatUtility.IsHumanlikeOrMechanoid(aggressor))
                {
                    Find.LetterStack.ReceiveLetter(label.CapitalizeFirst(), text.CapitalizeFirst(),
                        pack ? LetterDefOf.ThreatBig : LetterDefOf.ThreatSmall, lookTargets);
                }
                else
                {
                    Messages.Message(text.CapitalizeFirst(), lookTargets,
                        pack ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.ThreatSmall, true);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"Zoology: protection notification failed: {ex}");
            }
        }

        // Only a completed, non-interrupted defense whose aggressor
        // actually retreated is a calm-down event. A job ending because its
        // actor was injured, incapacitated, killed or entered a mental state
        // must never announce that the animal has calmed down.
        public static void TryNotifyProtectionEnded(
            Pawn protector, Pawn aggressor, Thing protectedThing,
            JobCondition condition, bool threatRetreated)
        {
            if (condition != JobCondition.Succeeded || !threatRetreated
                || protector?.Spawned != true || protector.Dead
                || protector.Downed || protector.Destroyed
                || protector.InMentalState
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
