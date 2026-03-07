using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZoologyMod
{
    internal static class CannotBeMutatedSettingsGate
    {
        public static bool Enabled()
        {
            var s = ZoologyModSettings.Instance;
            return s == null || s.EnableCannotBeMutatedProtection;
        }
    }

    
    public class ModExtension_CannotBeMutated : DefModExtension
    {
        
    }

    
    public static class CannotBeMutatedUtil
    {
        
        
        public static bool IsCannotBeMutated(this Pawn pawn)
        {
            if (pawn == null) return false;

            
            if (pawn.def?.GetModExtension<ModExtension_CannotBeMutated>() != null) return true;

            
            if (pawn.kindDef?.GetModExtension<ModExtension_CannotBeMutated>() != null) return true;

            return false;
        }
    }

    
    [HarmonyPatch(typeof(CompTargetable_AllAnimalsOnTheMap), "ValidateTarget")]
    public static class Patch_CompTargetable_AllAnimalsOnTheMap_ValidateTarget_CannotBeMutated
    {
        public static bool Prepare() => CannotBeMutatedSettingsGate.Enabled();

        public static void Postfix(CompTargetable_AllAnimalsOnTheMap __instance, LocalTargetInfo target, bool showMessages, ref bool __result)
        {
            if (!__result) return;

            Pawn pawn = target.Thing as Pawn;
            if (pawn != null && pawn.IsCannotBeMutated())
            {
                __result = false;
                
            }
        }
    }

    
    [HarmonyPatch(typeof(Verb_CastTargetEffectBiomutationLance), "ValidateTarget")]
    public static class Patch_Verb_CastTargetEffectBiomutationLance_ValidateTarget_CannotBeMutated
    {
        public static bool Prepare() => CannotBeMutatedSettingsGate.Enabled();

        public static bool Prefix(Verb_CastTargetEffectBiomutationLance __instance, LocalTargetInfo target, bool showMessages, ref bool __result)
        {
            Pawn pawn = target.Pawn;
            if (pawn != null && pawn.IsCannotBeMutated())
            {
                __result = false;
                if (showMessages)
                {
                    Messages.Message("MessageBiomutationLanceInvalidTargetRace".Translate(pawn), __instance.caster, MessageTypeDefOf.RejectInput, null);
                }
                return false; 
            }
            return true;
        }
    }

    
    [HarmonyPatch(typeof(FleshbeastUtility), "SpawnFleshbeastFromPawn")]
    public static class Patch_FleshbeastUtility_SpawnFleshbeastFromPawn_CannotBeMutated
    {
        public static bool Prepare() => CannotBeMutatedSettingsGate.Enabled();

        
        
        public static bool Prefix(Pawn pawn)
        {
            if (pawn != null && pawn.IsCannotBeMutated())
            {
                return false;  
            }
            return true;
        }
    }

    
    [HarmonyPatch(typeof(CompObelisk_Mutator), "TryMutatingRandomAnimal")]
    public static class Patch_CompObelisk_Mutator_TryMutatingRandomAnimal_CannotBeMutated
    {
        public static bool Prepare() => CannotBeMutatedSettingsGate.Enabled();

        
        public static bool Prefix(CompObelisk_Mutator __instance, ref bool __result, ref Pawn mutatedAnimal, ref Pawn resultBeast)
        {
            mutatedAnimal = null;
            resultBeast = null;

            if (__instance?.parent?.Map == null)
            {
                __result = false;
                return false; 
            }

            
            IEnumerable<Pawn> candidates = from pawn in __instance.parent.Map.mapPawns.AllPawnsSpawned
                                           where pawn.Faction == null &&
                                                 pawn.IsAnimal &&
                                                 !pawn.IsCannotBeMutated() && 
                                                 !pawn.Position.Fogged(__instance.parent.Map)
                                           select pawn;

            Pawn pawn3;
            if (candidates.TryRandomElement(out pawn3))
            {
                mutatedAnimal = pawn3;
                resultBeast = FleshbeastUtility.SpawnFleshbeastFromPawn(pawn3, false, false, Array.Empty<PawnKindDef>());
                if (resultBeast != null)
                {
                    EffecterDefOf.ObeliskSpark.Spawn(__instance.parent.Position, __instance.parent.Map, 1f).Cleanup();
                    __result = true;
                    return false;  
                }
            }

            __result = false;
            return false;
        }
    }

    
    [HarmonyPatch(typeof(CompAbilityEffect_PsychicSlaughter), "Valid")]
    public static class Patch_CompAbilityEffect_PsychicSlaughter_Valid_CannotBeMutated
    {
        public static bool Prepare() => CannotBeMutatedSettingsGate.Enabled();

        public static void Postfix(CompAbilityEffect_PsychicSlaughter __instance, LocalTargetInfo target, bool throwMessages, ref bool __result)
        {
            try
            {
                if (!__result) return; 

                Pawn pawn = target.Pawn;
                if (pawn == null) return;

                
                if (pawn.IsCannotBeMutated())
                {
                    
                    if (throwMessages)
                    {
                        const string key = "PhotonozoaCannotBeTargetedByPsychicSlaughter";
                        string text = key.Translate(pawn.Named("PAWN"));
                        if (text == key) 
                        {
                            text = $"Cannot target {pawn.LabelShort} with Psychic Slaughter.";
                        }
                        Messages.Message(text, pawn, MessageTypeDefOf.RejectInput);
                    }

                    __result = false;
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology] Patch_CompAbilityEffect_PsychicSlaughter_Valid_CannotBeMutated error: {e}");
            }
        }
    }
}
