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
        static NoPorcupineQuill_HarmonyPatches()
        {
            try
            {
                var settings = ZoologyModSettings.Instance;
                if (settings != null && !settings.EnableNoPorcupineQuillPatch)
                {
                    return;
                }

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

                var spawnSetup = AccessTools.Method(typeof(Pawn), "SpawnSetup", new Type[] { typeof(Map), typeof(bool) });
                if (spawnSetup != null)
                {
                    var spawnSetupPostfix = new HarmonyMethod(typeof(NoPorcupineQuill_HarmonyPatches), nameof(PawnSpawnSetup_Postfix));
                    harmony.Patch(spawnSetup, postfix: spawnSetupPostfix);
                }
                else
                {
                    Log.Warning("[Zoology.NoPorcupineQuill] Pawn.SpawnSetup not found - can't patch one-shot cleanup.");
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] patch init error: {e}");
            }
        }

        private static bool IsNoPorcupineQuillEnabled()
        {
            var settings = ZoologyModSettings.Instance;
            return settings == null || settings.EnableNoPorcupineQuillPatch;
        }

        private static bool IsTargetPorcupineQuill(Hediff hediff)
        {
            if (hediff?.def == null) return false;
            return string.Equals(hediff.def.defName, "PorcupineQuill", StringComparison.Ordinal);
        }

        private static void TryRemoveExistingPorcupineQuill(Pawn pawn)
        {
            if (pawn == null) return;
            if (pawn.def?.GetModExtension<ModExtension_NoPorcupineQuill>() == null) return;

            var hediffSet = pawn.health?.hediffSet;
            if (hediffSet == null) return;

            var porcupineDef = DefDatabase<HediffDef>.GetNamedSilentFail("PorcupineQuill");
            if (porcupineDef == null) return;

            var existing = hediffSet.GetFirstHediffOfDef(porcupineDef, false);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
                if (Prefs.DevMode)
                    Log.Message($"[Zoology.NoPorcupineQuill] removed {porcupineDef.defName} from {pawn.LabelShort} ({pawn.ThingID}) on spawn.");
            }
        }

        public static bool AddDirect_Prefix(HediffSet __instance, Hediff hediff)
        {
            try
            {
                if (!IsNoPorcupineQuillEnabled())
                {
                    return true;
                }

                if (!IsTargetPorcupineQuill(hediff)) return true;
                if (__instance?.pawn == null) return true;

                var pawn = __instance.pawn;

                var ext = pawn.def?.GetModExtension<ModExtension_NoPorcupineQuill>();
                if (ext == null) return true; 

                if (Prefs.DevMode)
                    Log.Message($"[Zoology.NoPorcupineQuill] prevented adding {hediff.def.defName} to pawn {pawn.LabelShort} ({pawn.ThingID})");

                return false; 
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] error in AddDirect_Prefix: {e}");
                
                return true;
            }
        }

        public static void PawnSpawnSetup_Postfix(Pawn __instance)
        {
            try
            {
                if (!IsNoPorcupineQuillEnabled())
                {
                    return;
                }

                TryRemoveExistingPorcupineQuill(__instance);
            }
            catch (Exception e)
            {
                Log.Error($"[Zoology.NoPorcupineQuill] error in PawnSpawnSetup_Postfix cleanup: {e}");
            }
        }
    }
}
