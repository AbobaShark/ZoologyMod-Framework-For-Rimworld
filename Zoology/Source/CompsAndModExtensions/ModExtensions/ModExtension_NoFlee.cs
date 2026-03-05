// ModExtension_NoFlee.cs

using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    /// <summary>
    /// Marker mod extension: if present on PawnKindDef or ThingDef, the pawn will not flee / enter panic/terror states.
    /// Add via &lt;modExtensions&gt; in XML.
    /// </summary>
    public class ModExtension_NoFlee : DefModExtension
    {
        /// <summary>
        /// If true, extra verbose logging will be emitted for debugging.
        /// </summary>
        public bool verboseLogging = false;
    }

    /// <summary>
    /// Utility helpers to check for the NoFlee extension.
    /// Prefers PawnKindDef (kindDef) then ThingDef (def).
    /// </summary>
    public static class NoFleeUtil
    {
        public static bool IsNoFlee(Pawn pawn)
        {
            return IsNoFlee(pawn, out _);
        }

        public static bool IsNoFlee(Pawn pawn, out ModExtension_NoFlee ext)
        {
            ext = null;
            if (pawn == null) return false;

            var pk = pawn.kindDef;
            if (pk != null)
            {
                ext = pk.GetModExtension<ModExtension_NoFlee>();
                if (ext != null) return true;
            }

            var td = pawn.def;
            if (td != null)
            {
                ext = td.GetModExtension<ModExtension_NoFlee>();
                if (ext != null) return true;
            }

            return false;
        }
    }

    // ---------------------------
    // Patch 1: JobGiver_AnimalFlee.TryGiveJob
    // If pawn has ModExtension_NoFlee -> do not give flee job.
    // ---------------------------
    [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    public static class Patch_JobGiver_AnimalFlee_TryGiveJob_NoFlee
    {
        public static bool Prefix(JobGiver_AnimalFlee __instance, Pawn pawn, ref Job __result)
        {
            try
            {
                if (pawn != null && NoFleeUtil.IsNoFlee(pawn, out var ext))
                {
                    __result = null;
                    if (ext?.verboseLogging == true && Prefs.DevMode)
                        Log.Message($"[Zoology] Blocked flee job for pawn {pawn.LabelShort} (NoFlee).");
                    return false; // block original -> no job
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_JobGiver_AnimalFlee_TryGiveJob_NoFlee threw: {ex}");
                return true;
            }
            return true;
        }
    }

    // ---------------------------
    // Patch 2: FleeUtility.ShouldAnimalFleeDanger
    // If pawn has ModExtension_NoFlee -> return false (should not flee)
    // ---------------------------
    [HarmonyPatch(typeof(FleeUtility), "ShouldAnimalFleeDanger")]
    public static class Patch_ShouldAnimalFleeDanger_NoFlee
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            try
            {
                if (pawn != null && NoFleeUtil.IsNoFlee(pawn, out var ext))
                {
                    __result = false;
                    if (ext?.verboseLogging == true && Prefs.DevMode)
                        Log.Message($"[Zoology] ShouldAnimalFleeDanger -> false for {pawn.LabelShort} (NoFlee).");
                    return false; // block original
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_ShouldAnimalFleeDanger_NoFlee threw: {ex}");
                return true;
            }
            return true;
        }
    }

    // ---------------------------
    // Patch 3: MentalStateHandler.TryStartMentalState
    // Prevent entering PanicFlee and Terror for pawns with ModExtension_NoFlee.
    // ---------------------------
    [HarmonyPatch(typeof(MentalStateHandler), "TryStartMentalState")]
    public static class Patch_MentalStateHandler_TryStartMentalState_NoFlee
    {
        // Keep a signature that Harmony can match; ensure parameters align.
        public static bool Prefix(
            MentalStateHandler __instance,
            MentalStateDef stateDef,
            string reason,
            bool forced,
            bool forceWake,
            bool causedByMood,
            Pawn otherPawn,
            bool transitionSilently,
            bool causedByDamage,
            bool causedByPsycast,
            ref bool __result)
        {
            try
            {
                if (stateDef == null) return true;

                // Only care about PanicFlee and Terror
                if (stateDef == MentalStateDefOf.PanicFlee || stateDef == MentalStateDefOf.Terror)
                {
                    // get private 'pawn' field from MentalStateHandler
                    var pawnObj = AccessTools.Field(typeof(MentalStateHandler), "pawn")?.GetValue(__instance);
                    var pawn = pawnObj as Pawn;

                    if (pawn != null && NoFleeUtil.IsNoFlee(pawn, out var ext))
                    {
                        if (Prefs.DevMode && ext?.verboseLogging == true)
                        {
                            Log.Message($"[Zoology] Blocked mental state '{stateDef.defName}' for pawn '{pawn.LabelShort}'.");
                        }
                        __result = false;
                        return false; // block original -> do not start mental state
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Zoology] Patch_MentalStateHandler_TryStartMentalState_NoFlee threw: {ex}");
                return true;
            }

            return true;
        }
    }
}