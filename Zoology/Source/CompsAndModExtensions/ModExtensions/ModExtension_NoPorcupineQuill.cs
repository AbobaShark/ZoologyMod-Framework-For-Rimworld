

using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZoologyMod
{
    
    public class ModExtension_NoPorcupineQuill : DefModExtension
    {
        
        
    }

    [StaticConstructorOnStartup]
    public static class NoPorcupineQuill_HarmonyPatches
    {
        
        private const int CleanupIntervalTicks = 6000;

        static NoPorcupineQuill_HarmonyPatches()
        {
            try
            {
                var harmony = new Harmony("com.abobashark.zoology.noporcupinequill");

                
                var addDirect = AccessTools.Method(typeof(HediffSet), "AddDirect");
                if (addDirect != null)
                {
                    var prefix = new HarmonyMethod(typeof(NoPorcupineQuill_HarmonyPatches), nameof(AddDirect_Prefix));
                    harmony.Patch(addDirect, prefix: prefix);
                }
                else
                {
                    Log.Warning("[Zoology.NoPorcupineQuill] HediffSet.AddDirect not found - can't patch add-blocking.");
                }

                
                
                var pawnTick = AccessTools.Method(typeof(Pawn), "Tick");
                if (pawnTick != null)
                {
                    var pawnTickPrefix = new HarmonyMethod(typeof(NoPorcupineQuill_HarmonyPatches), nameof(PawnTick_Prefix));
                    harmony.Patch(pawnTick, prefix: pawnTickPrefix);
                }
                else
                {
                    Log.Warning("[Zoology.NoPorcupineQuill] Pawn.Tick not found - can't patch periodic cleanup.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] patch init error: {e}");
            }
        }

        
        
        public static bool AddDirect_Prefix(HediffSet __instance, Hediff hediff)
        {
            try
            {
                if (hediff == null || __instance?.pawn == null) return true;

                var pawn = __instance.pawn;

                
                var ext = pawn.def?.GetModExtension<ModExtension_NoPorcupineQuill>();
                if (ext == null) return true; 

                
                var porcupineDef = DefDatabase<HediffDef>.GetNamedSilentFail("PorcupineQuill");
                if (porcupineDef == null)
                {
                    
                    return true;
                }

                
                if (hediff.def == porcupineDef)
                {
                    
                    if (Prefs.DevMode)
                        Log.Message($"[Zoology.NoPorcupineQuill] prevented adding {hediff.def.defName} to pawn {pawn.LabelShort} ({pawn.ThingID})");

                    return false; 
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] error in AddDirect_Prefix: {e}");
                
                return true;
            }
        }

        
        
        public static void PawnTick_Prefix(Pawn __instance)
        {
            try
            {
                var pawn = __instance;
                if (pawn == null) return;

                
                if (!pawn.IsHashIntervalTick(CleanupIntervalTicks)) return;

                
                var ext = pawn.def?.GetModExtension<ModExtension_NoPorcupineQuill>();
                if (ext == null) return;

                
                var porcupineDef = DefDatabase<HediffDef>.GetNamedSilentFail("PorcupineQuill");
                if (porcupineDef == null) return;

                var hediffSet = pawn.health?.hediffSet;
                if (hediffSet == null) return;

                var existing = hediffSet.GetFirstHediffOfDef(porcupineDef, false);
                if (existing != null)
                {
                    
                    pawn.health.RemoveHediff(existing);
                    if (Prefs.DevMode)
                        Log.Message($"[Zoology.NoPorcupineQuill] removed {porcupineDef.defName} from {pawn.LabelShort} ({pawn.ThingID})");
                }
            }
            catch (Exception e)
            {
                
                Log.Error($"[Zoology.NoPorcupineQuill] error in PawnTick_Prefix cleanup: {e}");
            }
        }
    }
}