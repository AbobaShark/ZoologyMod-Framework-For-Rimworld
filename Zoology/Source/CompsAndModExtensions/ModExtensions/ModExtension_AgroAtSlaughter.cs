using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class AgroAtSlaughterSettingsGate
    {
        public static bool Enabled()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableAgroAtSlaughter;
        }
    }

    
    
    
    
    public class ModExtension_AgroAtSlaughter : DefModExtension
    {
        
        
        
        public bool verboseLogging = false;

        
        
        
        
        public bool excludeFromRituals = true;
    }

    
    
    
    
    public static class AgroAtSlaughterUtil
    {
        public static bool IsAgroAtSlaughter(Pawn pawn, out ModExtension_AgroAtSlaughter ext)
        {
            ext = null;
            if (pawn?.def == null || !ZoologyCacheUtility.HasAgroAtSlaughterExtension(pawn.def))
            {
                return false;
            }

            return DefModExtensionCache<ModExtension_AgroAtSlaughter>.TryGet(pawn, out ext);
        }

        public static bool IsAgroAtSlaughter(Pawn pawn)
        {
            return IsAgroAtSlaughter(pawn, out _);
        }
    }

    
    
    
    
    
    [HarmonyPatch(typeof(DesignationManager), "AddDesignation")]
    public static class Patch_DesignationManager_AddDesignation_Agro
    {
        public static bool Prepare() => AgroAtSlaughterSettingsGate.Enabled();

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
                
                Log.Error($"[Zoology] Patch_DesignationManager_AddDesignation_Agro.Postfix exception: {ex}");
            }
        }
    }

    
    
    
    
    [HarmonyPatch(typeof(JobDriver_Slaughter), "MakeNewToils")]
    public static class Patch_JobDriver_Slaughter_MakeNewToils_Agro
    {
        public static bool Prepare() => AgroAtSlaughterSettingsGate.Enabled();

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
                        
                        target.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);
                    }
                    catch
                    {
                        
                    }

                    
                    Messages.Message("ZoologySensesSlaughterIntent".Translate(target.LabelCap), MessageTypeDefOf.ThreatBig);

                    
                    try
                    {
                        if (target.Map != null)
                        {
                            Designation des = null;
                            var designations = target.Map.designationManager.AllDesignationsOn(target);
                            for (int i = 0; i < designations.Count; i++)
                            {
                                Designation candidate = designations[i];
                                if (candidate != null && candidate.def == DesignationDefOf.Slaughter)
                                {
                                    des = candidate;
                                    break;
                                }
                            }
                            if (des != null) target.Map.designationManager.RemoveDesignation(des);
                        }
                    }
                    catch (Exception)
                    {
                        
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

    
    
    
    
    [HarmonyPatch(typeof(RitualRoleAnimal), "AppliesToPawn")]
    public static class Patch_RitualRoleAnimal_AppliesToPawn
    {
        public static bool Prepare() => AgroAtSlaughterSettingsGate.Enabled();

        public static void Postfix(RitualRoleAnimal __instance, Pawn p, ref bool __result, ref string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            try
            {
                if (!__result || p == null) return;

                if (!AgroAtSlaughterUtil.IsAgroAtSlaughter(p, out var ext)) return;
                if (ext == null) return;
                if (!ext.excludeFromRituals) return;

                
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
