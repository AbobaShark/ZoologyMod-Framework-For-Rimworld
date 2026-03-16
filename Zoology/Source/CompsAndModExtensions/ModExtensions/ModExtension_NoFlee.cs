using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class NoFleeSettingsGate
    {
        public static bool Enabled()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableNoFleeExtension;
        }
    }

    
    
    
    
    public class ModExtension_NoFlee : DefModExtension
    {
        
        
        
        public bool verboseLogging = false;
    }

    
    
    
    
    public static class NoFleeUtil
    {
        public static bool IsNoFlee(Pawn pawn)
        {
            return IsNoFlee(pawn, out _);
        }

        public static bool IsNoFlee(Pawn pawn, out ModExtension_NoFlee ext)
        {
            ext = null;
            if (pawn?.def == null || !ZoologyCacheUtility.HasNoFleeExtension(pawn.def))
            {
                return false;
            }

            return DefModExtensionCache<ModExtension_NoFlee>.TryGet(pawn, out ext);
        }
    }

    
    
    
    
    
    [HarmonyPatch(typeof(FleeUtility), "ShouldAnimalFleeDanger")]
    public static class Patch_ShouldAnimalFleeDanger_NoFlee
    {
        public static bool Prepare() => NoFleeSettingsGate.Enabled();

        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            try
            {
                if (pawn?.def == null || !ZoologyCacheUtility.HasNoFleeExtension(pawn.def))
                {
                    return true;
                }

                ModExtension_NoFlee ext = Prefs.DevMode ? DefModExtensionCache<ModExtension_NoFlee>.Get(pawn.def) : null;
                if (pawn != null)
                {
                    __result = false;
                    if (ext?.verboseLogging == true && Prefs.DevMode)
                        Log.Message($"[Zoology] ShouldAnimalFleeDanger -> false for {pawn.LabelShort} (NoFlee).");
                    return false; 
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

    
    
    
    
    [HarmonyPatch(typeof(MentalStateHandler), "TryStartMentalState")]
    public static class Patch_MentalStateHandler_TryStartMentalState_NoFlee
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(MentalStateHandler), "pawn");

        public static bool Prepare() => NoFleeSettingsGate.Enabled();

        
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

                
                if (stateDef == MentalStateDefOf.PanicFlee || stateDef == MentalStateDefOf.Terror)
                {
                    var pawn = PawnField?.GetValue(__instance) as Pawn;

                    if (pawn?.def != null
                        && ZoologyCacheUtility.HasNoFleeExtension(pawn.def)
                        && NoFleeUtil.IsNoFlee(pawn, out var ext))
                    {
                        if (Prefs.DevMode && ext?.verboseLogging == true)
                        {
                            Log.Message($"[Zoology] Blocked mental state '{stateDef.defName}' for pawn '{pawn.LabelShort}'.");
                        }
                        __result = false;
                        return false; 
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
