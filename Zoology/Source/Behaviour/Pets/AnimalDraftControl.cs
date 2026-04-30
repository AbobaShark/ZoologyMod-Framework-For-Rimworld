using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZoologyMod
{
    internal static class AnimalDraftControlUtility
    {
        internal const string DraftControlTrainableDefName = "Zoology_DraftControl";
        private static readonly string DraftToggleDescText = "CommandToggleDraftDesc".Translate().ToString();
        private static readonly string AnimalAttackTargetLabelText = "AnimalAttackTarget".Translate().ToString();
        private static readonly string CancelAttackLabelText = "CancelAttack".Translate().ToString();
        private static TrainableDef cachedDraftControlTrainable;
        private static HediffDef cachedSentienceCatalystHediff;
        private static readonly HashSet<Pawn> masterRingCache = new HashSet<Pawn>();
        private static int masterRingCacheFrame = -1;
        private static int anySelectedDraftControlFrame = -1;
        private static bool anySelectedDraftControl;
        private static int draftedMoveContextDepth;
        private static bool draftedMoveContextIsMultiselect;
        private static bool loggedLinkedTrainingSyncFailure;
        private static readonly AccessTools.FieldRef<Pawn_TrainingTracker, DefMap<TrainableDef, bool>> WantedTrainablesRef =
            AccessTools.FieldRefAccess<Pawn_TrainingTracker, DefMap<TrainableDef, bool>>("wantedTrainables");
        private static readonly AccessTools.FieldRef<Pawn_TrainingTracker, DefMap<TrainableDef, int>> StepsRef =
            AccessTools.FieldRefAccess<Pawn_TrainingTracker, DefMap<TrainableDef, int>>("steps");
        private static readonly AccessTools.FieldRef<Pawn_TrainingTracker, DefMap<TrainableDef, bool>> LearnedRef =
            AccessTools.FieldRefAccess<Pawn_TrainingTracker, DefMap<TrainableDef, bool>>("learned");

        internal static TrainableDef DraftControlTrainable =>
            cachedDraftControlTrainable ??= DefDatabase<TrainableDef>.GetNamedSilentFail(DraftControlTrainableDefName);

        internal static HediffDef SentienceCatalystHediff =>
            cachedSentienceCatalystHediff ??= DefDatabase<HediffDef>.GetNamedSilentFail("SentienceCatalyst");

        internal static bool IsFeatureEnabledNow()
        {
            ZoologyModSettings settings = ZoologyMod.Settings ?? ZoologyModSettings.Instance;
            return settings == null || (!settings.DisableAllRuntimePatches && settings.EnableAnimalDraftControl);
        }

        internal static bool HasSentienceCatalyst(Pawn pawn)
        {
            if (!ModsConfig.OdysseyActive || pawn?.health?.hediffSet == null)
            {
                return false;
            }

            HediffDef catalyst = SentienceCatalystHediff;
            if (catalyst == null)
            {
                return false;
            }

            return pawn.health.hediffSet.HasHediff(catalyst);
        }

        internal static bool HasDraftControlAccess(Pawn pawn, TrainableDef draftControl)
        {
            return HasDraftControlAccess(pawn, pawn?.def, draftControl);
        }

        internal static bool HasDraftControlAccess(Pawn pawn, ThingDef raceDef, TrainableDef draftControl)
        {
            if (pawn == null || draftControl == null)
            {
                return raceDef?.race?.specialTrainables?.Contains(draftControl) == true;
            }

            List<TrainableDef> specialTrainables = pawn.def?.race?.specialTrainables ?? raceDef?.race?.specialTrainables;
            if (specialTrainables != null && specialTrainables.Contains(draftControl))
            {
                return true;
            }

            return HasSentienceCatalyst(pawn);
        }

        internal static bool IsDraftControlCandidate(Pawn pawn)
        {
            if (!IsFeatureEnabledNow())
            {
                return false;
            }

            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (pawn.RaceProps == null || !pawn.RaceProps.Animal || pawn.training == null)
            {
                return false;
            }

            TrainableDef draftControl = DraftControlTrainable;
            if (draftControl == null)
            {
                return false;
            }

            if (!HasDraftControlAccess(pawn, draftControl))
            {
                return false;
            }

            return pawn.training.HasLearned(draftControl);
        }

        internal static bool IsDraftControlActivePawn(Pawn pawn)
        {
            return pawn != null
                && pawn.Drafted
                && IsDraftControlCandidate(pawn)
                && CanDoDraftControl(pawn).Accepted;
        }

        internal static bool ShouldHideAttackTargetCommands(Pawn pawn)
        {
            return IsFeatureEnabledNow() && ShouldLinkTrainables(pawn);
        }

        internal static bool IsAttackTargetCommandGizmo(Gizmo gizmo)
        {
            if (gizmo is not Command command)
            {
                return false;
            }

            if (command.icon == Pawn_TrainingTracker.AttackTargetTexture)
            {
                return true;
            }

            string label = command.defaultLabel;
            if (label.NullOrEmpty())
            {
                return false;
            }

            return label == AnimalAttackTargetLabelText || label == CancelAttackLabelText;
        }

        internal static bool TryGetLinkedTrainables(out TrainableDef attackTarget, out TrainableDef draftControl)
        {
            attackTarget = TrainableDefOf.AttackTarget;
            draftControl = DraftControlTrainable;
            return attackTarget != null && draftControl != null;
        }

        internal static bool IsLinkedTrainable(TrainableDef td)
        {
            if (!TryGetLinkedTrainables(out TrainableDef attackTarget, out TrainableDef draftControl))
            {
                return false;
            }

            return td == attackTarget || td == draftControl;
        }

        internal static bool ShouldLinkTrainables(Pawn pawn)
        {
            if (pawn?.training == null || !TryGetLinkedTrainables(out _, out TrainableDef draftControl))
            {
                return false;
            }

            return HasDraftControlAccess(pawn, draftControl);
        }

        internal static void SyncLinkedTraining(Pawn_TrainingTracker tracker)
        {
            Pawn pawn = tracker?.pawn;
            if (pawn == null || !ShouldLinkTrainables(pawn))
            {
                return;
            }

            if (!TryGetLinkedTrainables(out TrainableDef attackTarget, out TrainableDef draftControl))
            {
                return;
            }

            DefMap<TrainableDef, int> steps = StepsRef(tracker);
            DefMap<TrainableDef, bool> learned = LearnedRef(tracker);
            DefMap<TrainableDef, bool> wanted = WantedTrainablesRef(tracker);
            if (steps == null || learned == null || wanted == null)
            {
                return;
            }

            try
            {
                int attackSteps = steps[attackTarget];
                int draftSteps = steps[draftControl];
                int sharedSteps = Math.Max(attackSteps, draftSteps);
                if (attackSteps != sharedSteps)
                {
                    steps[attackTarget] = sharedSteps;
                }

                if (draftSteps != sharedSteps)
                {
                    steps[draftControl] = sharedSteps;
                }

                bool sharedWanted = wanted[attackTarget] || wanted[draftControl];
                if (wanted[attackTarget] != sharedWanted)
                {
                    wanted[attackTarget] = sharedWanted;
                }

                if (wanted[draftControl] != sharedWanted)
                {
                    wanted[draftControl] = sharedWanted;
                }

                bool attackLearned = learned[attackTarget] || learned[draftControl] || sharedSteps >= attackTarget.steps;
                bool draftLearned = learned[draftControl] || learned[attackTarget] || sharedSteps >= draftControl.steps;
                if (learned[attackTarget] != attackLearned)
                {
                    learned[attackTarget] = attackLearned;
                }

                if (learned[draftControl] != draftLearned)
                {
                    learned[draftControl] = draftLearned;
                }
            }
            catch (Exception ex)
            {
                if (!loggedLinkedTrainingSyncFailure)
                {
                    loggedLinkedTrainingSyncFailure = true;
                    Log.Warning("[Zoology] Skipped linked training sync because training maps are not fully initialized yet. " + ex.GetType().Name);
                }
            }
        }

        internal static int GetLinkedSteps(Pawn_TrainingTracker tracker, TrainableDef td, int fallback)
        {
            if (tracker == null || !IsLinkedTrainable(td))
            {
                return fallback;
            }

            DefMap<TrainableDef, int> steps = StepsRef(tracker);
            if (steps == null)
            {
                return fallback;
            }

            return steps[td];
        }

        internal static bool GetLinkedLearned(Pawn_TrainingTracker tracker, TrainableDef td, bool fallback)
        {
            if (tracker == null || !IsLinkedTrainable(td))
            {
                return fallback;
            }

            DefMap<TrainableDef, bool> learned = LearnedRef(tracker);
            if (learned == null)
            {
                return fallback;
            }

            return learned[td];
        }

        internal static AcceptanceReport CanDoDraftControl(Pawn pawn)
        {
            if (!IsDraftControlCandidate(pawn))
            {
                return false;
            }

            if (pawn.GetLord()?.LordJob is LordJob_Ritual && pawn.drafter?.Drafted != true)
            {
                return "ZoologyCannotDraftDuringRitual".Translate();
            }

            if (pawn.Downed)
            {
                return "IsIncapped".Translate(pawn.LabelShort, pawn);
            }

            if (pawn.Deathresting)
            {
                return "IsDeathresting".Translate(pawn.Named("PAWN"));
            }

            if (pawn.InMentalState)
            {
                return "InMentalState".Translate(pawn.Named("PAWN"), pawn.MentalStateDef.Named("MENTALSTATE"));
            }

            if (pawn.playerSettings?.Master == null)
            {
                return "NoMaster".Translate();
            }

            Pawn master = pawn.playerSettings.Master;
            if (!master.Spawned || master.Map != pawn.Map)
            {
                return "MasterNotSpawned".Translate();
            }

            if (master.Downed)
            {
                return "IsIncapped".Translate(master.LabelShort, master);
            }

            if (master.Deathresting)
            {
                return "IsDeathresting".Translate(master.Named("PAWN"));
            }

            if (master.InMentalState)
            {
                return "InMentalState".Translate(master.Named("PAWN"), master.MentalStateDef.Named("MENTALSTATE"));
            }

            return true;
        }

        internal static bool InMasterCommandRange(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn == null || !target.IsValid)
            {
                return false;
            }

            AcceptanceReport report = CanDoDraftControl(pawn);
            if (!report.Accepted)
            {
                return false;
            }

            Pawn master = pawn.playerSettings.Master;
            Map map = pawn.MapHeld;
            if (master == null || map == null || master.MapHeld != map || !target.Cell.InBounds(map))
            {
                return false;
            }

            return target.Cell.InHorDistOf(master.Position, Pawn_TrainingTracker.AttackTargetRange);
        }

        internal static bool ShouldAllowDraftedProvider(FloatMenuOptionProvider provider)
        {
            return provider is FloatMenuOptionProvider_DraftedMove || provider is FloatMenuOptionProvider_DraftedAttack;
        }

        internal static void EnsureDrafter(Pawn pawn)
        {
            if (pawn != null && pawn.drafter == null)
            {
                pawn.drafter = new Pawn_DraftController(pawn);
            }
        }

        internal static bool ShouldUndraftForMasterState(Pawn pawn)
        {
            if (pawn?.drafter == null
                || !pawn.drafter.Drafted
                || pawn.Faction != Faction.OfPlayer
                || pawn.playerSettings == null)
            {
                return false;
            }

            if (pawn.RaceProps == null || !pawn.RaceProps.Animal)
            {
                return false;
            }

            Pawn master = pawn.playerSettings.Master;
            return master == null || master.Dead || master.Downed;
        }

        internal static void UndraftIfMasterUnavailable(Pawn pawn)
        {
            if (!ShouldUndraftForMasterState(pawn))
            {
                return;
            }

            pawn.drafter.Drafted = false;
        }

        internal static void UndraftMasteredAnimalsIfMasterDowned(Pawn master)
        {
            if (master == null || !master.Downed)
            {
                return;
            }

            foreach (Pawn pawn in PawnUtility.SpawnedMasteredPawns(master))
            {
                UndraftIfMasterUnavailable(pawn);
            }
        }

        internal static void UndraftMasteredAnimalsLeftOnMap(Pawn master, Map previousMap)
        {
            if (master == null
                || previousMap == null
                || master.Faction == null
                || master.RaceProps == null
                || !master.RaceProps.Humanlike)
            {
                return;
            }

            List<Pawn> pawns = previousMap.mapPawns?.SpawnedPawnsInFaction(master.Faction);
            if (pawns == null || pawns.Count == 0)
            {
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.playerSettings?.Master != master)
                {
                    continue;
                }

                if (pawn.MapHeld != previousMap)
                {
                    continue;
                }

                if (pawn.drafter == null || !pawn.drafter.Drafted)
                {
                    continue;
                }

                pawn.drafter.Drafted = false;
            }
        }

        internal static void ApplyDraftCommandRestrictions(Command_Toggle command, Pawn pawn, AcceptanceReport report)
        {
            if (command == null || pawn == null || command.Disabled)
            {
                return;
            }

            if (pawn.Downed)
            {
                command.Disable("IsIncapped".Translate(pawn.LabelShort, pawn));
                return;
            }

            if (pawn.Deathresting)
            {
                command.Disable("IsDeathresting".Translate(pawn.Named("PAWN")));
                return;
            }

            if (!report.Accepted)
            {
                command.Disable(report.Reason);
            }
        }

        internal static bool IsDraftToggleCommand(Command_Toggle command)
        {
            if (command == null)
            {
                return false;
            }

            return command.defaultDesc == DraftToggleDescText;
        }

        internal static Command_Toggle CreateDraftToggle(Pawn pawn)
        {
            EnsureDrafter(pawn);
            AcceptanceReport report = CanDoDraftControl(pawn);

            Command_Toggle command = new Command_Toggle
            {
                hotKey = KeyBindingDefOf.Command_ColonistDraft,
                isActive = () => pawn.drafter != null && pawn.drafter.Drafted,
                toggleAction = delegate
                {
                    EnsureDrafter(pawn);
                    if (pawn.drafter == null)
                    {
                        return;
                    }

                    if (pawn.drafter.Drafted)
                    {
                        pawn.drafter.Drafted = false;
                        return;
                    }

                    AcceptanceReport canDo = CanDoDraftControl(pawn);
                    if (!canDo.Accepted)
                    {
                        if (!canDo.Reason.NullOrEmpty())
                        {
                            Messages.Message(canDo.Reason, pawn, MessageTypeDefOf.RejectInput, historical: false);
                        }
                        return;
                    }

                    pawn.drafter.Drafted = true;
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.Drafting, KnowledgeAmount.SpecificInteraction);
                },
                defaultDesc = "CommandToggleDraftDesc".Translate(),
                icon = TexCommand.Draft,
                turnOnSound = SoundDefOf.DraftOn,
                turnOffSound = SoundDefOf.DraftOff,
                groupKeyIgnoreContent = 81729172,
                defaultLabel = (pawn.Drafted ? "CommandUndraftLabel" : "CommandDraftLabel").Translate()
            };

            ApplyDraftCommandRestrictions(command, pawn, report);
            command.tutorTag = pawn.Drafted ? "Undraft" : "Draft";
            return command;
        }

        internal static bool ShouldDrawMasterRadius(Pawn master)
        {
            if (master == null || !master.Spawned || !IsFeatureEnabledNow() || Find.Selector == null)
            {
                return false;
            }

            int frame = Time.frameCount;
            if (masterRingCacheFrame != frame)
            {
                masterRingCacheFrame = frame;
                masterRingCache.Clear();

                List<object> selectedObjects = Find.Selector.SelectedObjectsListForReading;
                if (selectedObjects != null && selectedObjects.Count > 0)
                {
                    for (int i = 0; i < selectedObjects.Count; i++)
                    {
                        Pawn selected = selectedObjects[i] as Pawn;
                        if (!IsDraftControlActivePawn(selected))
                        {
                            continue;
                        }

                        Pawn selectedMaster = selected.playerSettings?.Master;
                        if (selectedMaster != null && selectedMaster.Spawned && selectedMaster.Map == selected.Map)
                        {
                            masterRingCache.Add(selectedMaster);
                        }
                    }
                }
            }

            return masterRingCache.Contains(master);
        }

        internal static bool AnySelectedDraftControlPawn()
        {
            if (!IsFeatureEnabledNow() || Find.Selector == null)
            {
                return false;
            }

            int frame = Time.frameCount;
            if (anySelectedDraftControlFrame == frame)
            {
                return anySelectedDraftControl;
            }

            anySelectedDraftControlFrame = frame;
            anySelectedDraftControl = false;

            List<object> selectedObjects = Find.Selector.SelectedObjectsListForReading;
            if (selectedObjects == null || selectedObjects.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < selectedObjects.Count; i++)
            {
                if (IsDraftControlActivePawn(selectedObjects[i] as Pawn))
                {
                    anySelectedDraftControl = true;
                    break;
                }
            }

            return anySelectedDraftControl;
        }

        internal static void PushDraftedMoveContext(bool isMultiselect)
        {
            draftedMoveContextDepth++;
            draftedMoveContextIsMultiselect = isMultiselect;
        }

        internal static void PopDraftedMoveContext()
        {
            if (draftedMoveContextDepth > 0)
            {
                draftedMoveContextDepth--;
            }

            if (draftedMoveContextDepth == 0)
            {
                draftedMoveContextIsMultiselect = false;
            }
        }

        internal static bool IsDraftedMoveMultiselectContext()
        {
            return draftedMoveContextDepth > 0 && draftedMoveContextIsMultiselect;
        }

        internal static IntVec3 FindNearestMasterRangeGotoCell(Pawn pawn, IntVec3 desiredRoot, List<IntVec3> reservedCells = null)
        {
            if (!IsDraftControlActivePawn(pawn) || !pawn.Spawned || pawn.MapHeld == null)
            {
                return IntVec3.Invalid;
            }

            Map map = pawn.MapHeld;
            IntVec3 root = desiredRoot;
            if (!root.IsValid || !root.InBounds(map))
            {
                root = pawn.Position;
            }

            return RCellFinder.BestOrderedGotoDestNear(
                root,
                pawn,
                c => (reservedCells == null || !reservedCells.Contains(c))
                    && InMasterCommandRange(pawn, c));
        }

    }

    [HarmonyPatch]
    public static class Patch_PawnTrainingTracker_CanAssignToTrain_AnimalDraftControl
    {
        private static bool loggedCanAssignToTrainGuard;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Pawn_TrainingTracker),
                nameof(Pawn_TrainingTracker.CanAssignToTrain),
                new[] { typeof(TrainableDef), typeof(ThingDef), typeof(bool).MakeByRefType(), typeof(Pawn) });
        }

        private static bool Prefix(TrainableDef td, ThingDef pawnDef, Pawn pawn, ref bool visible, ref AcceptanceReport __result)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return true;
            }

            if (td == null || pawnDef?.race == null)
            {
                visible = false;
                __result = false;
                return false;
            }

            TrainabilityDef trainability = TrainableUtility.GetTrainability(pawn) ?? pawnDef.race.trainability;
            if (td.requiredTrainability != null && trainability == null)
            {
                visible = false;
                __result = false;
                if (!loggedCanAssignToTrainGuard)
                {
                    loggedCanAssignToTrainGuard = true;
                    Log.Warning("[Zoology] Guarded Pawn_TrainingTracker.CanAssignToTrain during PostLoadInit due to missing trainability data.");
                }
                return false;
            }

            return true;
        }

        private static Exception Finalizer(
            Exception __exception,
            TrainableDef td,
            ThingDef pawnDef,
            ref bool visible,
            ref AcceptanceReport __result)
        {
            if (__exception == null)
            {
                return null;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit && __exception is NullReferenceException)
            {
                visible = false;
                __result = false;
                if (!loggedCanAssignToTrainGuard)
                {
                    loggedCanAssignToTrainGuard = true;
                    Log.Warning("[Zoology] Suppressed Pawn_TrainingTracker.CanAssignToTrain nullref during PostLoadInit.");
                }
                return null;
            }

            return __exception;
        }

        private static void Postfix(TrainableDef td, ThingDef pawnDef, Pawn pawn, ref bool visible, ref AcceptanceReport __result)
        {
            TrainableDef draftControl = AnimalDraftControlUtility.DraftControlTrainable;
            if (td == null)
            {
                return;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                return;
            }

            if (pawn?.training != null && AnimalDraftControlUtility.IsLinkedTrainable(td))
            {
                AnimalDraftControlUtility.SyncLinkedTraining(pawn.training);
            }

            bool featureEnabled = AnimalDraftControlUtility.IsFeatureEnabledNow();
            bool hasDraftControlAccess = AnimalDraftControlUtility.HasDraftControlAccess(pawn, pawnDef, draftControl);

            if (td == draftControl)
            {
                if (!featureEnabled)
                {
                    visible = false;
                    __result = false;
                    return;
                }

                if (__result.Accepted || pawn == null)
                {
                    return;
                }

                if (!AnimalDraftControlUtility.HasSentienceCatalyst(pawn))
                {
                    return;
                }

                visible = true;
                __result = true;
                return;
            }

            if (td != TrainableDefOf.AttackTarget || !featureEnabled || !hasDraftControlAccess)
            {
                return;
            }

            if (!__result.Accepted && !visible)
            {
                return;
            }

            visible = false;
            __result = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.HasLearned))]
    public static class Patch_PawnTrainingTracker_HasLearned_AnimalDraftControl
    {
        private static void Postfix(Pawn_TrainingTracker __instance, TrainableDef td, Pawn ___pawn, ref bool __result)
        {
            if (!AnimalDraftControlUtility.IsLinkedTrainable(td))
            {
                return;
            }

            Pawn pawn = ___pawn ?? __instance?.pawn;
            if (!AnimalDraftControlUtility.ShouldLinkTrainables(pawn))
            {
                return;
            }

            AnimalDraftControlUtility.SyncLinkedTraining(__instance);
            __result = AnimalDraftControlUtility.GetLinkedLearned(__instance, td, __result);
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), "GetSteps")]
    public static class Patch_PawnTrainingTracker_GetSteps_AnimalDraftControl
    {
        private static void Postfix(Pawn_TrainingTracker __instance, TrainableDef td, ref int __result)
        {
            if (!AnimalDraftControlUtility.IsLinkedTrainable(td)
                || !AnimalDraftControlUtility.ShouldLinkTrainables(__instance?.pawn))
            {
                return;
            }

            AnimalDraftControlUtility.SyncLinkedTraining(__instance);
            __result = AnimalDraftControlUtility.GetLinkedSteps(__instance, td, __result);
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.CanBeTrained))]
    public static class Patch_PawnTrainingTracker_CanBeTrained_AnimalDraftControl
    {
        private static void Prefix(Pawn_TrainingTracker __instance, TrainableDef td)
        {
            if (AnimalDraftControlUtility.IsLinkedTrainable(td))
            {
                AnimalDraftControlUtility.SyncLinkedTraining(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.Train))]
    public static class Patch_PawnTrainingTracker_Train_AnimalDraftControl
    {
        private static void Postfix(Pawn_TrainingTracker __instance, TrainableDef td)
        {
            if (AnimalDraftControlUtility.IsLinkedTrainable(td))
            {
                AnimalDraftControlUtility.SyncLinkedTraining(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.SetWantedRecursive))]
    public static class Patch_PawnTrainingTracker_SetWantedRecursive_AnimalDraftControl
    {
        private static void Postfix(Pawn_TrainingTracker __instance, TrainableDef td)
        {
            if (AnimalDraftControlUtility.IsLinkedTrainable(td))
            {
                AnimalDraftControlUtility.SyncLinkedTraining(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.ExposeData))]
    public static class Patch_PawnTrainingTracker_ExposeData_AnimalDraftControl
    {
        private static void Postfix(Pawn_TrainingTracker __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            AnimalDraftControlUtility.SyncLinkedTraining(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.CanTakeOrder), MethodType.Getter)]
    public static class Patch_Pawn_CanTakeOrder_AnimalDraftControl
    {
        private static void Postfix(Pawn __instance, ref bool __result)
        {
            if (!AnimalDraftControlUtility.IsDraftControlCandidate(__instance))
            {
                return;
            }

            __result = __instance.Drafted && AnimalDraftControlUtility.CanDoDraftControl(__instance).Accepted;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_AnimalDraftControl
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            bool isCandidate = AnimalDraftControlUtility.IsDraftControlCandidate(__instance);
            AcceptanceReport controlReport = isCandidate ? AnimalDraftControlUtility.CanDoDraftControl(__instance) : false;
            bool hasDraftToggle = false;

            foreach (Gizmo gizmo in __result)
            {
                if (AnimalDraftControlUtility.ShouldHideAttackTargetCommands(__instance)
                    && AnimalDraftControlUtility.IsAttackTargetCommandGizmo(gizmo))
                {
                    continue;
                }

                if (isCandidate && gizmo is Command_Toggle toggle && AnimalDraftControlUtility.IsDraftToggleCommand(toggle))
                {
                    hasDraftToggle = true;
                    AnimalDraftControlUtility.ApplyDraftCommandRestrictions(toggle, __instance, controlReport);
                }

                yield return gizmo;
            }

            if (!isCandidate || hasDraftToggle)
            {
                yield break;
            }

            yield return AnimalDraftControlUtility.CreateDraftToggle(__instance);
        }
    }

    [HarmonyPatch]
    public static class Patch_PawnPlayerSettings_MasterSetter_AnimalDraftControl
    {
        private static readonly AccessTools.FieldRef<Pawn_PlayerSettings, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_PlayerSettings, Pawn>("pawn");

        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.Master));
        }

        private static void Postfix(Pawn_PlayerSettings __instance)
        {
            AnimalDraftControlUtility.UndraftIfMasterUnavailable(PawnRef(__instance));
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    public static class Patch_Pawn_ExposeData_AnimalDraftControl
    {
        private static void Postfix(Pawn __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            AnimalDraftControlUtility.UndraftIfMasterUnavailable(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Notify_Downed))]
    public static class Patch_Pawn_NotifyDowned_AnimalDraftControl
    {
        private static void Postfix(Pawn __instance)
        {
            AnimalDraftControlUtility.UndraftMasteredAnimalsIfMasterDowned(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class Patch_Pawn_DeSpawn_AnimalDraftControl
    {
        private static void Prefix(Pawn __instance, ref Map __state)
        {
            __state = __instance.Map;
        }

        private static void Postfix(Pawn __instance, Map __state)
        {
            AnimalDraftControlUtility.UndraftMasteredAnimalsLeftOnMap(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.GetGizmos))]
    public static class Patch_PawnTrainingTracker_GetGizmos_AnimalDraftControl
    {
        private static bool Prefix(Pawn_TrainingTracker __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!AnimalDraftControlUtility.ShouldHideAttackTargetCommands(__instance?.pawn))
            {
                return true;
            }

            __result = EmptyGizmos();
            return false;
        }

        private static IEnumerable<Gizmo> EmptyGizmos()
        {
            yield break;
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider), nameof(FloatMenuOptionProvider.SelectedPawnValid))]
    public static class Patch_FloatMenuOptionProvider_SelectedPawnValid_AnimalDraftControl
    {
        private static void Postfix(FloatMenuOptionProvider __instance, Pawn pawn, FloatMenuContext context, ref bool __result)
        {
            if (!AnimalDraftControlUtility.IsDraftControlActivePawn(pawn))
            {
                return;
            }

            if (!AnimalDraftControlUtility.ShouldAllowDraftedProvider(__instance))
            {
                __result = false;
                return;
            }

            if (!__result)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedMove), "GetSingleOption")]
    public static class Patch_DraftedMove_GetSingleOption_ContextFlag
    {
        private static void Prefix(FloatMenuContext context)
        {
            AnimalDraftControlUtility.PushDraftedMoveContext(context != null && context.IsMultiselect);
        }

        private static Exception Finalizer(Exception __exception)
        {
            AnimalDraftControlUtility.PopDraftedMoveContext();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedMove), nameof(FloatMenuOptionProvider_DraftedMove.PawnCanGoto))]
    public static class Patch_DraftedMove_PawnCanGoto_AnimalDraftControl
    {
        private static void Postfix(Pawn pawn, IntVec3 gotoLoc, ref AcceptanceReport __result)
        {
            if (!__result.Accepted || !AnimalDraftControlUtility.IsDraftControlActivePawn(pawn))
            {
                return;
            }

            if (AnimalDraftControlUtility.InMasterCommandRange(pawn, gotoLoc))
            {
                return;
            }

            if (!AnimalDraftControlUtility.IsDraftedMoveMultiselectContext())
            {
                __result = "CannotGoOutOfRange".Translate() + ": " + "OutOfCommandRange".Translate();
                return;
            }

            IntVec3 fallbackCell = AnimalDraftControlUtility.FindNearestMasterRangeGotoCell(pawn, gotoLoc);
            if (!fallbackCell.IsValid)
            {
                __result = "CannotGoOutOfRange".Translate() + ": " + "OutOfCommandRange".Translate();
            }
        }
    }

    [HarmonyPatch(typeof(MultiPawnGotoController), nameof(MultiPawnGotoController.RecomputeDestinations))]
    public static class Patch_MultiPawnGotoController_RecomputeDestinations_AnimalDraftControl
    {
        private static readonly AccessTools.FieldRef<MultiPawnGotoController, List<Pawn>> PawnsRef =
            AccessTools.FieldRefAccess<MultiPawnGotoController, List<Pawn>>("pawns");

        private static readonly AccessTools.FieldRef<MultiPawnGotoController, List<IntVec3>> DestsRef =
            AccessTools.FieldRefAccess<MultiPawnGotoController, List<IntVec3>>("dests");

        private static readonly AccessTools.FieldRef<MultiPawnGotoController, IntVec3> EndRef =
            AccessTools.FieldRefAccess<MultiPawnGotoController, IntVec3>("end");

        private static void Postfix(MultiPawnGotoController __instance)
        {
            if (__instance == null)
            {
                return;
            }

            List<Pawn> pawns = PawnsRef(__instance);
            List<IntVec3> dests = DestsRef(__instance);
            if (pawns == null || dests == null || pawns.Count == 0 || pawns.Count != dests.Count)
            {
                return;
            }

            IntVec3 desiredRoot = EndRef(__instance);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!AnimalDraftControlUtility.IsDraftControlActivePawn(pawn))
                {
                    continue;
                }

                IntVec3 destination = dests[i];
                if (destination.IsValid && AnimalDraftControlUtility.InMasterCommandRange(pawn, destination))
                {
                    continue;
                }

                IntVec3 fallbackCell = AnimalDraftControlUtility.FindNearestMasterRangeGotoCell(pawn, desiredRoot, dests);
                dests[i] = fallbackCell.IsValid ? fallbackCell : IntVec3.Invalid;
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedAttack), "CanTarget")]
    public static class Patch_DraftedAttack_CanTarget_NullGuard
    {
        private static bool Prefix(Thing clickedThing, ref bool __result)
        {
            if (clickedThing != null)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedAttack), nameof(FloatMenuOptionProvider_DraftedAttack.GetOptionsFor))]
    public static class Patch_DraftedAttack_GetOptionsFor_SkipNullMultiselectOption
    {
        private static readonly MethodInfo CanTargetMethod =
            AccessTools.Method(typeof(FloatMenuOptionProvider_DraftedAttack), "CanTarget");

        private static readonly MethodInfo GetMultiselectAttackOptionMethod =
            AccessTools.Method(typeof(FloatMenuOptionProvider_DraftedAttack), "GetMultiselectAttackOption");

        private static bool Prefix(FloatMenuOptionProvider_DraftedAttack __instance, Thing clickedThing, FloatMenuContext context, ref IEnumerable<FloatMenuOption> __result)
        {
            if (context == null || !context.IsMultiselect)
            {
                return true;
            }

            bool hasDraftControlPawn = false;
            foreach (Pawn pawn in context.ValidSelectedPawns)
            {
                if (AnimalDraftControlUtility.IsDraftControlActivePawn(pawn))
                {
                    hasDraftControlPawn = true;
                    break;
                }
            }

            if (!hasDraftControlPawn)
            {
                return true;
            }

            if (clickedThing == null || CanTargetMethod == null || GetMultiselectAttackOptionMethod == null)
            {
                __result = EmptyOptions();
                return false;
            }

            if (!(bool)CanTargetMethod.Invoke(null, new object[] { clickedThing }))
            {
                __result = EmptyOptions();
                return false;
            }

            FloatMenuOption option = GetMultiselectAttackOptionMethod.Invoke(__instance, new object[] { clickedThing, context }) as FloatMenuOption;
            __result = option == null ? EmptyOptions() : SingleOption(option);
            return false;
        }

        private static IEnumerable<FloatMenuOption> EmptyOptions()
        {
            yield break;
        }

        private static IEnumerable<FloatMenuOption> SingleOption(FloatMenuOption option)
        {
            if (option != null)
            {
                yield return option;
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.UseRangedAttack))]
    public static class Patch_FloatMenuUtility_UseRangedAttack_AnimalDraftControl
    {
        private static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (pawn == null)
            {
                __result = false;
                return false;
            }

            if (!AnimalDraftControlUtility.IsDraftControlActivePawn(pawn))
            {
                return true;
            }

            if (pawn.equipment == null)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetRangedAttackAction))]
    public static class Patch_FloatMenuUtility_GetRangedAttackAction_AnimalDraftControl
    {
        private static bool Prefix(Pawn pawn, LocalTargetInfo target, ref string failStr, ref Action __result)
        {
            if (pawn == null)
            {
                failStr = "Incapable".Translate();
                __result = null;
                return false;
            }

            if (!AnimalDraftControlUtility.IsDraftControlActivePawn(pawn))
            {
                return true;
            }

            failStr = string.Empty;
            __result = null;
            if (pawn.equipment?.Primary == null)
            {
                return false;
            }

            Verb primaryVerb = pawn.equipment.PrimaryEq?.PrimaryVerb;
            if (primaryVerb == null || primaryVerb.verbProps.IsMeleeAttack)
            {
                return false;
            }

            if (!pawn.Drafted)
            {
                failStr = "IsNotDraftedLower".Translate(pawn.LabelShort, pawn);
            }
            else if (target.IsValid && !AnimalDraftControlUtility.InMasterCommandRange(pawn, target))
            {
                failStr = "OutOfCommandRange".Translate();
            }
            else if (target.IsValid && !primaryVerb.CanHitTarget(target))
            {
                if (!pawn.Position.InHorDistOf(target.Cell, primaryVerb.EffectiveRange))
                {
                    failStr = "OutOfRange".Translate();
                }
                else
                {
                    float minRange = primaryVerb.verbProps.EffectiveMinRange(target, pawn);
                    failStr = (pawn.Position.DistanceToSquared(target.Cell) < minRange * minRange)
                        ? "TooClose".Translate()
                        : "CannotHitTarget".Translate();
                }
            }
            else if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                failStr = "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn);
            }
            else if (pawn == target.Thing)
            {
                failStr = "CannotAttackSelf".Translate();
            }
            else if (target.Thing is Pawn targetPawn
                && (pawn.InSameExtraFaction(targetPawn, ExtraFactionType.HomeFaction)
                    || pawn.InSameExtraFaction(targetPawn, ExtraFactionType.MiniFaction)))
            {
                failStr = "CannotAttackSameFactionMember".Translate();
            }
            else if (target.Thing is Pawn victim
                && HistoryEventUtility.IsKillingInnocentAnimal(pawn, victim)
                && !new HistoryEvent(HistoryEventDefOf.KilledInnocentAnimal, pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
            {
                failStr = "IdeoligionForbids".Translate();
            }
            else if (!(target.Thing is Pawn venerated)
                || pawn.Ideo == null
                || !pawn.Ideo.IsVeneratedAnimal(venerated)
                || new HistoryEvent(HistoryEventDefOf.HuntedVeneratedAnimal, pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
            {
                __result = delegate
                {
                    Job job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                };
                return false;
            }
            else
            {
                failStr = "IdeoligionForbids".Translate();
            }

            failStr = failStr.CapitalizeFirst();
            return false;
        }
    }

    [HarmonyPatch(typeof(FloatMenuUtility), nameof(FloatMenuUtility.GetMeleeAttackAction))]
    public static class Patch_FloatMenuUtility_GetMeleeAttackAction_AnimalDraftControl
    {
        private static bool Prefix(Pawn pawn, LocalTargetInfo target, bool ignoreControlled, ref string failStr, ref Action __result)
        {
            if (pawn == null)
            {
                failStr = "Incapable".Translate();
                __result = null;
                return false;
            }

            if (!AnimalDraftControlUtility.IsDraftControlActivePawn(pawn))
            {
                return true;
            }

            failStr = string.Empty;
            __result = null;
            if (!pawn.Drafted && !ignoreControlled)
            {
                failStr = "IsNotDraftedLower".Translate(pawn.LabelShort, pawn);
            }
            else if (target.IsValid && !AnimalDraftControlUtility.InMasterCommandRange(pawn, target))
            {
                failStr = "OutOfCommandRange".Translate();
            }
            else if (target.IsValid && !pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                failStr = "NoPath".Translate();
            }
            else if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                failStr = "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn);
            }
            else if (pawn.meleeVerbs.TryGetMeleeVerb(target.Thing) == null)
            {
                failStr = "Incapable".Translate();
            }
            else if (pawn == target.Thing)
            {
                failStr = "CannotAttackSelf".Translate();
            }
            else if (target.Thing is Pawn targetPawn
                && (pawn.InSameExtraFaction(targetPawn, ExtraFactionType.HomeFaction)
                    || pawn.InSameExtraFaction(targetPawn, ExtraFactionType.MiniFaction)))
            {
                failStr = "CannotAttackSameFactionMember".Translate();
            }
            else if (!(target.Thing is Pawn victim)
                || !victim.RaceProps.Animal
                || !HistoryEventUtility.IsKillingInnocentAnimal(pawn, victim)
                || new HistoryEvent(HistoryEventDefOf.KilledInnocentAnimal, pawn.Named(HistoryEventArgsNames.Doer)).DoerWillingToDo())
            {
                __result = delegate
                {
                    Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                    if (target.Thing is Pawn meleeVictim)
                    {
                        job.killIncappedTarget = meleeVictim.Downed;
                    }

                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                };
                return false;
            }
            else
            {
                failStr = "IdeoligionForbids".Translate();
            }

            failStr = failStr.CapitalizeFirst();
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawExtraSelectionOverlays))]
    public static class Patch_Pawn_DrawExtraSelectionOverlays_AnimalDraftControl
    {
        private static void Postfix(Pawn __instance)
        {
            if (__instance == null || !AnimalDraftControlUtility.IsFeatureEnabledNow())
            {
                return;
            }

            Map map = __instance.MapHeld;
            if (map == null)
            {
                return;
            }

            Pawn master = null;
            IntVec3 center = IntVec3.Invalid;

            if (AnimalDraftControlUtility.IsDraftControlActivePawn(__instance))
            {
                master = __instance.playerSettings?.Master;
                if (master == null || !master.Spawned || master.MapHeld != map)
                {
                    return;
                }

                center = master.Position;
            }
            else
            {
                if (__instance.Faction != Faction.OfPlayer
                    || __instance.playerSettings == null
                    || __instance.RaceProps == null
                    || !__instance.RaceProps.Humanlike
                    || !AnimalDraftControlUtility.ShouldDrawMasterRadius(__instance))
                {
                    return;
                }

                center = __instance.Position;
            }

            if (!center.IsValid)
            {
                return;
            }

            GenDraw.DrawRadiusRing(
                center,
                Pawn_TrainingTracker.AttackTargetRange,
                Color.white,
                c => c.InBounds(map));
        }
    }
}
