"""Regression checks for paired-corpse defense after save reload and failed jobs."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
PAIR = ROOT / "Zoology/Source/Behaviour/Predation/PreyProtection/PredatorPreyPairGameComponent.cs"
CARRY = ROOT / "Zoology/Source/Behaviour/Predation/PreyProtection/Patch_Predation.cs"
DRIVER = PAIR.with_name("JobDriver_ProtectPrey.cs")
NOTIFY = PAIR.parents[2] / "Shared/ZoologyNotificationUtility.cs"


class PairedCorpseDefenseRuntimeContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.src = PAIR.read_text(encoding="utf-8")

    def test_persisted_pairs_survive_reload_but_transient_cooldowns_do_not(self):
        src = self.src
        self.assertIn('Scribe_Collections.Look(ref pairsUntil, "Zoology_pairsUntil"', src)
        self.assertIn("private static void ResetTransientProtectionState()", src)
        reset = src.split("private static void ResetTransientProtectionState()", 1)[1].split(
            "private long PairKeyFor(", 1)[0]
        self.assertIn("lastTriggerAttempt.Clear();", reset)
        self.assertNotIn("pairsUntil.Clear()", reset)
        started = src.split("public override void StartedNewGame()", 1)[1].split(
            "public override void LoadedGame()", 1)[0]
        loaded = src.split("public override void LoadedGame()", 1)[1].split(
            "public override void ExposeData()", 1)[0]
        expose = src.split("public override void ExposeData()", 1)[1].split(
            "private static void ResetTransientProtectionState()", 1)[0]
        self.assertIn("ResetTransientProtectionState();", started)
        self.assertIn("ResetTransientProtectionState();", loaded)
        self.assertIn("ResetTransientProtectionState();", expose)

    def test_direct_theft_ignores_stale_negative_pawn_cache(self):
        src = self.src
        lookup = src.split("private Pawn FindPawnById(", 1)[1].split(
            "private Corpse FindCorpseById(", 1)[0]
        self.assertIn("bool forceFreshLookup = false", lookup)
        self.assertIn("!forceFreshLookup", lookup)
        trigger = src.split("public void TryTriggerDefendFor(", 1)[1].split(
            "public List<Corpse> GetActivePairedCorpses(", 1)[0]
        self.assertIn("FindPawnById(pairPid, forceFreshLookup: true)", trigger)

    def test_defense_cannot_mistake_known_paired_predator_for_missing_owner(self):
        src = self.src
        ownership = src.split("public bool IsCorpseEffectivelyUnownedFor(", 1)[1].split(
            "public void ClearPairsForPredator(", 1)[0]
        self.assertIn("Pawn knownPairedOwner = null", ownership)
        self.assertIn("knownPairedOwner ?? GetOwnerOfCorpse(corpse)", ownership)
        trigger = src.split("public void TryTriggerDefendFor(", 1)[1].split(
            "public List<Corpse> GetActivePairedCorpses(", 1)[0]
        self.assertIn("IsCorpseEffectivelyUnownedFor(interrupter, corpse, pred)", trigger)
        self.assertNotIn("IsCorpseEffectivelyUnownedFor(interrupter, corpse))", trigger)
        self.assertIn("FillActivePredatorIdsForCorpse(cid, now, activePredatorIdBuffer);",
                      trigger)
        self.assertIn("RestoreActivePairsForCorpseOnInteraction(cid, now);", trigger)
        self.assertIn("AddRuntimePairMapping(predatorId, corpseId);", src)

    def test_humans_and_mechanoids_cannot_use_small_scavenger_exception(self):
        src = self.src
        ownership = src.split("public bool IsCorpseEffectivelyUnownedFor(", 1)[1].split(
            "public void ClearPairsForPredator(", 1)[0]
        self.assertIn(
            "if (eater != null && !PawnThreatUtility.IsHumanlikeOrMechanoid(eater))",
            ownership,
        )
        self.assertIn("eaterSize * sizeMultiplier <= ownerSize", ownership)
        # Large predator vs. BodySize~1 colonist must not bypass protection
        # solely by treating the colonist as a small animal scavenger.
        self.assertLess(
            ownership.index("!PawnThreatUtility.IsHumanlikeOrMechanoid(eater)"),
            ownership.index("eaterSize * sizeMultiplier <= ownerSize"),
        )

    def test_failed_jobs_do_not_trigger_cooldown_or_notifications(self):
        trigger = self.src.split("public void TryTriggerDefendFor(", 1)[1].split(
            "public List<Corpse> GetActivePairedCorpses(", 1)[0]
        self.assertIn("taken = pred.jobs.TryTakeOrderedJob(protectJob);", trigger)
        self.assertIn("taken = pred.jobs.TryTakeOrderedJob(attackJob);", trigger)
        self.assertIn("if (!taken) continue;", trigger)
        self.assertIn("lastTriggerAttempt[candidate.PairKey] = now;", trigger)
        self.assertLess(trigger.index("if (!taken) continue;"),
                        trigger.index("lastTriggerAttempt[candidate.PairKey] = now;"))
        self.assertLess(trigger.index("int successfulDefenders = 0;"),
                        trigger.index("ZoologyNotificationUtility.TryNotifyProtectionStarted("))
        self.assertIn("defendCandidateBuffer.RemoveRange(", trigger)
        self.assertIn("defendCandidateBuffer.Count > 0 &&", trigger)

    def test_defense_has_one_notification_source_with_aggregate_severity(self):
        driver = DRIVER.read_text(encoding="utf-8")
        self.assertNotIn("LetterLabelPredatorProtectingPrey", driver)
        self.assertNotIn("ReceiveLetter(", driver)
        self.assertNotIn("Messages.Message(", driver)
        self.assertNotIn("Notify_PredatorHuntingPlayerNotification(", driver)
        trigger = self.src.split("public void TryTriggerDefendFor(", 1)[1].split(
            "public List<Corpse> GetActivePairedCorpses(", 1)[0]
        self.assertNotIn("Find.LetterStack.ReceiveLetter(", trigger)
        self.assertNotIn("Messages.Message(", trigger)
        self.assertEqual(trigger.count("ZoologyNotificationUtility.TryNotifyProtectionStarted("), 1)
        self.assertIn("alreadyActive = true;", trigger)
        self.assertIn("interrupter, corpse, protectors, alreadyActive", trigger)
        self.assertIn("for (int i = 0; i < protectors.Count; i++)", trigger)
        self.assertIn("protectors[i].mindState?.Notify_PredatorHuntingPlayerNotification();", trigger)
        self.assertLess(trigger.index("if (!taken) continue;"),
                        trigger.index("ZoologyNotificationUtility.TryNotifyProtectionStarted("))
        notify = NOTIFY.read_text(encoding="utf-8")
        self.assertIn("bool pack = protectors.Count > 1;", notify)
        self.assertIn("pack ? LetterDefOf.ThreatBig : LetterDefOf.ThreatSmall", notify)
        self.assertIn("pack ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.ThreatSmall", notify)

    def test_carry_event_still_triggers_direct_protection(self):
        carry = CARRY.read_text(encoding="utf-8")
        self.assertIn("PawnCarry_TryStartCarry_Postfix(", carry)
        self.assertIn("PawnCarry_TryStartCarry_Int_Postfix(", carry)
        self.assertIn("comp.TryTriggerDefendFor(corpse, carrier);", carry)
        self.assertIn("PawnCarry_TryStartCarry", carry)


if __name__ == "__main__":
    unittest.main()
