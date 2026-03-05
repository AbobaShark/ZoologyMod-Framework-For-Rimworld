// ModExtension_AgroAtSlaughter.cs
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    /// <summary>
    /// ModExtension to mark PawnKinds/ThingDefs as "agro at slaughter".
    /// Add to PawnKindDef/ThingDef via &lt;modExtensions&gt; in XML.
    /// </summary>
    public class ModExtension_AgroAtSlaughter : DefModExtension
    {
        /// <summary>
        /// If true, show detailed messages in logs (use sparingly).
        /// </summary>
        public bool verboseLogging = false;

        /// <summary>
        /// If true, this pawn kind will be excluded from ritual animals by Patch_RitualRoleAnimal.
        /// (Default: true to preserve previous Comp behavior.)
        /// </summary>
        public bool excludeFromRituals = true;
    }

    /// <summary>
    /// Small helper for checking if a pawn is marked with ModExtension_AgroAtSlaughter.
    /// Checks PawnKindDef first, then Pawn.def as fallback.
    /// </summary>
    public static class AgroAtSlaughterUtil
    {
        public static bool IsAgroAtSlaughter(Pawn pawn, out ModExtension_AgroAtSlaughter ext)
        {
            ext = null;
            if (pawn == null) return false;

            // Preferred: PawnKindDef (for kinds defined in XML)
            var pk = pawn.kindDef;
            if (pk != null)
            {
                ext = pk.GetModExtension<ModExtension_AgroAtSlaughter>();
                if (ext != null) return true;
            }

            // Fallback: ThingDef (pawn.def)
            var td = pawn.def;
            if (td != null)
            {
                ext = td.GetModExtension<ModExtension_AgroAtSlaughter>();
                if (ext != null) return true;
            }

            return false;
        }

        public static bool IsAgroAtSlaughter(Pawn pawn)
        {
            return IsAgroAtSlaughter(pawn, out _);
        }
    }

    // --------------------------
    // Patch: DesignationManager.AddDesignation (postfix)
    // Show message when someone designates a non-downed pawn for slaughter,
    // if the target is marked with ModExtension_AgroAtSlaughter and setting enabled.
    // --------------------------
    [HarmonyPatch(typeof(DesignationManager), "AddDesignation")]
    public static class Patch_DesignationManager_AddDesignation_Agro
    {
        public static void Postfix(DesignationManager __instance, Designation newDes)
        {
            try
            {
                if (newDes?.def != DesignationDefOf.Slaughter) return;
                if (!(newDes.target.Thing is Pawn target)) return;
                if (target.Downed) return;

                var settings = ZoologyModSettings.Instance;
                if (settings == null || !settings.EnableAgroAtSlaughter) return;

                if (!AgroAtSlaughterUtil.IsAgroAtSlaughter(target, out var ext)) return;

                // Show message (same as original)
                Messages.Message(
                    "ZoologySlaughterDesignationAdded".Translate(target.LabelShort),
                    target,
                    MessageTypeDefOf.CautionInput
                );

                if (ext != null && ext.verboseLogging)
                    Log.Message($"[Zoology] Slaughter designation added on agro pawn {target.LabelShortCap}.");
            }
            catch (Exception ex)
            {
                // don't break the game
                Log.Error($"[Zoology] Patch_DesignationManager_AddDesignation_Agro.Postfix exception: {ex}");
            }
        }
    }

    // --------------------------
    // Patch: JobDriver_Slaughter.MakeNewToils (postfix)
    // Replace slaughter toils with one that triggers manhunter (if target is not downed and is marked)
    // --------------------------
    [HarmonyPatch(typeof(JobDriver_Slaughter), "MakeNewToils")]
    public static class Patch_JobDriver_Slaughter_MakeNewToils_Agro
    {
        public static void Postfix(JobDriver_Slaughter __instance, ref IEnumerable<Toil> __result)
        {
            try
            {
                var target = __instance?.pawn?.CurJob?.targetA.Thing as Pawn;
                if (target == null) return;
                if (target.Downed) return;

                var settings = ZoologyModSettings.Instance;
                if (settings == null || !settings.EnableAgroAtSlaughter) return;

                if (!AgroAtSlaughterUtil.IsAgroAtSlaughter(target, out var ext)) return;

                // Build replacement toils: approach + reserve, then trigger manhunter + messages + remove designation + stop jobs.
                var newToils = new List<Toil>
                {
                    Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch),
                    Toils_Reserve.Reserve(TargetIndex.A, 1, -1, null)
                };

                var trigger = new Toil();
                trigger.initAction = () =>
                {
                    try
                    {
                        // Try to start manhunter state on the target (safe call)
                        target.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);
                    }
                    catch
                    {
                        // swallow exceptions to avoid log spam
                    }

                    // Big threat message for the player
                    Messages.Message("ZoologySensesSlaughterIntent".Translate(target.LabelCap), MessageTypeDefOf.ThreatBig);

                    // Remove the Slaughter designation on the target if it still exists
                    try
                    {
                        if (target.Map != null)
                        {
                            var des = target.Map.designationManager.AllDesignationsOn(target)
                                        .FirstOrDefault(d => d.def == DesignationDefOf.Slaughter);
                            if (des != null) target.Map.designationManager.RemoveDesignation(des);
                        }
                    }
                    catch (Exception)
                    {
                        // ignore designation removal errors
                    }

                    try
                    {
                        __instance.pawn.jobs.StopAll();
                    }
                    catch { /* ignore */ }

                    if (ext != null && ext.verboseLogging)
                        Log.Message($"[Zoology] Slaughter toils replaced: pawn {__instance.pawn?.LabelShort} triggered agro on {target.LabelShortCap}");
                };
                trigger.defaultCompleteMode = ToilCompleteMode.Instant;
                newToils.Add(trigger);

                __result = newToils;
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_JobDriver_Slaughter_MakeNewToils_Agro.Postfix exception: {ex}");
            }
        }
    }

    // --------------------------
    // Patch: RitualRoleAnimal.AppliesToPawn (postfix)
    // Exclude agro animals from ritual selection (if configured)
    // --------------------------
    [HarmonyPatch(typeof(RitualRoleAnimal), "AppliesToPawn")]
    public static class Patch_RitualRoleAnimal_AppliesToPawn
    {
        public static void Postfix(RitualRoleAnimal __instance, Pawn p, ref bool __result, ref string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            try
            {
                if (!__result || p == null) return;

                if (!AgroAtSlaughterUtil.IsAgroAtSlaughter(p, out var ext)) return;
                if (ext == null) return;
                if (!ext.excludeFromRituals) return;

                // Exclude from rituals
                __result = false;
                if (!skipReason)
                    reason = "MessageRitualRoleMustBePeacefulAnimal".Translate(__instance.LabelCap);

                if (ext.verboseLogging)
                    Log.Message($"[Zoology] Excluding {p.LabelShortCap} from ritual roles due to AgroAtSlaughter extension.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Zoology] Patch_RitualRoleAnimal_AppliesToPawn postfix error: {ex}");
            }
        }
    }
}