using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    // Odyssey's Attack Target gizmos check the master's range independently of drafted movement.
    // Keep the vanilla order for nearby targets and only extend it for eligible animals.
    internal static class AnimalDraftAttackTargetRange
    {
        private static readonly string IndividualDescription = "AnimalAttackTargetDesc".Translate().ToString();
        private static readonly string MasterDescription = "AnimalsAttackTargetDesc".Translate().ToString();

        // Spectrobionta replaces the master's group gizmo to exclude dormant creatures.
        // It implements the per-animal range rule itself; do not wrap that replacement twice.
        internal static readonly bool SpectrobiontaOwnsMasterGizmo =
            AccessTools.TypeByName("SpectrobiontaMod.SpectrobiontaAttackTargetUtility") != null;

        internal static bool IsUnlimitedRangeAttacker(Pawn animal)
        {
            return ModsConfig.OdysseyActive
                && TrainableDefOf.AttackTarget != null
                && animal != null
                && animal.Spawned
                && animal.training != null
                && animal.training.HasLearned(TrainableDefOf.AttackTarget)
                && AnimalDraftControlUtility.IsDraftControlCandidate(animal)
                && animal.def.GetModExtension<ModExtension_AnimalDraftRules>()?.HasUnlimitedRange(animal) == true
                && AnimalDraftControlUtility.CanDoDraftControl(animal).Accepted;
        }

        internal static bool IsIndividualCommand(Command_Target command)
        {
            return command != null
                && command.icon == Pawn_TrainingTracker.AttackTargetTexture
                && command.hotKey == KeyBindingDefOf.Misc2
                && command.defaultDesc == IndividualDescription;
        }

        internal static bool IsMasterCommand(Command_Target command)
        {
            return command != null
                && command.icon == Pawn_TrainingTracker.AttackTargetTexture
                && command.hotKey == KeyBindingDefOf.Misc8
                && command.defaultDesc == MasterDescription;
        }

        internal static bool AnySelectedUnlimitedRangeAttacker()
        {
            List<object> selected = Find.Selector?.SelectedObjectsListForReading;
            if (selected == null)
            {
                return false;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                if (IsUnlimitedRangeAttacker(selected[i] as Pawn))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool AnySelectedRangeLimitedAttacker()
        {
            List<object> selected = Find.Selector?.SelectedObjectsListForReading;
            if (selected == null)
            {
                return false;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                Pawn animal = selected[i] as Pawn;
                if (animal?.training?.HasLearned(TrainableDefOf.AttackTarget) == true
                    && animal.playerSettings?.Master != null
                    && !IsUnlimitedRangeAttacker(animal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool HasUnlimitedMasteredAttacker(Pawn master)
        {
            if (master == null || !master.Spawned)
            {
                return false;
            }

            foreach (Pawn animal in PawnUtility.SpawnedMasteredPawns(master))
            {
                if (IsUnlimitedRangeAttacker(animal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool HasLimitedMasteredAttacker(Pawn master)
        {
            if (master == null || !master.Spawned)
            {
                return false;
            }

            foreach (Pawn animal in PawnUtility.SpawnedMasteredPawns(master))
            {
                if (animal?.training?.HasLearned(TrainableDefOf.AttackTarget) == true
                    && !IsUnlimitedRangeAttacker(animal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryOrder(Pawn animal, LocalTargetInfo target, out string failure)
        {
            failure = null;
            if (!IsUnlimitedRangeAttacker(animal)
                || target.Thing == null
                || target.Thing.MapHeld != animal.MapHeld
                || !target.Cell.InBounds(animal.MapHeld))
            {
                return false;
            }

            Action attack = FloatMenuUtility.GetMeleeAttackAction(animal, target, out failure, ignoreControlled: true);
            if (attack == null || !failure.NullOrEmpty())
            {
                return false;
            }

            attack();
            animal.training.attackTarget = target.Thing;
            animal.mindState.enemyTarget = target.Thing;
            return true;
        }

        private static void IssueRemoteOrders(IEnumerable<Pawn> animals, LocalTargetInfo target)
        {
            bool ordered = false;
            string failure = null;
            foreach (Pawn animal in animals)
            {
                if (!IsUnlimitedRangeAttacker(animal))
                {
                    continue;
                }

                if (TryOrder(animal, target, out string currentFailure))
                {
                    ordered = true;
                }
                else if (failure.NullOrEmpty() && !currentFailure.NullOrEmpty())
                {
                    failure = currentFailure;
                }
            }

            if (!ordered)
            {
                Messages.Message(
                    failure.NullOrEmpty() ? "MessageNotInRangeOfMaster".Translate() : failure,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }
        }

        internal static void IssueRemoteSelected(LocalTargetInfo target)
        {
            List<object> selected = Find.Selector?.SelectedObjectsListForReading;
            if (selected == null)
            {
                return;
            }

            List<Pawn> animals = new List<Pawn>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] is Pawn pawn)
                {
                    animals.Add(pawn);
                }
            }

            IssueRemoteOrders(animals, target);
        }

        internal static void IssueRemoteMastered(Pawn master, LocalTargetInfo target)
        {
            if (master == null || !master.Spawned)
            {
                return;
            }

            IssueRemoteOrders(PawnUtility.SpawnedMasteredPawns(master), target);
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), "CanDoAttackTarget")]
    internal static class Patch_CanDoAttackTarget_MasterlessZoology
    {
        private static void Postfix(Pawn pawn, ref AcceptanceReport __result)
        {
            // Only bypass missing/unavailable-master failures, not other restrictions
            // (including Spectrobionta's dormancy guard).
            if (!__result.Accepted
                && (__result.Reason == "NoMaster".Translate().ToString()
                    || __result.Reason == "MasterNotSpawned".Translate().ToString())
                && pawn?.def.GetModExtension<ModExtension_AnimalDraftRules>()?.CanDraftWithoutMaster(pawn) == true
                && AnimalDraftAttackTargetRange.IsUnlimitedRangeAttacker(pawn))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.GetGizmos))]
    internal static class Patch_AnimalAttackTargetGizmo_UnlimitedRange
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn_TrainingTracker __instance)
        {
            if (__result == null)
            {
                yield break;
            }

            Pawn source = __instance?.pawn;
            bool hasUnlimited = AnimalDraftAttackTargetRange.AnySelectedUnlimitedRangeAttacker();
            foreach (Gizmo gizmo in __result)
            {
                if (hasUnlimited
                    && gizmo is Command_Target command
                    && AnimalDraftAttackTargetRange.IsIndividualCommand(command))
                {
                    Action<LocalTargetInfo> originalAction = command.action;
                    Action<LocalTargetInfo> originalUpdate = command.onUpdate;
                    bool drawMasterRange = AnimalDraftAttackTargetRange.AnySelectedRangeLimitedAttacker();
                    command.action = target =>
                    {
                        Pawn master = source?.playerSettings?.Master;
                        if (master != null && master.Spawned
                            && source?.MapHeld == master.MapHeld
                            && target.IsValid
                            && target.Cell.InHorDistOf(master.Position, Pawn_TrainingTracker.AttackTargetRange))
                        {
                            originalAction?.Invoke(target);
                        }
                        else
                        {
                            AnimalDraftAttackTargetRange.IssueRemoteSelected(target);
                        }
                    };

                    command.onUpdate = target =>
                    {
                        if (drawMasterRange)
                        {
                            originalUpdate?.Invoke(target);
                        }
                    };

                }

                yield return gizmo;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.GetGizmos))]
    internal static class Patch_MasterAttackTargetGizmo_UnlimitedRange
    {
        private static readonly AccessTools.FieldRef<Pawn_PlayerSettings, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_PlayerSettings, Pawn>("pawn");

        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn_PlayerSettings __instance)
        {
            if (__result == null)
            {
                yield break;
            }

            Pawn master = PawnRef(__instance);
            bool hasUnlimited = !AnimalDraftAttackTargetRange.SpectrobiontaOwnsMasterGizmo
                && AnimalDraftAttackTargetRange.HasUnlimitedMasteredAttacker(master);

            foreach (Gizmo gizmo in __result)
            {
                if (hasUnlimited
                    && gizmo is Command_Target command
                    && AnimalDraftAttackTargetRange.IsMasterCommand(command))
                {
                    Action<LocalTargetInfo> originalAction = command.action;
                    Action<LocalTargetInfo> originalUpdate = command.onUpdate;
                    bool drawMasterRange = AnimalDraftAttackTargetRange.HasLimitedMasteredAttacker(master);
                    command.action = target =>
                    {
                        if (master != null && master.Spawned
                            && target.IsValid
                            && target.Cell.InHorDistOf(master.Position, Pawn_TrainingTracker.AttackTargetRange))
                        {
                            originalAction?.Invoke(target);
                        }
                        else
                        {
                            AnimalDraftAttackTargetRange.IssueRemoteMastered(master, target);
                        }
                    };
                    command.onUpdate = target =>
                    {
                        if (drawMasterRange)
                        {
                            originalUpdate?.Invoke(target);
                        }
                    };
                }

                yield return gizmo;
            }
        }
    }
}
